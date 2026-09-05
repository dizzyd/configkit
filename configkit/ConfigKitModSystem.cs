// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.
//
// Derived from ConfigLib by Maltiez (https://github.com/maltiez2/vsmod_configlib),
// released under CC0 1.0 Universal. Adapted to drop the Dear ImGui dependency.

using ConfigKit.Gui;
using ConfigKit.Patches;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace ConfigKit;

public sealed class ConfigKitModSystem : ModSystem, IConfigProvider
{
    public IEnumerable<string> Domains => _domains;
    public IConfig? GetConfig(string domain) => GetConfigImpl(domain);
    public ISetting? GetSetting(string domain, string code) => GetConfigImpl(domain)?.GetSetting(code);
    public void RegisterManagedConfig(string domain, object configObject, string? path = null, Action? onSyncedFromServer = null, Action<string>? onSettingChanged = null, Action? onConfigSaved = null)
    {
        if (_api == null) return;

        // Accepting a registration while dormant would leave the caller believing its
        // settings are managed when nothing will ever sync or display them.
        if (_standingDown)
        {
            LoggerUtil.Notify(_api, this, $"Not registering '{domain}': ConfigKit has stood down in favour of another config mod.");
            return;
        }

        if (!_canRegisterNewConfig)
        {
            LoggerUtil.Error(_api, this, $"Cant register custom managed config '{domain}': too late, configs have been already sent to clients");
            return;
        }

        if (Unmanaged(domain)) return;

        // A domain already taken. Dictionary.Add throws, and this method is called from a
        // mod's StartPre - so the throw escapes into the mod loader and that mod fails to
        // start, over a settings screen. Worse for a library registering several configs in
        // a loop: one duplicate and every config after it is never registered at all, which
        // is how InsanityLib's mods came down when the same static registry ran on both
        // sides of a singleplayer session. Refuse the way every other refusal here does.
        if (_configs.ContainsKey(domain))
        {
            LoggerUtil.Notify(_api, this, $"Not registering '{domain}': a config is already registered under that domain.");
            return;
        }

        // What the dropdown calls this config. A domain that is a mod id resolves to that
        // mod's name; anything else falls through to Lang, which returns the key unchanged
        // when nobody translated it. That last case is what SetConfigDisplayName is for.
        string name = _api.ModLoader.GetMod(domain)?.Info.Name ?? Lang.Get(domain);

        Config config = new(_api, domain, name, configObject, path ?? domain + ".json");

        // Say what was registered, and say what was not. The old reflection walk dropped
        // every member it could not classify with no row, no key and no log line, so a mod
        // with a dictionary in its config looked like it had simply lost it.
        if (config.Schema is { } schema)
        {
            LoggerUtil.Notify(_api, this, $"Registered '{domain}': {schema.Summary()}.");

            foreach (string notice in schema.Notices)
            {
                LoggerUtil.Notify(_api, this, $"({domain}) {notice}");
            }
        }

        _configs.Add(domain, config);
        _domains.Add(domain);
        _configsToRegister.Add(domain, config);

        if (_api.Side == EnumAppSide.Server)
        {
            config.ConfigSaved += OnConfigSaved;
            foreach ((string code, ConfigSetting setting) in config.Settings)
            {
                setting.SettingChanged += (value) => OnSettingChanged(domain, code, value);
                OnSettingLoaded(domain, code, setting);
            }
        }

        config.SettingChanged += setting => SettingChanged?.Invoke(domain, config, setting);
        // The config assigns onto the object itself now, before it raises this - so by the
        // time the mod's callback runs, its own settings object already holds the new value.
        config.SettingChanged += setting => onSettingChanged?.Invoke(setting.YamlCode);

        // The constructor has already read the file, and SettingChanged is only subscribed
        // above - after the fact - so nothing has pushed those values onto the caller's
        // object yet. Without this a mod runs on its compiled-in defaults while the file and
        // the settings screen both show what the player edited.
        config.AssignSettingsValues(configObject);

        config.ConfigSaved += config =>
        {
            onConfigSaved?.Invoke();
        };

        if (_api.Side == EnumAppSide.Client)
        {
            ConfigsLoaded += () =>
            {
                onSyncedFromServer?.Invoke();
            };
        }

        _customManagedConfigs.Add(domain);
    }

