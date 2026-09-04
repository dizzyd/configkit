using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Nested objects, and the compatibility rules that constrain them.
///
/// The change these cover is additive by design: a config file written before it must keep
/// loading, with every value it holds intact and every newly-visible member at its default.
/// Several of these tests exist only to fail loudly if a later tidy-up breaks that.
///
/// All of it is server-side model work with no GUI, so it runs headless - and only in a
/// single-process session, where this test host owns the server.
/// </summary>
[SingleplayerOnly]
public class SchemaTests
{
    // ---------------------------------------------------------------- fixtures

    public class RainCollector
    {
        [Description("Collect rain at all.")]
        public bool Enabled = true;

        [Description("Litres gathered per hour of rain.")]
        [Range(0.1, 20.0)]
        public float LitresPerHour = 2.5f;
    }

    public class Thirst
    {
        public float HungerRate = 1.5f;
        public int MaxThirst = 1500;
    }

    public class NestedSettings
    {
        [Description("Turn every tweak off without uninstalling.")]
        public bool Enabled = true;

        [Category("Doors")]
        public int AutoCloseDelay = 4000;

        public RainCollector RainCollector = new();

        public Thirst Thirst = new();
    }

    public class ReadOnlySettings
    {
        public int Editable = 1;

        [ReadOnly(true)]
        public int Declared = 2;

        /// <summary>Nothing can assign this, so a control for it would be theatre.</summary>
        public readonly int Fixed = 3;

        /// <summary>A get-only collection is a different case: it is filled in place.</summary>
        public List<string> Fillable { get; } = new();
    }

    public class Doors { public bool Enabled = true; }
    public class Chutes { public bool Enabled = true; }

    /// <summary>Two sub-objects with the same field name - one translation key each, not one shared.</summary>
    public class Colliding
    {
        public Doors Doors = new();
        public Chutes Chutes = new();
    }

    /// <summary>Every remaining row of the documented attribute table, in one class.</summary>
    public class TableSettings
    {
        /// <summary>
        /// [DisplayName] cannot go on a field - its AttributeUsage allows class, method,
        /// property, indexer and event only - so on a property it is, and fields use
        /// [Display(Name)] instead.
        /// </summary>
        [DisplayName("How far it looks")]
        public int SearchRadius { get; set; } = 12;

        [Display(Name = "Speed multiplier", Order = 0)]
        public float Speed = 1.5f;

        [AllowedValues(1, 2, 4, 8)]
        public int Step = 2;

        [Description("What it says on the label.")]
        public bool Enabled = true;
    }

    public class TaggedSettings
    {
        [Category("Waypoints, clientside")]
        public string Colour = "red";

        [Category("Doors")]
        public int Delay = 10;
    }

    public class ExclusionSettings
    {
        public int Visible = 1;

        [Browsable(false)]
        public int HiddenButStored = 2;

        [JsonIgnore]
        public int NotPersisted = 3;
    }

    public class RenamedSettings
    {
        [JsonProperty("closesBehind")]
        public bool ClosesBehind = true;

        public int Untouched = 7;
    }

    public abstract class Unbuildable { public int Value; }

    /// <summary>A member with no way to construct it: nothing can store or edit this.</summary>
    public class HasDeadMember
    {
        public int Editable = 1;
        public Unbuildable Broken = null!;
    }

    /// <summary>
    /// Two mods in the published corpus hold a member of their own declaring type - a handle
    /// back to the config rather than config data. Walking it is an infinite recursion.
    /// </summary>
    public class SelfReferential
    {
        public int Depth = 1;
        public SelfReferential? Config;
    }

    public class ContainerSettings
    {
        public int Scalar = 1;
        public Dictionary<string, float> Weights = new() { ["a"] = 1f };
        public List<string> Names = new() { "one" };
    }

    // ---------------------------------------------------------------- helpers

    private static Config Fresh(object settings, string domain, string file)
    {
        Delete(file);
        return new Config(Sapi, domain, domain, settings, file);
    }

    private static string PathOf(string file) => Path.Combine(Sapi.DataBasePath, "ModConfig", file);

