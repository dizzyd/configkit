// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.
//
// Derived from ConfigLib by Maltiez (https://github.com/maltiez2/vsmod_configlib),
// released under CC0 1.0 Universal. Adapted to drop the Dear ImGui dependency.

using Newtonsoft.Json.Linq;
using ProtoBuf;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ConfigKit;

public class ConfigSetting : ISetting
{
    public JsonObject Value
    {
        get => _value;
        set
        {
            bool changed = _value.ToString() != value.ToString();
            _value = value;
            if (changed)
            {
                ChangedSinceLastSave = true;
                SettingChanged?.Invoke(this);
            }
        }
    }
    public JsonObject DefaultValue { get; internal set; }
    public ConfigSettingType SettingType { get; internal set; }
    public string YamlCode { get; internal set; }
    public string? MappingKey
    {
        get => _mappingKey;
        set
        {
            _mappingKey = value;

            // The key can come from a server running a different version of the mod, where
            // an enum member has been renamed or removed. Indexing blind threw
            // KeyNotFoundException out of a packet handler, which on a client meant the
            // join itself failed.
            if (_mappingKey != null && Validation?.Mapping != null
                && Validation.Mapping.TryGetValue(_mappingKey, out JsonObject? mapped))
            {
                Value = mapped;
            }
        }
    }
    public string? Comment { get; internal set; }

    /// <summary>
    /// A .NET format string for writing this setting's number out - "P", "N2" and friends,
    /// from [DisplayFormat]. Display only; the stored value is never formatted.
    /// </summary>
    public string? Format { get; internal set; }

    /// <summary>
    /// Whether null is one of this setting's values, as against the absence of one. True only
    /// for a nullable value type; see SchemaNode.Nullable.
    /// </summary>
    public bool Nullable { get; internal set; }

    /// <summary>Whether this setting currently holds null.</summary>
    public bool IsNull => Value.Token == null || Value.Token.Type == JTokenType.Null;

    /// <summary>
    /// Why this setting's current value is not acceptable, from the author's own validation
    /// attributes, or null when it is fine. An invalid value stays here to be seen and
    /// corrected; it is not assigned onto the config object.
    /// </summary>
    public string? Error { get; internal set; }

    /// <summary>This setting's value as the member's own type. For the validators.</summary>
    internal object? CoercedValue(Type memberType) => CoerceTo(memberType);
    /// <summary>
    /// The member this setting was reflected from, for a managed config. Null for one built
    /// from a definition file, which has no class behind it.
    ///
    /// The schema used to live in a dictionary beside the settings, keyed by code, and the
    /// two had to be kept in step by hand - which they were not: it indexed only scalars, so
    /// every container failed to find its member and silently never reached the object.
    /// </summary>
    internal SchemaNode? Node { get; set; }

    /// <summary>
    /// Codes this setting also answers to when reading a file, and never writes. A member
    /// renamed - by [JsonProperty], or by the author - has its old name sitting in every
    /// existing config file, and re-keying without reading the old one back would silently
    /// reset the value to its default with nothing in the log to say so.
    /// </summary>
    public IReadOnlyList<string> LegacyCodes => Node?.LegacyPaths ?? (IReadOnlyList<string>)[];
    public Validation? Validation { get; internal set; }
    public float SortingWeight { get; internal set; }
    public string? InGui { get; internal set; }
    public bool Logarithmic { get; internal set; }
    public bool ClientSide { get; internal set; }
    /// <summary>Shown but not editable: [ReadOnly(true)], or a member with no way to assign it.</summary>
    public bool ReadOnly { get; internal set; }
    public bool Hide { get; internal set; }
    public string Link { get; internal set; } = "";
    public bool ChangedSinceLastSave {
        get;
        internal set;
    }

    public event Action<ConfigSetting>? SettingChanged;

