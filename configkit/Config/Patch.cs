// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.
//
// Derived from ConfigLib by Maltiez (https://github.com/maltiez2/vsmod_configlib),
// released under CC0 1.0 Universal. Adapted to drop the Dear ImGui dependency.

using Newtonsoft.Json.Linq;
using SimpleExpressionEngine;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.Common;

namespace ConfigKit;

public class ConfigPatches
{
    private readonly ICoreAPI _api;
    private readonly List<AssetPatch> _patches = new();

    public ConfigPatches(ICoreAPI api, Config config)
    {
        _api = api;

        HashSet<string> files = GetFiles(config.Definition["patches"]);

        foreach (string filePath in files)
        {
            try
            {
                _patches.Add(new(api, filePath, config));
            }
            catch (Exception exception)
            {
                _api.Logger.Warning($"[ConfigKit] Exception on creating patch to asset '{filePath}'.");
                _api.Logger.VerboseDebug($"[ConfigKit] Exception on creating patch to asset '{filePath}':\n{exception}\n.");
            }
        }
    }

    public void Apply()
    {
        foreach (AssetPatch patch in _patches)
        {
            try
            {
                (int successful, int failed) = patch.Apply(out bool serverSide);
                if (!serverSide && failed == 0)
                {
                    _api.Logger.VerboseDebug($"[ConfigKit] Values patched: {successful} in asset: '{patch.File}'.");
                }
                else
                {
                    _api.Logger.VerboseDebug($"[ConfigKit] Values patched: {successful}, failed: {failed} in asset: '{patch.File}'.");
                }
            }
            catch (Exception exception)
            {
                _api.Logger.Debug($"[ConfigKit] Exception on applying patch to asset: '{patch.File}'.");
                _api.Logger.VerboseDebug($"[ConfigKit] Exception on applying patch to asset:\n{exception}\n.");
            }
        }
    }

    public static HashSet<string> GetFiles(JsonObject? definition)
    {
        HashSet<string> files = new();

        if (definition?.Token is not JObject categories) return files;

        foreach ((_, JToken? category) in categories)
        {
            if (category is not JObject categoryObject) continue;

            foreach ((string file, _) in categoryObject)
            {
                if (!files.Contains(file)) files.Add(file);
            }
        }

        return files;
    }
}

internal partial class AssetPatch
{
    public string File => _asset;

    private readonly string _asset;
    private readonly List<IValuePatch> _patches = new();
    private readonly ICoreAPI _api;
    private readonly HashSet<AssetCategory> _serverSideCategories = new()
    {
        AssetCategory.itemtypes,
        AssetCategory.blocktypes,
        AssetCategory.entities,
        AssetCategory.recipes
    };

    public AssetPatch(ICoreAPI api, string assetPath, Config config)
    {
        _api = api;
        _asset = assetPath;

        JsonObject definition = config.Definition["patches"];
        CombinedContext<float, float> context = new(new List<IContext<float, float>>() { new MathContext(), new BooleanMathContext(), new NumberSettingsContext(config.Settings) });

        if (!ConstructBooleanPatches(definition, assetPath, context))
            _api.Logger.Debug($"[ConfigKit] Error on parsing 'boolean' patches for '{assetPath}'.");
        if (!ConstructNumberPatches(definition, assetPath, context, "number", ConfigSettingType.Float))
            _api.Logger.Debug($"[ConfigKit] Error on parsing 'number' patches for '{assetPath}'.");
        if (!ConstructNumberPatches(definition, assetPath, context, "float", ConfigSettingType.Float))
            _api.Logger.Debug($"[ConfigKit] Error on parsing 'float' patches for '{assetPath}'.");
        if (!ConstructNumberPatches(definition, assetPath, context, "integer", ConfigSettingType.Integer))
            _api.Logger.Debug($"[ConfigKit] Error on parsing 'integer' patches for '{assetPath}'.");
        if (!ConstructConstPatches(definition, assetPath))
            _api.Logger.Debug($"[ConfigKit] Error on parsing 'const' patches for '{assetPath}'.");
        if (!ConstructStringPatches(definition, assetPath, config, context))
            _api.Logger.Debug($"[ConfigKit] Error on parsing 'string' patches for '{assetPath}'.");
        if (!ConstructJsonPatches(definition, assetPath, config, context))
            _api.Logger.Debug($"[ConfigKit] Error on parsing 'other' patches for '{assetPath}'.");
    }

