// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.
//
// Derived from ConfigLib by Maltiez (https://github.com/maltiez2/vsmod_configlib),
// released under CC0 1.0 Universal. Adapted to drop the Dear ImGui dependency.

using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace ConfigKit;


internal sealed class JsonObjectPath
{
    public JsonObjectPath(string path)
    {
        _segments = path.Split("/").Where(element => element != "").ToArray();
        _path = _segments.Select(Convert).ToArray();

        // Only a path made entirely of plain object keys can be created where it does not
        // already exist - a wildcard or a range selects among things that exist and has no
        // single place to put a missing one.
        IsSimpleKeyPath = _segments.Length > 0 && _segments.All(IsPlainKey);
    }

    /// <summary>True when every element is a plain object key, so <see cref="SetOrCreate"/> can build it.</summary>
    public bool IsSimpleKeyPath { get; }

    public IEnumerable<JsonObject> Get(JsonObject tree)
    {
        IEnumerable<JsonObject> result = [tree];
        foreach (PathElementDelegate element in _path)
        {
            result = element.Invoke(result);
            if (result == null) return Array.Empty<JsonObject>();
        }
        return result;
    }
    public int Set(JsonObject tree, JsonObject value)
    {
        // Materialise before mutating. Get returns a lazy chain, so replacing tokens while
        // enumerating it - and then counting it again afterwards - re-ran the selectors
        // against nodes that had already been swapped out.
        List<JsonObject> targets = Get(tree).ToList();

        foreach (JsonObject element in targets)
        {
            element.Token?.Replace(value.Token);
        }

        return targets.Count;
    }

    /// <summary>
    /// <see cref="Set"/> replaces tokens that are already there and creates nothing, which is
    /// right for patching a game asset - you only ever edit what the asset already has. A
    /// managed config is the other case: the setting exists in code and the player's file
    /// predates it, so there is no "Thirst" object to descend into and the value would simply
    /// never be written.
    /// </summary>
    public int SetOrCreate(JsonObject tree, JsonObject value)
    {
        int replaced = Set(tree, value);
        if (replaced > 0 || !IsSimpleKeyPath) return replaced;

        if (tree.Token is not JObject current) return 0;

        for (int index = 0; index < _segments.Length - 1; index++)
        {
            if (current[_segments[index]] is JObject existing)
            {
                current = existing;
                continue;
            }

            // Also covers the case where the key exists but holds a scalar, which happens
            // when a member that used to be a number becomes a nested object.
            JObject created = new();
            current[_segments[index]] = created;
            current = created;
        }

        current[_segments[^1]] = value.Token;
        return 1;
    }

    private static bool IsPlainKey(string element)
        => !int.TryParse(element, out _)
           && element != "-"
           && TryParseRange(element) == null
           && TryParseWildcard(element) == null
           && TryParseCondition(element) == null;

    private delegate IEnumerable<JsonObject> PathElementDelegate(IEnumerable<JsonObject> attribute);
    private readonly IEnumerable<PathElementDelegate> _path;
    private readonly string[] _segments;

    private PathElementDelegate Convert(string element)
    {
        if (int.TryParse(element, out int index))
        {
            return tree => PathElementByIndex(tree, index);
        }
        else
        {
            if (element == "-") return tree => PathElementByAllIndexes(tree);

            PathElementDelegate? rangeResult = TryParseRange(element);
            if (rangeResult != null) return rangeResult;

            PathElementDelegate? wildcardResult = TryParseWildcard(element);
            if (wildcardResult != null) return wildcardResult;

            PathElementDelegate? conditionResult = TryParseCondition(element);
            if (conditionResult != null) return conditionResult;

            return tree => PathElementByKey(tree, element);
        }
    }