    public ConfigSetting(string yamlCode, JsonObject defaultValue, ConfigSettingType settingType)
    {
        _value = defaultValue;
        DefaultValue = defaultValue;
        SettingType = settingType;
        YamlCode = yamlCode;
        InGui = yamlCode;
        Hide = false;
    }
    public ConfigSetting(ConfigSettingPacket settings)
    {
        _value = new(Unwrap(JObject.Parse(settings.Value)));
        DefaultValue = _value;
        SettingType = ConfigSettingType.None;
        MappingKey = settings.MappingKey;
        ClientSide = settings.ClientSide;
        YamlCode = string.Empty;
        Hide = false;
    }
    private ConfigSetting(ConfigSetting previous)
    {
        _value = previous.Value.Clone();
        DefaultValue = previous.DefaultValue.Clone();
        SettingType = previous.SettingType;
        YamlCode = previous.YamlCode;
        MappingKey = previous.MappingKey;
        Comment = previous.Comment;
        Format = previous.Format;
        Nullable = previous.Nullable;
        Validation = previous.Validation;
        SortingWeight = previous.SortingWeight;
        InGui = previous.InGui;
        Logarithmic = previous.Logarithmic;
        ClientSide = previous.ClientSide;
        SettingChanged = previous.SettingChanged;
        Hide = previous.Hide;
        ReadOnly = previous.ReadOnly;
        Link = previous.Link;
        Node = previous.Node;
    }

    /// <summary>
    /// Assigns this setting's value to one specific member of one specific object, rather
    /// than searching a type for a member whose name matches the code. A flattened config
    /// has codes like "Thirst/HungerRate", which match no member name anywhere - the schema
    /// knows which member each setting came from, so it does not have to guess.
    /// </summary>
    internal bool AssignTo(object owner, MemberInfo member) => member switch
    {
        PropertyInfo property => AssignSettingValue(owner, property),
        FieldInfo field => AssignSettingValue(owner, field),
        _ => false
    };

    internal void Changed() => Debug.Write("");// SettingChanged?.Invoke(this);
    internal void SetValueFrom(ConfigSetting value)
    {
        Value = value._value.Clone();
        MappingKey = value.MappingKey;
    }

    public static implicit operator ConfigSettingPacket(ConfigSetting setting) => new(setting);

    public ConfigSetting Clone() => new(this);

    public bool AssignSettingValue(object target)
    {
        Type targetType = target.GetType();

        IEnumerable<(string code, FieldInfo field)> fields = targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(field => (NormalizeName(field.Name), field));
        IEnumerable<(string code, PropertyInfo field)> properties = targetType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(property => property.CanWrite).Select(property => (NormalizeName(property.Name), property));

        return AssignSettingValue(target, fields, properties);
    }
    internal bool AssignSettingValue(object target, IEnumerable<(string code, FieldInfo field)> fields, IEnumerable<(string code, PropertyInfo field)> properties)
    {
        string code = NormalizeName(YamlCode);

        bool anyAssigned = false;

        foreach ((_, FieldInfo? field) in fields.Where(entry => entry.code == code))
        {
            if (AssignSettingValue(target, field))
            {
                anyAssigned = true;
            }
        }

        foreach ((_, PropertyInfo? field) in properties.Where(entry => entry.code == code))
        {
            if (AssignSettingValue(target, field))
            {
                anyAssigned = true;
            }
        }

        return anyAssigned;
    }

    internal static string NormalizeName(string name) => name.ToLowerInvariant().Replace("_", "");
    private bool AssignSettingValue(object target, PropertyInfo? property)
    {
        if (property == null) return false;

        if (!property.CanWrite)
        {
            return SettingType == ConfigSettingType.Other
                && property.CanRead
                && FillInPlace(property.GetValue(target));
        }

        object? value = CoerceTo(property.PropertyType);
        if (value == null && !CanTakeNull(property.PropertyType)) return false;

        property.SetValue(target, value);
        return true;
    }

