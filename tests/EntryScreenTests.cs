using System;
using System.Collections.Generic;
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
/// The screen for one entry of a container - a class inside a dictionary or a list - gets
/// the same treatment a root row does.
///
/// Reported by TheInsanityGod as three things: no sliders in the sub config, validation not
/// going over nested entries, and nullable numbers jumping back to 0. They were one thing.
/// The entry screens built their own controls - a number input for every number, a text
/// input for the rest - and so had none of what the root screen had learned: the slider for
/// a [Range], the range as a constraint, the empty box that means null.
///
/// A fourth: a dictionary keyed by an enum offered a text box for the key, and a name the
/// enum did not have was written to the file and thrown away on the next load, with the
/// exception swallowed and nothing on screen.
/// </summary>
[SingleplayerOnly]
public class EntryScreenTests
{
    public enum Tier { Low, Mid, High }

    /// <summary>Backed by a byte: casting its values to int[] threw, at registration for a root member.</summary>
    public enum Size : byte { S, M, L }

    [Flags]
    public enum Perms { Read = 1, Write = 2 }

    /// <summary>Two names for one value. A file saved under either has to keep reading.</summary>
    public enum Mode { Off = 0, On = 1, Enabled = 1 }

    public class Rule
    {
        [Key]
        public string Code = "";

        public Size Size = Size.M;

        /// <summary>A nullable enum: "(unset)" has to write null, not the key it had before.</summary>
        public Tier? Optional;

        /// <summary>Added after files were written: an entry without it must read as 5, not null.</summary>
        [Range(1, 10)]
        public int Added = 5;

        /// <summary>A closed range: a slider on the root screen, so a slider here.</summary>
        [Range(0.0, 1.0)]
        public float Chance = 0.5f;

        /// <summary>An open bound: not a slider, but still a constraint.</summary>
        [Range(0.0, double.PositiveInfinity)]
        public float Weight = 1f;

        /// <summary>WearAndTear's shape, one level down: null means "no limit".</summary>
        [Range(0.0, double.PositiveInfinity)]
        public float? Limit;

        public Tier Tier = Tier.Mid;

        public bool On = true;
    }

    public class Settings
    {
        public bool Enabled = true;

        public Dictionary<string, Rule> Rules = new()
        {
            ["one"] = new Rule { Code = "one" },
            ["two"] = new Rule { Code = "two", Chance = 0.8f, Tier = Tier.High, Limit = 4f }
        };

        public Dictionary<Tier, float> ByTier = new() { [Tier.Low] = 1f, [Tier.Mid] = 2f };

        public Dictionary<int, string> ByNumber = new() { [7] = "seven" };

        public Dictionary<Perms, int> ByPerm = new() { [Perms.Read] = 1 };

        public Dictionary<ulong, string> ByBig = new() { [1] = "a" };

        public Dictionary<byte, string> BySmall = new() { [1] = "a" };

        public Size RootSize = Size.L;

        public Mode Mode = Mode.Off;

        public List<Rule> Pool = new() { new Rule { Code = "first" } };
    }

    // ---------------------------------------------------------------- helpers

    private static void Delete(string file)
    {
        string path = Path.Combine(Capi.DataBasePath, "ModConfig", file);
        if (File.Exists(path)) File.Delete(path);
    }

    private static (Config config, Settings settings) Build(string domain)
    {
        string file = $"configkit-{domain}.json";
        Delete(file);

        Settings settings = new();
        Config config = new(Capi, domain, domain, settings, file);
        config.AssignSettingsValues(settings);
        return (config, settings);
    }

