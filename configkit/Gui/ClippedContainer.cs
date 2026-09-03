// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.

using System.Collections.Generic;
using Vintagestory.API.Client;

namespace ConfigKit.Gui;

/// <summary>
/// A <see cref="GuiElementContainer"/> that hides the rows scrolled out of view.
///
/// The stock container draws its own static texture, which the surrounding BeginClip
/// scissors correctly, and then asks every child to draw itself. Several stock elements -
/// dynamic text, sliders, number inputs - paint straight at their own bounds and never
/// consult InsideClipBounds, so a row below the fold still appears: its frame is scissored
/// away but its label and value are not. With more settings than fit, that lands on top of
/// the buttons and the hotbar, and it is why a row could look like a half-drawn ghost.
///
/// The same bounds decide mouse handling, so an invisible switch sitting under the Save
/// button could be flipped by clicking the button. Culling both keeps what is drawn and
/// what is clickable to the same set of rows.
///
/// It also repairs the GL scissor between children, which is a separate engine bug.
/// GuiElementTextInput.RenderInteractiveElements overwrites the scissor rect with its own
/// box so it can trim its text, and then finishes with GlScissorFlag(false) rather than
/// restoring the state it was handed. GuiElementHoverText does the mirror of that: it
/// switches the flag back on without setting a rect. Put a number input, a tooltip and a
/// slider in one clipped container - which is exactly a settings row - and every slider
/// after the first number input is scissored into a stale text-box-sized rect and vanishes.
/// That is why a mod with a rangeless number setting lost the sliders below it while a mod
/// without one looked perfect.
/// </summary>
public class ClippedContainer : GuiElementContainer
{
    /// Reused so a per-frame cull allocates nothing.
    private readonly List<GuiElement> _visible = new();

    /// Handed to the base class so it draws its own surface without walking any children.
    private readonly List<GuiElement> _none = new();

    public ClippedContainer(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds)
    {
    }

    /// <summary>
    /// The rows that would actually be drawn. Exposed so a test can assert that the ones
    /// scrolled out of view are left out, which is not visible any other way.
    /// </summary>
    public IEnumerable<GuiElement> VisibleElements
    {
        get
        {
            foreach (GuiElement element in Elements)
            {
                if (!Hidden(element)) yield return element;
            }
        }
    }

    /// <summary>Is this element entirely outside the clip it was given?</summary>
    private static bool Hidden(GuiElement element)
    {
        ElementBounds? clip = element.InsideClipBounds;
        if (clip == null) return false;

        double top = element.Bounds.renderY;
        double bottom = top + element.Bounds.OuterHeight;

        return bottom < clip.renderY || top > clip.renderY + clip.OuterHeight;
    }

    /// <summary>
    /// Runs <paramref name="action"/> with only the visible rows in <see cref="Elements"/>.
    /// The base class walks that list directly, so swapping it is what lets a stock
    /// container be culled without reimplementing its rendering.
    /// </summary>
    private void OnlyVisible(System.Action action)
    {
        List<GuiElement> all = Elements;

        _visible.Clear();
        foreach (GuiElement element in all)
        {
            if (!Hidden(element)) _visible.Add(element);
        }

        Elements = _visible;
        try
        {
            action();
        }
        finally
        {
            Elements = all;
        }
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        // Let the base draw the container's own static texture, with no children of its own
        // to walk, so that each child can then be rendered behind its own scissor.
        List<GuiElement> all = Elements;
        Elements = _none;
        try
        {
            base.RenderInteractiveElements(deltaTime);
        }
        finally
        {
            Elements = all;
        }

        MouseOverCursor = null;
        foreach (GuiElement element in all)
        {
            if (Hidden(element)) continue;

            // Re-establish the clip for every child rather than trusting the previous one to
            // have left it alone. A tooltip still escapes the clip: it turns the scissor off
            // for its own draw, which is inside this push and undone by the pop.
            bool scissored = InsideClipBounds != null;
            if (scissored) api.Render.PushScissor(InsideClipBounds);

            element.RenderInteractiveElements(deltaTime);

            if (scissored) api.Render.PopScissor();

            if (element.IsPositionInside(api.Input.MouseX, api.Input.MouseY))
            {
                MouseOverCursor = element.MouseOverCursor;
            }
        }
    }

    public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
        => OnlyVisible(() => base.OnMouseDown(api, args));

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
        => OnlyVisible(() => base.OnMouseUp(api, args));

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
        => OnlyVisible(() => base.OnMouseMove(api, args));
}
