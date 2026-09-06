using System.Drawing;
using Dameview.Platform;
using Dameview.UI.Animation;

namespace Dameview.UI;

// Owns transient popup placement and outside-click dismissal. Popup content remains
// owned by the control that created it.
internal sealed class PopupHost : UiElement
{
    private UiElement? _anchor;
    private UiElement? _content;
    private Action? _closed;
    private readonly PopupPresenter _presenter;
    private AnimatedFloat _visibility = new(0.0f, 24.0);
    private SizeF _preferredSize;
    private bool _outsidePressed;
    private bool _closing;

    internal PopupHost()
    {
        _presenter = new PopupPresenter(this);
        AddChild(_presenter);
        IsVisible = false;
    }

    internal bool IsOpen => _content is not null && !_closing;
    internal override bool IsHitTestVisible => IsOpen;
    internal override bool PreservesFocusOnPointerPress => true;

    internal void Show(UiElement anchor, UiElement content, SizeF preferredSize, Action closed)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(closed);
        RemoveContent(invokeClosed: true);
        _anchor = anchor;
        _content = content;
        _closed = closed;
        _preferredSize = preferredSize;
        _outsidePressed = false;
        _closing = false;
        _visibility = new AnimatedFloat(0.0f, 24.0);
        _visibility.SetTarget(1.0f);
        _presenter.SetContent(content);
        _presenter.IsVisible = true;
        IsVisible = true;
    }

    internal void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        _closing = true;
        _outsidePressed = false;
        _visibility.SetTarget(0.0f);
        Action? closed = _closed;
        _closed = null;
        closed?.Invoke();
        InvalidateLayout();
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
        _presenter.Measure(_preferredSize);
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

        const float margin = 8.0f;
        const float gap = 4.0f;
        RectangleF anchor = _anchor.GetBoundsRelativeTo(this);
        float availableWidth = MathF.Max(0.0f, finalSize.Width - 2.0f * margin);
        float availableHeight = MathF.Max(0.0f, finalSize.Height - 2.0f * margin);
        float width = MathF.Min(MathF.Max(anchor.Width, _preferredSize.Width), availableWidth);
        float height = MathF.Min(_preferredSize.Height, availableHeight);
        float x = Math.Clamp(anchor.Left, margin, MathF.Max(margin, finalSize.Width - margin - width));
        float below = finalSize.Height - margin - (anchor.Bottom + gap);
        float above = anchor.Top - gap - margin;
        bool opensBelow = below >= height || below >= above;
        float y = opensBelow
            ? anchor.Bottom + gap
            : anchor.Top - gap - height;
        y = Math.Clamp(y, margin, MathF.Max(margin, finalSize.Height - margin - height));
        float visibleHeight = height * _visibility.Current;
        _presenter.Arrange(new RectangleF(x, opensBelow ? y : y + height - visibleHeight, width, visibleHeight));
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        bool wasAnimating = _visibility.Current != _visibility.Target;
        bool continues = _visibility.Update(context.ElapsedSeconds);
        if (wasAnimating)
        {
            InvalidateLayout();
        }

        if (_closing && !continues)
        {
            RemoveContent(invokeClosed: false);
        }

        return continues;
    }

    internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
    {
        bool insidePopup = IsOpen && _presenter.GetBoundsRelativeTo(this).Contains(input.Position);
        switch (input.Kind)
        {
            case UiPointerEventKind.DoubleClicked
                when input.Button == PointerButton.Primary && !insidePopup:
                Close();
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case UiPointerEventKind.Pressed
                when input.Button == PointerButton.Primary && !insidePopup:
                _outsidePressed = true;
                return new UiPointerResult(Consumed: true, CapturePointer: true);

            case UiPointerEventKind.Released when _outsidePressed:
                _outsidePressed = false;
                Close();
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case UiPointerEventKind.Cancelled:
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
        _content = null;
        _closed = null;
        _outsidePressed = false;
        _closing = false;
        _presenter.SetContent(null);
        _presenter.IsVisible = false;
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

    private sealed class PopupPresenter(PopupHost owner) : UiElement
    {
        private UiElement? _content;

        internal override bool IsHitTestVisible => owner.IsOpen;

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