    public (int successful, int failed) Apply(out bool serverSideAsset)
    {
        IEnumerable<(JsonObject? asset, string path)> assets;
        if (_asset.StartsWith('@'))
        {
            // Materialised, not left lazy. The chain retrieves - and so restores the
            // baseline of - every asset it touches, and it is walked twice below: once to
            // count, once to patch. The second walk would find the assets holding their
            // restored baseline rather than what ConfigKit last wrote, and take its own
            // work for another writer's.
            assets = RetrieveAssetsByWildcard().ToList();
            serverSideAsset = false;

            _api.Logger.VerboseDebug($"[ConfigKit] Retrieved {assets.Count()} assets by wildcard '{_asset}'.");
        }
        else
        {
            JsonObject? assetValue = RetrieveAsset(out serverSideAsset);
            assets = new List<(JsonObject? asset, string path)>() { (assetValue, _asset) };
            if (serverSideAsset) return (0, 0);
            if (assetValue == null)
            {
                _api.Logger.VerboseDebug($"[ConfigKit] Failed to retrieve asset by path '{_asset}'");
                return (0, 0);
            }
        }

        int failedCount = 0;
        int successfulCount = 0;

        foreach (IValuePatch patch in _patches)
        {
            foreach ((JsonObject? asset, string path) in assets)
            {
                if (asset == null) continue;

                try
                {
                    int pathCount = patch.Apply(asset);
                    successfulCount += pathCount;

                    _api.Logger.VerboseDebug($"[ConfigKit] Patched {pathCount} values by path '{patch.Path}'");
                }
                catch (Exception exception)
                {
                    _api.Logger.VerboseDebug($"[ConfigKit] Failed to apply patch by path '{patch.Path}' in asset '{path}'.\nException: {exception}\n");
                    failedCount++;
                }

                StoreAsset(asset, path);
            }
        }

        return (successfulCount, failedCount);
    }

    private IEnumerable<(JsonObject? asset, string path)> RetrieveAssetsByWildcard()
    {
        string wildcard = _asset.Substring(1, _asset.Length - 1);

        return _api.Assets.GetLocations("")
            .Where(path => WildcardUtil.Match(wildcard, path.ToString()))
            .Select(path => path.ToString())
            .Select(RetrieveAsset)
            .Where(entry => !entry.serverSide)
            .Where(entry => entry.asset != null)
            .Select(entry => (entry.asset, entry.path));
    }

    private JsonObject? RetrieveAsset(out bool serverSide) => RetrieveAsset(_asset, out serverSide);
    private (bool serverSide, JsonObject? asset, string path) RetrieveAsset(string path)
    {
        JsonObject? asset = RetrieveAsset(path, out bool serverSide);
        return (serverSide, asset, path);
    }
    /// <summary>What an asset looked like before ConfigKit patched it, and what ConfigKit
    /// last wrote over it.</summary>
    private sealed class AssetBaseline
    {
        public byte[] Pristine = [];
        public byte[] Written = [];
    }

    /// <summary>
    /// The bytes each asset had before ConfigKit patched it.
    ///
    /// Patches rewrite IAsset.Data in place, and patching runs more than once on a client -
    /// once over the locally parsed configs and again when the server's configs arrive. A
    /// formula written against the asset's own value ("value * 2") therefore compounded, so
    /// a x2 multiplier silently became x4. Every application starts from the baseline bytes
    /// instead, which makes patching idempotent however often it runs.
    ///
    /// The baseline is not fixed for the session: it is rebased whenever the asset no longer
    /// holds what ConfigKit last wrote, because that means someone else has written to it -
    /// the json patch loader at ExecuteOrder 0.05, or another mod. Restoring the whole file
    /// over their bytes would silently revert their work, which is how a server owner's json
    /// patch to a modded asset went missing with no word in the log. Their file becomes the
    /// baseline, with only the paths this patch owns taken back to what they held before
    /// ConfigKit first wrote - keeping their version of those too would feed ConfigKit's own
    /// result into a formula written against the asset's value, and "value * 2" would
    /// compound after all.
    ///
    /// Keyed by the asset object, not its path, so the two sides of a singleplayer session
    /// keep their own baselines - a json patch can be conditional on side.
    /// </summary>
    private static readonly ConditionalWeakTable<IAsset, AssetBaseline> _baselines = new();

