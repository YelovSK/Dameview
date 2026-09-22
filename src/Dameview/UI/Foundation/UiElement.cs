using System.Drawing;
using Dameview.UI.Animation;
using Dameview.Win32.Input;

namespace Dameview.UI.Foundation;

/// <summary>Base class for an element in the custom UI tree.</summary>
/// <remarks>
/// Elements are measured and arranged in DIPs. Pointer positions passed to an
/// element are local to that element, while drawing is performed in its arranged
/// coordinate space. Derived classes customize behavior through the protected
/// core methods and should invalidate the root when their rendered state changes.
/// </remarks>
internal abstract class UiElement
{
    private readonly List<UiElement> _children = [];
    private readonly List<AnimatedFloat> _animations = [];
    // How far the element has entered, easing toward IsPresent. Only exists with a Transition.
    private AnimatedFloat? _presence;

    internal RectangleF Bounds { get; private set; }
    internal SizeF DesiredSize { get; private set; }
    internal UiElement? Parent { get; private set; }
    internal UiRoot? Root { get; private set; }
    internal IReadOnlyList<UiElement> Children => _children;
    internal UiVisualState VisualState { get; private set; }
    internal bool HasFocusWithin { get; private set; }

    internal bool IsVisible
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            if (!value)
            {
                Root?.DisconnectSubtree(this);
            }

