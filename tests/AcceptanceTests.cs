using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// The acceptance list from the corpus survey, checked directly rather than by inference.
/// Each test names the numbered case it covers and the mod the shape came from.
/// </summary>
[SingleplayerOnly]
public class AcceptanceTests
{
    // ---------------------------------------------------------------- fixtures

    [Flags]
    public enum Sides { None = 0, North = 1, South = 2, East = 4, West = 8 }

    public class Transition
    {
        public float Rate = 1f;
        public string Note = "";
    }

    /// <summary>Case 4: a non-string key with a class for a value (Hydrate or Diedrate).</summary>
    public class AssetKeyed
    {
        public Dictionary<AssetLocation, Transition> TransitionConfig = new();
    }

    /// <summary>Case 10: enums, including a [Flags] one.</summary>
    public class Enums
    {
        public Sides Faces = Sides.North | Sides.South;
        public Sides Single = Sides.East;
    }

    public class Sub { public int Value = 1; public bool Enabled = true; }

    /// <summary>Case 6: twelve sub-configs on one root (Hydrate or Diedrate).</summary>
    public class TwelveSubConfigs
    {
        public Sub Thirst = new(); public Sub Satiety = new(); public Sub PerishRates = new();
        public Sub LiquidEncumbrance = new(); public Sub HeatAndCooling = new(); public Sub GroundWater = new();
        public Sub Rain = new(); public Sub Pump = new(); public Sub WorldGen = new();
        public Sub Containers = new(); public Sub XLib = new(); public Sub Extra = new();
    }

    /// <summary>Case 7 and 8: thirty containers and a string array on one class (Tass Universal Sync Timers).</summary>
    public class ThirtyContainers
    {
        public string[] Codes = ["game:wolf-*"];
        public Dictionary<string, float> C01 = new(); public Dictionary<string, float> C02 = new();
        public Dictionary<string, float> C03 = new(); public Dictionary<string, float> C04 = new();
        public Dictionary<string, float> C05 = new(); public Dictionary<string, float> C06 = new();
        public Dictionary<string, float> C07 = new(); public Dictionary<string, float> C08 = new();
        public Dictionary<string, float> C09 = new(); public Dictionary<string, float> C10 = new();
        public Dictionary<string, float> C11 = new(); public Dictionary<string, float> C12 = new();
        public Dictionary<string, float> C13 = new(); public Dictionary<string, float> C14 = new();
        public Dictionary<string, float> C15 = new(); public List<string> L01 = new();
        public List<string> L02 = new(); public List<string> L03 = new();
        public List<string> L04 = new(); public List<string> L05 = new();
        public List<string> L06 = new(); public List<string> L07 = new();
        public List<string> L08 = new(); public List<string> L09 = new();
        public List<string> L10 = new(); public List<string> L11 = new();
        public List<string> L12 = new(); public List<string> L13 = new();
        public List<string> L14 = new(); public List<string> L15 = new();
    }

    public class Simple
    {
        public float Rate = 1f;
        public int Count = 5;
    }

    // ---------------------------------------------------------------- helpers

    private static string PathOf(string file) => Path.Combine(Sapi.DataBasePath, "ModConfig", file);

    private static void Delete(string file)
    {
        string path = PathOf(file);
        if (File.Exists(path)) File.Delete(path);
    }

    private static Config Fresh(object settings, string domain, string file)
    {
        Delete(file);
        Config config = new(Sapi, domain, domain, settings, file);
        config.AssignSettingsValues(settings);
        return config;
    }

    // ---------------------------------------------------------------- case 4

    /// <summary>
    /// Case 4. The round trip already covered a non-string key with a scalar value; the
    /// corpus shape is a non-string key with a class for a value.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ADictionaryOfAssetLocationToClassRoundTrips()
    {
        await OnServer();

        AssetKeyed settings = new();
        settings.TransitionConfig[new AssetLocation("game:bread-spelt")] =
            new Transition { Rate = 0.4f, Note = "stales fast" };

        Config config = Fresh(settings, "acc-assetobj", "configkit-acc-assetobj.json");
        config.WriteToFile();

        AssetKeyed loaded = new();
        Config reopened = new(Sapi, "acc-assetobj2", "acc-assetobj2", loaded, "configkit-acc-assetobj.json");
        reopened.AssignSettingsValues(loaded);

        Assert.Equal(1, loaded.TransitionConfig.Count);
        Transition back = loaded.TransitionConfig[new AssetLocation("game:bread-spelt")];
        Assert.Close(0.4f, back.Rate, 0.001f);
        Assert.Equal("stales fast", back.Note);
    }

