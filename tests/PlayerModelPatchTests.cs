using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// The reported case, against the real mods instead of a fixture.
///
/// The Skaven/Rat player model mod declares a ConfigKit config whose patches target
/// config/customplayermodels/skaven.json - the same file PlayerModelLib reads a model's
/// group from for the character selection screen. A server owner moving that group with
/// an ordinary json patch found it silently reverted: the json patch loader runs at
/// ExecuteOrder 0.05, after ConfigKit at 0.01, so ConfigKit's second pass over the file -
/// the one that runs when the server's configs arrive - restored bytes snapshotted before
/// the patch existed.
///
/// Needs a mods directory carrying playermodellib, the Skaven mod and a patch mod that
/// does the move; see docs/TESTING-WITH-REAL-MODS.md. Skipped, with the reason, otherwise.
/// </summary>
public class PlayerModelPatchTests
{
    private const string Domain = "vintageskavenrat";
    private const string ModelConfig = "vintageskavenrat:config/customplayermodels/skaven.json";
    private const string PatchedGroup = "beast";

    private static JsonObject SkavenModelConfig()
    {
        IAsset? asset = Capi.Assets.TryGet(new AssetLocation(ModelConfig));

        if (asset == null) Skip($"'{ModelConfig}' is not loaded - the Skaven pack is not in this run's mods");

        return new JsonObject(JToken.Parse(asset!.ToText()));
    }

    /// <summary>
    /// The patch is only interesting if ConfigKit is patching the same file. If it has stood
    /// down, or the mod's config went unclaimed, everything below would pass for the wrong
    /// reason.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task ConfigKitIsManagingTheSkavenConfig()
    {
        await OnClient();

        SkavenModelConfig();

        ConfigKitModSystem system = Capi.ModLoader.GetModSystem<ConfigKitModSystem>();

        Assert.True(system.Domains.Contains(Domain),
            $"ConfigKit is not managing '{Domain}', so the rest of this file proves nothing. Domains: {string.Join(", ", system.Domains)}");
    }

    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task AServerJsonPatchToTheSkavenGroupSurvivesConfigKit()
    {
        await OnClient();

        Assert.Equal(PatchedGroup, SkavenModelConfig()["skaven"]["Group"].AsString(""));
    }

    /// <summary>
    /// End to end: not just the asset bytes, but what PlayerModelLib built out of them at
    /// AssetsFinalize, which is what the character selection screen groups by.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    public async Task PlayerModelLibReadsTheServerPatchedGroup()
    {
        await OnClient();

        SkavenModelConfig();

        ModSystem? models = Capi.ModLoader.GetModSystem("PlayerModelLib.CustomModelsSystem");

        if (models == null) Skip("PlayerModelLib is not loaded");

        object? loaded = models!.GetType().GetProperty("CustomModels")?.GetValue(models);

        Assert.NotNull(loaded);

        string groups = "";
        string? skavenGroup = null;

        foreach (DictionaryEntry entry in (IDictionary)loaded!)
        {
            string code = (string)entry.Key;
            string? group = entry.Value?.GetType().GetProperty("Group")?.GetValue(entry.Value) as string;

            groups += $"{code}={group} ";

            if (code.Contains("skaven")) skavenGroup = group;
        }

        Assert.NotNull(skavenGroup, $"PlayerModelLib loaded no model whose code names skaven. Loaded: {groups}");
        Assert.Equal(PatchedGroup, skavenGroup!, $"the group PlayerModelLib built for the skaven model. Loaded: {groups}");
    }
}
