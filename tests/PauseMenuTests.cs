using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConfigKit;
using ConfigKit.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

/// <summary>
/// Issue #3: opening settings is not enough. The real pause menu must remain behind
/// it for drawing AND input, and closing settings must not resume the world.
/// </summary>
public class PauseMenuTests
{
    // Pausing also stops server ticks, so Input.Press/Ticks would hang these tests.
    private static async Task Press(GlKeys key)
    {
        await Input.KeyDown(key);
        await Input.KeyUp(key);
        await Frames.Wait(3);
    }

    private static async Task Click(double x, double y)
    {
        await Input.MouseMove((int)x, (int)y);
        await Input.RawMouseDown();
        await Frames.Wait(2);
        await Input.RawMouseUp();
        await Frames.Wait(3);
    }

    private static IEnumerable<GuiElement> Elements(GuiDialog dialog)
        => dialog.Composers.Values.SelectMany(composer =>
            Enumerable.Range(1, composer.CurrentElementKey)
                .Select(i => composer["element-" + i]));

    private static GuiDialog PauseMenu()
        => Capi.Gui.OpenedGuis.Single(dialog => dialog.GetType().Name == "GuiDialogEscapeMenu");

    private static async Task<ConfigDialog> ClickModSettings(GuiDialog pause)
    {
        GuiElementTextButton button = Elements(pause).OfType<GuiElementTextButton>()
            .Single(element => element.Text == "Mods settings");
        ElementBounds bounds = button.Bounds;
        await Click(bounds.absX + bounds.OuterWidth / 2, bounds.absY + bounds.OuterHeight / 2);

        return Capi.Gui.OpenedGuis.OfType<ConfigDialog>().Single();
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task PauseMenuButtonOpensAboveTheMenuAndEscapeReturnsToIt()
    {
        await OnClient();
        try
        {
            await Press(GlKeys.Escape);
            GuiDialog pause = PauseMenu();
            Assert.True(Capi.IsGamePaused, "the test must start paused");

            ConfigDialog settings = await ClickModSettings(pause);
            Assert.True(settings.DrawOrder > pause.DrawOrder, "pause menu draws over settings");
            Assert.True(settings.InputOrder < pause.InputOrder, "pause menu gets clicks before settings");
            Assert.True(settings.Focused, "settings did not receive keyboard focus");
            Assert.True(pause.IsOpened(), "opening settings dismissed the pause menu");
            Assert.True(Capi.IsGamePaused, "opening settings resumed the world");

            await Press(GlKeys.Escape);
            Assert.False(settings.IsOpened(), "Escape did not close settings");
            Assert.True(pause.IsOpened(), "Escape also closed the pause menu");
            Assert.True(Capi.IsGamePaused, "Escape resumed the world under settings");

            // Reopening the same dialog must retain its ordering. Close it with the
            // actual titlebar X rather than calling TryClose directly.
            for (int i = 0; i < 2; i++)
            {
                settings = await ClickModSettings(pause);
                GuiElementDialogTitleBar title = Elements(settings).OfType<GuiElementDialogTitleBar>().Single();
                double scale = RuntimeEnv.GUIScale;
                await Click(title.Bounds.absX + title.Bounds.OuterWidth - 19 * scale,
                    title.Bounds.absY + 13 * scale);
                Assert.False(settings.IsOpened(), "the titlebar close click was intercepted");
                Assert.True(pause.IsOpened(), "closing settings dismissed the pause menu");
                Assert.True(Capi.IsGamePaused, "closing settings resumed the world");
            }

            await Press(GlKeys.Escape);
            Assert.False(pause.IsOpened(), "the next Escape did not dismiss the pause menu");
            Assert.False(Capi.IsGamePaused, "the world did not resume after leaving the pause menu");
        }
        finally
        {
            await Gui.CloseDialogs();
        }
    }

    public class TextSettings
    {
        public string Label = "start";
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task PausedSettingsReceiveMouseClicksAndTypingIncludingTheToggleKey()
    {
        await OnClient();
        TextSettings model = new();
        Config config = new(Capi, "ckpausetyping", "Pause typing", model, "ckpausetyping.json");
        ConfigDialog settings = new(Capi, new Dictionary<string, Config> { ["ckpausetyping"] = config });
        try
        {
            await Press(GlKeys.Escape);
            GuiDialog pause = PauseMenu();
            settings.TryOpen();
            await Frames.Wait(5);

            var rect = settings.ScreenRectFor("Label");
            Assert.NotNull(rect);
            await Click(rect.Value.X + rect.Value.Width / 2, rect.Value.Y + rect.Value.Height / 2);
            await Input.KeyDown(GlKeys.P);
            await Input.Type('p');
            await Input.KeyUp(GlKeys.P);
            await Frames.Wait(3);

            Assert.True(settings.IsOpened(), "typing the toggle key closed settings");
            Assert.True(model.Label.Contains('p'), "the pause menu swallowed the click or text input");
            Assert.True(pause.IsOpened(), "editing settings closed the pause menu");
            Assert.True(Capi.IsGamePaused, "editing settings resumed the world");

            await Press(GlKeys.Escape);
            Assert.False(settings.IsOpened(), "Escape did not close settings with a text field focused");
            Assert.True(pause.IsOpened(), "Escape from a text field closed the pause menu");
            Assert.True(Capi.IsGamePaused, "Escape from a text field resumed the world");
        }
        finally
        {
            settings.TryClose();
            settings.Dispose();
            await Gui.CloseDialogs();
        }
    }

    [VsTest(TimeoutMs = 60000)]
    [RequiresClient]
    [SingleplayerOnly]
    public async Task StandaloneHotkeyStillOpensAndClosesWithoutPausing()
    {
        await OnClient();
        try
        {
            await Press(GlKeys.P);
            ConfigDialog settings = Capi.Gui.OpenedGuis.OfType<ConfigDialog>().Single();
            Assert.False(Capi.IsGamePaused, "the standalone hotkey unexpectedly paused the world");

            await Press(GlKeys.Escape);
            Assert.False(settings.IsOpened(), "Escape did not close standalone settings");
            Assert.False(Capi.IsGamePaused, "closing standalone settings opened the pause menu");

            await Press(GlKeys.P);
            Assert.True(settings.IsOpened(), "the hotkey did not reopen settings");
            Assert.True(await Input.Hotkey("configkitconfigs"), "the toggle handler did not handle closing");
            Assert.False(settings.IsOpened(), "the toggle handler did not close settings");
        }
        finally
        {
            await Gui.CloseDialogs();
        }
    }
}