    private static void Delete(string file)
    {
        string path = PathOf(file);
        if (File.Exists(path)) File.Delete(path);
    }

    private static JObject FileJson(Config config) => JObject.Parse(File.ReadAllText(config.ConfigFilePath));

    // ---------------------------------------------------------------- compatibility

    /// <summary>
    /// Compatibility rule 1. A scalar's code is the member's own name, verbatim - not
    /// camel-cased, not normalised. Every value in every existing config file is filed under
    /// that name, so changing how it is generated silently resets everyone's settings with
    /// nothing in the log to say why.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ScalarCodesAreExactlyTheMemberName()
    {
        await OnServer();

        Config config = Fresh(new NestedSettings(), "schema-codes", "configkit-schema-codes.json");

        Assert.True(config.SettingCodes.Contains("Enabled"),
            $"expected a setting coded 'Enabled'; got {string.Join(", ", config.SettingCodes)}");
        Assert.True(config.SettingCodes.Contains("AutoCloseDelay"));

        Assert.True(FileJson(config).ContainsKey("Enabled"), "'Enabled' is not a top level key in the file");
    }

    /// <summary>
    /// Compatibility rule 4. A file written before nested members were visible must keep every
    /// value it holds, and pick up the new members at their defaults rather than resetting.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AnOldFileLoadsWithNewMembersAtTheirDefaults()
    {
        await OnServer();

        const string file = "configkit-schema-oldfile.json";
        Delete(file);

        // Exactly what the previous version of ConfigKit would have written: the scalars it
        // could classify, and nothing else.
        File.WriteAllText(PathOf(file), "{\n  \"Enabled\": false,\n  \"AutoCloseDelay\": 9999\n}\n");

        NestedSettings settings = new();
        Config config = new(Sapi, "schema-oldfile", "schema-oldfile", settings, file);
        config.AssignSettingsValues(settings);

        Assert.False(settings.Enabled, "a value already in the file was lost");
        Assert.Equal(9999, settings.AutoCloseDelay);

        // Newly visible, so at its compiled-in default rather than reset to zero.
        Assert.Close(2.5f, settings.RainCollector.LitresPerHour, 0.001f);
        Assert.Close(1.5f, settings.Thirst.HungerRate, 0.001f);
    }

    /// <summary>
    /// Compatibility rule 3. [JsonProperty] renames the key a member is written under, which
    /// is what lets a mod migrating off LoadModConfig keep its players' files - but a mod
    /// already on ConfigKit has the member name in its files, so the old name has to be read.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ARenamedMemberIsWrittenUnderItsNewNameAndStillReadsTheOld()
    {
        await OnServer();

        const string file = "configkit-schema-renamed.json";
        Delete(file);

        // A file from before [JsonProperty] was honoured: the member name, not the json name.
        File.WriteAllText(PathOf(file), "{\n  \"ClosesBehind\": false,\n  \"Untouched\": 42\n}\n");

        RenamedSettings settings = new();
        Config config = new(Sapi, "schema-renamed", "schema-renamed", settings, file);
        config.AssignSettingsValues(settings);

        Assert.False(settings.ClosesBehind, "the value stored under the old member name was not read back");
        Assert.Equal(42, settings.Untouched);

        Assert.True(config.SettingCodes.Contains("closesBehind"),
            $"expected the [JsonProperty] name as the code; got {string.Join(", ", config.SettingCodes)}");

        config.WriteToFile();
        JObject written = FileJson(config);
        Assert.True(written.ContainsKey("closesBehind"), "the new name was not written");
        Assert.False(written["closesBehind"]!.Value<bool>(), "the value did not survive the rename");
    }

    // ---------------------------------------------------------------- nesting

    [VsTest(TimeoutMs = 60000)]
    public async Task NestedObjectsBecomePathCodedSettings()
    {
        await OnServer();

        Config config = Fresh(new NestedSettings(), "schema-nested", "configkit-schema-nested.json");

        Assert.True(config.SettingCodes.Contains("RainCollector/LitresPerHour"),
            $"nested member missing; got {string.Join(", ", config.SettingCodes)}");
        Assert.True(config.SettingCodes.Contains("Thirst/MaxThirst"));

        Assert.Close(2.5f, config.GetSetting("RainCollector/LitresPerHour")!.Value.AsFloat(), 0.001f);
        Assert.Equal(1500, config.GetSetting("Thirst/MaxThirst")!.Value.AsInt());
    }

