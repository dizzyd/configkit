using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Every texture the settings window makes is let go of when the window is rebuilt.
///
/// Reported by TheInsanityGod as a lag spike after "the UI changes a lot", with the log
/// full of "Texture with texture id N is leaking memory, missing call to Dispose". That
/// line is written by LoadedTexture's finaliser, so the spike is the garbage collector
/// finding a pile of textures nobody disposed - one pile per rebuild of the screen, and the
/// screen is rebuilt on every fold, drill-down and mod switch.
///
/// The game keeps the allocation trace when RuntimeEnv.DebugTextureDispose is set, which is
/// what turns "something leaks" into a line number - and these tests report every leak
/// they find, whoever owns it, because a leak in a stock control would show up here first.
/// </summary>
public class TextureLeakTests
{
    public enum Level { Low, Mid, High }

    public class Rule
    {
        [Key]
        public string Code = "";
        [Range(0.0, 1.0)]
        public float Chance = 0.5f;
        public Level Tier = Level.Mid;
        public bool On = true;
    }

    public class Busy
    {
        [Description("a switch")]
        public bool Enabled = true;

        [Description("an enum")]
        public Level Level = Level.Mid;

        [Description("a nullable bool")]
        public bool? Maybe;

        [Category("Numbers")]
        [Range(0, 100)]
        public int Slider = 50;

        [Category("Numbers")]
        [Range(0.0, 1.0)]
        public float Ratio = 0.5f;

        [Category("Numbers")]
        public int Plain = 5;

        [Category("Numbers")]
        public float? Optional;

        [Category("Words")]
        public string Text = "hello";

        [Category("Words")]
        [AllowedValues("a", "b", "c")]
        public string Choice = "a";

        [Category("Words")]
        public string Colour = "#4FBFA8";

        [Category("Rules")]
        public Dictionary<string, Rule> Rules = new()
        {
            ["one"] = new Rule { Code = "one" },
            ["two"] = new Rule { Code = "two", Chance = 0.8f }
        };

        [Category("Rules")]
        public Dictionary<Level, float> ByLevel = new() { [Level.Low] = 1f };

        [Category("Rules")]
        public List<string> Codes = new() { "game:door-*" };
    }

    // ---------------------------------------------------------------- the log

    private static string LogPath => Path.Combine(Capi.DataBasePath, "Logs", "client-debug.log");

    private static string[] LeakLines()
    {
        if (!File.Exists(LogPath)) return [];

        // Read without locking the file the game is still writing to.
        using FileStream stream = new(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(stream);

        // One entry per leak, with the trace's frames - which the game writes on the lines
        // that follow - folded onto it, so the site can be read off the entry.
        List<string> leaks = [];

        foreach (string line in reader.ReadToEnd().Split('\n'))
        {
            if (line.Contains("is leaking memory")) leaks.Add(line.TrimEnd());
            else if (leaks.Count > 0 && line.TrimStart().StartsWith("at ")) leaks[^1] += " " + line.Trim();
        }

        return [.. leaks];
    }

    /// <summary>
    /// Collects, waits for the finalisers, and fails with the allocation sites of whatever
    /// was left behind - grouped by the frame that made the texture, so a stock control's
    /// name comes out as readily as one of ours.
    /// </summary>
    private static async Task ExpectNoLeaksSince(int before, string doing)
    {
        // Nothing references the replaced composers now, so a full collection finalises
        // every texture that was never disposed.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await Frames.Wait(6);

        string[] fresh = LeakLines().Skip(before).ToArray();
        if (fresh.Length == 0) return;

        IEnumerable<string> sites = fresh
            .Select(Site)
            .GroupBy(site => site)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Count()} x {group.Key}")
            .Take(8);

        Assert.Fail($"{fresh.Length} textures leaked while {doing}:\n  " + string.Join("\n  ", sites));
    }

    /// <summary>The first frame of a leak's trace that is not the texture's own constructor.</summary>
    private static string Site(string line)
    {
        string[] frames = line.Split(" at ", StringSplitOptions.RemoveEmptyEntries);

        return frames
            .Skip(1)
            .Select(frame => frame.Trim())
            .FirstOrDefault(frame => !frame.Contains("Environment.get_StackTrace")
                                  && !frame.Contains("LoadedTexture..ctor"))
            ?? line;
    }

