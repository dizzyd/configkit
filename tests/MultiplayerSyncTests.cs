using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Runs in the CLIENT process of a two-process session, against a real server on the
/// other end of a socket:
///
///   bash scripts/boot.sh --multiplayer
///   bash scripts/run.sh ~/mods/configkit/tests --mod ... --mods ... --multiplayer
///
/// Everything else in this suite is singleplayer, where client and server share one
/// process and `IsSinglePlayer` is true. ConfigKit's sync takes a completely different
/// branch there - and both bugs found in review lived on the branch these tests reach.
/// </summary>
public class MultiplayerSyncTests
{
    private const string Domain = "demomod";

    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    [RequiresMultiplayer]
    public async Task WeAreActuallyOnARemoteServer()
    {
        await OnClient();

        Assert.True(Remote.Available, "not a two-process session - boot with --multiplayer");
        Assert.False(Capi.IsSinglePlayer,
            "this suite is meaningless in singleplayer: ConfigKit takes the other sync branch");
    }

    /// <summary>
    /// The regression that 14 singleplayer tests missed: SyncFromServer cleared the very
    /// dictionary it then iterated, so a synced config arrived with no settings at all.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    [RequiresMultiplayer]
    public async Task ASyncedConfigStillHasItsSettings()
    {
        await OnClient();

        IConfig? config = Capi.ModLoader.GetModSystem<ConfigKitModSystem>().GetConfig(Domain);
        Assert.NotNull(config);

        foreach (string code in new[] { "serverFlag", "serverNumber", "serverLabel", "clientFlag" })
        {
            Assert.NotNull(config!.GetSetting(code));
        }

        Assert.Equal(7, config!.GetSetting("serverNumber")!.Value.AsInt());
    }

    /// <summary>
    /// What the whole design is for: the server's value, not the client's local default.
    /// The client parses the same asset itself, so the two must differ for this to prove
    /// anything - the server's value is changed over the bridge before we look.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    [RequiresMultiplayer]
    public async Task TheServersValueWinsOverTheClientsLocalDefault()
    {
        await OnClient();

        string onServer = await Remote.Eval(
            "sapi.ModLoader.GetModSystem<ConfigKit.ConfigKitModSystem>()" +
            ".GetConfig(\"demomod\").GetSetting(\"serverNumber\").Value.AsInt()");

        Log($"server reports serverNumber = {onServer}");

        int onClient = Capi.ModLoader.GetModSystem<ConfigKitModSystem>()
            .GetConfig(Domain)!.GetSetting("serverNumber")!.Value.AsInt();

        // Same value, read from two different processes across a socket.
        Assert.Equal(onServer.Trim(), onClient.ToString());
    }

    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    [RequiresMultiplayer]
    public async Task ClientSideSettingsAreNotOverwrittenByTheServer()
    {
        await OnClient();

        IConfig config = Capi.ModLoader.GetModSystem<ConfigKitModSystem>().GetConfig(Domain)!;

        ISetting clientFlag = config.GetSetting("clientFlag")!;
        Assert.False(clientFlag.Value.AsBool(), "clientFlag should still be at its local default");
    }

    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    [RequiresMultiplayer]
    public async Task TheWindowOpensAgainstASyncedConfig()
    {
        await OnClient();

        Assert.True(ConfigKit.Gui.ConfigGui.IsAvailable,
            "no settings window on a client joined to a remote server");

        Assert.True(ConfigKit.Gui.ConfigGui.Show(), "settings window did not open");
        await Frames.Wait(10);
        await Shot.Take("configkit-multiplayer");
        ConfigKit.Gui.ConfigGui.Show();
    }
}
