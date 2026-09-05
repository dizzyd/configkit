using System.Linq;
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

        // Its own command because null is the thing being looked at, and it has to be
        // distinguishable from 0, from "" and from false - which ordinary interpolation
        // renders identically to the empty string for all four.
        api.ChatCommands.Create("ckdemonulls")
            .WithDescription("Print the nullable settings, showing null as <null> rather than blank.")
            .RequiresPrivilege(Vintagestory.API.Server.Privilege.chat)
            .HandleWith(_ => Vintagestory.API.Common.TextCommandResult.Success(
                $"MaintenanceLimit={Show(Config.MaintenanceLimit)}  OptionalRatio={Show(Config.OptionalRatio)}\n"
                + $"OptionalCount={Show(Config.OptionalCount)}  OptionalToggle={Show(Config.OptionalToggle)}\n"
                + $"OptionalLevel={Show(Config.OptionalLevel)}  OptionalNote={Show(Config.OptionalNote)}\n"
                + $"Corner.Threshold={Show(Config.Corner.Threshold)}\n"
                + string.Join("\n", Config.Parts.Select(part =>
                    $"Parts[{part.Key}] Limit={Show(part.Value.Limit)} AvgLifeSpan={part.Value.AvgLifeSpanInYears}"))
                + $"\nRawWithNulls={Config.RawWithNulls.Count} entries: "
                + string.Join(" ", Config.RawWithNulls.Select(entry => $"{entry.Key}={entry.Value.ToString(Newtonsoft.Json.Formatting.None)}"))));
    }

    /// <summary>Null as something you can see, rather than as an empty string.</summary>
    private static string Show(object? value) => value?.ToString() ?? "<null>";
}
