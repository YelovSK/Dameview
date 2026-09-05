using System.Drawing;
using Dameview.Platform;

namespace Dameview.UI;

internal sealed class UiRoot
{
    private readonly UiElement _content;
    private UiElement? _capturedElement;
    private UiElement? _focusedElement;
    private UiElement? _hoveredElement;
    private SizeF _pixelSize;
    private bool _layoutDirty = true;
    private float _dpi;

    internal UiRoot(UiElement content, float dpi)
    {
        _content = content;
        _dpi = dpi;
        content.AttachToRoot(this);
    }

    internal event Action? Invalidated;

    internal UiElement? CapturedElement => _capturedElement;
    internal UiElement? FocusedElement => _focusedElement;
    internal float Dpi => _dpi;

    internal void SetDpi(float dpi)
    {
        if (_dpi == dpi)
        {
            return;
        }

        _dpi = dpi;
        InvalidateLayout();
    }

    internal void Draw(in UiDrawContext context, SizeF pixelSize)
    {
        EnsureLayout(pixelSize);
        context.DrawElement(_content);
    }

    internal bool Update(in UiUpdateContext context) => _content.UpdateTree(context);
    internal void Arrange(SizeF pixelSize) => EnsureLayout(pixelSize);

    internal bool HandlePointer(in UiPointerEvent nativeInput)
    {
        UiPointerEvent input = nativeInput with
        {
            Position = new PointF(
                UiDpi.PixelsToDips(nativeInput.Position.X, _dpi),
                UiDpi.PixelsToDips(nativeInput.Position.Y, _dpi)),
        };

        if (input.Kind == UiPointerEventKind.Cancelled)
        {
            bool hadCapture = _capturedElement is not null;
            CancelPointer();
            return hadCapture;
        }

        EnsureLayout(_pixelSize);
        if (input.Kind == UiPointerEventKind.Moved)
        {
            _content.ObservePointerMoveTree(input);
        }

        UiElement? hit = _content.HitTest(input.Position);
        SetHovered(hit);

        UiElement? target = _capturedElement ?? hit;
        if (input.Kind == UiPointerEventKind.Pressed)
        {
            UiElement? focusable = FindFocusable(target);
            if (focusable is not null || target?.PreservesFocusOnPointerPress != true)
            {
                SetFocus(focusable);
            }
        }

        if (input.Kind == UiPointerEventKind.Released)
        {
            _capturedElement = null;
        }

        bool consumed = RoutePointer(target, input);
        if (input.Kind is UiPointerEventKind.Released or UiPointerEventKind.Cancelled)
        {
            target?.SetVisualState(UiVisualState.Pressed, false);
        }

        return consumed;
    }

    internal void CancelPointer()
    {
        UiElement? captured = _capturedElement;
        _capturedElement = null;
        if (captured is null)
        {
            return;
        }

        captured.OnPointerEvent(new UiPointerEvent(UiPointerEventKind.Cancelled, PointF.Empty));
        captured.SetVisualState(UiVisualState.Pressed, false);
    }

    internal void ClearPointer()
    {
        CancelPointer();
        SetHovered(null);
    }

    internal void DisconnectSubtree(UiElement subtree)
    {
        if (IsWithin(_capturedElement, subtree))
        {
            CancelPointer();
        }

        if (IsWithin(_hoveredElement, subtree))
        {
            SetHovered(null);
        }

        if (IsWithin(_focusedElement, subtree))
        {
            SetFocus(null);
        }
    }

    internal bool HandleKey(
        UiKeyEvent input,
        UiElement scope,
        bool wrapFocus,
        bool directionalNavigation)
    {
        if (input.Key == UiKey.Tab)
        {
            MoveFocus(scope, input.Shift ? -1 : 1, wrapFocus);
            return true;
        }

        if (_focusedElement?.OnKeyEvent(input) == true)
        {
            return true;
        }

        if (directionalNavigation && input.Key is UiKey.Left or UiKey.Up or UiKey.Right or UiKey.Down)
        {
            int direction = input.Key is UiKey.Left or UiKey.Up ? -1 : 1;
            MoveFocus(scope, direction, wrap: true);
            return true;
        }

        return false;
    }

