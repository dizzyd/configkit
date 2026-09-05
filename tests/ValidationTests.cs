using System;
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
/// The author's own validation attributes, run, with the messages they wrote.
///
/// Reported by InsanityLib's author, who supports these and noted ConfigKit did not. It read
/// [Range] as a pair of slider bounds and nothing else, so a range constrained nothing
/// wherever a slider was not the control - and [Range(0, double.PositiveInfinity)], which is
/// how "this cannot be negative" gets written, is exactly such a case, because an open bound
/// takes the number input. Typing -5 into it stuck.
///
/// A value that fails is kept on the setting, so the player sees what they typed and can fix
/// it, and is deliberately not assigned onto the config object - the mod keeps the last value
/// its own attributes agreed to.
/// </summary>
public class ValidationTests
{
    // ---------------------------------------------------------------- fixtures

    /// <summary>Someone else's validator, which is the case that has to work by itself.</summary>
    public class EvenAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object? value, ValidationContext context)
            => value is int number && number % 2 != 0
                ? new ValidationResult($"{context.DisplayName} must be even")
                : ValidationResult.Success!;
    }

    public class ThrowingAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object? value, ValidationContext context)
            => throw new InvalidOperationException("validator exploded");
    }

    public class Constrained
    {
        /// <summary>WearAndTear's shape: an open upper bound saying only "not negative".</summary>
        [Range(0d, double.PositiveInfinity)]
        public float Limit { get; set; } = 1f;

        [Range(1, 10, ErrorMessage = "Pick a number of doors between 1 and 10")]
        public int Doors { get; set; } = 4;

        [StringLength(5)]
        public string Tag { get; set; } = "abc";

        [Even]
        public int Pairs { get; set; } = 2;

        [Throwing]
        public int Cursed { get; set; } = 1;

        public int Unconstrained { get; set; } = 1;
    }

    private static Config Build(string domain, Constrained settings)
        => new(Capi, domain, "Validation", settings, domain + ".json");

    private static void Set(Config config, string code, JValue value)
        => config.GetSetting(code)!.Value = new JsonObject(value);

    // ---------------------------------------------------------------- the reported case

    /// <summary>
    /// An open [Range] bound is a constraint, not decoration. This is the loose end left by
    /// the fix that stopped an infinite bound crashing the client: the control became right
    /// and the bound still did nothing.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AnOpenRangeBoundStillRejectsANegative()
    {
        await OnServer();

        Constrained settings = new();
        Config config = Build("ckval1", settings);

        Set(config, "Limit", new JValue(-5f));

        ConfigSetting limit = (ConfigSetting)config.GetSetting("Limit")!;
        Assert.NotNull(limit.Error);

        // The mod keeps the value its own attribute agreed to.
        Assert.Equal(1f, settings.Limit);

        // And a good value goes through and clears the error.
        Set(config, "Limit", new JValue(7f));
        Assert.Null(limit.Error);
        Assert.Equal(7f, settings.Limit);
    }

    /// <summary>The author's own message, not one this library invented.</summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task TheMessageIsTheOneTheAuthorWrote()
    {
        await OnServer();

        Config config = Build("ckval2", new Constrained());

        Set(config, "Doors", new JValue(99));

        Assert.Equal("Pick a number of doors between 1 and 10",
            ((ConfigSetting)config.GetSetting("Doors")!).Error);
    }

    /// <summary>
    /// Any ValidationAttribute, including one the author wrote themselves. This is the part
    /// that cannot be faked by special-casing the handful of attributes in the BCL.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task ACustomValidatorRuns()
    {
        await OnServer();

        Constrained settings = new();
        Config config = Build("ckval3", settings);

        Set(config, "Pairs", new JValue(3));
        Assert.Equal("Pairs must be even", ((ConfigSetting)config.GetSetting("Pairs")!).Error);
        Assert.Equal(2, settings.Pairs);

        Set(config, "Pairs", new JValue(8));
        Assert.Null(((ConfigSetting)config.GetSetting("Pairs")!).Error);
        Assert.Equal(8, settings.Pairs);
    }

    /// <summary>
    /// A validator that throws is a bug in someone else's mod, running on every keystroke
    /// inside the GUI's event handling. It is reported rather than allowed to escape.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AValidatorThatThrowsIsReportedNotPropagated()
    {
        await OnServer();

        Config config = Build("ckval4", new Constrained());

        Set(config, "Cursed", new JValue(3));

        string? error = ((ConfigSetting)config.GetSetting("Cursed")!).Error;
        Assert.NotNull(error);
        Assert.True(error!.Contains("validator exploded"), $"lost the reason: {error}");
    }

    /// <summary>
    /// Other attributes work, a member with none is never blocked, and the whole thing stays
    /// quiet when the config is sound - which is the normal case and the one that must not
    /// have become slower or noisier.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    public async Task AGoodConfigReportsNothing()
    {
        await OnServer();

        Constrained settings = new();
        Config config = Build("ckval5", settings);

        Assert.Equal(0, config.Errors.Count);

        Set(config, "Unconstrained", new JValue(-9999));
        Assert.Equal(-9999, settings.Unconstrained);
        Assert.Equal(0, config.Errors.Count);

        Set(config, "Tag", new JValue("far too long"));
        Assert.Equal(1, config.Errors.Count);
        Assert.Equal("abc", settings.Tag);
    }

    /// <summary>
    /// The screen says so. An error appears and clears as the player types, without the
    /// window recomposing under their cursor.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task TheScreenShowsTheError()
    {
        await OnClient();

        Config config = Build("ckval6", new Constrained());
        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { ["ckval6"] = config });
        dialog.TryOpen();
        await Frames.Wait(8);

        try
        {
            Assert.Equal("", dialog.ErrorText);

            dialog.TypeInto("Limit", "-5");
            await Frames.Wait(2);

            Assert.True(dialog.ErrorText.Length > 0, "the screen said nothing about a rejected value");

            dialog.TypeInto("Limit", "5");
            await Frames.Wait(2);

            Assert.Equal("", dialog.ErrorText);
        }
        finally
        {
            dialog.TryClose();
        }
    }
}
