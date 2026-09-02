using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Standing tests for bugs that were found by review rather than by failure. Each one
/// fails against the code as it was.
/// </summary>
public class RegressionTests
{
    /// <summary>
    /// Two blocks may legitimately declare the same weight. The old workaround added
    /// 1E-10f, which for any weight of 1 or more is lost entirely in float32, so the
    /// SortedDictionary threw and the whole config came back empty.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task DuplicateWeightsDoNotEmptyTheConfig()
    {
        await OnClient();

        // The object form is the one that reads "weight" from the definition; array-form
        // settings are weighted by position, so they can never collide.
        const string definition = @"{
            ""version"": 1,
            ""settings"": {
                ""integer"": {
                    ""a"": { ""default"": 1, ""weight"": 1 },
                    ""b"": { ""default"": 2, ""weight"": 1 },
                    ""c"": { ""default"": 3, ""weight"": 1 }
                }
            }
        }";

        Config config = new(Capi, "dupweight", "dupweight", new JsonObject(JToken.Parse(definition)));

        Assert.Equal(3, config.SettingCodes.Count());
        Assert.Equal(2, config.GetSetting("b")!.Value.AsInt());
    }

    /// <summary>
    /// An enum's mapping key can arrive from a server whose version of the mod renamed a
    /// member. Indexing the mapping blind threw KeyNotFoundException out of a packet
    /// handler, which on a client meant the join failed.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AnUnknownMappingKeyIsIgnoredRatherThanThrowing()
    {
        await OnClient();

        const string definition = @"{
            ""version"": 1,
            ""settings"": [
                { ""type"": ""integer"", ""code"": ""level"", ""default"": ""Normal"",
                  ""mapping"": { ""Gentle"": 0, ""Normal"": 1, ""Brutal"": 2 } }
            ]
        }";

        Config config = new(Capi, "mapkey", "mapkey", new JsonObject(JToken.Parse(definition)));
        ISetting level = config.GetSetting("level")!;

        int before = level.Value.AsInt();

        // A member this build has never heard of.
        level.MappingKey = "Apocalyptic";

        Assert.Equal(before, level.Value.AsInt());
    }

    /// <summary>
    /// A path with the "-" selector - every element of an array, the "-/value" form real
    /// mods use - was evaluated lazily, mutated mid-enumeration and then counted by
    /// re-running the selectors over tokens that had already been replaced.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AnArraySelectorPatchesEveryElement()
    {
        await OnClient();

        Vintagestory.API.Common.IAsset? asset =
            Capi.Assets.TryGet(new Vintagestory.API.Common.AssetLocation("demomod:config/arrayed.json"));
        Assert.NotNull(asset);

        JsonObject arrayed = new(JToken.Parse(asset!.ToText()));
        JsonObject[] items = arrayed["items"].AsArray();

        Assert.Equal(3, items.Length);
        foreach (JsonObject item in items) Assert.Equal(21, item["v"].AsInt());
    }
}
