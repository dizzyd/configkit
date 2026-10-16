// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.

using HarmonyLib;
using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;

namespace ConfigKit;

/// <summary>
/// Settings screens for mods that never described their config. A mod that reads its config
/// with <c>LoadModConfig&lt;T&gt;</c> gets a screen built from <c>T</c>, exactly as if it had
/// handed that object to <see cref="ConfigKitModSystem.RegisterManagedConfig"/> itself.
///
/// Off unless <c>ModConfig/configkit.json</c> turns it on. It rewrites a call in every mod that
/// makes one, and most players have ConfigKit because some other mod depends on it rather than
/// because they chose it, so this is theirs to opt into.
///
/// How: ConfigKit's constructor runs after every mod assembly is loaded and before any mod's
/// StartPre, which is the one window in which no mod has read its config yet. There every
/// mod's calls to <c>LoadModConfig&lt;T&gt;</c> and <c>StoreModConfig&lt;T&gt;</c> are
/// redirected through here. The call still happens exactly as written; this only notes which
/// object went with which file. ConfigKit registers what it noted in its own AssetsLoaded,
/// after the configlib-patches.json assets, so a mod that describes itself always wins over a
/// guess.
///
/// Public only because the redirected IL lives in other mods' assemblies and has to be able
/// to call <see cref="LoadThrough"/> and <see cref="StoreThrough"/>. Nothing here is an API.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AutoConfigs
{
    private const string HarmonyId = "com.dizzyd.configkit.autoconfigs";

    private static readonly MethodInfo LoadDefinition = GenericDefinition(nameof(ICoreAPICommon.LoadModConfig));
    private static readonly MethodInfo StoreDefinition = GenericDefinition(nameof(ICoreAPICommon.StoreModConfig));

    /// <summary>
    /// The assemblies of configlib and Auto Config Lib. Assembly names, not mod ids - those are
    /// in ConfigKitModSystem.Incumbents, which decides standing down once an api exists. This
    /// list answers the same question earlier, in the constructor, where there is none.
    /// </summary>
    private static readonly string[] IncumbentAssemblies = ["configlib", "AutoConfigLib"];

    /// <summary>
    /// A mod referencing one of these has already decided what its settings screen is, and a
    /// guess would only compete with it.
    /// </summary>
    private static readonly string[] SelfManaged = ["ConfigKit", .. IncumbentAssemblies];

    /// <summary>
    /// Every method already redirected, across worlds. Patches outlive the world that made
    /// them, and singleplayer constructs this mod twice, once per side - on different threads.
    /// </summary>
    private static readonly HashSet<MethodBase> Redirected = [];

    /// <summary>What the last <see cref="Install"/> did, for StartPre to log; there is no logger yet.</summary>
    internal static string? Report { get; private set; }

    /// <summary>
    /// Redirects every mod's config calls, if configkit.json asks for it. When it does not,
    /// counts the assemblies that make such calls, so the log can say the option exists.
    /// Never throws: this runs inside the mod loader, where an exception takes the game down.
    /// </summary>
    /// <returns>Whether config calls are being redirected for this world.</returns>
    internal static bool Install()
    {
        lock (Redirected)
        {
            try
            {
                bool enabled = OwnConfig.AutoConfigsEnabled();

                // ConfigKit is about to stand down, and its own StartPre will say so.
                // AutoConfigLib rewrites these same calls.
                if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => IncumbentAssemblies.Contains(assembly.GetName().Name, StringComparer.OrdinalIgnoreCase)))
                {
                    Report = null;
                    return false;
                }

                // Not every one of these lacks a screen - some describe themselves through an
                // asset or by reflection - and that cannot be known until assets are loaded.
                List<Assembly> candidates = Candidates().Where(assembly => CallTokens(assembly).Count > 0).ToList();

                if (!enabled)
                {
                    Report = candidates.Count == 0 ? null
                        : $"{candidates.Count} mod assembl{(candidates.Count == 1 ? "y reads" : "ies read")} a config with LoadModConfig. "
                        + $"Set \"AutoConfigs\": true in ModConfig/{OwnConfig.FileName} to give the ones without a settings screen one; "
                        + "it takes effect when a world next loads.";
                    return false;
                }

                Harmony harmony = new(HarmonyId);
                int methods = 0;
                List<string> failures = [];

                foreach (Assembly assembly in candidates)
                {
                    foreach (MethodBase method in Callers(assembly))
                    {
                        // Already done by the other side, or by an earlier world.
                        if (Redirected.Contains(method))
                        {
                            methods++;
                            continue;
                        }

                        try
                        {
                            Redirect(harmony, method);
                            Redirected.Add(method);
                            methods++;
                        }
                        catch (Exception exception)
                        {
                            failures.Add($"{method.DeclaringType?.FullName}.{method.Name}: {exception.Message}");
                        }
                    }
                }

                Report = $"Auto configs on: redirected config calls in {methods} method(s) across {candidates.Count} mod assemblies."
                    + (failures.Count == 0 ? "" : $" Could not redirect {failures.Count}: {string.Join("; ", failures)}");
                return true;
            }
            catch (Exception exception)
            {
                Report = $"Auto configs failed and are off for this session: {exception}";
                return false;
            }
        }
    }

    /// <summary>
    /// Mod assemblies: loaded, built against the game's API, not part of the game itself, and
    /// not already working with a config library of their own.
    /// </summary>
    public static IEnumerable<Assembly> Candidates()
    {
        string game = WithSeparator(GamePaths.Binaries);
        string data = WithSeparator(GamePaths.DataPath);
        Assembly own = typeof(AutoConfigs).Assembly;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || assembly == own) continue;

            // Whatever ships with the game lives under its install. So can the data folder:
            // a server run from its install with --dataPath ./data has every mod under it too.
            // A mod compiled in-game from source, or loaded from bytes, has no location.
            string location = assembly.Location;
            if (location.Length > 0)
            {
                string path = Path.GetFullPath(location);
                if (path.StartsWith(game, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(data, StringComparison.OrdinalIgnoreCase)) continue;
            }

            AssemblyName[] references;
            try
            {
                references = assembly.GetReferencedAssemblies();
            }
            catch (Exception)
            {
                continue;
            }

            if (!references.Any(reference => reference.Name == "VintagestoryAPI")) continue;
            if (references.Any(reference => SelfManaged.Contains(reference.Name, StringComparer.OrdinalIgnoreCase))) continue;

            yield return assembly;
        }
    }

    private static string WithSeparator(string directory)
    {
        string full = Path.GetFullPath(directory);
        return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// Methods in an assembly that call <c>LoadModConfig&lt;T&gt;</c> or <c>StoreModConfig&lt;T&gt;</c>
    /// with a concrete <c>T</c>. Methods that are themselves generic, or live in a generic type,
    /// are left out: Harmony cannot patch an open generic, and <c>T</c> would not say what the
    /// config is anyway. Type initialisers are left out too: patching one is unreliable, and
    /// it may have run before ConfigKit's constructor did.
    /// </summary>
    public static IEnumerable<MethodBase> Callers(Assembly assembly)
    {
        HashSet<int> tokens = CallTokens(assembly);
        if (tokens.Count == 0) yield break;

        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (Type type in Types(assembly))
        {
            if (type.ContainsGenericParameters) continue;

            IEnumerable<MethodBase> methods = type.GetMethods(all).Cast<MethodBase>()
                .Concat(type.GetConstructors(all).Where(constructor => !constructor.IsStatic));

            foreach (MethodBase method in methods)
            {
                if (method.IsAbstract || method.ContainsGenericParameters) continue;
                if (MentionsAny(method, tokens) && CallsTarget(method)) yield return method;
            }
        }
    }

    /// <summary>
    /// Puts one method's config calls through here. Each becomes a static call carrying the
    /// same arguments plus the name of the calling assembly, which is how a config is traced
    /// back to its mod.
    /// </summary>
    public static void Redirect(Harmony harmony, MethodBase method)
        => harmony.Patch(method, transpiler: new HarmonyMethod(typeof(AutoConfigs), nameof(Transpiler)));

    /// <summary>
    /// The redirected <c>LoadModConfig&lt;T&gt;</c>. Loads exactly as before, then notes what
    /// came back. Never lets anything of ConfigKit's escape into the calling mod.
    /// </summary>
    public static T LoadThrough<T>(ICoreAPICommon api, string filename, string assembly)
    {
        T value = api.LoadModConfig<T>(filename);
        Note(api, assembly, filename, value, loaded: true);
        return value;
    }

    /// <summary>
    /// The redirected <c>StoreModConfig&lt;T&gt;</c>. A mod's first run loads nothing - there
    /// is no file yet - and then stores the defaults it made instead, so without this the
    /// screen would only appear on the second launch.
    /// </summary>
    public static void StoreThrough<T>(ICoreAPICommon api, T data, string filename, string assembly)
    {
        api.StoreModConfig<T>(data, filename);
        Note(api, assembly, filename, data, loaded: false);
    }

    private static void Note(ICoreAPICommon api, string assembly, string filename, object? value, bool loaded)
    {
        if (value == null || api is not ICoreAPI full) return;

        try
        {
            full.ModLoader.GetModSystem<ConfigKitModSystem>()?.NoteAutoConfig(assembly, filename, value, loaded);
        }
        catch (Exception exception)
        {
            LoggerUtil.Verbose(full, typeof(AutoConfigs), $"Could not note '{filename}': {exception.Message}");
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        string assembly = original.DeclaringType?.Assembly.FullName ?? "";
        CodeInstruction? previous = null;

        foreach (CodeInstruction instruction in instructions)
        {
            // A prefix binds to the instruction right after it: a constrained. callvirt cannot
            // become a call, and nothing can be inserted between tail. and its call.
            bool prefixed = previous?.opcode == OpCodes.Constrained || previous?.opcode == OpCodes.Tailcall;

            if (!prefixed && TargetOf(instruction) is { } target)
            {
                string through = target.GetGenericMethodDefinition() == LoadDefinition ? nameof(LoadThrough) : nameof(StoreThrough);

                // The calling assembly goes on the stack as one more argument. Labels and
                // exception blocks move to it, since it is now the first instruction of the call.
                CodeInstruction name = new(OpCodes.Ldstr, assembly);
                instruction.MoveLabelsTo(name);
                instruction.MoveBlocksTo(name);
                yield return name;

                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(AutoConfigs), through).MakeGenericMethod(target.GetGenericArguments());
            }

            previous = instruction;
            yield return instruction;
        }
    }

    private static bool IsConfigCall(MethodInfo method)
        => method.IsGenericMethod && method.GetGenericMethodDefinition() is { } definition
        && (definition == LoadDefinition || definition == StoreDefinition);

    private static MethodInfo? TargetOf(CodeInstruction instruction)
    {
        if (instruction.opcode != OpCodes.Callvirt && instruction.opcode != OpCodes.Call) return null;
        return instruction.operand is MethodInfo called && IsConfigCall(called) ? called : null;
    }

    private static bool CallsTarget(MethodBase method)
    {
        try
        {
            return PatchProcessor.GetOriginalInstructions(method).Any(instruction => TargetOf(instruction) != null);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The metadata tokens an assembly uses for <c>LoadModConfig&lt;T&gt;</c> and
    /// <c>StoreModConfig&lt;T&gt;</c>. A generic method call is always a MethodSpec, and there
    /// are few enough of those that reading them all is far cheaper than decoding every
    /// method body - and an assembly with none, which is most of them, is done after this.
    /// </summary>
    private static HashSet<int> CallTokens(Assembly assembly)
    {
        HashSet<int> tokens = [];

        foreach (Module module in assembly.GetModules())
        {
            // The table's length is not exposed. ResolveMethod throws ArgumentOutOfRange one row
            // past its end, and that is how this loop finishes; the bound is only the token format's.
            for (int row = 1; row < 0x00FFFFFF; row++)
            {
                int token = 0x2B000000 | row;
                try
                {
                    if (module.ResolveMethod(token) is MethodInfo method && IsConfigCall(method)) tokens.Add(token);
                }
                catch (ArgumentOutOfRangeException)
                {
                    break;
                }
                catch (Exception)
                {
                    // An instantiation over a type parameter cannot resolve without its
                    // generic context. Those are the generic callers Callers skips anyway.
                }
            }
        }

        return tokens;
    }

    /// <summary>
    /// A cheap filter before decoding a body: does a call or callvirt to one of these tokens
    /// appear in its raw IL. A byte match can be a coincidence, so CallsTarget confirms it.
    /// </summary>
    private static bool MentionsAny(MethodBase method, HashSet<int> tokens)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception)
        {
            return false;
        }

        if (il == null) return false;

        for (int index = 0; index + 4 < il.Length; index++)
        {
            if ((il[index] == 0x6F || il[index] == 0x28) && tokens.Contains(BitConverter.ToInt32(il, index + 1))) return true;
        }

        return false;
    }

    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static MethodInfo GenericDefinition(string name)
        => typeof(ICoreAPICommon).GetMethods().Single(method => method.Name == name && method.IsGenericMethodDefinition);

    // What follows decides what a noted config becomes, for ConfigKitModSystem.RegisterAutoConfigs.

    /// <summary>
    /// The mod a calling assembly belongs to: the one that loaded it, or failing that, the one
    /// whose mod systems come from it.
    /// </summary>
    public static Mod? ModFor(ICoreAPI api, string assembly)
    {
        foreach (Mod mod in api.ModLoader.Mods)
        {
            if ((mod as ModContainer)?.Assembly?.FullName == assembly) return mod;
        }

        return api.ModLoader.Mods.FirstOrDefault(mod => mod.Systems.Any(system => system.GetType().Assembly.FullName == assembly));
    }

    /// <summary>
    /// The domain an auto config registers under. The mod id when the file is named after the
    /// mod, as most are; otherwise the mod id and the rest of the file's name, so a mod with
    /// several files gets a config for each. It depends on nothing but the two names, so a
    /// server and a client that both load a file agree on what to call it - the sync matches
    /// them by domain. A mod id is letters and digits only, so the hyphen keeps these from
    /// ever colliding with one.
    /// </summary>
    public static string DomainFor(string modId, string filename)
    {
        string stem = (Path.ChangeExtension(filename.Replace('\\', '/'), null) ?? filename).ToLowerInvariant();
        if (stem.StartsWith(modId, StringComparison.Ordinal)) stem = stem[modId.Length..];

        string slug = new string(stem.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray()).Trim('-');
        return slug.Length == 0 ? modId : $"{modId}-{slug}";
    }

    /// <summary>
    /// Why a type cannot be given a screen, or null if it can. The object is edited in place,
    /// so it has to be a class the mod is holding a reference to; and a screen with nothing on
    /// it but raw JSON is not worth putting in the list.
    /// </summary>
    public static string? WhyNot(Type type)
    {
        if (type.IsValueType) return "it is a value type, so edits could never reach the mod's copy";
        if (type == typeof(string) || typeof(JsonObject).IsAssignableFrom(type) || typeof(Newtonsoft.Json.Linq.JToken).IsAssignableFrom(type))
            return "it is raw JSON, with no shape to build a screen from";
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) return "it is a collection rather than a settings class";
        if (type.IsAbstract) return "it is abstract";

        ConfigSchema schema = SchemaBuilder.For(type);
        bool editable = schema.Walk().Any(node => !node.Hidden && node.Kind is SchemaKind.Scalar or SchemaKind.Dictionary or SchemaKind.List);

        return editable ? null : "it has no setting ConfigKit can edit";
    }
}
