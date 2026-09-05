using System;
using System.Collections.Generic;
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
/// What the last released version did with ten real mods' configs, held against what this
/// build does with the same files.
///
/// The whole structured-config change was supposed to be additive, and three regressions in
/// flat configs got through anyway - a saved value that reverted, a slider that lost its
/// declared precision, a heading that vanished. Every test written alongside that work
/// checked that the *new* behaviour was right. None checked that the old behaviour was
/// unchanged, which is a different question and the one that mattered.
///
/// The baseline in tests/goldens was recorded by running tests/golden/CaptureGolden.cs
/// against a worktree of v1.2.0 with those mods loaded, after editing every setting off its
/// default - a config full of defaults proves nothing, because a value silently restored to
/// its default looks exactly like one that was kept.
///
/// It is self-contained: each case carries the mod's definition and the file v1.2.0 wrote,
/// so nothing here needs those mods installed - the baseline travels as an asset of the
/// ckgolden fixture. Regenerate it only when a change to what ConfigKit reads or draws is
/// intended, and say so in the commit.
/// </summary>
[SingleplayerOnly]
public class CompatibilityTests
{
    private static JObject? _golden;

    private static JObject Golden()
    {
        if (_golden != null) return _golden;

        Vintagestory.API.Common.IAsset? asset = Capi.Assets.TryGet(
            new Vintagestory.API.Common.AssetLocation("ckgolden", "config/golden.json"));

        Assert.NotNull(asset);

        _golden = JObject.Parse(asset!.ToText());
        return _golden;
    }

    /// <summary>Rebuilds one recorded config: the definition it came from, over the file v1.2.0 left.</summary>
    private static Config Rebuild(string domain, JObject record)
    {
        string file = record["file"]!.Value<string>()!;
        string target = Path.Combine(Capi.DataBasePath, "ModConfig", file);

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, record["contents"]!.Value<string>()!);

        JsonObject definition = new(JToken.Parse(record["definition"]!.Value<string>()!));

