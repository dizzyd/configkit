using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// The settings window is drawn with the game's Cairo GUI, so only a real client proves
/// anything about it: composing bounds, laying out rows and opening a GuiDialog all
/// compile no matter how wrong they are.
/// </summary>
public class ConfigDialogTests
{
    /// A definition exercising every control the dialog knows how to draw.
    private const string Definition = @"{
        ""version"": 1,
        ""settings"": [
            { ""type"": ""separator"", ""title"": ""Behaviour"", ""text"": ""How the demo mod behaves."" },
            { ""type"": ""boolean"", ""code"": ""enabled"", ""nameInGui"": ""Enabled"",
              ""default"": true, ""comment"": ""Turns the whole thing on and off."" },
            { ""type"": ""integer"", ""code"": ""radius"", ""nameInGui"": ""Search radius"",
              ""default"": 12, ""range"": { ""min"": 4, ""max"": 40 } },
            { ""type"": ""float"", ""code"": ""speed"", ""nameInGui"": ""Speed multiplier"",
              ""default"": 1.5, ""range"": { ""min"": 0.5, ""max"": 4.0 } },
            { ""type"": ""integer"", ""code"": ""limit"", ""nameInGui"": ""Hard limit (no range)"", ""default"": 250 },
            { ""type"": ""string"", ""code"": ""label"", ""nameInGui"": ""Label text"", ""default"": ""gravestone"" },
            { ""type"": ""string"", ""code"": ""mode"", ""nameInGui"": ""Mode"",
              ""default"": ""balanced"", ""values"": [ ""gentle"", ""balanced"", ""brutal"" ] },
            { ""type"": ""color"", ""code"": ""markerColor"", ""nameInGui"": ""Marker colour"",
              ""default"": ""#4FBFA8"", ""comment"": ""Hex colour of the marker."" },
            { ""type"": ""color"", ""code"": ""brokenColor"", ""nameInGui"": ""Colour (invalid value)"",
              ""default"": ""not-a-colour"" }
        ]
    }";

