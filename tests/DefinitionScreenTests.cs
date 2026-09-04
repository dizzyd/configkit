using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// What the settings screen does for a mod that drives it from a configlib-patches.json
/// definition rather than from a C# class.
///
/// These mods did not ask for any of the structured-config work, and most of them ship no
/// C# at all - so the question worth answering is not "does it still work" but "does it look
/// the same". Folding and filtering were built for config derived from classes; this is where
/// it is checked that they do not turn up uninvited.
/// </summary>
[SingleplayerOnly]
public class DefinitionScreenTests
{
    /// <summary>A definition in the array format, optionally grouped under titled separators.</summary>
    private static string Definition(int settings, bool titledSections)
    {
        StringBuilder json = new();
        json.Append("{ \"version\": 1, \"settings\": [");

        for (int index = 0; index < settings; index++)
        {
            if (titledSections && index % 5 == 0)
            {
                json.Append($"{{ \"type\": \"separator\", \"title\": \"Group {index / 5}\", ")
                    .Append($"\"text\": \"What group {index / 5} is for.\" }},");
            }

            json.Append($"{{ \"type\": \"integer\", \"code\": \"n{index:00}\", ")
                .Append($"\"nameInGui\": \"Number {index}\", \"default\": {index} }}");

            if (index < settings - 1) json.Append(',');
        }

        return json.Append("] }").ToString();
    }