    internal void SetFocus(UiElement? element)
    {
        if (ReferenceEquals(_focusedElement, element))
        {
            return;
        }

        UiElement? previous = _focusedElement;
        _focusedElement = element;
        previous?.SetVisualState(UiVisualState.Focused, false);
        element?.SetVisualState(UiVisualState.Focused, true);
        UpdateFocusWithin(previous, element);

        if (element is not null)
        {
            BringIntoView(element);
        }
    }

    internal void InvalidateVisual() => Invalidated?.Invoke();

    internal void InvalidateLayout()
    {
        _layoutDirty = true;
        Invalidated?.Invoke();
    }

    internal float DipsToPixels(float value) => UiDpi.DipsToPixels(value, _dpi);

    private void EnsureLayout(SizeF pixelSize)
    {
        if (_pixelSize != pixelSize)
        {
            _pixelSize = pixelSize;
            _layoutDirty = true;
        }

        if (!_layoutDirty)
        {
            return;
        }

        var size = new SizeF(
            UiDpi.PixelsToDips(pixelSize.Width, _dpi),
            UiDpi.PixelsToDips(pixelSize.Height, _dpi));
        _content.Measure(size);
        _content.Arrange(new RectangleF(PointF.Empty, size));
        _layoutDirty = false;
    }

    private bool RoutePointer(UiElement? target, in UiPointerEvent input)
    {
        bool consumed = false;
        for (UiElement? element = target; element is not null; element = element.Parent)
        {
            UiPointerResult result = element.OnPointerEvent(element.ToLocal(input));
            if (result.NeedsRepaint)
            {
                InvalidateVisual();
            }

            if (result.CapturePointer && input.Kind == UiPointerEventKind.Pressed)
            {
                _capturedElement = element;
                element.SetVisualState(UiVisualState.Pressed, true);
            }

            consumed |= result.Consumed;
            if (result.Consumed)
            {
                break;
            }
        }

        return consumed;
    }

    private void SetHovered(UiElement? element)
    {
        if (ReferenceEquals(_hoveredElement, element))
        {
            return;
        }

        _hoveredElement?.SetVisualState(UiVisualState.Hovered, false);
        _hoveredElement = element;
        _hoveredElement?.SetVisualState(UiVisualState.Hovered, true);
    }

    private static UiElement? FindFocusable(UiElement? element)
    {
        while (element is not null
            && (!element.IsFocusable || element.HasVisualState(UiVisualState.Disabled)))
        {
            element = element.Parent;
        }

        return element;
    }

    private static bool IsWithin(UiElement? element, UiElement subtree)
    {
        for (; element is not null; element = element.Parent)
        {
            if (ReferenceEquals(element, subtree))
            {
                return true;
            }
        }

        return false;
    }

    private void MoveFocus(UiElement scope, int direction, bool wrap)
    {
        List<UiElement> focusable = [];
        CollectFocusable(scope, focusable);
        if (focusable.Count == 0)
        {
            SetFocus(null);
            return;
        }

        int current = focusable.IndexOf(_focusedElement!);
        int next = current < 0
            ? (direction > 0 ? 0 : focusable.Count - 1)
            : current + direction;
        if (wrap)
        {
            next = (next + focusable.Count) % focusable.Count;
        }

        SetFocus(next >= 0 && next < focusable.Count ? focusable[next] : null);
    }

    private static void CollectFocusable(UiElement element, List<UiElement> result)
    {
        if (!element.IsVisible)
        {
            return;
        }

        if (element.IsFocusable && !element.HasVisualState(UiVisualState.Disabled))
        {
            result.Add(element);
        }

        foreach (UiElement child in element.Children)
        {
            CollectFocusable(child, result);
        }
    }

    private static void UpdateFocusWithin(UiElement? previous, UiElement? current)
    {
        HashSet<UiElement> currentAncestors = [];
        for (UiElement? element = current; element is not null; element = element.Parent)
        {
            currentAncestors.Add(element);
        }

        for (UiElement? element = previous; element is not null; element = element.Parent)
        {
            if (!currentAncestors.Contains(element))
            {
                element.SetFocusWithin(false);
            }
        }

        for (UiElement? element = current; element is not null; element = element.Parent)
        {
            element.SetFocusWithin(true);
        }
    }

    private static void BringIntoView(UiElement element)
    {
        for (UiElement? ancestor = element.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor.BringIntoView(element.GetBoundsRelativeTo(ancestor));
        }
    }
}
