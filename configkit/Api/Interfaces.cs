// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.
//
// Derived from ConfigLib by Maltiez (https://github.com/maltiez2/vsmod_configlib),
// released under CC0 1.0 Universal. Adapted to drop the Dear ImGui dependency.

using ConfigKit.Formatting;
using Vintagestory.API.Datastructures;

namespace ConfigKit;

/// <summary>
/// Provides methods for accessing configs and settings
/// </summary>
public interface IConfigProvider
{
    /// <summary>
    /// Fired when config window is closed
    /// </summary>
    event Action? ConfigWindowClosed;
    /// <summary>
    /// Fired when config window is opened
    /// </summary>
    event Action? ConfigWindowOpened;
    /// <summary>
    /// Fired right after all configs are loaded
    /// </summary>
    event Action? ConfigsLoaded;

    /// <summary>
    /// Stores domains of all mods that with configs
    /// </summary>
    IEnumerable<string> Domains { get; }
    /// <summary>
    /// Provides access to configs by domains. Each domain can have only one config registered. Domain also is used to get mod name in GUI, because of this you should use domain that matches modid of some mod.
    /// </summary>
    /// <param name="domain">Mod id of a mod which config you want to access</param>
    /// <returns>Config or null if not found</returns>
    IConfig? GetConfig(string domain);
    /// <summary>
    /// Same as <see cref="GetConfig"/> but retrieves specific setting by its code
    /// </summary>
    /// <param name="domain">Mod id of a mod which setting you want to access</param>
    /// <param name="code">Setting code, specified either in 'code' field of the setting or by key in 'settings'</param>
    /// <returns>Setting or null if not found</returns>
    ISetting? GetSetting(string domain, string code);
    /// <summary>
    /// Registers a plain settings object for a mod. Its public fields and properties become
    /// settings, annotated with the standard <see cref="System.ComponentModel"/> and
    /// <see cref="System.ComponentModel.DataAnnotations"/> attributes - so the annotated class
    /// keeps no reference to ConfigKit and works with ConfigKit absent.
    /// </summary>
    /// <param name="domain">Mod id of your mod</param>
    /// <param name="configObject">The settings object; its values are assigned in place</param>
    void RegisterManagedConfig(string domain, object configObject, string path = null,
        Action onSyncedFromServer = null, Action<string> onSettingChanged = null, Action onConfigSaved = null);
    /// <summary>
    /// Fired when a setting in a config changed. Action arguments: domain, config, setting.
    /// </summary>
    event Action<string, IConfig, ISetting>? SettingChanged;
}

public interface IConfig
{
    /// <summary>
    /// Full path to loaded config file.<br/>In multiplayer on client is equal to path of client side config.
    /// </summary>
    string ConfigFilePath { get; }
    /// <summary>
    /// Version of loaded config file.<br/>In multiplayer on client is equal to version of client side config.
    /// </summary>
    public int Version { get; }
    /// <summary>
    /// Writes current config into file.<br/>In multiplayer on client updates only client side settings and write to file on client side.
    /// </summary>
    void WriteToFile();
    /// <summary>
    /// Reads config from file.<br/>In multiplayer on client reads values only of client side settings.
    /// </summary>
    /// <returns></returns>
    bool ReadFromFile();
    /// <summary>
    /// Sets all settings to default values<br/>In multiplayer on client sets values only of client side settings.
    /// </summary>
    void RestoreToDefaults();
    /// <summary>
    /// Returns settings by given code.<br/>Setting code, specified either in 'code' field of the setting or by key in 'settings'.
    /// </summary>
    /// <param name="code">Setting code, specified either in 'code' field of the setting or by key in 'settings'.</param>
    /// <returns></returns>
    ISetting? GetSetting(string code);
    /// <summary>
    /// Get called when a setting value changes.
    /// </summary>
    public event Action<ISetting>? SettingChanged;
    /// <summary>
    /// Will try to assign values from config to object fields or properties with matching YAML codes.
    /// </summary>
    /// <param name="target"></param>
    void AssignSettingsValues(object target);
}

public enum ConfigSettingType
{
    /// <summary>
    /// If type is undefined due to some error
    /// </summary>
    None,
    /// <summary>
    /// Corresponds to <see cref="bool"/> values. Edited by checkbox in GUI.
    /// </summary>
    Boolean,
    /// <summary>
    /// Corresponds to <see cref="float"/> values. Edited by slider or entering values directly in GUI. Can have minimum and maximum values specified.
    /// </summary>
    Float,
    /// <summary>
    /// Corresponds to <see cref="int"/> values. Edited by slider or entering values directly in GUI. Can have minimum and maximum values specified.
    /// </summary>
    Integer,
    /// <summary>
    /// Corresponds to <see cref="string"/> values. Edited by line edit in GUI.
    /// </summary>
    String,
    /// <summary>
    /// Color as string in hex format with '#' prefix
    /// </summary>
    Color,
    /// <summary>
    /// Corresponds to arbitrary <see cref="JsonObject"/> values.
    /// </summary>
    Other,
    /// <summary>
    /// Hidden setting that just stores constant value for use in patches
    /// </summary>
    Constant
    
}

public interface ISetting : IConfigBlock
{
    JsonObject Value { get; set; }
    /// <summary>
    /// Sets <see cref="Value"/> to corresponding value from mapping on being set.
    /// </summary>
    string? MappingKey { get; set; }
    JsonObject DefaultValue { get; }
    ConfigSettingType SettingType { get; }
    string YamlCode { get; }
    Validation? Validation { get; }
    bool AssignSettingValue(object target);
}
