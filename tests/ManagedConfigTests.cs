using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// The migration guide tells authors with an ImGui settings screen to delete it and
/// annotate a plain settings class instead. This is that promise under test: nothing here
/// references a ConfigKit type, only System.ComponentModel attributes.
/// </summary>
public class ManagedConfigTests
{
    public class DemoSettings
    {
        [Description("Turns the whole thing on and off.")]
        public bool Enabled = true;

        [Description("How far the thing looks for candidates.")]
        [Range(4, 40)]
        public int SearchRadius = 12;

        [Description("Multiplier applied to the base rate.")]
        [Range(0.5, 4.0)]
        public float SpeedMultiplier = 1.5f;

        [Description("Never exceed this many, whatever the radius says.")]
        public int HardLimit = 250;

        [Description("Written on the marker.")]
        public string LabelText = "gravestone";
    }

    public enum Difficulty { Gentle = 0, Normal = 1, Brutal = 2 }

    /// <summary>
    /// The types that used to take a mod's whole registration down. The settings model has
    /// one float type and one integer type; a config class does not.
    /// </summary>
    public class AwkwardTypes
    {
        [Description("A double, not a float.")]
        public double Ratio = 0.75;

        [Description("An enum.")]
        public Difficulty Level = Difficulty.Normal;

        [Description("A long.")]
        public long BigNumber = 5_000_000_000L;

        [Description("An int field whose [DefaultValue] is an int, on a float setting.")]
        [DefaultValue(3)]
        public float Scale = 2f;
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AwkwardNumericTypesSurviveRegistration()
    {
        await OnClient();

        AwkwardTypes settings = new();
        Config config = new(Capi, "awkward", "Awkward Types", settings, "configkit-awkward.json");

        // Before the fix, an unboxing cast on the double threw and the config came back empty.
        Assert.Equal(4, config.SettingCodes.Count());


        Assert.Close(0.75f, config.GetSetting("Ratio")!.Value.AsFloat(), 0.001f);
        Assert.Equal(3f, config.GetSetting("Scale")!.Value.AsFloat());

        // An enum becomes an Integer setting with a name -> value mapping, so the GUI can
        // offer a dropdown of names. The key is what identifies the choice, not Value.
        ISetting level = config.GetSetting("Level")!;
        Assert.Equal("Normal", level.MappingKey);
        Assert.Equal(3, level.Validation!.Mapping!.Count);

        // And back onto the object, where assigning a float to a double throws just as hard.
        config.GetSetting("Ratio")!.Value = new Vintagestory.API.Datastructures.JsonObject(
            new Newtonsoft.Json.Linq.JValue(0.25f));
        config.GetSetting("Level")!.MappingKey = "Brutal";   // what the dropdown does

        config.AssignSettingsValues(settings);

        Assert.Close(0.25, settings.Ratio, 0.001);
        Assert.Equal(Difficulty.Brutal, settings.Level);
    }

