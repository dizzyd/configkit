using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Records what a build of ConfigKit does with a set of real configs, so a later build can be
/// held to it. This is the generator; CompatibilityTests is the comparison.
///
/// It is written to compile against the released version as well as the current one - it uses
/// nothing that was not already public at v1.2.0 - so the same file can be run from a worktree
/// of the old tag to produce the baseline.
///
///     git worktree add /tmp/ck-v120 v1.2.0
///     dotnet build /tmp/ck-v120/configkit/configkit.csproj -c Debug
///     VSTK_GOLDEN=1 run.sh tests/golden --mod /tmp/ck-v120/configkit --mods ~/mods/ckdemo --client
///
/// It lives in its own directory because the suite compiles as one unit, and the rest of it
/// uses API the released build does not have.
///
/// It edits every setting away from its default first, because a config full of defaults
/// proves nothing about upgrading: the interesting question is whether a player's own values
/// survive, and a default that is silently restored looks identical to one that was kept.
/// </summary>
[SingleplayerOnly]
public class CaptureGolden
{
    [VsTest(TimeoutMs = 180000)]
    [RequiresClient]
    public async Task RecordWhatThisBuildDoes()
    {
        if (Environment.GetEnvironmentVariable("VSTK_GOLDEN") != "1")
        {
            Log("set VSTK_GOLDEN=1 to record a compatibility baseline");
            return;
        }

        await OnClient();

        string outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "golden");
        Directory.CreateDirectory(outDir);

        ConfigKitModSystem ck = Capi.ModLoader.GetModSystem<ConfigKitModSystem>();
        JObject all = new();

        foreach (string domain in ck.Domains.OrderBy(d => d, StringComparer.Ordinal))
        {
            if (ck.GetConfig(domain) is not Config config) continue;

            Edit(config);
            config.WriteToFile();

            // The definition too, so the comparison can rebuild this config without the mod
            // that shipped it. A managed config has no definition asset - it is generated
            // from a class - and is not what a compatibility baseline is for anyway.
            string? definition = Definition(domain);
            if (definition == null)
            {
                Log($"{domain}: no definition asset, skipped");
                continue;
            }

            all[domain] = new JObject
            {
                ["definition"] = definition,
                ["settings"] = Settings(config),
                ["rendered"] = new JArray(Rendered(config)),
                ["file"] = Path.GetFileName(config.ConfigFilePath),
                // Inline, so the baseline is one file the game can load as an asset. The
                // test harness exposes no path to its own suite directory.
                ["contents"] = File.Exists(config.ConfigFilePath) ? File.ReadAllText(config.ConfigFilePath) : ""
            };

            await Frames.Wait(1);
        }

        File.WriteAllText(Path.Combine(outDir, "golden.json"), all.ToString(Formatting.Indented));
        Log($"recorded {all.Count} configs to {outDir}");
    }

    private static string? Definition(string domain)
    {
        try
        {
            Vintagestory.API.Common.IAsset? asset = Capi.Assets.TryGet(
                new Vintagestory.API.Common.AssetLocation(domain, "config/configlib-patches.json"));

            return asset?.ToText();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Moves every setting off its default, deterministically, so the baseline is a config
    /// somebody has actually used. Choices constrained to a set stay inside it.
    /// </summary>
    private static void Edit(Config config)
    {
        foreach (string code in config.SettingCodes.ToList())
        {
            if (config.GetSetting(code) is not ConfigSetting setting) continue;

            try
            {
                if (setting.Validation?.Mapping is { Count: > 1 } mapping)
                {
                    string current = setting.MappingKey ?? "";
                    setting.MappingKey = mapping.Keys.FirstOrDefault(k => k != current) ?? current;
                    continue;
                }

                if (setting.Validation?.Values is { Count: > 1 } values)
                {
                    string current = setting.Value.Token?.ToString() ?? "";
                    JsonObject? other = values.FirstOrDefault(v => v.Token?.ToString() != current);
                    if (other != null) setting.Value = other;
                    continue;
                }

                setting.Value = setting.SettingType switch
                {
                    ConfigSettingType.Boolean => new JsonObject(new JValue(!setting.Value.AsBool())),
                    ConfigSettingType.Integer => new JsonObject(new JValue(Bounded(setting, setting.Value.AsInt() + 3))),
                    ConfigSettingType.Float => new JsonObject(new JValue(Bounded(setting, setting.Value.AsFloat() + 0.25f))),
                    ConfigSettingType.String => new JsonObject(new JValue(setting.Value.AsString("") + "-edited")),
                    ConfigSettingType.Color => new JsonObject(new JValue("#C8553D")),
                    _ => setting.Value
                };
            }
            catch (Exception)
            {
                // A setting that will not take the value is not what this is measuring.
            }
        }
    }

    private static float Bounded(ConfigSetting setting, float wanted)
    {
        float min = setting.Validation?.Minimum?.AsFloat() ?? float.MinValue;
        float max = setting.Validation?.Maximum?.AsFloat() ?? float.MaxValue;

        return wanted > max ? max : wanted < min ? min : wanted;
    }

    private static JObject Settings(Config config)
    {
        JObject settings = new();

        foreach (string code in config.SettingCodes)
        {
            if (config.GetSetting(code) is not ConfigSetting setting) continue;

            settings[code] = new JObject
            {
                ["value"] = setting.Value.Token?.ToString() ?? "",
                ["type"] = setting.SettingType.ToString(),
                ["ingui"] = setting.InGui ?? "",
                ["comment"] = setting.Comment ?? "",
                ["mappingKey"] = setting.MappingKey ?? "",
                ["clientSide"] = setting.ClientSide,
                ["hide"] = setting.Hide
            };
        }

        return settings;
    }

    /// <summary>The rows the screen draws, in the order it draws them.</summary>
    private static List<string> Rendered(Config config)
    {
        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { [config.Domain()] = config });
        dialog.TryOpen();

        List<string> codes = dialog.RenderedSettings
            .OrderBy(entry => Index(entry.Key))
            .Select(entry => entry.Value.YamlCode)
            .ToList();

        dialog.TryClose();
        return codes;
    }

    private static int Index(string key)
    {
        int dash = key.LastIndexOf('-');
        return dash >= 0 && int.TryParse(key[(dash + 1)..], out int index) ? index : 0;
    }
}

internal static class GoldenExtensions
{
    /// <summary>
    /// The domain a config belongs to. Config.Domain is internal in both versions, and the
    /// file it writes is named for the domain in every case this records.
    /// </summary>
    public static string Domain(this Config config) => Path.GetFileNameWithoutExtension(config.ConfigFilePath);
}