    private bool AssignSettingValue(object target, FieldInfo? field)
    {
        if (field == null) return false;

        if (field.IsInitOnly)
        {
            return SettingType == ConfigSettingType.Other && FillInPlace(field.GetValue(target));
        }

        object? value = CoerceTo(field.FieldType);
        if (value == null && !CanTakeNull(field.FieldType)) return false;

        field.SetValue(target, value);
        return true;
    }

    /// <summary>
    /// Whether what is stored is literally null, as against a value that failed to convert.
    /// The two both arrive at the member as null and mean opposite things: one is the
    /// author's own "not set", the other is a conversion this library could not do, where
    /// leaving the class default alone is the safer answer.
    /// </summary>
    private bool StoredNull => Value.Token == null || Value.Token.Type == JTokenType.Null;

    /// <summary>
    /// Converts this setting's value to the member's own declared type.    /// <summary>
    /// Whether null may be written to a member of this type. A reference type always takes
    /// it; a nullable value type takes it only when the null is really what was stored -
    /// Nullable&lt;int&gt; reports IsValueType true, which is why an int? left unset used to be
    /// skipped rather than nulled. A plain value type never does, because assigning null to
    /// an int field throws and would cost the member its value entirely.
    /// </summary>
    private bool CanTakeNull(Type memberType)
        => !memberType.IsValueType
           || (System.Nullable.GetUnderlyingType(memberType) != null && StoredNull);


    ///
    /// The settings model has one floating point type and one integer type, but a config
    /// class does not: a <c>double</c> field is classified as Float, and assigning a
    /// <c>float</c> to it throws ArgumentException - which, during registration, takes the
    /// mod's whole config with it. Enums have the same problem in the other direction, and
    /// so does a field typed <c>long</c> or <c>byte</c>. Converting to the target type
    /// rather than assuming it is what the model happened to pick keeps all of them working.
    /// </summary>
    private object? CoerceTo(Type memberType)
    {
        Type type = System.Nullable.GetUnderlyingType(memberType) ?? memberType;

        // A mapped setting - an enum is modelled as one - keeps the mapping key in Value and
        // the value it stands for in the mapping. Reading Value directly yields the key's
        // name, which converts to 0 for every member of the enum.
        JsonObject value = Value;
        if (MappingKey != null && Validation?.Mapping != null
            && Validation.Mapping.TryGetValue(MappingKey, out JsonObject? mapped))
        {
            value = mapped;
        }

        // A stored null is a value, not an absence. A mod that declares a nullable member and
        // distinguishes "no code set" from "the empty code" gets that distinction back;
        // coercing null to "" or 0 changes what its own config says without telling it.
        // Only where null actually fits: a non-nullable value type keeps the old coercion,
        // because assigning null to an int field throws and would drop the member entirely.
        if (value.Token == null || value.Token.Type == JTokenType.Null)
        {
            if (!memberType.IsValueType || System.Nullable.GetUnderlyingType(memberType) != null) return null;
        }

        try
        {
            if (type.IsEnum)
            {
                // A [Flags] value is stored by name ("North, South"); a plain enum resolves
                // through its mapping to a number.
                string? name = value.Token?.Type == JTokenType.String ? value.AsString() : null;

                return string.IsNullOrWhiteSpace(name)
                    ? Enum.ToObject(type, value.AsInt())
                    : Enum.Parse(type, name, ignoreCase: true);
            }

            return SettingType switch
            {
                ConfigSettingType.Boolean => Convert.ChangeType(value.AsBool(), type, CultureInfo.InvariantCulture),
                ConfigSettingType.Float => Convert.ChangeType(value.AsFloat(), type, CultureInfo.InvariantCulture),
                ConfigSettingType.Integer => Convert.ChangeType(value.AsInt(), type, CultureInfo.InvariantCulture),
                ConfigSettingType.String => FromString(value.AsString(""), type),
                ConfigSettingType.Color => FromString(value.AsString(""), type),
                ConfigSettingType.Other => Structured(value, type),
                _ => null
            };
        }
        catch (Exception)
        {
            // A config class can declare a type the setting simply cannot become. Skipping
            // that one member is right; taking down the registration is not.
            return null;
        }
    }