    private static ConfigDialog Open(string domain, int settings, bool titledSections)
    {
        Config config = new(Capi, domain, domain, new JsonObject(JToken.Parse(Definition(settings, titledSections))));

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { [domain] = config });
        dialog.TryOpen();
        return dialog;
    }

    /// <summary>
    /// Better Ruins' shape, and the one that showed this was wrong in a live game: the
    /// category-map format, with the separators declared in a separate "formatting" array and
    /// positioned among the settings by weight. Sixty-five flat settings with ten dividers
    /// through them - dividers the author placed for rhythm, not containers.
    /// </summary>
    private static string CategoryMapDefinition(int settings, int dividers)
    {
        StringBuilder json = new();
        json.Append("{ \"version\": 1, \"settings\": { \"integer\": {");

        for (int index = 0; index < settings; index++)
        {
            json.Append($"\"N{index:00}\": {{ \"default\": {index}, \"weight\": {index} }}");
            if (index < settings - 1) json.Append(',');
        }

        json.Append("} }, \"formatting\": [");

        for (int index = 0; index < dividers; index++)
        {
            float weight = index * (settings / (float)dividers) + 0.5f;
            json.Append($"{{ \"type\": \"separator\", \"title\": \"Part {index}\", ")
                .Append($"\"weight\": {weight.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}");
            if (index < dividers - 1) json.Append(',');
        }

        return json.Append("] }").ToString();
    }

    /// <summary>
    /// A definition's separators are dividers, not containers, so they never fold however
    /// long the list is. Folding them turned a legible sixty-five row screen into ten
    /// collapsed boxes with nothing in them.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ALongDividedDefinitionDoesNotFold()
    {
        await OnClient();

        string source = CategoryMapDefinition(65, 10);
        Config config = new(Capi, "def-divided", "def-divided", new JsonObject(JToken.Parse(source)));

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["def-divided"] = config });
        dialog.TryOpen();
        await Frames.Wait(8);

        string seen = $"sections={dialog.Sections.Count} rendered={dialog.RenderedSettings.Count} "
                    + $"allOpen={dialog.EverythingShown} codes={config.SettingCodes.Count()}";

        // The separators here sit on weights the settings also use, which is what turned up
        // an unguarded Add in ConstructYaml: it threw, and the config came back empty.
        Assert.True(config.SettingCodes.Count() == 65, $"the config came back empty: {seen}");
        Assert.True(dialog.Sections.Count == 10, $"expected 10 dividers: {seen}");
        Assert.True(dialog.EverythingShown, $"an author's dividers were treated as folds: {seen}");
        Assert.True(dialog.RenderedSettings.Count == 65, $"expected every row: {seen}");

        // The filter still narrows it, which is what makes a list this long usable.
        dialog.SetFilter("N4");
        await Frames.Wait(6);
        Assert.Equal(10, dialog.RenderedSettings.Count);

        dialog.TryClose();
    }

    /// <summary>
    /// The common case by a distance: a flat definition with no separators. Nothing groups it,
    /// so nothing can fold it, and every row is on screen exactly as before.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AFlatDefinitionIsUnchangedHoweverLongItIs()
    {
        await OnClient();

        ConfigDialog dialog = Open("def-flat", 30, titledSections: false);
        await Frames.Wait(8);

        Assert.Equal(0, dialog.Sections.Count);
        Assert.Equal(30, dialog.RenderedSettings.Count);

        dialog.TryClose();
    }

    /// <summary>A grouped definition that fits is shown whole, as it always was.</summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AShortGroupedDefinitionIsShownWhole()
    {
        await OnClient();

        ConfigDialog dialog = Open("def-short", 6, titledSections: true);
        await Frames.Wait(8);

        Assert.Equal(2, dialog.Sections.Count);
        Assert.True(dialog.EverythingShown, "a definition that fits was folded");
        Assert.Equal(6, dialog.RenderedSettings.Count);

        dialog.TryClose();
    }

    /// <summary>
    /// The one that actually changes. A long definition whose author used titled separators
    /// now folds under them, where before every row was on screen behind a scrollbar.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ALongGroupedDefinitionStillShowsEveryRow()
    {
        await OnClient();

        ConfigDialog dialog = Open("def-long", 30, titledSections: true);
        await Frames.Wait(8);

        Assert.Equal(6, dialog.Sections.Count);

        // Folding belongs to sections derived from a class. A definition's separators are the
        // author's own dividers, and hiding their rows changes a screen they laid out.
        Assert.True(dialog.EverythingShown);
        Assert.Equal(30, dialog.RenderedSettings.Count);

        dialog.SetFilter("Number 22");
        await Frames.Wait(6);
        Assert.Equal(1, dialog.RenderedSettings.Count);

        dialog.TryClose();
    }

    /// <summary>
    /// The definition format lets a separator carry an explanatory line as well as a title,
    /// and several published mods use it. Turning titled blocks into fold toggles threw that
    /// line away, which is a definition mod losing prose it wrote.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ASectionKeepsTheProseItsAuthorWrote()
    {
        await OnClient();

        ConfigDialog dialog = Open("def-prose", 6, titledSections: true);
        await Frames.Wait(8);

        // Short enough that nothing folds, so both groups say their piece.
        Assert.True(dialog.RenderedNotes.Contains("What group 0 is for."),
            $"the separator's text is gone; on screen: {string.Join(" | ", dialog.RenderedNotes)}");
        Assert.True(dialog.RenderedNotes.Contains("What group 1 is for."));

        dialog.TryClose();
    }

    /// <summary>A long definition keeps its prose too, however many rows it has.</summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ALongDefinitionKeepsItsProse()
    {
        await OnClient();

        ConfigDialog dialog = Open("def-prose-long", 30, titledSections: true);
        await Frames.Wait(8);

        Assert.True(dialog.RenderedNotes.Contains("What group 3 is for."),
            $"a section's line is missing; on screen: {string.Join(" | ", dialog.RenderedNotes)}");

        dialog.TryClose();
    }

    /// <summary>
    /// Whatever the grouping does, the values are the same and so is the file. A definition
    /// mod's config is the thing that must not move.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task TheValuesBehindADefinitionScreenAreUntouched()
    {
        await OnClient();

        ConfigDialog dialog = Open("def-values", 30, titledSections: true);
        await Frames.Wait(8);

        Config config = (Config)dialog.Configs["def-values"];

        Assert.Equal(30, config.SettingCodes.Count());
        Assert.Equal(7, config.GetSetting("n07")!.Value.AsInt());
        Assert.Equal(29, config.GetSetting("n29")!.Value.AsInt());

        // No schema: this config came from a definition, not from a class.
        Assert.Equal("", config.SchemaSummary);

        dialog.TryClose();
    }
}
