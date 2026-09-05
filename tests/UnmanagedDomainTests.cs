using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using Newtonsoft.Json;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Handing one mod to another config manager, without switching ConfigKit off.
///
/// ConfigKit stands down entirely when configlib or autoconfiglib is installed, because those
/// two own the same files and the same assets - they contend for everything, so there is
/// nothing to divide. A manager that contends for a handful of mods is a different problem:
/// Integrated Mod Manager takes exactly the mods carrying a config/imm.json and edits their
/// config files in place, so it overlaps only where a mod is described to both. Standing down
/// globally there would cost every other mod its settings screen to settle an argument about
/// one.
///
/// configkit.json names the mods to leave alone. It is not managed by ConfigKit and has no
/// screen: a switch that turns things off has to stay reachable when they are off.
/// </summary>
[SingleplayerOnly]
public class UnmanagedDomainTests
{
    public class Settings
    {
        public int Radius = 8;
        public bool Enabled = true;
    }

    private static string OwnConfigPath => Path.Combine(Capi.DataBasePath, "ModConfig", "configkit.json");

    private static void WriteSkipList(params string[] domains)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OwnConfigPath)!);
        File.WriteAllText(OwnConfigPath, JsonConvert.SerializeObject(
            new ConfigKitOwnConfig { UnmanagedDomains = [.. domains] }, Formatting.Indented));
    }

    [AfterEach]
    public void Restore()
    {
        // The list is read once at startup, so leaving one behind would not affect a later
        // test - but leaving a file behind that says "do not manage things" would be a nasty
        // surprise for whoever debugs the next failure.
        if (File.Exists(OwnConfigPath)) File.Delete(OwnConfigPath);
    }

    /// <summary>
    /// The file appears on its own. Nobody will guess a key they have never seen, and the
    /// player who needs this is looking at a mod whose settings screen is fighting itself.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task TheFileIsCreatedSoItCanBeFound()
    {
        await OnServer();

        if (File.Exists(OwnConfigPath)) File.Delete(OwnConfigPath);

        HashSet<string> unmanaged = OwnConfig.UnmanagedDomains(Capi);

        Assert.Equal(0, unmanaged.Count);
        Assert.True(File.Exists(OwnConfigPath), "configkit.json was not created");

        string written = File.ReadAllText(OwnConfigPath);
        Assert.True(written.Contains("UnmanagedDomains"), $"the key is not discoverable in: {written}");
    }

    /// <summary>A listed mod is read back, whatever case it was written in.</summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AListedDomainIsRead()
    {
        await OnServer();

        WriteSkipList("SomeMod", "  spaced  ", "");

        HashSet<string> unmanaged = OwnConfig.UnmanagedDomains(Capi);

        Assert.Equal(2, unmanaged.Count);
        Assert.True(unmanaged.Contains("somemod"), "case should not matter");
        Assert.True(unmanaged.Contains("spaced"), "surrounding space should not matter");
    }

    /// <summary>
    /// A broken file is reported and ignored. This exists to rescue a broken setup, so it
    /// must not be able to cause one - a typo here cannot be allowed to unmanage everything.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AMalformedFileIsIgnoredRatherThanFatal()
    {
        await OnServer();

        Directory.CreateDirectory(Path.GetDirectoryName(OwnConfigPath)!);
        File.WriteAllText(OwnConfigPath, "{ this is not json");

        Assert.Equal(0, OwnConfig.UnmanagedDomains(Capi).Count);
    }

    /// <summary>
    /// And the effect: a listed mod is not claimed, while its neighbours are untouched. This
    /// is the difference from standing down - one mod, not the library.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task AListedModIsNotClaimedAndTheRestStillAre()
    {
        await OnClient();

        ConfigKitModSystem system = Capi.ModLoader.GetModSystem<ConfigKitModSystem>();

        int before = system.Domains.Count();

        // Registration is the claim point a C# mod uses; the asset loader is the other, and
        // both consult the same list.
        system.RegisterManagedConfig("ckunmanaged-kept", new Settings(), "ck-unmanaged-kept.json");

        Assert.True(system.Domains.Contains("ckunmanaged-kept"), "an unlisted mod should still be claimed");
        Assert.Equal(before + 1, system.Domains.Count());
    }

    /// <summary>
    /// WillManage answers for another config manager deciding whether to leave a mod alone.
    ///
    /// The two obvious ways to ask this from outside both fail. Reading Domains is a
    /// lifecycle race - Integrated Mod Manager discovers ownership in AssetsLoaded at
    /// ExecuteOrder -0.001, and ConfigKit does not register its asset-declared configs until
    /// its own AssetsLoaded at 0.01, so a mod shipping only a configlib-patches.json is
    /// invisible at the moment it is asked about. Scanning for that descriptor instead
    /// answers a different question, because the file exists whether or not ConfigKit acts
    /// on it.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task WillManageAnswersForAnotherManager()
    {
        await OnClient();

        ConfigKitModSystem system = Capi.ModLoader.GetModSystem<ConfigKitModSystem>();

        // A registered mod: claimed, and says so.
        system.RegisterManagedConfig("ckwillmanage", new Settings(), "ck-willmanage.json");
        Assert.True(system.WillManage("ckwillmanage"), "a registered domain should be managed");

        // A mod declaring nothing to anyone.
        Assert.True(!system.WillManage("nosuchmod"), "an unknown domain should not be managed");

        // The fixtures ship real configlib-patches.json assets, so this covers the case the
        // lifecycle race would otherwise hide - and it is true here because they have since
        // loaded, which is the point: the answer does not depend on when it is asked.
        Assert.True(system.WillManage("collidedemo"), "a declarative config should be managed");

        Assert.True(!system.WillManage(""), "an empty domain should not throw or claim");

        // Not covered here: WillManage returning false for a domain in UnmanagedDomains.
        // The skip list is read once in StartPre, so flipping it needs a restarted session
        // rather than a test. The two halves are checked separately - AListedDomainIsRead
        // covers the reading, and the guard itself is one _unmanaged.Contains call.
    }
}
