using System.Collections.Generic;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// A player can click a text field and type in it, and those keystrokes do not also reach the
/// game.
///
/// Two reports from the compatibility pack, one cause. MoreHudBars' "Hud bar style" would not
/// accept typing; and with the settings window open, W and S still walked the character
/// around.
///
/// GuiElementContainer forwards key events only when the container itself has focus, and
/// clicking a row focuses the child rather than the container. So every text field in the
/// library could be focused and show a caret while swallowing every keystroke - and because
/// nothing ever set args.Handled, the same keystroke went on to the game's movement controls.
///
/// Nothing caught it because every other test drives values through the model, where the
/// widget's own handler was always fine.
/// </summary>
public class TypingTests
{
    public class Settings
    {
        public string Label = "start";
    }

    private static ConfigDialog Open(string domain, object settings, out Config config)
    {
        config = new Config(Capi, domain, "Typing", settings, domain + ".json");
        ConfigDialog dialog = new(Capi, new Dictionary<string, Config> { [domain] = config });
        dialog.TryOpen();
        return dialog;
    }

    /// <summary>
    /// Click the field, then send a character, exactly as the dialog receives them in game.
    /// Deliberately not through the widget's handler - the widget was never the problem.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task ClickingATextFieldLetsYouTypeInIt()
    {
        await OnClient();

        Settings settings = new();
        ConfigDialog dialog = Open("cktyping", settings, out Config _);
        await Frames.Wait(8);

        try
        {
            (double X, double Y, double Width, double Height)? rect = dialog.ScreenRectFor("Label");
            Assert.NotNull(rect);

            MouseEvent click = new(
                (int)(rect!.Value.X + rect.Value.Width / 2),
                (int)(rect.Value.Y + rect.Value.Height / 2),
                0, 0, EnumMouseButton.Left, 0);

            dialog.OnMouseDown(click);
            Assert.True(click.Handled, "the click did not reach the field");

            KeyEvent typed = new() { KeyChar = 'X' };
            dialog.OnKeyPress(typed);
            await Frames.Wait(2);

            Assert.True(settings.Label != "start",
                $"the keystroke never reached the focused field. Still '{settings.Label}'");
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// The window owns the keyboard while it is open.
    ///
    /// A contract test rather than a behavioural one: the engine, not ConfigKit, decides what
    /// reaches the game's controls, and it decides it from this. Without it W and S walked
    /// the player around behind an open settings window.
    /// </summary>
    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task TheWindowOwnsTheKeyboardWhileOpen()
    {
        await OnClient();

        ConfigDialog dialog = Open("cktyping3", new Settings(), out Config _);

        try
        {
            Assert.True(dialog.CaptureAllInputs(),
                "the game will keep reading movement keys behind the settings window");
        }
        finally
        {
            dialog.TryClose();
        }
    }

    /// <summary>
    /// And the keystroke is consumed. An unhandled key carries on to the game's controls,
    /// which is why typing in the settings window also walked the player forwards.
    /// </summary>
    [VsTest(TimeoutMs = 90000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task TypingDoesNotAlsoReachTheGame()
    {
        await OnClient();

        Settings settings = new();
        ConfigDialog dialog = Open("cktyping2", settings, out Config _);
        await Frames.Wait(8);

        try
        {
            (double X, double Y, double Width, double Height) rect = dialog.ScreenRectFor("Label")!.Value;

            dialog.OnMouseDown(new MouseEvent(
                (int)(rect.X + rect.Width / 2), (int)(rect.Y + rect.Height / 2),
                0, 0, EnumMouseButton.Left, 0));

            // 'w' is the one that matters: unconsumed, it is a step forward.
            KeyEvent typed = new() { KeyChar = 'w' };
            dialog.OnKeyPress(typed);

            Assert.True(typed.Handled, "the settings window let a keystroke through to the game");
        }
        finally
        {
            dialog.TryClose();
        }
    }
}
