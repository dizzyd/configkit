// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.
//
// Derived from ConfigLib by Maltiez (https://github.com/maltiez2/vsmod_configlib),
// released under CC0 1.0 Universal. Adapted to drop the Dear ImGui dependency.

using ConfigKit.Formatting;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using YamlDotNet.Serialization;

namespace ConfigKit;

public sealed class Config : IConfig, IDisposable
{
    public string ConfigFilePath { get; private set; }
    public int Version { get; private set; }

    public event Action<Config>? ConfigSaved;

    public event Action<ISetting>? SettingChanged;

    public Config(ICoreAPI api, string domain, string modName, JsonObject json)
    {
        _api = api;
        _domain = domain;
        _modName = modName;
        _json = json;

        RelativeFilePath = $"{_domain}.yaml";
        ConfigFilePath = ConfigPathFor(_api, RelativeFilePath);

        try
        {
            Parse(json, out _settings, out _configBlocks, out _defaultYaml, domain);
            _clientSideSettings = new(_settings);   // a copy: aliasing lets Clear() empty _settings
            WriteToFile();
            _patches = new(api, this);
            CreateFileWatcher();
            SubscribeToSettingsChanges();
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"[ConfigKit] ({domain}) Error on parsing config: {exception}.");
            _patches = new(api, this);
            _settings = new();
            _configBlocks = new();
            _defaultYaml = "";
        }
    }
    public Config(ICoreAPI api, string domain, string modName, JsonObject json, Dictionary<string, ConfigSetting> serverSideSettings)
    {
        _api = api;
        _domain = domain;
        _modName = modName;
        _json = json;

        RelativeFilePath = $"{_domain}.yaml";
        ConfigFilePath = ConfigPathFor(_api, RelativeFilePath);

        try
        {
            Parse(json, out _settings, out _configBlocks, out _defaultYaml, domain, checkVersion: false);
            DistributeSettingsBySides(serverSideSettings);
            WriteToFile();
            _patches = new(api, this);
            CreateFileWatcher();
            SubscribeToSettingsChanges();
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"[ConfigKit] ({domain}) Error on parsing config: {exception}.");
            _patches = new(api, this);
            _settings = new();
            _configBlocks = new();
            _defaultYaml = "";
        }
    }
    public Config(ICoreAPI api, string domain, string modName, JsonObject json, string file)
    {
        _api = api;
        _domain = domain;
        _modName = modName;
        _json = json;
        RelativeFilePath = file;
        ConfigFilePath = ConfigPathFor(_api, file);
        JsonFilePath = ConfigFilePath;
        _configType = ConfigType.JSON;

        try
        {
            ParseJson(json, out _settings, out _configBlocks, out _defaultJson, domain);
            _clientSideSettings = new(_settings);   // a copy: aliasing lets Clear() empty _settings
            _patches = new(api, this);
            CreateFileWatcher();
            SubscribeToSettingsChanges();
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"[ConfigKit] ({domain}) Error on parsing config: {exception}.");
            _patches = new(api, this);
            _settings = new();
            _configBlocks = new();
            _defaultYaml = "";
        }
    }
    public Config(ICoreAPI api, string domain, string modName, JsonObject json, string file, Dictionary<string, ConfigSetting> serverSideSettings)
    {
        _api = api;
        _domain = domain;
        _modName = modName;
        _json = json;
        RelativeFilePath = file;
        ConfigFilePath = ConfigPathFor(_api, file);
        JsonFilePath = ConfigFilePath;
        _configType = ConfigType.JSON;
        JsonFilePath = file;

        try
        {
            ParseJson(json, out _settings, out _configBlocks, out _defaultJson, domain);
            DistributeSettingsBySides(serverSideSettings);
            _patches = new(api, this);
            CreateFileWatcher();
            SubscribeToSettingsChanges();
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"[ConfigKit] ({domain}) Error on parsing config: {exception}.");
            _patches = new(api, this);
            _settings = new();
            _configBlocks = new();
            _defaultYaml = "";
        }
    }
    public Config(ICoreAPI api, string domain, string modName, object configObject, string file)
    {
        _api = api;
        _domain = domain;
        _modName = modName;
        _json = DefinitionFromObject(configObject, domain);
        RelativeFilePath = file;
        ConfigFilePath = ConfigPathFor(_api, file);
        JsonFilePath = ConfigFilePath;
        _configType = ConfigType.JSON;

        try
        {
            // The author's own object, serialised, is the document written when no config
            // file exists yet. It carries the exact nested shape - including every member
            // that has no setting of its own - so a path like "Thirst/HungerRate" has
            // something to be written into, and the file matches what the mod's own
            // StoreModConfig would have produced.
            ParseJson(_json, out _settings, out _configBlocks, out _defaultJson, domain, Skeleton(configObject));
            _clientSideSettings = new(_settings);   // a copy: aliasing lets Clear() empty _settings
            _patches = new(api, this);
            WriteToFile();
            CreateFileWatcher();
            SubscribeToSettingsChanges();
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"[ConfigKit] ({domain}) Error on parsing config: {exception}.");
            _patches = new(api, this);
            _settings = new();
            _configBlocks = new();
            _defaultYaml = "";
        }
    }

    internal void Apply() => _patches.Apply();

    /// <summary>
    /// The settings definition this config was built from. For a managed config this is what
    /// ConfigKit made of the registered class, which is the first thing worth looking at when
    /// a setting does not appear where its author expected.
    /// </summary>
    public JsonObject Definition => _json;
    internal SortedDictionary<float, IConfigBlock> ConfigBlocks => _configBlocks;
    internal Dictionary<string, ConfigSetting> Settings => _settings;
    internal ConfigType FileType => _configType;
    internal string JsonFilePath { get; } = "";
    internal string RelativeFilePath { get; } = "";
    internal string Domain => _domain;
    internal string ModName => _modName;
    /// <summary>The shape of the registered settings object, or null for a definition-driven config.</summary>
    internal ConfigSchema? Schema => _schema;

    /// <summary>
    /// What ConfigKit made of the registered class, in one line: how many settings, sections
    /// and containers it found, and how many members it could not make editable. Empty for a
    /// definition-driven config.
    /// </summary>
    public string SchemaSummary => _schema?.Summary() ?? "";

    /// <summary>
    /// Every member that is not an ordinary editable setting, and why. The rule this serves is
    /// that nothing is dropped in silence - a member ConfigKit cannot render is reported here
    /// and in the log, never simply absent.
    /// </summary>
    public IReadOnlyList<string> SchemaNotices => _schema?.Notices ?? [];

    /// <summary>Codes of every setting in this config, in no particular order.</summary>
    public IEnumerable<string> SettingCodes => _settings.Keys;

    public ISetting? GetSetting(string code)
    {
        if (!_settings.ContainsKey(code)) return null;
        return _settings[code];
    }
    public void WriteToFile()
    {
        try
        {
            string content = "";

            switch (_configType)
            {
                case ConfigType.YAML:
                    if (_api is ICoreClientAPI { IsSinglePlayer: false })
                    {
                        content = ToYaml(_clientSideSettings.Values);
                    }
                    else
                    {
                        content = ToYaml(_settings.Values);
                    }
                    break;
                case ConfigType.JSON:
                    if (_api is ICoreClientAPI { IsSinglePlayer: false })
                    {
                        content = ToJson(_settings.Values, ReadConfigFile(_defaultJson, false), onlyClientSide: true);
                    }
                    else
                    {
                        content = ToJson(_settings.Values, ReadConfigFile(_defaultJson, false));
                    }
                    break;
            }

            WriteConfigFile(content);
            ConfigSaved?.Invoke(this);
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"Exception when trying to deserialize yaml and write it to file for '{_domain}' config.\nException: {exception}\n");
        }

    }
    public bool ReadFromFile() => ReadFromFile(true);
    public bool ReadFromFile(bool overrideOnFail)
    {
        try
        {
            string content = ReadConfigFile(_defaultYaml, overrideOnFail);

            switch (_configType)
            {
                case ConfigType.YAML:
                    if (_api is ICoreClientAPI { IsSinglePlayer: false })
                    {
                        return FromYaml(_clientSideSettings.Values, content);
                    }
                    else
                    {
                        return FromYaml(_settings.Values, content);
                    }
                case ConfigType.JSON:
                    if (_api is ICoreClientAPI { IsSinglePlayer: false })
                    {
                        return FromJson(_settings.Values, content, onlyClientSide: true);
                    }
                    else
                    {
                        return FromJson(_settings.Values, content);
                    }
            }

            return false;
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"Exception when trying read YAML file and parse it for'{_domain}' config.\nException: {exception}\n");
            return false;
        }
    }
    public bool TryReadFromFile()
    {
        try
        {
            if (!ReadConfigFile(out string content)) return false;

            switch (_configType)
            {
                case ConfigType.YAML:
                    if (_api is ICoreClientAPI { IsSinglePlayer: false })
                    {
                        return FromYaml(_clientSideSettings.Values, content);
                    }
                    else
                    {
                        return FromYaml(_settings.Values, content);
                    }
                case ConfigType.JSON:
                    if (_api is ICoreClientAPI { IsSinglePlayer: false })
                    {
                        return FromJson(_settings.Values, content, onlyClientSide: true);
                    }
                    else
                    {
                        return FromJson(_settings.Values, content);
                    }
            }

            return false;
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"Exception when trying read '{_configType}' file and parse it for'{_domain}' config.\nException: {exception}\n");
            return false;
        }
    }
    public void RestoreToDefaults()
    {
        try
        {
            switch (_configType)
            {
                case ConfigType.YAML:
                    if (_api is ICoreClientAPI { IsSinglePlayer: false })
                    {
                        FromYaml(_clientSideSettings.Values, _defaultYaml);
                    }
                    else
                    {
                        FromYaml(_settings.Values, _defaultYaml);
                    }
                    break;
                case ConfigType.JSON:
                    if (_api is ICoreClientAPI { IsSinglePlayer: false })
                    {
                        FromJson(_settings.Values, _defaultJson, onlyClientSide: true);
                    }
                    else
                    {
                        FromJson(_settings.Values, _defaultJson);
                    }
                    break;
            }
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"Exception when trying to restore settings to defaults for '{_domain}' config.\nException: {exception}\n");
        }
    }
    public void AssignSettingsValues(object target)
    {
        if (_schema != null && _schema.Root.IsInstanceOfType(target))
        {
            AssignBySchema(target);
            return;
        }

        Type targetType = target.GetType();

        IEnumerable<(string code, FieldInfo field)> fields = targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(field => (ConfigSetting.NormalizeName(field.Name), field));
        IEnumerable<(string code, PropertyInfo field)> properties = targetType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(property => property.CanWrite).Select(property => (ConfigSetting.NormalizeName(property.Name), property));

        foreach ((_, ConfigSetting? setting) in _settings)
        {
            try
            {
                setting.AssignSettingValue(target, fields, properties);
            }
            catch (Exception exception)
            {
                LoggerUtil.Error(_api, this, $"Exception on assigning value for setting '{setting.YamlCode}' for config '{_domain}'.\nException: {exception}");
            }
        }
    }
    /// <summary>
    /// Assigns every setting onto the object it was reflected from, using the schema rather
    /// than matching a code against member names. A flattened config has codes like
    /// "Thirst/HungerRate", which match no member name anywhere.
    /// </summary>
    private void AssignBySchema(object target)
    {
        foreach ((string code, ConfigSetting setting) in _settings)
        {
            try
            {
                AssignBySchema(target, code, setting);
            }
            catch (Exception exception)
            {
                LoggerUtil.Error(_api, this, $"Exception on assigning value for setting '{code}' for config '{_domain}'.\nException: {exception}");
            }
        }
    }

    private bool AssignBySchema(object target, string code, ConfigSetting setting)
    {
        if (!_nodesByPath.TryGetValue(code, out SchemaNode? node)) return false;

        object? owner = OwnerFor(node, target);
        return owner != null && setting.AssignTo(owner, node.Member);
    }

    /// <summary>
    /// Assigns one setting back onto the registered object. The caller cannot do this itself:
    /// a nested setting's code is a path, and only the schema knows which member it came from.
    /// </summary>
    internal void AssignSettingValue(object target, ConfigSetting setting)
    {
        if (_schema != null && _schema.Root.IsInstanceOfType(target))
        {
            AssignBySchema(target, setting.YamlCode, setting);
            return;
        }

        setting.AssignSettingValue(target);
    }

    /// <summary>Walks down from the config root to the object this node's member lives on.</summary>
    private object? OwnerFor(SchemaNode node, object root)
    {
        if (node.Parent == null) return root;

        object? grandparent = OwnerFor(node.Parent, root);
        return grandparent == null ? null : ResolveOwner(node.Parent, grandparent);
    }

    public void SyncFromServer(Config config, bool isSinglePlayer)
    {
        if (isSinglePlayer)
        {
            foreach ((string code, ConfigSetting setting) in _settings)
            {
                bool serverSide = config.Settings.ContainsKey(code) && !config.Settings[code].ClientSide;

                if (serverSide)
                {
                    _settings[code].SetValueFrom(config.Settings[code]);
                }
            }
        }
        else
        {
            // Snapshot first. This rebuilds _clientSideSettings from _settings, and the two
            // must not be the same object or the clear below empties what the loop reads -
            // which silently left a synced config with no settings at all on a real server.
            Dictionary<string, ConfigSetting> current = new(_settings);
            _clientSideSettings.Clear();

            foreach ((string code, ConfigSetting setting) in current)
            {
                bool serverSide = config.Settings.ContainsKey(code) && !config.Settings[code].ClientSide;

                if (serverSide)
                {
                    _clientSideSettings.Add(code, setting.Clone());
                    _settings[code].SetValueFrom(config.Settings[code]);
                }
                else
                {
                    _clientSideSettings.Add(code, setting);
                }
            }
        }
    }


    internal enum ConfigType
    {
        YAML,
        JSON
    }

    private readonly ICoreAPI _api;
    private readonly string _domain;
    private readonly string _modName;
    private readonly Dictionary<string, ConfigSetting> _settings;
    private readonly Dictionary<string, ConfigSetting> _clientSideSettings = new();
    private readonly SortedDictionary<float, IConfigBlock> _configBlocks;
    private readonly JsonObject _json;
    private readonly ConfigPatches _patches;
    private readonly string _defaultYaml = "";
    private readonly string _defaultJson = "{}";
    private readonly ConfigType _configType = ConfigType.YAML;
    private ConfigSchema? _schema;
    private Dictionary<string, SchemaNode> _nodesByPath = [];
    private FileSystemWatcher? _configFileWatcher;
    private bool _disposedValue;
    private readonly object _fileChangedLockObject = new();
    private bool _fileChanged = false; // protected by _fileOperationLockObject
    private long _fileChangedListener = 0;
    private const int _fileChangeCheckIntervalMs = 2000;
    private static Dictionary<string, FileSystemWatcher?> _fileWatchers = [];
    private static Dictionary<string, List<Config>> _configsByPath = [];

    private void SubscribeToSettingsChanges()
    {
        foreach ((_, ConfigSetting? setting) in _settings)
        {
            setting.SettingChanged += setting => SettingChanged?.Invoke(setting);
        }
    }
    private void DistributeSettingsBySides(Dictionary<string, ConfigSetting> serverSideSettings)
    {
        foreach ((string code, ConfigSetting setting) in _settings)
        {
            bool serverSide = serverSideSettings.ContainsKey(code) && !serverSideSettings[code].ClientSide;

            if (serverSide)
            {
                _clientSideSettings.Add(code, setting.Clone());
                _settings[code].SetValueFrom(serverSideSettings[code]);
            }
            else
            {
                _clientSideSettings.Add(code, setting);
            }
        }
    }
    private void Parse(JsonObject json, out Dictionary<string, ConfigSetting> settings, out SortedDictionary<float, IConfigBlock> configBlocks, out string defaultConfig, string domain, bool checkVersion = true)
    {
        Version = FromJsonDefinition(json, out settings, out configBlocks, domain);
        defaultConfig = ToYaml(settings.Values);
        string yamlConfig = ReadConfigFile(defaultConfig, true);
        bool valid = FromYaml(settings.Values, yamlConfig);
        if (checkVersion && !valid)
        {
            WriteConfigFile(defaultConfig);
            FromYaml(settings.Values, defaultConfig);
        }
    }
    private void ParseJson(JsonObject json, out Dictionary<string, ConfigSetting> settings, out SortedDictionary<float, IConfigBlock> configBlocks, out string defaultConfig, string domain, string? skeleton = null)
    {
        Version = FromJsonDefinition(json, out settings, out configBlocks, domain);

        // Bind before reading: a setting has to know the codes it used to answer to before
        // anything looks a value up, or a renamed member reads nothing and silently takes
        // its default.
        BindSchemaToSettings(settings);

        // The default document has to be finished before anything reads the file, because
        // reading is what creates the file when there is none - and whatever is written there
        // is then what the first load reads back. Seeding it with the raw skeleton got a
        // [DefaultValue] wrong: the skeleton carries the field's initialiser, which is not the
        // setting's default when an attribute overrides it.
        string baseDocument = skeleton ?? (ReadConfigFile(out string existing) ? existing : "{}");

        JsonObject defaults;
        try
        {
            defaults = new(JObject.Parse(baseDocument));
        }
        catch (Exception exception)
        {
            LoggerUtil.Verbose(_api, this, $"[ParseJson] Error on parsing default document:\n{exception}");
            defaults = new(new JObject());
        }

        foreach (ConfigSetting setting in settings.Values)
        {
            new JsonObjectPath(setting.YamlCode).SetOrCreate(defaults, StoredForm(setting, setting.DefaultValue));
        }

        defaultConfig = defaults.Token.ToString(Newtonsoft.Json.Formatting.Indented);

        string jsonConfig = ReadConfigFile(defaultConfig, false);

        JsonObject jsonConfigObject;

        try
        {
            jsonConfigObject = new(JObject.Parse(jsonConfig));
        }
        catch (Exception exception)
        {
            LoggerUtil.Verbose(_api, this, $"[ParseJson] Error on parsing config file:\n{exception}\nFile content:\n{jsonConfig}");
            throw;
        }

        foreach (ConfigSetting setting in settings.Values)
        {
            setting.Value = ReadStoredValue(jsonConfigObject, setting) ?? setting.DefaultValue;
        }
    }

    /// <summary>
    /// What a setting looks like in the file. A mapped setting - an enum is modelled as one -
    /// is stored as the member name, so a renamed member fails to resolve loudly instead of
    /// silently landing on whatever now holds its old ordinal.
    /// </summary>
    private static JsonObject StoredForm(ConfigSetting setting, JsonObject value)
        => setting.Validation?.Mapping == null ? value : new(new JValue(setting.MappingKey));

    /// <summary>
    /// Reads a setting out of a stored document, falling back to any code it used to be
    /// written under. Aliases are read and never written, so a rename picks the old value up
    /// once and writes it back under the new name on the next save.
    /// </summary>
    private static JsonObject? ReadStoredValue(JsonObject stored, ConfigSetting setting)
    {
        JsonObject? value = new JsonObjectPath(setting.YamlCode).Get(stored).FirstOrDefault((JsonObject?)null);
        if (value != null) return value;

        foreach (string legacy in setting.LegacyCodes)
        {
            value = new JsonObjectPath(legacy).Get(stored).FirstOrDefault((JsonObject?)null);
            if (value != null) return value;
        }

        return null;
    }

    /// <summary>
    /// Hands each setting the metadata that lives on its schema node rather than in the
    /// definition - the aliases it answers to, and the member it assigns back onto.
    /// </summary>
    private void BindSchemaToSettings(Dictionary<string, ConfigSetting> settings)
    {
        if (_schema == null) return;

        _nodesByPath = _schema.Walk()
            .Where(node => node.Kind == SchemaKind.Scalar)
            .ToDictionary(node => node.Path, node => node);

        foreach ((string code, ConfigSetting setting) in settings)
        {
            if (_nodesByPath.TryGetValue(code, out SchemaNode? node) && node.LegacyPaths.Count > 0)
            {
                setting.LegacyCodes = node.LegacyPaths;
            }
        }
    }

    private string? Skeleton(object configObject)
    {
        try
        {
            return JToken.FromObject(configObject).ToString(Newtonsoft.Json.Formatting.Indented);
        }
        catch (Exception exception)
        {
            // A member Newtonsoft cannot serialise should not cost the mod its whole config;
            // without a skeleton the paths are still created by SetOrCreate as they are written.
            LoggerUtil.Verbose(_api, this, $"Could not serialise '{_domain}' config object for its default document: {exception.Message}");
            return null;
        }
    }
    private JsonObject DefinitionFromObject(object configObject, string domain)
    {
        Type configObjectType = configObject.GetType();
        _schema = SchemaBuilder.For(configObjectType);
        MemberInfo[] staticMembers = configObjectType.GetMembers(BindingFlags.Public | BindingFlags.Static);

        JObject root = [];
        JArray settings = [];
        root.Add("settings", settings);

        foreach (MemberInfo member in staticMembers)
        {
            if (member.Name.ToLowerInvariant() != "version")
            {
                continue;
            }

            int? value = (int?)((member as PropertyInfo)?.GetValue(configObject) ?? (member as FieldInfo)?.GetValue(configObject));
            if (value != null)
            {
                root.Add("version", value.Value);
            }
        }

        EmitSettings(_schema!.Nodes, configObject, settings, domain, section: null);

        return new JsonObject(root);
    }

    /// <summary>
    /// Walks the schema in order, emitting one setting block per scalar leaf and a separator
    /// whenever the section changes. A nested object contributes no setting of its own - it
    /// is a heading and a path prefix, and its leaves are ordinary settings with a path for a
    /// code, so every control, validation and reset the flat case already had applies to them
    /// unchanged.
    /// </summary>
    private void EmitSettings(List<SchemaNode> nodes, object? owner, JArray settings, string domain, string? section)
    {
        foreach (SchemaNode node in nodes)
        {
            switch (node.Kind)
            {
                case SchemaKind.Scalar:
                    if (node.Section != section)
                    {
                        section = node.Section;
                        AddSeparator(settings, section);
                    }
                    settings.Add(SettingDefinition(node, owner, domain));
                    break;

                case SchemaKind.Object:
                    object? child = ResolveOwner(node, owner);
                    // Suppress a heading nothing lands under - containers do not become
                    // settings yet, so an object holding only containers has no rows.
                    if (node.Children.Any(HasVisibleLeaf))
                    {
                        section = SchemaBuilder.ChildSection(node);
                        AddSeparator(settings, section);
                        EmitSettings(node.Children, child, settings, domain, section);
                    }
                    break;

                // Containers and anything unclassifiable are reported through the schema's
                // notices rather than emitted. They are not silent; they are just not yet
                // editable.
                default:
                    break;
            }
        }
    }

    private static bool HasVisibleLeaf(SchemaNode node)
        => node.Kind == SchemaKind.Scalar || (node.Kind == SchemaKind.Object && node.Children.Any(HasVisibleLeaf));

    private static void AddSeparator(JArray settings, string? title)
    {
        if (title == null) return;

        settings.Add(new JObject
        {
            { "type", "separator" },
            { "title", title },
            { "collapsible", true }
        });
    }

    /// <summary>
    /// The instance a nested object's leaves hang off. A config class that declares
    /// <c>public ThirstConfig Thirst;</c> without initialising it is holding null, and
    /// reading defaults out of null yields nothing - so create one and give it to the author's
    /// object, which is the state their own code would have needed anyway.
    /// </summary>
    private object? ResolveOwner(SchemaNode node, object? owner)
    {
        if (owner == null) return null;

        object? value = node.Member switch
        {
            PropertyInfo property => property.CanRead ? property.GetValue(owner) : null,
            FieldInfo field => field.GetValue(owner),
            _ => null
        };

        if (value != null) return value;

        try
        {
            value = Activator.CreateInstance(node.MemberType);
            if (value == null) return null;

            switch (node.Member)
            {
                case PropertyInfo property when property.CanWrite: property.SetValue(owner, value); break;
                case FieldInfo field when !field.IsInitOnly: field.SetValue(owner, value); break;
            }
        }
        catch (Exception exception)
        {
            LoggerUtil.Verbose(_api, this, $"Could not create '{node.Path}' ({node.MemberType.Name}): {exception.Message}");
            return null;
        }

        return value;
    }

    private static JObject SettingDefinition(SchemaNode node, object? owner, string domain)
    {
        JObject definition = [];

        definition.Add("code", node.Path);
        definition.Add("ingui", node.Label ?? $"{domain}:setting-{node.Code}");
        definition.Add("type", node.ScalarType.ToString().ToLowerInvariant());
        definition.Add("default", GetDefaultValue(node, owner));

        if (node.Comment != null) definition.Add("comment", node.Comment);
        if (node.ClientSide) definition.Add("clientSide", true);
        if (node.Logarithmic) definition.Add("logarithmic", true);
        if (node.Hidden) definition.Add("hide", true);

        switch (node.ScalarType)
        {
            case ConfigSettingType.Float:
                SetFloatSettingDefinition(node.Member, definition);
                break;
            case ConfigSettingType.Integer:
                SetIntegerSettingDefinition(node.Member, node.MemberType, definition);
                break;
            case ConfigSettingType.String:
                SetStringSettingDefinition(node.Member, definition);
                break;
            default:
                break;
        }

        return definition;
    }

    private static JValue GetDefaultValue(SchemaNode node, object? owner)
    {
        MemberInfo info = node.Member;
        ConfigSettingType settingType = node.ScalarType;

        DefaultValueAttribute? attribute = info.GetCustomAttribute<DefaultValueAttribute>();
        object? value = attribute?.Value
            ?? (owner == null ? null : (info as PropertyInfo)?.GetValue(owner) ?? (info as FieldInfo)?.GetValue(owner));

        if (value == null) return new(value);

        // Cast, not convert, was the old behaviour here, and an unboxing cast demands the
        // exact type: a double field, an enum, a long, or a [DefaultValue(1)] on a float
        // field all threw InvalidCastException and took the mod's whole registration with
        // them. Convert handles every boxed numeric and enum.
        try
        {
            if (value is Enum) return new JValue(Convert.ToInt32(value, CultureInfo.InvariantCulture));

            return settingType switch
            {
                ConfigSettingType.Boolean => new JValue(Convert.ToBoolean(value, CultureInfo.InvariantCulture)),
                ConfigSettingType.Float => new JValue(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
                ConfigSettingType.Integer => new JValue(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
                ConfigSettingType.String => new JValue(Convert.ToString(value, CultureInfo.InvariantCulture)),
                _ => new(value)
            };
        }
        catch (Exception)
        {
            return new JValue(value);
        }
    }
    private static void SetFloatSettingDefinition(MemberInfo info, JObject definition)
    {
        RangeAttribute? rangeAttribute = info.GetCustomAttribute<RangeAttribute>();
        if (rangeAttribute != null)
        {
            JObject range = new()
            {
                { "min", Convert.ToSingle(rangeAttribute.Minimum) },
                { "max", Convert.ToSingle(rangeAttribute.Maximum) }
            };
            definition.Add("range", range);
        }

        AllowedValuesAttribute? allowedValuesAttribute = info.GetCustomAttribute<AllowedValuesAttribute>();
        if (allowedValuesAttribute != null)
        {
            IEnumerable<float> allowedValues = allowedValuesAttribute.Values.Select(Convert.ToSingle);
            JArray values = new(allowedValues);
            definition.Add("values", values);
        }
    }
    private static void SetIntegerSettingDefinition(MemberInfo info, Type memberType, JObject definition)
    {
        RangeAttribute? rangeAttribute = info.GetCustomAttribute<RangeAttribute>();
        if (rangeAttribute != null)
        {
            JObject range = new()
            {
                { "min", Convert.ToInt32(rangeAttribute.Minimum) },
                { "max", Convert.ToInt32(rangeAttribute.Maximum) }
            };
            definition.Add("range", range);
        }

        Type? valueType = Nullable.GetUnderlyingType(memberType) ?? memberType;
        if (valueType?.IsEnum == true)
        {
            string[] enumNames = valueType.GetEnumNames();
            int[] enumValues = (int[])valueType.GetEnumValues();

            if (enumNames.Length == enumValues.Length)
            {
                JObject mapping = [];
                for (int index = 0; index < enumNames.Length; index++)
                {
                    mapping.Add(enumNames[index], enumValues[index]);
                }
                definition.Add("mapping", mapping);
                int indexClamped = Math.Max(enumValues.IndexOf(definition["default"]?.Value<int>() ?? 0), 0);
                definition.Remove("default");
                definition.Add("default", enumNames[indexClamped]);
            }
        }

        AllowedValuesAttribute? allowedValuesAttribute = info.GetCustomAttribute<AllowedValuesAttribute>();
        if (allowedValuesAttribute != null)
        {
            IEnumerable<int> allowedValues = allowedValuesAttribute.Values.Select(Convert.ToInt32);
            JArray values = new(allowedValues);
            definition.Add("values", values);
        }
    }
    private static void SetStringSettingDefinition(MemberInfo info, JObject definition)
    {
        AllowedValuesAttribute? allowedValuesAttribute = info.GetCustomAttribute<AllowedValuesAttribute>();
        if (allowedValuesAttribute != null)
        {
            IEnumerable<string?> allowedValues = allowedValuesAttribute.Values.Select(Convert.ToString);
            JArray values = new(allowedValues);
            definition.Add("values", values);
        }
    }


    private bool ReadConfigFile(out string config)
    {
        config = "";

        try
        {
            if (Path.Exists(ConfigFilePath))
            {
                try
                {
                    using StreamReader outputFile = new(ConfigFilePath);
                    if (_configFileWatcher != null) _configFileWatcher.EnableRaisingEvents = true;
                    config = outputFile.ReadToEnd();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }
    private string ReadConfigFile(string defaultConfig, bool overrideOnFail)
    {
        try
        {
            if (Path.Exists(ConfigFilePath))
            {
                try
                {
                    using StreamReader outputFile = new(ConfigFilePath);
                    if (_configFileWatcher != null) _configFileWatcher.EnableRaisingEvents = true;
                    return outputFile.ReadToEnd();
                }
                catch
                {
                    if (overrideOnFail)
                    {
                        _api.Logger.Notification($"[ConfigKit] [config domain: {_domain}] Was not able to read settings, will create default settings file: {ConfigFilePath}");
                        using StreamWriter outputFile = new(ConfigFilePath);
                        outputFile.Write(defaultConfig);
                    }
                }
            }
            else
            {
                _api.Logger.Notification($"[ConfigKit] [config domain: {_domain}] Creating default settings file: {ConfigFilePath}");
                using StreamWriter outputFile = new(ConfigFilePath);
                outputFile.Write(defaultConfig);
            }
        }
        catch
        {
            _api.Logger.Debug($"[ConfigKit] [config domain: {_domain}] Was not able to read/write settings file: {ConfigFilePath}");
        }

        return defaultConfig;
    }
    private void WriteConfigFile(string content)
    {
        try
        {
            using StreamWriter outputFile = new(ConfigFilePath);
            outputFile.Write(content);
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"[ConfigKit] [config domain: {_domain}] Exception when trying to deserialize yaml and write it to file.\nException: {exception}\n");
        }
    }
    /// <summary>
    /// Detaches this config from the watcher it shares with every other config in the same
    /// directory, and tears the watcher down only once nobody is left using it.
    ///
    /// Disposing the shared watcher outright - and clearing the whole path registry - meant
    /// that whichever config happened to be disposed first killed file-change reloading for
    /// every other config in the process, and left a disposed watcher cached in the static
    /// dictionary for anything created afterwards. Leaving a world and rejoining was enough
    /// to trigger it, and the only symptom was that editing a config file stopped doing
    /// anything.
    /// </summary>
    private void ReleaseFileWatcher()
    {
        if (_configsByPath.TryGetValue(ConfigFilePath, out List<Config>? configs))
        {
            configs.Remove(this);
            if (configs.Count == 0) _configsByPath.Remove(ConfigFilePath);
        }

        string? directory = Path.GetDirectoryName(ConfigFilePath);
        if (directory == null)
        {
            _configFileWatcher = null;
            return;
        }

        bool stillInUse = _configsByPath.Keys.Any(
            path => string.Equals(Path.GetDirectoryName(path), directory, StringComparison.Ordinal));

        if (!stillInUse && _fileWatchers.TryGetValue(directory, out FileSystemWatcher? watcher))
        {
            watcher?.Dispose();
            _fileWatchers.Remove(directory);
        }

        _configFileWatcher = null;
    }

    /// <summary>
    /// Resolves a config file name under ModConfig, and refuses anything that escapes it.
    ///
    /// On a client, the domain and file name arrive from the SERVER over the config registry
    /// (see ConfigRegistry.FromBytes). Path.Combine happily honours "..", an absolute path or
    /// a rooted drive, so without this a hostile server could name a file whose contents it
    /// also largely controls and have the client create it anywhere the game process can
    /// write. Falling back to the domain name keeps a bad name from taking the config down.
    /// </summary>
    private static string ConfigPathFor(ICoreAPI api, string? fileName)
    {
        string root = Path.GetFullPath(Path.Combine(api.DataBasePath, "ModConfig"));

        // A client that has never written a config has no ModConfig directory, and
        // StreamWriter does not create one. On a client joined to a remote server this is
        // the first thing that touches it.
        try { Directory.CreateDirectory(root); } catch (Exception) { }

        string candidate = string.IsNullOrWhiteSpace(fileName) ? "config.yaml" : fileName!;
        string resolved;

        try
        {
            resolved = Path.GetFullPath(Path.Combine(root, candidate));
        }
        catch (Exception)
        {
            resolved = "";
        }

        // Compare against root plus a separator so "ModConfigEvil" cannot pass as "ModConfig".
        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (resolved.StartsWith(prefix, StringComparison.Ordinal)) return resolved;

        LoggerUtil.Warn(api, typeof(Config),
            $"Refusing config file name '{candidate}': it resolves outside ModConfig. Using a safe name instead.");

        return Path.Combine(root, Path.GetFileName(candidate) is { Length: > 0 } safe ? safe : "config.yaml");
    }

    private void CreateFileWatcher()
    {
        string? directory = Path.GetDirectoryName(ConfigFilePath);

        if (directory == null)
        {
            LoggerUtil.Warn(_api, this, $"[config domain: {_domain}] Unable to extract directory from: {ConfigFilePath}");
            return;
        }

        RegisterForFileChanges();

        if (!_fileWatchers.TryGetValue(directory, out _configFileWatcher))
        {
            try
            {
                _configFileWatcher = new(directory);
                _configFileWatcher.Changed += FileEventHandler;
                _configFileWatcher.Created += FileEventHandler;
                _configFileWatcher.NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite;
                _configFileWatcher.Error += (_, e) => Debug.WriteLine(e.GetException());
                _configFileWatcher.EnableRaisingEvents = true;
                _fileWatchers.Add(directory, _configFileWatcher);
            }
            catch (Exception exception)
            {
                string combined = Path.Combine(_api.DataBasePath, "ModConfig", $"{_domain}.yaml");
                LoggerUtil.Error(_api, this, $"Failed to create file watcher. Automatic updates when files are changed on disc will not work.");
                LoggerUtil.Verbose(_api, this, $"[config domain: {_domain}] Failed to create file watcher. Automatic updates when file is changed on disc will not work.\nPaths:\n  data: {_api.DataBasePath}\n  combined: {combined}\nException:\n{exception}\n");
                _fileWatchers.Add(directory, null);
                return;
            }
        }

        int initialDelay = Math.Abs(Path.GetFileName(ConfigFilePath).GetHashCode()) % _fileChangeCheckIntervalMs;

        _fileChangedListener = _api.World.RegisterGameTickListener(_ => OnFileChanged(), _fileChangeCheckIntervalMs, initialDelay);
    }

    private void RegisterForFileChanges()
    {
        if (_configsByPath.TryGetValue(ConfigFilePath, out List<Config>? configs))
        {
            if (!configs.Contains(this)) configs.Add(this);
        }
        else
        {
            _configsByPath[ConfigFilePath] = [this];
        }
    }
    private bool CheckIfFileChanged()
    {
        lock (_fileChangedLockObject)
        {
            if (!_fileChanged) return false;

            _fileChanged = false;
            return true;
        }
    }
    private void FileEventHandler(object sender, FileSystemEventArgs eventArgs)
    {
        if (eventArgs.ChangeType != WatcherChangeTypes.Changed && eventArgs.ChangeType != WatcherChangeTypes.Created)
        {
            return;
        }

        Debug.WriteLine($"File changed: {eventArgs.FullPath}");

        _api.Event.EnqueueMainThreadTask(() =>
        {
            if (_configsByPath.TryGetValue(eventArgs.FullPath, out List<Config>? configs))
            {
                Debug.WriteLine($"Config changed ({configs.Count}): {eventArgs.FullPath}");

                foreach (Config config in configs)
                {
                    lock (config._fileChangedLockObject)
                    {
                        config._fileChanged = true;
                    }
                }
            }
        }, "configlib");
    }
    private void OnFileChanged()
    {
        if (CheckIfFileChanged())
        {
            TryReadFromFile();
        }
    }


    private bool FromYaml(IEnumerable<ConfigSetting> settings, string yaml)
    {
        ValuesFromYaml(out Dictionary<string, JsonObject> values, yaml);

        if (Version != -1 && (!values.ContainsKey("version") || ConvertVersion(values["version"].Token) != Version)) return false;

        foreach (ConfigSetting setting in settings)
        {
            if (!values.ContainsKey(setting.YamlCode)) continue;

            if (setting.Validation?.Mapping == null)
            {
                JToken converted = ConvertValue(values[setting.YamlCode].Token, setting.SettingType);
                setting.Value = new(converted);
                continue;
            }

            string key = values[setting.YamlCode].AsString("");
            if (setting.Validation?.Mapping?.ContainsKey(key) == true)
            {
                setting.Value = setting.Validation.Mapping[key];
                setting.MappingKey = key;
            }
        }

        return true;
    }
    private string ToYaml(IEnumerable<ConfigSetting> settings)
    {
        return ConstructYaml(settings, _configBlocks, Version);
    }
    private int FromJsonDefinition(JsonObject json, out Dictionary<string, ConfigSetting> settings, out SortedDictionary<float, IConfigBlock> configBlocks, string domain)
    {
        settings = new();

        bool arrayFormat = json["settings"].IsArray();
        int version = json["version"]?.AsInt(0) ?? 0;

        if (arrayFormat)
        {
            SettingsAndFormattingFromJsonArray(settings, json["settings"].AsArray(), out configBlocks, domain);
        }
        else
        {
            SettingsFromJson(settings, json, ref version, domain);

            FormattingFromJson(json, out SortedDictionary<float, IConfigBlock> formatting, domain);
            configBlocks = CombineConfigBlocks(formatting, settings.Values);
        }

        if (json.KeyExists("constants"))
        {
            ParseConstants(settings, json["constants"]);
        }

        return version;
    }
    private string ToJson(IEnumerable<ConfigSetting> settings, string defaultJson, bool onlyClientSide = false)
    {
        JsonObject config = new(JObject.Parse(defaultJson));
        foreach (ConfigSetting setting in settings.Where(item => !onlyClientSide || item.ClientSide))
        {
            JsonObjectPath jsonPath = new(setting.YamlCode);
            JsonObject stored = StoredForm(setting, setting.Value);

            // SetOrCreate rather than Set: on a config file written before this setting
            // existed there is no node at its path to replace, and the old top-level fallback
            // tested the code for a backslash while paths are split on a forward slash - so a
            // nested setting was never written at all.
            int count = jsonPath.SetOrCreate(config, stored);
            if (count == 0 && !setting.YamlCode.Contains('/'))
            {
                (config.Token as JObject)?.Add(setting.YamlCode, stored.Token);
            }
        }
        return config.Token.ToString(Newtonsoft.Json.Formatting.Indented);
    }
    private bool FromJson(IEnumerable<ConfigSetting> settings, string json, bool onlyClientSide = false)
    {
        JsonObject jsonConfigObject = new(JObject.Parse(json));
        foreach (ConfigSetting setting in settings.Where(item => !onlyClientSide || item.ClientSide))
        {
            JsonObject value = ReadStoredValue(jsonConfigObject, setting) ?? setting.DefaultValue;

            if (setting.Validation?.Mapping == null)
            {
                setting.Value = value;
                continue;
            }

            string key = value.AsString("");
            if (setting.Validation?.Mapping?.ContainsKey(key) == true)
            {
                setting.Value = setting.Validation.Mapping[key];
                setting.MappingKey = key;
            }
        }
        return true;
    }

    private void ParseConstants(Dictionary<string, ConfigSetting> settings, JsonObject constants)
    {
        foreach (JToken item in constants.Token)
        {
            if (item is not JProperty property)
            {
                continue;
            }

            string code = property.Name;
            ConfigSetting setting = new(code, new JsonObject(property.Value), ConfigSettingType.Constant)
            {
                Hide = true
            };
            settings.Add(code, setting);
        }
    }
    /// <summary>
    /// The next weight not already taken, nudging upwards by the smallest representable
    /// step. Two blocks may legitimately declare the same weight, and the previous
    /// workaround added a fixed 1E-10f - which for any weight of 1 or more is lost entirely
    /// in float32, so Add threw and the catch upstream left the mod with an empty config.
    /// </summary>
    private static float NextFreeWeight(SortedDictionary<float, IConfigBlock> taken, float weight)
    {
        while (taken.ContainsKey(weight))
        {
            float next = MathF.BitIncrement(weight);
            weight = next > weight ? next : weight + 1f;
        }

        return weight;
    }

    private SortedDictionary<float, IConfigBlock> CombineConfigBlocks(SortedDictionary<float, IConfigBlock> formatting, IEnumerable<ConfigSetting> settings)
    {
        SortedDictionary<float, IConfigBlock> configBlocks = new();
        foreach ((float sortingWeight, IConfigBlock block) in formatting)
        {
            configBlocks.Add(NextFreeWeight(configBlocks, sortingWeight), block);
        }

        foreach (ConfigSetting setting in settings)
        {
            configBlocks.Add(NextFreeWeight(configBlocks, setting.SortingWeight), setting);
        }

        return configBlocks;
    }
    private void FormattingFromJson(JsonObject json, out SortedDictionary<float, IConfigBlock> formatting, string domain)
    {
        formatting = new();

        if (!json.KeyExists("formatting") || !json["formatting"].IsArray()) return;

        foreach (JsonObject block in json["formatting"].AsArray())
        {
            IFormattingBlock formattingBlock = ParseBlock(block, domain);

            formatting.Add(NextFreeWeight(formatting, formattingBlock.SortingWeight), formattingBlock);
        }
    }
    private IFormattingBlock ParseBlock(JsonObject block, string domain)
    {
        switch (block["type"]?.AsString())
        {
            case "separator":
                return new Separator(block, domain, _api);
        }

        return new Blank();
    }
    private int ConvertVersion(JToken value)
    {
        return new JsonObject(ConvertValue(value, ConfigSettingType.Integer)).AsInt(0);
    }
    private string ConstructYaml(IEnumerable<ConfigSetting> settings, SortedDictionary<float, IConfigBlock> formatting, int version)
    {
        SettingsToYaml(settings, out SortedDictionary<float, string> yaml);

        yaml.Add(-1, $"version: {version}");

        foreach ((float weight, IConfigBlock block) in formatting.Where(entry => entry.Value is IFormattingBlock))
        {
            yaml.Add(weight, (block as IFormattingBlock)?.Yaml ?? "");
        }

        return yaml.Select(entry => entry.Value).Aggregate((first, second) => $"{first}\n{second}");
    }
    private void ValuesFromYaml(out Dictionary<string, JsonObject> values, string yaml)
    {
        JObject json;

        try
        {
            IDeserializer deserializer = new DeserializerBuilder().Build();
            object? yamlObject = deserializer.Deserialize(yaml);

            ISerializer serializer = new SerializerBuilder()
                .JsonCompatible()
                .Build();

            json = JObject.Parse(serializer.Serialize(yamlObject));
        }
        catch (Exception exception)
        {
            LoggerUtil.Verbose(_api, this, $"[ValuesFromYaml] Error on parsing config file:\n{exception}\nFile content:\n{yaml}");
            throw;
        }

        values = [];
        foreach ((string code, JToken? value) in json)
        {
            if (value == null) continue;
            values.Add(code, new(value));
        }
    }
    private JToken ConvertValue(JToken? value, ConfigSettingType type)
    {
        string? strValue = (string?)(value as JValue)?.Value;
        if (strValue == null) return value ?? new JValue(strValue);

        CultureInfo culture = new("en-US");

        switch (type)
        {
            case ConfigSettingType.Boolean:
                bool boolValue = bool.Parse(strValue);
                return new JValue(boolValue);
            case ConfigSettingType.Float:
                float floatValue = float.Parse(strValue, NumberStyles.Float, culture);
                return new JValue(floatValue);
            case ConfigSettingType.Integer:
                int intValue = int.Parse(strValue, NumberStyles.Integer, culture);
                return new JValue(intValue);
            case ConfigSettingType.String:
                return new JValue(strValue);
            case ConfigSettingType.Color:
                return new JValue(strValue);
            default:
                return value ?? new JValue(strValue);
        }
    }
    private void SettingsToYaml(IEnumerable<ConfigSetting> settings, out SortedDictionary<float, string> yaml)
    {
        yaml = new();
        foreach (ConfigSetting setting in settings.Where(setting => !setting.Hide))
        {
            float weight = setting.SortingWeight < 0 ? 0 : setting.SortingWeight;

            while (yaml.ContainsKey(weight))
            {
                float next = MathF.BitIncrement(weight);
                weight = next > weight ? next : weight + 1f;
            }

            yaml.Add(weight, setting.ToYaml());
        }
    }
    private void SettingsFromJson(Dictionary<string, ConfigSetting> settings, JsonObject definition, ref int version, string domain)
    {
        version = definition["version"]?.AsInt(0) ?? 0;

        if (definition["settings"].KeyExists("boolean"))
        {
            ParseSettingsCategory(definition["settings"]["boolean"], settings, ConfigSettingType.Boolean, domain);
        }

        if (definition["settings"].KeyExists("integer"))
        {
            ParseSettingsCategory(definition["settings"]["integer"], settings, ConfigSettingType.Integer, domain);
        }

        if (definition["settings"].KeyExists("float"))
        {
            ParseSettingsCategory(definition["settings"]["float"], settings, ConfigSettingType.Float, domain);
        }

        if (definition["settings"].KeyExists("number"))
        {
            ParseSettingsCategory(definition["settings"]["number"], settings, ConfigSettingType.Float, domain);
        }

        if (definition["settings"].KeyExists("string"))
        {
            ParseSettingsCategory(definition["settings"]["string"], settings, ConfigSettingType.String, domain);
        }

        if (definition["settings"].KeyExists("other"))
        {
            ParseSettingsCategory(definition["settings"]["other"], settings, ConfigSettingType.Other, domain);
        }

        if (definition["settings"].KeyExists("color"))
        {
            ParseSettingsCategory(definition["settings"]["color"], settings, ConfigSettingType.Color, domain);
        }
    }
    private void SettingsAndFormattingFromJsonArray(Dictionary<string, ConfigSetting> settings, JsonObject[] definition, out SortedDictionary<float, IConfigBlock> configBlocks, string domain)
    {
        configBlocks = new();
        float weight = 0;
        foreach (JsonObject block in definition)
        {
            string type = block["type"].AsString();
            weight += 1;

            switch (type)
            {
                case "separator":
                    IConfigBlock formattingBlock = ParseFormattingBlock(type, block, domain);
                    configBlocks.Add(weight, formattingBlock);
                    continue;
                default:
                    break;
            }

            ConfigSettingType settingType = type switch
            {
                "boolean" => ConfigSettingType.Boolean,
                "integer" => ConfigSettingType.Integer,
                "number" => ConfigSettingType.Float,
                "float" => ConfigSettingType.Float,
                "string" => ConfigSettingType.String,
                "other" => ConfigSettingType.Other,
                "color" => ConfigSettingType.Color,
                _ => ConfigSettingType.None
            };

            (string code, ConfigSetting setting) = ParseSettingBlock(block, settingType, domain);
            configBlocks.Add(weight, setting);
            settings.Add(code, setting);
            setting.SortingWeight = weight;
        }
    }
    private IConfigBlock ParseFormattingBlock(string type, JsonObject block, string domain)
    {
        IFormattingBlock formattingBlock = ParseBlock(block, domain);
        return formattingBlock;
    }
    private (string code, ConfigSetting setting) ParseSettingBlock(JsonObject block, ConfigSettingType settingType, string domain)
    {
        if (!block.KeyExists("code"))
        {
            throw new ArgumentException($"[ConfigKit] ({domain}) Setting has no code: {block}");
        }

        string code = block["code"].AsString();
        ConfigSetting setting = ConfigSetting.FromJson(block, settingType, domain, code, _api);
        return (code, setting);
    }
    private void ParseSettingsCategory(JsonObject category, Dictionary<string, ConfigSetting> settings, ConfigSettingType settingType, string domain)
    {
        foreach (JToken item in category.Token)
        {
            if (item is not JProperty property)
            {
                continue;
            }

            string code = property.Name;
            ConfigSetting setting = ConfigSetting.FromJson(new(property.Value), settingType, domain, code, _api);
            settings.Add(code, setting);
        }
    }


    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                ReleaseFileWatcher();

                if (_fileChangedListener != 0)
                {
                    _api.World.UnregisterGameTickListener(_fileChangedListener);
                    _fileChangedListener = 0;
                }
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
