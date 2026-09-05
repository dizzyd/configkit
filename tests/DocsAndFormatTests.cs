using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// The three things a config class carries that ConfigKit used to ignore: its doc comments,
/// its display formats, and - for a mod holding more than one config - what to call each of
/// them on screen.
///
/// All three came out of porting TheInsanityGod's InsanityLib off configlib and onto ConfigKit.
/// That corpus documents every setting with a /// comment and nothing else, so 169 real
/// settings arrived with no tooltips at all; several are ratios declared
/// [DisplayFormat(DataFormatString = "P")] that read 0.95 instead of 95%; and WearAndTear holds
/// seven config files, which became seven dropdown entries named after their file paths.
/// </summary>
public class DocsAndFormatTests
{
    // ---------------------------------------------------------------- fixtures

    public class Documented
    {
        /// <summary>How far the thing reaches.</summary>
        public int Radius = 8;

        /// <summary>
        ///     Wrapped across lines,
        ///     indented like real source.
        /// </summary>
        public float Spread = 1f;

        /// <summary>Mentions <see cref="T:Some.Namespace.OtherThing"/> in passing.</summary>
        public bool Linked = true;

        /// <summary>This doc comment loses to the attribute.</summary>
        [Description("The attribute wins.")]
        public int Both = 1;

        public int Undocumented = 1;

        /// <summary>A property, which is keyed P: rather than F:.</summary>
        public string Named { get; set; } = "x";
    }

    /// <summary>The XML a compiler would emit for <see cref="Documented"/>.</summary>
    private static string DocumentationFor()
    {
        string type = typeof(Documented).FullName!.Replace('+', '.');

        return $@"<?xml version=""1.0""?>
<doc>
  <assembly><name>test</name></assembly>
  <members>
    <member name=""F:{type}.Radius""><summary>How far the thing reaches.</summary></member>
    <member name=""F:{type}.Spread"">
      <summary>
          Wrapped across lines,
          indented like real source.
      </summary>
    </member>
    <member name=""F:{type}.Linked""><summary>Mentions <see cref=""T:Some.Namespace.OtherThing""/> in passing.</summary></member>
    <member name=""F:{type}.Both""><summary>This doc comment loses to the attribute.</summary></member>
    <member name=""P:{type}.Named""><summary>A property, which is keyed P: rather than F:.</summary></member>
  </members>
</doc>";
    }

    private static MemberInfo Member(string name) => typeof(Documented).GetMember(name).Single();

    // ---------------------------------------------------------------- doc comments

    /// <summary>
    /// A member's own /// comment becomes its tooltip. Asking an author to repeat that text
    /// in a [Description] is the duplication this library exists to avoid, and a corpus that
    /// documents everything and annotates nothing got no tooltips at all.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ADocCommentBecomesTheTooltip()
    {
        await OnServer();

        string xml = DocumentationFor();

        Assert.Equal("How far the thing reaches.", XmlDocs.SummaryIn(xml, Member("Radius")));

        // A property is keyed P:, a field F:. Getting that wrong silently loses half of any
        // class that mixes them, which most do.
        Assert.Equal("A property, which is keyed P: rather than F:.", XmlDocs.SummaryIn(xml, Member("Named")));

        // Source indentation is not part of the sentence.
        Assert.Equal("Wrapped across lines, indented like real source.", XmlDocs.SummaryIn(xml, Member("Spread")));

        // <see cref="..."/> carries its subject in an attribute, so a naive read drops the
        // word entirely and leaves "Mentions in passing."
        Assert.Equal("Mentions OtherThing in passing.", XmlDocs.SummaryIn(xml, Member("Linked")));

        // Nothing recorded for it.
        Assert.Null(XmlDocs.SummaryIn(xml, Member("Undocumented")));
    }

    /// <summary>
    /// Absent a documentation file - which is every mod that has not opted into
    /// GenerateDocumentationFile, and this very test assembly, compiled in memory - nothing
    /// happens. The point of the fallback is that it is free when unavailable.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task NoDocumentationFileCostsNothing()
    {
        await OnServer();

        // This assembly is compiled by the game's Roslyn and has no file on disk.
        Assert.Null(XmlDocs.Summary(Member("Radius")));
        Assert.Null(XmlDocs.SummaryIn("not xml at all", Member("Radius")));

        Config config = new(Capi, "ckdocs", "Docs", new Documented(), "ck-docs.json");
        Assert.Equal(6, config.SettingCodes.Count());
    }

    /// <summary>
    /// [Description] outranks the doc comment. One was written to be shown and the other to
    /// be read in the source, and a class carrying both means the author chose.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AnExplicitDescriptionOutranksTheDocComment()
    {
        await OnServer();

        Config config = new(Capi, "ckdocs2", "Docs", new Documented(), "ck-docs2.json");

        Assert.Equal("The attribute wins.", ((ConfigSetting)config.GetSetting("Both")!).Comment);
    }

    // ---------------------------------------------------------------- display format

    public class Formatted
    {
        [Range(0.0, 1.0)]
        [DisplayFormat(DataFormatString = "P")]
        public float Ratio = 0.95f;

        [Range(0.0, 1.0)]
        public float Plain = 0.95f;

        // Open bound, so this is a number input rather than a slider - the control a player
        // types into, which must never show a formatted value.
        [Range(0.0, double.PositiveInfinity)]
        [DisplayFormat(DataFormatString = "P")]
        public float Unbounded = 0.5f;

