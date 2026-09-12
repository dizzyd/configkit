using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using Newtonsoft.Json.Linq;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Rows do not overlap each other.
///
/// Reported by a player, with a screenshot of WearAndTear's AutoPartRegistryConfig: several
/// raw-JSON boxes drawn on top of one another and over the rows beneath, labels colliding,
/// the screen unreadable and unusable.
///
/// The cause is structural rather than about any one mod. Rows are laid out by advancing a y
/// cursor by RowHeight + RowGap, but a raw-JSON member is given a control twice RowHeight
/// tall - so every such row overlaps whatever follows it, always, by design. No test caught
/// it because every test asked whether a row *existed*, which it did, in the same place as
/// its neighbour.
///
/// Hence the invariant rather than a case: no two controls may occupy the same space, for
/// any config, whatever the control.
/// </summary>
public class RowLayoutTests
{
    /// <summary>
    /// A Dictionary&lt;string, JToken&gt; has no schema by definition, so it is the raw JSON
    /// control - the one that is taller than its row. Two of them, with enough content to be
    /// worth drawing.
    /// </summary>
    public class Awkward
    {
        public bool Before = true;

        public Dictionary<string, JToken> LegacyData = new()
        {
            ["alpha"] = JToken.Parse(@"{""a"":1,""b"":""some text"",""c"":[1,2,3]}"),
            ["beta"] = JToken.Parse(@"{""a"":2,""b"":""more text here"",""c"":[4,5,6]}"),
        };

        public int Between = 3;

        public Dictionary<string, JToken> MoreLegacyData = new()
        {
            ["gamma"] = JToken.Parse(@"{""nested"":{""deep"":{""deeper"":""value""}}}"),
        };

        public string After = "last";
    }

    /// <summary>
    /// Labels that do not fit their line. The reported one was an untranslated key with a
    /// display name passed as the domain - "Wear And Tear / Server / Auto Part Registry
    /// Config:setting-Enabled" - which wrapped to three lines and was drawn over the two
    /// rows beneath it. A long [Display(Name)] wraps the same way with no key involved.
    /// </summary>
    public class Wordy
    {
        public bool Before = true;

        [System.ComponentModel.DataAnnotations.Display(
            Name = "Whether the registry should also consider every part of the fruit press when it decides what wears")]
        public bool IncludeFruitPress = true;

        public int Between = 3;

        [System.ComponentModel.DataAnnotations.Display(
            Name = "The lowest proportion of metal a recipe's ingredients may have for its product to count as a metal part")]
        public float MinimalMetalComposition = 0.8f;

        public string After = "last";
    }

    private static List<string> Overlaps(ConfigDialog dialog)
    {
        List<string> problems = [];
        IReadOnlyList<(string Code, double Y, double Height)> rows = dialog.RowGeometry;

        for (int index = 1; index < rows.Count; index++)
        {
            (string code, double y, double height) = rows[index - 1];
            (string nextCode, double nextY, double _) = rows[index];

            if (y + height > nextY + 0.5)
            {
                problems.Add($"{code} ends at {y + height} but {nextCode} starts at {nextY}");
            }
        }

        return problems;
    }

    /// <summary>
    /// The reported case: a config holding raw-JSON members among ordinary ones.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task ARawJsonRowDoesNotOverlapTheRowBelowIt()
    {
        await OnClient();

        Config config = new(Capi, "ckrows", "Rows", new Awkward(), "ck-rows.json");
        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["ckrows"] = config });
        dialog.TryOpen();
        await Frames.Wait(8);

        try
        {
            // The control that caused it is present, so this test is actually testing it.
            Assert.Equal("GuiElementTextArea", dialog.ControlKindFor("LegacyData"));

            Assert.Equal("", string.Join("\n", Overlaps(dialog)));
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// A label that wraps makes its row taller, rather than running on into the next one.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task ALabelThatWrapsMakesItsRowTaller()
    {
        await OnClient();

        Config config = new(Capi, "ckwordy", "Wordy", new Wordy(), "ck-wordy.json");
        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["ckwordy"] = config });
        dialog.TryOpen();
        await Frames.Wait(8);

        try
        {
            // The long names made it to the screen, so the rows being measured are the wide ones.
            string labels = string.Join("\n", dialog.RenderedLabels);
            Assert.Contains(labels, "Whether the registry should also consider");
            Assert.Contains(labels, "The lowest proportion of metal");

            Assert.Equal("", string.Join("\n", Overlaps(dialog)));

            // And they were actually taller: a one-line row is RowHeight, and these are not.
            IReadOnlyList<(string Code, double Y, double Height)> rows = dialog.RowGeometry;
            double plain = rows.Single(row => row.Code == "Before").Height;
            Assert.Greater(rows.Single(row => row.Code == "IncludeFruitPress").Height, plain,
                "IncludeFruitPress's row against a one-line row");
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// The other half of that report. A domain with spaces in it - a display name passed
    /// where the mod id goes - made every untranslated key look like a translated label to
    /// the dialog's "has a colon and no space" test, so the raw key went on the row. The
    /// setting knows whether its label was translated; the dialog is to ask it.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task AnUntranslatedKeyUnderASpacedDomainStillReadsAsTheMember()
    {
        await OnClient();

        const string domain = "Wear And Tear / Wear And Tear / Server / Auto Part Registry Config";
        Config config = new(Capi, domain, domain, new Awkward(), "ck-spaced.json");
        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { [domain] = config });
        dialog.TryOpen();
        await Frames.Wait(8);

        try
        {
            string labels = string.Join("\n", dialog.RenderedLabels);
            Assert.Contains(labels, "Before");
            Assert.False(labels.Contains(":setting-"), "a raw key on a row: " + labels);
            Assert.Equal("", string.Join("\n", Overlaps(dialog)));
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// And the same invariant over every config that happens to be loaded, so a control added
    /// later cannot reintroduce it. Run with real mods this covers their shapes too.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task NoConfigDrawsTwoControlsInTheSamePlace()
    {
        await OnClient();

        ConfigKitModSystem system = Capi.ModLoader.GetModSystem<ConfigKitModSystem>();
        List<string> problems = [];
        int checkedConfigs = 0;

        foreach (string domain in system.Domains.OrderBy(domain => domain))
        {
            if (system.GetConfig(domain) is not Config config) continue;

            ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { [domain] = config });
            dialog.TryOpen();
            await Frames.Wait(2);

            try
            {
                checkedConfigs++;
                problems.AddRange(Overlaps(dialog).Select(problem => $"{domain}: {problem}"));

                // Sections fold rows away; open each in turn so their rows are measured too.
                foreach (string section in dialog.Sections.ToList())
                {
                    dialog.ToggleSectionNamed(section);
                    await Frames.Wait(1);
                    problems.AddRange(Overlaps(dialog).Select(problem => $"{domain} [{section}]: {problem}"));
                }
            }
            finally
            {
                dialog.TryClose();
            }
        }

        Log($"checked {checkedConfigs} configs");
        Assert.Equal("", string.Join("\n", problems.Take(10)));
    }
}
