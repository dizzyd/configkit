// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

namespace ConfigKit.Gui;

/// <summary>
/// The seam between the config core and whatever renders it.
///
/// The core - loading, patching, syncing, writing files - is side-agnostic and has no
/// opinion about how settings are displayed. A client-side GUI registers itself here;
/// on a server, or before the dialog exists, every entry point is a no-op rather than
/// a null reference.
/// </summary>
public static class ConfigGui
{
    /// <summary>Set by the client GUI layer. Returns true if the window handled the toggle.</summary>
    public static Func<bool>? Toggle { get; set; }

    public static bool IsAvailable => Toggle != null;

    /// <summary>Opens or closes the config window. Safe to call when no GUI is registered.</summary>
    public static bool Show() => Toggle?.Invoke() ?? false;
}
