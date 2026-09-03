using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using Newtonsoft.Json.Linq;
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
}
