using System.Drawing;
using Dameview.UI.Foundation;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

/// <param name="Shortcut">The keys that do the same, shown as a hint.</param>
internal sealed record ContextMenuItem(
    string Label,
    Action Invoke,
    string? Shortcut = null,
    bool IsEnabled = true,
    UiButtonTone Tone = UiButtonTone.Default);

/// <summary>Actions offered at a point, in groups divided by separators.</summary>
/// <remarks>
/// Like a native menu, it holds the keyboard while open, so no shortcut can change what the
/// menu was built for behind its back.
/// </remarks>
internal sealed class ContextMenu : UiElement
{
    private const float Padding = 4.0f;
    private const float SeparatorHeight = 9.0f;
    private const float MinimumWidth = 200.0f;

    private readonly PopupHost _host;
    private readonly Row[] _rows;
    private int _highlighted = -1;

    private ContextMenu(PopupHost host, IEnumerable<IReadOnlyList<ContextMenuItem>> groups)
    {
        _host = host;
        List<Row> rows = [];
        foreach (IReadOnlyList<ContextMenuItem> group in groups)
        {
            if (group.Count == 0)
            {
                continue;
            }

            if (rows.Count > 0)
            {
                AddChild(new Separator());
            }

            foreach (ContextMenuItem item in group)
            {
                var row = new Row(this, item, rows.Count);
                rows.Add(row);
                AddChild(row);
            }
        }

        _rows = [.. rows];
    }

    internal static void Show(
        PopupHost host,
        UiElement anchor,
        PointF point,
        IEnumerable<IReadOnlyList<ContextMenuItem>> groups)
    {
        var menu = new ContextMenu(host, groups);
        if (menu._rows.Length == 0)
        {
            return;
        }

        host.ShowAt(anchor, point, menu, static () => { });
        menu.Root?.CaptureKeyboard(menu);
    }

    internal override bool OnKeyEvent(WindowKeyEvent input)
    {
        switch (input.Key)
        {
            case WindowKey.Escape:
                _host.Close();
                break;

            case WindowKey.Up:
                MoveHighlight(-1);
                break;

            case WindowKey.Down:
                MoveHighlight(1);
                break;

            case WindowKey.Enter or WindowKey.Space when _highlighted >= 0:
                Invoke(_rows[_highlighted].Item);
                break;
        }

        return true;
    }

    internal override void OnKeyboardCaptureLost() => _host.Close();

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float width = MinimumWidth;
        float height = 2.0f * Padding;
        foreach (UiElement child in Children)
        {
            SizeF desired = child.Measure(availableSize);
            width = MathF.Max(width, desired.Width);
            height += desired.Height;
        }

