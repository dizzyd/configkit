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
