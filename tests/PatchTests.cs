using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Asset patching: the feature content mods actually use, and the largest part of this
/// library by volume. Everything here reads the resolved asset after load, which is what
/// the game itself will see.
///
/// The fixture mod ships tunables.json, tuned-a/b.json and untouched.json alongside its
/// patch definitions, so these assert against assets whose original values are known.
/// </summary>
public class PatchTests
{
    private static JsonObject Asset(string path)
    {
        IAsset? asset = Capi.Assets.TryGet(new AssetLocation(path));
        Assert.NotNull(asset);
        return new JsonObject(Newtonsoft.Json.Linq.JToken.Parse(asset!.ToText()));
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ANumberPatchWritesTheSettingIntoTheAsset()
    {
        await OnClient();

        // nested/radius <- patchNumber (21), over an original value of 3.
        Assert.Equal(21, Asset("demomod:config/tunables.json")["nested"]["radius"].AsInt());
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ABooleanPatchWritesTheSettingIntoTheAsset()
    {
        await OnClient();

        // flag <- patchFlag (true), over an original value of false.
        Assert.True(Asset("demomod:config/tunables.json")["flag"].AsBool(),
            "boolean patch did not reach the asset");
    }

    /// <summary>
    /// The compounding bug, as a standing test. "value * 2" is written against the asset's
    /// own number, and a client applies patches twice - once over its local configs, again
    /// when the server's arrive. Without the pristine-bytes snapshot this reads 40.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task APatchRelativeToTheAssetsOwnValueDoesNotCompound()
    {
        await OnClient();

        int damage = Asset("demomod:config/tunables.json")["damage"].AsInt();

        Assert.Equal(20, damage);   // 10 * 2, once
    }

    /// <summary>
    /// A server owner patching an asset that ConfigKit also patches. The json patch loader
    /// runs at ExecuteOrder 0.05, after ConfigKit at 0.01, so on a client the second pass -
    /// the one over the server's configs - used to restore bytes snapshotted before the
    /// patch existed and revert it with nothing said in the log. Reported against the
    /// Skaven/Rat player model mod, whose config patches the same file PlayerModelLib reads
    /// a model's group from.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AJsonPatchToAPatchedAssetSurvivesConfigKit()
    {
        await OnClient();

        JsonObject asset = Asset("demomod:config/foreign.json");

        Assert.Equal("from-a-json-patch", asset["serverOwned"].AsString(""));
        Assert.Equal(21, asset["value"].AsInt());   // and ConfigKit's own patch still lands
    }

    /// <summary>
    /// The two halves of the above together. Keeping the other writer's whole file as the
    /// baseline would feed ConfigKit's own result back into "value * 2" and read 20; taking
    /// only the paths ConfigKit owns back to what they held before it first wrote reads 10.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task APatchRelativeToTheAssetsOwnValueDoesNotCompoundOnAJsonPatchedAsset()
    {
        await OnClient();

        Assert.Equal(10, Asset("demomod:config/foreign.json")["relative"].AsInt());   // 5 * 2, once
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AWildcardPatchHitsEveryMatchingAsset()
    {
        await OnClient();

        Assert.Equal(21, Asset("demomod:config/tuned-a.json")["value"].AsInt());
        Assert.Equal(21, Asset("demomod:config/tuned-b.json")["value"].AsInt());
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AWildcardPatchLeavesNonMatchingAssetsAlone()
    {
        await OnClient();

        // untouched.json sits in the same folder and does not match "tuned-*".
        Assert.Equal(1, Asset("demomod:config/untouched.json")["value"].AsInt());
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task PatchingLeavesTheRestOfTheAssetIntact()
    {
        await OnClient();

        JsonObject asset = Asset("demomod:config/tunables.json");

        Assert.Equal("leave me alone", asset["untouchedKey"].AsString(""));
        Assert.True(asset["nested"].KeyExists("radius"), "patching flattened a nested object");
    }

    /// <summary>
    /// Server-side categories are refused on a client, because the server owns them and
    /// syncs the result. Patching them locally would desync the two.
    /// </summary>
    [SingleplayerOnly]
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task TheServerSideOfPatchingIsWhereBlockAndItemChangesHappen()
    {
        await OnServer();

        ConfigKitModSystem system = Sapi.ModLoader.GetModSystem<ConfigKitModSystem>();
        Assert.True(system.Domains.Contains("demomod"), "fixture config missing on the server");
    }
}