            field = value;
            InvalidateLayout();
        }
    } = true;

    /// <summary>How the element animates as <see cref="IsPresent"/> changes; without one it switches at once.</summary>
    internal UiTransition? Transition
    {
        get;
        set
        {
            field = value;
            _presence = value is { } transition
                ? new AnimatedFloat(IsPresent ? 1.0f : 0.0f, transition.Response)
                : null;
        }
    }

    /// <summary>Whether the element should be shown.</summary>
    /// <remarks>
    /// Unlike <see cref="IsVisible"/>, this plays the <see cref="Transition"/>. A leaving element
    /// stops taking input at once but stays visible until its exit has finished.
    /// </remarks>
    internal bool IsPresent
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            float target = value ? 1.0f : 0.0f;
            // Nothing is on screen to animate from, so the element takes its new state at once.
            if (_presence is null || Root is not { LayoutPasses: > 0 } || (!value && !IsVisible))
            {
                _presence?.SetValue(target);
                IsVisible = value;
                return;
            }

            if (!value)
            {
                Root.DisconnectSubtree(this);
            }
            else if (!IsVisible)
            {
                _presence.SetValue(0.0f);
                IsVisible = true;
            }

            _presence.SetTarget(target);
            InvalidateVisual();
        }
    } = true;

    /// <summary>How far the element has entered, from 0 when absent to 1 when fully present.</summary>
    internal float Presence => _presence?.Current ?? 1.0f;

    /// <summary>The share of its space a container should give this element.</summary>
    internal float LayoutPresence => Transition is { Collapse: true } ? Presence : 1.0f;

    /// <summary>Whether this element can receive keyboard focus.</summary>
    internal virtual bool IsFocusable => false;
    /// <summary>Whether this element and its descendants participate in hit testing.</summary>
    internal virtual bool IsHitTestVisible => true;
    /// <summary>Whether this element observes pointer moves even without capture.</summary>
    internal virtual bool ObservePointerMoves => false;
    /// <summary>Whether a pointer press on this element may leave the current focus unchanged.</summary>
    internal virtual bool PreservesFocusOnPointerPress => false;
    /// <summary>The pointer cursor shown while hovering or capturing this element.</summary>
    internal virtual WindowCursor Cursor => WindowCursor.Default;
    /// <summary>The opacity applied to this element and its drawn content.</summary>
    internal virtual float Opacity => Transition is { Fade: true } ? Presence : 1.0f;
    /// <summary>An animated translation applied for drawing and hit testing.</summary>
    internal virtual PointF VisualOffset => Transition is { } transition
        ? new PointF(
            transition.HiddenOffset.X * (1.0f - Presence),
            transition.HiddenOffset.Y * (1.0f - Presence))
        : PointF.Empty;
    /// <summary>An animated scale about the element's center, applied for drawing only.</summary>
    internal virtual float VisualScale => Transition is { } transition
        ? transition.HiddenScale + ((1.0f - transition.HiddenScale) * Presence)
        : 1.0f;

    /// <summary>Measures this element and stores the size it would like to occupy.</summary>
    internal SizeF Measure(SizeF availableSize)
    {
        DesiredSize = MeasureCore(availableSize);
        return DesiredSize;
    }

    /// <summary>Assigns the element's bounds and arranges its descendants.</summary>
    internal void Arrange(RectangleF bounds)
    {
        Bounds = bounds;
        ArrangeCore(bounds.Size);
    }

    /// <summary>Updates this element and all visible descendants.</summary>
    /// <returns><see langword="true"/> while any element in the subtree still needs animation updates.</returns>
    internal bool UpdateTree(in UiUpdateContext context)
    {
        if (!IsVisible)
        {
            return false;
        }

        bool continues = UpdatePresence(context);
        foreach (AnimatedFloat animation in _animations)
        {
            continues |= animation.Update(context);
        }

        continues |= UpdateCore(context);
        foreach (UiElement child in _children)
        {
            continues |= child.UpdateTree(context);
        }

        return continues;
    }

    /// <summary>Settles the transition at once, e.g. when the element is about to appear somewhere new.</summary>
    internal void FinishTransition()
    {
        _presence?.SetValue(IsPresent ? 1.0f : 0.0f);
        IsVisible = IsPresent;
    }

    /// <summary>Finds the topmost hit-testable element at a point in the parent's coordinate space.</summary>
    /// <returns>The deepest matching element, or <see langword="null"/> when the point is outside the subtree.</returns>
    internal UiElement? HitTest(PointF positionInParent)
    {
        if (!IsVisible || !IsPresent || !IsHitTestVisible)
        {
            return null;
        }

        RectangleF visualBounds = GetVisualBounds();
        if (!visualBounds.Contains(positionInParent))
        {
            return null;
        }

        var local = new PointF(
            positionInParent.X - visualBounds.X,
            positionInParent.Y - visualBounds.Y);
        for (int index = _children.Count - 1; index >= 0; index--)
        {
            if (_children[index].HitTest(local) is { } hit)
            {
                return hit;
            }
        }

        return HitTestCore(local) ? this : null;
    }

    /// <summary>Forwards a pointer-move observation to this element and all visible descendants.</summary>
    internal void ObservePointerMoveTree(in WindowPointerEvent input)
    {
        if (!IsVisible)
        {
            return;
        }

        if (ObservePointerMoves)
        {
            ObservePointerMove(ToLayoutLocal(input));
        }

        foreach (UiElement child in _children)
        {
            child.ObservePointerMoveTree(input);
        }
    }

    /// <summary>Converts a root-space pointer event to this element's local coordinates.</summary>
    internal WindowPointerEvent ToLocal(in WindowPointerEvent input)
    {
        PointF origin = GetRootOrigin(includeVisualOffset: true);
        return input with
        {
            Position = new PointF(input.Position.X - origin.X, input.Position.Y - origin.Y),
        };
    }

    /// <summary>Gets this element's arranged bounds relative to an ancestor.</summary>
    internal RectangleF GetBoundsRelativeTo(UiElement ancestor)
    {
        PointF origin = GetRootOrigin(includeVisualOffset: true);
        PointF ancestorOrigin = ancestor.GetRootOrigin(includeVisualOffset: true);
        return new RectangleF(
            origin.X - ancestorOrigin.X,
            origin.Y - ancestorOrigin.Y,
            Bounds.Width,
            Bounds.Height);
    }

    /// <summary>Enables or disables one visual state and notifies the element of the change.</summary>
    internal void SetVisualState(UiVisualState state, bool enabled)
    {
        UiVisualState updated = enabled ? VisualState | state : VisualState & ~state;
        if (updated == VisualState)
        {
            return;
        }

        VisualState = updated;
        OnVisualStateChanged();
        InvalidateVisual();
    }

    /// <summary>Determines whether the specified visual state is active.</summary>
    internal bool HasVisualState(UiVisualState state) => (VisualState & state) != 0;

    /// <summary>Handles a pointer event and reports whether it was consumed or requires capture/repaint.</summary>
    internal virtual UiPointerResult OnPointerEvent(in WindowPointerEvent input) => default;
    /// <summary>Handles a key event.</summary>
    /// <returns><see langword="true"/> when the event was handled and should not be routed further.</returns>
    internal virtual bool OnKeyEvent(WindowKeyEvent input) => false;
    /// <summary>Handles committed text input.</summary>
    /// <returns><see langword="true"/> when the text was handled.</returns>
    internal virtual bool OnTextInput(string text) => false;
    /// <summary>Requests that a descendant's bounds be brought into this element's visible region.</summary>
    internal virtual void BringIntoView(RectangleF descendantBounds) { }

    /// <summary>Updates whether keyboard focus is somewhere in this element's subtree.</summary>
    internal void SetFocusWithin(bool value)
    {
        if (HasFocusWithin == value)
        {
            return;
        }

        HasFocusWithin = value;
        OnFocusWithinChanged();
    }

    /// <summary>Connects this element and its descendants to a UI root.</summary>
    internal void AttachToRoot(UiRoot root)
    {
        Root = root;
        foreach (UiElement child in _children)
        {
            child.AttachToRoot(root);
        }
    }

    /// <summary>Adds a child and attaches it to this element's root when already connected.</summary>
    protected void AddChild(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null)
        {
            throw new InvalidOperationException("The UI element already has a parent.");
        }

        child.Parent = this;
        _children.Add(child);
        if (Root is { } root)
        {
            child.AttachToRoot(root);
        }

        InvalidateLayout();
    }

    /// <summary>Removes a child and disconnects its subtree from the root.</summary>
    protected void RemoveChild(UiElement child)
    {
        if (!_children.Remove(child))
        {
            return;
        }

        Root?.DisconnectSubtree(child);
        child.Parent = null;
        child.DetachFromRoot();
        InvalidateLayout();
    }

    /// <summary>The shared text layouts, for text an element measures itself against.</summary>
    /// <remarks>
    /// Any element invalidating layout re-measures the whole tree, so text that has not changed
    /// must come back out of the cache rather than be shaped again.
    /// </remarks>
    protected UiTextLayoutCache TextLayouts => Root?.TextLayouts
        ?? throw new InvalidOperationException(
            "Text is measured only once the element is attached to a root.");

    /// <summary>Creates a value that this element advances on every update, before <see cref="UpdateCore"/>.</summary>
    protected AnimatedFloat Animate(float initialValue, double response, float completionDistance = 0.001f)
    {
        var animation = new AnimatedFloat(initialValue, response, completionDistance);
        _animations.Add(animation);
        return animation;
    }

    /// <summary>Requests a redraw without forcing layout.</summary>
    protected void InvalidateVisual() => Root?.InvalidateVisual();
    /// <summary>Requests layout and a redraw from the owning root.</summary>
    protected void InvalidateLayout() => Root?.InvalidateLayout();
    /// <summary>Calculates the desired size for this element.</summary>
    protected virtual SizeF MeasureCore(SizeF availableSize) => SizeF.Empty;
    /// <summary>Arranges children after this element has received its final size.</summary>
    protected virtual void ArrangeCore(SizeF finalSize) { }
    /// <summary>Advances animation or other time-dependent state.</summary>
    /// <returns><see langword="true"/> while another update is needed.</returns>
    protected virtual bool UpdateCore(in UiUpdateContext context) => false;
    /// <summary>Draws this element in its arranged bounds.</summary>
    protected virtual void DrawCore(in UiDrawContext context) { }
    /// <summary>Determines whether the element surface itself accepts pointer hits.</summary>
    /// <returns><see langword="true"/> to hit this element when no child is hit.</returns>
    protected virtual bool HitTestCore(PointF position) => true;
    /// <summary>Observes a pointer move without consuming or capturing the event.</summary>
    protected virtual void ObservePointerMove(in WindowPointerEvent input) { }
    /// <summary>Called after one or more visual-state flags change.</summary>
    protected virtual void OnVisualStateChanged() { }
    /// <summary>Called when focus enters or leaves this element's subtree.</summary>
    protected virtual void OnFocusWithinChanged() { }

    private bool UpdatePresence(in UiUpdateContext context)
    {
        if (_presence is null)
        {
            return false;
        }

        float previous = _presence.Current;
        bool continues = _presence.Update(context);
        if (_presence.Current != previous && Transition is { Collapse: true })
        {
            InvalidateLayout();
        }

        if (!continues && !IsPresent)
        {
            IsVisible = false;
        }

        return continues;
    }

    private WindowPointerEvent ToLayoutLocal(in WindowPointerEvent input)
    {
        PointF origin = GetRootOrigin(includeVisualOffset: false);
        return input with
        {
            Position = new PointF(input.Position.X - origin.X, input.Position.Y - origin.Y),
        };
    }

    private RectangleF GetVisualBounds()
    {
        PointF offset = VisualOffset;
        return new RectangleF(Bounds.X + offset.X, Bounds.Y + offset.Y, Bounds.Width, Bounds.Height);
    }

    private void DetachFromRoot()
    {
        Root = null;
        foreach (UiElement child in _children)
        {
            child.DetachFromRoot();
        }
    }

    private PointF GetRootOrigin(bool includeVisualOffset)
    {
        float x = 0.0f;
        float y = 0.0f;
        for (UiElement? element = this; element is not null; element = element.Parent)
        {
            x += element.Bounds.X;
            y += element.Bounds.Y;
            if (includeVisualOffset)
            {
                x += element.VisualOffset.X;
                y += element.VisualOffset.Y;
            }
        }

        return new PointF(x, y);
    }

    /// <summary>Draws this element through its derived rendering hook.</summary>
    internal void Draw(in UiDrawContext context) => DrawCore(context);
}

/// <summary>Frame timing supplied to UI elements during an update.</summary>
internal readonly record struct UiUpdateContext(
    double ElapsedSeconds,
    bool AnimationsEnabled = true);

/// <summary>Describes how a pointer handler consumed an event and affected routing.</summary>
internal readonly record struct UiPointerResult(
    bool Consumed = false,
    bool NeedsRepaint = false,
    bool CapturePointer = false);