    private JsonObject? RetrieveAsset(string path, out bool serverSide)
    {
        serverSide = false;

        AssetLocation location = new(path);
        if (_api.Side == EnumAppSide.Client && _serverSideCategories.Contains(location.Category))
        {
            serverSide = true;
            return null;
        }

        IAsset? asset;
        try
        {
            asset = _api.Assets.Get(path);
            RestoreBaseline(asset, path);
        }
        catch
        {
            _api.Logger.Debug($"[ConfigKit] Asset '{path}' not found, skipping it.");
            return null;
        }

        if (asset == null) return null;

        return ParseJson(asset.Data);
    }

    /// <summary>An asset's bytes as json, array or object, or null if they are neither.</summary>
    private static JsonObject? ParseJson(byte[] data)
    {
        string json = Asset.BytesToString(data);

        try
        {
            return new(JArray.Parse(json));
        }
        catch
        {
            try
            {
                return new(JObject.Parse(json));
            }
            catch
            {
                return null;
            }
        }
    }

    private void RestoreBaseline(IAsset? asset, string path)
    {
        if (asset?.Data == null) return;

        lock (_baselines)
        {
            if (!_baselines.TryGetValue(asset, out AssetBaseline? baseline))
            {
                // First sight of this asset. What it holds now is what it held before.
                _baselines.AddOrUpdate(asset, new AssetBaseline { Pristine = (byte[])asset.Data.Clone() });
                return;
            }

            if (baseline.Written.AsSpan().SequenceEqual(asset.Data))
            {
                asset.Data = (byte[])baseline.Pristine.Clone();
                return;
            }

            baseline.Pristine = Rebase(baseline.Pristine, asset.Data) ?? (byte[])asset.Data.Clone();
            baseline.Written = [];
            asset.Data = (byte[])baseline.Pristine.Clone();

            _api.Logger.VerboseDebug($"[ConfigKit] Asset '{path}' has been written to by something else since ConfigKit last patched it. Keeping that version and rebasing onto it.");
        }
    }

    /// <summary>
    /// Someone else's version of the asset, with every path this patch owns taken back to
    /// what the old baseline held. Null if either version will not parse, which leaves the
    /// caller to keep theirs whole.
    /// </summary>
    private byte[]? Rebase(byte[] pristine, byte[] foreign)
    {
        try
        {
            JsonObject? before = ParseJson(pristine);
            JsonObject? after = ParseJson(foreign);

            if (before == null || after == null) return null;

            foreach (IValuePatch patch in _patches)
            {
                JsonObject[] original = patch.Nodes(before).ToArray();
                JsonObject[] live = patch.Nodes(after).ToArray();

                // Paired by position. A foreign patch that added the node ConfigKit patches
                // leaves nothing to take it back to, and one that removed it leaves nothing
                // to write into; both are right to skip.
                for (int index = 0; index < Math.Min(original.Length, live.Length); index++)
                {
                    JToken? held = original[index].Token?.DeepClone();
                    if (held == null) continue;

                    live[index].Token?.Replace(held);
                }
            }

            string? rebased = after.ToString();

            return rebased == null ? null : System.Text.Encoding.UTF8.GetBytes(rebased);
        }
        catch (Exception exception)
        {
            _api.Logger.VerboseDebug($"[ConfigKit] Failed to rebase patches onto another writer's version of an asset: {exception}");
            return null;
        }
    }

