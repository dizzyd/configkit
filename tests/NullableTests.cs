using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Nullable value types, where null is a value the author means rather than the absence of
/// one.
///
/// The case that forced this is WearAndTear's, reported by its author:
///
///     [Range(0d, double.PositiveInfinity)]
///     public float? MaintenanceLimit { get; set; }
///
/// null is "no limit" and 0 is "no repair allowed" - opposite meanings. ConfigKit unwrapped
/// the nullable during classification, so the setting was a plain float, AsFloat turned the
/// stored null into 0, and the screen showed the player the reverse of what their file said.
/// There was also no way to type null back, so the first edit was one-way.
///
/// Checked against the real thing: with WearAndTear loaded, Clutch/MaintenanceLimit read
/// "type = Float, value = null, asFloat = 0".
/// </summary>
public class NullableTests
{
    public class Limits
    {
        /// <summary>WearAndTear's shape exactly: an open lower bound and a meaningful null.</summary>
        [Range(0d, double.PositiveInfinity)]
        public float? MaintenanceLimit { get; set; }

        /// <summary>A closed range, which would be a slider were it not nullable.</summary>
        [Range(0d, 1d)]
        public float? Ratio { get; set; } = 0.5f;

        public int? Count { get; set; }

        public bool? Toggle { get; set; }

        /// <summary>Not nullable, and must keep behaving exactly as it did.</summary>
        [Range(0d, 1d)]
        public float Plain { get; set; } = 0.25f;
    }

    /// <summary>
    /// A stored null is shown as nothing at all. Zero is a different value and, for the
    /// member this came from, the opposite one.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task ANullIsShownAsEmptyRatherThanZero()
    {
        await OnClient();

        Config config = new(Capi, "cknull", "Nullable", new Limits(), "ck-null.json");

        ConfigSetting limit = (ConfigSetting)config.GetSetting("MaintenanceLimit")!;
        Assert.True(limit.Nullable, "the nullable was unwrapped and lost");
        Assert.True(limit.IsNull, "an unset float? should start null");

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["cknull"] = config });
        dialog.TryOpen();
        await Frames.Wait(8);

        try
        {
            // A slider has no position for "unset", so a nullable number takes the input.
            Assert.Equal("GuiElementNumberInput", dialog.ControlKindFor("MaintenanceLimit"));

            // Even where the range would otherwise earn a slider.
            Assert.Equal("GuiElementNumberInput", dialog.ControlKindFor("Ratio"));

            // And a plain float still gets one - this is the guard that stops the change
            // from quietly turning every slider in the library into a text box.
            Assert.Equal("GuiElementSlider", dialog.ControlKindFor("Plain"));

            Assert.Equal("", dialog.NumberTextFor("MaintenanceLimit"));
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// And null can be put back. Without this the first edit is one-way: a player who nudges
    /// "no limit" to 5 can never return it, and no amount of care with the file helps them.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task ClearingTheBoxPutsTheNullBack()
    {
        await OnClient();

        Limits settings = new();
        Config config = new(Capi, "cknull2", "Nullable", settings, "ck-null2.json");

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["cknull2"] = config });
        dialog.TryOpen();
        await Frames.Wait(8);

        try
        {
            dialog.TypeInto("MaintenanceLimit", "5");
            Assert.Equal(5f, settings.MaintenanceLimit);

            dialog.TypeInto("MaintenanceLimit", "");
            Assert.Null(settings.MaintenanceLimit);
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// A bool? has three states and a switch has two, so it becomes a dropdown. Left as a
    /// switch, null read as false - which is a value, and for a flag guarding behaviour it
    /// is the value that turns the behaviour off.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task ANullableBooleanHasThreeStates()
    {
        await OnClient();

        Config config = new(Capi, "cknull3", "Nullable", new Limits(), "ck-null3.json");

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["cknull3"] = config });
        dialog.TryOpen();
        await Frames.Wait(8);

        try
        {
            Assert.Equal("GuiElementDropDown", dialog.ControlKindFor("Toggle"));
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// Null survives the file. It did before this change - WriteToFile writes the token, not
    /// AsFloat - and it has to keep doing so, because that round trip is the only reason an
    /// untouched config was not already corrupted.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ANullSurvivesWritingAndReadingBack()
    {
        await OnServer();

        Limits settings = new();
        Config config = new(Capi, "cknull4", "Nullable", settings, "ck-null4.json");

        config.WriteToFile();
        Assert.True(config.ReadFromFile(), "would not read back what it wrote");

        Assert.True(((ConfigSetting)config.GetSetting("MaintenanceLimit")!).IsNull,
            "the null came back as something else");

        Limits loaded = new() { MaintenanceLimit = 99f, Count = 7 };
        config.AssignSettingsValues(loaded);

        Assert.Null(loaded.MaintenanceLimit);
        Assert.Null(loaded.Count);
    }

    /// <summary>
    /// Setting a value and clearing it again leaves the object exactly where it started,
    /// through the whole chain rather than only in the setting.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AValueAndThenANullBothReachTheObject()
    {
        await OnServer();

        Limits settings = new();
        Config config = new(Capi, "cknull5", "Nullable", settings, "ck-null5.json");

        config.GetSetting("Count")!.Value = new JsonObject(new JValue(12));
        Assert.Equal(12, settings.Count);

        config.GetSetting("Count")!.Value = new JsonObject(JValue.CreateNull());
        Assert.Null(settings.Count);

        // And the file says null too, rather than 0.
        config.WriteToFile();
        Assert.True(config.ReadFromFile(), "would not read back what it wrote");
        Assert.True(((ConfigSetting)config.GetSetting("Count")!).IsNull, "null became something else on disk");
    }
}
