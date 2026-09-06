// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.

using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;

namespace ConfigKit.Gui;

/// <summary>
/// One level of a container the player has opened: which setting owns the subtree, where
/// inside that subtree we are, and what the schema says is there.
/// </summary>
internal sealed class ContainerFrame
{
    public required ConfigSetting Setting;
    public required SchemaNode Node;
    /// <summary>Keys and indices from the setting's own value down to this level.</summary>
    public required List<object> Path;
    public required string Crumb;
    public required bool Locked;
}

/// <summary>
/// Reading and writing inside a container setting's subtree.
///
/// Every write is clone, mutate, assign. Mutating the setting's live token in place looks
/// simpler and is wrong twice over: ConfigSetting.Value decides whether anything changed by
/// comparing the old text to the new, so an in-place edit is invisible to it and never
/// reaches the mod's object - and a half-finished structural edit would already be live.
/// </summary>
internal static class Subtree
{
    public static JToken? Navigate(JToken? root, IReadOnlyList<object> path, int count)
    {
        JToken? current = root;

        for (int index = 0; index < count && current != null; index++)
        {
            current = path[index] switch
            {
                int position => current is JArray array && position >= 0 && position < array.Count
                    ? array[position]
                    : null,
                string key => current is JObject o ? o[key] : null,
                _ => null
            };
        }

        return current;
    }

    public static JToken? Navigate(JToken? root, IReadOnlyList<object> path) => Navigate(root, path, path.Count);

    /// <summary>
    /// Applies an edit to a copy of the setting's whole value and assigns it back, which is
    /// what makes the change visible to the setting and so to the mod.
    /// </summary>
    public static void Edit(ConfigSetting setting, IReadOnlyList<object> path, Action<JToken> edit)
    {
        JToken? original = setting.Value.Token;
        if (original == null) return;

        JToken root = original.DeepClone();
        JToken? target = Navigate(root, path);
        if (target == null) return;

        edit(target);
        setting.Value = new JsonObject(root);
    }

    public static void SetValue(ConfigSetting setting, IReadOnlyList<object> path, object last, JToken value)
        => Edit(setting, path, target =>
        {
            switch (last)
            {
                case int index when target is JArray array && index >= 0 && index < array.Count:
                    array[index] = value;
                    break;
                case string key when target is JObject o:
                    o[key] = value;
                    break;
            }
        });

    public static void Remove(ConfigSetting setting, IReadOnlyList<object> path, object last)
        => Edit(setting, path, target =>
        {
            switch (last)
            {
                case int index when target is JArray array && index >= 0 && index < array.Count:
                    array.RemoveAt(index);
                    break;
                case string key when target is JObject o:
                    o.Remove(key);
                    break;
            }
        });

    /// <summary>
    /// Renames a key, keeping its position. Refused rather than merged when the new name is
    /// already taken: the copied dictionary editors in the wild do Remove then TryAdd, so a
    /// collision silently destroys the entry that was there.
    /// </summary>
    public static bool Rename(ConfigSetting setting, IReadOnlyList<object> path, string from, string to)
    {
        if (from == to) return true;
        if (string.IsNullOrWhiteSpace(to)) return false;

        if (Navigate(setting.Value.Token, path) is not JObject current) return false;
        if (current[to] != null) return false;

        Edit(setting, path, target =>
        {
            if (target is not JObject o || o[from] is not JToken value) return;

            // Rebuilt in order rather than removed and appended, so renaming an entry does
            // not send it to the bottom of the list under the player's cursor.
            List<JProperty> rebuilt = o.Properties()
                .Select(property => property.Name == from ? new JProperty(to, value) : property)
                .ToList();

            o.RemoveAll();
            foreach (JProperty property in rebuilt) o.Add(property);
        });

        return true;
    }

    public static void Add(ConfigSetting setting, IReadOnlyList<object> path, string? key, JToken value)
        => Edit(setting, path, target =>
        {
            switch (target)
            {
                case JArray array:
                    array.Add(value);
                    break;
                case JObject o when key != null:
                    o[key] = value;
                    break;
            }
        });
}

/// <summary>
/// Inventing a key for a new dictionary entry, and saying why when it cannot.
///
/// A hand-rolled editor typically adds an entry under a name that already exists, which
/// silently replaces the one that was there. Picking a free name up front removes the whole
/// class of mistake; when there is no free name to pick - a dictionary keyed by a three
/// member enum that already has three entries - the button is disabled and says so, which
/// is much better than a click that does nothing.
/// </summary>
internal static class KeyGenerator
{
    public static bool TryGenerate(JObject existing, SchemaNode? keyNode, out string key, out string reason)
    {
        key = "";
        reason = "";

        Type? type = keyNode?.MemberType;

        if (type != null && (Nullable.GetUnderlyingType(type) ?? type).IsEnum)
        {
            Type enumType = Nullable.GetUnderlyingType(type) ?? type;

            foreach (string name in Enum.GetNames(enumType))
            {
                if (existing[name] == null)
                {
                    key = name;
                    return true;
                }
            }

            reason = $"every {enumType.Name} already has an entry";
            return false;
        }

        if (type != null && IsWholeNumber(type))
        {
            for (int candidate = 0; candidate < int.MaxValue; candidate++)
            {
                string name = candidate.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (existing[name] == null)
                {
                    key = name;
                    return true;
                }
            }

            reason = "no free number left";
            return false;
        }

        for (int suffix = 1; ; suffix++)
        {
            string candidate = $"new-entry-{suffix}";
            if (existing[candidate] == null)
            {
                key = candidate;
                return true;
            }
        }
    }

