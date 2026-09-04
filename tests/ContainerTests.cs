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
/// Dictionaries, lists and arrays as config members.
///
/// Every shape here is taken from a mod in the published corpus that holds it today, so
/// passing these means handling the corpus as it actually exists rather than as it might.
/// The shapes and the mods they came from are named on each test.
/// </summary>
[SingleplayerOnly]
public class ContainerTests
{
    // ---------------------------------------------------------------- fixtures

    public enum Nutrient { N = 0, P = 1, K = 2 }

    /// <summary>Dana Tweaks' CreatureOpenDoors: a dictionary value with several members.</summary>
    public class OpenDoors
    {
        public string EntityCode = "";
        public List<string> Doors = new();
        public float Chance = 0.5f;
        public bool ClosesBehind = true;
        public int Cooldown = 30;
        public string Note = "";
    }

    public class LootEntry
    {
        public string ItemCode = "";
        public int Quantity = 1;
    }

    public class TierLoot
    {
        public List<LootEntry> Pool = new();
    }

    public class Rewards
    {
        public TierLoot Easy = new();
    }

    public class Containers
    {
        public int Scalar = 7;

        /// <summary>TassHunting's ArmorBounceByMetal.</summary>
        public Dictionary<string, float> Weights = new() { ["copper"] = 1f, ["iron"] = 2f };

        /// <summary>Vanilla Variants' ChuteFlowRates - the shape nothing in the corpus handles.</summary>
        public Dictionary<string, Dictionary<string, float>> Nested = new()
        {
            ["copper"] = new() { ["in"] = 1f, ["out"] = 2f }
        };

        /// <summary>Dana Tweaks' CreaturesOpenDoors.</summary>
        public Dictionary<string, OpenDoors> Creatures = new();

        /// <summary>Hydrate or Diedrate's TransitionConfig - a non-string key.</summary>
        public Dictionary<AssetLocation, float> ByAsset = new();

        /// <summary>Balanced Thirst's UrineNutrientLevels - an enum key.</summary>
        public Dictionary<Nutrient, float> ByNutrient = new() { [Nutrient.P] = 0.25f };

        /// <summary>Thievery's LockpickRewardsConfig.Easy.Pool - a list two classes down.</summary>
        public Rewards Rewards = new();

        /// <summary>TassHunting's ApexCodes.</summary>
        public string[] Codes = ["game:wolf-male", "game:bear"];

        /// <summary>minimal compass' requiredItems.</summary>
        public HashSet<string> Required = new() { "game:compass" };

        /// <summary>Hydrate or Diedrate's and Thievery's LegacyData - the migration escape hatch.</summary>
        public Dictionary<string, JToken> LegacyData = new();

        /// <summary>Common shape that plain assignment cannot reach at all.</summary>
        public List<string> Blacklist { get; } = new() { "game:soil" };

        /// <summary>Serialised as a string by its TypeConverter, not as an object with fields.</summary>
        public AssetLocation Marker = new("game:door-oak");
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

    private static JObject FileJson(Config config) => JObject.Parse(File.ReadAllText(config.ConfigFilePath));

    /// <summary>Writes the config out, then loads it fresh onto a new object - a real round trip.</summary>
    private static Containers Reload(Config config, string domain, string file)
    {
        config.WriteToFile();

        Containers loaded = new();
        Config reopened = new(Sapi, domain + "-2", domain + "-2", loaded, file);
        reopened.AssignSettingsValues(loaded);
        return loaded;
    }

    // ---------------------------------------------------------------- the shapes

    [VsTest(TimeoutMs = 60000)]
    public async Task EveryContainerBecomesASetting()
    {
        await OnServer();

        Config config = Fresh(new Containers(), "cont-all", "configkit-cont-all.json");

        foreach (string code in new[] { "Weights", "Nested", "Creatures", "ByAsset", "ByNutrient",
                                        "Codes", "Required", "LegacyData", "Blacklist", "Rewards/Easy/Pool" })
        {
            Assert.True(config.SettingCodes.Contains(code),
                $"'{code}' is not a setting; got {string.Join(", ", config.SettingCodes)}");
        }
    }

