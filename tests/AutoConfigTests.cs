using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ConfigKit;
using HarmonyLib;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Auto configs: a settings screen for a mod that reads its config with LoadModConfig and
/// describes it to nobody.
///
/// The real switch is read in ConfigKit's constructor, before any mod's StartPre, and a test
/// runs long after that. So these drive the same steps by hand, against the ckautoconfig
/// fixture - a mod the game compiles from source, which therefore has no reference to
/// ConfigKit, exactly like the mods this is for. Each test uses files of its own: the live
/// mod system keeps what earlier tests registered, and a file once claimed stays claimed.
/// </summary>
[SingleplayerOnly]
public class AutoConfigTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string HarmonyId = "configkit.tests.autoconfigs";

    private static string ModConfig(string file) => Path.Combine(Capi.DataBasePath, "ModConfig", file);
    private static string OwnConfigPath => ModConfig(OwnConfig.FileName);

    private static readonly List<string> _written = [];

    private static string Written(string file)
    {
        _written.Add(file);
        return file;
    }

    private static Assembly FixtureAssembly()
    {
        Mod mod = Capi.ModLoader.GetMod("ckautoconfig")
            ?? throw new InvalidOperationException("fixture mod ckautoconfig is not loaded - pass tests/fixtures/Mods with --mods");
        return mod.Systems.First().GetType().Assembly;
    }

    private static MethodInfo Fixture(string name) => FixtureAssembly().GetType("CkAutoConfig.Loader", throwOnError: true)!
        .GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;

    private static object? Load(string file) => Fixture("Load").Invoke(null, [Capi, file]);

    private static int Radius(object settings) => (int)settings.GetType().GetField("Radius")!.GetValue(settings)!;

    private static void SetRadius(object settings, int radius) => settings.GetType().GetField("Radius")!.SetValue(settings, radius);

    private static void WriteSettings(string file, int radius)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModConfig(file))!);
        File.WriteAllText(ModConfig(file), $"{{ \"Enabled\": true, \"Radius\": {radius}, \"Rate\": 2.5 }}");
    }

    private static ConfigKitModSystem System => Capi.ModLoader.GetModSystem<ConfigKitModSystem>();

    private static void RegisterAutoConfigs() => typeof(ConfigKitModSystem).GetMethod("RegisterAutoConfigs", Private)!.Invoke(System, null);

    private static IDictionary Noted => (IDictionary)typeof(ConfigKitModSystem).GetField("_autoConfigs", Private)!.GetValue(System)!;

    /// <summary>
    /// Runs a test body as if auto configs had been on since the world loaded and nothing had
    /// been registered yet, then puts the live system back the way it was.
    /// </summary>
    private static async Task WithAutoConfigs(Func<Task> body, params string[] redirect)
    {
        FieldInfo on = typeof(ConfigKitModSystem).GetField("_autoConfigsOn", Private)!;
        FieldInfo registered = typeof(ConfigKitModSystem).GetField("_autoConfigsRegistered", Private)!;
        object? wasOn = on.GetValue(System), wasRegistered = registered.GetValue(System);

        Harmony harmony = new(HarmonyId);
        foreach (string method in redirect) AutoConfigs.Redirect(harmony, Fixture(method));

        try
        {
            on.SetValue(System, true);
            registered.SetValue(System, false);
            await body();
        }
        finally
        {
            Noted.Clear();
            on.SetValue(System, wasOn);
            registered.SetValue(System, wasRegistered);
        }
    }

    [AfterEach]
    public void Restore()
    {
        new Harmony(HarmonyId).UnpatchAll(HarmonyId);

        if (File.Exists(OwnConfigPath)) File.Delete(OwnConfigPath);
        foreach (string file in _written)
        {
            if (File.Exists(ModConfig(file))) File.Delete(ModConfig(file));
        }
        _written.Clear();
    }

    /// <summary>
    /// Off unless the file says otherwise, and anything unreadable is off. It patches other
    /// mods, so the safe reading of a typo is "no".
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task TheSwitchIsOffUntilTheFileTurnsItOn()
    {
        await OnServer();

        if (File.Exists(OwnConfigPath)) File.Delete(OwnConfigPath);
        Assert.False(OwnConfig.AutoConfigsEnabled(), "no file should mean off");

        Directory.CreateDirectory(Path.GetDirectoryName(OwnConfigPath)!);
        File.WriteAllText(OwnConfigPath, "{ \"UnmanagedDomains\": [] }");
        Assert.False(OwnConfig.AutoConfigsEnabled(), "a file without the key should mean off");

        File.WriteAllText(OwnConfigPath, "{ \"AutoConfigs\": true }");
        Assert.True(OwnConfig.AutoConfigsEnabled(), "the key set should mean on");

        File.WriteAllText(OwnConfigPath, "{ \"AutoConfigs\": tru");
        Assert.False(OwnConfig.AutoConfigsEnabled(), "a malformed file should mean off, not throw");
    }

    /// <summary>
    /// A configkit.json written before the option existed gains the key, with what was in it
    /// kept. Nobody switches on an option they cannot see. A malformed one is left as it is.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AnOlderFileGainsTheKeyAndKeepsItsValues()
    {
        await OnServer();

        Directory.CreateDirectory(Path.GetDirectoryName(OwnConfigPath)!);
        File.WriteAllText(OwnConfigPath, "{ \"UnmanagedDomains\": [ \"somemod\" ] }");

        OwnConfig.AddMissingKeys(Capi);

        string written = File.ReadAllText(OwnConfigPath);
        Assert.True(written.Contains("\"AutoConfigs\": false"), $"the key should have been added: {written}");
        Assert.True(written.Contains("somemod"), $"the existing list should still be on disk: {written}");
        Assert.True(OwnConfig.UnmanagedDomains(Capi).Contains("somemod"), "the existing list should still read back");

        File.WriteAllText(OwnConfigPath, "{ broken");
        OwnConfig.AddMissingKeys(Capi);
        Assert.Equal("{ broken", File.ReadAllText(OwnConfigPath));
    }

    /// <summary>
    /// The scan finds a mod's typed config calls and nothing else: not the untyped overload,
    /// which has no class to build from, and not a generic helper, which Harmony cannot patch.
    /// And it looks only in mods that have not chosen a config library of their own.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task TheScanFindsTypedConfigCallsOnly()
    {
        await OnClient();

        List<string> callers = AutoConfigs.Callers(FixtureAssembly()).Select(method => method.Name).ToList();

        Assert.True(callers.Contains("Load"), $"LoadModConfig<T> caller not found in: {string.Join(", ", callers)}");
        Assert.True(callers.Contains("Save"), $"StoreModConfig<T> caller not found in: {string.Join(", ", callers)}");
        Assert.False(callers.Contains("Raw"), "the untyped LoadModConfig has nothing to build a screen from");
        Assert.False(callers.Contains("Generic"), "a generic method cannot be patched");

        List<Assembly> candidates = AutoConfigs.Candidates().ToList();
        Assert.True(candidates.Contains(FixtureAssembly()), "the fixture mod should be a candidate");
        Assert.False(candidates.Contains(typeof(ConfigKitModSystem).Assembly), "ConfigKit should leave itself alone");
        Assert.False(candidates.Contains(typeof(AutoConfigTests).Assembly), "a mod referencing ConfigKit should be left alone");
    }

    /// <summary>
    /// End to end: the redirected calls behave exactly as written, ConfigKit notes what they
    /// returned, and registration gives that very object a screen - so an edit on the screen
    /// reaches the object the mod is holding.
    ///
    /// Covers both ways a mod meets its config: a load of an existing file, and a first run,
    /// where the load finds nothing and the mod stores its defaults instead.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task ARedirectedModGetsAScreenOverTheObjectItHolds()
    {
        await OnClient();

        string existing = Written("ckautoconfig.json");
        string firstRun = Written("ckautoconfig-firstrun.json");
        WriteSettings(existing, 20);
        if (File.Exists(ModConfig(firstRun))) File.Delete(ModConfig(firstRun));

        await WithAutoConfigs(async () =>
        {
            object loaded = Load(existing)!;
            Assert.Equal(20, Radius(loaded));

            // First run: nothing to load, so the mod makes defaults and stores them.
            Assert.Null(Load(firstRun));
            object defaults = Activator.CreateInstance(loaded.GetType())!;
            Fixture("Save").Invoke(null, [Capi, defaults, firstRun]);
            Assert.True(File.Exists(ModConfig(firstRun)), "the redirected store should still have written the file");

            RegisterAutoConfigs();

            Assert.True(System.Domains.Contains("ckautoconfig"), $"no auto config registered; domains: {string.Join(", ", System.Domains)}");
            Assert.True(System.Domains.Contains("ckautoconfig-firstrun"), "the first-run file should have been registered from its store");

            ISetting radius = System.GetSetting("ckautoconfig", "Radius")!;
            Assert.NotNull(radius);
            Assert.Equal(20, radius.Value.AsInt());

            // The screen edits the mod's own object, not a copy.
            radius.Value = new JsonObject(new Newtonsoft.Json.Linq.JValue(33));
            Assert.Equal(33, Radius(loaded));
            await Task.CompletedTask;
        }, "Load", "Save");
    }

    /// <summary>
    /// A mod that loads its config again holds a new object. The screen has to follow it, or
    /// every edit lands on the object the mod dropped and looks saved while doing nothing.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task AReloadedConfigTakesTheScreenWithIt()
    {
        await OnClient();

        string file = Written("ckautoconfig-reloaded.json");
        WriteSettings(file, 11);

        await WithAutoConfigs(async () =>
        {
            object first = Load(file)!;
            RegisterAutoConfigs();

            object second = Load(file)!;
            Assert.False(ReferenceEquals(first, second), "the mod should have a new object after loading again");

            System.GetSetting("ckautoconfig-reloaded", "Radius")!.Value = new JsonObject(new Newtonsoft.Json.Linq.JValue(44));

            Assert.Equal(44, Radius(second));
            Assert.Equal(11, Radius(first));
            await Task.CompletedTask;
        }, "Load");
    }

    /// <summary>
    /// A file the mod has already given ConfigKit, under a domain that is not its mod id, is
    /// not given to it a second time. Two configs over one file is two writers and two
    /// watchers - the conflict standing down exists to prevent.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task AFileTheModRegisteredItselfIsLeftAlone()
    {
        await OnClient();

        string file = Written("ckautoconfig-described.json");
        WriteSettings(file, 5);

        await WithAutoConfigs(async () =>
        {
            object loaded = Load(file)!;
            System.RegisterManagedConfig("ckautoselfdescribed", loaded, file);
            System.SetConfigDisplayName("ckautoselfdescribed", "Described By Itself");

            RegisterAutoConfigs();

            Assert.False(System.Domains.Contains("ckautoconfig-described"), "the file was already registered by its mod");
            await Task.CompletedTask;
        }, "Load");
    }

    /// <summary>
    /// Registering an auto config leaves the mod's file as it was. The values were just read
    /// from it, and a rewrite would only strip what the player wrote there, comments included.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task RegistrationDoesNotRewriteTheModsFile()
    {
        await OnClient();

        string file = Written("ckautoconfig-commented.json");
        const string original = "{\n  // the player's own note\n  \"Radius\": 9\n}";
        Directory.CreateDirectory(Path.GetDirectoryName(ModConfig(file))!);
        File.WriteAllText(ModConfig(file), original);

        await WithAutoConfigs(async () =>
        {
            Load(file);
            RegisterAutoConfigs();

            Assert.True(System.Domains.Contains("ckautoconfig-commented"), "the auto config should have registered");
            Assert.Equal(original, File.ReadAllText(ModConfig(file)));
            await Task.CompletedTask;
        }, "Load");
    }

    /// <summary>
    /// What the mod did to its object after loading it survives registration. Toolsmith
    /// empties its tool lists straight after loading and appends to them as its assets load;
    /// registration put the file's lists back in between, so every launch appended another
    /// copy until the lists matched no tool at all.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task RegistrationKeepsWhatTheModChangedAfterLoading()
    {
        await OnClient();

        string file = Written("ckautoconfig-changed.json");
        WriteSettings(file, 20);

        await WithAutoConfigs(async () =>
        {
            object loaded = Load(file)!;
            SetRadius(loaded, 7);

            RegisterAutoConfigs();

            Assert.Equal(7, Radius(loaded));
            Assert.Equal(7, System.GetSetting("ckautoconfig-changed", "Radius")!.Value.AsInt());
            await Task.CompletedTask;
        }, "Load");
    }

    /// <summary>
    /// An object that cannot be serialised has no document to read values from, so its
    /// settings come from the file - and then the file's values have to reach the object, or
    /// the screen shows one value while the mod runs on another.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task AnObjectThatCannotBeWrittenTakesTheFilesValues()
    {
        await OnClient();

        string file = Written("ckautoconfig-fragile.json");
        WriteSettings(file, 20);
        FieldInfo tangle = FixtureAssembly().GetType("CkAutoConfig.Fragile", throwOnError: true)!.GetField("Tangle")!;

        await WithAutoConfigs(async () =>
        {
            try
            {
                object loaded = Fixture("LoadFragile").Invoke(null, [Capi, file])!;
                SetRadius(loaded, 7);
                tangle.SetValue(null, true);

                RegisterAutoConfigs();

                Assert.Equal(20, System.GetSetting("ckautoconfig-fragile", "Radius")!.Value.AsInt());
                Assert.Equal(20, Radius(loaded));
            }
            finally
            {
                tangle.SetValue(null, false);
            }
            await Task.CompletedTask;
        }, "LoadFragile");
    }

    public class Bounded { [System.ComponentModel.DataAnnotations.Range(1, 10)] public int Radius = 5; }

    /// <summary>
    /// A value the mod's own attributes reject shows on its row. Assigning used to be what
    /// checked it, and a config read from its object no longer assigns at registration.
    /// The mod keeps the value either way: it loaded it itself.
    ///
    /// Registered directly rather than through the fixture: the game compiles source mods
    /// without System.ComponentModel.DataAnnotations, so the fixture cannot carry a [Range].
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task AValueTheModsAttributesRejectIsShown()
    {
        await OnClient();

        string file = Written("ckautoconfig-bounded.json");
        Bounded loaded = new() { Radius = 50 };

        bool registered = (bool)typeof(ConfigKitModSystem).GetMethod("TryRegisterManagedConfig", Private)!
            .Invoke(System, ["ckautoconfig-bounded", loaded, file, null, null, null, true])!;

        Assert.True(registered, "the adopted config should have registered");
        Assert.NotNull(((ConfigSetting)System.GetSetting("ckautoconfig-bounded", "Radius")!).Error);
        Assert.Equal(50, loaded.Radius);
    }

    private const string InliningSource = """
        using System.Runtime.CompilerServices;
        using Vintagestory.API.Common;

        public class InlineSettings { public int Radius = 1; }

        public static class InlineLoader
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static InlineSettings Tiny(ICoreAPI api, string file) => api.LoadModConfig<InlineSettings>(file);

            // Straight to full optimisation. A first call otherwise compiles at tier 0, which
            // inlines nothing; a mod's hot method gets here by tiering up after enough calls.
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            public static InlineSettings ViaTiny(ICoreAPI api, string file) => Tiny(api, file);
        }
        """;

    /// <summary>
    /// The inlining fixture, compiled optimised as a shipped mod is. The game compiles source
    /// mods - ckautoconfig included - unoptimised, and the JIT inlines nothing there.
    /// </summary>
    private static Type CompileInlineLoader()
    {
        IEnumerable<MetadataReference> references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location));

        CSharpCompilation compilation = CSharpCompilation.Create("ckinline" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(InliningSource)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

        using MemoryStream image = new();
        EmitResult result = compilation.Emit(image);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        return Assembly.Load(image.ToArray()).GetType("InlineLoader", throwOnError: true)!;
    }

    /// <summary>
    /// A config load inside a helper small enough to be inlined still reaches ConfigKit. The
    /// redirect goes in before any mod method is compiled, so the question is whether the JIT
    /// inlines the helper's original body into a caller compiled afterwards.
    ///
    /// The control shows inlining is real here: a caller compiled before the redirect keeps
    /// the original call. Without it a pass would prove nothing.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task AnInlinableHelperIsStillRedirected()
    {
        await OnClient();

        string file = Written("ckautoconfig-tiny.json");
        WriteSettings(file, 3);

        await WithAutoConfigs(async () =>
        {
            Harmony harmony = new(HarmonyId);

            Type control = CompileInlineLoader();
            control.GetMethod("ViaTiny")!.Invoke(null, [Capi, file]);
            AutoConfigs.Redirect(harmony, control.GetMethod("Tiny")!);
            control.GetMethod("ViaTiny")!.Invoke(null, [Capi, file]);
            bool inlined = !Noted.Contains(file);
            Log($"control: a caller compiled before the redirect {(inlined ? "inlined the helper" : "still called it")}");

            Type subject = CompileInlineLoader();
            AutoConfigs.Redirect(harmony, subject.GetMethod("Tiny")!);
            subject.GetMethod("ViaTiny")!.Invoke(null, [Capi, file]);

            Assert.True(inlined, "the control did not inline, so this run cannot say anything about inlining");
            Assert.True(Noted.Contains(file), "a caller compiled after the redirect inlined the helper's original call");
            await Task.CompletedTask;
        });
    }

    /// <summary>
    /// WillManage answers for a mod with an auto config still waiting for AssetsLoaded, so a
    /// config manager asking early does not take a mod ConfigKit is about to claim.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task WillManageCountsAPendingAutoConfig()
    {
        await OnClient();

        string file = Written("ckautoconfig-pending.json");
        WriteSettings(file, 4);

        await WithAutoConfigs(async () =>
        {
            Load(file);

            bool pending = (bool)typeof(ConfigKitModSystem).GetMethod("AutoConfigPending", Private)!.Invoke(System, ["ckautoconfig"])!;
            Assert.True(pending, "a noted config should count as one ConfigKit will manage");

            bool other = (bool)typeof(ConfigKitModSystem).GetMethod("AutoConfigPending", Private)!.Invoke(System, ["nosuchmod"])!;
            Assert.False(other, "a mod with nothing noted should not");
            await Task.CompletedTask;
        }, "Load");
    }

    /// <summary>
    /// A domain is a function of the mod id and the file alone, so a server and a client that
    /// load the same file agree on it without talking - the sync matches configs by domain.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task DomainsDependOnlyOnTheModAndTheFile()
    {
        await OnServer();

        Assert.Equal("mymod", AutoConfigs.DomainFor("mymod", "mymod.json"));
        Assert.Equal("mymod", AutoConfigs.DomainFor("mymod", "MyMod.json"));
        Assert.Equal("mymod-config", AutoConfigs.DomainFor("mymod", "MyModConfig.json"));
        Assert.Equal("mymod-server", AutoConfigs.DomainFor("mymod", "mymod/server.json"));
        Assert.Equal("mymod-server", AutoConfigs.DomainFor("mymod", "mymod\\server.json"));
        Assert.Equal("mymod-other-config", AutoConfigs.DomainFor("mymod", "Other Config.json"));
    }

    public class Editable { public int Count = 3; }
    public class NothingPublic { private int _count = 3; public int Peek() => _count; }
    public struct Valued { public int Count; }

    /// <summary>
    /// What gets a screen. The object is edited in place, so it must be a class the mod holds
    /// a reference to, and it must have something on it to edit.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task OnlyEditableClassesGetAScreen()
    {
        await OnServer();

        Assert.Null(AutoConfigs.WhyNot(typeof(Editable)));
        Assert.NotNull(AutoConfigs.WhyNot(typeof(Valued)));
        Assert.NotNull(AutoConfigs.WhyNot(typeof(NothingPublic)));
        Assert.NotNull(AutoConfigs.WhyNot(typeof(JsonObject)));
        Assert.NotNull(AutoConfigs.WhyNot(typeof(Newtonsoft.Json.Linq.JObject)));
        Assert.NotNull(AutoConfigs.WhyNot(typeof(List<int>)));
        Assert.NotNull(AutoConfigs.WhyNot(typeof(Dictionary<string, int>)));
        Assert.NotNull(AutoConfigs.WhyNot(typeof(string)));
    }
}
