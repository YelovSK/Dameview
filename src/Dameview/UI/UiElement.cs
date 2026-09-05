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

    internal virtual bool IsFocusable => false;
    internal virtual bool IsHitTestVisible => true;
    internal virtual bool ObservePointerMoves => false;
    internal virtual bool PreservesFocusOnPointerPress => false;
    internal virtual UiCursor Cursor => UiCursor.Default;
    internal virtual float Opacity => 1.0f;
    internal virtual PointF VisualOffset => PointF.Empty;

    internal SizeF Measure(SizeF availableSize)
    {
        DesiredSize = MeasureCore(availableSize);
        return DesiredSize;
    }

    internal void Arrange(RectangleF bounds)
    {
        Bounds = bounds;
        ArrangeCore(bounds.Size);
    }

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

    internal UiPointerEvent ToLocal(in UiPointerEvent input)
    {
        PointF origin = GetRootOrigin(includeVisualOffset: true);
        return input with
        {
            Position = new PointF(input.Position.X - origin.X, input.Position.Y - origin.Y),
        };
    }

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

    internal bool HasVisualState(UiVisualState state) => (VisualState & state) != 0;

    internal virtual UiPointerResult OnPointerEvent(in UiPointerEvent input) => default;
    internal virtual bool OnKeyEvent(UiKeyEvent input) => false;
    internal virtual void BringIntoView(RectangleF descendantBounds) { }

    internal void SetFocusWithin(bool value)
    {
        if (HasFocusWithin == value)
        {
            return;
        }

        HasFocusWithin = value;
        OnFocusWithinChanged();
    }

    internal void AttachToRoot(UiRoot root)
    {
        Root = root;
        foreach (UiElement child in _children)
        {
            child.AttachToRoot(root);
        }
    }

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

    protected void InvalidateVisual() => Root?.InvalidateVisual();
    protected void InvalidateLayout() => Root?.InvalidateLayout();
    protected virtual SizeF MeasureCore(SizeF availableSize) => SizeF.Empty;
    protected virtual void ArrangeCore(SizeF finalSize) { }
    protected virtual bool UpdateCore(in UiUpdateContext context) => false;
    protected virtual void DrawCore(in UiDrawContext context) { }
    protected virtual bool HitTestCore(PointF position) => true;
    protected virtual void ObservePointerMove(in UiPointerEvent input) { }
    protected virtual void OnVisualStateChanged() { }
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

    internal void Draw(in UiDrawContext context) => DrawCore(context);
}

internal readonly record struct UiUpdateContext(double ElapsedSeconds);

internal readonly record struct UiPointerResult(
    bool Consumed = false,
    bool NeedsRepaint = false,
    bool CapturePointer = false);
