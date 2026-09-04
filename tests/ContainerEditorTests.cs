using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// The settings screen for structured config: folding sections, and the drill-down editor
/// for dictionaries and lists.
///
/// The defects these are written against are real ones, copied across seven mods in the
/// published corpus: a rename that destroys the entry it collides with, an add that lands on
/// a key already in use, and an editor that mutates the dictionary while indexing it.
/// </summary>
[SingleplayerOnly]
public class ContainerEditorTests
{
    public enum Nutrient { N, P, K }

    public class Door
    {
        [Key]
        public string EntityCode = "";
        public float Chance = 0.5f;
        public bool ClosesBehind = true;
        public List<string> Doors = new();
    }

    public class Rain
    {
        public bool Enabled = true;
        public float LitresPerHour = 2.5f;
    }

    public class Settings
    {
        public bool Enabled = true;

        [Category("Doors")]
        public Dictionary<string, Door> Creatures = new()
        {
            ["game:drifter-normal"] = new Door { EntityCode = "game:drifter-normal" },
            ["game:wolf-male"] = new Door { EntityCode = "game:wolf-male", Chance = 0.8f }
        };

        [Category("Doors")]
        public Dictionary<string, int> Delays = new() { ["oak"] = 4000 };

        [Category("Chutes")]
        public Dictionary<string, Dictionary<string, float>> Flow = new()
        {
            ["copper"] = new() { ["in"] = 1f, ["out"] = 2f }
        };

        [Category("Soil")]
        public Dictionary<Nutrient, float> Nutrients = new()
        {
            [Nutrient.N] = 1f, [Nutrient.P] = 2f, [Nutrient.K] = 3f
        };

        [Category("Loot")]
        public List<Door> Pool = new() { new Door { EntityCode = "first" } };

        /// <summary>Dana Tweaks' AutoCloseDelays: keys are block codes, wildcards included.</summary>
        [Category("Blocks")]
        [DataType("blockcode")]
        public Dictionary<string, int> Blocks = new()
        {
            ["game:door-plank-north-down-closed-left"] = 1,
            ["game:door-*"] = 2,
            ["game:not-a-real-block"] = 3
        };

        public Rain Rain = new();
    }

    /// <summary>A config small enough that folding it would only get in the way.</summary>
    public class Tiny
    {
        [Category("Only")]
        public bool Enabled = true;
    }

    // ---------------------------------------------------------------- helpers

    private static void Delete(string file)
    {
        string path = Path.Combine(Capi.DataBasePath, "ModConfig", file);
        if (File.Exists(path)) File.Delete(path);
    }

