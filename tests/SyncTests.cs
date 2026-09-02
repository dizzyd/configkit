using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Everything else in this suite builds a Config by hand. These tests use the real path
/// instead: a fixture mod ships a configlib-patches.json, the server loads it, and the
/// client is supposed to receive it over the recipe registry before its GUI is built.
///
/// Run with the fixture mod loaded:
///   run.sh ~/mods/configkit/tests --mod ~/mods/configkit/configkit \
///          --mods ~/mods/configkit/tests/fixtures/Mods --client
/// </summary>
public class SyncTests
{
    private const string Domain = "demomod";

    [SingleplayerOnly]
    [VsTest(TimeoutMs = 60000)]
    public async Task ServerLoadsTheFixtureConfig()
    {
        await OnServer();

        ConfigKitModSystem system = Sapi.ModLoader.GetModSystem<ConfigKitModSystem>();
        Assert.NotNull(system);
        Assert.True(system.Domains.Contains(Domain),
            $"server has no '{Domain}' config; domains: {string.Join(", ", system.Domains)}");

        IConfig? config = system.GetConfig(Domain);
        Assert.NotNull(config);
        Assert.Equal(7, config!.GetSetting("serverNumber")!.Value.AsInt());
        Assert.Equal("from-server", config.GetSetting("serverLabel")!.Value.AsString(""));
    }

    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task ClientReceivesTheServerConfig()
    {
        await OnClient();

        ConfigKitModSystem system = Capi.ModLoader.GetModSystem<ConfigKitModSystem>();
        Assert.NotNull(system);

        // If the registry never reached the client, this is where it shows: the client
        // side has no domains at all rather than wrong values.
        Assert.True(system.Domains.Contains(Domain),
            $"client received no '{Domain}' config; domains: {string.Join(", ", system.Domains)}");

        IConfig? config = system.GetConfig(Domain);
        Assert.NotNull(config);
        Assert.Equal(7, config!.GetSetting("serverNumber")!.Value.AsInt());
        Assert.Equal("from-server", config.GetSetting("serverLabel")!.Value.AsString(""));
    }

    /// <summary>
    /// The client parses the fixture's assets itself as well, so matching values alone do
    /// not prove anything travelled. This asserts the registry path actually ran and
    /// replaced the local parse - the server serialises to bytes and the client reads them
    /// back even in singleplayer.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task ConfigsCameFromTheServerNotFromLocalAssets()
    {
        await OnClient();

        ConfigKitModSystem system = Capi.ModLoader.GetModSystem<ConfigKitModSystem>();
        Assert.True(system.ConfigsReceivedFromServer,
            "client never received configs over the registry; it is showing its own local parse");
    }

    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task ClientSideSettingsArriveToo()
    {
        await OnClient();

        IConfig? config = Capi.ModLoader.GetModSystem<ConfigKitModSystem>().GetConfig(Domain);
        Assert.NotNull(config);

        ISetting? clientFlag = config!.GetSetting("clientFlag");
        Assert.NotNull(clientFlag);
        Assert.False(clientFlag!.Value.AsBool(), "clientFlag should still be at its default");
    }

    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task TheWindowIsWiredUpOnTheClient()
    {
        await OnClient();

        // The pause-menu button and the hotkey both go through ConfigGui, which the GUI
        // manager sets when configs arrive. Nothing else in this suite exercises that.
        Assert.True(ConfigGui.IsAvailable, "no settings window was registered on the client");
    }

    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task TheWindowOpensOnTheSyncedConfig()
    {
        await OnClient();

        Assert.True(ConfigGui.Show(), "settings window did not open");
        await Frames.Wait(10);

        await Shot.Take("configkit-synced");

        ConfigGui.Show();
    }
}