    /// <summary>
    /// Names a registered config for the settings dropdown, overriding the mod name a domain
    /// would otherwise resolve to. Safe to call at any point after registration; the dropdown
    /// reads it when the window is composed.
    ///
    /// Deliberately not a parameter on <see cref="RegisterManagedConfig"/>. Adding one - even
    /// with a default - changes that method's signature, and a caller compiled against the
    /// previous release binds to the signature it saw at compile time. The result is a
    /// MissingMethodException at registration rather than a compile error, which for a mod
    /// registering several configs takes out every config after the first.
    /// </summary>
    public void SetConfigDisplayName(string domain, string displayName)
    {
        if (_configs.TryGetValue(domain, out Config? config)) config.SetModName(displayName);
    }

    /// <summary>
    /// configlib's name for <see cref="RegisterManagedConfig"/>, kept so a mod that reaches
    /// the library by reflection finds it here too.
    ///
    /// Several mods do exactly that - look the method up by name, check its parameter list,
    /// and log "ConfigLib found but RegisterCustomManagedConfig not available" when it is
    /// missing - so without this alias their settings screen silently disappears under
    /// ConfigKit even though nothing else about them needs changing. The parameter list is
    /// configlib's, exactly: some of those callers match on the full six-type signature and
    /// would reject anything else.
    /// </summary>
    public void RegisterCustomManagedConfig(string domain, object configObject, string? path = null, Action? onSyncedFromServer = null, Action<string>? onSettingChanged = null, Action? onConfigSaved = null)
        => RegisterManagedConfig(domain, configObject, path, onSyncedFromServer, onSettingChanged, onConfigSaved);

    public event Action? ConfigWindowClosed;
    public event Action? ConfigWindowOpened;
    public event Action<string, IConfig, ISetting>? SettingChanged;
    [Obsolete]
    public static event Action<ICoreAPI>? ConfigsChanged;

    public const string ConfigSavedEvent = "configkit:{0}:config-saved";
    public const string ConfigChangedEvent = "configkit:{0}:setting-changed";
    public const string ConfigLoadedEvent = "configkit:{0}:setting-loaded";
    public const string ConfigReloadEvent = "configkit:config-reload";

    /// <summary>
    /// On Server: right after configs are applied<br/>
    /// On Client: right after configs are received from server and applied (between AssetsLoaded and AssetsFinalize stages)
    /// </summary>
    public event Action? ConfigsLoaded;

    internal HashSet<string> GetDomains() => _domains;
    internal Config? GetConfigImpl(string domain) => _configs?.ContainsKey(domain) == true ? _configs[domain] : null;

    private readonly Dictionary<string, Config> _configs = new();
    private readonly HashSet<string> _domains = new();

    /// <summary>Mod ids configkit.json says to leave to someone else. See ConfigKitOwnConfig.</summary>
    private HashSet<string> _unmanaged = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a domain has been handed to another config manager. Checked at both points a
    /// config can be claimed - a mod registering one, and an asset declaring one - because a
    /// clash is about the file, and both routes end at the same file.
    /// </summary>
    private bool Unmanaged(string domain)
    {
        if (!_unmanaged.Contains(domain)) return false;

        LoggerUtil.Notify(_api, this, $"Not managing '{domain}': {OwnConfig.FileName} lists it as unmanaged.");
        return true;
    }
    private readonly Dictionary<string, Config> _configsToRegister = [];
    private readonly HashSet<string> _customManagedConfigs = [];

    private ICoreAPI? _api;
    private const string _registryCode = "configkit:configs";
    private ConfigRegistry? _registry;
    private IClientNetworkChannel? _eventsChannel;
    private IServerNetworkChannel? _eventsServerChannel;
    private const string _channelName = "configkit:events";
    private bool _canRegisterNewConfig = true;
    private bool _standingDown;
    private ConfigGuiManager? _guiManager;

    public override void StartPre(ICoreAPI api)
    {
        _api = api;
        _standingDown = StandDown(api);

        // Read even while standing down: it costs nothing, and it means the file exists to be
        // found by someone whose first encounter with ConfigKit is a conflict.
        _unmanaged = OwnConfig.UnmanagedDomains(api);
    }

