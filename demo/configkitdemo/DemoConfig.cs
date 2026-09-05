using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ConfigKitDemo;

// One of every shape ConfigKit can render, arranged so each section of the settings screen
// demonstrates one idea. Nothing in this file references ConfigKit: these are stock .NET
// attributes, and this class compiles and runs with ConfigKit not installed.
//
// The sections are numbered so they read top to bottom in the window.

public enum Difficulty { Gentle, Normal, Brutal }

[Flags]
public enum Sides { None = 0, North = 1, South = 2, East = 4, West = 8 }

public class DemoConfig
{
    // ---------------------------------------------------------------- always visible

    // No [Category], so these sit above the first heading and are never folded away.

    [Description("Turn the whole demo off without uninstalling it.")]
    public bool Enabled = true;

    [Description("An enum. A dropdown of its member names, stored by name.")]
    public Difficulty Level = Difficulty.Normal;

    // ---------------------------------------------------------------- 1. controls

    [Category("1 Controls")]
    [Description("An int with a range, so a slider rather than a text box.")]
    [Range(4, 40)]
    public int SearchRadius = 12;

    [Category("1 Controls")]
    [Description("A float with a range. The readout beside it shows the real value.")]
    [Range(0.5, 4.0)]
    public float SpeedMultiplier = 1.5f;

    [Category("1 Controls")]
    [Description("An int with no range, so a plain number input.")]
    public int HardLimit = 250;

    [Category("1 Controls")]
    [Description("A string.")]
    public string LabelText = "gravestone";

    [Category("1 Controls")]
    [Description("A fixed set of choices becomes a dropdown.")]
    [AllowedValues(1, 2, 4, 8)]
    public int Step = 2;

    [Category("1 Controls")]
    [Description("A [Flags] enum. Its value is a combination, so it is stored by its combined name.")]
    public Sides OpenFaces = Sides.North | Sides.South;

    // ---------------------------------------------------------------- 2. labels and order

    [Category("2 Labels")]
    [Display(Name = "This label was set by hand", Order = 1)]
    [Description("[Display(Name)] overrides the tidied-up field name. It works on a field; [DisplayName] does not.")]
    public int Renamed = 1;

    [Category("2 Labels")]
    [Display(Order = 0)]
    [Description("Declared second, drawn first, because [Display(Order = 0)] says so.")]
    public int MovedToTheTop = 2;

    [Category("2 Labels")]
    [Description("No attribute at all. The field name is tidied up into a label.")]
    public int PlainOldFieldName = 3;

    // ---------------------------------------------------------------- 3. not editable

    [Category("3 Not editable")]
    [ReadOnly(true)]
    [Description("[ReadOnly(true)]. Shown, saved, but not editable.")]
    public int DeclaredReadOnly = 7;

    [Category("3 Not editable")]
    [Description("A readonly field. Nothing could assign it, so a control for it would be theatre.")]
    public readonly int CannotBeAssigned = 8;

    [Category("3 Not editable")]
    [Browsable(false)]
    [Description("[Browsable(false)]. Not on screen at all - but look for it in the config file, still there.")]
    public int HiddenButSaved = 9;

    [Category("3 Not editable")]
    [JsonIgnore]
    [Description("[JsonIgnore]. Not on screen and not in the file either.")]
    public int NotPersistedAtAll = 10;

    // A member ConfigKit cannot store at all: no way to construct it. It gets a line on the
    // screen saying so rather than vanishing, which is the rule the whole library turns on.
    [Category("3 Not editable")]
    public Unbuildable? Impossible;

    // ---------------------------------------------------------------- 4. a nested class

    [Description("A class becomes a section of its own, and each of its fields an ordinary row.")]
    public RainCollector RainCollector = new();

    // ---------------------------------------------------------------- 5. dictionaries

    [Category("5 Dictionaries")]
    [Description("The commonest structured setting there is. [DataType] flags a key that names nothing.")]
    [DataType("blockcode")]
    public Dictionary<string, int> AutoCloseDelays = new()
    {
        ["game:door-*"] = 4000,           // a wildcard: valid, matches many
        ["game:door-crude"] = 8000,       // a real block
        ["game:door-oak"] = 2000          // looks real, is not - watch for the mark
    };