    /// <summary>
    /// A string setting whose member is not a string. AssetLocation and friends carry a
    /// TypeConverter and are stored as their string form, and Convert.ChangeType cannot build
    /// one because they are not IConvertible.
    /// </summary>
    private static object? FromString(string text, Type type)
    {
        if (type == typeof(string)) return text;

        TypeConverter converter = TypeDescriptor.GetConverter(type);
        if (converter.CanConvertFrom(typeof(string)))
        {
            return converter.ConvertFromInvariantString(text);
        }

        return Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A whole subtree - a dictionary, a list, or anything else with no editor of its own.
    /// Newtonsoft builds whatever the member declared, which is what makes a
    /// Dictionary&lt;AssetLocation, T&gt; or a Dictionary&lt;string, JToken&gt; work without
    /// this library knowing anything about either.
    /// </summary>
    private object? Structured(JsonObject value, Type type)
    {
        // JsonObject's own conversion, for the members that actually want game attributes -
        // through the null strip, because a tree holding a null cannot be serialised and the
        // mod receiving it would be handed a landmine rather than a config.
        if (typeof(ITreeAttribute).IsAssignableFrom(type)) return Attributes.For(value);

        return value.Token?.ToObject(type);
    }

    /// <summary>
    /// Fills a collection the member will not let us replace - <c>public List&lt;string&gt; X
    /// { get; } = new();</c> is a common shape, and assignment simply cannot reach it. Clearing
    /// and refilling also keeps any reference the mod already took to the collection live.
    /// </summary>
    private bool FillInPlace(object? existing)
    {
        if (existing == null) return false;

        object? fresh = Value.Token?.ToObject(existing.GetType());
        if (fresh == null) return false;

        switch (existing)
        {
            case System.Collections.IDictionary target when fresh is System.Collections.IDictionary source:
                target.Clear();
                foreach (System.Collections.DictionaryEntry entry in source) target[entry.Key] = entry.Value;
                return true;

            case System.Collections.IList target when fresh is System.Collections.IList source:
                target.Clear();
                foreach (object? item in source) target.Add(item);
                return true;

            default:
                return false;
        }
    }

    internal string ToYaml()
    {
        string yamlToken = SerializeToken();
        yamlToken = AddComments(yamlToken);
        return yamlToken;
    }

    #region YAML
    private string SerializeToken()
    {
        JProperty token;
        if (MappingKey != null)
        {
            token = new(YamlCode, new JValue(MappingKey));
        }
        else
        {
            token = new(YamlCode, Value.Token);
        }


        JObject tokenObject = new()
        {
            token
        };

        object simplifiedToken = ConvertJTokenToObject(tokenObject);

        YamlDotNet.Serialization.Serializer serializer = new();

        using StringWriter writer = new();
        serializer.Serialize(writer, simplifiedToken);
        string yaml = writer.ToString();
        return yaml;
    }
    static object ConvertJTokenToObject(JToken token)
    {
        if (token is JValue value && value.Value != null)
            return value.Value;
        if (token is JArray)
            return token.AsEnumerable().Select(ConvertJTokenToObject).ToList();
        if (token is JObject)
            return token.AsEnumerable().Cast<JProperty>().ToDictionary(x => x.Name, x => ConvertJTokenToObject(x.Value));
        throw new InvalidOperationException("Unexpected token: " + token);
    }