        return new SizeF(width + 2.0f * Padding, height);
    }

    // Children keep their measured heights, so while the popup unrolls it clips them instead
    // of squashing them.
    protected override void ArrangeCore(SizeF finalSize)
    {
        float width = MathF.Max(0.0f, finalSize.Width - 2.0f * Padding);
        float y = Padding;
        foreach (UiElement child in Children)
        {
            child.Arrange(new RectangleF(Padding, y, width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        var surface = new RoundedRectangle(
            new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height),
            UiDesign.ControlCornerRadius,
            UiDesign.ControlCornerRadius);
        context.FillRoundedRectangle(surface, context.Palette.Surface);
        context.DrawRoundedRectangle(surface, context.Palette.SurfaceBorder);
    }

    private void Invoke(ContextMenuItem item)
    {
        // Closed first, so an action that opens something of its own finds the way clear.
        _host.Close();
        item.Invoke();
    }

    private void MoveHighlight(int direction)
    {
        int index = _highlighted;
        for (int step = 0; step < _rows.Length; step++)
        {
            index = index < 0
                ? (direction > 0 ? 0 : _rows.Length - 1)
                : (index + direction + _rows.Length) % _rows.Length;
            if (_rows[index].IsEnabled)
            {
                SetHighlighted(index);
                return;
            }
        }
    }

    private void SetHighlighted(int index)
    {
        if (_highlighted == index)
        {
            return;
        }

        _highlighted = index;
        InvalidateVisual();
    }

    // The pointer and the arrow keys move the same highlight, as in a native menu.
    private void OnRowHoverChanged(Row row, bool hovered)
    {
        if (hovered && row.IsEnabled)
        {
            SetHighlighted(row.Index);
        }
        else if (!hovered && _highlighted == row.Index)
        {
            SetHighlighted(-1);
        }
    }

    private sealed class Row : InteractiveControl
    {
        private const float Height = 32.0f;
        private const float TextPadding = 12.0f;
        private const float ShortcutGap = 32.0f;
        private static readonly UiFont LabelFont = new(UiDesign.BodyFontSize, FontWeight.Medium);
        private static readonly UiFont ShortcutFont = new(12.0f, Alignment: TextAlignment.Trailing);

        private readonly ContextMenu _menu;

        internal Row(ContextMenu menu, ContextMenuItem item, int index)
        {
            _menu = menu;
            Item = item;
            Index = index;
            IsEnabled = item.IsEnabled;
        }

        internal ContextMenuItem Item { get; }
        internal int Index { get; }
        internal override bool IsFocusable => false;
        internal override bool PreservesFocusOnPointerPress => true;

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            float width = 2.0f * TextPadding + MeasureText(Item.Label, LabelFont);
            if (Item.Shortcut is { } shortcut)
            {
                width += ShortcutGap + MeasureText(shortcut, ShortcutFont);
            }

            return new SizeF(width, Height);
        }

        protected override void DrawCore(in UiDrawContext context)
        {
            var background = new RoundedRectangle(
                new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height),
                UiDesign.ControlCornerRadius,
                UiDesign.ControlCornerRadius);
            if (_menu._highlighted == Index)
            {
                context.FillRoundedRectangle(background, context.Palette.ControlHover);
            }

            if (PressedAmount > 0.0f)
            {
                context.FillRoundedRectangle(background, context.Palette.ControlPressed, PressedAmount);
            }

            var text = new Rect(TextPadding, 0.0f, MathF.Max(0.0f, Bounds.Width - 2.0f * TextPadding), Bounds.Height);
            Color4 labelColor = !IsEnabled ? context.Palette.SecondaryText
                : Item.Tone == UiButtonTone.Danger ? context.Palette.ErrorText
                : context.Palette.PrimaryText;
            context.DrawText(Item.Label, LabelFont, text, labelColor, DrawTextOptions.Clip);
            if (Item.Shortcut is { } shortcut)
            {
                context.DrawText(shortcut, ShortcutFont, text, context.Palette.SecondaryText, DrawTextOptions.Clip);
            }
        }

        protected override void OnVisualStateChanged()
        {
            base.OnVisualStateChanged();
            _menu.OnRowHoverChanged(this, HasVisualState(UiVisualState.Hovered));
        }

        protected override void Activate() => _menu.Invoke(Item);

        private float MeasureText(string text, UiFont font) => MathF.Ceiling(TextLayouts
            .Get(text, font, new SizeF(10_000.0f, Height))
            .Metrics.WidthIncludingTrailingWhitespace);
    }

    private sealed class Separator : UiElement
    {
        internal override bool IsHitTestVisible => false;

        protected override SizeF MeasureCore(SizeF availableSize) => new(0.0f, SeparatorHeight);

        protected override void DrawCore(in UiDrawContext context)
        {
            context.FillRoundedRectangle(
                new RoundedRectangle(
                    new RectangleF(UiDesign.Spacing, MathF.Floor(Bounds.Height / 2.0f), Bounds.Width - 2.0f * UiDesign.Spacing, 1.0f),
                    0.0f,
                    0.0f),
                context.Palette.SurfaceBorder);
        }
    }
}