    /// <summary>
    /// ConfigKit, configlib and autoconfiglib all want to own the same config files and
    /// the same <c>configlib-patches.json</c> assets. Two owners for one file is worse than
    /// none, so defer to whichever was already installed rather than compete with it.
    /// </summary>
    private static bool StandDown(ICoreAPI api)
    {
        string[] incumbents = new[] { "configlib", "autoconfiglib" }
            .Where(api.ModLoader.IsModEnabled)
            .ToArray();

        if (incumbents.Length == 0) return false;

        api.Logger.Notification(
            "[ConfigKit] standing down: {0} is installed and already manages mod configs. "
          + "Remove it to let ConfigKit take over.", string.Join(" and ", incumbents));
        return true;
    }

    public override void Start(ICoreAPI api)
    {
        if (_standingDown) return;

        _registry = api.RegisterRecipeRegistry<ConfigRegistry>(_registryCode);
        api.Event.RegisterEventBusListener(ReloadJsonConfigs, filterByEventName: ConfigReloadEvent);

        if (api.Side == EnumAppSide.Client)
        {
            PauseMenuPatch.Patch();
            ConfigRegistry.ConfigsLoaded += ReloadConfigs;
            _eventsChannel = (api as ICoreClientAPI)?.Network.RegisterChannel(_channelName)
                .RegisterMessageType<ConfigEventPacket>()
                .RegisterMessageType<ServerSideSettingChanged>()
                .SetMessageHandler<ServerSideSettingChanged>(OnServerSettingChanged);

        }
        else
        {
            _eventsServerChannel = (api as ICoreServerAPI)?.Network.RegisterChannel(_channelName)
                .RegisterMessageType<ConfigEventPacket>()
                .SetMessageHandler<ConfigEventPacket>(SendEvent)
                .RegisterMessageType<ServerSideSettingChanged>()
                .SetMessageHandler<ServerSideSettingChanged>(OnServerSettingChanged);
        }
    }
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (_standingDown) return;

        LoadConfigs();
        _api?.Logger.Notification($"[ConfigKit] Configs loaded: {_configs.Count}");
        foreach ((_, Config config) in _configs)
        {
            config.Apply();
        }

        // A client whose server does not run ConfigKit never receives a config registry, so
        // ReloadConfigs never fires and the window used to be built nowhere at all - the
        // hotkey and the pause-menu button silently did nothing. Its own local configs are
        // loaded and editable, so build the window here; a later sync rebuilds it.
        if (api is ICoreClientAPI clientApi)
        {
            try
            {
                BuildSettingsWindow(clientApi);
            }
            catch (Exception exception)
            {
                LoggerUtil.Error(_api, this, $"Error creating the settings window: {exception}");
            }
        }