    // ---------------------------------------------------------------- rebuilding

    /// <summary>
    /// Rebuilds the screen many times through the model and then asks the collector what
    /// was left behind.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task RebuildingTheScreenLeaksNoTextures()
    {
        await OnClient();

        RuntimeEnv.DebugTextureDispose = true;

        Config first = new(Capi, "ckleak-a", "Leak A", new Busy(), "ck-leak-a.json");
        Config second = new(Capi, "ckleak-b", "Leak B", new Busy(), "ck-leak-b.json");

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config>
        {
            ["ckleak-a"] = first,
            ["ckleak-b"] = second
        });

        int before = LeakLines().Length;

        dialog.TryOpen();
        await Frames.Wait(6);

        try
        {
            for (int round = 0; round < 8; round++)
            {
                dialog.ToggleSectionNamed("Numbers");
                await Frames.Wait(2);
                dialog.ToggleSectionNamed("Words");
                await Frames.Wait(2);
                dialog.ToggleSectionNamed("Rules");
                await Frames.Wait(2);

                Assert.True(dialog.OpenSetting("Rules"));
                await Frames.Wait(2);
                Assert.True(dialog.OpenEntry("two"));
                await Frames.Wait(2);
                dialog.Back();
                await Frames.Wait(2);
                dialog.Back();
                await Frames.Wait(2);

                dialog.SetFilter("a");
                await Frames.Wait(2);
                dialog.SetFilter("");
                await Frames.Wait(2);
            }
        }
        finally
        {
            dialog.TryClose();
            RuntimeEnv.DebugTextureDispose = false;
        }