    private static ConfigDialog OpenDemoDialog()
    {
        JsonObject json = new(JToken.Parse(Definition));
        Config config = new(Capi, "demo", "Demo Mod", json);

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["demo"] = config });
        dialog.TryOpen();
        return dialog;
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task SettingsWindowOpens()
    {
        await OnClient();

        ConfigDialog dialog = OpenDemoDialog();
        await Frames.Wait(10);

        Assert.True(dialog.IsOpened(), "settings window did not open");
        await Shot.Take("configkit-settings");

        dialog.TryClose();
    }

    [SingleplayerOnly]
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task EverySettingGetsAControl()
    {
        await OnClient();

        ConfigDialog dialog = OpenDemoDialog();
        await Frames.Wait(10);

        // Eight settings; the separator is formatting and must not claim a row of its own.
        Assert.Equal(8, dialog.RenderedSettings.Count);

        string[] codes = dialog.RenderedSettings.Values.Select(setting => setting.YamlCode).OrderBy(code => code).ToArray();
        Assert.Equal("brokenColor,enabled,label,limit,markerColor,mode,radius,speed", string.Join(",", codes));

        dialog.TryClose();
    }

    /// <summary>
    /// A float rides the integer slider multiplied up, so the slider's own number is not the
    /// setting's. Better Ruins showed 10000 on a slider whose readout said 100.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task ASliderReportsTheSettingsValueNotItsOwn()
    {
        await OnClient();

        const string definition = @"{
            ""version"": 1,
            ""settings"": [
                { ""type"": ""float"", ""code"": ""percent"", ""nameInGui"": ""Percent"",
                  ""default"": 100, ""range"": { ""min"": 0, ""max"": 100 } },
                { ""type"": ""float"", ""code"": ""narrow"", ""nameInGui"": ""Narrow"",
                  ""default"": 1.5, ""range"": { ""min"": 0.5, ""max"": 4.0 } }
            ]
        }";

        Config config = new(Capi, "sliders", "Sliders", new JsonObject(JToken.Parse(definition)));
        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["sliders"] = config });
        dialog.TryOpen();
        await Frames.Wait(8);

        // What the player reads beside each slider.
        Assert.Equal("100", dialog.SliderValueTexts["percent"]);
        Assert.Equal("1.5", dialog.SliderValueTexts["narrow"]);

        // A wide range is not worth hundredths: 0 to 100 at 100x is a ten thousand step
        // slider that stores 43.27 when the player meant 43.
        Assert.True(dialog.SliderStepsFor("percent") <= 2000,
            $"a wide range kept absurd granularity: {dialog.SliderStepsFor("percent")} steps");

        // A narrow one keeps them, because there it is the whole point.
        Assert.True(dialog.SliderStepsFor("narrow") > 300,
            $"a narrow range lost its precision: {dialog.SliderStepsFor("narrow")} steps");

        dialog.TryClose();
    }

    /// <summary>
    /// The swatch is drawn from the hex string, so parsing is what decides whether a player
    /// sees their colour or a struck-through box.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ColourHexParsingHandlesTheFormsPeopleWrite()
    {
        await OnClient();

        Assert.True(ConfigDialog.TryParseHex("#4FBFA8", out double r, out double g, out double b));
        Assert.Close(0.310, r, 0.01);
        Assert.Close(0.749, g, 0.01);
        Assert.Close(0.659, b, 0.01);

        Assert.True(ConfigDialog.TryParseHex("4FBFA8", out _, out _, out _), "bare hex, no hash");
        Assert.True(ConfigDialog.TryParseHex("#FF4FBFA8", out _, out _, out _), "with alpha");
        Assert.True(ConfigDialog.TryParseHex("  #4fbfa8  ", out _, out _, out _), "padded and lowercase");

        Assert.False(ConfigDialog.TryParseHex("not-a-colour", out _, out _, out _));
        Assert.False(ConfigDialog.TryParseHex("#4FBF", out _, out _, out _), "too short");
        Assert.False(ConfigDialog.TryParseHex(null, out _, out _, out _));
    }

    /// <summary>
    /// Sliders are integer-only, so a float setting rides at 100x. The readout beside one
    /// has to show the setting's value rather than the slider's: a speed of 1.5 must read
    /// "1.5", never "150".
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    // Joined to a server, this setting is server-controlled and renders as read-only text
    // with no slider behind it, so there is no readout to look up. Pre-existing; the test
    // has always errored in the two-process tier rather than skipping.
    [SingleplayerOnly]
    public async Task SliderReadoutsShowTheSettingsOwnValue()
    {
        await OnClient();

        ConfigDialog dialog = OpenDemoDialog();
        await Frames.Wait(10);

        IReadOnlyDictionary<string, string> texts = dialog.SliderValueTexts;

        Assert.Equal("12", texts["radius"]);
        Assert.Equal("1.5", texts["speed"]);

        // "limit" has no range, so it is a number field rather than a slider and gets no
        // readout of its own - the field already shows the number.
        Assert.False(texts.ContainsKey("limit"), "a rangeless setting should not get a slider readout");

        dialog.TryClose();
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task DefaultsSurviveARoundTripThroughTheWindow()
    {
        await OnClient();

        ConfigDialog dialog = OpenDemoDialog();
        await Frames.Wait(10);

        Config config = (Config)dialog.Configs["demo"];
        Assert.Equal(12, config.GetSetting("radius")!.Value.AsInt());
        Assert.Equal(true, config.GetSetting("enabled")!.Value.AsBool());
        Assert.Equal("balanced", config.GetSetting("mode")!.Value.AsString(""));

        dialog.TryClose();
    }

    /// <summary>
    /// "Restore defaults" at the bottom is all-or-nothing. A player who has changed six
    /// settings and wants one back needs the per-row button, and it has to leave the other
    /// five alone - including the ones sharing a control type with it.
    /// </summary>
    [SingleplayerOnly]
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ResettingOneSettingLeavesTheRestAlone()
    {
        await OnClient();

        ConfigDialog dialog = OpenDemoDialog();
        await Frames.Wait(10);

        Config config = (Config)dialog.Configs["demo"];
        ISetting radius = config.GetSetting("radius")!;
        ISetting speed = config.GetSetting("speed")!;
        ISetting label = config.GetSetting("label")!;
        ISetting enabled = config.GetSetting("enabled")!;

        radius.Value = new JsonObject(new JValue(33));
        speed.Value = new JsonObject(new JValue(3.75f));
        label.Value = new JsonObject(new JValue("headstone"));
        enabled.Value = new JsonObject(new JValue(false));
        await Frames.Wait(2);

        Assert.True(dialog.ResetSetting("radius"), "radius was not on screen to reset");
        await Frames.Wait(2);

        Assert.Equal(12, radius.Value.AsInt());

        // The other four keep the edit. A reset that quietly restored the whole config
        // would pass an assertion on radius alone.
        Assert.Close(3.75f, speed.Value.AsFloat(), 0.001f);
        Assert.Equal("headstone", label.Value.AsString(""));
        Assert.False(enabled.Value.AsBool(), "enabled should still be the edited value");

        // The readout beside the reset slider has to follow the value, or the row goes on
        // showing 33 while the config holds 12. (Speed's readout is not checked: it was
        // edited through the setting rather than the slider, so it was never in sync to
        // begin with - only a reset or a recompose pushes a value into a widget.)
        Assert.Equal("12", dialog.SliderValueTexts["radius"]);

        await Shot.Take("configkit-reset-one");
        dialog.TryClose();
    }

    /// <summary>
    /// A dropdown does not read its own setting per frame - it is composed with a selected
    /// index - so a reset has to push the new selection into it. The rest of the window
    /// reloads on recompose; this one does not.
    /// </summary>
    [SingleplayerOnly]
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ResettingADropdownMovesTheSelection()
    {
        await OnClient();

        ConfigDialog dialog = OpenDemoDialog();
        await Frames.Wait(10);

        Config config = (Config)dialog.Configs["demo"];
        ISetting mode = config.GetSetting("mode")!;

        mode.Value = new JsonObject(new JValue("brutal"));
        await Frames.Wait(2);
        Assert.Equal("brutal", mode.Value.AsString(""));

        Assert.True(dialog.ResetSetting("mode"), "mode was not on screen to reset");
        await Frames.Wait(2);

        Assert.Equal("balanced", mode.Value.AsString(""));

        dialog.TryClose();
    }

    /// <summary>
    /// An enum setting stores its default as the mapping key, not the mapped value, so the
    /// reset has to go back through MappingKey - which is also what carries the choice onto
    /// the mod's own object.
    /// </summary>
    [SingleplayerOnly]
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ResettingAMappedSettingRestoresItsKey()
    {
        await OnClient();

        ManagedConfigTests.AwkwardTypes settings = new();
        Config config = new(Capi, "resetmapped", "Reset Mapped", settings, "configkit-resetmapped.json");
        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["resetmapped"] = config });
        dialog.TryOpen();
        await Frames.Wait(10);

        ISetting level = config.GetSetting("Level")!;
        Assert.Equal("Normal", level.MappingKey);

        level.MappingKey = "Brutal";
        await Frames.Wait(2);

        Assert.True(dialog.ResetSetting("Level"), "Level was not on screen to reset");
        await Frames.Wait(2);

        Assert.Equal("Normal", level.MappingKey);

        // And the value the key maps to, since that is what reaches the mod.
        config.AssignSettingsValues(settings);
        Assert.Equal(ManagedConfigTests.Difficulty.Normal, settings.Level);

        dialog.TryClose();
    }

    /// <summary>
    /// More settings than fit, with a rangeless number setting above the sliders. That order
    /// matters: a number input leaves the GL scissor pointing at its own box and switched
    /// off, and the next tooltip switches it back on, so every slider below one used to be
    /// clipped into nothing.
    /// </summary>
    private static string TallDefinition()
    {
        System.Text.StringBuilder sb = new();
        sb.Append(@"{ ""version"": 1, ""settings"": [");
        sb.Append(@"{ ""type"": ""integer"", ""code"": ""plain"", ""nameInGui"": ""Rangeless number"", ""default"": 450 },");
        for (int i = 0; i < 30; i++)
        {
            sb.Append(@"{ ""type"": ""integer"", ""code"": ""s" + i + @""", ""nameInGui"": ""Setting " + i
                + @""", ""comment"": ""Tooltip " + i + @""", ""default"": 5, ""range"": { ""min"": 0, ""max"": 10 } },");
        }
        sb.Length--;
        sb.Append("] }");
        return sb.ToString();
    }

    /// <summary>
    /// Rows scrolled out of view must not be drawn. The stock GuiElementContainer draws every
    /// child wherever its bounds say, and several stock elements ignore InsideClipBounds, so
    /// labels and values from below the fold landed on top of the buttons and the hotbar.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task RowsBelowTheFoldAreNotDrawn()
    {
        await OnClient();

        JsonObject json = new(JToken.Parse(TallDefinition()));
        Config config = new(Capi, "tall", "Tall Mod", json);
        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["tall"] = config });
        dialog.TryOpen();
        await Frames.Wait(10);

        ClippedContainer container = (ClippedContainer)dialog.SingleComposer.GetContainer("rows");
        int total = container.Elements.Count;
        var visible = container.VisibleElements.ToList();

        Assert.True(total > visible.Count, $"nothing was culled: {total} elements all drawn");
        Assert.True(visible.Count > 0, "everything was culled");

        ElementBounds clip = container.InsideClipBounds!;
        double bottom = clip.renderY + clip.OuterHeight;
        foreach (GuiElement element in visible)
        {
            Assert.True(element.Bounds.renderY <= bottom,
                $"{element.GetType().Name} at {element.Bounds.renderY} draws past the clip at {bottom}");
        }

        dialog.TryClose();
    }
}
