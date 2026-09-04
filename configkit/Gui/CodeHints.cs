// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ConfigKit.Gui;

/// <summary>
/// What a config key is supposed to name, from <c>[DataType("blockcode")]</c> on its member.
///
/// A dictionary keyed by block code is the single most common structured setting in the
/// published corpus, and a typo in one of those keys does nothing at all: the entry sits in
/// the file looking correct and simply never matches. Telling the player at the point they
/// type it is worth more than any amount of documentation.
///
/// Wildcards are first class, not an afterthought - config keys in the wild are routinely
/// "game:door-*", and a key that matches several blocks is exactly as valid as one that
/// matches a single block.
/// </summary>
public static class CodeHints
{
    public const string BlockCode = "blockcode";
    public const string ItemCode = "itemcode";
    public const string EntityCode = "entitycode";

    /// <summary>Watermark for an empty field. Null when the member said nothing about its keys.</summary>
    public static string? Placeholder(string? dataType) => Normalize(dataType) switch
    {
        BlockCode => "block code, e.g. game:plank-oak or game:door-*",
        ItemCode => "item code, e.g. game:nugget-native-copper or game:nugget-*",
        EntityCode => "entity code, e.g. game:wolf-eurasian-adult-male or game:wolf-*",
        _ => null
    };

    public static string Describe(string? dataType) => Normalize(dataType) switch
    {
        BlockCode => "block",
        ItemCode => "item",
        EntityCode => "entity",
        _ => "code"
    };

    /// <summary>
    /// Whether this code names anything the game has loaded. Null means we were told nothing
    /// about the member, or cannot answer - and an unknown answer must never be reported as a
    /// problem, so a mod whose blocks load later is not flagged as broken.
    /// </summary>
    public static bool? Resolves(ICoreClientAPI? capi, string? dataType, string code)
    {
        string? kind = Normalize(dataType);
        if (kind == null || capi?.World == null) return null;
        if (string.IsNullOrWhiteSpace(code)) return null;

        return kind switch
        {
            BlockCode => Any(capi.World.Blocks?.Select(block => block.Code), code),
            ItemCode => Any(capi.World.Items?.Select(item => item.Code), code),
            EntityCode => Any(capi.World.EntityTypes?.Select(type => type.Code), code),
            _ => null
        };
    }

    private static bool? Any(IEnumerable<AssetLocation?>? codes, string pattern)
    {
        if (codes == null) return null;

        // A registry that has not loaded yet answers "no" to everything, which would mark
        // every key wrong. No opinion is the honest answer there.
        List<AssetLocation> known = codes.Where(code => code != null).Select(code => code!).ToList();
        if (known.Count == 0) return null;

        AssetLocation? wanted = TryParse(pattern);
        if (wanted == null) return false;

        return known.Any(code => WildcardUtil.Match(wanted, code));
    }

    private static AssetLocation? TryParse(string pattern)
    {
        try
        {
            return new AssetLocation(pattern);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Normalize(string? dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType)) return null;

        string normalized = dataType.Replace("-", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();

        return normalized switch
        {
            "blockcode" or "block" or "blocks" => BlockCode,
            "itemcode" or "item" or "items" => ItemCode,
            "entitycode" or "entity" or "entities" or "creature" or "creaturecode" => EntityCode,
            _ => null
        };
    }
}
