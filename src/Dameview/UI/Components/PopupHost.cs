using System.Drawing;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Dameview.Win32.Input;

namespace Dameview.UI.Components;

// Owns transient popup placement and outside-click dismissal. Popup content remains
// owned by the control that created it.
internal sealed class PopupHost : UiElement
{
    private const float Margin = 8.0f;

    private UiElement? _anchor;
    // A point within the anchor to open from, or the whole anchor when absent.
    private PointF? _anchorPoint;
    private UiElement? _content;
    private Action? _closed;
    private readonly PopupPresenter _presenter;
    private bool _outsidePressed;

    internal PopupHost()
    {
        _presenter = new PopupPresenter(this);
        AddChild(_presenter);
        IsVisible = false;
    }

    internal bool IsOpen => _content is not null && _presenter.IsPresent;
    internal override bool IsHitTestVisible => IsOpen;
    internal override bool PreservesFocusOnPointerPress => true;

    /// <summary>Opens content, sized by its own measure, beside the anchor and at least as wide.</summary>
    internal void Show(UiElement anchor, UiElement content, Action closed) =>
        Show(anchor, anchorPoint: null, content, closed);

    /// <summary>Opens content at a point in the anchor's coordinates, as a context menu does.</summary>
    internal void ShowAt(UiElement anchor, PointF point, UiElement content, Action closed) =>
        Show(anchor, point, content, closed);

    private void Show(UiElement anchor, PointF? anchorPoint, UiElement content, Action closed)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(closed);
        RemoveContent(invokeClosed: true);
        _anchor = anchor;
        _anchorPoint = anchorPoint;
        _content = content;
        _closed = closed;
        _outsidePressed = false;
        _presenter.SetContent(content);
        IsVisible = true;
        _presenter.IsPresent = true;
    }

    internal void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        _outsidePressed = false;
        _presenter.IsPresent = false;
        Action? closed = _closed;
        _closed = null;
        closed?.Invoke();
    }

    internal bool HandleEscape()
    {
        if (!IsOpen)
        {
            return false;
        }

        Close();
        return true;
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        _presenter.Measure(new SizeF(
            MathF.Max(0.0f, availableSize.Width - 2.0f * Margin),
            MathF.Max(0.0f, availableSize.Height - 2.0f * Margin)));
        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        if (_anchor is null || _content is null)
        {
            return;
        }

        if (!IsEffectivelyVisible(_anchor))
        {
            RemoveContent(invokeClosed: true);
            return;
        }

        const float gap = 4.0f;
        RectangleF anchor = _anchor.GetBoundsRelativeTo(this);
        if (_anchorPoint is { } point)
        {
            anchor = new RectangleF(anchor.X + point.X, anchor.Y + point.Y, 0.0f, 0.0f);
        }

        SizeF desired = _content.DesiredSize;
        float availableWidth = MathF.Max(0.0f, finalSize.Width - 2.0f * Margin);
        float availableHeight = MathF.Max(0.0f, finalSize.Height - 2.0f * Margin);
        float width = MathF.Min(MathF.Max(anchor.Width, desired.Width), availableWidth);
        float height = MathF.Min(desired.Height, availableHeight);
        float x = Math.Clamp(anchor.Left, Margin, MathF.Max(Margin, finalSize.Width - Margin - width));
        float below = finalSize.Height - Margin - (anchor.Bottom + gap);
        float above = anchor.Top - gap - Margin;
        bool opensBelow = below >= height || below >= above;
        float y = opensBelow
            ? anchor.Bottom + gap
            : anchor.Top - gap - height;
        y = Math.Clamp(y, Margin, MathF.Max(Margin, finalSize.Height - Margin - height));
        // The popup unrolls from its anchor as it enters and rolls back up as it leaves.
        float visibleHeight = height * _presenter.LayoutPresence;
        _presenter.Arrange(new RectangleF(x, opensBelow ? y : y + height - visibleHeight, width, visibleHeight));
    }

    // Lets go of a closed popup's anchor and content once it has rolled up.
    protected override bool UpdateCore(in UiUpdateContext context)
    {
        if (_content is null || _presenter.IsPresent)
        {
            return false;
        }

        if (_presenter.IsVisible)
        {
            return true;
        }

        RemoveContent(invokeClosed: false);
        return false;
    }

    internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
    {
        bool insidePopup = IsOpen && _presenter.GetBoundsRelativeTo(this).Contains(input.Position);
        switch (input.Kind)
        {
            case WindowPointerEventKind.DoubleClicked
                when input.Button == PointerButton.Primary && !insidePopup:
                Close();
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Wheel when !insidePopup:
                Close();
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Pressed when !insidePopup:
                _outsidePressed = true;
                return new UiPointerResult(Consumed: true, CapturePointer: true);

            case WindowPointerEventKind.Released when _outsidePressed:
                _outsidePressed = false;
                Close();
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Cancelled:
                _outsidePressed = false;
                return new UiPointerResult(Consumed: true);

            default:
                return new UiPointerResult(Consumed: true);
        }
    }

    private void RemoveContent(bool invokeClosed)
    {
        if (_content is null)
        {
            return;
        }

        Action? closed = invokeClosed ? _closed : null;
        _anchor = null;
        _anchorPoint = null;
        _content = null;
        _closed = null;
        _outsidePressed = false;
        _presenter.IsPresent = false;
        _presenter.FinishTransition();
        _presenter.SetContent(null);
        IsVisible = false;
        closed?.Invoke();
    }

    private static bool IsEffectivelyVisible(UiElement element)
    {
        for (UiElement? current = element; current is not null; current = current.Parent)
        {
            if (!current.IsVisible)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class PopupPresenter : UiElement
    {
        private readonly PopupHost _owner;
        private UiElement? _content;

        internal PopupPresenter(PopupHost owner)
        {
            _owner = owner;
            // The host sizes the presenter by its presence rather than fading it.
            Transition = new UiTransition(Collapse: true, Response: 24.0);
            IsPresent = false;
        }

        internal override bool IsHitTestVisible => _owner.IsOpen;

        internal void SetContent(UiElement? content)
        {
            if (_content is not null)
            {
                RemoveChild(_content);
            }

            _content = content;
            if (content is not null)
            {
                AddChild(content);
            }
        }

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            _content?.Measure(availableSize);
            return availableSize;
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            _content?.Arrange(new RectangleF(PointF.Empty, finalSize));
        }

        protected override bool HitTestCore(PointF position) => false;
    }
}