        await Frames.Wait(4);
        await ExpectNoLeaksSince(before, "rebuilding the screen");
    }

    // ---------------------------------------------------------------- the reported case

    public class Long
    {
        public Dictionary<string, int> Many = new(
            Enumerable.Range(0, 60).Select(i => new KeyValuePair<string, int>($"entry-{i:00}", i)));
    }

    /// <summary>
    /// The case from TheInsanityGod's traced log: a container screen too long to fit, and a
    /// rebuild of the screen fired from one of its own rows - a remove, an add, a rename, a
    /// fold. Every row scrolled out of view leaked its textures, 2817 of them in one minute
    /// of editing a long dictionary, because the container's mouse handlers ran with only
    /// the visible rows in its element list and the rebuild disposed the container while
    /// they did.
    ///
    /// Driven through the mouse on purpose: called from code, the handler is not inside the
    /// container's dispatch and the leak cannot happen. That is why no other test saw it.
    /// </summary>
    [VsTest(TimeoutMs = 120000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task RemovingARowOfALongListLeaksNothing()
    {
        await OnClient();

        RuntimeEnv.DebugTextureDispose = true;

        Config config = new(Capi, "ckleak-long", "Leak Long", new Long(), "ck-leak-long.json");
        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["ckleak-long"] = config });

        int before = LeakLines().Length;

        dialog.TryOpen();
        await Frames.Wait(6);

        try
        {
            Assert.True(dialog.OpenSetting("Many"));
            await Frames.Wait(6);

            // More rows than the window shows, or there is nothing out of view to leak.
            Assert.Greater(dialog.RenderedSettings.Count, 20, "the fixture should overflow the window");

            for (int round = 0; round < 3; round++)
            {
                string first = dialog.EntryLabels.First();
                var rect = dialog.RemoveButtonRectFor(first);
                Assert.NotNull(rect, "no remove button on screen for the first row");

                await ClickAt(dialog, (int)(rect!.Value.X + rect.Value.Width / 2), (int)(rect.Value.Y + rect.Value.Height / 2));
                await Frames.Wait(3);

                Assert.False(dialog.EntryLabels.Contains(first), "the click did not remove the row");
            }
        }
        finally
        {
            dialog.TryClose();
            RuntimeEnv.DebugTextureDispose = false;
        }

        await Frames.Wait(4);
        await ExpectNoLeaksSince(before, "removing rows of a long list by mouse");
    }

    // ---------------------------------------------------------------- playing with it

    /// <summary>
    /// Drives the window the way a player does - through the game's own mouse and keyboard
    /// events, not the model - across every config the session has loaded. Clicks each
    /// control, hovers each tooltip, opens each dropdown, drags each slider, types into each
    /// box, folds and drills and filters, and between rounds changes the GUI scale, which is
    /// the game's own "recompose every dialog" path, and closes and reopens the window.
    ///
    /// The model-driven test above cannot see a leak in something that only composes on
    /// use: a tooltip's texture is made the first time it is shown, a dropdown's list the
    /// first time it is opened, a slider's hover text when it is dragged. This one can.
    ///
    /// With the compatibility pack loaded, the configs here are other authors' real ones.
    /// </summary>
    [VsTest(TimeoutMs = 600000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task PlayingWithTheScreenLeaksNoTextures()
    {
        await OnClient();

        RuntimeEnv.DebugTextureDispose = true;

        Config own = new(Capi, "ckleak-c", "Leak C", new Busy(), "ck-leak-c.json");
        Dictionary<string, Config> configs = new() { ["ckleak-c"] = own };

        // Proof that the events land: a click that a control never received changes nothing.
        int changes = 0;
        own.SettingChanged += _ => changes++;

        ConfigKitModSystem system = Capi.ModLoader.GetModSystem<ConfigKitModSystem>();
        foreach (string domain in system.Domains)
        {
            if (system.GetConfig(domain) is Config real) configs[domain] = real;
        }

        ConfigDialog dialog = new(Capi, configs);

        int before = LeakLines().Length;
        int actions = 0;

        dialog.TryOpen();
        await Frames.Wait(6);

        try
        {
            for (int round = 0; round < 3; round++)
            {
                foreach (string domain in configs.Keys)
                {
                    Assert.True(dialog.ShowDomain(domain));
                    await Frames.Wait(3);

                    actions += await Play(dialog);
                }

                // The game's own route to a recompose of everything open. Watched on the
                // guiScale setting by ClientMain, which marks every composer to rebuild on
                // its next render.
                Capi.Settings.Float["guiScale"] = 1.1f;
                await Frames.Wait(4);
                Capi.Settings.Float["guiScale"] = 1f;
                await Frames.Wait(4);

                dialog.TryClose();
                await Frames.Wait(3);
                dialog.TryOpen();
                await Frames.Wait(6);
            }
        }
        finally
        {
            Capi.Settings.Float["guiScale"] = 1f;
            dialog.TryClose();
            RuntimeEnv.DebugTextureDispose = false;
        }

        Log($"drove {actions} controls across {configs.Count} configs; {changes} value changes on the fixture");
        Assert.Greater(actions, 20, "the test hardly touched anything");
        Assert.Greater(changes, 20, "the mouse and keyboard events did not reach the controls");

        await Frames.Wait(4);
        await ExpectNoLeaksSince(before, $"playing with {configs.Count} configs' screens");
    }

    /// <summary>One mod's screen: every section, every visible control, every container two levels down.</summary>
    private static async Task<int> Play(ConfigDialog dialog)
    {
        int actions = 0;

        foreach (string section in dialog.Sections.ToArray())
        {
            dialog.ToggleSectionNamed(section);
            await Frames.Wait(2);
            actions += await Poke(dialog);
        }

        // Everything at the top, and everything if nothing folds.
        actions += await Poke(dialog);

        foreach (string code in dialog.RenderedSettings.Values.Select(s => s.YamlCode).Distinct().ToArray())
        {
            if (!dialog.OpenSetting(code)) continue;
            await Frames.Wait(3);
            actions += await Poke(dialog);

            string? first = dialog.EntryLabels.FirstOrDefault();
            if (first != null && dialog.OpenEntry(first))
            {
                await Frames.Wait(3);
                actions += await Poke(dialog);
                dialog.Back();
                await Frames.Wait(2);
            }

            dialog.Back();
            await Frames.Wait(2);
        }

        dialog.SetFilter("e");
        await Frames.Wait(2);
        actions += await Poke(dialog);
        dialog.SetFilter("");
        await Frames.Wait(2);

        return actions;
    }

    /// <summary>
    /// A press and release at a point, handed to the dialog the way the game's GUI system
    /// hands it one. The harness's Input.Click cannot be used here: the game builds that
    /// event from the platform's real cursor, which on a test box sits wherever it sat, so
    /// the click lands nowhere near the control. Moving first keeps api.Input.MouseX in
    /// step, which is what a dropdown's list and a tooltip read.
    /// </summary>
    private static async Task ClickAt(ConfigDialog dialog, int x, int y)
    {
        await Input.MouseMove(x, y);
        await Frames.Wait(1);
        dialog.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        await Frames.Wait(2);
        dialog.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        await Frames.Wait(1);
    }

    /// <summary>Every control on the screen as it stands: hovered, then used, then reset.</summary>
    private static async Task<int> Poke(ConfigDialog dialog)
    {
        int actions = 0;

        ElementBounds? clip = dialog.SingleComposer?.GetElement("rows")?.InsideClipBounds;

        foreach (string code in dialog.RenderedSettings.Values.Select(s => s.YamlCode).Distinct().ToArray())
        {
            if (dialog.ScreenRectFor(code) is not (double x, double y, double w, double h)) continue;
            if (w <= 0 || h <= 0) continue;

            // Only what is actually on screen. A row scrolled out of view is culled, and a
            // click meant for it would land on whatever is drawn there instead.
            if (clip != null && (y < clip.absY || y + h > clip.absY + clip.OuterHeight)) continue;

            int centreX = (int)(x + w / 2);
            int centreY = (int)(y + h / 2);

            // The label's tooltip composes the first time the mouse rests on it.
            await Input.MouseMove((int)(x - 120), centreY);
            await Frames.Wait(2);

            await Input.MouseMove(centreX, centreY);
            await Frames.Wait(1);

            switch (dialog.ControlKindFor(code))
            {
                case "GuiElementSwitch":
                    await ClickAt(dialog, centreX, centreY);
                    break;

                case "GuiElementSlider":
                {
                    // A drag, which composes the hover text that follows the handle.
                    int from = (int)(x + w * 0.3), to = (int)(x + w * 0.6);
                    await Input.MouseMove(from, centreY);
                    dialog.OnMouseDown(new MouseEvent(from, centreY, EnumMouseButton.Left, 0));
                    await Frames.Wait(1);
                    await Input.MouseMove((int)(x + w * 0.5), centreY);
                    await Frames.Wait(1);
                    await Input.MouseMove(to, centreY);
                    await Frames.Wait(1);
                    dialog.OnMouseUp(new MouseEvent(to, centreY, EnumMouseButton.Left, 0));
                    break;
                }

                case "GuiElementDropDown":
                    // Open the list, then pick its first entry from the list itself.
                    await ClickAt(dialog, centreX, centreY);
                    await Frames.Wait(2);
                    await ClickAt(dialog, centreX, (int)(y + h + 15 * RuntimeEnv.GUIScale));
                    break;

                case "GuiElementNumberInput":
                case "NullableNumberInput":
                    await ClickAt(dialog, centreX, centreY);
                    await Input.Type("7");
                    await Frames.Wait(1);
                    // The up arrow, which has a highlight texture of its own.
                    await ClickAt(dialog, (int)(x + w - 8), (int)(y + h * 0.25));
                    break;

                case "GuiElementTextInput":
                case "CommittingTextInput":
                    await ClickAt(dialog, centreX, centreY);
                    await Input.Type("x");
                    break;

                case "GuiElementTextArea":
                    await ClickAt(dialog, centreX, centreY);
                    break;

                default:
                    continue;
            }

            await Frames.Wait(2);

            // Back where it was, so the next round sees the same screen - and so a value
            // typed into a real mod's config never reaches its file.
            dialog.ResetSetting(code);
            await Frames.Wait(1);
            actions++;
        }

        // Off the window, so a hover texture is not being held up by the mouse.
        await Input.MouseMove(5, 5);
        await Frames.Wait(1);

        return actions;
    }
}