    /// <summary>Remembers what was written, so a later pass can tell ConfigKit's own bytes
    /// from someone else's.</summary>
    private static void RecordWritten(IAsset asset, byte[] bytes)
    {
        lock (_baselines)
        {
            if (_baselines.TryGetValue(asset, out AssetBaseline? baseline)) baseline.Written = bytes;
        }
    }

    /// <summary>Forgets the baselines. Assets are reloaded with the world.</summary>
    internal static void ForgetAssetBaselines()
    {
        lock (_baselines) _baselines.Clear();
    }

    private void StoreAsset(JsonObject data, string path)
    {
        IAsset? asset = _api.Assets.Get(path);
        if (asset == null) return;

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(data.ToString());
        asset.Data = bytes;
        RecordWritten(asset, bytes);
    }

    private static readonly Regex _booleanExpressionRegex = GetBooleanExpressionRegex();

    private bool ConstructBooleanPatches(JsonObject definition, string assetPath, CombinedContext<float, float> context)
    {
        if (!definition.KeyExists("boolean")) return true;
        if (!definition["boolean"].KeyExists(assetPath)) return true;
        if (definition["boolean"][assetPath]?.Token is not JObject booleanPatches) return false;

        bool failed = false;

        foreach ((string key, JToken? value) in booleanPatches)
        {
            if (value is not JValue boolValue || boolValue.Type != JTokenType.String)
            {
                _api.Logger.Error($"[ConfigKit] Error on parsing patch '{assetPath}/{key}': patch value '{value}' is not string.");
                failed = true;
                continue;
            }
            string setting = (string?)boolValue.Value ?? "";

            setting = Process(setting, context);

            _patches.Add(new BooleanPatch(key, setting, context));
        }

        return !failed;
    }
    private bool ConstructStringPatches(JsonObject definition, string assetPath, Config config, CombinedContext<float, float> context)
    {
        if (!definition.KeyExists("string")) return true;
        if (!definition["string"].KeyExists(assetPath)) return true;
        if (definition["string"][assetPath]?.Token is not JObject stringPatches) return false;

        bool failed = false;

        foreach ((string key, JToken? value) in stringPatches)
        {
            if (value is not JValue boolValue || boolValue.Type != JTokenType.String)
            {
                _api.Logger.Error($"[ConfigKit] Error on parsing patch '{assetPath}/{key}': patch value '{value}' is not string.");
                failed = true;
                continue;
            }

            string setting = (string?)boolValue.Value ?? "";
            setting = Process(setting, context);

            if (setting == "value") continue;

            _patches.Add(new StringPatch(key, setting, config));
        }

        return !failed;
    }
    private bool ConstructJsonPatches(JsonObject definition, string assetPath, Config config, CombinedContext<float, float> context)
    {
        if (!definition.KeyExists("other")) return true;
        if (!definition["other"].KeyExists(assetPath)) return true;
        if (definition["other"][assetPath]?.Token is not JObject jsonPatches) return false;

        bool failed = false;

        foreach ((string key, JToken? value) in jsonPatches)
        {
            if (value is not JValue boolValue || boolValue.Type != JTokenType.String)
            {
                _api.Logger.Error($"[ConfigKit] Error on parsing patch '{assetPath}/{key}': patch value '{value}' is not string.");
                failed = true;
                continue;
            }

            string setting = (string?)boolValue.Value ?? "";
            setting = Process(setting, context);

            if (setting == "value") continue;

            if (config.GetSetting(setting) == null)
            {
                _api.Logger.Error($"[ConfigKit] Error on parsing patch '{assetPath}/{key}': other setting '{setting}' not found.");
                failed = true;
                continue;
            }
            /*if (config.GetSetting(setting)?.SettingType != ConfigSettingType.Other)
            {
                _api.Logger.Error($"[ConfigKit] Error on parsing patch '{assetPath}/{key}': setting '{setting}' is not form 'other' category.");
                failed = true;
                continue;
            }*/
            _patches.Add(new JsonPatch(key, setting, config));
        }

        return !failed;
    }
    private bool ConstructNumberPatches(JsonObject definition, string assetPath, CombinedContext<float, float> context, string category, ConfigSettingType type)
    {
        if (!definition.KeyExists(category)) return true;
        if (!definition[category].KeyExists(assetPath)) return true;
        if (definition[category][assetPath]?.Token is not JObject jsonPatches) return false;

        bool failed = false;

        foreach ((string key, JToken? value) in jsonPatches)
        {
            if (value is not JValue formula || formula.Type != JTokenType.String)
            {
                _api.Logger.Error($"[ConfigKit] Error on parsing '{category}' patch '{assetPath}/{key}': patch value '{value}' is not string.");
                failed = true;
                continue;
            }
            string setting = (string?)formula.Value ?? "";
            setting = Process(setting, context);

            _patches.Add(new NumberPatch(key, setting, context, type));
        }

        return !failed;
    }
    private bool ConstructConstPatches(JsonObject definition, string assetPath)
    {
        if (!definition.KeyExists("const")) return true;
        if (!definition["const"].KeyExists(assetPath)) return true;
        if (definition["const"][assetPath]?.Token is not JObject jsonPatches) return false;

        bool failed = false;

        foreach ((string key, JToken? value) in jsonPatches)
        {
            _patches.Add(new ConstPatch(key, new JsonObject(value)));
        }

        return !failed;
    }