    private static (ConfigDialog dialog, Settings settings) Open(string domain)
    {
        (Config config, Settings settings) = Build(domain);

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { [domain] = config });
        dialog.TryOpen();
        return (dialog, settings);
    }

    /// <summary>A press and release handed to the dialog, as the game's GUI system does it.</summary>
    private static async Task ClickAt(ConfigDialog dialog, double x, double y)
    {
        await Input.MouseMove((int)x, (int)y);
        await Frames.Wait(1);
        dialog.OnMouseDown(new Vintagestory.API.Client.MouseEvent((int)x, (int)y, Vintagestory.API.Common.EnumMouseButton.Left, 0));
        await Frames.Wait(2);
        dialog.OnMouseUp(new Vintagestory.API.Client.MouseEvent((int)x, (int)y, Vintagestory.API.Common.EnumMouseButton.Left, 0));
        await Frames.Wait(1);
    }

    /// <summary>Opens a dropdown and picks its entry at this index, through the mouse.</summary>
    private static async Task Pick(ConfigDialog dialog, string code, int index)
    {
        var rect = dialog.ScreenRectFor(code);
        Assert.NotNull(rect, $"no control on screen for {code}");
        (double x, double y, double w, double h) = rect!.Value;

        await ClickAt(dialog, x + w / 2, y + h / 2);
        await Frames.Wait(2);
        await ClickAt(dialog, x + w / 2, y + h + (index + 0.5) * 30 * Vintagestory.API.Config.RuntimeEnv.GUIScale);
        await Frames.Wait(2);
    }

    private static async Task<(ConfigDialog dialog, Settings settings)> OpenEntry(string domain, string entry)
    {
        (ConfigDialog dialog, Settings settings) = Open(domain);
        await Frames.Wait(6);

        Assert.True(dialog.OpenSetting("Rules"), "the dictionary would not open");
        await Frames.Wait(4);
        Assert.True(dialog.OpenEntry(entry), "the entry would not open");
        await Frames.Wait(6);

        return (dialog, settings);
    }

    // ---------------------------------------------------------------- controls

    /// <summary>
    /// A field of an entry gets the control its attributes earn, exactly as it would at the
    /// root: a slider for a closed range, a number input for an open one, a dropdown for an
    /// enum, a switch for a bool.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AnEntrysFieldsGetTheRootScreensControls()
    {
        await OnClient();

        (ConfigDialog dialog, _) = await OpenEntry("entry-controls", "two");

        try
        {
            Assert.Equal("GuiElementSlider", dialog.ControlKindFor("Chance"));
            Assert.Equal("GuiElementNumberInput", dialog.ControlKindFor("Weight"));
            Assert.Equal("GuiElementDropDown", dialog.ControlKindFor("Tier"));
            Assert.Equal("GuiElementSwitch", dialog.ControlKindFor("On"));
            Assert.Equal("GuiElementTextInput", dialog.ControlKindFor("Code"));

            // And the slider reads the entry's own value, not the class default.
            Assert.Equal("0.8", dialog.SliderValueTexts["Chance"]);
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// Newtonsoft writes an enum inside the author's object as its number, and the entry's
    /// dropdown used to look that number up as a name, find nothing, and show the first
    /// member - "Low" for an entry that said High.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AnEnumStoredAsANumberShowsItsMember()
    {
        await OnClient();

        (ConfigDialog dialog, Settings settings) = await OpenEntry("entry-enum", "two");

        try
        {
            Assert.Equal("High", dialog.DropdownValueFor("Tier"));

            // Round trip: the container is written back by name and read as the member.
            ConfigSetting tier = (ConfigSetting)dialog.RenderedSettings.Values.First(s => s.YamlCode == "Tier");
            tier.MappingKey = "Low";
            await Frames.Wait(2);

            Assert.Equal(Tier.Low, settings.Rules["two"].Tier);
        }
        finally
        {
            dialog.TryClose();
        }
    }

    // ---------------------------------------------------------------- validation

    /// <summary>
    /// A [Range] on a field of the class a dictionary holds constrains that field in every
    /// entry. The bad value stays on screen to be fixed, the row and the status line both
    /// say so, and the object keeps the last value the attribute agreed to.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AnEntrysFieldIsValidatedAgainstItsOwnAttributes()
    {
        await OnClient();

        (ConfigDialog dialog, Settings settings) = await OpenEntry("entry-valid", "two");

        try
        {
            Assert.Null(dialog.RowErrorFor("Weight"));
            Assert.Equal("", dialog.ErrorText);

            Assert.True(dialog.TypeInto("Weight", "-5"), "no control to type into");
            await Frames.Wait(2);

            Assert.NotNull(dialog.RowErrorFor("Weight"), "the row said nothing about a rejected value");
            Assert.Contains(dialog.ErrorText, "two");
            Assert.Contains(dialog.ErrorText, "Weight");
            Assert.Close(1f, settings.Rules["two"].Weight, 0.001f, "a refused value reached the object");

            Assert.True(dialog.TypeInto("Weight", "5"));
            await Frames.Wait(2);

            Assert.Null(dialog.RowErrorFor("Weight"));
            Assert.Equal("", dialog.ErrorText);
            Assert.Close(5f, settings.Rules["two"].Weight, 0.001f);
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// The same check in code, with no screen involved: the container reports the failure
    /// and the whole container is held back from the object until it is fixed.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AContainerReportsAFailingEntry()
    {
        await OnServer();

        (Config config, Settings settings) = Build("entry-valid-code");
        if (config.Errors.Count != 0)
        {
            Assert.Fail("a sound config reported: " + string.Join("; ", config.Errors.Select(e => $"{e.Key}={e.Value}")));
        }

        JObject rules = (JObject)config.GetSetting("Rules")!.Value.Token.DeepClone();
        rules["two"]!["Chance"] = 7f;
        config.GetSetting("Rules")!.Value = new JsonObject(rules);

        Assert.Equal(1, config.Errors.Count);
        Assert.Contains(config.Errors["Rules"], "two > Chance");
        Assert.Close(0.8f, settings.Rules["two"].Chance, 0.001f, "a refused value reached the object");

        // A fresh copy: editing the token the setting now holds is invisible to it.
        rules = (JObject)rules.DeepClone();
        rules["two"]!["Chance"] = 0.2f;
        config.GetSetting("Rules")!.Value = new JsonObject(rules);

        Assert.Equal(0, config.Errors.Count);
        Assert.Close(0.2f, settings.Rules["two"].Chance, 0.001f);
    }

    // ---------------------------------------------------------------- nulls

    /// <summary>
    /// A nullable inside an entry reads empty when null, clears back to null, and stays
    /// empty when the box loses focus - which is where the stock input puts a 0.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ANullableInsideAnEntryStaysNull()
    {
        await OnClient();

        (ConfigDialog dialog, Settings settings) = await OpenEntry("entry-null", "one");

        try
        {
            Assert.Equal("NullableNumberInput", dialog.ControlKindFor("Limit"));
            Assert.Equal("", dialog.NumberTextFor("Limit"));
            Assert.Null(settings.Rules["one"].Limit);

            Assert.True(dialog.TypeInto("Limit", "3"));
            await Frames.Wait(2);
            Assert.Close(3f, settings.Rules["one"].Limit ?? -1f, 0.001f);

            Assert.True(dialog.TypeInto("Limit", ""));
            await Frames.Wait(2);
            Assert.Null(settings.Rules["one"].Limit, "clearing the box did not put the null back");

            Assert.True(dialog.Blur("Limit"));
            await Frames.Wait(2);

            Assert.Equal("", dialog.NumberTextFor("Limit"), "losing focus put a number back into the box");
            Assert.Null(settings.Rules["one"].Limit, "losing focus turned the null into a number");
        }
        finally
        {
            dialog.TryClose();
        }
    }

    // ---------------------------------------------------------------- keys

    /// <summary>
    /// A dictionary keyed by an enum offers its members for a key rather than a text box -
    /// the members not already taken, plus the entry's own.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AnEnumKeyIsChosenNotTyped()
    {
        await OnClient();

        (ConfigDialog dialog, Settings settings) = Open("entry-enumkey");
        await Frames.Wait(6);

        try
        {
            Assert.True(dialog.OpenSetting("ByTier"));
            await Frames.Wait(6);

            Assert.Equal("GuiElementDropDown", dialog.KeyControlKindFor("Low"));

            // A name the enum does not have is refused, and the entry is left alone.
            Assert.False(dialog.RenameEntry("Low", "Bogus"), "a key the enum does not have was accepted");
            await Frames.Wait(2);
            Assert.True(settings.ByTier.ContainsKey(Tier.Low), "the refused rename took the entry with it");

            // A member spelled carelessly lands on the member.
            Assert.True(dialog.RenameEntry("Low", "high"), "a member of the enum was refused");
            await Frames.Wait(4);
            Assert.Equal("High,Mid", string.Join(",", dialog.EntryLabels.OrderBy(label => label)));
            Assert.True(settings.ByTier.ContainsKey(Tier.High));
            Assert.Close(1f, settings.ByTier[Tier.High], 0.001f);
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>A dictionary keyed by a number refuses a key that is not one.</summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ANumericKeyRefusesText()
    {
        await OnClient();

        (ConfigDialog dialog, Settings settings) = Open("entry-numkey");
        await Frames.Wait(6);

        try
        {
            Assert.True(dialog.OpenSetting("ByNumber"));
            await Frames.Wait(6);

            Assert.False(dialog.RenameEntry("7", "seven"), "text was accepted as a numeric key");
            await Frames.Wait(2);
            Assert.True(settings.ByNumber.ContainsKey(7));

            Assert.True(dialog.RenameEntry("7", " 8 "));
            await Frames.Wait(4);
            Assert.Equal("seven", settings.ByNumber[8]);
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>A list of classes gets the same screen as a dictionary's entry does.</summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AListElementGetsTheSameScreen()
    {
        await OnClient();

        (ConfigDialog dialog, Settings settings) = Open("entry-list");
        await Frames.Wait(6);

        try
        {
            Assert.True(dialog.OpenSetting("Pool"));
            await Frames.Wait(4);
            Assert.True(dialog.OpenEntry("first"));
            await Frames.Wait(6);

            Assert.Equal("GuiElementSlider", dialog.ControlKindFor("Chance"));

            Assert.True(dialog.TypeInto("Weight", "-1"));
            await Frames.Wait(2);
            Assert.NotNull(dialog.RowErrorFor("Weight"));
            Assert.Contains(dialog.ErrorText, "#0");
        }
        finally
        {
            dialog.TryClose();
        }
    }

    // ---------------------------------------------------------------- from review

    /// <summary>
    /// "(unset)" on a nullable enum writes null. The value went null but the mapping key
    /// stayed, and a mapped setting is written and assigned through its key - so the file and
    /// the object kept the old member while the dropdown read "(unset)".
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task UnsettingANullableEnumInsideAnEntryWritesNull()
    {
        await OnClient();

        (ConfigDialog dialog, Settings settings) = await OpenEntry("entry-unset", "two");

        try
        {
            ConfigSetting optional = (ConfigSetting)dialog.RenderedSettings.Values.First(s => s.YamlCode == "Optional");
            optional.MappingKey = "High";
            await Frames.Wait(2);
            Assert.Equal(Tier.High, settings.Rules["two"].Optional);

            // The first entry of a nullable dropdown is "(unset)".
            await Pick(dialog, "Optional", 0);

            Assert.Equal("(unset)", dialog.DropdownValueFor("Optional"));
            Assert.Null(settings.Rules["two"].Optional, "the object kept the old member");

            Config config = (Config)dialog.Configs["entry-unset"];
            JToken? stored = config.GetSetting("Rules")!.Value.Token?["two"]?["Optional"];
            Assert.True(stored == null || stored.Type == JTokenType.Null, $"the subtree holds {stored}");
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>An enum backed by a byte registers, and gets its dropdown at the root and inside an entry.</summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AByteBackedEnumGetsADropdownEverywhere()
    {
        await OnClient();

        (ConfigDialog dialog, _) = Open("entry-byteenum");
        await Frames.Wait(6);

        try
        {
            Assert.Equal("GuiElementDropDown", dialog.ControlKindFor("RootSize"));
            Assert.Equal("L", dialog.DropdownValueFor("RootSize"));

            Assert.True(dialog.OpenSetting("Rules"));
            await Frames.Wait(4);
            Assert.True(dialog.OpenEntry("two"));
            await Frames.Wait(6);

            Assert.Equal("GuiElementDropDown", dialog.ControlKindFor("Size"));
            Assert.Equal("M", dialog.DropdownValueFor("Size"));
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// A field added to the class after a file was written is absent from every stored entry.
    /// Deserialising gives it the class's own initialiser, so that is what validation checks
    /// and what the screen shows - not null, which failed the range, and not 0. And an entry
    /// stored as null is a value with no fields in it to check.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AnAbsentFieldAndANullEntryAreNotErrors()
    {
        await OnClient();

        string domain = "entry-absent";
        string file = $"configkit-{domain}.json";
        Delete(file);
        File.WriteAllText(Path.Combine(Capi.DataBasePath, "ModConfig", file),
            @"{ ""Rules"": { ""old"": { ""Code"": ""old"", ""Chance"": 0.3 }, ""gone"": null } }");

        Settings settings = new();
        Config config = new(Capi, domain, domain, settings, file);
        config.AssignSettingsValues(settings);

        if (config.Errors.Count != 0)
        {
            Assert.Fail("a sound file reported: " + string.Join("; ", config.Errors.Select(e => $"{e.Key}={e.Value}")));
        }

        Assert.Equal(5, settings.Rules["old"].Added);
        Assert.True(settings.Rules.ContainsKey("gone") && settings.Rules["gone"] == null, "the null entry was not kept as null");

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { [domain] = config });
        dialog.TryOpen();
        await Frames.Wait(6);

        try
        {
            Assert.True(dialog.OpenSetting("Rules"));
            await Frames.Wait(4);
            Assert.True(dialog.OpenEntry("old"));
            await Frames.Wait(6);

            // A closed range, so a slider; its readout is where the number is shown.
            Assert.Equal("GuiElementSlider", dialog.ControlKindFor("Added"));
            Assert.Equal("5", dialog.SliderValueTexts["Added"]);
            Assert.Null(dialog.RowErrorFor("Added"));
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// A [Flags] enum's keys are combinations, so they are typed rather than chosen, and a
    /// combination of members is accepted - a bare number is not.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task AFlagsKeyAcceptsACombination()
    {
        await OnClient();

        (ConfigDialog dialog, Settings settings) = Open("entry-flagskey");
        await Frames.Wait(6);

        try
        {
            Assert.True(dialog.OpenSetting("ByPerm"));
            await Frames.Wait(6);

            Assert.Equal("CommittingTextInput", dialog.KeyControlKindFor("Read"));

            Assert.True(dialog.RenameEntry("Read", "read, write"), "a combination of members was refused");
            await Frames.Wait(4);
            Assert.Equal("Read, Write", string.Join(",", dialog.EntryLabels));
            Assert.True(settings.ByPerm.ContainsKey(Perms.Read | Perms.Write));

            Assert.False(dialog.RenameEntry("Read, Write", "7"), "a bare number was accepted as a flags key");
            Assert.False(dialog.RenameEntry("Read, Write", "Read, Execute"), "a name the enum lacks was accepted");
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>A numeric key is checked against its own type: a ulong past long.MaxValue is fine, 300 is not a byte.</summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    public async Task ANumericKeyIsCheckedAgainstItsOwnType()
    {
        await OnClient();

        (ConfigDialog dialog, Settings settings) = Open("entry-numtypes");
        await Frames.Wait(6);

        try
        {
            Assert.True(dialog.OpenSetting("ByBig"));
            await Frames.Wait(6);
            Assert.True(dialog.RenameEntry("1", "18446744073709551615"), "ulong.MaxValue was refused");
            await Frames.Wait(4);
            Assert.True(settings.ByBig.ContainsKey(ulong.MaxValue));
            dialog.Back();
            await Frames.Wait(4);

            Assert.True(dialog.OpenSetting("BySmall"));
            await Frames.Wait(6);
            Assert.False(dialog.RenameEntry("1", "300"), "300 was accepted as a byte");
            Assert.True(dialog.RenameEntry("1", " 255 "));
            await Frames.Wait(4);
            Assert.True(settings.BySmall.ContainsKey(255));
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// An enum with two names for one value keeps both in its mapping, so a file saved under
    /// the alias reads back as that member rather than falling to the default.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AnAliasedEnumMemberStillReadsFromTheFile()
    {
        await OnServer();

        string domain = "entry-alias";
        string file = $"configkit-{domain}.json";
        Delete(file);
        File.WriteAllText(Path.Combine(Capi.DataBasePath, "ModConfig", file), @"{ ""Mode"": ""Enabled"" }");

        Settings settings = new();
        Config config = new(Capi, domain, domain, settings, file);
        config.AssignSettingsValues(settings);

        ConfigSetting mode = (ConfigSetting)config.GetSetting("Mode")!;
        Assert.Equal("Enabled", mode.MappingKey);
        Assert.Equal(Mode.On, settings.Mode);
        Assert.True(mode.Validation?.Mapping?.ContainsKey("On") == true, "the other name for the value is gone");
    }

    /// <summary>
    /// The plain case the alias test builds on, and it was broken too: a managed config built
    /// over an existing file never turned the saved name back into its key, so the object got
    /// the default - and the constructor's own save wrote the default back over the file. A
    /// number, which is what a file the mod wrote itself before ConfigKit holds, reads too.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ANamedEnumMemberReadsFromTheFile()
    {
        await OnServer();

        foreach ((string stored, string tag) in new[] { ("\"On\"", "name"), ("1", "number") })
        {
            string domain = $"entry-named-{tag}";
            string file = $"configkit-{domain}.json";
            Delete(file);
            File.WriteAllText(Path.Combine(Capi.DataBasePath, "ModConfig", file), $"{{ \"Mode\": {stored} }}");

            Settings settings = new();
            Config config = new(Capi, domain, domain, settings, file);
            config.AssignSettingsValues(settings);

            Assert.Equal("On", ((ConfigSetting)config.GetSetting("Mode")!).MappingKey, $"stored as a {tag}");
            Assert.Equal(Mode.On, settings.Mode, $"stored as a {tag}");

            // And the constructor's save kept it, rather than writing the default back.
            Assert.Contains(File.ReadAllText(Path.Combine(Capi.DataBasePath, "ModConfig", file)), "\"On\"");
        }
    }
}