    /// <summary>
    /// The file keeps the shape the mod's own StoreModConfig would have written, so an author
    /// migrating off LoadModConfig does not orphan their players' files.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task NestedSettingsAreWrittenAsNestedJson()
    {
        await OnServer();

        Config config = Fresh(new NestedSettings(), "schema-shape", "configkit-schema-shape.json");
        config.WriteToFile();

        JObject file = FileJson(config);

        Assert.True(file["RainCollector"] is JObject, $"expected a nested object, file was:\n{file}");
        Assert.Close(2.5f, file["RainCollector"]!["LitresPerHour"]!.Value<float>(), 0.001f);
        Assert.False(file.ContainsKey("RainCollector/LitresPerHour"), "the path was written as a literal key");
    }

    /// <summary>
    /// A nested value has to travel back onto the object the mod actually reads, which is a
    /// different object from the one registered. Nothing else in the library knew how.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task NestedValuesAssignBackOntoTheRegisteredObject()
    {
        await OnServer();

        NestedSettings settings = new();
        Config config = Fresh(settings, "schema-assign", "configkit-schema-assign.json");

        config.GetSetting("RainCollector/LitresPerHour")!.Value = new JsonObject(new JValue(9.5f));
        config.AssignSettingsValues(settings);

        Assert.Close(9.5f, settings.RainCollector.LitresPerHour, 0.001f);
    }

    [VsTest(TimeoutMs = 60000)]
    public async Task NestedObjectsGetTheirOwnSection()
    {
        await OnServer();

        Config config = Fresh(new NestedSettings(), "schema-sections", "configkit-schema-sections.json");

        List<string> titles = SeparatorTitles(config);

        // Tidied up the same way a label is, so a heading reads "Rain collector".
        Assert.True(titles.Contains("Rain collector"),
            $"no tidied section for the nested object; got {string.Join(", ", titles)}");
        Assert.True(titles.Contains("Doors"), $"[Category] did not name a section; got {string.Join(", ", titles)}");
    }

    /// <summary>
    /// [Category] is matched against its flag words with spaces and case stripped. The section
    /// name has to come from the raw text, or the heading reads "doors".
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ASectionKeepsTheCasingItsAuthorWrote()
    {
        await OnServer();

        Config config = Fresh(new TaggedSettings(), "schema-casing", "configkit-schema-casing.json");

        Assert.True(SeparatorTitles(config).Contains("Doors"),
            $"expected 'Doors'; got {string.Join(", ", SeparatorTitles(config))}");
    }

    /// <summary>
    /// The two flag words configlib understood still work, alongside a section name in the
    /// same attribute. Dropping them would change behaviour for anyone already using them.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task CategoryCarriesASectionAndTheClientSideFlagTogether()
    {
        await OnServer();

        Config config = Fresh(new TaggedSettings(), "schema-tags", "configkit-schema-tags.json");

        Assert.True(((ConfigSetting)config.GetSetting("Colour")!).ClientSide,
            "the clientside flag was lost when [Category] also named a section");
        Assert.True(SeparatorTitles(config).Contains("Waypoints"));
    }

    /// <summary>
    /// A nested setting's code is a path, but its row says only its own name - the heading
    /// above it already says which object it belongs to.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ANestedRowIsLabelledByItsOwnNameNotItsPath()
    {
        await OnClient();

        Delete("configkit-schema-label.json");
        NestedSettings settings = new();
        Config config = new(Capi, "schema-label", "schema-label", settings, "configkit-schema-label.json");

        ConfigKit.Gui.ConfigDialog dialog = new(Capi,
            new Dictionary<string, Config> { ["schema-label"] = config });
        dialog.TryOpen();
        await Frames.Wait(8);

        string section = dialog.Sections.First(title => title.Contains("Rain"));
        dialog.ToggleSectionNamed(section);
        await Frames.Wait(6);

        Assert.True(dialog.RenderedLabels.Contains("Litres per hour"),
            $"expected a tidy row label; on screen: {string.Join(", ", dialog.RenderedLabels)}");

        dialog.TryClose();
    }

