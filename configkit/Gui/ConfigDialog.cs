// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.

using ConfigKit.Formatting;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace ConfigKit.Gui;

/// <summary>
/// The settings window, drawn with the game's own Cairo GUI.
///
/// One mod's settings are shown at a time, picked from a dropdown, because a pack can
/// easily carry twenty configurable mods and tabs across the top run out of room long
/// before that does.
///
/// Rows live inside a clipped region whose parent bounds we move to scroll:
/// ElementBounds.CalcWorldBounds cascades to children, so shifting the one content
/// bounds moves every row in it.
/// </summary>
public class ConfigDialog : GuiDialog
{
    private const double DialogWidth = 700;
    private const double MaxContentHeight = 420;
    private const double MinContentHeight = 90;
    private const double RowHeight = 32;
    private const double RowGap = 6;
    private const double LabelWidth = 330;
    private const double ControlWidth = 250;
    private const double ValueWidth = 54;
    private const double ResetWidth = 56;
    private const double ResetGap = 14;

    private readonly Dictionary<string, Config> _configs;
    private readonly List<string> _domains;
    private string _domain;

    /// Rows are rebuilt whenever the mod selection changes, so the widget keys have to be
    /// unique per compose rather than per setting.
    private readonly Dictionary<string, ConfigSetting> _settingsByKey = new();

    /// The control built for each setting, kept because container children are not
    /// reachable through the composer by key.
    private readonly Dictionary<string, GuiElement> _widgets = new();

    /// The readout beside each slider. Kept separately from _widgets because it is not the
    /// control for the setting, it only reports it.
    private readonly Dictionary<string, GuiElementDynamicText> _sliderValues = new();

    private ElementBounds? _contentBounds;
    private double _contentHeight;

    public ConfigDialog(ICoreClientAPI capi, Dictionary<string, Config> configs) : base(capi)
    {
        _configs = configs;
        _domains = configs.Keys.OrderBy(domain => DisplayName(domain), StringComparer.OrdinalIgnoreCase).ToList();
        _domain = _domains.FirstOrDefault() ?? "";
    }

    public override string ToggleKeyCombinationCode => "configkitconfigs";

    /// <summary>The configs this window is showing, keyed by mod domain.</summary>
    public IReadOnlyDictionary<string, Config> Configs => _configs;

    /// <summary>
    /// The settings currently laid out, keyed by widget. Empty until the window has been
    /// composed, and rebuilt whenever the selected mod changes.
    /// </summary>
    public IReadOnlyDictionary<string, ConfigSetting> RenderedSettings => _settingsByKey;

    /// <summary>
    /// What each slider's readout currently says, keyed by the setting's yaml code. The
    /// number a player reads is not the slider's own value (floats are carried at 100x), so
    /// this is worth asserting on directly.
    /// </summary>
    public IReadOnlyDictionary<string, string> SliderValueTexts
        => _sliderValues
            .Where(entry => _settingsByKey.ContainsKey(entry.Key))
            .ToDictionary(entry => _settingsByKey[entry.Key].YamlCode, entry => entry.Value.GetText());

    /// <summary>
    /// Restores one rendered setting to its default, exactly as that row's Reset button
    /// does. Returns false if the window is not showing a setting with that code.
    /// </summary>
    public bool ResetSetting(string yamlCode)
    {
        foreach ((string key, ConfigSetting setting) in _settingsByKey)
        {
            if (setting.YamlCode == yamlCode) return OnResetSetting(setting, key);
        }

        return false;
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        Compose();
    }

    private string DisplayName(string domain)
    {
        Config config = _configs[domain];
        return string.IsNullOrWhiteSpace(config.ModName) ? domain : config.ModName;
    }

    // ------------------------------------------------------------------ composition