    private static Config BuildManagedConfig(out DemoSettings settings)
    {
        settings = new DemoSettings();
        return new Config(Capi, "demopoco", "Demo POCO Mod", settings, "configkit-demopoco.json");
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AttributesBecomeSettings()
    {
        await OnClient();

        Config config = BuildManagedConfig(out _);

        string[] codes = config.SettingCodes.OrderBy(code => code, StringComparer.Ordinal).ToArray();
        Assert.Equal("Enabled,HardLimit,LabelText,SearchRadius,SpeedMultiplier", string.Join(",", codes));

        // [Description] is the tooltip, not the label.
        ConfigSetting enabled = (ConfigSetting)config.GetSetting("Enabled")!;
        Assert.Equal("Turns the whole thing on and off.", enabled.Comment);

        // Field defaults are the config defaults; no [DefaultValue] required.
        Assert.Equal(12, config.GetSetting("SearchRadius")!.Value.AsInt());
        Assert.Equal("gravestone", config.GetSetting("LabelText")!.Value.AsString(""));
        Assert.True(config.GetSetting("Enabled")!.Value.AsBool(), "Enabled should default to true");

        // [Range] is what turns a typed number into a slider rather than a text box.
        Validation? radius = config.GetSetting("SearchRadius")!.Validation;
        Assert.NotNull(radius);
        Assert.Equal(4, radius!.Minimum!.AsInt());
        Assert.Equal(40, radius.Maximum!.AsInt());

        Validation? speed = config.GetSetting("SpeedMultiplier")!.Validation;
        Assert.NotNull(speed);
        Assert.Close(0.5f, speed!.Minimum!.AsFloat(), 0.001f);
        Assert.Close(4.0f, speed.Maximum!.AsFloat(), 0.001f);

        // A field with no [Range] must not acquire one.
        Assert.Null(config.GetSetting("HardLimit")!.Validation?.Minimum);

        // Types are inferred from the field types, not declared anywhere.
        Assert.Equal(ConfigSettingType.Boolean, config.GetSetting("Enabled")!.SettingType);
        Assert.Equal(ConfigSettingType.Integer, config.GetSetting("SearchRadius")!.SettingType);
        Assert.Equal(ConfigSettingType.Float, config.GetSetting("SpeedMultiplier")!.SettingType);
        Assert.Equal(ConfigSettingType.String, config.GetSetting("LabelText")!.SettingType);
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task EditedValuesAreAssignedBackToTheObject()
    {
        await OnClient();

        Config config = BuildManagedConfig(out DemoSettings settings);

        config.GetSetting("SearchRadius")!.Value = new Vintagestory.API.Datastructures.JsonObject(
            new Newtonsoft.Json.Linq.JValue(31));
        config.GetSetting("Enabled")!.Value = new Vintagestory.API.Datastructures.JsonObject(
            new Newtonsoft.Json.Linq.JValue(false));

        config.AssignSettingsValues(settings);

        Assert.Equal(31, settings.SearchRadius);
        Assert.False(settings.Enabled, "Enabled should have been assigned back onto the object");
    }

    /// <summary>
    /// Several mods reach configlib by reflection rather than by reference, and look this
    /// method up by name and full signature - Weapon Out and Multi Signpost match on the
    /// six-type array below, Divine Ascension checks each parameter type in turn and logs
    /// "ConfigLib API has changed" otherwise. ConfigKit calls the method
    /// RegisterManagedConfig, so without the alias those mods silently lose their settings
    /// screen. This is the lookup they perform, verbatim.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ConfiglibsNameForRegisterManagedConfigStillResolves()
    {
        await OnClient();

        ConfigKitModSystem system = Capi.ModLoader.GetModSystem<ConfigKitModSystem>();

        System.Reflection.MethodInfo? method = system.GetType().GetMethod(
            "RegisterCustomManagedConfig",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
            null,
            new[] { typeof(string), typeof(object), typeof(string), typeof(Action), typeof(Action<string>), typeof(Action) },
            null);

        Assert.NotNull(method);

        // And it has to actually register, not just exist.
        DemoSettings settings = new();
        method!.Invoke(system, new object?[] { "aliasdemo", settings, "configkit-aliasdemo.json", null, null, null });

        IConfig? config = system.GetConfig("aliasdemo");
        Assert.NotNull(config);
        Assert.Equal(12, config!.GetSetting("SearchRadius")!.Value.AsInt());
    }

    [SingleplayerOnly]
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ManagedConfigRendersWithReadableLabels()
    {
        await OnClient();

        Config config = BuildManagedConfig(out _);
        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["demopoco"] = config });
        dialog.TryOpen();
        await Frames.Wait(10);

        Assert.True(dialog.IsOpened(), "settings window did not open");
        Assert.Equal(5, dialog.RenderedSettings.Count);

        await Shot.Take("configkit-managed");

        dialog.TryClose();
    }
}