    [GeneratedRegex("\\((?<expression>.+)\\) \\? (?<true>.+) \\: (?<false>.+)", RegexOptions.Compiled)]
    private static partial Regex GetBooleanExpressionRegex();

    private const float _epsilon = float.Epsilon;
    private string Process(string value, IContext<float, float> context)
    {
        Match match = _booleanExpressionRegex.Match(value);

        if (!match.Success) return value;

        string expression = match.Groups["expression"].Value;
        string onTrue = match.Groups["true"].Value;
        string onFalse = match.Groups["false"].Value;

        float result = MathParser.Parse(expression).Evaluate(context);

        return Math.Abs(result) > _epsilon ? onTrue : onFalse;
    }
}

internal interface IValuePatch
{
    int Apply(JsonObject asset);

    /// <summary>The nodes of this asset the patch writes to - the paths it owns.</summary>
    IEnumerable<JsonObject> Nodes(JsonObject asset);

    string Path { get; }
}

internal sealed class BooleanExpressionSelector
{
    private readonly INode<float, float, float> _value;
    private readonly IContext<float, float> _context;
    private readonly string _onTrue;
    private readonly string _onFalse;
    private const float _epsilon = 1e-10f;

    public BooleanExpressionSelector(string expression, string onTrue, string onFalse, IContext<float, float> context)
    {
        _value = MathParser.Parse(expression);
        _context = context;
        _onTrue = onTrue;
        _onFalse = onFalse;
    }

    public string Resolve(float value)
    {
        CombinedContext<float, float> context = new(new List<IContext<float, float>> { _context, new ValueContext<float, float>(value) });

        float result = _value.Evaluate(context);

        return Math.Abs(result) > _epsilon ? _onTrue : _onFalse;
    }
}

internal sealed class NumberPatch : IValuePatch
{
    public string Path { get; set; }

    private readonly JsonObjectPath _path;
    private readonly INode<float, float, float> _value;
    private readonly IContext<float, float> _context;
    private readonly ConfigSettingType _returnType;

    public NumberPatch(string path, string formula, IContext<float, float> context, ConfigSettingType type)
    {
        _path = new(path);
        Path = path;
        _value = MathParser.Parse(formula);
        _context = context;
        _returnType = type;
    }

    public IEnumerable<JsonObject> Nodes(JsonObject asset) => _path.Get(asset);

    public int Apply(JsonObject asset)
    {
        IEnumerable<JsonObject> jsonValues = Nodes(asset);
        jsonValues.Foreach(ApplyToOne);
        return jsonValues.Count();
    }