    // ---------------------------------------------------------------- case 10

    /// <summary>
    /// Case 10. A plain enum is a dropdown of its names. A [Flags] enum is a different
    /// animal: its value is a combination, which is not one of the names.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AFlagsEnumSurvivesARoundTrip()
    {
        await OnServer();

        Enums settings = new();
        Config config = Fresh(settings, "acc-flags", "configkit-acc-flags.json");
        config.WriteToFile();

        string file = File.ReadAllText(config.ConfigFilePath);

        Enums loaded = new();
        Config reopened = new(Sapi, "acc-flags2", "acc-flags2", loaded, "configkit-acc-flags.json");
        reopened.AssignSettingsValues(loaded);

        Assert.Equal(Sides.East, loaded.Single);
        Assert.True(loaded.Faces == (Sides.North | Sides.South),
            $"a combined [Flags] value did not survive: got {loaded.Faces}. File was:\n{file}");
    }

    // ---------------------------------------------------------------- case 6

    /// <summary>Case 6. Twelve sub-configs on one root, each its own section.</summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task TwelveSubConfigsEachGetTheirOwnSection()
    {
        await OnClient();

        Delete("configkit-acc-twelve.json");
        TwelveSubConfigs settings = new();
        Config config = new(Capi, "acc-twelve", "acc-twelve", settings, "configkit-acc-twelve.json");

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["acc-twelve"] = config });
        dialog.TryOpen();
        await Frames.Wait(8);

        Assert.Equal(12, dialog.Sections.Count);

        // Folded, so the screen is twelve headings rather than twenty-four rows.
        Assert.False(dialog.EverythingShown);
        Assert.Equal(0, dialog.RenderedSettings.Count);

        dialog.ToggleSectionNamed("Ground water");
        await Frames.Wait(6);
        Assert.Equal(2, dialog.RenderedSettings.Count);

        dialog.TryClose();
    }

    // ---------------------------------------------------------------- cases 7 and 8

    /// <summary>
    /// Cases 7 and 8. Thirty containers and a string array on one class: every one becomes a
    /// setting, and the screen composes without falling over.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task ThirtyContainersOnOneClassStayUsable()
    {
        await OnClient();

        Delete("configkit-acc-thirty.json");
        ThirtyContainers settings = new();
        Config config = new(Capi, "acc-thirty", "acc-thirty", settings, "configkit-acc-thirty.json");

        Assert.Equal(31, config.SettingCodes.Count());

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["acc-thirty"] = config });
        dialog.TryOpen();
        await Frames.Wait(10);

        Assert.Equal(31, dialog.RenderedSettings.Count);

        // Every one opens.
        Assert.True(dialog.OpenSetting("C07"));
        await Frames.Wait(4);
        Assert.True(dialog.Back());

        dialog.TryClose();
    }

    // ---------------------------------------------------------------- case 11

    /// <summary>
    /// Case 11. A malformed config file must not leave the mod running on defaults with
    /// nothing to say so - which is exactly what the library this replaces does.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AMalformedManagedConfigDoesNotSilentlyEmptyItself()
    {
        await OnServer();

        const string file = "configkit-acc-broken.json";
        Delete(file);
        File.WriteAllText(PathOf(file), "{ \"Rate\": 2.5, \"Count\" 7 }\n");   // missing colon

        Simple settings = new();
        Config config = new(Sapi, "acc-broken", "acc-broken", settings, file);

        Assert.Equal(2, config.SettingCodes.Count());

        config.AssignSettingsValues(settings);
        Assert.Close(1f, settings.Rate, 0.001f);      // its own default, not zero
        Assert.Equal(5, settings.Count);

        // And the file the player broke is still there to fix, not replaced by defaults.
        Assert.True(File.ReadAllText(PathOf(file)).Contains("\"Count\" 7"),
            "the malformed file was overwritten, losing whatever the player had edited");
    }

    // ---------------------------------------------------------------- case 13

    /// <summary>Case 13. Numbers written without a leading zero, as Json.NET accepts.</summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task NumbersWrittenWithoutALeadingZeroLoad()
    {
        await OnServer();

        const string file = "configkit-acc-lenient2.json";
        Delete(file);
        File.WriteAllText(PathOf(file), "{\n  \"Rate\": .01,\n  \"Count\": 3,\n}\n");

        Simple settings = new();
        Config config = new(Sapi, "acc-lenient2", "acc-lenient2", settings, file);
        config.AssignSettingsValues(settings);

        Assert.Close(0.01f, settings.Rate, 0.0001f);
        Assert.Equal(3, settings.Count);
    }
}
