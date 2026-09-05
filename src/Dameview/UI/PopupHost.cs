using System.Drawing;
using Dameview.Platform;

namespace Dameview.UI;

// Owns transient popup placement and outside-click dismissal. Popup content remains
// owned by the control that created it.
internal sealed class PopupHost : UiElement
{
    private UiElement? _anchor;
    private UiElement? _content;
    private Action? _closed;
    private SizeF _preferredSize;
    private bool _outsidePressed;

    internal PopupHost()
    {
        IsVisible = false;
    }

    internal bool IsOpen => _content is not null;
    internal override bool PreservesFocusOnPointerPress => true;

    internal void Show(UiElement anchor, UiElement content, SizeF preferredSize, Action closed)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(closed);
        Close();
        _anchor = anchor;
        _content = content;
        _closed = closed;
        _preferredSize = preferredSize;
        _outsidePressed = false;
        AddChild(content);
        IsVisible = true;
    }

    internal void Close()
    {
        if (_content is null)
        {
            return;
        }

        UiElement content = _content;
        Action? closed = _closed;
        _anchor = null;
        _content = null;
        _closed = null;
        _outsidePressed = false;
        RemoveChild(content);
        IsVisible = false;
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
        _content?.Measure(_preferredSize);
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
            Close();
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
        float y = below >= height || below >= above
            ? anchor.Bottom + gap
            : anchor.Top - gap - height;
        y = Math.Clamp(y, margin, MathF.Max(margin, finalSize.Height - margin - height));
        _content.Arrange(new RectangleF(x, y, width, height));
    }

    internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
    {
        bool insidePopup = _content?.Bounds.Contains(input.Position) == true;
        switch (input.Kind)
        {
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
}
