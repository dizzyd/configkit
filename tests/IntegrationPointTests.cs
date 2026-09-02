using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ConfigKit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// The seams where ConfigKit meets the game and other mods: the Harmony patch behind the
/// pause-menu button, and what happens when it is not the config mod in charge.
/// </summary>
public class IntegrationPointTests
{
    private const string HarmonyId = "com.dizzyd.configkit";

    private static MethodInfo PatchedAddButton() =>
        typeof(GuiComposerHelpers).GetMethod("AddButton", AccessTools.all, null, new[]
        {
            typeof(GuiComposer), typeof(string), typeof(ActionConsumable),
            typeof(ElementBounds), typeof(EnumButtonStyle), typeof(string)
        }, null);

    /// <summary>
    /// Registered exactly once. ModSystem.Start runs once per side, and in singleplayer
    /// both sides are the same assembly - the trap that has bitten this workspace before,
    /// where a postfix ran twice and only showed up when it stopped being idempotent.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ThePauseMenuPatchIsRegisteredExactlyOnce()
    {
        await OnClient();

        MethodInfo target = PatchedAddButton();
        Assert.NotNull(target);

        Patches info = Harmony.GetPatchInfo(target);
        Assert.NotNull(info);

        int ours = info!.Prefixes.Count(p => p.owner == HarmonyId);
        Assert.Equal(1, ours);
    }

    /// <summary>
    /// The button and the hotkey both route through ConfigGui, which is the seam the GUI
    /// layer registers itself on. Nothing else asserts the pause menu can reach it.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ThePauseMenuRouteReachesTheWindow()
    {
        await OnClient();

        Assert.True(ConfigKit.Gui.ConfigGui.IsAvailable, "nothing registered a settings window");

        Assert.True(ConfigKit.Gui.ConfigGui.Show(), "the pause-menu route did not open the window");
        await Frames.Wait(5);
        ConfigKit.Gui.ConfigGui.Show();
    }

    /// <summary>
    /// A config file written against an older definition must not be applied blindly on top
    /// of a newer one - the settings may mean different things. The file is rejected and
    /// defaults are used.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task AConfigFileFromAnOlderVersionIsNotAppliedToANewerDefinition()
    {
        await OnClient();

        const string v1 = @"{ ""version"": 1, ""settings"": [
            { ""type"": ""integer"", ""code"": ""count"", ""default"": 5 } ] }";

        Config first = new(Capi, "versioned", "versioned", new JsonObject(JToken.Parse(v1)));
        first.GetSetting("count")!.Value = new JsonObject(new JValue(77));
        first.WriteToFile();

        Assert.True(File.Exists(first.ConfigFilePath));

        // Same domain, same file, but the definition has moved on.
        const string v2 = @"{ ""version"": 2, ""settings"": [
            { ""type"": ""integer"", ""code"": ""count"", ""default"": 5 } ] }";

        Config second = new(Capi, "versioned", "versioned", new JsonObject(JToken.Parse(v2)));

        Assert.Equal(2, second.Version);
        Assert.Equal(5, second.GetSetting("count")!.Value.AsInt());
    }

    /// <summary>
    /// Two config mods managing one file is worse than none, so ConfigKit defers. Skipped
    /// unless a session actually has ConfigLib loaded - run it with
    /// --mods pointing at a directory containing configlib.
    /// </summary>
    [SingleplayerOnly]
    [VsTest(TimeoutMs = 60000)]
    public async Task ItStandsDownWhenAnotherConfigModIsInstalled()
    {
        await OnServer();

        if (!Sapi.ModLoader.IsModEnabled("configlib") && !Sapi.ModLoader.IsModEnabled("autoconfiglib"))
        {
            Log("no other config mod present - nothing to stand down for");
            return;
        }

        ConfigKitModSystem system = Sapi.ModLoader.GetModSystem<ConfigKitModSystem>();

        Assert.Equal(0, system.Domains.Count());
        Assert.Null(system.GetConfig("demomod"));
    }
}
