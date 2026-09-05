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

    /// <summary>
    /// An open upper bound must not become a slider.
    ///
    /// Found by driving TheInsanityGod's mods through ConfigKit: an unbounded maximum is
    /// idiomatic in that corpus - [Range(0, double.PositiveInfinity)] on a damage multiplier,
    /// [Range(1, int.MaxValue)] on an interval in milliseconds - and it means "no upper
    /// limit", not "a slider two billion units wide". Nothing threw: converting infinity to
    /// int saturates at int.MaxValue rather than raising, so the bad range travelled all the
    /// way into the widget and took the whole client down with it, with nothing in the log
    /// after the line announcing the config file.
    ///
    /// The range is not discarded - it still validates - the setting just gets the number
    /// input, which is the only sensible control for an open bound anyway.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task AnUnboundedRangeGetsANumberInputNotASlider()
    {
        await OnClient();

        const string definition = @"{
            ""version"": 1,
            ""settings"": [
                { ""type"": ""float"", ""code"": ""infinite"", ""nameInGui"": ""Infinite"",
                  ""default"": 1.0, ""range"": { ""min"": 0, ""max"": 1e30 } },
                { ""type"": ""integer"", ""code"": ""huge"", ""nameInGui"": ""Huge"",
                  ""default"": 1000, ""range"": { ""min"": 1, ""max"": 2147483647 } },
                { ""type"": ""float"", ""code"": ""ordinary"", ""nameInGui"": ""Ordinary"",
                  ""default"": 1.5, ""range"": { ""min"": 0.5, ""max"": 4.0 } }
            ]
        }";

        Config config = new(Capi, "openrange", "Open range", new JsonObject(JToken.Parse(definition)));
        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["openrange"] = config });
        dialog.TryOpen();
        await Frames.Wait(8);

        try
        {
            Assert.Equal("GuiElementNumberInput", dialog.ControlKindFor("infinite"));
            Assert.Equal("GuiElementNumberInput", dialog.ControlKindFor("huge"));

            // And a range that can be a slider still is one - this is the guard that stops
            // the fix from quietly turning every slider in the library into a text box.
            Assert.Equal("GuiElementSlider", dialog.ControlKindFor("ordinary"));
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// A stored null reaches the object as null, not as "".
    ///
    /// Also from that corpus: a config class with a nullable member left unset writes null
    /// faithfully to the file, but read back it was coerced to the empty string, so a mod
    /// testing <c>Code == null</c> for "not configured" silently saw a configured empty code
    /// instead. A non-nullable value type keeps the old coercion, because assigning null to
    /// an int field throws and would drop the member entirely - a worse answer than a zero.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AStoredNullStaysNullOnTheObject()
    {
        await OnClient();

        NullableSettings settings = new() { Code = "set", Count = 7, Ratio = 0.5f };
        Config config = new(Capi, "nulls", "Nulls", settings, "ck-nulls.json");

        config.GetSetting("Code")!.Value = new JsonObject(JValue.CreateNull());
        config.GetSetting("Count")!.Value = new JsonObject(JValue.CreateNull());
        config.GetSetting("Ratio")!.Value = new JsonObject(JValue.CreateNull());

        NullableSettings loaded = new() { Code = "untouched", Count = 3, Ratio = 9f };
        config.AssignSettingsValues(loaded);

        Assert.Null(loaded.Code);
        Assert.Null(loaded.Count);

        // Not nullable, so null cannot be assigned; the coercion still applies rather than
        // the member being skipped.
        Assert.Equal(0f, loaded.Ratio);
    }

    private class NullableSettings
    {
        public string? Code = "default";
        public int? Count = 1;
        public float Ratio = 1f;
    }
}