    [Category("5 Dictionaries")]
    [Description("Values are a class, so each entry opens a screen of its own.")]
    [DataType("entitycode")]
    public Dictionary<string, DoorRule> CreaturesOpenDoors = new()
    {
        ["game:drifter-*"] = new DoorRule { EntityCode = "game:drifter-*", Chance = 0.35f, ClosesBehind = false },
        ["game:wolf-*"] = new DoorRule { EntityCode = "game:wolf-*", Chance = 0.8f }
    };

    [Category("5 Dictionaries")]
    [Description("A dictionary of dictionaries. The second level is the first level again.")]
    public Dictionary<string, Dictionary<string, float>> ChuteFlowRates = new()
    {
        ["copper"] = new() { ["in"] = 1.0f, ["out"] = 2.0f },
        ["iron"] = new() { ["in"] = 1.5f, ["out"] = 3.0f }
    };

    [Category("5 Dictionaries")]
    [Description("An enum for a key. Add can run out of keys - try adding a fourth.")]
    public Dictionary<Difficulty, float> PerDifficulty = new()
    {
        [Difficulty.Gentle] = 0.5f,
        [Difficulty.Normal] = 1.0f
    };

    [Category("5 Dictionaries")]
    [Description("An AssetLocation for a key. Stored as its string form by its TypeConverter.")]
    public Dictionary<AssetLocation, float> SatietyByItem = new()
    {
        [new AssetLocation("game:bread-spelt")] = 0.8f
    };

    // ---------------------------------------------------------------- 6. lists

    [Category("6 Lists")]
    [Description("A list of strings, with a hint about what belongs in it.")]
    [DataType("blockcode")]
    public List<string> SoilBlacklist = new() { "game:crock-burned" };

    [Category("6 Lists")]
    [Description("A list of classes. Each row is labelled by the field marked [Key].")]
    public List<LootEntry> LootPool = new()
    {
        new LootEntry { ItemCode = "game:nugget-gold", Quantity = 3 },
        new LootEntry { ItemCode = "game:nugget-silver", Quantity = 5 }
    };

    [Category("6 Lists")]
    [Description("An array.")]
    public string[] ApexCodes = ["game:bear-*", "game:wolf-*"];

    [Category("6 Lists")]
    [Description("A set. No duplicates, and no way to add one.")]
    public HashSet<string> RequiredItems = new() { "game:compass" };

    [Category("6 Lists")]
    [Description("A get-only collection. Plain assignment cannot reach it, so it is filled in place.")]
    public List<string> FilledInPlace { get; } = new() { "one", "two" };

    // ---------------------------------------------------------------- 7. the awkward ones

    [Category("7 Awkward")]
    [JsonProperty("storedUnderThisName")]
    [Description("The file writes 'storedUnderThisName'. The field name still reads, so an old file loads.")]
    public bool RenamedInTheFile = true;

    [Category("7 Awkward")]
    [Description("Schemaless. Survives untouched, which is how a mod migrates an old config.")]
    public Dictionary<string, JToken> LegacyData = new()
    {
        ["oldSetting"] = JToken.Parse("{\"value\":3,\"unit\":\"per-hour\"}")
    };

    [Category("7 Awkward")]
    [Description("A double, a long and a decimal all work; the model has one float and one integer type.")]
    public double Ratio = 0.75;

    [Category("7 Awkward")]
    public long BigNumber = 5_000_000_000L;

    // ---------------------------------------------------------------- 9. nulls

    // null is a value here, not an absence, and telling the two apart is the whole point.
    // The shape that forced this is WearAndTear's: a nullable float whose null means "no
    // limit" while 0 means "none allowed" - opposite things. Before ConfigKit kept
    // nullability, null was shown as 0, which stated the reverse of what the file said, and
    // could never be typed back.
    //
    // Try: clear a box to put null back, and run /ckdemonulls to see what your object holds.

    [Category("9 Nulls")]
    [Description("WearAndTear's shape. Unset means no limit; 0 would mean nothing may be repaired. Starts null - clear the box to get it back.")]
    [Range(0.0, double.PositiveInfinity)]
    public float? MaintenanceLimit { get; set; }

    [Category("9 Nulls")]
    [Description("A closed range, which would be a slider were it not nullable. A slider has no position for unset, so this is a number input too.")]
    [Range(0.0, 1.0)]
    public float? OptionalRatio { get; set; } = 0.5f;

