using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Writing settings to disk, reading them back, and noticing when someone edits the file
/// by hand. The file watcher broke twice during this project and had no test either time;
/// the only symptom was that editing a config quietly stopped doing anything.
/// </summary>
public class ReloadAndPersistenceTests
{
    private static Config NewConfig(string domain, string definition)
        => new(Capi, domain, domain, new JsonObject(JToken.Parse(definition)));

    private const string Definition = @"{
        ""version"": 1,
        ""settings"": [
            { ""type"": ""integer"", ""code"": ""count"", ""nameInGui"": ""Count"", ""default"": 5 },
            { ""type"": ""boolean"", ""code"": ""on"", ""nameInGui"": ""On"", ""default"": true },
            { ""type"": ""string"",  ""code"": ""tag"", ""nameInGui"": ""Tag"", ""default"": ""alpha"" }
        ]
    }";

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task AnEditedValueSurvivesWriteAndReadBack()
    {
        await OnClient();

        Config config = NewConfig("persist-roundtrip", Definition);

        config.GetSetting("count")!.Value = new JsonObject(new JValue(42));
        config.GetSetting("tag")!.Value = new JsonObject(new JValue("omega"));
        config.WriteToFile();

        // A second Config over the same domain reads the file the first one just wrote.
        Config reopened = NewConfig("persist-roundtrip", Definition);

        Assert.Equal(42, reopened.GetSetting("count")!.Value.AsInt());
        Assert.Equal("omega", reopened.GetSetting("tag")!.Value.AsString(""));
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task RestoreToDefaultsPutsEverythingBack()
    {
        await OnClient();

        Config config = NewConfig("persist-defaults", Definition);

        config.GetSetting("count")!.Value = new JsonObject(new JValue(99));
        config.GetSetting("on")!.Value = new JsonObject(new JValue(false));

        config.RestoreToDefaults();

        Assert.Equal(5, config.GetSetting("count")!.Value.AsInt());
        Assert.True(config.GetSetting("on")!.Value.AsBool(), "boolean was not restored");
    }

    /// <summary>
    /// The live-reload path: something outside the game edits the YAML, and the config
    /// picks it up. This is what the shared file watcher exists for.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task EditingTheFileOnDiskReachesTheRunningConfig()
    {
        await OnClient();

        Config config = NewConfig("persist-watch", Definition);
        Assert.Equal(5, config.GetSetting("count")!.Value.AsInt());

        string path = config.ConfigFilePath;
        Assert.True(File.Exists(path), $"config file was never written: {path}");

        string edited = File.ReadAllText(path).Replace("count: 5", "count: 17");
        Assert.NotEqual(edited, File.ReadAllText(path), "could not find 'count: 5' to edit");
        File.WriteAllText(path, edited);

        // The watcher marks the file dirty; a tick listener does the reading, so this is a
        // wait on game ticks rather than on wall-clock.
        // Until fails the test itself if the condition never holds.
        await Until(() => config.GetSetting("count")!.Value.AsInt() == 17, 600,
            "the file edit to reach the running config");

        Assert.Equal(17, config.GetSetting("count")!.Value.AsInt());
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task ReadFromFileTakesWhatIsOnDisk()
    {
        await OnClient();

        Config config = NewConfig("persist-read", Definition);
        string path = config.ConfigFilePath;

        File.WriteAllText(path, File.ReadAllText(path).Replace("count: 5", "count: 8"));

        Assert.True(config.ReadFromFile(), "ReadFromFile reported failure");
        Assert.Equal(8, config.GetSetting("count")!.Value.AsInt());
    }

    /// <summary>
    /// A config file the player has broken must not cost them every setting. Today the
    /// parse throws and the constructor leaves the config empty, so this documents the
    /// current behaviour rather than the desired one.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task AMalformedFileDoesNotTakeTheGameDown()
    {
        await OnClient();

        Config config = NewConfig("persist-malformed", Definition);
        File.WriteAllText(config.ConfigFilePath, "this: is: not: valid: yaml: [[[\n");

        // Whatever it decides, it must not throw out into the caller.
        config.ReadFromFile();

        Log($"after a malformed file the config has {config.SettingCodes.Count()} setting(s)");
    }
}
