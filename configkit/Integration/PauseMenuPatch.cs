// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.
//
// Derived from ConfigLib by Maltiez (https://github.com/maltiez2/vsmod_configlib),
// released under CC0 1.0 Universal. Adapted to drop the Dear ImGui dependency.

using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ConfigKit.Patches;

internal static class PauseMenuPatch
{
    // Not "configlib". Patching under another mod's id means its UnpatchAll would rip out
    // ours, and every patch we own is attributed to it in any diagnostic that lists them.
    private const string HarmonyId = "com.dizzyd.configkit";

    public static void Patch()
    {
        new Harmony(HarmonyId).Patch(
            typeof(GuiComposerHelpers).GetMethod("AddButton", AccessTools.all, new Type[] {
                typeof(GuiComposer),
                typeof(string),
                typeof(ActionConsumable),
                typeof(ElementBounds),
                typeof(EnumButtonStyle),
                typeof(string)
            }),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(PauseMenuPatch), nameof(AddButton)))
            );
    }
    public static void Unpatch()
    {
        new Harmony(HarmonyId).Unpatch(
            typeof(GuiComposerHelpers).GetMethod("AddButton", AccessTools.all, new Type[] {
                typeof(GuiComposer),
                typeof(string),
                typeof(ActionConsumable),
                typeof(ElementBounds),
                typeof(EnumButtonStyle),
                typeof(string)
            }),
                HarmonyPatchType.Prefix
            );
    }

    private static bool AddButton(ref GuiComposer __result, GuiComposer composer, string text, ActionConsumable onClick, ElementBounds bounds)
    {
        if (text != Lang.Get("game:mainmenu-settings") || bounds.fixedWidth < 200) return true;

        ElementBounds left = new()
        {
            Alignment = EnumDialogArea.LeftFixed,
            BothSizing = ElementSizing.Fixed,
            fixedY = bounds.fixedY,
            fixedPaddingX = 2.0,
            fixedPaddingY = 2.0
        };

        ElementBounds right = new()
        {
            Alignment = EnumDialogArea.RightFixed,
            BothSizing = ElementSizing.Fixed,
            fixedY = bounds.fixedY,
            fixedPaddingX = 2.0,
            fixedPaddingY = 2.0
        };

        __result = composer
            .AddButton(text, onClick, left.WithFixedWidth(144))
            .AddButton("Mods settings", ConfigKit.Gui.ConfigGui.Show, right.WithFixedWidth(183));

        return false;
    }
}
