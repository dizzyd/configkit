using ConfigKit;
using Vintagestory.API.Common;

namespace ConfigKitDemo;

/// <summary>
/// The whole integration, for something with one of every shape in it. There is no draw
/// callback, no ControlButtons, and no widget ids - the settings class is the description of
/// the screen.
/// </summary>
public class ConfigKitDemoSystem : ModSystem
{
    public static DemoConfig Config { get; private set; } = new();

    public override void StartPre(ICoreAPI api)
    {
        ConfigKitModSystem? configkit = api.ModLoader.GetModSystem<ConfigKitModSystem>();

        if (configkit == null)
        {
            api.Logger.Warning("[configkitdemo] ConfigKit is not loaded, so there is nothing to demonstrate.");
            return;
        }

        configkit.RegisterManagedConfig(
            "configkitdemo",
            Config,
            "configkitdemo.json",
            onSettingChanged: code => api.Logger.Notification($"[configkitdemo] {code} changed"));
    }

    public override void StartServerSide(Vintagestory.API.Server.ICoreServerAPI api)
    {
        // Somewhere to see that edits actually reach the object, rather than only the file.
        api.ChatCommands.Create("ckdemo")
            .WithDescription("Print what the demo config currently holds.")
            .RequiresPrivilege(Vintagestory.API.Server.Privilege.chat)
            .HandleWith(_ => Vintagestory.API.Common.TextCommandResult.Success(
                $"Enabled={Config.Enabled} Level={Config.Level} OpenFaces={Config.OpenFaces}\n"
                + $"SearchRadius={Config.SearchRadius} SpeedMultiplier={Config.SpeedMultiplier}\n"
                + $"RainCollector.LitresPerHour={Config.RainCollector.LitresPerHour} "
                + $"RainCollector.Overflow.SpillAtPercent={Config.RainCollector.Overflow.SpillAtPercent}\n"
                + $"AutoCloseDelays={Config.AutoCloseDelays.Count} entries, "
                + $"CreaturesOpenDoors={Config.CreaturesOpenDoors.Count}, "
                + $"ChuteFlowRates={Config.ChuteFlowRates.Count}\n"
                + $"LootPool={Config.LootPool.Count}, FilledInPlace=[{string.Join(", ", Config.FilledInPlace)}], "
                + $"ApexCodes=[{string.Join(", ", Config.ApexCodes)}]"));
    }
}