    private void Compose()
    {
        if (_domains.Count == 0)
        {
            ComposeEmpty();
            return;
        }

        _settingsByKey.Clear();

        // Lay the rows out once without a composer to learn how tall they are, so a mod
        // with three settings gets a short panel instead of half a screen of empty wood.
        _contentHeight = LayoutRows(null, _configs[_domain]);
        double visibleHeight = Math.Clamp(_contentHeight, MinContentHeight, MaxContentHeight);
        bool needsScrollbar = _contentHeight > visibleHeight + 0.5;

        ElementBounds dropdownBounds = ElementBounds.Fixed(0, 28, 360, 28);
        ElementBounds clipBounds = ElementBounds.Fixed(0, 70, DialogWidth, visibleHeight);
        ElementBounds insetBounds = clipBounds.ForkBoundingParent(3, 3, 3, 3);
        ElementBounds scrollbarBounds = ElementBounds.Fixed(DialogWidth + 10, 70, 20, visibleHeight);

        // The bounds every row hangs off. Scrolling moves this one element.
        _contentBounds = ElementBounds.Fixed(0, 0, DialogWidth - 20, 10);

        double buttonY = visibleHeight + 86;
        ElementBounds saveBounds = ElementBounds.Fixed(0, buttonY, 90, 26);
        ElementBounds reloadBounds = ElementBounds.Fixed(100, buttonY, 90, 26);
        ElementBounds defaultsBounds = ElementBounds.Fixed(200, buttonY, 150, 26);

        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        GuiComposer composer = capi.Gui
            .CreateCompo("configkit-settings", ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle))
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar("Mod settings", OnClose)
            .BeginChildElements(bgBounds)
                .AddDropDown(
                    _domains.ToArray(),
                    _domains.Select(DisplayName).ToArray(),
                    Math.Max(0, _domains.IndexOf(_domain)),
                    OnDomainSelected,
                    dropdownBounds,
                    "domain")
                .AddInset(insetBounds, 3)
                .BeginClip(clipBounds);

        // A ClippedContainer rather than AddContainer: the stock one lets rows scrolled out
        // of view draw and be clicked. See ClippedContainer for what that looked like.
        composer.AddInteractiveElement(new ClippedContainer(capi, _contentBounds), "rows");

        // Rows are built as elements and handed to the container, which draws them itself
        // inside the clip. Adding them straight to the composer bakes their frames and text
        // into the dialog's own surface, where a render-time clip never reaches them: with
        // more settings than fit, labels and empty switch frames drew over the buttons and
        // the hotbar while only the parts drawn per frame were correctly hidden.
        LayoutRows(composer.GetContainer("rows"), _configs[_domain]);
        _contentBounds.fixedHeight = _contentHeight;

        composer
                .EndClip();

        // A scrollbar over content that already fits tells the player there is more to see.
        if (needsScrollbar) composer.AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar");

        composer
            .AddSmallButton("Save", OnSave, saveBounds, EnumButtonStyle.Normal)
            .AddSmallButton("Reload", OnReload, reloadBounds, EnumButtonStyle.Normal)
            .AddSmallButton("Restore defaults", OnDefaults, defaultsBounds, EnumButtonStyle.Normal)
            .EndChildElements();

        SingleComposer = composer.Compose();