    [Category("9 Nulls")]
    [Description("A nullable int, starting null.")]
    public int? OptionalCount { get; set; }

    [Category("9 Nulls")]
    [Description("A nullable bool has three states, so it is a dropdown rather than a switch. Left as a switch, null read as false.")]
    public bool? OptionalToggle { get; set; }

    [Category("9 Nulls")]
    [Description("A nullable enum. Its dropdown gains an (unset) entry.")]
    public Difficulty? OptionalLevel { get; set; }

    [Category("9 Nulls")]
    [Description("A nullable string. Null and the empty string are different, and both survive the file.")]
    public string? OptionalNote { get; set; }

    [Category("9 Nulls")]
    [Description("A class holding a null, so the null lives inside a subtree rather than at the top.")]
    public NullableCorner Corner { get; set; } = new();

    [Category("9 Nulls")]
    [Description("The real shape: a dictionary of classes, each with a nullable inside. Drill in - and note that editing one has to serialise a tree containing a null.")]
    public Dictionary<string, PartLimits> Parts { get; set; } = new()
    {
        ["clutch"] = new PartLimits { Code = "wearandtear:clutch" },
        ["windmill"] = new PartLimits { Code = "wearandtear:windmillsails", Limit = 0.75f },
    };

    [Category("9 Nulls")]
    [Description("Raw JSON with an explicit null in it. This is the one that could not be turned into a game attribute tree at all: the format has no null, and writing one threw.")]
    public Dictionary<string, JToken> RawWithNulls { get; set; } = new()
    {
        ["clutch"] = JToken.Parse(@"{""Code"":""wearandtear:clutch"",""MaintenanceLimit"":null,""AvgLifeSpanInYears"":3.0}"),
    };

    // ---------------------------------------------------------------- 8. client side

    // Only these are the player's own. Everything else above is server truth, and on a
    // server you do not control they are shown read-only. Because sub-objects flatten, this
    // works per field rather than per class.

    [Category("8 Yours, not the server's, clientside")]
    [Description("A client-side setting. Yours even on someone else's server.")]
    public string MarkerColour = "#4FBFA8";

    [Category("8 Yours, not the server's, clientside")]
    [Description("Another one, so the section has company.")]
    public bool ShowOverlay = true;
}

public class RainCollector
{
    [Description("Collect rain at all.")]
    public bool Enabled = true;

    [Description("Litres gathered per hour of rain.")]
    [Range(0.1, 20.0)]
    public float LitresPerHour = 2.5f;

    [Description("A class inside a class. Its heading carries both names.")]
    public Overflow Overflow = new();
}

public class Overflow
{
    [Description("What to do when it is full.")]
    public bool SpillOver = true;

    [Range(0, 100)]
    public int SpillAtPercent = 90;
}

public class DoorRule
{
    [Key]
    [Description("Which creature this rule is about. [Key] makes it the row label.")]
    public string EntityCode = "";

    [Description("Chance per attempt that it manages the latch.")]
    [Range(0.0, 1.0)]
    public float Chance = 0.5f;

    [Description("Shuts the door behind it.")]
    public bool ClosesBehind = true;

    [Description("Doors it can work. A list inside a dictionary entry.")]
    [DataType("blockcode")]
    public List<string> Doors = new();
}

public class NullableCorner
{
    [Description("An ordinary field, for contrast.")]
    public bool Enabled = true;

    [Description("A nullable one level down. Its null has to survive the nesting.")]
    [Range(0.0, double.PositiveInfinity)]
    public float? Threshold { get; set; }
}

public class PartLimits
{
    [Key]
    [Description("Which part this is about.")]
    public string Code { get; set; } = "";

    [Description("Unset means no limit. This is the member the whole section exists for.")]
    [Range(0.0, double.PositiveInfinity)]
    public float? Limit { get; set; }

    [Description("An ordinary value beside it, so a null is visibly different from a zero.")]
    [Range(0.0, 100.0)]
    public float AvgLifeSpanInYears { get; set; } = 3f;
}

public class LootEntry
{
    [Key]
    public string ItemCode = "";

    [Range(1, 64)]
    public int Quantity = 1;
}

/// <summary>Abstract, so nothing can construct one. There is no editor for this, by definition.</summary>
public abstract class Unbuildable
{
    public int Value;
}
