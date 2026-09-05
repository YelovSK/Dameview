using System.Drawing;
using Dameview.Platform;
using Vortice.Direct2D1;

namespace Dameview.UI.Layout;

internal sealed class Scrollbar : UiElement
{
    private const float TrackPadding = 2.0f;
    private const float ThumbMinimumHeight = 24.0f;
    private readonly Action<float> _setOffset;
    private float _contentExtent;
    private float _viewportExtent;
    private float _offset;
    private bool _dragging;
    private float _dragOffset;

    internal Scrollbar(Action<float> setOffset)
    {
        _setOffset = setOffset;
    }

    internal bool HasOverflow => MaximumOffset > 0.0f;
    internal float TrackHeight => MathF.Max(0.0f, Bounds.Height - 2.0f * TrackPadding);
    internal float ThumbHeight => Math.Clamp(
        _viewportExtent * _viewportExtent / MathF.Max(_contentExtent, 1.0f),
        MathF.Min(ThumbMinimumHeight, TrackHeight),
        TrackHeight);
    internal RectangleF ThumbBounds => GetThumbBounds();

    internal override bool IsHitTestVisible => HasOverflow;
    internal override bool PreservesFocusOnPointerPress => true;

    internal void SetMetrics(float contentExtent, float viewportExtent, float offset)
    {
        _contentExtent = MathF.Max(0.0f, contentExtent);
        _viewportExtent = MathF.Max(0.0f, viewportExtent);
        _offset = Math.Clamp(offset, 0.0f, MaximumOffset);
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        if (!HasOverflow)
        {
            return;
        }

        context.FillRoundedRectangle(
            new RoundedRectangle(
                new RectangleF(4.0f, TrackPadding, MathF.Max(0.0f, Bounds.Width - 8.0f), TrackHeight),
                3.0f,
                3.0f),
            context.Palette.ControlHover,
            0.55f);
        context.FillRoundedRectangle(
            new RoundedRectangle(GetThumbBounds(), 4.0f, 4.0f),
            _dragging ? context.Palette.Accent : context.Palette.SecondaryText,
            _dragging ? 0.95f : 0.75f);
    }

    internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
    {
        switch (input.Kind)
        {
            case UiPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                RectangleF thumb = GetThumbBounds();
                _dragging = true;
                _dragOffset = thumb.Contains(input.Position)
                    ? input.Position.Y - thumb.Y
                    : thumb.Height / 2.0f;
                if (!thumb.Contains(input.Position))
                {
                    SetOffsetFromThumbTop(input.Position.Y - _dragOffset - TrackPadding);
                }

                return new UiPointerResult(Consumed: true, CapturePointer: true, NeedsRepaint: true);

            case UiPointerEventKind.Moved when _dragging:
                SetOffsetFromThumbTop(input.Position.Y - _dragOffset - TrackPadding);
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case UiPointerEventKind.Released when _dragging:
                _dragging = false;
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case UiPointerEventKind.Cancelled when _dragging:
                _dragging = false;
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            default:
                return default;
        }
    }

    private float MaximumOffset => MathF.Max(0.0f, _contentExtent - _viewportExtent);

    private RectangleF GetThumbBounds()
    {
        float travel = MathF.Max(0.0f, TrackHeight - ThumbHeight);
        float ratio = MaximumOffset <= 0.0f ? 0.0f : _offset / MaximumOffset;
        return new RectangleF(
            2.0f,
            TrackPadding + travel * ratio,
            MathF.Max(0.0f, Bounds.Width - 4.0f),
            ThumbHeight);
    }

    private void SetOffsetFromThumbTop(float thumbTop)
    {
        float travel = TrackHeight - ThumbHeight;
        float ratio = travel <= 0.0f ? 0.0f : thumbTop / travel;
        _setOffset(Math.Clamp(ratio, 0.0f, 1.0f) * MaximumOffset);
    }
}