    /// <summary>Acceptance test 1, from TassHunting.</summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ADictionaryOfScalarsRoundTrips()
    {
        await OnServer();

        Containers settings = new();
        Config config = Fresh(settings, "cont-dict", "configkit-cont-dict.json");

        config.GetSetting("Weights")!.Value = new JsonObject(JObject.Parse("{\"copper\":3.5,\"gold\":9}"));

        Containers loaded = Reload(config, "cont-dict", "configkit-cont-dict.json");

        Assert.Equal(2, loaded.Weights.Count);
        Assert.Close(3.5f, loaded.Weights["copper"], 0.001f);
        Assert.Close(9f, loaded.Weights["gold"], 0.001f);
    }

    /// <summary>
    /// Acceptance test 2, from Vanilla Variants. The corpus handles this by looping the outer
    /// dictionary in the caller and hand-writing an editor per inner one; here it is one
    /// setting and needs no special case at all.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ANestedDictionaryRoundTripsWithNoSpecialCase()
    {
        await OnServer();

        Containers settings = new();
        Config config = Fresh(settings, "cont-nested", "configkit-cont-nested.json");

        config.GetSetting("Nested")!.Value =
            new JsonObject(JObject.Parse("{\"copper\":{\"in\":4,\"out\":5},\"iron\":{\"in\":6}}"));

        Containers loaded = Reload(config, "cont-nested", "configkit-cont-nested.json");

        Assert.Equal(2, loaded.Nested.Count);
        Assert.Close(4f, loaded.Nested["copper"]["in"], 0.001f);
        Assert.Close(6f, loaded.Nested["iron"]["in"], 0.001f);
    }

    /// <summary>Acceptance test 3, from Dana Tweaks.</summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ADictionaryOfObjectsRoundTrips()
    {
        await OnServer();

        Containers settings = new();
        settings.Creatures["game:drifter-normal"] = new OpenDoors
        {
            EntityCode = "game:drifter-normal",
            Doors = { "game:door-oak" },
            Chance = 0.35f,
            ClosesBehind = false,
            Cooldown = 90,
            Note = "shoves"
        };

        Config config = Fresh(settings, "cont-obj", "configkit-cont-obj.json");
        Containers loaded = Reload(config, "cont-obj", "configkit-cont-obj.json");

        Assert.Equal(1, loaded.Creatures.Count);
        OpenDoors drifter = loaded.Creatures["game:drifter-normal"];
        Assert.Close(0.35f, drifter.Chance, 0.001f);
        Assert.False(drifter.ClosesBehind);
        Assert.Equal(90, drifter.Cooldown);
        Assert.Equal("shoves", drifter.Note);
        Assert.Equal(1, drifter.Doors.Count);
    }

    /// <summary>Acceptance test 4, from Hydrate or Diedrate. The key is not a string.</summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ADictionaryKeyedByAssetLocationRoundTrips()
    {
        await OnServer();

        Containers settings = new();
        settings.ByAsset[new AssetLocation("game:bread-spelt")] = 0.8f;

        Config config = Fresh(settings, "cont-asset", "configkit-cont-asset.json");
        Containers loaded = Reload(config, "cont-asset", "configkit-cont-asset.json");

        Assert.Equal(1, loaded.ByAsset.Count);
        Assert.Close(0.8f, loaded.ByAsset[new AssetLocation("game:bread-spelt")], 0.001f);
    }

    /// <summary>
    /// An enum key has to be stored as its member name. Stored as an ordinal, renaming or
    /// reordering a member silently moves every value onto a different one.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AnEnumKeyIsStoredByName()
    {
        await OnServer();

        Containers settings = new();
        Config config = Fresh(settings, "cont-enum", "configkit-cont-enum.json");
        config.WriteToFile();

        JObject nutrients = (JObject)FileJson(config)["ByNutrient"]!;
        Assert.True(nutrients.ContainsKey("P"), $"expected the member name as the key; got {nutrients}");

        Containers loaded = Reload(config, "cont-enum", "configkit-cont-enum.json");
        Assert.Close(0.25f, loaded.ByNutrient[Nutrient.P], 0.001f);
    }