    private string AddComments(string yamlToken)
    {
        (string first, string other) = SplitToken(yamlToken);
        string comment = GetPreComment();
        if (comment != "") comment += "\n";
        if (Link != "") comment += $"# {Link}\n";
        string inline = GetInlineComment();
        if (inline != "") inline = $" # {inline}";
        string defaultValue = GetDefaultValueComment();
        if (defaultValue != "" && inline == "") defaultValue = $" # {defaultValue}";
        return $"{comment}{first}{inline}{defaultValue}{other}";
    }
    private static (string firstLine, string remainder) SplitToken(string token)
    {
        string[] split = token.Split("\r\n", 2, StringSplitOptions.RemoveEmptyEntries);

        return (split[0], split.Length > 1 ? $"\r\n{split[1]}" : "");
    }
    private string GetPreComment()
    {
        return Comment == null ? "" : $"\n# {Comment.Replace("\n", "\n# ")}";
    }
    private string GetDefaultValueComment()
    {
        return $" (default: {DefaultValue}) ";
    }
    private string GetInlineComment()
    {
        if (Validation?.Mapping != null)
        {
            return ParseMappingComment();
        }

        if (Validation?.Values != null)
        {
            return ParseValuesComment();
        }

        return ParseRangeComment();
    }
    private string ParseMappingComment()
    {
        if (Validation == null || Validation.Mapping == null) return "";

        StringBuilder result = new();
        result.Append("value from: ");
        bool first = true;
        foreach ((string name, _) in Validation.Mapping)
        {
            if (!first) result.Append(", ");
            if (first) first = false;
            result.Append(name);
        }
        return result.ToString();
    }
    private string ParseRangeComment()
    {
        if (Validation == null || (Validation.Minimum == null && Validation.Maximum == null && Validation.Step == null)) return "";

        string minMax = (Validation.Minimum != null, Validation.Maximum != null) switch
        {
            (true, true) => $"from {Validation.Minimum} to {Validation.Maximum}",
            (true, false) => $"greater than {Validation.Minimum}",
            (false, true) => $"less than {Validation.Maximum}",
            _ => ""
        };

        if (Validation.Step != null && minMax != "")
        {
            return $"{minMax} with step of {Validation.Step}";
        }
        else
        {
            return minMax;
        }
    }
    private string ParseValuesComment()
    {
        if (Validation == null || Validation.Values == null) return "";

        StringBuilder result = new();
        result.Append("value from: ");
        bool first = true;
        foreach (string value in Validation.Values.Select(element => element.ToString()))
        {
            if (!first) result.Append(", ");
            if (first) first = false;
            result.Append(value);
        }
        return result.ToString();
    }
    #endregion

    private JsonObject _value;
    private string? _mappingKey;