        ConfigsLoaded?.Invoke();
        ConfigsChanged?.Invoke(api);
    }

    private void BuildSettingsWindow(ICoreClientAPI clientApi)
    {
        _guiManager?.Dispose();
        _guiManager = new ConfigGuiManager(clientApi, _configs);
        _guiManager.ConfigWindowOpened += () => ConfigWindowOpened?.Invoke();
        _guiManager.ConfigWindowClosed += () => ConfigWindowClosed?.Invoke();
    }
    public override double ExecuteOrder() => 0.01;
    public override void Dispose()
    {
        if (_api?.Side == EnumAppSide.Client)
        {
            PauseMenuPatch.Unpatch();
        }

        foreach ((_, Config config) in _configs)
        {
            config.Dispose();
        }

        _guiManager?.Dispose();
        _guiManager = null;

        _configs.Clear();
        AssetPatch.ForgetPristineAssets();
        _domains.Clear();
        {
        }
        if (_api?.Side == EnumAppSide.Client)
        {
            ConfigRegistry.ConfigsLoaded -= ReloadConfigs;
        }
        _registry = null;

        base.Dispose();
    }


    /// <summary>
    /// True once this client has taken its configs from the server's registry rather than
    /// from its own copy of the assets. Always false on a server. Until it flips, values
    /// read on a client are local defaults and may not be what the server is enforcing.
    /// </summary>
    public bool ConfigsReceivedFromServer { get; private set; }

    private void ReloadConfigs(Dictionary<string, Config> configs)
    {
        ConfigsReceivedFromServer = true;

        _api?.Logger.Notification($"[ConfigKit] Configs received from server: {configs.Count}");

        foreach ((string domain, Config config) in configs)
        {
            if (_customManagedConfigs.Contains(domain))
            {
                _configs[domain].SyncFromServer(config, (_api as ICoreClientAPI)?.IsSinglePlayer == true);
                _configs[domain].ConfigSaved += OnConfigSaved;
                foreach ((string code, ConfigSetting setting) in _configs[domain].Settings)
                {
                    setting.SettingChanged += (value) => OnSettingChanged(domain, code, value);
                    OnSettingLoaded(domain, code, setting);
                }
                continue;
            }

            _domains.Add(domain);

            // The config parsed from local assets is being replaced by the server's. Dispose
            // it, or it stays registered in the static file-watcher tables holding a handler
            // bound to this session, and the watcher is never released.
            if (_configs.TryGetValue(domain, out Config? replaced) && !ReferenceEquals(replaced, config))
            {
                replaced.Dispose();
            }

            _configs[domain] = config;
            config.Apply();

            config.ConfigSaved += OnConfigSaved;
            foreach ((string code, ConfigSetting setting) in config.Settings)
            {
                setting.SettingChanged += (value) => OnSettingChanged(domain, code, value);
                OnSettingLoaded(domain, code, setting);
            }
        }


        if (_api is ICoreClientAPI clientApi)
        {
            try
            {
                BuildSettingsWindow(clientApi);
            }
            catch (Exception exception)
            {
                LoggerUtil.Error(_api, this, $"Error creating the settings window: {exception}");
            }
        }

        ConfigsLoaded?.Invoke();
    }
    /// <summary>
    /// Whether ConfigKit is, or is going to be, in charge of a mod's config.
    ///
    /// For another config manager deciding whether to leave a mod alone. Neither of the two
    /// obvious ways to ask that from outside actually works:
    ///
    /// <list type="bullet">
    /// <item><description>
    /// Reading <see cref="Domains"/> is a lifecycle race. C# registrations land in StartPre
    /// and are there early, but a mod that only ships a configlib-patches.json is not
    /// registered until this system's AssetsLoaded at ExecuteOrder 0.01 - and a manager
    /// looking earlier than that sees nothing, for precisely the content mods most likely to
    /// be described to both.
    /// </description></item>
    /// <item><description>
    /// Scanning for configlib-patches.json assets answers a different question. A descriptor
    /// exists whether or not ConfigKit will act on it: it stands down entirely when configlib
    /// or autoconfiglib is installed, and a player can hand any single mod to someone else
    /// through configkit.json. Ceding to a manager that is not managing leaves the mod with
    /// no settings screen at all, and nothing saying why.
    /// </description></item>
    /// </list>
    ///
    /// This answers the real question at any point after assets exist, so a caller does not
    /// have to reason about load order at all.
    /// </summary>
    /// <param name="domain">The mod id to ask about.</param>
    public bool WillManage(string domain)
    {
        if (_api == null || string.IsNullOrWhiteSpace(domain)) return false;

        // Nothing is managed while dormant, and nothing listed is managed ever.
        if (_standingDown || _unmanaged.Contains(domain)) return false;

        // Already claimed - a registration, or an asset already read.
        if (_domains.Contains(domain)) return true;

        // Not yet claimed, but declared: this is what LoadConfigs will pick up.
        try
        {
            return _api.Assets.GetMany(AssetCategory.config.Code)
                .Any(asset => asset.Name == "configlib-patches.json"
                           && string.Equals(asset.Location.Domain, domain, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            // Assets are not readable before AssetsLoaded. Being asked that early can only
            // mean the answer is "whatever has registered so far", which is the check above.
            return false;
        }
    }

    private void LoadConfigs()
    {
        if (_api == null) return;

        ConfigRegistry? registry = _registry ?? GetRegistry(_api);

        foreach (IAsset asset in _api.Assets.GetMany(AssetCategory.config.Code).Where((asset) => asset.Name == "configlib-patches.json"))
        {
            try
            {
                LoadConfig(asset, registry);
            }
            catch (Exception exception)
            {
                _api.Logger.Error($"[ConfigKit] Error on loading config for {asset.Location.Domain}.");
                _api.Logger.VerboseDebug($"[ConfigKit] Error on loading config for {asset.Location.Domain}.\n{exception}\n");
            }
        }

        if (_api.Side == EnumAppSide.Server)
        {
            foreach ((string domain, Config config) in _configsToRegister)
            {
                registry?.Register(domain, config);
            }
            _configsToRegister.Clear();
        }

        ConfigRegistry.OnToBytes += () => _canRegisterNewConfig = false;
    }
    private void LoadConfig(IAsset asset, ConfigRegistry? registry)
    {
        if (_api == null) return;

        string domain = asset.Location.Domain;

        if (Unmanaged(domain)) return;

        byte[] data = asset.Data;
        data = System.Text.Encoding.Convert(System.Text.Encoding.UTF8, System.Text.Encoding.Unicode, data);
        string json = System.Text.Encoding.Unicode.GetString(data);
        int startIndex = 0;
        if (json.Contains('{'))
        {
            startIndex = json.IndexOf('{');
        }
        json = json.Substring(startIndex, json.Length - startIndex);
        JObject token = JObject.Parse(json);
        JsonObject parsedConfig = new(token);

        Config config;
        if (parsedConfig.KeyExists("file"))
        {
            config = new(_api, domain, _api.ModLoader.GetMod(domain)?.Info.Name ?? Lang.Get(domain), parsedConfig, parsedConfig["file"].AsString());
        }
        else
        {
            config = new(_api, domain, _api.ModLoader.GetMod(domain)?.Info.Name ?? Lang.Get(domain), parsedConfig);
        }

        _configs.Add(domain, config);
        _domains.Add(domain);

        registry?.Register(domain, config);

        if (_api.Side == EnumAppSide.Server)
        {
            config.ConfigSaved += OnConfigSaved;
            foreach ((string code, ConfigSetting setting) in config.Settings)
            {
                setting.SettingChanged += (value) => OnSettingChanged(domain, code, value);
                OnSettingLoaded(domain, code, setting);
            }
        }

        config.SettingChanged += setting => SettingChanged?.Invoke(domain, config, setting);
    }
    private static ConfigRegistry? GetRegistry(ICoreAPI api)
    {
        return (api.World as GameMain)?.GetRecipeRegistry(_registryCode) as ConfigRegistry;
    }

    private void OnConfigSaved(Config config)
    {
        TreeAttribute eventDataTree = new();
        eventDataTree.SetString("domain", config.Domain);
        string eventName = string.Format(ConfigSavedEvent, config.Domain);
        _api?.Event.PushEvent(eventName, eventDataTree);
        SendEventToServer(eventName, eventDataTree);

        if (_api is ICoreClientAPI clientApi && clientApi.World.Player.HasPrivilege(Privilege.controlserver))
        {
            ServerSideSettingChanged packet = new(config.Domain, config.Settings.Where(setting => setting.Value.ChangedSinceLastSave && !setting.Value.ClientSide).ToDictionary(), clientApi.IsSinglePlayer);

            if (packet.Settings.Any())
            {
                _eventsChannel?.SendPacket(packet);
            }
        }

        foreach ((_, ConfigSetting? setting) in config.Settings)
        {
            setting.ChangedSinceLastSave = false;
        }
    }
    private void OnSettingChanged(string domain, string code, ConfigSetting setting)
    {
        TreeAttribute eventDataTree = new();
        eventDataTree.SetString("domain", domain);
        eventDataTree.SetString("setting", code);

        switch (setting.SettingType)
        {
            case ConfigSettingType.Boolean:
                eventDataTree.SetBool("value", setting.Value.AsBool());
                break;
            case ConfigSettingType.Float:
                eventDataTree.SetFloat("value", setting.Value.AsFloat());
                break;
            case ConfigSettingType.Integer:
                eventDataTree.SetInt("value", setting.Value.AsInt());
                break;
            case ConfigSettingType.String:
                eventDataTree.SetString("value", setting.Value.AsString());
                break;
            case ConfigSettingType.Other:
                // Never a raw ToAttribute: it yields null for a JSON null, and a null entry
                // in a tree is a NullReferenceException the moment anything writes it.
                if (Attributes.For(setting.Value) is { } attribute) eventDataTree["value"] = attribute;
                break;
        }
        string eventName = string.Format(ConfigChangedEvent, domain);
        _api?.Event.PushEvent(eventName, eventDataTree);
        SendEventToServer(eventName, eventDataTree);
    }
    private void OnSettingLoaded(string domain, string code, ConfigSetting setting)
    {
        TreeAttribute eventDataTree = new();
        eventDataTree.SetString("domain", domain);
        eventDataTree.SetString("setting", code);

        switch (setting.SettingType)
        {
            case ConfigSettingType.Boolean:
                eventDataTree.SetBool("value", setting.Value.AsBool());
                break;
            case ConfigSettingType.Float:
                eventDataTree.SetFloat("value", setting.Value.AsFloat());
                break;
            case ConfigSettingType.Integer:
                eventDataTree.SetInt("value", setting.Value.AsInt());
                break;
            case ConfigSettingType.String:
                eventDataTree.SetString("value", setting.Value.AsString());
                break;
            case ConfigSettingType.Other:
                // Never a raw ToAttribute: it yields null for a JSON null, and a null entry
                // in a tree is a NullReferenceException the moment anything writes it.
                if (Attributes.For(setting.Value) is { } attribute) eventDataTree["value"] = attribute;
                break;
        }
        string eventName = string.Format(ConfigLoadedEvent, domain);
        _api?.Event.PushEvent(eventName, eventDataTree);
    }
    private void ReloadJsonConfigs(string eventName, ref EnumHandling handling, IAttribute data)
    {
        string domain = (data as ITreeAttribute)?.GetAsString("domain") ?? "";

        // Anything can push an event onto the bus; an unknown domain must not throw out of it.
        if (!_configs.TryGetValue(domain, out Config? config))
        {
            LoggerUtil.Warn(_api, this, $"Reload requested for unknown config domain '{domain}'.");
            return;
        }

        config.ReadFromFile();
    }
    private void OnServerSettingChanged(IServerPlayer player, ServerSideSettingChanged packet)
    {
        if (!packet.Settings.Any()) return;

        if (!player.HasPrivilege(Privilege.controlserver))
        {
            _api?.Logger.Warning($"[ConfigKit] Player '{player.PlayerName}' without privilege '{Privilege.controlserver}' tried to change config for mod  '{packet.ConfigDomain}'.");
            _api?.Logger.Audit($"[ConfigKit] missing privilege to change config: '{player.PlayerName}' - '{packet.ConfigDomain}'.");
            return;
        }

        Config? config = GetConfigImpl(packet.ConfigDomain);

        if (config == null)
        {
            _api?.Logger.Error($"[ConfigKit] Player '{player.PlayerName}' tried to change config '{packet.ConfigDomain}', but such config does not exist.");
            return;
        }

        string settingsChanged = "";

        foreach ((string settingCode, ConfigSettingPacket settingPacket) in packet.Settings)
        {
            ConfigSetting settingFromClient = new(settingPacket);

            ConfigSetting? serverSetting = (ConfigSetting?)config.GetSetting(settingCode);

            if (serverSetting == null)
            {
                _api?.Logger.Error($"[ConfigKit] Player '{player.PlayerName}' tried to change setting '{settingCode}' in config for mod '{packet.ConfigDomain}', but such setting does not exist in this config.");
                continue;
            }

            if (serverSetting.ClientSide) continue;

            serverSetting.Value = settingFromClient.Value;
            serverSetting.MappingKey = settingFromClient.MappingKey;

            if (settingsChanged != "") settingsChanged += ", ";
            settingsChanged += serverSetting.YamlCode;
        }

        _api?.Logger.Audit($"[ConfigKit] config changed: '{player.PlayerName}' - {packet.ConfigDomain} - {settingsChanged}");
        _api?.Logger.Notification($"[ConfigKit] Player '{player.PlayerName}' changed settings: {settingsChanged}, and saved config file for mod '{_api.ModLoader.GetMod(packet.ConfigDomain)?.Info.Name} ({packet.ConfigDomain})'.");

        if (!packet.IsSinglePlayer) config.WriteToFile();

        _eventsServerChannel?.BroadcastPacket(packet, player);
    }
    private void OnServerSettingChanged(ServerSideSettingChanged packet)
    {
        if (!packet.Settings.Any()) return;

        Config? config = GetConfigImpl(packet.ConfigDomain);

        if (config == null)
        {
            return;
        }

        string settingsChanged = "";

        foreach ((string settingCode, ConfigSettingPacket settingPacket) in packet.Settings)
        {
            ConfigSetting settingFromClient = new(settingPacket);

            ConfigSetting? serverSetting = (ConfigSetting?)config.GetSetting(settingCode);

            if (serverSetting == null)
            {
                continue;
            }

            if (serverSetting.ClientSide) continue;

            serverSetting.Value = settingFromClient.Value;
            serverSetting.MappingKey = settingFromClient.MappingKey;

            if (settingsChanged != "") settingsChanged += ", ";
            settingsChanged += serverSetting.YamlCode;
        }
    }

    private void SendEventToServer(string eventName, TreeAttribute eventData)
    {
        if(_api is not ICoreClientAPI clientApi || !clientApi.World.Player.HasPrivilege(Privilege.controlserver)) return;

        ConfigEventPacket eventPacket = new()
        {
            EventName = eventName,
            Data = eventData.ToBytes()
        };
        _eventsChannel?.SendPacket(eventPacket);
    }
    private void SendEvent(IServerPlayer fromPlayer, ConfigEventPacket eventPacket)
    {
        if (eventPacket.EventName is null || !eventPacket.EventName.StartsWith("configkit:"))
        {
            _api?.Logger.Warning($"[ConfigKit] Player '{fromPlayer.PlayerName}' tried to push event outside of ConfigKit scope: '{eventPacket.EventName}'.");
            _api?.Logger.Audit($"[ConfigKit] received event push outside of ConfigKit scope: '{fromPlayer.PlayerName}' - '{eventPacket.EventName}'.");
            return;
        }

        if (!fromPlayer.HasPrivilege(Privilege.controlserver))
        {
            _api?.Logger.Warning($"[ConfigKit] Player '{fromPlayer.PlayerName}' without privilege '{Privilege.controlserver}' tried to push event '{eventPacket.EventName}'.");
            _api?.Logger.Audit($"[ConfigKit] missing privilege to push event: '{fromPlayer.PlayerName}' - '{eventPacket.EventName}'.");
            return;
        }

        TreeAttribute eventDataTree = new();
        eventDataTree.FromBytes(eventPacket.Data);
        eventDataTree.SetString("player", fromPlayer.PlayerUID);
        _api?.Event.PushEvent(eventPacket.EventName, eventDataTree);
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
internal class ConfigEventPacket
{
    public string EventName { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
internal class SettingsPacket
{
    public string Domain { get; set; } = "";
    public Dictionary<string, ConfigSettingPacket> Settings { get; set; } = new();
    public byte[] Definition { get; set; } = System.Array.Empty<byte>();

    public SettingsPacket() { }

    public SettingsPacket(string domain, Dictionary<string, ConfigSetting> settings, JsonObject definition)
    {
        Dictionary<string, ConfigSettingPacket> serialized = [];
        foreach ((string key, ConfigSetting? value) in settings)
        {
            serialized.Add(key, new(value));
        }

        Definition = System.Text.Encoding.UTF8.GetBytes(definition.ToString());
        Settings = serialized;
        Domain = domain;
    }

    public Dictionary<string, ConfigSetting> GetSettings()
    {
        Dictionary<string, ConfigSetting> deserialized = [];
        foreach ((string key, ConfigSettingPacket? value) in Settings)
        {
            deserialized.Add(key, new(value));
        }
        return deserialized;
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
internal class ServerSideSettingChanged
{
    public Dictionary<string, ConfigSettingPacket> Settings { get; set; } = [];
    public string ConfigDomain { get; set; } = "";
    public bool IsSinglePlayer { get; set; } = false;

    public ServerSideSettingChanged() { }

    public ServerSideSettingChanged(string domain, Dictionary<string, ConfigSetting> settings, bool isSinglePlayer)
    {
        Dictionary<string, ConfigSettingPacket> serialized = [];
        foreach ((string key, ConfigSetting? value) in settings)
        {
            serialized.Add(key, new(value));
        }

        Settings = serialized;
        ConfigDomain = domain;
        IsSinglePlayer = isSinglePlayer;
    }
}