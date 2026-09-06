using System;
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
    /// The rows a mouse event can reach: the visible ones, as a copy.
    ///
    /// This used to swap <see cref="GuiElementContainer.Elements"/> for the visible subset
    /// while the base class ran its handler, and put the full list back afterwards. A row's
    /// handler that rebuilds the screen - remove, add, rename, fold - runs inside that
    /// window, and the rebuild disposes this container while the swap is in force: only the
    /// visible rows were disposed, and every row scrolled out of view leaked its textures.
    /// TheInsanityGod's traced log had 2817 of them from one minute of editing a long
    /// dictionary, every one allocated by a row of a container screen. So the handlers
    /// below walk their own list and never touch the field the disposer walks.
    ///
    /// A fresh list per event rather than a reused one: a handler can rebuild the screen or
    /// re-enter this container, and neither may pull the list out from under the loop that
    /// is walking it. Events are rare next to frames, so the allocation is nothing.
    /// </summary>
    private List<GuiElement> Reachable()
    {
        List<GuiElement> reachable = new(Elements.Count);
        foreach (GuiElement element in Elements)
        {
            if (!Hidden(element)) reachable.Add(element);
        }

        return reachable;
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        // Let the base draw the container's own static texture, with no children of its own
        // to walk, so that each child can then be rendered behind its own scissor. Nothing
        // in that draw can call back into a handler, so the swap is safe here and only here.
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

    // The three handlers below are GuiElementContainer's own, over the reachable rows
    // rather than Elements. The base class's fallbacks - OnMouseDownOnElement when the
    // press was inside the container, nothing on move - are written out because a method
    // cannot call its grandparent's implementation past the one it overrides.

    public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
    {
        bool beforeHandled = false;
        bool nowHandled = false;

        foreach (GuiElement element in Reachable())
        {
            if (!beforeHandled)
            {
                element.OnMouseDown(api, args);
                nowHandled = args.Handled;
            }

            if (!beforeHandled && nowHandled)
            {
                if (element.Focusable && !element.HasFocus) element.OnFocusGained();
            }
            else if (element.Focusable && element.HasFocus)
            {
                element.OnFocusLost();
            }

            beforeHandled = nowHandled;
        }

        if (!args.Handled && IsPositionInside(args.X, args.Y)) OnMouseDownOnElement(api, args);
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        foreach (GuiElement element in Reachable())
        {
            element.OnMouseUp(api, args);
        }

        if (!args.Handled && IsPositionInside(args.X, args.Y)) OnMouseUpOnElement(api, args);
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        foreach (GuiElement element in Reachable())
        {
            element.OnMouseMove(api, args);
            if (args.Handled) break;
        }
    }

    /// <summary>
    /// Key events reach whichever child has focus.
    ///
    /// GuiElementContainer forwards keys only when the *container itself* has focus:
    ///
    ///     public override void OnKeyPress(ICoreClientAPI api, KeyEvent args)
    ///     {
    ///         if (!HasFocus) return;
    ///
    /// but clicking a row focuses the child - OnMouseDown calls element.OnFocusGained() and
    /// nothing ever focuses the container. So a text field could be clicked, take focus and
    /// show a caret, and then swallow every keystroke in silence.
    ///
    /// Reported against MoreHudBars' "Hud bar style", and not specific to it: it was every
    /// text field in the library, and every number input, since they all live in this
    /// container. It survived this long because the tests set values through the model - the
    /// widget's handler was always fine, and nothing ever asked whether a keystroke could
    /// reach it.
    /// </summary>
    public override void OnKeyPress(ICoreClientAPI api, KeyEvent args)
        => ToFocusedChild(args, element => element.OnKeyPress(api, args));

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
        => ToFocusedChild(args, element => element.OnKeyDown(api, args));

    private void ToFocusedChild(KeyEvent args, Action<GuiElement> send)
    {
        foreach (GuiElement element in Elements)
        {
            if (!element.HasFocus) continue;

            send(element);
            if (args.Handled) return;
        }
    }
}
