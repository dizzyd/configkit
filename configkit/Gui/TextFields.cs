// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.

using System.Reflection;
using Vintagestory.API.Client;

namespace ConfigKit.Gui;

internal static class TextFields
{
    // void in 1.22, bool in 1.23: the same source, two methods to a compiled caller.
    private static readonly MethodInfo _setValue =
        typeof(GuiElementEditableTextBase).GetMethod("SetValue", [typeof(string), typeof(bool)])!;

    public static void SetText(this GuiElementEditableTextBase field, string text) =>
        _setValue.Invoke(field, BindingFlags.DoNotWrapExceptions, null, [text, true], null);
}