    private static IEnumerable<JsonObject> PathElementByAllIndexes(IEnumerable<JsonObject> attributes)
    {
        List<JsonObject> result = new();
        foreach (JsonObject[] attributesArray in attributes.Where(element => element.IsArray()).Select(element => element.AsArray()))
        {
            int size = attributesArray.Length;
            for (int i = 0; i < size; i++)
            {
                result.Add(attributesArray[i]);
            }
        }

        return result;
    }
    private static IEnumerable<JsonObject> PathElementByIndexes(IEnumerable<JsonObject> attributes, int start, int end)
    {
        List<JsonObject> result = new();
        foreach (JsonObject[] attributesArray in attributes.Where(element => element.IsArray()).Select(element => element.AsArray()))
        {
            int size = attributesArray.Length;
            for (int i = Math.Max(0, start); i < Math.Min(end, size); i++)
            {
                result.Add(attributesArray[i]);
            }
        }

        return result;
    }
    private static IEnumerable<JsonObject> PathElementByIndex(IEnumerable<JsonObject> attributes, int index)
    {
        List<JsonObject> result = new();

        foreach (JsonObject attribute in attributes.Where(element => element.IsArray()))
        {
            if (index < 0 || attribute.AsArray().Length <= index)
            {
                continue;
            }

            JsonObject[] jsonArray = attribute.AsArray();

            result.Add(jsonArray[index]);
        }

        return result;
    }
    private static IEnumerable<JsonObject> PathElementByKey(IEnumerable<JsonObject> attributes, string key)
    {
        List<JsonObject> result = new();

        foreach (JsonObject attribute in attributes)
        {
            if (attribute?.KeyExists(key) == true)
            {
                result.Add(attribute[key]);
                continue;
            }
        }

        return result;
    }
    private static IEnumerable<JsonObject> PathElementByWildcard(IEnumerable<JsonObject> attributes, string wildcard)
    {
        List<JsonObject> result = new();

        foreach (JObject token in attributes.Select(attribute => attribute.Token).OfType<JObject>())
        {
            foreach ((string key, JToken? value) in token)
            {
                if (WildcardUtil.Match(wildcard, key) && value != null)
                {
                    result.Add(new(value));
                }
            }
        }

        return result;
    }
    private static IEnumerable<JsonObject> PathElementByCondition(IEnumerable<JsonObject> attributes, string code, string condition)
    {
        IEnumerable<JArray> arrays = attributes
            .Select(element => element.Token)
            .OfType<JArray>();

        IEnumerable<JObject> objects = attributes
            .Select(element => element.Token)
            .OfType<JObject>();

        IEnumerable<JToken> tokens = [];
        if (arrays.Any())
        {
            tokens = tokens.Concat(arrays.Select(a => a as IEnumerable<JToken>).Aggregate((a, b) => a.Concat(b)));
        }
        if (objects.Any())
        {
            tokens = tokens.Concat(objects.Select(a => a as IEnumerable<JToken>).Aggregate((a, b) => a.Concat(b)));
        }

        IEnumerable<JsonObject> fromObjects = tokens
            .OfType<JObject>()
            .Select(a => new JsonObject(a))
            .Where(a => a.KeyExists(code) && a[code].AsString() == condition);

        IEnumerable<JsonObject> fromProperties = tokens
            .OfType<JProperty>()
            .Select(a => new JsonObject(a.Value))
            .Where(a => a.KeyExists(code) && a[code].AsString() == condition);

        return fromObjects.Concat(fromProperties);
    }

    private static PathElementDelegate? TryParseRange(string element)
    {
        if (element.Contains('-'))
        {
            string[] indexes = element.Split('-');
            if (indexes.Length != 2) return null;

            bool parsedStart = int.TryParse(indexes[0], out int start);
            bool parsedEnd = int.TryParse(indexes[1], out int end);

            if (!parsedStart || !parsedEnd) return null;

            return tree => PathElementByIndexes(tree, start, end);
        }

        if (element.Contains(".."))
        {
            string[] indexes = element.Split("..");
            if (indexes.Length != 2) return null;

            bool parsedStart = int.TryParse(indexes[0], out int start);
            bool parsedEnd = int.TryParse(indexes[1], out int end);

            if (!parsedStart || !parsedEnd) return null;

            return tree => PathElementByIndexes(tree, start, end + 1);
        }

        return null;
    }
    private static PathElementDelegate? TryParseWildcard(string element)
    {
        if (!element.StartsWith("@@")) return null;
        string wildcard = element.Substring(2, element.Length - 2);

        return tree => PathElementByWildcard(tree, wildcard);
    }
    private static PathElementDelegate? TryParseCondition(string element)
    {
        if (!element.Contains("=")) return null;

        string[] parts = element.Split("=");
        
        if (parts.Length != 2) return null;

        string code = parts[0];
        string condition = parts[1];

        return tree => PathElementByCondition(tree, code, condition);
    }
}