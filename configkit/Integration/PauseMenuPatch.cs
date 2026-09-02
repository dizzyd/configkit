// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.
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

    /// Tracked so Dispose cannot unpatch something we never patched - which matters when
    /// ConfigKit has stood down in favour of another config mod and never patched at all.
    private static bool _patched;

    public static void Patch()
    {
        if (_patched) return;

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

        _patched = true;
    }
    public static void Unpatch()
    {
        if (!_patched) return;

        new Harmony(HarmonyId).Unpatch(
            typeof(GuiComposerHelpers).GetMethod("AddButton", AccessTools.all, new Type[] {
                typeof(GuiComposer),
                typeof(string),
                typeof(ActionConsumable),
                typeof(ElementBounds),
                typeof(EnumButtonStyle),
                typeof(string)
            }),
                HarmonyPatchType.Prefix,
                // Harmony's third parameter defaults to "*", which removes EVERY owner's
                // prefix on this method - including other config mods' pause-menu buttons.
                // Constructing the Harmony instance with our id does not scope the call.
                HarmonyId
            );

        _patched = false;
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
