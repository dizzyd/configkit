#nullable enable
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// A nested object declared nullable is an optional section: null means off.
///
/// Reported by WearAndTear's author, whose sub-configs can be set to null as a whole to
/// disable what they configure. ConfigKit used to replace such a null with a fresh instance
/// at registration and write it back populated, so a player who had switched a feature off
/// by nulling its section had it switched on again, with defaults, and nothing in the log.
///
/// The same report noticed that a section's own [Description] never reached the screen.
/// </summary>
public class OptionalSectionTests
{
    public class Sails
    {
        [Description("How fast the sails turn.")]
        public float Speed = 1.5f;

        public bool Creak = true;
    }

    public class Mill
    {
        public bool Enabled = true;

        /// <summary>Everything about the sails. Null means the mill has none.</summary>
        [Description("Everything about the sails. Null means the mill has none.")]
        public Sails? Sails;

        [Description("The stones that do the grinding.")]
        public Sails? Stones = new();

        /// <summary>Unannotated and uninitialised: an author who forgot, not a switch.</summary>
        public Sails Plain = null!;
    }

    private static Config Build(Mill mill, string file)
        => new(Capi, "ckoptional", "Optional", mill, file);

    private static JObject FileJson(Config config) => JObject.Parse(File.ReadAllText(config.ConfigFilePath));

    private static void Set(Config config, string code, JToken value)
        => ((ConfigSetting)config.GetSetting(code)!).Value = new JsonObject(value);

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ANullSectionStaysNullAndGetsASwitch()
    {
        await OnClient();

        Mill mill = new();
        Config config = Build(mill, "ck-optional-null.json");

        // The author's object is left as it was: null where it said null, and an instance
        // where an unannotated member was merely never initialised.
        Assert.Null(mill.Sails, "Sails was materialised");
        Assert.NotNull(mill.Stones);
        Assert.NotNull(mill.Plain, "an unannotated null member should still get an instance");

        // Each optional section has a switch keyed by its own path, defaulting to what the
        // object held; its rows exist with the class's defaults.
        ConfigSetting sails = (ConfigSetting)config.GetSetting("Sails")!;
        Assert.Equal(ConfigSettingType.Boolean, sails.SettingType);
        Assert.False(sails.Value.AsBool(), "Sails switch should default to off");
        Assert.True(config.GetSetting("Stones")!.Value.AsBool(), "Stones switch should default to on");
        Assert.Null(config.GetSetting("Plain"), "a plain section has no switch");
        Assert.Close(config.GetSetting("Sails/Speed")!.Value.AsFloat(), 1.5, 0.001);

        // And the file says null, as the mod's own serialiser would have.
        JObject file = FileJson(config);
        Assert.Equal(JTokenType.Null, file["Sails"]!.Type, "Sails in the file");
        Assert.Equal(JTokenType.Object, file["Stones"]!.Type, "Stones in the file");
        Assert.Close(file["Stones"]!["Speed"]!.Value<double>(), 1.5, 0.001);
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task TheSwitchAttachesAndDetachesTheObject()
    {
        await OnClient();

        Mill mill = new();
        Config config = Build(mill, "ck-optional-switch.json");

        // An edit under an off section is held for it, not applied to the author's object.
        Set(config, "Sails/Speed", new JValue(3f));
        Assert.Null(mill.Sails, "editing a row must not switch the section on");

        Set(config, "Sails", new JValue(true));
        Assert.NotNull(mill.Sails, "switching on should attach an object");
        Assert.Close(mill.Sails!.Speed, 3, 0.001, "the edit made while off");

        config.WriteToFile();
        Assert.Close(FileJson(config)["Sails"]!["Speed"]!.Value<double>(), 3, 0.001);

        Set(config, "Sails", new JValue(false));
        Assert.Null(mill.Sails, "switching off should set the member back to null");

        config.WriteToFile();
        Assert.Equal(JTokenType.Null, FileJson(config)["Sails"]!.Type, "Sails in the file after switching off");

        // Off and on again keeps what was set; a switch is not a reset.
        Set(config, "Sails", new JValue(true));
        Assert.Close(mill.Sails!.Speed, 3, 0.001, "after switching back on");
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task TheFileDecidesTheSwitch()
    {
        await OnClient();

        Mill mill = new();
        Config config = Build(mill, "ck-optional-file.json");

        File.WriteAllText(config.ConfigFilePath, """
            {
              "Enabled": true,
              "Sails": { "Speed": 7.0, "Creak": false },
              "Stones": null
            }
            """);
        Assert.True(config.ReadFromFile(), "read back");

        Assert.True(config.GetSetting("Sails")!.Value.AsBool(), "Sails switch from an object in the file");
        Assert.False(config.GetSetting("Stones")!.Value.AsBool(), "Stones switch from a null in the file");
        Assert.NotNull(mill.Sails, "Sails attached from the file");
        Assert.Close(mill.Sails!.Speed, 7, 0.001);
        Assert.Null(mill.Stones, "Stones detached from the file");

        // Written back, the file keeps that shape rather than resurrecting Stones.
        config.WriteToFile();
        JObject file = FileJson(config);
        Assert.Equal(JTokenType.Null, file["Stones"]!.Type, "Stones after a round trip");
        Assert.Close(file["Sails"]!["Speed"]!.Value<double>(), 7, 0.001);
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task ASectionsDescriptionHeadsItAndAnOffSectionIsLocked()
    {
        await OnClient();

        Mill mill = new();
        Config config = Build(mill, "ck-optional-screen.json");
        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["ckoptional"] = config });
        dialog.TryOpen();
        await Frames.Wait(8);

        try
        {
            // Too tall to show unfolded, so sections open one at a time, as a player's would.
            Assert.True(dialog.ToggleSectionNamed("Sails"), "no section called Sails");
            await Frames.Wait(4);

            // The class's description is the line under its heading. The switch is a row,
            // and the rows under an off section are read-only text rather than controls.
            Assert.Contains(string.Join("\n", dialog.RenderedNotes), "Everything about the sails.");
            Assert.Equal("GuiElementSwitch", dialog.ControlKindFor("Sails"));
            Assert.Equal("", dialog.ControlKindFor("Sails/Speed"), "a row under an off section should have no control");

            Assert.True(dialog.ToggleSectionNamed("Stones"), "no section called Stones");
            await Frames.Wait(4);

            Assert.Contains(string.Join("\n", dialog.RenderedNotes), "The stones that do the grinding.");
            Assert.NotEqual("", dialog.ControlKindFor("Stones/Speed"), "a row under an on section should have a control");
        }
        finally
        {
            dialog.TryClose();
        }
    }
}