        // Yaml mode when the definition names no file of its own, exactly as the mod system
        // decides it.
        return definition.KeyExists("file")
            ? new Config(Capi, domain, domain, definition, definition["file"].AsString())
            : new Config(Capi, domain, domain, definition);
    }

    /// <summary>
    /// Every value a player had is still the value they have. This is the one that matters:
    /// a config silently reverting on upgrade is the worst thing this library could do.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task EveryRecordedValueStillReadsTheSame()
    {
        await OnClient();

        List<string> problems = [];
        int checkedSettings = 0;

        foreach ((string domain, JToken? token) in Golden())
        {
            JObject record = (JObject)token!;
            Config config = Rebuild(domain, record);
            JObject expected = (JObject)record["settings"]!;

            foreach ((string code, JToken? want) in expected)
            {
                checkedSettings++;

                if (config.GetSetting(code) is not ConfigSetting setting)
                {
                    problems.Add($"{domain}/{code}: gone");
                    continue;
                }

                string was = want!["value"]!.Value<string>()!;
                string now = setting.Value.Token?.ToString() ?? "";

                if (now != was) problems.Add($"{domain}/{code}: {was} -> {now}");

                string wasKey = want["mappingKey"]!.Value<string>()!;
                string nowKey = setting.MappingKey ?? "";
                if (nowKey != wasKey) problems.Add($"{domain}/{code}: mapping {wasKey} -> {nowKey}");
            }
        }

        Log($"checked {checkedSettings} settings across {Golden().Count} configs");
        Assert.Equal("", string.Join("\n", problems.Take(20)));
    }

    /// <summary>
    /// And the metadata that decides what a row is: its type, whether the server owns it,
    /// whether it is on screen at all. A value that survives under a control that changed
    /// type is still a regression.
    ///
    /// Labels and tooltips are deliberately not compared. They are run through Lang, and the
    /// baseline was recorded with those mods installed, so their keys resolved; here they do
    /// not. Comparing them would measure which lang files happen to be loaded rather than
    /// anything about ConfigKit. What reads a label out of a definition is covered by
    /// ConfigDialogTests and ManagedConfigTests, which control their own definitions.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task EverySettingKeepsItsTypeAndLabel()
    {
        await OnClient();

        List<string> problems = [];

        foreach ((string domain, JToken? token) in Golden())
        {
            JObject record = (JObject)token!;
            Config config = Rebuild(domain, record);

            foreach ((string code, JToken? want) in (JObject)record["settings"]!)
            {
                if (config.GetSetting(code) is not ConfigSetting setting) continue;

                Compare(problems, domain, code, "type", want!["type"]!.Value<string>()!, setting.SettingType.ToString());
                Compare(problems, domain, code, "clientSide", want["clientSide"]!.Value<bool>().ToString(), setting.ClientSide.ToString());
                Compare(problems, domain, code, "hide", want["hide"]!.Value<bool>().ToString(), setting.Hide.ToString());

                // A label that vanished entirely is still worth catching, even though its
                // text cannot be compared here.
                bool had = want["ingui"]!.Value<string>()!.Length > 0;
                if (had && string.IsNullOrEmpty(setting.InGui)) problems.Add($"{domain}/{code}: lost its label");
            }
        }

        Assert.Equal("", string.Join("\n", problems.Take(20)));
    }

    private static void Compare(List<string> problems, string domain, string code, string what, string was, string now)
    {
        if (now != was) problems.Add($"{domain}/{code}: {what} {was} -> {now}");
    }

    /// <summary>
    /// The same rows on screen, in the same order. This is what "no visual regression" means
    /// for a flat config and it is checkable: a heading that stopped being drawn, a row that
    /// fell behind a fold, or an order that shifted all show up here. It deliberately does not
    /// compare pixels - the type got larger and a filter box appeared on purpose.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task EveryScreenDrawsTheSameRowsInTheSameOrder()
    {
        await OnClient();

        List<string> problems = [];

        foreach ((string domain, JToken? token) in Golden())
        {
            JObject record = (JObject)token!;
            Config config = Rebuild(domain, record);

            string[] was = record["rendered"]!.Select(entry => entry.Value<string>()!).ToArray();

            ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { [domain] = config });
            dialog.TryOpen();
            await Frames.Wait(2);

            string[] now = dialog.RenderedSettings
                .OrderBy(entry => Index(entry.Key))
                .Select(entry => entry.Value.YamlCode)
                .ToArray();

            dialog.TryClose();

            if (!was.SequenceEqual(now))
            {
                problems.Add($"{domain}: {was.Length} rows -> {now.Length}\n"
                           + $"  was: {string.Join(",", was.Take(12))}\n"
                           + $"  now: {string.Join(",", now.Take(12))}");
            }
        }

        Assert.Equal("", string.Join("\n", problems));
    }

    private static int Index(string key)
    {
        int dash = key.LastIndexOf('-');
        return dash >= 0 && int.TryParse(key[(dash + 1)..], out int index) ? index : 0;
    }

    /// <summary>
    /// Loading a v1.2.0 file and saving it again must not change what a later load sees.
    /// A rewrite that quietly drops or renames a key is how an upgrade eats a config one
    /// session after the upgrade, rather than during it.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task RewritingAnOldFileDoesNotChangeWhatItMeans()
    {
        await OnClient();

        List<string> problems = [];

        foreach ((string domain, JToken? token) in Golden())
        {
            JObject record = (JObject)token!;
            Config config = Rebuild(domain, record);

            config.WriteToFile();
            if (!config.ReadFromFile())
            {
                problems.Add($"{domain}: would not read back what it just wrote");
                continue;
            }

            foreach ((string code, JToken? want) in (JObject)record["settings"]!)
            {
                if (config.GetSetting(code) is not ConfigSetting setting) continue;

                string was = want!["value"]!.Value<string>()!;
                string now = setting.Value.Token?.ToString() ?? "";

                if (now != was) problems.Add($"{domain}/{code}: {was} -> {now} after a rewrite");
            }
        }

        Assert.Equal("", string.Join("\n", problems.Take(20)));
    }
}