    /// <summary>Acceptance test 5, from Thievery: a list of objects two classes down.</summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AListOfObjectsTwoClassesDownRoundTrips()
    {
        await OnServer();

        Containers settings = new();
        settings.Rewards.Easy.Pool.Add(new LootEntry { ItemCode = "game:nugget-gold", Quantity = 3 });

        Config config = Fresh(settings, "cont-list", "configkit-cont-list.json");

        Assert.True(config.SettingCodes.Contains("Rewards/Easy/Pool"),
            $"got {string.Join(", ", config.SettingCodes)}");

        Containers loaded = Reload(config, "cont-list", "configkit-cont-list.json");

        Assert.Equal(1, loaded.Rewards.Easy.Pool.Count);
        Assert.Equal("game:nugget-gold", loaded.Rewards.Easy.Pool[0].ItemCode);
        Assert.Equal(3, loaded.Rewards.Easy.Pool[0].Quantity);
    }

    /// <summary>Acceptance test 8, from TassHunting.</summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ArraysAndSetsRoundTrip()
    {
        await OnServer();

        Containers settings = new();
        Config config = Fresh(settings, "cont-array", "configkit-cont-array.json");

        config.GetSetting("Codes")!.Value = new JsonObject(JArray.Parse("[\"game:hyena\"]"));

        Containers loaded = Reload(config, "cont-array", "configkit-cont-array.json");

        Assert.Equal(1, loaded.Codes.Length);
        Assert.Equal("game:hyena", loaded.Codes[0]);
        Assert.True(loaded.Required.Contains("game:compass"), "the HashSet did not survive");
    }

    /// <summary>
    /// Acceptance test 9, from Hydrate or Diedrate and Thievery. Both carry a
    /// Dictionary&lt;string, JToken&gt; purely so a config from an older version can be read
    /// and migrated, which only works if nothing normalises it on the way through.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task SchemalessMigrationDataSurvivesUntouched()
    {
        await OnServer();

        Containers settings = new();
        settings.LegacyData["oldThirstRate"] = JToken.Parse("{\"value\":3,\"unit\":\"per-hour\"}");
        settings.LegacyData["oldFlag"] = JToken.Parse("true");

        Config config = Fresh(settings, "cont-legacy", "configkit-cont-legacy.json");
        Containers loaded = Reload(config, "cont-legacy", "configkit-cont-legacy.json");

        Assert.Equal(2, loaded.LegacyData.Count);
        Assert.Equal(3, loaded.LegacyData["oldThirstRate"]["value"]!.Value<int>());
        Assert.Equal("per-hour", loaded.LegacyData["oldThirstRate"]["unit"]!.Value<string>());
        Assert.True(loaded.LegacyData["oldFlag"]!.Value<bool>());
    }

    /// <summary>
    /// A get-only collection is a common shape and plain assignment cannot reach it at all -
    /// the old walk skipped every non-writable property. Filling it in place also keeps any
    /// reference the mod already took to the collection live.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AGetOnlyCollectionIsFilledInPlace()
    {
        await OnServer();

        Containers settings = new();
        List<string> captured = settings.Blacklist;

        Config config = Fresh(settings, "cont-getonly", "configkit-cont-getonly.json");

        config.GetSetting("Blacklist")!.Value = new JsonObject(JArray.Parse("[\"game:sand\",\"game:gravel\"]"));
        config.AssignSettingsValues(settings);

        Assert.Equal(2, settings.Blacklist.Count);
        Assert.Equal("game:sand", settings.Blacklist[0]);
        Assert.True(ReferenceEquals(captured, settings.Blacklist),
            "the collection was replaced rather than filled, so a reference the mod took is now stale");
    }