    public void ApplyToOne(JsonObject jsonValue)
    {
        float previousValue = jsonValue?.AsFloat(0) ?? 0;

        if (jsonValue is not null && ((JValue)jsonValue.Token).Value is bool)
        {
            previousValue = jsonValue.AsBool() ? 1 : 0;
        }

        CombinedContext<float, float> context = new(new List<IContext<float, float>> { new ValueContext<float, float>(previousValue), _context });

        float value = _value.Evaluate(context);

        switch (_returnType)
        {
            case ConfigSettingType.Float:
                jsonValue?.Token.Replace(new JValue(value));
                break;
            case ConfigSettingType.Integer:
                jsonValue?.Token.Replace(new JValue((int)value));
                break;
        }
    }
}
internal sealed class BooleanPatch : IValuePatch
{
    public string Path { get; set; }

    private readonly JsonObjectPath _path;
    private readonly INode<float, float, float> _value;
    private readonly IContext<float, float> _context;
    private const float _epsilon = 1e-10f;

    public BooleanPatch(string path, string formula, IContext<float, float> context)
    {
        _path = new(path);
        Path = path;
        _value = MathParser.Parse(formula);
        _context = context;
    }

    public IEnumerable<JsonObject> Nodes(JsonObject asset) => _path.Get(asset);

    public int Apply(JsonObject asset)
    {
        IEnumerable<JsonObject> jsonValues = Nodes(asset);
        jsonValues.Foreach(ApplyToOne);
        return jsonValues.Count();
    }

    public void ApplyToOne(JsonObject jsonValue)
    {
        float previousValue = (jsonValue?.AsBool() ?? false) ? 1 : 0;

        CombinedContext<float, float> context = new(new List<IContext<float, float>> { _context, new ValueContext<float, float>(previousValue) });

        float value = _value.Evaluate(context);
        bool result = Math.Abs(value) > _epsilon;

        jsonValue?.Token.Replace(new JValue(result));
    }
}
internal sealed class StringPatch : IValuePatch
{
    public string Path { get; set; }

    private readonly JsonObjectPath _path;
    private readonly string _value;

    public StringPatch(string path, string value, Config config)
    {
        _path = new(path);
        Path = path;
        _value = config.GetSetting(value)?.Value.AsString() ?? value;
    }

    public IEnumerable<JsonObject> Nodes(JsonObject asset) => _path.Get(asset);

    public int Apply(JsonObject asset)
    {
        IEnumerable<JsonObject> jsonValues = Nodes(asset);
        jsonValues.Foreach(ApplyToOne);
        return jsonValues.Count();
    }
    public void ApplyToOne(JsonObject jsonValue)
    {
        jsonValue?.Token.Replace(new JValue(_value));
    }
}
internal sealed class JsonPatch : IValuePatch
{
    public string Path { get; set; }

    private readonly JsonObjectPath _path;
    private readonly JsonObject? _value;

    public JsonPatch(string path, string value, Config config)
    {
        _path = new(path);
        Path = path;
        _value = config.GetSetting(value)?.Value;
    }

    public IEnumerable<JsonObject> Nodes(JsonObject asset) => _path.Get(asset);

    public int Apply(JsonObject asset)
    {
        IEnumerable<JsonObject> jsonValues = Nodes(asset);
        jsonValues.Foreach(ApplyToOne);
        return jsonValues.Count();
    }
    public void ApplyToOne(JsonObject jsonValue)
    {
        if (_value == null) return;
        jsonValue?.Token.Replace(_value.Token);
    }
}
internal sealed class ConstPatch : IValuePatch
{
    public string Path { get; set; }

    private readonly JsonObjectPath _path;
    private readonly JsonObject _value;

    public ConstPatch(string path, JsonObject value)
    {
        _path = new(path);
        Path = path;
        _value = value;
    }

    public IEnumerable<JsonObject> Nodes(JsonObject asset) => _path.Get(asset);

    public int Apply(JsonObject asset)
    {
        IEnumerable<JsonObject> jsonValues = Nodes(asset);
        jsonValues.Foreach(ApplyToOne);
        return jsonValues.Count();
    }
    public void ApplyToOne(JsonObject jsonValue)
    {
        if (_value == null) return;
        jsonValue?.Token.Replace(_value.Token);
    }
}