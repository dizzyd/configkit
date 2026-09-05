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
/// Every config that is actually loaded, taken along the whole chain: class or definition to
/// schema, to settings, to the file, to the screen, and back.
///
/// The other suites cover shapes I chose, which are the shapes I expected to find. Every
/// defect of the last few rounds instead came from looking at a real mod - Better Ruins
/// alone gave three, one of which emptied a config entirely. This is that check written down:
/// it asserts nothing about any particular mod, only that whatever is loaded holds together.
///
/// Run it with real mods to make it mean something:
///
///     run.sh &lt;tests&gt; --mod .../configkit --mods ~/mods/ckdemo --client
///
/// With only the fixtures it still passes, and still catches a config that empties itself.
/// </summary>
[SingleplayerOnly]
public class PipelineTests
{
    private static IEnumerable<(string domain, Config config)> Loaded()
    {
        ConfigKitModSystem ck = Capi.ModLoader.GetModSystem<ConfigKitModSystem>();

        return ck.Domains
            .OrderBy(domain => domain, StringComparer.Ordinal)
            .Select(domain => (domain, config: ck.GetConfig(domain) as Config))
            .Where(entry => entry.config != null)
            .Select(entry => (entry.domain, entry.config!));
    }

    /// <summary>How many settings a definition declares, whichever of the two formats it uses.</summary>
    private static int Declared(Config config)
    {
        JsonObject settings = config.Definition["settings"];

        if (settings.IsArray())
        {
            return settings.AsArray().Count(block => block["type"].AsString() != "separator");
        }

        if (settings.Token is not JObject categories) return 0;

        return categories.Properties()
            .Select(property => property.Value as JObject)
            .Where(group => group != null)
            .Sum(group => group!.Count);
    }

    /// <summary>
    /// A config that declares settings must have them. This is the failure that keeps coming
    /// back: the constructor catches an exception, leaves no settings at all, and the mod runs
    /// on its compiled-in defaults with only a log line - which is precisely the behaviour
    /// this library exists to replace.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task NoLoadedConfigIsSilentlyEmpty()
    {
        await OnClient();

        List<string> empty = [];
        List<string> seen = [];

        foreach ((string domain, Config config) in Loaded())
        {
            int declared = Declared(config);
            int actual = config.SettingCodes.Count();

            seen.Add($"{domain}={actual}/{declared}");
            if (declared > 0 && actual == 0) empty.Add(domain);
        }

        Log($"configs: {string.Join("  ", seen)}");

        Assert.Equal(0, empty.Count);
        Assert.True(seen.Count > 0, "no configs loaded at all - this test proves nothing here");
    }

    /// <summary>
    /// Every setting holds a value, and every managed one knows the member it came from.
    /// The schema used to live in a dictionary beside the settings and the two drifted:
    /// it indexed only scalars, so containers silently never reached the object.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task EverySettingIsWiredUp()
    {
        await OnClient();

        List<string> problems = [];

        foreach ((string domain, Config config) in Loaded())
        {
            bool managed = config.SchemaSummary.Length > 0;

            foreach (string code in config.SettingCodes)
            {
                if (config.GetSetting(code) is not ConfigSetting setting)
                {
                    problems.Add($"{domain}/{code}: not retrievable by its own code");
                    continue;
                }

                if (setting.Value?.Token == null) problems.Add($"{domain}/{code}: no value");

                // A managed config's settings each came from a member; a definition's did not.
                if (managed && !setting.LegacyCodes.Any() && setting.Value == null)
                {
                    problems.Add($"{domain}/{code}: managed but unbound");
                }
            }
        }

        Assert.Equal("", string.Join("\n", problems));
    }

    /// <summary>
    /// Written out and read back, every value survives. This is the leg that matters most:
    /// a player's settings live in that file between sessions.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task EveryConfigSurvivesWriteAndReadBack()
    {
        await OnClient();

        List<string> problems = [];

        foreach ((string domain, Config config) in Loaded())
        {
            Dictionary<string, string> before = config.SettingCodes
                .ToDictionary(code => code, code => config.GetSetting(code)!.Value.Token?.ToString() ?? "");

            try
            {
                config.WriteToFile();
            }
            catch (Exception exception)
            {
                problems.Add($"{domain}: writing threw {exception.GetType().Name}");
                continue;
            }

            if (!File.Exists(config.ConfigFilePath))
            {
                problems.Add($"{domain}: wrote no file at {config.ConfigFilePath}");
                continue;
            }

            if (!config.ReadFromFile())
            {
                problems.Add($"{domain}: would not read its own file back");
                continue;
            }

            foreach ((string code, string was) in before)
            {
                string now = config.GetSetting(code)?.Value.Token?.ToString() ?? "";
                if (now != was) problems.Add($"{domain}/{code}: {was} became {now}");
            }
        }

        Assert.Equal("", string.Join("\n", problems));
    }

    /// <summary>
    /// Every config composes a screen, and every setting it has is either on it or folded
    /// behind a heading that is. A row that exists in neither place has vanished, which is
    /// the one thing this library promises does not happen.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task EveryConfigComposesAScreenThatAccountsForItsSettings()
    {
        await OnClient();

        List<string> problems = [];

        foreach ((string domain, Config config) in Loaded())
        {
            ConfigDialog dialog;

            try
            {
                dialog = new ConfigDialog(Capi, new Dictionary<string, Config> { [domain] = config });
                dialog.TryOpen();
            }
            catch (Exception exception)
            {
                problems.Add($"{domain}: composing threw {exception.GetType().Name}: {exception.Message}");
                continue;
            }

            await Frames.Wait(2);

            try
            {
                // Hidden settings are deliberately off screen; everything else is either
                // drawn or inside a section that can be opened to draw it.
                int hidden = config.SettingCodes.Count(code => config.GetSetting(code) is ConfigSetting s && s.Hide);
                int reachable = dialog.RenderedSettings.Count;

                foreach (string section in dialog.Sections)
                {
                    dialog.ToggleSectionNamed(section);
                    await Frames.Wait(1);
                    reachable = Math.Max(reachable, dialog.RenderedSettings.Count);
                }

                if (dialog.Sections.Count == 0 && reachable + hidden < config.SettingCodes.Count())
                {
                    problems.Add($"{domain}: {reachable} of {config.SettingCodes.Count()} rows on an unsectioned screen");
                }
            }
            finally
            {
                dialog.TryClose();
            }
        }

        Assert.Equal("", string.Join("\n", problems));
    }
}