    private static List<string> SeparatorTitles(Config config)
        => config.Definition["settings"].AsArray()
            .Where(block => block["type"].AsString() == "separator")
            .Select(block => block["title"].AsString(""))
            .ToList();

    /// <summary>
    /// [ReadOnly] was read into the schema and then never used - it sat in the documentation
    /// as a supported attribute while doing nothing at all.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ReadOnlyMembersAreMarkedAndStillStored()
    {
        await OnServer();

        Config config = Fresh(new ReadOnlySettings(), "schema-ro", "configkit-schema-ro.json");

        Assert.False(((ConfigSetting)config.GetSetting("Editable")!).ReadOnly);
        Assert.True(((ConfigSetting)config.GetSetting("Declared")!).ReadOnly, "[ReadOnly(true)] was ignored");
        Assert.True(((ConfigSetting)config.GetSetting("Fixed")!).ReadOnly,
            "a readonly field can never be assigned, so its control would do nothing");

        // A collection is filled in place rather than replaced, so it stays editable.
        Assert.False(((ConfigSetting)config.GetSetting("Fillable")!).ReadOnly,
            "a get-only collection was marked read-only, but it is filled in place");

        // Read-only is about the control, not the file: every key is still there.
        Assert.True(FileJson(config).ContainsKey("Declared"));
        Assert.True(FileJson(config).ContainsKey("Fixed"));
    }

    /// <summary>
    /// A translation key built from the member name alone is not unique once objects nest:
    /// two sub-objects each with an "Enabled" field shared one key, so a mod shipping
    /// translations would have seen one string on both rows.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task NestedSettingsDoNotShareATranslationKey()
    {
        await OnServer();

        Config config = Fresh(new Colliding(), "schema-lang", "configkit-schema-lang.json");

        string[] keys = config.Definition["settings"].AsArray()
            .Where(block => block["type"].AsString() != "separator")
            .Select(block => block["ingui"].AsString(""))
            .ToArray();

        Assert.Equal(2, keys.Length);
        Assert.Equal(2, keys.Distinct().Count(), $"two settings share one lang key: {string.Join(", ", keys)}");
        Assert.True(keys.Contains("schema-lang:setting-Doors-Enabled"),
            $"expected the path in the key; got {string.Join(", ", keys)}");
    }

    /// <summary>
    /// The rest of the attribute table, checked against the code rather than against the
    /// document that claims them. Three entries in that table turned out to do nothing at
    /// all, so the table is not evidence of anything by itself.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task TheDocumentedAttributesAllDoSomething()
    {
        await OnServer();

        Config config = Fresh(new TableSettings(), "schema-table", "configkit-schema-table.json");

        JsonObject[] blocks = config.Definition["settings"].AsArray();

        // [Display(Order = 0)] moves a member declared second to the front.
        Assert.Equal("Speed", blocks[0]["code"].AsString());

        JsonObject radius = blocks.First(b => b["code"].AsString() == "SearchRadius");
        JsonObject step = blocks.First(b => b["code"].AsString() == "Step");
        JsonObject enabled = blocks.First(b => b["code"].AsString() == "Enabled");
        JsonObject speed = blocks[0];

        // [DisplayName] and [Display(Name)] both become the label, verbatim rather than a
        // lang key the screen would have to fall back from.
        Assert.Equal("How far it looks", radius["ingui"].AsString());
        Assert.Equal("Speed multiplier", speed["ingui"].AsString());

        // [Description] is the hover text.
        Assert.Equal("What it says on the label.", enabled["comment"].AsString());

        // [AllowedValues] is a dropdown of fixed choices.
        Assert.True(step.KeyExists("values"), $"[AllowedValues] produced nothing: {step}");
        Assert.Equal(4, step["values"].AsArray().Length);
    }

    // ---------------------------------------------------------------- exclusion