        if (needsScrollbar)
        {
            SingleComposer.GetScrollbar("scrollbar")
                .SetHeights((float)visibleHeight, (float)_contentHeight);
        }
    }

    private void ComposeEmpty()
    {
        ElementBounds textBounds = ElementBounds.Fixed(0, 30, 420, 60);
        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        SingleComposer = capi.Gui
            .CreateCompo("configkit-settings", ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle))
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar("Mod settings", OnClose)
            .BeginChildElements(bgBounds)
                .AddStaticText(
                    "No mods here have settings ConfigKit can edit.",
                    CairoFont.WhiteDetailText(), textBounds)
            .EndChildElements()
            .Compose();
    }

    /// <summary>
    /// Walks the config's blocks in order, building one row per visible setting.
    /// A null container measures without building, so the panel can be sized to its
    /// content before anything is added: one method, so the two passes cannot drift.
    /// </summary>
    private double LayoutRows(GuiElementContainer? container, Config config)
    {
        double y = 4;
        int index = 0;

        if (container != null) { _widgets.Clear(); _sliderValues.Clear(); }

        foreach ((float _, IConfigBlock block) in config.ConfigBlocks)
        {
            if (block is IFormattingBlock formatting)
            {
                y = AddFormattingBlock(container, formatting, y);
                continue;
            }

            if (block is not ConfigSetting setting || setting.Hide) continue;

            string key = $"setting-{index++}";
            if (container == null)
            {
                y += RowHeight + RowGap;
                continue;
            }

            bool locked = IsServerControlled(setting);
            if (!locked) _settingsByKey[key] = setting;

            ElementBounds labelBounds = ElementBounds.Fixed(0, y + 4, LabelWidth, RowHeight);
            ElementBounds controlBounds = ElementBounds.Fixed(LabelWidth + 16, y, ControlWidth, RowHeight - 4);

            container.Add(new GuiElementDynamicText(capi,
                locked ? LabelFor(setting) + " (server)" : LabelFor(setting),
                locked ? CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment) : CairoFont.WhiteDetailText(),
                labelBounds));

            if (!string.IsNullOrEmpty(setting.Comment))
            {
                container.Add(new GuiElementHoverText(capi, setting.Comment,
                    CairoFont.WhiteDetailText(), 320, labelBounds.FlatCopy()));
            }

            if (locked)
            {
                container.Add(new GuiElementDynamicText(capi, ValueText(setting),
                    CairoFont.WhiteDetailText(), controlBounds));
            }
            else
            {
                AddControl(container, setting, controlBounds, key);
                AddResetButton(container, setting, y, key);
            }

            y += RowHeight + RowGap;
        }

        return y;
    }

    private double AddFormattingBlock(GuiElementContainer? container, IFormattingBlock block, double y)
    {
        if (block.Title != null)
        {
            container?.Add(new GuiElementDynamicText(capi,
                block.Title, CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold),
                ElementBounds.Fixed(0, y + 10, DialogWidth - 40, 26)));
            y += 40;
        }

        if (block.Text != null)
        {
            container?.Add(new GuiElementDynamicText(capi,
                block.Text, CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(0, y, DialogWidth - 40, 24)));
            y += 28;
        }

        return y;
    }

    /// <summary>
    /// A server-side setting on a multiplayer client belongs to the server. Only a player
    /// with controlserver may change one, and the change is pushed over the network.
    /// </summary>
    private bool IsServerControlled(ConfigSetting setting)
    {
        if (setting.ClientSide) return false;
        if (capi.IsSinglePlayer) return false;

        // Only a server that actually runs ConfigKit owns these. Joining one that does not,
        // a client keeps managing its own configs locally - so locking them would leave the
        // player unable to edit settings nobody else is managing.
        if (!capi.ModLoader.GetModSystem<ConfigKitModSystem>().ConfigsReceivedFromServer) return false;

        return capi.World?.Player?.HasPrivilege(Privilege.controlserver) != true;
    }

    private static string ValueText(ConfigSetting setting)
        => setting.MappingKey ?? setting.Value.Token?.ToString() ?? "";

    private static string NumberText(ConfigSetting setting)
        => setting.SettingType == ConfigSettingType.Float
            ? setting.Value.AsFloat().ToString(System.Globalization.CultureInfo.InvariantCulture)
            : setting.Value.AsInt().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string LabelFor(ConfigSetting setting)
    {
        string? label = setting.InGui;

        // A managed (POCO) config labels each setting "<domain>:setting-<FieldName>", which
        // Lang returns unchanged when the mod ships no translation for it. Showing a player
        // "mymod:setting-MaxRadius" is worse than showing "Max radius".
        if (string.IsNullOrWhiteSpace(label) || IsUntranslatedLangKey(label!))
        {
            return Humanize(setting.YamlCode);
        }

        return label!;
    }

    private static bool IsUntranslatedLangKey(string label)
        => label.Contains(':') && !label.Contains(' ');

    /// <summary>"MaxClientViewDistance" -> "Max client view distance".</summary>
    private static string Humanize(string code)
    {
        string spaced = Regex.Replace(code.Replace('_', ' '), "(?<=[a-z0-9])(?=[A-Z])", " ");
        spaced = spaced.Trim();
        if (spaced.Length == 0) return code;

        return char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
    }

    // ------------------------------------------------------------------ controls

    private void AddControl(GuiElementContainer container, ConfigSetting setting, ElementBounds bounds, string key)
    {
        Validation? validation = setting.Validation;

        if (validation?.Mapping != null)
        {
            string[] keys = validation.Mapping.Keys.ToArray();
            int selected = Math.Max(0, Array.IndexOf(keys, setting.MappingKey ?? ""));
            Remember(key, container, new GuiElementDropDown(capi, keys, keys, selected,
                (code, on) => { if (on) setting.MappingKey = code; },
                bounds, CairoFont.WhiteDetailText(), false));
            return;
        }

        if (validation?.Values != null)
        {
            // Render the raw token, not AsString: JsonObject.AsString returns the default
            // for anything that is not literally a string, so a list of numbers came out as
            // blank rows and wrote an empty string back into the setting.
            string[] values = validation.Values.Select(TokenText).ToArray();
            int selected = Math.Max(0, Array.IndexOf(values, TokenText(setting.Value)));
            Remember(key, container, new GuiElementDropDown(capi, values, values, selected,
                (code, on) => { if (on) SetFromText(setting, code); },
                bounds, CairoFont.WhiteDetailText(), false));
            return;
        }

        switch (setting.SettingType)
        {
            case ConfigSettingType.Boolean:
                Remember(key, container, new GuiElementSwitch(capi, on => setting.Value = FromBool(on), bounds));
                break;

            case ConfigSettingType.Integer when HasRange(validation):
            case ConfigSettingType.Float when HasRange(validation):
                AddSliderControl(container, setting, bounds, key);
                break;

            case ConfigSettingType.Integer:
            case ConfigSettingType.Float:
                Remember(key, container, new GuiElementNumberInput(capi, bounds,
                    text => OnNumberTyped(setting, text), CairoFont.WhiteDetailText()));
                break;

            case ConfigSettingType.Other:
                Remember(key, container, new GuiElementTextArea(capi, bounds.WithFixedHeight(RowHeight * 2),
                    text => OnJsonTyped(setting, text), CairoFont.WhiteDetailText()));
                break;

            case ConfigSettingType.Color:
                AddColorControl(container, setting, bounds, key);
                break;

            case ConfigSettingType.String:
            default:
                Remember(key, container, new GuiElementTextInput(capi, bounds,
                    text => setting.Value = FromString(text), CairoFont.WhiteDetailText()));
                break;
        }
    }

    private void Remember(string key, GuiElementContainer container, GuiElement element)
    {
        _widgets[key] = element;
        container.Add(element);
    }

    /// <summary>
    /// One setting's own "restore this to its default" button, beside its control.
    ///
    /// "Restore defaults" at the bottom of the window is all-or-nothing, which is no help
    /// to a player who has changed six settings and wants one of them back. This resets
    /// only its own row, and only in memory: like every other edit here, it takes Save to
    /// reach the file.
    /// </summary>
    private void AddResetButton(GuiElementContainer container, ConfigSetting setting, double y, string key)
    {
        // Placed from the column constants rather than from the control's own bounds: a
        // switch resizes the bounds it is given down to its own square, which would drag
        // the button on a boolean row in to sit against the toggle while every other row's
        // button stayed out at the right.
        ElementBounds bounds = ElementBounds.Fixed(
            LabelWidth + 16 + ControlWidth + ResetGap, y, ResetWidth, RowHeight - 4);

        container.Add(new GuiElementTextButton(capi, "Reset",
            CairoFont.WhiteDetailText(),
            CairoFont.WhiteDetailText().WithColor(GuiStyle.ActiveButtonTextColor),
            () => OnResetSetting(setting, key),
            bounds, EnumButtonStyle.Small));

        container.Add(new GuiElementHoverText(capi,
            $"Restore this setting to {DefaultText(setting)}",
            CairoFont.WhiteDetailText(), 260, bounds.FlatCopy()));
    }

    private bool OnResetSetting(ConfigSetting setting, string key)
    {
        RestoreDefault(setting);
        SyncWidget(key, setting);
        return true;
    }

    /// <summary>
    /// A mapped setting stores its default as the mapping *key* ("medium"), not the value
    /// that key resolves to, so it has to go back through MappingKey - whose setter assigns
    /// the mapped value as well. Anything else takes the default value directly.
    /// </summary>
    private static void RestoreDefault(ConfigSetting setting)
    {
        if (setting.Validation?.Mapping != null)
        {
            string key = setting.DefaultValue.AsString("");
            if (setting.Validation.Mapping.ContainsKey(key))
            {
                setting.MappingKey = key;
                return;
            }
            // The stored default is not one of the keys - a definition that changed under a
            // saved config, or a default written as the mapped value. Setting it directly is
            // still better than leaving the player's edit in place.
        }

        setting.Value = setting.DefaultValue.Clone();
    }

    private static string DefaultText(ConfigSetting setting)
    {
        string text = setting.DefaultValue.Token?.ToString() ?? "";
        return string.IsNullOrWhiteSpace(text) ? "its default" : $"its default ({text})";
    }

    /// <summary>
    /// A slider with its current value beside it.
    ///
    /// Vanilla's own ShowTextWhenResting draws the number inside the track, where it rides
    /// along under the handle and is scissored to the bar. It also shows the slider's own
    /// integer: a float setting is carried at 100x (see FloatScale), so a value of 2.5 would
    /// read 250. A separate readout avoids both, and reuses NumberText so the number matches
    /// what the config file gets.
    /// </summary>
    private void AddSliderControl(GuiElementContainer container, ConfigSetting setting, ElementBounds bounds, string key)
    {
        ElementBounds sliderBounds = bounds.FlatCopy().WithFixedWidth(bounds.fixedWidth - ValueWidth - 8);
        ElementBounds valueBounds = ElementBounds.Fixed(
            bounds.fixedX + bounds.fixedWidth - ValueWidth, bounds.fixedY + 2, ValueWidth, bounds.fixedHeight);

        GuiElementDynamicText readout = new(capi, NumberText(setting),
            CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Right), valueBounds);

        Remember(key, container, new GuiElementSlider(capi, value =>
        {
            bool handled = OnSlider(setting, value);
            readout.SetNewText(NumberText(setting));
            return handled;
        }, sliderBounds));

        _sliderValues[key] = readout;
        container.Add(readout);
    }

    /// <summary>
    /// Colours are "#rrggbb" strings. Vanilla offers a fixed-palette picker but no
    /// free-form one, so this is a hex field with a live swatch beside it: exact values
    /// stay typeable and pasteable, and a mistyped one is visible rather than silent.
    /// </summary>
    private void AddColorControl(GuiElementContainer container, ConfigSetting setting, ElementBounds bounds, string key)
    {
        double swatchSize = bounds.fixedHeight;
        ElementBounds fieldBounds = bounds.FlatCopy().WithFixedWidth(bounds.fixedWidth - swatchSize - 8);
        ElementBounds swatchBounds = ElementBounds.Fixed(
            bounds.fixedX + bounds.fixedWidth - swatchSize, bounds.fixedY, swatchSize, swatchSize);

        GuiElementCustomDraw swatch = new(capi, swatchBounds,
            (ctx, surface, currentBounds) => DrawSwatch(ctx, currentBounds, setting.Value.AsString("#000000")));

        Remember(key, container, new GuiElementTextInput(capi, fieldBounds,
            text => { setting.Value = FromString(text); swatch.Redraw(); }, CairoFont.WhiteDetailText()));

        container.Add(swatch);
    }

    private static void DrawSwatch(Cairo.Context ctx, ElementBounds bounds, string hex)
    {
        double x = 0, y = 0, w = bounds.InnerWidth, h = bounds.InnerHeight;

        if (TryParseHex(hex, out double r, out double g, out double b))
        {
            ctx.SetSourceRGB(r, g, b);
            ctx.Rectangle(x, y, w, h);
            ctx.Fill();
        }
        else
        {
            // Unparseable: a flat dark box with a stroke through it, so it reads as "not a
            // colour" rather than as black.
            ctx.SetSourceRGB(0.12, 0.12, 0.12);
            ctx.Rectangle(x, y, w, h);
            ctx.Fill();
            ctx.SetSourceRGB(0.75, 0.3, 0.3);
            ctx.LineWidth = 2;
            ctx.MoveTo(x + 3, y + 3);
            ctx.LineTo(x + w - 3, y + h - 3);
            ctx.Stroke();
        }

        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 1;
        ctx.Rectangle(x + 0.5, y + 0.5, w - 1, h - 1);
        ctx.Stroke();
    }

    /// <summary>Parses "#rrggbb" or "#aarrggbb", with or without the hash.</summary>
    public static bool TryParseHex(string? hex, out double r, out double g, out double b)
    {
        r = g = b = 0;
        if (hex == null) return false;

        string digits = hex.Trim().TrimStart('#');
        if (digits.Length == 8) digits = digits[2..];   // drop alpha
        if (digits.Length != 6) return false;

        if (!int.TryParse(digits, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out int packed)) return false;

        r = ((packed >> 16) & 0xFF) / 255.0;
        g = ((packed >> 8) & 0xFF) / 255.0;
        b = (packed & 0xFF) / 255.0;
        return true;
    }

    /// <summary>The value as written in the definition, whatever JSON type it is.</summary>
    private static string TokenText(JsonObject value) => value.Token?.ToString() ?? "";

    /// <summary>Parses a dropdown choice back into the setting's own type.</summary>
    private static void SetFromText(ConfigSetting setting, string text)
    {
        setting.Value = setting.SettingType switch
        {
            ConfigSettingType.Integer when int.TryParse(text, out int i) => FromInt(i),
            ConfigSettingType.Float when float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float f) => FromFloat(f),
            ConfigSettingType.Boolean when bool.TryParse(text, out bool b) => FromBool(b),
            _ => FromString(text)
        };
    }

    private static bool HasRange(Validation? validation)
        => validation?.Minimum != null && validation?.Maximum != null;

    /// <summary>
    /// Pushes current values into the widgets. They are held by reference rather than
    /// looked up on the composer, because rows now live inside a container and are not
    /// registered as named composer elements.
    /// </summary>
    private void LoadControlValues()
    {
        foreach ((string key, ConfigSetting setting) in _settingsByKey)
        {
            SyncWidget(key, setting);
        }
    }

    /// <summary>
    /// Pushes one setting's current value into its control. Split out of LoadControlValues
    /// so a per-setting reset can refresh its own row without recomposing the window, which
    /// would throw away the player's scroll position on every click.
    /// </summary>
    private void SyncWidget(string key, ConfigSetting setting)
    {
        if (!_widgets.TryGetValue(key, out GuiElement? widget)) return;

        Validation? validation = setting.Validation;

        // Dropdowns are built with their selection already set, so this only matters
        // when the value changes underneath them - a reset, or a reload from file.
        if (validation?.Mapping != null)
        {
            if (widget is GuiElementDropDown mapped) mapped.SetSelectedValue(setting.MappingKey ?? "");
            return;
        }

        if (validation?.Values != null)
        {
            if (widget is GuiElementDropDown listed) listed.SetSelectedValue(TokenText(setting.Value));
            return;
        }

        switch (widget)
        {
            case GuiElementSwitch toggle:
                toggle.On = setting.Value.AsBool();
                break;

            case GuiElementSlider slider:
                slider.SetValues(
                    ToSliderInt(setting, setting.Value),
                    ToSliderInt(setting, validation!.Minimum!),
                    ToSliderInt(setting, validation.Maximum!),
                    Math.Max(1, validation.Step == null ? 1 : ToSliderInt(setting, validation.Step)));
                if (_sliderValues.TryGetValue(key, out GuiElementDynamicText? readout))
                {
                    readout.SetNewText(NumberText(setting));
                }
                break;

            case GuiElementTextArea area:
                area.SetValue(setting.Value.ToString());
                break;

            case GuiElementNumberInput number:
                number.SetValue(NumberText(setting));
                break;

            case GuiElementTextInput text:
                text.SetValue(setting.SettingType == ConfigSettingType.Color
                    ? setting.Value.AsString("")
                    : setting.Value.AsString(""));
                break;
        }
    }

    // Sliders are integer-only, so a float setting is carried at 100x and divided back.
    private const int FloatScale = 100;

    private static int ToSliderInt(ConfigSetting setting, JsonObject value)
        => setting.SettingType == ConfigSettingType.Float
            ? (int)Math.Round(value.AsFloat() * FloatScale)
            : value.AsInt();

    private static bool OnSlider(ConfigSetting setting, int value)
    {
        setting.Value = setting.SettingType == ConfigSettingType.Float
            ? FromFloat(value / (float)FloatScale)
            : FromInt(value);
        return true;
    }

    private static void OnNumberTyped(ConfigSetting setting, string text)
    {
        if (setting.SettingType == ConfigSettingType.Float)
        {
            if (float.TryParse(text, out float parsed)) setting.Value = FromFloat(parsed);
        }
        else if (int.TryParse(text, out int parsed)) setting.Value = FromInt(parsed);
    }

    private static void OnJsonTyped(ConfigSetting setting, string text)
    {
        // Half-typed JSON is normal while editing; keep the last good value instead of
        // throwing on every keystroke.
        try { setting.Value = new JsonObject(JToken.Parse(text)); }
        catch (Exception) { }
    }

    private static JsonObject FromBool(bool value) => new(new JValue(value));
    private static JsonObject FromInt(int value) => new(new JValue(value));
    private static JsonObject FromFloat(float value) => new(new JValue(value));
    private static JsonObject FromString(string value) => new(new JValue(value));

    // ------------------------------------------------------------------ actions

    private void OnDomainSelected(string domain, bool selected)
    {
        if (!selected || domain == _domain) return;

        _domain = domain;
        Compose();
        LoadControlValues();
    }

    private void OnScroll(float value)
    {
        if (_contentBounds == null) return;

        _contentBounds.fixedY = -value;
        _contentBounds.CalcWorldBounds();
    }

    private bool OnSave()
    {
        _configs[_domain].WriteToFile();
        capi.TriggerIngameError(this, "saved", $"Saved settings for {DisplayName(_domain)}.");
        return true;
    }

    private bool OnReload()
    {
        _configs[_domain].ReadFromFile();
        Compose();
        LoadControlValues();
        return true;
    }

    private bool OnDefaults()
    {
        _configs[_domain].RestoreToDefaults();
        Compose();
        LoadControlValues();
        return true;
    }

    private void OnClose() => TryClose();

    public override bool TryOpen()
    {
        bool opened = base.TryOpen();
        if (opened) LoadControlValues();
        return opened;
    }

    public override bool ShouldReceiveRenderEvents() => IsOpened();
}