        [Range(0.0, 1.0)]
        [DisplayFormat(DataFormatString = "this is not a format string")]
        public float Broken = 0.25f;
    }

    /// <summary>
    /// A ratio declared [DisplayFormat(DataFormatString = "P")] reads as a percentage beside
    /// its slider, which is how its author describes it everywhere except in the file.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task ADisplayFormatReachesTheReadout()
    {
        await OnClient();

        Config config = new(Capi, "ckfmt", "Format", new Formatted(), "ck-fmt.json");
        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["ckfmt"] = config });
        dialog.TryOpen();
        await Frames.Wait(8);

        try
        {
            // Exactly what .NET's "P" produces under InvariantCulture, which is what an
            // author who wrote that format string gets everywhere else. The separator before
            // the sign is a non-breaking space; which space it is, is culture data rather
            // than anything this library decides.
            string ratio = dialog.SliderValueTexts["Ratio"].Replace('\u00a0', ' ');
            Assert.Equal("95.00 %", ratio);

            // Undeclared, so untouched.
            Assert.Equal("0.95", dialog.SliderValueTexts["Plain"]);

            // A typo'd format does not throw: .NET reads anything it does not recognise as a
            // custom format, where a plain letter stands for itself, so this one rendered the
            // number as its own text with no digits left. The formatting is what gets
            // dropped, never the number.
            Assert.Equal("0.25", dialog.SliderValueTexts["Broken"]);
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// The value stored is never formatted, and neither is the box a player types into.
    /// Showing "95.00%" in an editable field means parsing it back, and a half-typed one has
    /// to parse too - which is why DisplayFormat.ApplyFormatInEditMode is not honoured.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task AFormatNeverReachesTheStoredValueOrTheInput()
    {
        await OnClient();

        Formatted settings = new();
        Config config = new(Capi, "ckfmt2", "Format", settings, "ck-fmt2.json");

        Assert.Equal("0.95", config.GetSetting("Ratio")!.Value.Token!.ToString());

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["ckfmt2"] = config });
        dialog.TryOpen();
        await Frames.Wait(8);

        try
        {
            // An open bound gets the number input, which stays raw whatever the format says.
            Assert.Equal("GuiElementNumberInput", dialog.ControlKindFor("Unbounded"));
        }
        finally
        {
            dialog.TryClose();
        }

        config.WriteToFile();
        Assert.True(config.ReadFromFile(), "the config would not read back what it wrote");
        Assert.Equal("0.95", config.GetSetting("Ratio")!.Value.Token!.ToString());
    }

    // ---------------------------------------------------------------- display name

    /// <summary>
    /// A mod holding several configs needs a domain each, and none of them is its mod id -
    /// so the dropdown fell back to the raw domain and a player chose between strings like
    /// "wearandtear-server-mainconfig". A registration may name itself instead.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task ARegistrationMayNameItselfForTheDropdown()
    {
        await OnClient();

        ConfigKitModSystem system = Capi.ModLoader.GetModSystem<ConfigKitModSystem>();

        system.RegisterManagedConfig("ckname-server", new Documented(), "ck-name-server.json");
        system.SetConfigDisplayName("ckname-server", "Named Mod: Server");

        system.RegisterManagedConfig("ckname-client", new Documented(), "ck-name-client.json");
        system.SetConfigDisplayName("ckname-client", "Named Mod: Client");

        Config server = (Config)system.GetConfig("ckname-server")!;
        Config client = (Config)system.GetConfig("ckname-client")!;

        ConfigDialog dialog = new(Capi, new Dictionary<string, Config>
        {
            ["ckname-server"] = server,
            ["ckname-client"] = client,
        });

        dialog.TryOpen();
        await Frames.Wait(4);

        try
        {
            // Sorted by the name a player reads, so a mod's configs sit together rather than
            // wherever their domains happened to fall.
            Assert.Equal("Named Mod: Client,Named Mod: Server", string.Join(",", dialog.DisplayNames));
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// RegisterManagedConfig keeps the signature it shipped with.
    ///
    /// This is not pedantry. Adding an optional parameter to it is source compatible and
    /// binary incompatible: a mod compiled against the previous release emits a call naming
    /// the exact six-parameter signature, which then does not exist. It throws at
    /// registration, and because InsanityLib registers a mod's configs in a loop, one throw
    /// left every WearAndTear config after the first null and took the server down at
    /// startup. That is how this test came to exist.
    ///
    /// Anything new goes on a method of its own, as SetConfigDisplayName did.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task TheRegistrationSignatureDoesNotDrift()
    {
        await OnServer();

        MethodInfo? register = typeof(ConfigKitModSystem).GetMethod(
            nameof(ConfigKitModSystem.RegisterManagedConfig));

        Assert.NotNull(register);

        string shape = string.Join(",", register!.GetParameters().Select(p => p.ParameterType.Name));
        Assert.Equal("String,Object,String,Action,Action`1,Action", shape);

        // configlib's own name for it, which several mods look up by reflection and match on
        // the full parameter list.
        MethodInfo? alias = typeof(ConfigKitModSystem).GetMethod(
            nameof(ConfigKitModSystem.RegisterCustomManagedConfig));

        Assert.NotNull(alias);
        Assert.Equal(shape, string.Join(",", alias!.GetParameters().Select(p => p.ParameterType.Name)));
    }
}