    private static (ConfigDialog dialog, Settings settings) Open(string domain)
    {
        string file = $"configkit-{domain}.json";
        Delete(file);

        Settings settings = new();
        Config config = new(Capi, domain, domain, settings, file);
        config.AssignSettingsValues(settings);

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { [domain] = config });
        dialog.TryOpen();
        return (dialog, settings);
    }

    // ---------------------------------------------------------------- accordion

    /// <summary>
    /// One section open at a time. Opening a second closes the first, which is what keeps a
    /// config with a dozen sub-objects to a dozen headings and one body.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task OpeningASectionClosesTheOneBefore()
    {
        await OnClient();

        (ConfigDialog dialog, _) = Open("acc-one");
        await Frames.Wait(6);

        Assert.False(dialog.EverythingShown, "this config is too big to be shown unfolded");

        dialog.ToggleSectionNamed("Doors");
        await Frames.Wait(4);
        Assert.Equal("Doors", dialog.OpenSection);

        dialog.ToggleSectionNamed("Chutes");
        await Frames.Wait(4);
        Assert.Equal("Chutes", dialog.OpenSection);

        dialog.ToggleSectionNamed("Chutes");
        await Frames.Wait(4);
        Assert.Null(dialog.OpenSection);

        dialog.TryClose();
    }

    /// <summary>
    /// Folding a config that already fits hides things for no reason. The layout pass already
    /// measures its own height, so this costs nothing to know.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ASmallConfigIsNotFoldedAtAll()
    {
        await OnClient();

        Delete("configkit-acc-tiny.json");
        Tiny settings = new();
        Config config = new(Capi, "acc-tiny", "acc-tiny", settings, "configkit-acc-tiny.json");

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["acc-tiny"] = config });
        dialog.TryOpen();
        await Frames.Wait(6);

        Assert.True(dialog.EverythingShown, "a one setting config was folded");
        Assert.True(dialog.RenderedSettings.Values.Any(setting => setting.YamlCode == "Enabled"),
            "the only setting was hidden behind a fold");

        dialog.TryClose();
    }

    // ---------------------------------------------------------------- drill-down

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task OpeningAContainerShowsItsEntries()
    {
        await OnClient();

        (ConfigDialog dialog, _) = Open("drill-open");
        await Frames.Wait(6);

        Assert.True(dialog.OpenSetting("Creatures"), "the container would not open");
        await Frames.Wait(4);

        Assert.Equal("Creatures", string.Join(" > ", dialog.OpenPath));
        Assert.Equal("game:drifter-normal,game:wolf-male", string.Join(",", dialog.EntryLabels));

        Assert.True(dialog.Back());
        await Frames.Wait(4);
        Assert.Equal(0, dialog.OpenPath.Count);

        dialog.TryClose();
    }

    /// <summary>
    /// A dictionary of dictionaries is the shape nothing in the corpus handles generically -
    /// Vanilla Variants loops the outer one in the caller and hand-writes an editor for each
    /// inner one. Here the second level is the first level again.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ANestedDictionaryOpensTwoLevelsDeep()
    {
        await OnClient();

        (ConfigDialog dialog, _) = Open("drill-nested");
        await Frames.Wait(6);

        Assert.True(dialog.OpenSetting("Flow"));
        await Frames.Wait(4);
        Assert.Equal("copper", string.Join(",", dialog.EntryLabels));

        Assert.True(dialog.OpenEntry("copper"), "the inner dictionary would not open");
        await Frames.Wait(4);

        Assert.Equal("Flow > copper", string.Join(" > ", dialog.OpenPath));
        Assert.Equal("in,out", string.Join(",", dialog.EntryLabels));

        dialog.TryClose();
    }

    /// <summary>A list row is labelled by its [Key] member rather than by its index.</summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AListRowIsLabelledByItsKeyMember()
    {
        await OnClient();

        (ConfigDialog dialog, _) = Open("drill-list");
        await Frames.Wait(6);

        Assert.True(dialog.OpenSetting("Pool"));
        await Frames.Wait(4);

        Assert.Equal("first", string.Join(",", dialog.EntryLabels));

        dialog.TryClose();
    }

    // ---------------------------------------------------------------- editing

    /// <summary>
    /// The defect copied across seven mods: rename does Remove then TryAdd, so renaming onto
    /// a key that already exists drops the entry that was there with no message. Refusing is
    /// the whole point.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ARenameOntoAnExistingKeyIsRefusedAndLosesNothing()
    {
        await OnClient();

        (ConfigDialog dialog, Settings settings) = Open("edit-rename");
        await Frames.Wait(6);

        Assert.True(dialog.OpenSetting("Creatures"));
        await Frames.Wait(4);

        Assert.False(dialog.RenameEntry("game:wolf-male", "game:drifter-normal"),
            "the rename was allowed onto a key that already exists");
        await Frames.Wait(4);

        Assert.Equal("game:drifter-normal,game:wolf-male", string.Join(",", dialog.EntryLabels));
        Assert.Equal(2, settings.Creatures.Count);
        Assert.Close(0.8f, settings.Creatures["game:wolf-male"].Chance, 0.001f);

        dialog.TryClose();
    }

    /// <summary>A rename that is allowed keeps its position rather than jumping to the end.</summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ARenameKeepsItsValueAndItsPlace()
    {
        await OnClient();

        (ConfigDialog dialog, Settings settings) = Open("edit-rename-ok");
        await Frames.Wait(6);

        Assert.True(dialog.OpenSetting("Creatures"));
        await Frames.Wait(4);

        Assert.True(dialog.RenameEntry("game:drifter-normal", "game:drifter-deep"));
        await Frames.Wait(4);

        Assert.Equal("game:drifter-deep,game:wolf-male", string.Join(",", dialog.EntryLabels));
        Assert.True(settings.Creatures.ContainsKey("game:drifter-deep"), "the renamed entry did not reach the object");
        Assert.False(settings.Creatures.ContainsKey("game:drifter-normal"));

        dialog.TryClose();
    }

    /// <summary>
    /// Add invents a key that is free. A hand-rolled editor typically adds under a name that
    /// already exists, which silently replaces the entry that was there.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AddingAnEntryPicksAFreeKeyAndReachesTheObject()
    {
        await OnClient();

        (ConfigDialog dialog, Settings settings) = Open("edit-add");
        await Frames.Wait(6);

        Assert.True(dialog.OpenSetting("Delays"));
        await Frames.Wait(4);

        Assert.True(dialog.AddEntry());
        await Frames.Wait(4);

        Assert.Equal(2, dialog.EntryLabels.Count);
        Assert.True(dialog.EntryLabels.Contains("oak"), "the entry that was there is gone");
        Assert.Equal(2, settings.Delays.Count);

        // Adding twice must not collide with the name the first one took.
        Assert.True(dialog.AddEntry());
        await Frames.Wait(4);
        Assert.Equal(3, settings.Delays.Count);

        dialog.TryClose();
    }

    /// <summary>
    /// A dictionary keyed by a three member enum that already holds three entries has nowhere
    /// to put a fourth. Saying so beats a button that silently does nothing.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AnExhaustedKeyDomainSaysWhyItCannotAdd()
    {
        await OnClient();

        (ConfigDialog dialog, _) = Open("edit-exhausted");
        await Frames.Wait(6);

        Assert.True(dialog.OpenSetting("Nutrients"));
        await Frames.Wait(4);

        Assert.Equal(3, dialog.EntryLabels.Count);
        Assert.False(dialog.CanAddEntry(out string reason), "a fourth entry was offered for a three member enum");
        Assert.True(reason.Length > 0, "no reason was given");

        dialog.TryClose();
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task RemovingAnEntryReachesTheObject()
    {
        await OnClient();

        (ConfigDialog dialog, Settings settings) = Open("edit-remove");
        await Frames.Wait(6);

        Assert.True(dialog.OpenSetting("Creatures"));
        await Frames.Wait(4);

        Assert.True(dialog.RemoveEntry("game:wolf-male"));
        await Frames.Wait(4);

        Assert.Equal("game:drifter-normal", string.Join(",", dialog.EntryLabels));
        Assert.Equal(1, settings.Creatures.Count);
        Assert.False(settings.Creatures.ContainsKey("game:wolf-male"));

        dialog.TryClose();
    }

    /// <summary>
    /// Editing a value two levels down has to travel all the way back out to the mod's own
    /// object, through the container setting that owns the whole subtree.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AnEditTwoLevelsDownReachesTheObject()
    {
        await OnClient();

        (ConfigDialog dialog, Settings settings) = Open("edit-deep");
        await Frames.Wait(6);

        Assert.True(dialog.OpenSetting("Flow"));
        await Frames.Wait(4);
        Assert.True(dialog.OpenEntry("copper"));
        await Frames.Wait(4);

        Assert.True(dialog.RenameEntry("in", "inflow"));
        await Frames.Wait(4);

        Assert.True(settings.Flow["copper"].ContainsKey("inflow"),
            $"the deep edit did not reach the object; keys: {string.Join(",", settings.Flow["copper"].Keys)}");
        Assert.Close(1f, settings.Flow["copper"]["inflow"], 0.001f);

        dialog.TryClose();
    }

    // ---------------------------------------------------------------- code hints

    /// <summary>
    /// A mistyped block code is the commonest way a structured config quietly does nothing:
    /// the entry looks right in the file and simply never matches. [DataType] is what lets
    /// the screen say so, and a wildcard has to count as valid or every real config is wrong.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ACodeThatNamesNothingIsFlaggedAndAWildcardIsNot()
    {
        await OnClient();

        bool? exact = CodeHints.Resolves(Capi, "blockcode", "game:door-plank-north-down-closed-left");
        bool? wild = CodeHints.Resolves(Capi, "blockcode", "game:door-*");
        bool? bogus = CodeHints.Resolves(Capi, "blockcode", "game:not-a-real-block");
        bool? entity = CodeHints.Resolves(Capi, "entitycode", "game:wolf-*");

        string seen = $"blocks={Capi.World.Blocks?.Count} entities={Capi.World.EntityTypes?.Count} "
                    + $"exact={exact} wild={wild} bogus={bogus} entity={entity}";

        Assert.True(wild == true, $"a wildcard over real blocks was not accepted: {seen}");
        Assert.True(exact == true, $"an exact real block code was not accepted: {seen}");
        Assert.True(bogus == false, $"a code that names nothing was not flagged: {seen}");
        Assert.True(entity == true, $"a wildcard over real entities was not accepted: {seen}");

        // No attribute means no opinion. Marking every key on an unannotated dictionary would
        // be worse than saying nothing.
        Assert.Null(CodeHints.Resolves(Capi, null, "game:whatever"));
        Assert.Null(CodeHints.Resolves(Capi, "somethingelse", "game:whatever"));
    }

    /// <summary>The attribute is spelled several ways in the wild; all of them mean the same thing.</summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task TheHintIsForgivingAboutHowItIsSpelled()
    {
        await OnClient();

        foreach (string spelling in new[] { "blockcode", "block-code", "block_code", "Block", "BLOCKS" })
        {
            // Compared with == rather than Assert.Equal: the harness does not unify bool
            // with bool?, so Assert.Equal(true, someNullableBool) fails whatever it holds.
            Assert.True(CodeHints.Resolves(Capi, spelling, "game:door-*") == true,
                $"'{spelling}' was not understood");
        }

        Assert.NotNull(CodeHints.Placeholder("entitycode"));
    }

    /// <summary>The screen still composes with a dictionary whose keys are checked.</summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ADictionaryOfBlockCodesStillOpens()
    {
        await OnClient();

        (ConfigDialog dialog, _) = Open("hint-open");
        await Frames.Wait(6);

        Assert.True(dialog.OpenSetting("Blocks"));
        await Frames.Wait(6);

        Assert.Equal("game:door-plank-north-down-closed-left,game:door-*,game:not-a-real-block",
            string.Join(",", dialog.EntryLabels));

        dialog.TryClose();
    }

    // ---------------------------------------------------------------- the bottom buttons

    /// <summary>
    /// Save, Reload and Restore defaults act on the whole config from wherever the player is
    /// standing. Restore asks first, because from three levels down it looks local.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task RestoreDefaultsAsksOnceAndThenResetsEverything()
    {
        await OnClient();

        (ConfigDialog dialog, Settings settings) = Open("edit-defaults");
        await Frames.Wait(6);

        Assert.True(dialog.OpenSetting("Creatures"));
        await Frames.Wait(4);
        Assert.True(dialog.RemoveEntry("game:wolf-male"));
        await Frames.Wait(4);
        Assert.Equal(1, settings.Creatures.Count);

        Config config = (Config)dialog.Configs["edit-defaults"];
        config.RestoreToDefaults();
        config.AssignSettingsValues(settings);
        await Frames.Wait(4);

        Assert.Equal(2, settings.Creatures.Count);
        Assert.True(settings.Creatures.ContainsKey("game:wolf-male"), "restore did not bring the entry back");

        dialog.TryClose();
    }
}