    /// <summary>An empty object deserialises into a new instance with all its field initialisers run.</summary>
    public static JToken BlankValue(SchemaNode? node) => node?.Kind switch
    {
        SchemaKind.List => new JArray(),
        SchemaKind.Scalar => BlankScalar(node),
        _ => new JObject()
    };

    private static JToken BlankScalar(SchemaNode node)
    {
        // For a nullable, "nothing yet" is null - not a zero that reads as a value.
        if (node.Nullable) return JValue.CreateNull();

        Type type = Nullable.GetUnderlyingType(node.MemberType) ?? node.MemberType;

        if (type.IsEnum) return new JValue(Enum.GetNames(type).FirstOrDefault() ?? "");

        return node.ScalarType switch
        {
            ConfigSettingType.Boolean => new JValue(false),
            ConfigSettingType.Integer => new JValue(0),
            ConfigSettingType.Float => new JValue(0f),
            _ => new JValue("")
        };
    }

    internal static bool IsWholeNumber(Type type)
    {
        Type actual = Nullable.GetUnderlyingType(type) ?? type;

        return actual == typeof(int) || actual == typeof(long) || actual == typeof(short)
            || actual == typeof(uint) || actual == typeof(ulong) || actual == typeof(ushort)
            || actual == typeof(byte) || actual == typeof(sbyte);
    }
}

/// <summary>
/// What a typed key has to be, given the dictionary's own key type.
///
/// A key is edited as text, but a <c>Dictionary&lt;Difficulty, float&gt;</c> reads its keys
/// back as Difficulty, and a key it cannot read used to be written to the file regardless:
/// the deserialiser then threw on the next load, the exception was caught, and the entry -
/// or the whole dictionary - was silently gone. Reported by TheInsanityGod, who typed into
/// an enum-keyed dictionary and got nothing back but the error being swallowed.
/// </summary>
internal static class KeyRules
{
    /// <summary>
    /// The enum a dictionary's keys are chosen from, or null when they are typed: not an
    /// enum, or a [Flags] enum, whose keys are combinations - "Read, Write" - that no list
    /// of members can offer.
    /// </summary>
    public static Type? EnumType(SchemaNode? keyNode)
    {
        Type? type = KeyType(keyNode);
        return type?.IsEnum == true && !IsFlags(type) ? type : null;
    }

    private static bool IsFlags(Type type) => type.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0;

    private static Type? KeyType(SchemaNode? keyNode)
        => keyNode == null ? null : Nullable.GetUnderlyingType(keyNode.MemberType) ?? keyNode.MemberType;

    /// <summary>
    /// Whether the text can be one of this dictionary's keys, and the spelling the key type
    /// itself uses for it - an enum member in its own case, a number without padding.
    /// </summary>
    public static bool Accepts(SchemaNode? keyNode, string text, out string canonical, out string reason)
    {
        canonical = text;
        reason = "";

        Type? type = KeyType(keyNode);
        if (type == null || type == typeof(string)) return true;

        if (type.IsEnum)
        {
            // A [Flags] key may be a combination, which TryParse reads and IsDefined does
            // not; a bare number is refused for both, because the file would hold a number
            // where every other key is a name.
            if (Enum.TryParse(type, text.Trim(), ignoreCase: true, out object? member) && member != null
                && (IsFlags(type) ? !IsNumeric(member.ToString()!) : Enum.IsDefined(type, member)))
            {
                canonical = member.ToString()!;
                return true;
            }

            reason = IsFlags(type)
                ? $"'{text}' is not a combination of {string.Join(", ", Enum.GetNames(type))}."
                : $"'{text}' is not one of {string.Join(", ", Enum.GetNames(type))}.";
            return false;
        }

        // Anything else with a string form - a number, or AssetLocation - is asked the same
        // way Newtonsoft will ask it when the file is read: through the type's own
        // converter, which knows a byte stops at 255 and a ulong does not stop at long.
        bool number = KeyGenerator.IsWholeNumber(type);

        try
        {
            System.ComponentModel.TypeConverter converter = System.ComponentModel.TypeDescriptor.GetConverter(type);
            if (converter.CanConvertFrom(typeof(string)))
            {
                object? parsed = converter.ConvertFromInvariantString(text.Trim());
                if (number && parsed != null) canonical = Convert.ToString(parsed, System.Globalization.CultureInfo.InvariantCulture)!;
            }
            return true;
        }
        catch (Exception exception)
        {
            reason = number
                ? $"'{text}' is not a whole number a {SchemaBuilder.Describe(type)} can hold."
                : $"'{text}' is not a valid {SchemaBuilder.Describe(type)}: {exception.Message}";
            return false;
        }
    }

    private static bool IsNumeric(string text)
        => text.Length > 0 && text.All(c => char.IsDigit(c) || c == '-');
}

/// <summary>
/// A text field that reports its value when the player leaves it rather than on every
/// keystroke.
///
/// Renaming a dictionary key per keystroke rewrites the dictionary on the way to a name the
/// player has not finished typing - "abc" passes through "a" and "ab", either of which may
/// collide with a key that already exists. Committing on focus loss or Enter means the
/// rename is validated once, against what was actually typed.
/// </summary>
internal sealed class CommittingTextInput : GuiElementTextInput
{
    private readonly Action<string> _onCommit;

    public CommittingTextInput(ICoreClientAPI capi, ElementBounds bounds, Action<string> onCommit, CairoFont font)
        : base(capi, bounds, _ => { }, font)
    {
        _onCommit = onCommit;
    }

    public override void OnFocusLost()
    {
        base.OnFocusLost();
        _onCommit(GetText());
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        base.OnKeyDown(api, args);

        if (args.KeyCode == (int)GlKeys.Enter || args.KeyCode == (int)GlKeys.KeypadEnter)
        {
            _onCommit(GetText());
        }
    }
}
