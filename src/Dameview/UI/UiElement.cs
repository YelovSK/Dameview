using System.Drawing;
using Dameview.Platform;

namespace Dameview.UI;

internal enum UiCursor
{
    Default,
    Pointer,
    ResizeHorizontal,
    ResizeVertical,
}

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
    private bool _isVisible = true;

    internal RectangleF Bounds { get; private set; }
    internal SizeF DesiredSize { get; private set; }
    internal UiElement? Parent { get; private set; }
    internal UiRoot? Root { get; private set; }
    internal IReadOnlyList<UiElement> Children => _children;
    internal UiVisualState VisualState { get; private set; }
    internal bool HasFocusWithin { get; private set; }

    internal bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
            {
                return;
            }

            if (!value)
            {
                Root?.DisconnectSubtree(this);
            }

            _isVisible = value;
            InvalidateLayout();
        }
    }

    /// <summary>Whether this element can receive keyboard focus.</summary>
    internal virtual bool IsFocusable => false;
    /// <summary>Whether this element and its descendants participate in hit testing.</summary>
    internal virtual bool IsHitTestVisible => true;
    /// <summary>Whether this element observes pointer moves even without capture.</summary>
    internal virtual bool ObservePointerMoves => false;
    /// <summary>Whether a pointer press on this element may leave the current focus unchanged.</summary>
    internal virtual bool PreservesFocusOnPointerPress => false;
    /// <summary>The pointer cursor shown while hovering or capturing this element.</summary>
    internal virtual UiCursor Cursor => UiCursor.Default;
    /// <summary>The opacity applied to this element and its drawn content.</summary>
    internal virtual float Opacity => 1.0f;
    /// <summary>An animated translation applied for drawing and hit testing.</summary>
    internal virtual PointF VisualOffset => PointF.Empty;

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

        bool continues = UpdateCore(context);
        foreach (UiElement child in _children)
        {
            continues |= child.UpdateTree(context);
        }

        return continues;
    }

    /// <summary>Finds the topmost hit-testable element at a point in the parent's coordinate space.</summary>
    /// <returns>The deepest matching element, or <see langword="null"/> when the point is outside the subtree.</returns>
    internal UiElement? HitTest(PointF positionInParent)
    {
        if (!IsVisible || !IsHitTestVisible || Opacity <= 0.0f)
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
    internal void ObservePointerMoveTree(in UiPointerEvent input)
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
    internal UiPointerEvent ToLocal(in UiPointerEvent input)
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
    internal virtual UiPointerResult OnPointerEvent(in UiPointerEvent input) => default;
    /// <summary>Handles a key event.</summary>
    /// <returns><see langword="true"/> when the event was handled and should not be routed further.</returns>
    internal virtual bool OnKeyEvent(UiKeyEvent input) => false;
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
    protected virtual void ObservePointerMove(in UiPointerEvent input) { }
    /// <summary>Called after one or more visual-state flags change.</summary>
    protected virtual void OnVisualStateChanged() { }
    /// <summary>Called when focus enters or leaves this element's subtree.</summary>
    protected virtual void OnFocusWithinChanged() { }

    private UiPointerEvent ToLayoutLocal(in UiPointerEvent input)
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
internal readonly record struct UiUpdateContext(double ElapsedSeconds);

/// <summary>Describes how a pointer handler consumed an event and affected routing.</summary>
internal readonly record struct UiPointerResult(
    bool Consumed = false,
    bool NeedsRepaint = false,
    bool CapturePointer = false);
