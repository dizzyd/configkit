using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ConfigKit;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// A disposed mod system is let go of by everything static it subscribed to.
///
/// ConfigRegistry.OnToBytes is static, and the mod system used to subscribe a lambda to it
/// that nothing could remove. The lambda held the mod system, the mod system held its api,
/// and the api held the whole server - so every process that started more than one server
/// kept every one of them. Issue #1 measured about 2 GB a server in a headless suite.
///
/// The session cannot be shut down and reopened from inside a test, so these drive a second,
/// throwaway mod system through the same load and dispose the real one goes through. It loads
/// into a registry of its own, so nothing it registers reaches the live one.
/// </summary>
public class LifecycleLeakTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    private static readonly Type _registryType = typeof(ConfigKitModSystem).Assembly.GetType("ConfigKit.ConfigRegistry", throwOnError: true)!;
    private static readonly Type _assetPatchType = typeof(ConfigKitModSystem).Assembly.GetType("ConfigKit.AssetPatch", throwOnError: true)!;

    /// <summary>Everything ConfigRegistry.OnToBytes would call right now.</summary>
    private static Delegate[] OnToBytesHandlers()
    {
        FieldInfo field = _registryType.GetField("OnToBytes", PrivateStatic)
            ?? throw new InvalidOperationException("ConfigRegistry.OnToBytes has no backing field - has it stopped being a field-like event?");

        return (field.GetValue(null) as Delegate)?.GetInvocationList() ?? [];
    }

    private static bool Subscribed(object system) => OnToBytesHandlers().Any(handler => ReferenceEquals(handler.Target, system));

    /// <summary>
    /// A mod system as far as the end of LoadConfigs, which is where it subscribes - without
    /// Start, which would register a second recipe registry and network channel.
    /// </summary>
    private static ConfigKitModSystem LoadedSystem()
    {
        ConfigKitModSystem system = new();
        system.StartPre(Sapi);

        typeof(ConfigKitModSystem).GetField("_registry", Private)!
            .SetValue(system, Activator.CreateInstance(_registryType, nonPublic: true));
        typeof(ConfigKitModSystem).GetMethod("LoadConfigs", Private)!
            .Invoke(system, null);

        return system;
    }

    /// <summary>
    /// Disposes a system without taking the live one's static state with it. Dispose forgets
    /// every asset baseline and drops every ConfigsChanged subscriber - right when the world
    /// is closing, wrong mid-session: the live system's next patch pass would take its own
    /// output for the original and compound, and other mods would stop hearing about changes.
    /// </summary>
    private static void DisposeKeepingStatics(ConfigKitModSystem system)
    {
        FieldInfo configsChanged = typeof(ConfigKitModSystem).GetField("ConfigsChanged", PrivateStatic)
            ?? throw new InvalidOperationException("ConfigKitModSystem.ConfigsChanged has no backing field - has it stopped being a field-like event?");
        object? subscribers = configsChanged.GetValue(null);

        object table = _assetPatchType.GetField("_baselines", PrivateStatic)!.GetValue(null)!;
        List<(object Asset, object Baseline)> kept = [];

        lock (table)
        {
            foreach (object entry in (IEnumerable)table)
            {
                Type pair = entry.GetType();
                kept.Add((pair.GetProperty("Key")!.GetValue(entry)!, pair.GetProperty("Value")!.GetValue(entry)!));
            }
        }

        try
        {
            system.Dispose();
        }
        finally
        {
            configsChanged.SetValue(null, subscribers);

            MethodInfo addOrUpdate = table.GetType().GetMethod("AddOrUpdate")!;
            lock (table)
            {
                foreach ((object asset, object baseline) in kept) addOrUpdate.Invoke(table, [asset, baseline]);
            }
        }
    }

    [VsTest(TimeoutMs = 60000)]
    public async Task DisposeUnsubscribesFromOnToBytes()
    {
        await OnServer();

        ConfigKitModSystem system = LoadedSystem();
        Assert.True(Subscribed(system), "a loaded mod system should be subscribed to OnToBytes - if this fails the test is no longer exercising anything");

        DisposeKeepingStatics(system);

        Assert.False(Subscribed(system), "a disposed mod system is still subscribed to the static OnToBytes, which keeps it and its server alive");
    }

    /// <summary>
    /// The stronger claim: once disposed and dropped, nothing anywhere still holds the mod
    /// system. Fails for any static reference, not just OnToBytes - which is the point.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ADisposedModSystemCanBeCollected()
    {
        await OnServer();

        WeakReference dropped = LoadAndDispose();

        for (int pass = 0; pass < 3 && dropped.IsAlive; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(dropped.IsAlive, "a disposed ConfigKitModSystem is still reachable after a full collection");
    }

    // Not inlined, so no local in the caller's frame keeps the system alive under the JIT.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndDispose()
    {
        ConfigKitModSystem system = LoadedSystem();
        DisposeKeepingStatics(system);
        return new WeakReference(system);
    }
}
