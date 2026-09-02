// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.

using Vintagestory.API.Client;

namespace ConfigKit.Gui;

/// <summary>
/// Owns the settings window on the client, and is the only thing that knows a GUI exists.
/// The config core registers nothing here itself - it hands over its configs and this
/// wires up the hotkey and the pause-menu button through <see cref="ConfigGui"/>.
/// </summary>
internal sealed class ConfigGuiManager : IDisposable
{
    private const string HotkeyCode = "configkitconfigs";

    private readonly ICoreClientAPI _api;
    private ConfigDialog? _dialog;

    public event Action? ConfigWindowOpened;
    public event Action? ConfigWindowClosed;

    public ConfigGuiManager(ICoreClientAPI api, Dictionary<string, Config> configs)
    {
        _api = api;

        _api.Input.RegisterHotKey(HotkeyCode, "(ConfigKit) Open mod settings", GlKeys.P, HotkeyType.GUIOrOtherControls);
        _api.Input.SetHotKeyHandler(HotkeyCode, _ => Toggle());

        _dialog = new ConfigDialog(api, configs);
        _dialog.OnOpened += () => ConfigWindowOpened?.Invoke();
        _dialog.OnClosed += () => ConfigWindowClosed?.Invoke();

        ConfigGui.Toggle = Toggle;
    }

    private bool Toggle()
    {
        if (_dialog == null) return false;

        if (_dialog.IsOpened()) _dialog.TryClose();
        else _dialog.TryOpen();

        return true;
    }

    public void Dispose()
    {
        if (ConfigGui.Toggle == Toggle) ConfigGui.Toggle = null;

        _dialog?.Dispose();
        _dialog = null;
    }
}
