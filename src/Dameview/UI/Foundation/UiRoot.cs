using System.Drawing;
using Dameview.Win32.Input;

namespace Dameview.UI.Foundation;

/// <summary>Owns layout, focus, hover, capture, and event routing for a UI tree.</summary>
internal sealed class UiRoot
{
    private readonly UiElement _content;
    private UiElement? _hoveredElement;
    private SizeF _pixelSize;
    private bool _layoutDirty = true;

    internal UiRoot(UiElement content, float dpi)
    {
        _content = content;
        Dpi = dpi;
        content.AttachToRoot(this);
    }

    internal event Action? Invalidated;
    internal event Action<WindowCursor>? CursorChanged;
    internal event Action<UiElement?>? PointerPressed;

    internal UiElement? CapturedElement { get; private set; }
    internal UiElement? FocusedElement { get; private set; }
    internal float Dpi { get; private set; }

    /// <summary>Updates the root DPI and invalidates layout when it changes.</summary>
    internal void SetDpi(float dpi)
    {
        if (Dpi == dpi)
        {
            return;
        }

        Dpi = dpi;
        InvalidateLayout();
    }

    /// <summary>Lays out the tree for the pixel surface and draws its content.</summary>
    internal void Draw(in UiDrawContext context, SizeF pixelSize)
    {
        EnsureLayout(pixelSize);
        context.DrawElement(_content);
    }

    /// <summary>Advances animations in the UI tree.</summary>
    /// <returns><see langword="true"/> when another update may be needed.</returns>
    internal bool Update(in UiUpdateContext context) => _content.UpdateTree(context);
    /// <summary>Ensures the tree is arranged for the specified pixel surface.</summary>
    internal void Arrange(SizeF pixelSize) => EnsureLayout(pixelSize);

    /// <summary>Converts and routes a native pointer event through the UI tree.</summary>
    /// <returns><see langword="true"/> when an element consumed the event.</returns>
    internal bool HandlePointer(in WindowPointerEvent nativeInput)
    {
        WindowPointerEvent input = nativeInput with
        {
            Position = new PointF(
                UiDpi.PixelsToDips(nativeInput.Position.X, Dpi),
                UiDpi.PixelsToDips(nativeInput.Position.Y, Dpi)),
        };

        if (input.Kind == WindowPointerEventKind.Cancelled)
        {
            bool hadCapture = CapturedElement is not null;
            CancelPointer();
            return hadCapture;
        }

        EnsureLayout(_pixelSize);
        if (input.Kind == WindowPointerEventKind.Moved)
        {
            _content.ObservePointerMoveTree(input);
        }

        UiElement? hit = _content.HitTest(input.Position);
        SetHovered(hit);

        UiElement? target = CapturedElement ?? hit;
        if (input.Kind == WindowPointerEventKind.Pressed)
        {
            PointerPressed?.Invoke(target);
            UiElement? focusable = FindFocusable(target);
            if (focusable is not null || target?.PreservesFocusOnPointerPress != true)
            {
                SetFocus(focusable);
            }
        }

        if (input.Kind == WindowPointerEventKind.Released)
        {
            CapturedElement = null;
        }

        bool consumed = RoutePointer(target, input);
        if (input.Kind is WindowPointerEventKind.Released or WindowPointerEventKind.Cancelled)
        {
            target?.SetVisualState(UiVisualState.Pressed, false);
        }

        UpdateCursor();

        return consumed;
    }

    /// <summary>Cancels the current pointer capture, if any.</summary>
    internal void CancelPointer()
    {
        UiElement? captured = CapturedElement;
        CapturedElement = null;
        if (captured is null)
        {
            return;
        }

        captured.OnPointerEvent(new WindowPointerEvent(WindowPointerEventKind.Cancelled, PointF.Empty));
        captured.SetVisualState(UiVisualState.Pressed, false);
        UpdateCursor();
    }

    /// <summary>Cancels capture and clears hover state, typically after pointer loss.</summary>
    internal void ClearPointer()
    {
        CancelPointer();
        SetHovered(null);
        UpdateCursor();
    }

    internal void DisconnectSubtree(UiElement subtree)
    {
        if (IsWithin(CapturedElement, subtree))
        {
            CancelPointer();
        }

        if (IsWithin(_hoveredElement, subtree))
        {
            SetHovered(null);
        }

        if (IsWithin(FocusedElement, subtree))
        {
            SetFocus(null);
        }
    }

    /// <summary>Routes a key event and optionally performs focus navigation.</summary>
    /// <returns><see langword="true"/> when the key was handled or used for navigation.</returns>
    internal bool HandleKey(
        WindowKeyEvent input,
        UiElement scope,
        bool wrapFocus,
        bool directionalNavigation)
    {
        if (input.Key == WindowKey.Tab)
        {
            MoveFocus(scope, input.Shift ? -1 : 1, wrapFocus);
            return true;
        }

        bool bubbleWithinScope = IsWithin(FocusedElement, scope);
        for (UiElement? element = FocusedElement; element is not null; element = element.Parent)
        {
            if (element.OnKeyEvent(input))
            {
                return true;
            }

            if (!bubbleWithinScope || ReferenceEquals(element, scope))
            {
                break;
            }
        }

        if (directionalNavigation && input.Key is WindowKey.Left or WindowKey.Up or WindowKey.Right or WindowKey.Down)
        {
            int direction = input.Key is WindowKey.Left or WindowKey.Up ? -1 : 1;
            MoveFocus(scope, direction, wrap: true);
            return true;
        }

        return false;
    }

    /// <summary>Routes committed text input from the focused element through its ancestors.</summary>
    internal bool HandleTextInput(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        for (UiElement? element = FocusedElement; element is not null; element = element.Parent)
        {
            if (element.OnTextInput(text))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Moves keyboard focus to an element or clears focus when <see langword="null"/>.</summary>
    internal void SetFocus(UiElement? element)
    {
        if (ReferenceEquals(FocusedElement, element))
        {
            return;
        }

        UiElement? previous = FocusedElement;
        FocusedElement = element;
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

    internal float DipsToPixels(float value) => UiDpi.DipsToPixels(value, Dpi);

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
            UiDpi.PixelsToDips(pixelSize.Width, Dpi),
            UiDpi.PixelsToDips(pixelSize.Height, Dpi));
        _content.Measure(size);
        _content.Arrange(new RectangleF(PointF.Empty, size));
        _layoutDirty = false;
    }

    private bool RoutePointer(UiElement? target, in WindowPointerEvent input)
    {
        bool consumed = false;
        for (UiElement? element = target; element is not null; element = element.Parent)
        {
            UiPointerResult result = element.OnPointerEvent(element.ToLocal(input));
            if (result.NeedsRepaint)
            {
                InvalidateVisual();
            }

            if (result.CapturePointer && input.Kind == WindowPointerEventKind.Pressed)
            {
                CapturedElement = element;
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

    private void UpdateCursor()
    {
        WindowCursor cursor = (CapturedElement ?? _hoveredElement)?.Cursor ?? WindowCursor.Default;
        if (cursor == _cursor)
        {
            return;
        }

        _cursor = cursor;
        CursorChanged?.Invoke(cursor);
    }

    private WindowCursor _cursor;

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

        int current = focusable.IndexOf(FocusedElement!);
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
