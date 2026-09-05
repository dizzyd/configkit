// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.

using ConfigKit.Formatting;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Cairo;
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
    private const double RowHeight = 36;
    private const double RowGap = 6;
    private const double LabelWidth = 320;
    private const double ControlWidth = 250;
    // Wide enough for a formatted readout, not just a bare number: [DisplayFormat] turns
    // 0.95 into "95.00 %", which wrapped onto a second line at the 54 this used to be.
    private const double ValueWidth = 78;
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

    private const double KeyWidth = 250;
    private const double EntryValueWidth = 240;
    private const double DeleteWidth = 28;
    private const double MarkWidth = 22;
    private const double EntryGap = 12;
    private const double SectionHeight = 42;

    private ElementBounds? _contentBounds;
    private double _contentHeight;

    /// The section currently unfolded, by identity rather than by caption. One at a time: a
    /// config with a dozen sub-objects is a dozen headings and one body, rather than a screen
    /// the player has to hunt through.
    private string? _openSection;

    /// Set when the whole config fits without scrolling, in which case folding it would be
    /// pure obstruction. Measured, not guessed - the layout pass already reports its height.
    private bool _allOpen;

    /// Whether this config's headings are structure or decoration. A section derived from a
    /// class is a container and folds; a separator an author placed in a definition file is a
    /// divider between rows of one flat list, and folding those turns a legible screen into
    /// ten collapsed boxes.
    private bool _foldable;

    /// The container the player has opened, and the containers above it. Empty is the root
    /// screen. Recursion here is a stack of screens rather than a recursive layout, which is
    /// why a dictionary of dictionaries needs no code of its own.
    private readonly List<ContainerFrame> _stack = new();

    private string _filter = "";

    /// "Restore defaults" throws away the whole mod's settings and is a click away from a
    /// row three levels down, so it asks once.
    private bool _confirmDefaults;

    /// Widgets whose value can only be pushed in once the composer has built them.
    private readonly List<Action> _afterCompose = new();

    /// The formatting block behind each heading, keyed by its identity, so its caption and
    /// its explanatory line are both recoverable when the block becomes a fold toggle.
    private readonly Dictionary<string, IFormattingBlock> _headingBlocks = new();

    /// Every line of prose currently drawn: separator text, and notes about members that
    /// cannot be edited.
    private readonly List<string> _notes = new();

    public ConfigDialog(ICoreClientAPI capi, Dictionary<string, Config> configs) : base(capi)
    {
        _configs = configs;
        _domains = configs.Keys.OrderBy(domain => DisplayName(domain), StringComparer.OrdinalIgnoreCase).ToList();
        _domain = _domains.FirstOrDefault() ?? "";
    }

    public override string ToggleKeyCombinationCode => "configkitconfigs";

    /// <summary>
    /// While this window is open, the keyboard belongs to it.
    ///
    /// Without this the game still reads movement keys underneath: typing a block code into
    /// a setting walked the player forwards, and W or S with nothing focused walked them
    /// around behind the window. Escape is exempt by the engine, so the window can always be
    /// closed - and the toggle hotkey now types its own letter into a focused field rather
    /// than closing, which is the right trade for a screen full of text boxes.
    /// </summary>
    public override bool CaptureAllInputs() => true;

    /// <summary>The configs this window is showing, keyed by mod domain.</summary>
    public IReadOnlyDictionary<string, Config> Configs => _configs;

    /// <summary>
    /// The settings currently laid out, keyed by widget. Empty until the window has been
    /// composed, and rebuilt whenever the selected mod changes.
    /// </summary>
    public IReadOnlyDictionary<string, ConfigSetting> RenderedSettings => _settingsByKey;

    /// <summary>
    /// Where each rendered control sits and how tall it is, top to bottom. Rows are laid out
    /// by advancing a y cursor, so a control taller than the step it advances by silently
    /// overlaps the row beneath it - which is invisible to every test that only asks whether
    /// a row exists.
    /// </summary>
    public IReadOnlyList<(string Code, double Y, double Height)> RowGeometry
        => _settingsByKey
            .Where(entry => _widgets.ContainsKey(entry.Key))
            .Select(entry => (
                entry.Value.YamlCode,
                _widgets[entry.Key].Bounds.fixedY,
                _widgets[entry.Key].Bounds.fixedHeight))
            .OrderBy(entry => entry.fixedY)
            .ToList();

    /// <summary>
    /// The message shown when a setting fails its own validation attributes, or "" when the
    /// config is sound.
    /// </summary>
    public string ErrorText { get; private set; } = "";

    private static CairoFont ErrorFont => CairoFont.WhiteSmallText().WithColor(GuiStyle.ErrorTextColor);

    /// <summary>
    /// Puts the current validation errors on the status line. Called after composing and
    /// after every edit, and it never recomposes - a dynamic text element is exactly the
    /// thing that can change under a player mid-keystroke without disturbing their cursor.
    /// </summary>
    private void ShowErrors()
    {
        if (!_configs.TryGetValue(_domain, out Config? config)) return;

        IReadOnlyDictionary<string, string> errors = config.Errors;

        ErrorText = errors.Count switch
        {
            0 => "",
            1 => $"{LabelForCode(config, errors.Keys.First())}: {errors.Values.First()}",
            _ => $"{LabelForCode(config, errors.Keys.First())}: {errors.Values.First()}"
                 + $"  (and {errors.Count - 1} more)"
        };

        SingleComposer?.GetDynamicText("errors")?.SetNewText(ErrorText);
    }

    private static string LabelForCode(Config config, string code)
        => config.GetSetting(code) is ConfigSetting setting ? LabelFor(setting) : code;

    /// <summary>
    /// What the mod dropdown offers, in the order a player sees it.
    /// </summary>
    public IReadOnlyList<string> DisplayNames => _domains.Select(DisplayName).ToList();

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
    /// How many steps a setting's slider has, which is what decides whether dragging it
    /// lands on values a player meant.
    /// </summary>
    public int SliderStepsFor(string yamlCode)
    {
        foreach (ConfigSetting setting in _settingsByKey.Values)
        {
            if (setting.YamlCode != yamlCode || setting.Validation is not { Minimum: not null, Maximum: not null }) continue;

            return ToSliderInt(setting, setting.Validation.Maximum) - ToSliderInt(setting, setting.Validation.Minimum);
        }

        return 0;
    }

    /// <summary>
    /// What a setting's text control currently shows, by yaml code. A nullable number reads
    /// empty when it holds null, which is the whole point of it and not visible in the value.
    /// </summary>
    public string NumberTextFor(string yamlCode)
        => WidgetFor(yamlCode) is GuiElementEditableTextBase text ? text.GetText() : "";

    /// <summary>
    /// Where a setting's control sits on screen, for a test that wants to click it rather
    /// than call its handler. Null when the setting is not currently drawn.
    /// </summary>
    public (double X, double Y, double Width, double Height)? ScreenRectFor(string yamlCode)
    {
        if (WidgetFor(yamlCode) is not GuiElement widget) return null;

        ElementBounds bounds = widget.Bounds;
        return (bounds.absX, bounds.absY, bounds.OuterWidth, bounds.OuterHeight);
    }

    /// <summary>
    /// Types into a setting's text control the way a player does - through the widget, so the
    /// change runs the handler the widget was built with rather than a path only a test uses.
    /// </summary>
    public bool TypeInto(string yamlCode, string text)
    {
        if (WidgetFor(yamlCode) is not GuiElementEditableTextBase editable) return false;

        editable.SetValue(text);
        return true;
    }

    private GuiElement? WidgetFor(string yamlCode)
    {
        foreach ((string key, ConfigSetting setting) in _settingsByKey)
        {
            if (setting.YamlCode == yamlCode && _widgets.TryGetValue(key, out GuiElement? widget)) return widget;
        }

        return null;
    }

    /// <summary>
    /// The class name of the control a setting was actually given, by yaml code, or "" if
    /// it is not on screen. Which control a range gets is a decision in its own right - an
    /// open bound has to become a number input rather than a slider - and asserting on the
    /// range alone cannot see it.
    /// </summary>
    public string ControlKindFor(string yamlCode) => WidgetFor(yamlCode)?.GetType().Name ?? "";

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

    /// <summary>
    /// The container screens currently open, outermost first. Empty on the root screen.
    /// </summary>
    public IReadOnlyList<string> OpenPath => _stack.Select(frame => frame.Crumb).ToList();

    /// <summary>
    /// The section headings on the root screen, in the order they are drawn. A section under a
    /// nested object carries its whole path - "Rewards › Easy" - so a leaf three classes down
    /// still says where it came from.
    /// </summary>
    public IReadOnlyList<string> Sections
        => _stack.Count > 0
            ? []
            : _configs[_domain].ConfigBlocks.Values
                .OfType<IFormattingBlock>()
                .Where(block => block.Title != null)
                .Select(block => block.Title)
                .ToList();

    /// <summary>The identity behind a heading's caption, or the caption itself if it has none.</summary>
    private string SectionKey(string title)
        => _configs[_domain].ConfigBlocks.Values
            .OfType<IFormattingBlock>()
            .Where(block => block.Title == title)
            .Select(block => block.Code ?? block.Title)
            .FirstOrDefault() ?? title;

    /// <summary>
    /// Every line of prose on screen: a section's explanatory text, and the note beside a
    /// member ConfigKit cannot store.
    /// </summary>
    public IReadOnlyList<string> RenderedNotes => _notes;

    /// <summary>The label drawn beside each setting currently on screen.</summary>
    public IReadOnlyList<string> RenderedLabels
        => _settingsByKey.Values.Select(LabelFor).ToList();

    /// <summary>The unfolded section's caption, or null when none is - or when all are shown.</summary>
    public string? OpenSection
        => _allOpen || _openSection == null
            ? null
            : _headingBlocks.TryGetValue(_openSection, out IFormattingBlock? block) ? block.Title : _openSection;

    /// <summary>True when the whole config fitted, so nothing is folded away.</summary>
    public bool EverythingShown => _allOpen;

    /// <summary>The entry labels on the container screen, in the order they are drawn.</summary>
    public IReadOnlyList<string> EntryLabels
    {
        get
        {
            if (_stack.Count == 0) return [];

            ContainerFrame frame = _stack[^1];
            JToken? token = Subtree.Navigate(frame.Setting.Value.Token, frame.Path);

            return Entries(token, frame.Node)
                .Select(entry => entry.label)
                .Where(label => _filter.Length == 0 || label.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <summary>Whether a new entry can be added here, and if not, why not.</summary>
    public bool CanAddEntry(out string reason)
    {
        reason = "";
        if (_stack.Count == 0) return false;

        ContainerFrame frame = _stack[^1];
        if (frame.Locked) { reason = "the server owns this setting"; return false; }
        if (frame.Node.Kind == SchemaKind.Object) { reason = "this is a fixed set of settings"; return false; }
        if (frame.Node.Kind != SchemaKind.Dictionary) return true;

        JObject existing = Subtree.Navigate(frame.Setting.Value.Token, frame.Path) as JObject ?? new JObject();
        return KeyGenerator.TryGenerate(existing, frame.Node.KeyNode, out _, out reason);
    }

    /// <summary>
    /// Opens a container setting's screen, exactly as its row's button does. False if that
    /// setting is not on screen or is not something that can be opened.
    /// </summary>
    public bool OpenSetting(string yamlCode)
    {
        if (_configs[_domain].GetSetting(yamlCode) is not ConfigSetting setting) return false;
        if (setting.Node is not { Kind: SchemaKind.Dictionary or SchemaKind.List } node) return false;

        // The same crumb the row's own button uses, so driving the screen from code and
        // clicking it produce the same breadcrumb.
        return OpenContainer(setting, node, LabelFor(setting), IsServerControlled(setting));
    }

    /// <summary>Opens the entry with this label, for a value that is itself a container or an object.</summary>
    public bool OpenEntry(string label)
    {
        if (_stack.Count == 0) return false;

        ContainerFrame frame = _stack[^1];
        JToken? token = Subtree.Navigate(frame.Setting.Value.Token, frame.Path);

        foreach ((object step, string entryLabel, JToken _) in Entries(token, frame.Node))
        {
            if (entryLabel != label) continue;

            SchemaNode? schema = SchemaOf(frame, step);
            if (schema == null || schema.Kind is not (SchemaKind.Object or SchemaKind.Dictionary or SchemaKind.List))
            {
                return false;
            }

            return OpenNested(frame, new List<object>(frame.Path) { step }, schema, label);
        }

        return false;
    }

    /// <summary>
    /// The schema for one row. A collection's rows all share the value schema; an object's
    /// rows each have their own, because its fields are not interchangeable.
    /// </summary>
    private static SchemaNode? SchemaOf(ContainerFrame frame, object step) => frame.Node.Kind switch
    {
        SchemaKind.Object => frame.Node.Children.FirstOrDefault(child => child.Code as object is string code && Equals(code, step)),
        SchemaKind.Dictionary => frame.Node.ValueNode,
        _ => frame.Node.ElementNode
    };

    /// <summary>
    /// Puts one field of an object entry back to its class default, as its Reset button does.
    /// False when this is not an object screen, or the class declares no default for it.
    /// </summary>
    public bool ResetField(string label)
    {
        if (_stack.Count == 0 || _stack[^1].Node.Kind != SchemaKind.Object) return false;

        ContainerFrame frame = _stack[^1];
        if (frame.Locked) return false;

        JObject? defaults = Defaults(frame.Node.MemberType);

        foreach (SchemaNode child in frame.Node.Children)
        {
            if ((child.Label ?? SchemaBuilder.Humanize(child.Code)) != label) continue;
            if (defaults?[child.Code] is not JToken value) return false;

            return OnResetField(frame, child.Code, value);
        }

        return false;
    }

    /// <summary>Goes up one container screen, as the Back button does.</summary>
    public bool Back() => OnBack();

    /// <summary>Adds an entry here, as the Add button does.</summary>
    public bool AddEntry() => _stack.Count > 0 && CanAddEntry(out _) && OnAddEntry(_stack[^1]);

    /// <summary>Removes the entry with this label, as its row's button does.</summary>
    public bool RemoveEntry(string label)
    {
        if (_stack.Count == 0 || _stack[^1].Node.Kind == SchemaKind.Object) return false;

        ContainerFrame frame = _stack[^1];
        JToken? token = Subtree.Navigate(frame.Setting.Value.Token, frame.Path);

        foreach ((object step, string entryLabel, JToken _) in Entries(token, frame.Node))
        {
            if (entryLabel == label) return OnRemoveEntry(frame, step);
        }

        return false;
    }

    /// <summary>
    /// Renames a key, as leaving its field does. False when the name is taken or blank - the
    /// entry is left alone rather than merged away.
    /// </summary>
    public bool RenameEntry(string from, string to)
    {
        if (_stack.Count == 0 || _stack[^1].Node.Kind == SchemaKind.Object) return false;

        bool renamed = Subtree.Rename(_stack[^1].Setting, _stack[^1].Path, from, to);
        Recompose();
        return renamed;
    }

    /// <summary>The text in the filter box.</summary>
    public string Filter => _filter;

    /// <summary>Types into the filter box, as the player does.</summary>
    public void SetFilter(string text)
    {
        string trimmed = text.Trim();
        if (trimmed == _filter) return;

        _filter = trimmed;
        Recompose();
    }

    /// <summary>Folds or unfolds a section by its caption, as clicking its heading does.</summary>
    public bool ToggleSectionNamed(string title) => ToggleSection(SectionKey(title));

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();

        // Every edit runs through the config, which sets each setting's Error before raising
        // this - so by the time it arrives the status line only has to be redrawn.
        foreach (Config config in _configs.Values) config.SettingChanged += OnAnySettingChanged;

        Compose();
    }

    public override void OnGuiClosed()
    {
        foreach (Config config in _configs.Values) config.SettingChanged -= OnAnySettingChanged;

        base.OnGuiClosed();
    }

    private void OnAnySettingChanged(ISetting _) => ShowErrors();

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
        _afterCompose.Clear();

        // Lay the rows out once without a composer to learn how tall they are, so a mod
        // with three settings gets a short panel instead of half a screen of empty wood.
        // The same pass decides whether the sections need folding at all: if everything
        // fits, folding it would only hide things for no reason.
        if (_stack.Count > 0)
        {
            _allOpen = false;
            _contentHeight = LayoutEntries(null);
        }
        else
        {
            _allOpen = true;
            double unfolded = LayoutRows(null, _configs[_domain]);

            // A filtered list is never folded - hiding half the matches behind a heading is
            // the opposite of what the player just asked for - and neither is a config whose
            // headings are an author's dividers rather than structure.
            _allOpen = !_foldable || _filter.Length > 0 || unfolded <= MaxContentHeight;
            _contentHeight = _allOpen ? unfolded : LayoutRows(null, _configs[_domain]);
        }

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
        // Its own line under the buttons, full width. Beside them it had a third of the
        // width, and a two-line message was clipped by the bottom of the dialog.
        ElementBounds errorBounds = ElementBounds.Fixed(0, buttonY + 34, DialogWidth - 40, 24);

        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        GuiComposer composer = capi.Gui
            .CreateCompo("configkit-settings", ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle))
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar("Mod settings", OnClose)
            .BeginChildElements(bgBounds);

        if (_stack.Count > 0)
        {
            AddContainerHeader(composer, dropdownBounds);
        }
        else
        {
            composer.AddDropDown(
                _domains.ToArray(),
                _domains.Select(DisplayName).ToArray(),
                Math.Max(0, _domains.IndexOf(_domain)),
                OnDomainSelected,
                dropdownBounds,
                "domain");

            AddFilterField(composer, dropdownBounds.fixedY);
        }

        composer
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
        if (_stack.Count > 0)
        {
            LayoutEntries(composer.GetContainer("rows"));
        }
        else
        {
            LayoutRows(composer.GetContainer("rows"), _configs[_domain]);
        }

        _contentBounds.fixedHeight = _contentHeight;

        composer
                .EndClip();

        // A scrollbar over content that already fits tells the player there is more to see.
        if (needsScrollbar) composer.AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar");

        // The same three buttons at every depth, acting on the whole config. Swapping them
        // for a Back button inside a container would make Save look like it saved only what
        // is on screen.
        composer
            .AddSmallButton("Save", OnSave, saveBounds, EnumButtonStyle.Normal)
            .AddSmallButton("Reload", OnReload, reloadBounds, EnumButtonStyle.Normal)
            .AddSmallButton(_confirmDefaults ? "Sure? Click again" : "Restore defaults",
                OnDefaults, defaultsBounds, EnumButtonStyle.Normal)
            // A live line rather than part of a row: an error appears and clears on every
            // keystroke, and growing a row to hold a message would relayout the window under
            // the player's cursor as they typed.
            .AddDynamicText("", ErrorFont, errorBounds, "errors")
            .EndChildElements();

        SingleComposer = composer.Compose();

        ShowErrors();

        foreach (Action action in _afterCompose) action();

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

        // Pair every block with the heading it sits under, so a filter can decide what to
        // show before anything is drawn - a heading is only worth drawing if something under
        // it survived.
        List<(string? section, IConfigBlock block)> blocks = [];
        string? walking = null;

        if (container != null) { _headingBlocks.Clear(); _notes.Clear(); }

        bool filtering = _filter.Length > 0;
        _foldable = config.Schema != null;

        foreach ((float _, IConfigBlock block) in config.ConfigBlocks)
        {
            if (block is IFormattingBlock heading && heading.Title != null)
            {
                // A definition file's separator may carry no code; then its caption is all
                // the identity there is, which is what it has always been.
                walking = heading.Code ?? heading.Title;
                _headingBlocks[walking] = heading;

                // A heading derived from a class is a fold toggle, drawn when its first
                // member is. A definition's separator is a divider the author placed, and
                // it stays where it was put: lifting it out of the sequence lost a trailing
                // one, and one followed only by another, together with the text on it.
                if (_foldable) continue;
            }

            blocks.Add((walking, block));
        }

        // Filtering cuts across the folding: a player searching for "delay" wants every
        // match, not the matches that happen to be in the one open section.
        HashSet<string> withMatches = filtering
            ? blocks.Where(entry => entry.section != null && Matches(entry.section, entry.block))
                    .Select(entry => entry.section!)
                    .ToHashSet()
            : [];

        string? headed = null;

        foreach ((string? section, IConfigBlock block) in blocks)
        {
            if (block is IFormattingBlock formatting)
            {
                // A filtered list shows what matches. A note about an uneditable member is
                // not a match for anything the player typed unless its own text says so.
                bool textMatches = formatting.Text?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false;

                if (formatting.Title != null)
                {
                    // A definition's separator, in place. Under a filter it is shown when
                    // something beneath it matched, or its own line did.
                    if (!filtering || textMatches || (section != null && withMatches.Contains(section)))
                    {
                        y = AddFormattingBlock(container, formatting, y);
                        headed = section;
                    }
                    continue;
                }

                if (!filtering || textMatches)
                {
                    y = AddFormattingBlock(container, formatting, y);
                }
                continue;
            }

            if (section != null && section != headed && (!filtering || withMatches.Contains(section)))
            {
                y = _foldable
                    ? AddSectionHeader(container, section, filtering || _allOpen || section == _openSection, y)
                    : AddFormattingBlock(container, _headingBlocks[section], y);

                headed = section;
            }

            if (block is not ConfigSetting setting || setting.Hide) continue;
            if (!Matches(section, block)) continue;

            string key = $"setting-{index++}";

            // How tall this row is, decided before the measuring pass bails out. It used to
            // add RowHeight for every row regardless, which is right until a row is taller
            // than one line: the window was then sized for a raw-JSON row it had counted as
            // an ordinary one, and clipped the box it had just made room for.
            bool isContainer = setting.Node is { Kind: SchemaKind.Dictionary or SchemaKind.List };
            double rowHeight = isContainer ? RowHeight : ControlHeight(setting);

            if (container == null)
            {
                y += rowHeight + RowGap;
                continue;
            }

            // Two different reasons a row cannot be edited, and they read differently: one
            // says the server owns it, the other that nothing owns it.
            bool serverOwned = IsServerControlled(setting);
            bool locked = serverOwned || setting.ReadOnly;
            SchemaNode? node = setting.Node;
            bool container_ = isContainer;

            if (!locked && !container_) _settingsByKey[key] = setting;
            if (container_) _settingsByKey[key] = setting;

            // Raw JSON is a document, not a value: it gets the label's own line and then the
            // full width of the row beneath it. In the control column it had 250px, which is
            // the width of a slider and no use at all for reading JSON.
            bool document = !container_ && setting.SettingType == ConfigSettingType.Other;

            ElementBounds controlBounds = document
                ? ElementBounds.Fixed(0, y + RowHeight, RowWidth, rowHeight - RowHeight - 4)
                : ElementBounds.Fixed(LabelWidth + 16, y, ControlWidth, rowHeight - 4);

            string labelText = serverOwned ? LabelFor(setting) + " (server)" : LabelFor(setting);
            CairoFont labelFont = locked
                ? CairoFont.WhiteSmallText().WithColor(GuiStyle.ColorParchment)
                : CairoFont.WhiteSmallText();

            // A document row's label heads its box rather than sitting beside a control, so
            // its baseline comes from one line's worth rather than the whole tall row.
            double baseline = Baseline(labelFont, y, document ? RowHeight : controlBounds.fixedHeight);
            ElementBounds labelBounds = OnBaseline(labelText, labelFont, baseline, 0, LabelWidth);

            container.Add(new GuiElementDynamicText(capi, labelText, labelFont, labelBounds));

            if (!string.IsNullOrEmpty(setting.Comment))
            {
                container.Add(new GuiElementHoverText(capi, setting.Comment,
                    CairoFont.WhiteDetailText(), 320, labelBounds.FlatCopy()));
            }

            if (container_)
            {
                // One row, whatever is inside it. A dictionary's contents are unbounded and
                // its entries need a different set of columns than a setting row has.
                ConfigSetting owner = setting;
                SchemaNode owned = node!;
                container.Add(new GuiElementTextButton(capi, EntryCountText(setting) + "  >",
                    CairoFont.WhiteDetailText(), CairoFont.WhiteDetailText().WithColor(GuiStyle.ActiveButtonTextColor),
                    () => OpenContainer(owner, owned, LabelFor(setting), locked),
                    controlBounds, EnumButtonStyle.Small));

                if (!locked) AddResetButton(container, setting, y, key);
            }
            else if (locked)
            {
                container.Add(new GuiElementDynamicText(capi, ValueText(setting),
                    CairoFont.WhiteDetailText(), controlBounds));
            }
            else
            {
                AddControl(container, setting, controlBounds, key);
                AddResetButton(container, setting, y, key);
            }

            y += rowHeight + RowGap;
        }

        return y;
    }

    /// <summary>
    /// Whether a row is on screen: what the filter matches, or - with no filter - what the
    /// open section holds, plus everything belonging to no section at all.
    /// </summary>
    private bool Matches(string? section, IConfigBlock block)
    {
        if (block is not ConfigSetting setting || setting.Hide) return false;

        if (_filter.Length == 0)
        {
            return section == null || !_foldable || _allOpen || section == _openSection;
        }

        return LabelFor(setting).Contains(_filter, StringComparison.OrdinalIgnoreCase)
            || setting.YamlCode.Contains(_filter, StringComparison.OrdinalIgnoreCase)
            || (section != null && section.Contains(_filter, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A foldable heading. Opening one closes whichever was open before it.</summary>
    private double AddSectionHeader(GuiElementContainer? container, string key, bool open, double y)
    {
        string title = _headingBlocks.TryGetValue(key, out IFormattingBlock? block) ? block.Title : key;

        if (container != null)
        {
            // ASCII, not a geometric arrow. The game's font carries no U+25B6 on every
            // platform, and a heading that renders as an empty box on someone's machine is
            // worse than one that is merely plain.
            string label = (open ? "  [-]  " : "  [+]  ") + title;

            // One width for every heading, and left aligned. Sizing each button to its own
            // text made a column that stepped in and out down the screen; leaving the text
            // centred - which is all GuiElementTextButton does by default - made every
            // heading float in the middle while its rows sat at the margin.
            GuiElementTextButton toggle = new(capi, label,
                CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold),
                CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold).WithColor(GuiStyle.ActiveButtonTextColor),
                () => ToggleSection(key),
                ElementBounds.Fixed(0, y + 2, DialogWidth - 40, 32), EnumButtonStyle.Small);

            toggle.SetOrientation(EnumTextOrientation.Left);
            container.Add(toggle);
        }

        y += SectionHeight;

        // A separator carries an explanatory line as well as a title, and turning the block
        // into a fold toggle threw that line away - a definition that explained its group
        // lost the explanation with nothing to show it had ever been there.
        if (!open || block?.Text == null)
        {
            return y;
        }

        if (container != null)
        {
            container.Add(new GuiElementDynamicText(capi, block.Text,
                CairoFont.WhiteDetailText(), ElementBounds.Fixed(0, y, DialogWidth - 40, 24)));
            _notes.Add(block.Text);
        }

        return y + 28;
    }

    private bool ToggleSection(string key)
    {
        _openSection = _openSection == key ? null : key;
        Recompose();
        return true;
    }

    /// <summary>"12 entries", or "empty" - the count is the useful thing on a closed row.</summary>
    private static string EntryCountText(ConfigSetting setting)
    {
        int count = setting.Value.Token switch
        {
            JObject o => o.Count,
            JArray a => a.Count,
            _ => 0
        };

        return count switch
        {
            0 => "empty",
            1 => "1 entry",
            _ => $"{count} entries"
        };
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
            if (container != null)
            {
                container.Add(new GuiElementDynamicText(capi,
                    block.Text, CairoFont.WhiteDetailText(),
                    ElementBounds.Fixed(0, y, DialogWidth - 40, 24)));
                _notes.Add(block.Text);
            }

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

    /// <summary>
    /// The number as text. A null on a nullable setting is an empty string, never "0":
    /// AsFloat on a null token returns zero, and for a member whose null means "no limit"
    /// while zero means "none allowed", showing the zero states the opposite of the truth.
    /// </summary>
    private static string NumberText(ConfigSetting setting)
    {
        if (setting.Nullable && setting.IsNull) return "";

        return setting.SettingType == ConfigSettingType.Float
            ? setting.Value.AsFloat().ToString(System.Globalization.CultureInfo.InvariantCulture)
            : setting.Value.AsInt().ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>What the dropdown shows for a bool? - "", "true" or "false".</summary>
    private static string NullableBoolText(ConfigSetting setting)
        => setting.IsNull ? NullChoice : (setting.Value.AsBool() ? "true" : "false");

    /// <summary>
    /// The caption for "no value" in a dropdown. Blank would be invisible and unclickable,
    /// so it is a word, and one no enum member or allowed value is likely to collide with.
    /// </summary>
    private const string NullChoice = "(unset)";

    /// <summary>A dropdown's choices, with the unset option in front where null is a value.</summary>
    private static string[] WithBlank(ConfigSetting setting, IEnumerable<string> choices)
        => setting.Nullable ? [NullChoice, .. choices] : [.. choices];

    private static bool IsBlank(ConfigSetting setting, string choice)
        => setting.Nullable && choice == NullChoice;

    /// <summary>
    /// The same number as the author would write it: NumberText, then any [DisplayFormat] on
    /// top. A ratio declared with "P" reads 95% rather than 0.95, which is how the setting is
    /// described everywhere except in the file.
    ///
    /// Only for text a player reads, never for text a player edits - see FormatString in the
    /// schema builder. An editable control keeps the raw number so that what is typed and what
    /// is stored are the same string.
    /// </summary>
    private static string ReadoutText(ConfigSetting setting) => Formatted(setting, NumberText(setting));

    private static string Formatted(ConfigSetting setting, string fallback)
        => setting.SettingType == ConfigSettingType.Float
            ? Format(setting, setting.Value.AsFloat(), () => fallback)
            : Format(setting, setting.Value.AsInt(), () => fallback);

    private static string LabelFor(ConfigSetting setting)
    {
        string? label = setting.InGui;

        // A managed (POCO) config labels each setting "<domain>:setting-<FieldName>", which
        // Lang returns unchanged when the mod ships no translation for it. Showing a player
        // "mymod:setting-MaxRadius" is worse than showing "Max radius".
        if (string.IsNullOrWhiteSpace(label) || IsUntranslatedLangKey(label!))
        {
            // The last segment only. A nested setting's code is a path, and a row reading
            // "Rain collector/litres per hour" repeats what its own heading already says.
            string code = setting.YamlCode;
            int slash = code.LastIndexOf('/');

            return Humanize(slash >= 0 ? code[(slash + 1)..] : code);
        }

        return label!;
    }

    private static bool IsUntranslatedLangKey(string label)
        => label.Contains(':') && !label.Contains(' ');

    /// <summary>"MaxClientViewDistance" -> "Max client view distance".</summary>
    private static string Humanize(string code) => SchemaBuilder.Humanize(code);

    // ------------------------------------------------------------------ controls

    private void AddControl(GuiElementContainer container, ConfigSetting setting, ElementBounds bounds, string key)
    {
        Validation? validation = setting.Validation;

        if (validation?.Mapping != null)
        {
            string[] keys = WithBlank(setting, validation.Mapping.Keys);
            int selected = Math.Max(0, Array.IndexOf(keys, setting.MappingKey ?? ""));
            Remember(key, container, new GuiElementDropDown(capi, keys, keys, selected,
                (code, on) =>
                {
                    if (!on) return;
                    if (IsBlank(setting, code)) setting.Value = FromNull();
                    else setting.MappingKey = code;
                },
                bounds, CairoFont.WhiteDetailText(), false));
            return;
        }

        if (validation?.Values != null)
        {
            // Render the raw token, not AsString: JsonObject.AsString returns the default
            // for anything that is not literally a string, so a list of numbers came out as
            // blank rows and wrote an empty string back into the setting.
            string[] values = WithBlank(setting, validation.Values.Select(TokenText));
            int selected = Math.Max(0, Array.IndexOf(values, TokenText(setting.Value)));
            Remember(key, container, new GuiElementDropDown(capi, values, values, selected,
                (code, on) =>
                {
                    if (!on) return;
                    if (IsBlank(setting, code)) setting.Value = FromNull();
                    else SetFromText(setting, code);
                },
                bounds, CairoFont.WhiteDetailText(), false));
            return;
        }

        switch (setting.SettingType)
        {
            // A switch has two positions and bool? has three states, so a nullable one gets
            // the dropdown instead - otherwise null reads as false, which is a value.
            case ConfigSettingType.Boolean when setting.Nullable:
                string[] choices = [NullChoice, "true", "false"];
                Remember(key, container, new GuiElementDropDown(capi, choices, choices,
                    Math.Max(0, Array.IndexOf(choices, NullableBoolText(setting))),
                    (code, on) =>
                    {
                        if (!on) return;
                        setting.Value = code == NullChoice ? FromNull() : FromBool(code == "true");
                    },
                    bounds, CairoFont.WhiteDetailText(), false));
                break;

            case ConfigSettingType.Boolean:
                Remember(key, container, new GuiElementSwitch(capi, on => setting.Value = FromBool(on), bounds));
                break;

            // A slider has a position for every value in its range and none for "unset", so
            // a nullable number takes the input, where clearing the box is how null is said.
            case ConfigSettingType.Integer when HasRange(setting) && !setting.Nullable:
            case ConfigSettingType.Float when HasRange(setting) && !setting.Nullable:
                AddSliderControl(container, setting, bounds, key);
                break;

            case ConfigSettingType.Integer:
            case ConfigSettingType.Float:
                Remember(key, container, new GuiElementNumberInput(capi, bounds,
                    text => OnNumberTyped(setting, text), CairoFont.WhiteDetailText()));
                break;

            case ConfigSettingType.Other:
                // Autoheight is on by default and grows the element to fit its text on every
                // change, straight out of the space the layout gave it - a WearAndTear member
                // of 844 characters drew a box 624px tall in a 72px row and buried nine rows
                // under it. The height is decided once, by ControlHeight, and held.
                GuiElementTextArea area = new(capi, bounds, text => OnJsonTyped(setting, text),
                    CairoFont.WhiteDetailText()) { Autoheight = false };

                Remember(key, container, area);
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

    /// <summary>
    /// How tall a setting's control needs to be. One line for everything with a widget of
    /// its own; for raw JSON, as many as the text needs, between two and six.
    ///
    /// The ceiling matters: a member with no editor of its own can hold an arbitrarily large
    /// document, and a row that grows without limit is a page with one row on it. Six lines
    /// shows the shape of the value; a member this size wants the drill-down editor, and
    /// raw JSON is the honest fallback for one that cannot have it.
    /// </summary>
    private double ControlHeight(ConfigSetting setting)
    {
        if (setting.SettingType != ConfigSettingType.Other) return RowHeight;

        // A label line, then as many lines as the document needs. The window scrolls, so a
        // long value is reachable rather than clipped - the earlier six-line ceiling kept
        // the row small and made the value unreadable instead, which is the wrong trade for
        // the one control with no editor of its own. The ceiling that remains is only to
        // stop a pathological value making a page with one row on it.
        double lines = Math.Clamp(MeasuredLines(JsonText(setting)), 3, MaxJsonLines);

        return RowHeight + RowHeight + lines * JsonLineHeight;
    }

    /// <summary>
    /// A raw value as it is both measured and shown. The two have to agree: sizing from the
    /// indented form and displaying the compact one is what made the box look empty.
    /// </summary>
    private static string JsonText(ConfigSetting setting)
        => setting.Value.Token?.ToString(Newtonsoft.Json.Formatting.Indented) ?? "";

    private double MeasuredLines(string text)
    {
        if (text.Length == 0) return 2;

        // Newlines from the indented JSON, plus what wrapping adds at this width.
        double perLine = Math.Max(1, RowWidth / JsonCharWidth);

        return text.Split('\n').Sum(line => Math.Max(1, Math.Ceiling(line.Length / perLine)));
    }

    // Measured against CairoFont.WhiteDetailText at the widths this dialog uses. Approximate
    // on purpose: the exact figure comes from the font at compose time, and being a line out
    // costs a little whitespace where being wrong about the row costs a readable screen.
    private const double JsonLineHeight = 20;
    private const double JsonCharWidth = 7.0;

    /// <summary>The most lines a raw value is given before it has to be scrolled past.</summary>
    private const int MaxJsonLines = 24;

    /// <summary>The full width of a row, label column through to the far edge of Reset.</summary>
    private const double RowWidth = LabelWidth + 16 + ControlWidth + ResetGap + ResetWidth;

    /// <summary>
    /// The baseline every piece of text in a row sits on.
    ///
    /// GuiElementDynamicText draws from the top of its bounds, so a label handed the row's
    /// own y sat about ten pixels above the middle of the switch or slider beside it. But
    /// centring each text box independently is not the fix: the label and the slider readout
    /// use different fonts, so their boxes are different heights and centring both leaves
    /// their baselines a couple of pixels apart - the kind of wrongness that is harder to see
    /// and worse to look at.
    ///
    /// So the row picks one baseline and every text element is placed against it. Its
    /// position centres the *cap height* rather than the line box, because a line box carries
    /// room for descenders whether or not the text has any: centring that puts a label with
    /// no descender visibly low.
    /// </summary>
    private double Baseline(CairoFont font, double y, double controlHeight)
    {
        FontExtents extents = font.GetFontExtents();
        double capHeight = extents.Ascent - extents.Descent;

        return y + (controlHeight + capHeight) / 2;
    }

    /// <summary>
    /// Bounds for text whose baseline lands on the row's, given its own font's ascent. A
    /// wrapped label grows downwards from that first baseline, as a paragraph does.
    /// </summary>
    private ElementBounds OnBaseline(string text, CairoFont font, double baseline, double x, double width)
    {
        double height = capi.Gui.Text.GetMultilineTextHeight(font, text, width);

        return ElementBounds.Fixed(x, baseline - font.GetFontExtents().Ascent, width,
            Math.Max(RowHeight, height));
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
        // On the row's baseline, taken from the label's font rather than the readout's own,
        // so the number and the label it belongs to sit on one line.
        CairoFont readoutFont = CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Right);

        ElementBounds valueBounds = OnBaseline(ReadoutText(setting), readoutFont,
            Baseline(CairoFont.WhiteSmallText(), bounds.fixedY, bounds.fixedHeight),
            bounds.fixedX + bounds.fixedWidth - ValueWidth, ValueWidth);

        GuiElementDynamicText readout = new(capi, ReadoutText(setting), readoutFont, valueBounds);

        GuiElementSlider slider = new(capi, value =>
        {
            bool handled = OnSlider(setting, value);
            readout.SetNewText(ReadoutText(setting));
            return handled;
        }, sliderBounds);

        // The slider carries an integer, and a float setting rides it multiplied up - so
        // left alone it shows its own number while the readout beside it shows the real
        // one, and a value of 100 reads as 10000 the moment you grab the handle.
        slider.OnSliderTooltip = value => SliderText(setting, value);

        Remember(key, container, slider);

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

    /// <summary>
    /// Whether a declared range can actually be a slider.
    ///
    /// Sliders are integer-only, so both bounds have to survive the trip through
    /// <see cref="ToSliderInt"/>, and a great many real configs declare bounds that do not.
    /// An unbounded maximum is idiomatic - <c>[Range(0, double.PositiveInfinity)]</c> for a
    /// multiplier, <c>[Range(1, int.MaxValue)]</c> for an interval in milliseconds - and it
    /// means "no upper limit", not "a slider two billion units wide". Converting infinity to
    /// int saturates at int.MaxValue rather than throwing, so the range survives the maths
    /// and only falls over later, in the widget, taking the client down with it.
    ///
    /// A range that cannot be a slider is not discarded: it still validates, the setting just
    /// gets the plain number input, which is the only sensible control for an open bound
    /// anyway. This is what AutoConfigLib does with the same attributes.
    /// </summary>
    private static bool HasRange(ConfigSetting setting)
    {
        Validation? validation = setting.Validation;
        if (validation?.Minimum == null || validation.Maximum == null) return false;

        float min = validation.Minimum.AsFloat();
        float max = validation.Maximum.AsFloat();

        if (!float.IsFinite(min) || !float.IsFinite(max) || max <= min) return false;

        // The span is measured at the scale the slider would actually use, because that is
        // what has to fit in an int - and in something a player can drag.
        double steps = (double)(max - min) * ScaleFor(setting);
        return steps >= 1 && steps <= MaxSliderSteps;
    }

    /// <summary>
    /// The widest slider worth drawing. Past this the control is a worse number input: a
    /// pixel is thousands of units, so no particular value can be chosen with it.
    /// </summary>
    private const int MaxSliderSteps = 1_000_000;

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
            if (widget is GuiElementDropDown mapped)
            {
                mapped.SetSelectedValue(setting.Nullable && setting.IsNull
                    ? NullChoice
                    : setting.MappingKey ?? "");
            }
            return;
        }

        if (validation?.Values != null)
        {
            if (widget is GuiElementDropDown listed)
            {
                listed.SetSelectedValue(setting.Nullable && setting.IsNull
                    ? NullChoice
                    : TokenText(setting.Value));
            }
            return;
        }

        switch (widget)
        {
            // A bool? is a dropdown rather than a switch, and reaches here with no mapping
            // or value list to have been caught by the branches above.
            case GuiElementDropDown tristate when setting.SettingType == ConfigSettingType.Boolean:
                tristate.SetSelectedValue(NullableBoolText(setting));
                break;

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
                    readout.SetNewText(ReadoutText(setting));
                }
                break;

            case GuiElementTextArea area:
                // The same form ControlHeight measured. Showing the compact token instead
                // put 115 characters of one-line JSON into a box sized for six indented
                // ones, which looked like a large empty box with two wrapped lines in it.
                area.SetValue(JsonText(setting));
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

    // Sliders are integer-only, so a float setting is carried multiplied up and divided back.
    private const int FloatScale = 100;

    /// <summary>
    /// How far a float setting is multiplied to ride an integer slider. Hundredths are right
    /// for the usual small range, and absurd for a wide one: a percentage from 0 to 100 became
    /// a ten thousand step slider, which is forty units per pixel and stores 43.27 when the
    /// player meant 43. Coarsen only once the step count stops meaning anything.
    ///
    /// A declared step is the author's own answer to that question, and it has to be
    /// representable: at a scale too coarse for it the step rounds to nothing and the widget
    /// clamps it to one unit of whatever scale was picked instead, so a 0 to 100 range with
    /// a step of 0.01 could no longer land on 43.27. The range heuristic is for the case
    /// where nothing was declared.
    /// </summary>
    private static int ScaleFor(ConfigSetting setting)
    {
        if (setting.SettingType != ConfigSettingType.Float) return 1;

        Validation? validation = setting.Validation;
        if (validation?.Step != null && validation.Step.AsFloat() > 0) return ScaleForStep(validation.Step.AsFloat());
        if (validation?.Minimum == null || validation.Maximum == null) return FloatScale;

        float span = validation.Maximum.AsFloat() - validation.Minimum.AsFloat();
        if (span <= 0) return FloatScale;

        if (span * FloatScale <= 2000) return FloatScale;
        return span * 10 <= 2000 ? 10 : 1;
    }

    /// <summary>The coarsest scale at which a step is a whole number of slider units.</summary>
    private static int ScaleForStep(float step)
    {
        foreach (int scale in new[] { 1, 10, FloatScale })
        {
            float scaled = step * scale;
            if (scaled >= 1 && Math.Abs(scaled - Math.Round(scaled)) < 0.001f) return scale;
        }

        // Finer than hundredths was never carried; the slider stays as fine as it gets.
        return FloatScale;
    }

    /// <summary>The real value behind a slider position, as the readout beside it would write it.</summary>
    private static string SliderText(ConfigSetting setting, int value)
    {
        // The tooltip that follows a drag reads a slider position rather than the stored
        // value, so it formats the number it was handed instead of going through Formatted.
        if (setting.SettingType != ConfigSettingType.Float)
        {
            return Format(setting, value, () => value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        float real = value / (float)ScaleFor(setting);
        return Format(setting, real, () => real.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// One number through the author's [DisplayFormat], with the raw text as the answer
    /// whenever that does not work out.
    ///
    /// Two ways it does not. A malformed standard format throws. Worse, one that is merely
    /// wrong does not: .NET reads any unrecognised string as a *custom* format, in which a
    /// letter that is not a specifier stands for itself - so a typo'd format renders the
    /// number as prose, silently, with no digits left in it. That is the check below: a
    /// formatted number with no digit in it is not a formatted number.
    /// </summary>
    private static string Format<T>(ConfigSetting setting, T value, Func<string> fallback) where T : IFormattable
    {
        if (setting.Format is not { Length: > 0 } format) return fallback();

        try
        {
            string text = value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

            return text.Any(char.IsDigit) ? text : fallback();
        }
        catch (FormatException)
        {
            return fallback();
        }
    }

    private static int ToSliderInt(ConfigSetting setting, JsonObject value)
        => setting.SettingType == ConfigSettingType.Float
            ? (int)Math.Round(value.AsFloat() * ScaleFor(setting))
            : value.AsInt();

    private static bool OnSlider(ConfigSetting setting, int value)
    {
        setting.Value = setting.SettingType == ConfigSettingType.Float
            ? FromFloat(value / (float)ScaleFor(setting))
            : FromInt(value);
        return true;
    }

    private static void OnNumberTyped(ConfigSetting setting, string text)
    {
        // An empty box on a nullable setting is null, which is the only way a player has of
        // putting one back once they have typed a number over it.
        if (setting.Nullable && string.IsNullOrWhiteSpace(text))
        {
            setting.Value = FromNull();
            return;
        }

        if (setting.SettingType == ConfigSettingType.Float)
        {
            if (float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float parsed))
            {
                setting.Value = FromFloat(parsed);
            }
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

    /// <summary>The "no value" a nullable setting can be put back to.</summary>
    private static JsonObject FromNull() => new(JValue.CreateNull());
    private static JsonObject FromInt(int value) => new(new JValue(value));
    private static JsonObject FromFloat(float value) => new(new JValue(value));
    private static JsonObject FromString(string value) => new(new JValue(value));

    // ------------------------------------------------------------------ container screens

    /// <summary>
    /// Header for a container screen: where you are, the way back, and a filter. A
    /// dictionary keyed by block code routinely runs to hundreds of entries, so the filter
    /// is not a refinement.
    /// </summary>
    private void AddContainerHeader(GuiComposer composer, ElementBounds row)
    {
        composer.AddSmallButton("Back", OnBack, ElementBounds.Fixed(0, row.fixedY, 70, 28), EnumButtonStyle.Small);

        composer.AddStaticText(Breadcrumb(), CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(80, row.fixedY + 4, DialogWidth - 320, 26));

        AddFilterField(composer, row.fixedY);
    }

    /// <summary>
    /// The filter, on whichever screen. Committed rather than live: recomposing on every
    /// keystroke takes the focus off the field the player is typing into.
    /// </summary>
    private void AddFilterField(GuiComposer composer, double y)
    {
        CommittingTextInput filter = new(capi,
            ElementBounds.Fixed(DialogWidth - 230, y, 230, 28),
            text => SetFilter(text),
            CairoFont.WhiteDetailText());

        filter.SetPlaceHolderText("filter");
        composer.AddInteractiveElement(filter, "filter");
        _afterCompose.Add(() => filter.SetValue(_filter));
    }

    private string Breadcrumb()
        => string.Join("  >  ", new[] { DisplayName(_domain) }.Concat(_stack.Select(frame => frame.Crumb)));

    /// <summary>
    /// One row per entry: its key, its value, and a way to remove it. A value that is itself
    /// a container or an object opens another screen, which is what makes a dictionary of
    /// dictionaries cost nothing - it is this method again, one level down.
    /// </summary>
    private double LayoutEntries(GuiElementContainer? container)
    {
        ContainerFrame frame = _stack[^1];

        // An object has a fixed shape: its fields are not entries, they cannot be renamed,
        // removed or added to, and each one has a control of its own. Running it through the
        // collection layout turned every field name into an editable key with a delete button
        // beside it, which is nonsense.
        return frame.Node.Kind == SchemaKind.Object
            ? LayoutObjectRows(container, frame)
            : LayoutCollectionRows(container, frame);
    }

    /// <summary>One row per field of a fixed-shape object, the same shape as the root screen.</summary>
    private double LayoutObjectRows(GuiElementContainer? container, ContainerFrame frame)
    {
        double y = 4;
        int index = 0;

        if (container != null) { _widgets.Clear(); _sliderValues.Clear(); }

        JToken? token = Subtree.Navigate(frame.Setting.Value.Token, frame.Path);

        // A fresh instance of the class is exactly its defaults - the field initialisers the
        // author wrote. An entry inside a dictionary has no default of its own, which is why
        // this is offered here and not on the collection screens.
        JObject? defaults = Defaults(frame.Node.MemberType);

        foreach (SchemaNode child in frame.Node.Children)
        {
            if (child.Hidden || (!child.IsSetting && child.Kind != SchemaKind.Object)) continue;

            string label = child.Label ?? SchemaBuilder.Humanize(child.Code);
            if (_filter.Length > 0 && !label.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;

            if (container == null)
            {
                y += RowHeight + RowGap;
                index++;
                continue;
            }

            string key = $"field-{index++}";

            ElementBounds controlBounds = ElementBounds.Fixed(LabelWidth + 16, y, ControlWidth, RowHeight - 4);
            ElementBounds labelBounds = OnBaseline(label, CairoFont.WhiteSmallText(),
                Baseline(CairoFont.WhiteSmallText(), y, controlBounds.fixedHeight), 0, LabelWidth);

            container.Add(new GuiElementDynamicText(capi, label, CairoFont.WhiteSmallText(), labelBounds));

            if (!string.IsNullOrEmpty(child.Comment))
            {
                container.Add(new GuiElementHoverText(capi, child.Comment,
                    CairoFont.WhiteDetailText(), 320, labelBounds.FlatCopy()));
            }

            JToken value = (token is JObject holder ? holder[child.Code] : null)
                ?? KeyGenerator.BlankValue(child);

            AddEntryValue(container, frame, child.Code, label, value, child, controlBounds, key);

            if (!frame.Locked && defaults?[child.Code] is JToken fieldDefault)
            {
                AddFieldResetButton(container, frame, child.Code, fieldDefault, y);
            }

            y += RowHeight + RowGap;
        }

        return y + 6;
    }

    private static JObject? Defaults(Type type)
    {
        try
        {
            object? blank = Activator.CreateInstance(type);
            return blank == null ? null : JToken.FromObject(blank) as JObject;
        }
        catch (Exception)
        {
            // No parameterless constructor, or a member Newtonsoft will not serialise. Then
            // there is no default to offer, and no button.
            return null;
        }
    }

    /// <summary>
    /// Puts one field of an object entry back to the value its class declares, in the same
    /// column the root screen's Reset sits in.
    /// </summary>
    private void AddFieldResetButton(GuiElementContainer container, ContainerFrame frame, string field, JToken value, double y)
    {
        ElementBounds bounds = ElementBounds.Fixed(
            LabelWidth + 16 + ControlWidth + ResetGap, y, ResetWidth, RowHeight - 4);

        container.Add(new GuiElementTextButton(capi, "Reset",
            CairoFont.WhiteDetailText(),
            CairoFont.WhiteDetailText().WithColor(GuiStyle.ActiveButtonTextColor),
            () => OnResetField(frame, field, value),
            bounds, EnumButtonStyle.Small));

        container.Add(new GuiElementHoverText(capi,
            $"Restore this to {value.ToString(Newtonsoft.Json.Formatting.None)}",
            CairoFont.WhiteDetailText(), 260, bounds.FlatCopy()));
    }

    private bool OnResetField(ContainerFrame frame, string field, JToken value)
    {
        Subtree.SetValue(frame.Setting, frame.Path, field, value.DeepClone());
        Recompose();
        return true;
    }

    private double LayoutCollectionRows(GuiElementContainer? container, ContainerFrame frame)
    {
        double y = 4;
        int index = 0;

        if (container != null) { _widgets.Clear(); _sliderValues.Clear(); }

        JToken? token = Subtree.Navigate(frame.Setting.Value.Token, frame.Path);
        SchemaNode? valueSchema = frame.Node.Kind == SchemaKind.Dictionary ? frame.Node.ValueNode : frame.Node.ElementNode;

        foreach ((object step, string label, JToken value) in Entries(token, frame.Node))
        {
            if (_filter.Length > 0 && !label.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;

            if (container == null)
            {
                y += RowHeight + RowGap;
                index++;
                continue;
            }

            string key = $"entry-{index++}";

            ElementBounds keyBounds = ElementBounds.Fixed(0, y, KeyWidth, RowHeight - 4);
            ElementBounds valueBounds = ElementBounds.Fixed(KeyWidth + EntryGap, y, EntryValueWidth, RowHeight - 4);
            ElementBounds deleteBounds = ElementBounds.Fixed(
                KeyWidth + EntryGap + EntryValueWidth + EntryGap, y, DeleteWidth, RowHeight - 4);

            AddEntryKey(container, frame, step, label, keyBounds);
            AddEntryValue(container, frame, step, label, value, valueSchema, valueBounds, key);

            if (frame.Node.Kind == SchemaKind.List && value.Type == JTokenType.String)
            {
                AddCodeMark(container, frame.Node.KeySource, value.Value<string>() ?? "", keyBounds);
            }

            if (!frame.Locked)
            {
                object removed = step;
                container.Add(new GuiElementTextButton(capi, "x",
                    CairoFont.WhiteDetailText(), CairoFont.WhiteDetailText().WithColor(GuiStyle.ActiveButtonTextColor),
                    () => OnRemoveEntry(frame, removed),
                    deleteBounds, EnumButtonStyle.Small));
            }

            y += RowHeight + RowGap;
        }

        y += 6;

        if (container != null && !frame.Locked)
        {
            AddAddButton(container, frame, token, y);
        }

        return y + RowHeight;
    }

    private static IEnumerable<(object step, string label, JToken value)> Entries(JToken? token, SchemaNode node)
    {
        if (node.Kind == SchemaKind.Object)
        {
            foreach (SchemaNode child in node.Children)
            {
                if (child.Hidden || (!child.IsSetting && child.Kind != SchemaKind.Object)) continue;

                JToken value = (token is JObject holder ? holder[child.Code] : null)
                    ?? KeyGenerator.BlankValue(child);

                yield return (child.Code, child.Label ?? SchemaBuilder.Humanize(child.Code), value);
            }

            yield break;
        }

        switch (token)
        {
            case JObject o:
                foreach (JProperty property in o.Properties())
                {
                    yield return (property.Name, property.Name, property.Value);
                }
                break;

            case JArray array:
                for (int index = 0; index < array.Count; index++)
                {
                    yield return (index, ElementLabel(array[index], index, node.LabelMember), array[index]);
                }
                break;
        }
    }

    /// <summary>
    /// What a list row says. Falls back through the element's [Key] member, then its first
    /// string, then the index - so an author annotates only when the guess is wrong.
    /// </summary>
    private static string ElementLabel(JToken value, int index, string? labelMember)
    {
        if (labelMember != null && value is JObject o && o[labelMember] is JToken label)
        {
            string text = label.ToString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        return $"#{index}";
    }

    private void AddEntryKey(GuiElementContainer container, ContainerFrame frame, object step, string label, ElementBounds bounds)
    {
        AddCodeMark(container, frame.Node.KeySource, label, bounds);

        // A list index is not editable, and neither is anything on a server-owned config.
        if (frame.Locked || step is not string existing)
        {
            container.Add(new GuiElementDynamicText(capi, label, CairoFont.WhiteDetailText(), bounds));
            return;
        }

        CommittingTextInput input = new(capi, bounds,
            text => OnRenameKey(frame, existing, text.Trim()),
            CairoFont.WhiteDetailText());

        string? placeholder = CodeHints.Placeholder(frame.Node.KeySource);
        if (placeholder != null) input.SetPlaceHolderText(placeholder);

        container.Add(input);
        _afterCompose.Add(() => input.SetValue(existing));
    }

    /// <summary>
    /// Marks a code that names nothing the game has loaded. A mistyped block code is the
    /// commonest way a structured config quietly does nothing - the entry looks right in the
    /// file and simply never matches - and nothing in the game says so today.
    ///
    /// Only ever marks a definite "no". A member with no [DataType], or a registry that has
    /// not loaded, gets no opinion rather than a warning.
    /// </summary>
    private void AddCodeMark(GuiElementContainer container, string? dataType, string code, ElementBounds row)
    {
        if (CodeHints.Resolves(capi, dataType, code) != false) return;

        ElementBounds bounds = ElementBounds.Fixed(
            KeyWidth + EntryGap + EntryValueWidth + EntryGap + DeleteWidth + 6,
            row.fixedY + 2, MarkWidth, row.fixedHeight);

        container.Add(new GuiElementDynamicText(capi, "!",
            CairoFont.WhiteDetailText().WithColor(GuiStyle.ErrorTextColor), bounds));

        container.Add(new GuiElementHoverText(capi,
            $"No {CodeHints.Describe(dataType)} matches '{code}'. It will have no effect. Wildcards like 'game:door-*' are fine.",
            CairoFont.WhiteDetailText(), 300, bounds.FlatCopy()));
    }

    private void AddEntryValue(GuiElementContainer container, ContainerFrame frame, object step, string label,
        JToken value, SchemaNode? schema, ElementBounds bounds, string key)
    {
        bool opens = schema != null
            && schema.Kind is SchemaKind.Object or SchemaKind.Dictionary or SchemaKind.List;

        if (opens)
        {
            List<object> path = new(frame.Path) { step };
            container.Add(new GuiElementTextButton(capi, NestedButtonText(value, schema!) + "  >",
                CairoFont.WhiteDetailText(), CairoFont.WhiteDetailText().WithColor(GuiStyle.ActiveButtonTextColor),
                () => OpenNested(frame, path, schema!, label),
                bounds, EnumButtonStyle.Small));
            return;
        }

        if (frame.Locked || schema == null)
        {
            container.Add(new GuiElementDynamicText(capi, value.ToString(), CairoFont.WhiteDetailText(), bounds));
            return;
        }

        AddEntryControl(container, frame, step, value, schema, bounds, key);
    }

    private static string NestedButtonText(JToken value, SchemaNode schema) => schema.Kind switch
    {
        SchemaKind.Object => "edit",
        _ => value switch
        {
            JObject o => o.Count == 1 ? "1 entry" : $"{o.Count} entries",
            JArray a => a.Count == 1 ? "1 entry" : $"{a.Count} entries",
            _ => "empty"
        }
    };

    /// <summary>A control for one scalar sitting inside a container, bound to its place in the subtree.</summary>
    private void AddEntryControl(GuiElementContainer container, ContainerFrame frame, object step,
        JToken value, SchemaNode schema, ElementBounds bounds, string key)
    {
        Type type = Nullable.GetUnderlyingType(schema.MemberType) ?? schema.MemberType;

        if (type.IsEnum)
        {
            string[] names = Enum.GetNames(type);
            int selected = Math.Max(0, Array.IndexOf(names, value.ToString()));

            Remember(key, container, new GuiElementDropDown(capi, names, names, selected,
                (code, on) => { if (on) Subtree.SetValue(frame.Setting, frame.Path, step, new JValue(code)); },
                bounds, CairoFont.WhiteDetailText(), false));
            return;
        }

        switch (schema.ScalarType)
        {
            case ConfigSettingType.Boolean:
            {
                GuiElementSwitch toggle = new(capi,
                    on => Subtree.SetValue(frame.Setting, frame.Path, step, new JValue(on)), bounds);
                Remember(key, container, toggle);
                bool on = value.Type == JTokenType.Boolean && value.Value<bool>();
                _afterCompose.Add(() => toggle.On = on);
                break;
            }

            case ConfigSettingType.Integer:
            case ConfigSettingType.Float:
            {
                bool wholeNumbers = schema.ScalarType == ConfigSettingType.Integer;
                GuiElementNumberInput number = new(capi, bounds,
                    text => OnEntryNumberTyped(frame, step, text, wholeNumbers), CairoFont.WhiteDetailText());
                Remember(key, container, number);
                string text = value.ToString();
                _afterCompose.Add(() => number.SetValue(text));
                break;
            }

            default:
            {
                GuiElementTextInput input = new(capi, bounds,
                    text => Subtree.SetValue(frame.Setting, frame.Path, step, new JValue(text)),
                    CairoFont.WhiteDetailText());
                Remember(key, container, input);

                // A list of codes describes its elements with the same attribute a dictionary
                // uses for its keys, so the hint and the mark apply here too.
                if (frame.Node.Kind == SchemaKind.List)
                {
                    string? placeholder = CodeHints.Placeholder(frame.Node.KeySource);
                    if (placeholder != null) input.SetPlaceHolderText(placeholder);
                }

                string text = value.Type == JTokenType.String ? value.Value<string>() ?? "" : value.ToString();
                _afterCompose.Add(() => input.SetValue(text));
                break;
            }
        }
    }

    private static void OnEntryNumberTyped(ContainerFrame frame, object step, string text, bool wholeNumbers)
    {
        if (wholeNumbers)
        {
            if (int.TryParse(text, out int parsed)) Subtree.SetValue(frame.Setting, frame.Path, step, new JValue(parsed));
            return;
        }

        if (float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float value))
        {
            Subtree.SetValue(frame.Setting, frame.Path, step, new JValue(value));
        }
    }

    /// <summary>
    /// Add, or a line saying why not. A dictionary keyed by a three member enum that already
    /// holds three entries has nowhere to put a fourth, and a button that silently does
    /// nothing is the worst of the available answers.
    /// </summary>
    private void AddAddButton(GuiElementContainer container, ContainerFrame frame, JToken? token, double y)
    {
        ElementBounds bounds = ElementBounds.Fixed(0, y, 150, RowHeight - 4);

        if (frame.Node.Kind == SchemaKind.Dictionary)
        {
            JObject existing = token as JObject ?? new JObject();

            if (!KeyGenerator.TryGenerate(existing, frame.Node.KeyNode, out _, out string reason))
            {
                container.Add(new GuiElementDynamicText(capi, $"Cannot add: {reason}",
                    CairoFont.WhiteDetailText().WithColor(GuiStyle.ColorParchment),
                    ElementBounds.Fixed(0, y + 4, DialogWidth - 40, RowHeight)));
                return;
            }
        }

        container.Add(new GuiElementTextButton(capi, "+ Add entry",
            CairoFont.WhiteDetailText(), CairoFont.WhiteDetailText().WithColor(GuiStyle.ActiveButtonTextColor),
            () => OnAddEntry(frame), bounds, EnumButtonStyle.Small));
    }

    // ------------------------------------------------------------------ container actions

    private bool OpenContainer(ConfigSetting setting, SchemaNode node, string crumb, bool locked)
    {
        _stack.Add(new ContainerFrame
        {
            Setting = setting,
            Node = node,
            Path = new List<object>(),
            Crumb = crumb,
            Locked = locked
        });

        _filter = "";
        Recompose();
        return true;
    }

    private bool OpenNested(ContainerFrame frame, List<object> path, SchemaNode node, string crumb)
    {
        _stack.Add(new ContainerFrame
        {
            Setting = frame.Setting,
            Node = node,
            Path = path,
            Crumb = crumb,
            Locked = frame.Locked
        });

        _filter = "";
        Recompose();
        return true;
    }

    private bool OnBack()
    {
        if (_stack.Count > 0) _stack.RemoveAt(_stack.Count - 1);
        _filter = "";
        Recompose();
        return true;
    }

    private bool OnRemoveEntry(ContainerFrame frame, object step)
    {
        Subtree.Remove(frame.Setting, frame.Path, step);
        Recompose();
        return true;
    }

    private bool OnAddEntry(ContainerFrame frame)
    {
        JToken? token = Subtree.Navigate(frame.Setting.Value.Token, frame.Path);
        SchemaNode? valueSchema = frame.Node.Kind == SchemaKind.Dictionary ? frame.Node.ValueNode : frame.Node.ElementNode;

        string? key = null;
        if (frame.Node.Kind == SchemaKind.Dictionary)
        {
            JObject existing = token as JObject ?? new JObject();
            if (!KeyGenerator.TryGenerate(existing, frame.Node.KeyNode, out key, out _)) return true;
        }

        Subtree.Add(frame.Setting, frame.Path, key, KeyGenerator.BlankValue(valueSchema));
        Recompose();
        return true;
    }

    private void OnRenameKey(ContainerFrame frame, string from, string to)
    {
        if (from == to) return;

        if (!Subtree.Rename(frame.Setting, frame.Path, from, to))
        {
            capi.TriggerIngameError(this, "duplicate-key",
                string.IsNullOrWhiteSpace(to)
                    ? "A key cannot be blank."
                    : $"There is already an entry called '{to}'.");
        }

        Recompose();
    }

    /// <summary>Rebuild the screen and push current values back into it.</summary>
    private void Recompose()
    {
        Compose();
        LoadControlValues();
    }

    // ------------------------------------------------------------------ actions

    private void OnDomainSelected(string domain, bool selected)
    {
        if (!selected || domain == _domain) return;

        _domain = domain;
        _stack.Clear();
        _openSection = null;
        _filter = "";
        _confirmDefaults = false;
        Recompose();
    }

    private void OnScroll(float value)
    {
        if (_contentBounds == null) return;

        _contentBounds.fixedY = -value;
        _contentBounds.CalcWorldBounds();
    }

    private bool OnSave()
    {
        _confirmDefaults = false;
        _configs[_domain].WriteToFile();
        capi.TriggerIngameError(this, "saved", $"Saved settings for {DisplayName(_domain)}.");
        return true;
    }

    private bool OnReload()
    {
        _configs[_domain].ReadFromFile();
        _confirmDefaults = false;
        Recompose();
        return true;
    }

    /// <summary>
    /// Throws away every setting for this mod, from wherever the player happens to be
    /// standing - so it asks first. Two clicks, not a modal: a modal over a modal in this
    /// GUI is more trouble than the confirmation is worth.
    /// </summary>
    private bool OnDefaults()
    {
        if (!_confirmDefaults)
        {
            _confirmDefaults = true;
            Recompose();
            return true;
        }

        _confirmDefaults = false;
        _configs[_domain].RestoreToDefaults();
        Recompose();
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