    /// <summary>
    /// [Browsable(false)] is a statement about display, so it hides the row and keeps the key.
    /// [JsonIgnore] is a statement about serialisation, so it excludes the member outright.
    /// Treating them as the same rule would have deleted values out of existing files.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task BrowsableHidesAndJsonIgnoreExcludes()
    {
        await OnServer();

        Config config = Fresh(new ExclusionSettings(), "schema-exclude", "configkit-schema-exclude.json");

        Assert.True(config.SettingCodes.Contains("HiddenButStored"),
            "[Browsable(false)] removed the setting instead of hiding it, which orphans its stored value");
        Assert.True(((ConfigSetting)config.GetSetting("HiddenButStored")!).Hide,
            "[Browsable(false)] did not hide the row");

        Assert.False(config.SettingCodes.Contains("NotPersisted"), "[JsonIgnore] member was still managed");
    }

    /// <summary>
    /// The rule is that nothing vanishes. A member ConfigKit cannot store gets no setting -
    /// a key in the file for something that cannot round-trip would be worse - but it does
    /// get a line on screen. Reporting it only in the log is not the same thing: the player
    /// never sees the log.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AMemberThatCannotBeStoredStillSaysSoOnScreen()
    {
        await OnClient();

        Delete("configkit-schema-dead.json");
        HasDeadMember settings = new();
        Config config = new(Capi, "schema-dead", "schema-dead", settings, "configkit-schema-dead.json");

        Assert.False(config.SettingCodes.Contains("Broken"),
            "a member that cannot round-trip was given a key in the file");

        string[] notes = config.Definition["settings"].AsArray()
            .Where(block => block["type"].AsString() == "separator")
            .Select(block => block["text"].AsString(""))
            .ToArray();

        Assert.True(notes.Any(note => note.Contains("Broken") && note.Contains("not editable")),
            $"nothing on screen mentions it; blocks: {string.Join(" | ", notes)}");

        ConfigKit.Gui.ConfigDialog dialog = new(Capi,
            new Dictionary<string, Config> { ["schema-dead"] = config });
        dialog.TryOpen();
        await Frames.Wait(8);
        dialog.TryClose();
    }

    // ---------------------------------------------------------------- guards

    /// <summary>
    /// Registration must survive a config class that holds itself. Before the cycle guard this
    /// recursed until the stack ran out.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ASelfReferentialMemberDoesNotRecurse()
    {
        await OnServer();

        Config config = Fresh(new SelfReferential(), "schema-cycle", "configkit-schema-cycle.json");

        Assert.True(config.SettingCodes.Contains("Depth"), "the config came back empty");
        Assert.False(config.SettingCodes.Any(code => code.StartsWith("Config/Config/")),
            $"recursed into itself; codes: {string.Join(", ", config.SettingCodes)}");

        Assert.True(config.SchemaNotices.Any(notice => notice.Contains("cycle")),
            $"the cycle was skipped without saying so; notices: {string.Join(" | ", config.SchemaNotices)}");
    }

    /// <summary>
    /// The rule: every public member is rendered, deliberately excluded, or reported. Nothing
    /// vanishes. Containers are not editable yet - but they must not be silent, which is
    /// exactly what the old reflection walk did to them.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ContainersAreReportedRatherThanDroppedInSilence()
    {
        await OnServer();

        Config config = Fresh(new ContainerSettings(), "schema-containers", "configkit-schema-containers.json");

        Assert.True(config.SettingCodes.Contains("Scalar"));
        Assert.True(config.SchemaSummary.Contains("containers"),
            $"the summary does not mention them: '{config.SchemaSummary}'");
    }

    /// <summary>
    /// The skeleton is the author's object serialised, so a container still appears in the
    /// file in the right shape even before it is editable - and a player who hand-edits it
    /// does not have it wiped on the next save.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AContainerSurvivesInTheFileEvenWhileItIsNotEditable()
    {
        await OnServer();

        Config config = Fresh(new ContainerSettings(), "schema-keep", "configkit-schema-keep.json");
        config.WriteToFile();

        JObject file = FileJson(config);
        Assert.True(file["Weights"] is JObject, $"the dictionary is missing from the file:\n{file}");
        Assert.True(file["Names"] is JArray, "the list is missing from the file");
    }
}
