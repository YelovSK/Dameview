using System.Drawing;
using Dameview.UI.Foundation;
using Dameview.Win32.Input;
using Vortice.Direct2D1;

namespace Dameview.UI.Layout;

internal sealed class Scrollbar : UiElement
{
    private const float TrackPadding = 2.0f;
    private const float ThumbMinimumHeight = 24.0f;
    private readonly Action<float> _setOffset;
    private UiOrientation _orientation;
    private float _contentExtent;
    private float _viewportExtent;
    private float _offset;
    private bool _dragging;
    private float _dragOffset;

    internal Scrollbar(Action<float> setOffset, UiOrientation orientation = UiOrientation.Vertical)
    {
        _setOffset = setOffset;
        _orientation = orientation;
    }

    internal bool HasOverflow => MaximumOffset > 0.0f;
    internal float TrackHeight => MathF.Max(0.0f, LayoutSize.Height - 2.0f * TrackPadding);
    internal float ThumbHeight => Math.Clamp(
        _viewportExtent * _viewportExtent / MathF.Max(_contentExtent, 1.0f),
        MathF.Min(ThumbMinimumHeight, TrackHeight),
        TrackHeight);
    internal RectangleF ThumbBounds => GetThumbBounds();

    internal override bool IsHitTestVisible => HasOverflow;
    internal override bool PreservesFocusOnPointerPress => true;
    internal override WindowCursor Cursor => WindowCursor.Pointer;

    internal void SetMetrics(float contentExtent, float viewportExtent, float offset)
    {
        _contentExtent = MathF.Max(0.0f, contentExtent);
        _viewportExtent = MathF.Max(0.0f, viewportExtent);
        _offset = Math.Clamp(offset, 0.0f, MaximumOffset);
    }

    internal void SetOrientation(UiOrientation orientation)
    {
        if (_orientation == orientation)
        {
            return;
        }

        _orientation = orientation;
        _dragging = false;
        InvalidateVisual();
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        if (!HasOverflow)
        {
            return;
        }

        context.FillRoundedRectangle(
            new RoundedRectangle(
                FromLayoutBounds(new RectangleF(
                    4.0f,
                    TrackPadding,
                    MathF.Max(0.0f, LayoutSize.Width - 8.0f),
                    TrackHeight)),
                3.0f,
                3.0f),
            context.Palette.ControlHover,
            0.55f);
        context.FillRoundedRectangle(
            new RoundedRectangle(GetThumbBounds(), 4.0f, 4.0f),
            _dragging ? context.Palette.Accent : context.Palette.SecondaryText,
            _dragging ? 0.95f : 0.75f);
    }

    internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
    {
        PointF position = ToLayoutPoint(input.Position);
        switch (input.Kind)
        {
            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                RectangleF thumb = GetLayoutThumbBounds();
                _dragging = true;
                _dragOffset = thumb.Contains(position)
                    ? position.Y - thumb.Y
                    : thumb.Height / 2.0f;
                if (!thumb.Contains(position))
                {
                    SetOffsetFromThumbTop(position.Y - _dragOffset - TrackPadding);
                }

                return new UiPointerResult(Consumed: true, CapturePointer: true, NeedsRepaint: true);

            case WindowPointerEventKind.Moved when _dragging:
                SetOffsetFromThumbTop(position.Y - _dragOffset - TrackPadding);
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Released when _dragging:
                _dragging = false;
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Cancelled when _dragging:
                _dragging = false;
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            default:
                return default;
        }
    }

    private float MaximumOffset => MathF.Max(0.0f, _contentExtent - _viewportExtent);

    private RectangleF GetThumbBounds() => FromLayoutBounds(GetLayoutThumbBounds());

    private RectangleF GetLayoutThumbBounds()
    {
        float travel = MathF.Max(0.0f, TrackHeight - ThumbHeight);
        float ratio = MaximumOffset <= 0.0f ? 0.0f : _offset / MaximumOffset;
        return new RectangleF(
            2.0f,
            TrackPadding + travel * ratio,
            MathF.Max(0.0f, LayoutSize.Width - 4.0f),
            ThumbHeight);
    }

    private void SetOffsetFromThumbTop(float thumbTop)
    {
        float travel = TrackHeight - ThumbHeight;
        float ratio = travel <= 0.0f ? 0.0f : thumbTop / travel;
        _setOffset(Math.Clamp(ratio, 0.0f, 1.0f) * MaximumOffset);
    }

    private SizeF LayoutSize => _orientation == UiOrientation.Vertical
        ? Bounds.Size
        : new SizeF(Bounds.Height, Bounds.Width);

    private PointF ToLayoutPoint(PointF point) => _orientation == UiOrientation.Vertical
        ? point
        : new PointF(point.Y, point.X);

    private RectangleF FromLayoutBounds(RectangleF bounds) => _orientation == UiOrientation.Vertical
        ? bounds
        : new RectangleF(bounds.Y, bounds.X, bounds.Height, bounds.Width);
}
