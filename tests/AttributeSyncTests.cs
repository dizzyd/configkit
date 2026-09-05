using System.Collections.Generic;
using System.Threading.Tasks;
using ConfigKit;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// A config carrying a null can still be sent over the wire.
///
/// Reported against WearAndTear, whose part props hold nulls on purpose -
/// "MaintenanceLimit": null means "no limit". The game's attribute format has no null:
/// TreeAttribute.ToBytes walks its entries calling val.Value.GetAttributeId() with no null
/// check, and JsonObject.ToAttribute returns null for a JSON null, because a JValue holding
/// null matches none of its type checks and falls off the end.
///
/// So converting such a config produced a tree that threw NullReferenceException the moment
/// anything wrote it - which for ConfigKit is a client with controlserver editing a setting
/// and the change being sent to the server.
///
/// The null cannot be carried, so the key is left out. The config file keeps it; only this
/// event copy drops it.
/// </summary>
public class AttributeSyncTests
{
    public class WithNulls
    {
        /// <summary>The reported shape: a structure with nulls inside it.</summary>
        public Dictionary<string, JToken> PartProps = new()
        {
            ["clutch"] = JToken.Parse(@"{
                ""Code"": ""wearandtear:clutch"",
                ""MaintenanceLimit"": null,
                ""MaterialVariant"": null,
                ""AvgLifeSpanInYears"": 3.0
            }"),
        };
    }

    /// <summary>
    /// The conversion the sync path uses, on a value with nulls in it, and then the write
    /// that used to throw. Asserting on ToBytes rather than on the tree's shape: the tree
    /// looked perfectly fine, and only writing it failed.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AValueHoldingNullsCanBeWrittenToBytes()
    {
        await OnServer();

        Config config = new(Capi, "cktree", "Tree", new WithNulls(), "ck-tree.json");

        ConfigSetting setting = (ConfigSetting)config.GetSetting("PartProps")!;

        // Straight from the game's own conversion this tree contains null entries.
        TreeAttribute raw = new();
        raw["value"] = setting.Value.ToAttribute();

        bool threw = false;
        try { raw.ToBytes(); } catch (System.NullReferenceException) { threw = true; }

        Assert.True(threw, "the game's own conversion no longer produces a null entry, so this test proves nothing");

        // Through ConfigKit's, it does not.
        TreeAttribute safe = new();
        safe["value"] = Attributes.For(setting.Value)!;

        byte[] bytes = safe.ToBytes();
        Assert.True(bytes.Length > 0, "wrote nothing");

        // And it reads back as a tree with the non-null members intact.
        TreeAttribute readBack = new();
        readBack.FromBytes(bytes);

        ITreeAttribute? value = readBack.GetTreeAttribute("value");
        Assert.NotNull(value);

        ITreeAttribute? clutch = value!.GetTreeAttribute("clutch");
        Assert.NotNull(clutch);
        Assert.Equal("wearandtear:clutch", clutch!.GetAsString("Code"));

        // The nulls are absent rather than present-and-broken: there is no null to carry.
        Assert.True(!clutch.HasAttribute("MaintenanceLimit"), "a null was carried after all");
    }

    /// <summary>
    /// Editing such a setting raises the change event, which is where the write happened.
    /// Nothing may throw out of it.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task EditingASettingHoldingNullsDoesNotThrow()
    {
        await OnServer();

        WithNulls settings = new();
        Config config = new(Capi, "cktree2", "Tree", settings, "ck-tree2.json");

        config.GetSetting("PartProps")!.Value = new JsonObject(JToken.Parse(
            @"{""clutch"":{""Code"":""wearandtear:clutch"",""MaintenanceLimit"":null}}"));

        Assert.Equal(0, config.Errors.Count);

        config.WriteToFile();
        Assert.True(config.ReadFromFile(), "would not read back what it wrote");

        // The file still says null - only the attribute copy cannot carry one.
        Assert.True(config.GetSetting("PartProps")!.Value.Token!.ToString().Contains("null"),
            "the file lost the null too");
    }
}