    private static JToken Unwrap(JObject token)
    {
        if (token["value"] is not JToken value) return new JValue("<invalid>");
        return value;
    }
    private static string Localize(string value, string domain)
    {
        bool hasDomain = value.Contains(':');
        string langCode = hasDomain ? value : $"{domain}:{value}";
        return Lang.HasTranslation(langCode) ? Lang.Get(langCode) : Lang.Get(value);
    }
    internal static ConfigSetting FromJson(JsonObject json, ConfigSettingType settingType, string domain, string code, ICoreAPI api)
    {
        if (!json.KeyExists("default")) LoggerUtil.Error(api, typeof(ConfigSetting), $"Setting '{domain}' of type '{settingType}' does not have default value");

        ConfigSetting setting = new(
            yamlCode: json["name"].AsString(code),
            defaultValue: json["default"],
            settingType
            )
        {
            Value = json["default"],
            Comment = json["comment"].AsString(),
            Format = json["format"].AsString(),
            Nullable = json["nullable"].AsBool(false),
            InGui = json["ingui"].AsString(json["nameInGui"].AsString(json["name"].AsString(code))),
            ClientSide = json["clientSide"].AsBool(false),
            SortingWeight = json["weight"].AsFloat(0),
            Logarithmic = json["logarithmic"].AsBool(false),
            Hide = json["hide"].AsBool(false),
            ReadOnly = json["readonly"].AsBool(false),
            Link = json["link"].AsString(""),
        };

        if (setting.InGui != null) setting.InGui = Localize(setting.InGui, domain);
        if (setting.Comment != null) setting.Comment = Localize(setting.Comment, domain);

        (string? mappingKey, JsonObject? value, Validation? validation) = Validation.FromJson(json, setting.Value.AsString());
        if (value != null) setting.Value = value;
        setting.MappingKey = mappingKey;
        setting.Validation = validation;

        return setting;
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ConfigSettingPacket
{
    public string Value { get; set; } = string.Empty;
    public string? MappingKey { get; set; }
    public bool ClientSide { get; private set; }

    public ConfigSettingPacket() { }
    public ConfigSettingPacket(ConfigSetting settings)
    {
        Value = Wrap(settings.Value.Token).ToString();
        MappingKey = settings.MappingKey;
        ClientSide = settings.ClientSide;
    }

    public static implicit operator ConfigSetting(ConfigSettingPacket setting) => new(setting);

    private static JObject Wrap(JToken token)
    {
        JObject result = new()
        {
            { "value", token }
        };
        return result;
    }
}

public class Validation
{
    public Dictionary<string, JsonObject>? Mapping { get; private set; }
    public JsonObject? Minimum { get; private set; }
    public JsonObject? Maximum { get; private set; }
    public JsonObject? Step { get; private set; }
    public List<JsonObject>? Values { get; private set; }

    public Validation() { }
    public Validation(Dictionary<string, JsonObject> mapping) => Mapping = mapping;
    public Validation(List<JsonObject> values) => Values = values;
    public Validation(JsonObject? min, JsonObject? max = null, JsonObject? step = null)
    {
        Minimum = min;
        Maximum = max;
        Step = step;
    }

    internal static (string? mappingKey, JsonObject? value, Validation? validation) FromJson(JsonObject json, string? value)
    {
        Validation validation = new();

        // A nullable enum left unset has no value to look up, and skipping the mapping for it
        // cost the setting its dropdown - it fell through to the number input, where a player
        // could not pick a member at all.
        //
        // Only for a nullable one. AsString returns null for any value that is not literally
        // a string, so a definition-based setting mapped by number reaches here with a null
        // value too, and treating that as mapped changes what it reads - the compatibility
        // baseline caught exactly that, a definition's "tier" going from 5 to 2.
        if (json.KeyExists("mapping") && (value != null || json["nullable"].AsBool(false)))
        {
            SetMapping(json["mapping"], validation);

            return value != null && validation.Mapping!.TryGetValue(value, out JsonObject? mapped)
                ? (value, mapped, validation)
                : (null, null, validation);
        }

        if (json.KeyExists("range"))
        {
            SetRange(json["range"], validation);
            return (null, null, validation);
        }

        if (json.KeyExists("values"))
        {
            SetValues(json["values"].AsArray(), validation);
            return (null, null, validation);
        }

        return (null, null, null);
    }

    private static void SetMapping(JsonObject mapping, Validation validation)
    {
        if (mapping.Token is not JObject mappingObject)
        {
            throw new InvalidConfigException($"Mapping has wrong format");
        }

        validation.Mapping = new();

        foreach ((string key, JToken? mappingValue) in mappingObject)
        {
            if (mappingValue == null)
            {
                throw new InvalidConfigException($"Mapping value for entry '{key}' is not valid value");
            }

            validation.Mapping.Add(key, new(mappingValue));
        }
    }
    private static void SetRange(JsonObject range, Validation validation)
    {
        JsonObject? min = range.KeyExists("min") ? range["min"] : null;
        JsonObject? max = range.KeyExists("max") ? range["max"] : null;
        JsonObject? step = range.KeyExists("step") ? range["step"] : null;

        validation.Minimum = min;
        validation.Maximum = max;
        validation.Step = step;
    }
    private static void SetValues(JsonObject[] values, Validation validation)
    {
        validation.Values = values.ToList();
    }
}