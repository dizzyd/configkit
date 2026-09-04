using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Not a test - a scene builder for the documentation screenshots. Shaped like Dana Tweaks,
/// which is the canonical hand-rolled config in the published corpus.
/// </summary>
[SingleplayerOnly]
public class ShotsTest
{
    public class DoorRule
    {
        [Key]
        [Description("Which creature this rule is about.")]
        public string EntityCode = "";

        [Description("Chance per attempt that it manages the latch.")]
        [Range(0.0, 1.0)]
        public float Chance = 0.5f;

        [Description("Shuts the door behind it.")]
        public bool ClosesBehind = true;

        [Description("Doors it can work.")]
        [DataType("blockcode")]
        public List<string> Doors = new();
    }

    public class RainCollector
    {
        [Description("Collect rain at all.")]
        public bool Enabled = true;

        [Description("Litres gathered per hour of rain.")]
        [Range(0.1, 20.0)]
        public float LitresPerHour = 2.5f;
    }

    public class TweaksConfig
    {
        [Description("Turn every tweak off without uninstalling.")]
        public bool Enabled = true;

        [Description("Delay before an auto-closing door shuts, in milliseconds.")]
        [Category("Doors")]
        [DataType("blockcode")]
        public Dictionary<string, int> AutoCloseDelays = new()
        {
            ["game:door-*"] = 4000,
            ["game:door-oak"] = 2000,
            ["game:door-crude"] = 8000
        };

        [Description("Which creatures can work which doors.")]
        [Category("Doors")]
        [DataType("entitycode")]
        public Dictionary<string, DoorRule> CreaturesOpenDoors = new()
        {
            ["game:drifter-*"] = new DoorRule { EntityCode = "game:drifter-*", Chance = 0.35f, ClosesBehind = false },
            ["game:wolf-*"] = new DoorRule { EntityCode = "game:wolf-*", Chance = 0.8f },
            ["game:pig-wild-*"] = new DoorRule { EntityCode = "game:pig-wild-*", Chance = 0.15f }
        };

        [Description("Items per second each metal chute moves.")]
        [Category("Chutes")]
        public Dictionary<string, Dictionary<string, float>> ChuteFlowRates = new()
        {
            ["copper"] = new() { ["in"] = 1.0f, ["out"] = 2.0f },
            ["iron"] = new() { ["in"] = 1.5f, ["out"] = 3.0f }
        };

        [Description("Extra colours offered in the waypoint dialog.")]
        [Category("Waypoints, clientside")]
        public List<string> ExtraWaypointColors = new() { "#4FBFA8", "#C8553D" };

        [Description("Blocks that never become unstable when the soil under them goes.")]
        [Category("Soil")]
        [DataType("blockcode")]
        public List<string> EverySoilUnstableBlacklist = new() { "game:crock-burned", "game:bowl-fired" };

        [Description("Whether a scythe harvests a whole patch.")]
        [Category("Soil")]
        public bool ScytheHarvestsPatch = true;

        [Description("Fuel value of each block a bread oven will take.")]
        [Category("Ovens")]
        [DataType("blockcode")]
        public Dictionary<string, float> OvenFuelBlocks = new() { ["game:firewood"] = 1.0f };

        public RainCollector RainCollector = new();
    }

    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    public async Task TakeDocumentationShots()
    {
        await OnClient();

        string file = "configkit-shots.json";
        string path = Path.Combine(Capi.DataBasePath, "ModConfig", file);
        if (File.Exists(path)) File.Delete(path);

        TweaksConfig settings = new();
        Config config = new(Capi, "danatweaks", "Dana Tweaks", settings, file);
        config.AssignSettingsValues(settings);

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["danatweaks"] = config });
        dialog.TryOpen();
        await Frames.Wait(12);

        dialog.ToggleSectionNamed("Doors");
        await Frames.Wait(12);
        await Shot.Take("ck-1-sections");

        dialog.OpenSetting("AutoCloseDelays");
        await Frames.Wait(12);
        await Shot.Take("ck-2-codes");

        dialog.Back();
        dialog.OpenSetting("CreaturesOpenDoors");
        await Frames.Wait(12);
        await Shot.Take("ck-3-dictionary");

        dialog.OpenEntry("game:wolf-*");
        await Frames.Wait(12);
        await Shot.Take("ck-4-entry");

        dialog.Back();
        dialog.Back();
        dialog.OpenSetting("ChuteFlowRates");
        await Frames.Wait(12);
        dialog.OpenEntry("copper");
        await Frames.Wait(12);
        await Shot.Take("ck-5-nested");

        dialog.Back();
        dialog.Back();
        dialog.ToggleSectionNamed("Rain collector");
        await Frames.Wait(12);
        await Shot.Take("ck-6-nested-object");

        dialog.TryClose();
    }
}
