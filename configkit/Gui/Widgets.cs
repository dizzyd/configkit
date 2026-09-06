// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.

using Cairo;
using Vintagestory.API.Client;

namespace ConfigKit.Gui;

/// <summary>
/// A number input whose empty box means "no value" and stays empty.
///
/// The stock <see cref="GuiElementNumberInput"/> puts a 0 into an empty box the moment it
/// loses focus - its own comment says so - which is right for an <c>int</c> and wrong for an
/// <c>int?</c>: a player who clears the box to put null back watches it turn into 0 as soon
/// as they click anywhere else, and 0 is the one value null is not. Reported by
/// TheInsanityGod as "nullable numbers jump back to 0".
///
/// The base behaviour cannot be skipped, only undone: the box is let go of, the base puts
/// its 0 in, and the 0 is taken out again before anyone hears about it.
/// </summary>
internal sealed class NullableNumberInput : GuiElementNumberInput
{
    private bool _restoring;

    public NullableNumberInput(ICoreClientAPI capi, ElementBounds bounds, Action<string> onTextChanged, CairoFont font)
        : base(capi, bounds, null!, font)
    {
        OnTextChanged = text =>
        {
            if (!_restoring) onTextChanged(text);
        };
    }

    public override void OnFocusLost()
    {
        bool empty = GetText() == string.Empty;

        _restoring = empty;
        try
        {
            base.OnFocusLost();
        }
        finally
        {
            _restoring = false;
        }

        if (empty) SetValue("");
    }
}

/// <summary>
/// A square of colour beside a hex field, redrawn as the field changes.
///
/// <see cref="GuiElementCustomDraw"/> looked like the tool for this and is not: built
/// non-interactive it paints once onto the dialog's static surface, so a later
/// <c>Redraw()</c> renders into a texture that is never drawn and never freed. Built
/// interactive it draws per frame but still has no <c>Dispose</c> for that texture. This
/// owns one <see cref="LoadedTexture"/> and lets it go with the rest of the row.
/// </summary>
internal sealed class ColorSwatch : GuiElement
{
    private readonly Func<string> _hex;
    private LoadedTexture _texture;

    public ColorSwatch(ICoreClientAPI capi, ElementBounds bounds, Func<string> hex) : base(capi, bounds)
    {
        _hex = hex;
        _texture = new LoadedTexture(capi);
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        Redraw();
    }

    public void Redraw()
    {
        ImageSurface surface = new(Format.Argb32, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
        Context ctx = genContext(surface);

        Draw(ctx, Bounds.InnerWidth, Bounds.InnerHeight, _hex());
        generateTexture(surface, ref _texture);

        ctx.Dispose();
        surface.Dispose();
    }

    private static void Draw(Context ctx, double w, double h, string hex)
    {
        if (ConfigDialog.TryParseHex(hex, out double r, out double g, out double b))
        {
            ctx.SetSourceRGB(r, g, b);
            ctx.Rectangle(0, 0, w, h);
            ctx.Fill();
        }
        else
        {
            // Unparseable: a flat dark box with a stroke through it, so it reads as "not a
            // colour" rather than as black.
            ctx.SetSourceRGB(0.12, 0.12, 0.12);
            ctx.Rectangle(0, 0, w, h);
            ctx.Fill();
            ctx.SetSourceRGB(0.75, 0.3, 0.3);
            ctx.LineWidth = 2;
            ctx.MoveTo(3, 3);
            ctx.LineTo(w - 3, h - 3);
            ctx.Stroke();
        }

        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 1;
        ctx.Rectangle(0.5, 0.5, w - 1, h - 1);
        ctx.Stroke();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        api.Render.Render2DTexturePremultipliedAlpha(_texture.TextureId, Bounds);
    }

    public override void Dispose()
    {
        base.Dispose();
        _texture.Dispose();
    }
}