    /// <summary>
    /// Newtonsoft writes a type with a TypeConverter as a plain string. Classifying it as a
    /// nested object would flatten it into Domain and Path and break the round trip.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ATypeConverterBackedMemberIsAStringNotAnObject()
    {
        await OnServer();

        Containers settings = new();
        Config config = Fresh(settings, "cont-loc", "configkit-cont-loc.json");
        config.WriteToFile();

        Assert.Equal("game:door-oak", FileJson(config)["Marker"]!.Value<string>());

        config.GetSetting("Marker")!.Value = new JsonObject(new JValue("game:door-birch"));

        Containers loaded = Reload(config, "cont-loc", "configkit-cont-loc.json");
        Assert.Equal("game:door-birch", loaded.Marker.ToString());
    }

    /// <summary>
    /// Acceptance test 13. Config JSON in the wild carries comments and trailing commas
    /// because Json.NET accepts them; a stricter parser would report working mods as broken.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AFileWrittenLenientlyStillLoads()
    {
        await OnServer();

        const string file = "configkit-cont-lenient.json";
        Delete(file);

        File.WriteAllText(PathOf(file),
            "{\n" +
            "  // how much each metal bounces\n" +
            "  \"Weights\": { \"copper\": 3, \"iron\": 4 },\n" +
            "  \"Scalar\": 11,\n" +
            "}\n");

        Containers settings = new();
        Config config = new(Sapi, "cont-lenient", "cont-lenient", settings, file);
        config.AssignSettingsValues(settings);

        Assert.Equal(11, settings.Scalar);
        Assert.Equal(2, settings.Weights.Count);
        Assert.Close(3f, settings.Weights["copper"], 0.001f);
    }

    /// <summary>
    /// A container has to survive the server-to-client packet, which carries a setting's value
    /// as a JSON string. It was already shaped to take an arbitrary token, so nothing about
    /// the wire format changed for this - which is worth a test precisely because it would be
    /// easy to break later without noticing.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AContainerSurvivesTheSyncPacket()
    {
        await OnServer();

        Containers settings = new();
        settings.Creatures["game:wolf-male"] = new OpenDoors { EntityCode = "game:wolf-male", Cooldown = 45 };

        Config config = Fresh(settings, "cont-packet", "configkit-cont-packet.json");

        ConfigSetting original = (ConfigSetting)config.GetSetting("Creatures")!;

        // Out to the wire and back. The client keeps its own setting and takes only the
        // value across, which is what SyncFromServer does.
        ConfigSetting arrived = new ConfigSettingPacket(original);
        Assert.Equal(original.Value.Token!.ToString(), arrived.Value.Token!.ToString());

        Containers target = new();
        original.Value = arrived.Value;
        Assert.True(original.AssignSettingValue(target), "the packet's value did not assign");

        Assert.Equal(1, target.Creatures.Count);
        Assert.Equal(45, target.Creatures["game:wolf-male"].Cooldown);
    }

    /// <summary>
    /// Containers now reach the settings screen, where until the structural editor lands they
    /// render as the raw-JSON control that already existed. Ugly, but it has to compose and
    /// it has to show the value - a container that silently produced no row would be the very
    /// failure this work exists to remove.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ContainersGetARowOnTheSettingsScreen()
    {
        await OnClient();

        Containers settings = new();
        Delete("configkit-cont-gui.json");
        Config config = new(Capi, "cont-gui", "Containers", settings, "configkit-cont-gui.json");

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["cont-gui"] = config });
        dialog.TryOpen();
        await Frames.Wait(10);

        string[] rendered = dialog.RenderedSettings.Values.Select(setting => setting.YamlCode).ToArray();

        foreach (string code in new[] { "Weights", "Nested", "Creatures", "Codes", "Rewards/Easy/Pool" })
        {
            Assert.True(rendered.Contains(code),
                $"'{code}' has no row; on screen: {string.Join(", ", rendered)}");
        }

        dialog.TryClose();
    }

    /// <summary>
    /// The summary is what makes the difference from the walk this replaced visible: it says
    /// what was found, so a member that is not editable is reported rather than absent.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task TheRegistrationSummaryCountsWhatItFound()
    {
        await OnServer();

        Config config = Fresh(new Containers(), "cont-summary", "configkit-cont-summary.json");

        Assert.True(config.SchemaSummary.Contains("settings"), config.SchemaSummary);
        Assert.True(config.SchemaSummary.Contains("containers"), config.SchemaSummary);
    }
}
