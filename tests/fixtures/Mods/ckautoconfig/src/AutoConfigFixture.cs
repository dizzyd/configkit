using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CkAutoConfig;

public class Settings
{
    public bool Enabled = true;
    public int Radius = 8;
    public float Rate = 1.5f;
}

/// <summary>
/// What a mod with no settings screen looks like: it reads and writes its config, and that
/// is all. Nothing here runs by itself - the tests call it, so the fixture changes nothing
/// about a session it is merely loaded into.
/// </summary>
public static class Loader
{
    public static Settings Load(ICoreAPI api, string file) => api.LoadModConfig<Settings>(file);

    public static void Save(ICoreAPI api, Settings settings, string file) => api.StoreModConfig(settings, file);

    /// <summary>The untyped overload. There is no class to build a screen from.</summary>
    public static JsonObject Raw(ICoreAPI api, string file) => api.LoadModConfig(file);

    /// <summary>A generic helper. T says nothing about the config, and Harmony cannot patch it.</summary>
    public static T Generic<T>(ICoreAPI api, string file) => api.LoadModConfig<T>(file);
}

public class CkAutoConfigSystem : ModSystem
{
}
