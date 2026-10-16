using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ConfigKit;

/// <summary>
/// ConfigKit's own settings, which are not managed by ConfigKit.
///
/// Deliberately a plain file with no settings screen. Everything in here decides what
/// ConfigKit will and will not take charge of, and a screen for that would have to be drawn
/// by the thing being configured - at which point a player who has switched a mod off cannot
/// switch it back on.
/// </summary>
public sealed class ConfigKitOwnConfig
{
    /// <summary>
    /// Mod ids ConfigKit must leave alone, whatever they declare.
    ///
    /// Two config managers writing one file is worse than either doing it, and the two that
    /// came before ConfigKit - configlib and autoconfiglib - own the same files and the same
    /// assets, so ConfigKit stands down entirely when either is installed. That is the right
    /// answer for a library that would contend for everything.
    ///
    /// It is the wrong answer for one that contends for a handful of mods. Integrated Mod
    /// Manager, for instance, manages exactly the mods carrying a config/imm.json and edits
    /// their config files directly, so it overlaps only where a mod is described to both. A
    /// global stand-down there would switch ConfigKit off for every other mod in the game to
    /// settle an argument about one.
    ///
    /// This is the small tool for that: name the mod, and ConfigKit does not claim it. The
    /// other manager keeps it, everything else is unaffected, and the decision belongs to
    /// whoever is looking at the broken screen rather than to either library's author.
    /// </summary>
    public List<string> UnmanagedDomains { get; set; } = [];

    /// <summary>
    /// Build a settings screen for every mod that reads its config with LoadModConfig and
    /// describes it to nobody. See AutoConfigs.
    ///
    /// Off by default. It rewrites a call in every mod that makes one, and most players have
    /// ConfigKit because another mod depends on it, not because they chose it. A mod that
    /// misbehaves under it can still be excluded on its own through UnmanagedDomains.
    /// </summary>
    public bool AutoConfigs { get; set; }
}

public static class OwnConfig
{
    public const string FileName = "configkit.json";

    /// <summary>
    /// Reads the skip list, creating the file if it is missing so that it can be found. A
    /// malformed one is reported and ignored rather than taking the library down - the whole
    /// point of this file is to rescue a broken setup, so it must not be able to cause one.
    /// </summary>
    public static HashSet<string> UnmanagedDomains(ICoreAPI api)
    {
        HashSet<string> unmanaged = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            ConfigKitOwnConfig? own = api.LoadModConfig<ConfigKitOwnConfig>(FileName);

            if (own == null)
            {
                api.StoreModConfig(new ConfigKitOwnConfig(), FileName);
                return unmanaged;
            }

            foreach (string domain in own.UnmanagedDomains ?? [])
            {
                if (!string.IsNullOrWhiteSpace(domain)) unmanaged.Add(domain.Trim());
            }
        }
        catch (Exception exception)
        {
            LoggerUtil.Warn(api, typeof(OwnConfig),
                $"Could not read {FileName}: {exception.Message}. Managing every config as usual.");

            return [];
        }

        if (unmanaged.Count > 0)
        {
            LoggerUtil.Notify(api, typeof(OwnConfig),
                $"Leaving these alone, as {FileName} asks: {string.Join(", ", unmanaged.OrderBy(domain => domain))}.");
        }

        return unmanaged;
    }

    /// <summary>
    /// Writes back a file that predates one of the keys, so that the key shows. Nobody turns on
    /// an option they cannot see. Read once untyped, to see which keys are really in the file -
    /// the typed read fills a missing one with its default and cannot tell - and once typed,
    /// to write back exactly what was there plus the new keys. A file that will not parse is
    /// left alone: UnmanagedDomains has already reported it.
    /// </summary>
    public static void AddMissingKeys(ICoreAPI api)
    {
        try
        {
            JsonObject? onDisk = api.LoadModConfig(FileName);
            if (onDisk == null || onDisk.KeyExists(nameof(ConfigKitOwnConfig.AutoConfigs))) return;

            if (api.LoadModConfig<ConfigKitOwnConfig>(FileName) is { } own) api.StoreModConfig(own, FileName);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Whether auto configs are on, read straight from disk. It is needed in ConfigKit's
    /// constructor, before there is an api to read it through. Anything unreadable means off:
    /// the safe answer for an option that patches other mods.
    /// </summary>
    public static bool AutoConfigsEnabled()
    {
        try
        {
            string path = Path.Combine(GamePaths.ModConfig, FileName);
            if (!File.Exists(path)) return false;

            return JsonConvert.DeserializeObject<ConfigKitOwnConfig>(File.ReadAllText(path))?.AutoConfigs == true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
