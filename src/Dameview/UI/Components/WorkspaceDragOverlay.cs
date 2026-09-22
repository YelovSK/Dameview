using System.Drawing;
using Dameview.Imaging.Loading;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Dameview.UI.Presentation;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class WorkspaceDragOverlay : UiElement, IDisposable
{
    private const float GhostWidth = 132.0f;
    private const float GhostThumbnailHeight = 88.0f;
    private const float GhostLabelHeight = 26.0f;
    private const float GhostPadding = 6.0f;
    private const float GhostOffset = 14.0f;
    private const float GhostOpacity = 0.92f;
    private const float PreviewInset = 6.0f;
    private const float PreviewFillOpacity = 0.18f;
    private const double PreviewResponse = 28.0;
    private static readonly UiFont LabelFont = new(12.0f, FontWeight.SemiBold, TextAlignment.Center, Ellipsis: true);

    private readonly IThumbnailImageLoader _thumbnailLoader;
    // Where the dragged item would land. It glides between landing spots and fades
    // in and out as the pointer enters or leaves one.
    private readonly AnimatedRectangle _preview = new(PreviewResponse);
    private readonly AnimatedFloat _previewOpacity = new(0.0f, PreviewResponse);
    private bool _previewPlaced;
    private string _label = string.Empty;
    private bool _hasThumbnail;
    private IDisposable? _thumbnailRequest;
    private CachedBitmapLease? _thumbnail;
    private PointF _pointer;
    private RectangleF _insertionMarker;

    internal WorkspaceDragOverlay(IThumbnailImageLoader thumbnailLoader)
    {
        _thumbnailLoader = thumbnailLoader;
        IsVisible = false;
    }

    internal override bool IsHitTestVisible => false;

    internal void Show(string label, string? imagePath)
    {
        ReleaseThumbnail();
        _label = label;
        _hasThumbnail = imagePath is not null;
        if (imagePath is not null)
        {
            _thumbnailRequest = _thumbnailLoader.Request(
                imagePath,
                ThumbnailPriority.Foreground,
                lease =>
                {
                    _thumbnail = lease;
                    InvalidateVisual();
                });
        }

        IsVisible = true;
    }

    /// <param name="insertionMarker">Where a tab would be inserted, or empty.</param>
    /// <param name="landingBounds">The area a split would give the dragged item, or empty.</param>
    internal void Update(PointF pointer, RectangleF insertionMarker, RectangleF landingBounds)
    {
        _pointer = pointer;
        _insertionMarker = insertionMarker;
        if (landingBounds.IsEmpty)
        {
            _previewOpacity.SetTarget(0.0f);
        }
        else
        {
            var preview = RectangleF.Inflate(landingBounds, -PreviewInset, -PreviewInset);
            // The first preview of a drag has nowhere to glide from, so it appears in place.
            if (_previewPlaced)
            {
                _preview.SetTarget(preview);
            }
            else
            {
                _preview.SetValue(preview);
                _previewPlaced = true;
            }

            _previewOpacity.SetTarget(1.0f);
        }

        InvalidateVisual();
    }

    internal void Hide()
    {
        IsVisible = false;
        ReleaseThumbnail();
        _insertionMarker = RectangleF.Empty;
        _previewOpacity.SetValue(0.0f);
        _previewPlaced = false;
    }

    protected override SizeF MeasureCore(SizeF availableSize) => availableSize;

    protected override bool UpdateCore(in UiUpdateContext context) =>
        _preview.Update(context) | _previewOpacity.Update(context);

    protected override void DrawCore(in UiDrawContext context)
    {
        if (!_insertionMarker.IsEmpty)
        {
            context.FillRoundedRectangle(
                new RoundedRectangle(_insertionMarker, 1.5f, 1.5f),
                context.Palette.Accent);
        }

        float previewOpacity = _previewOpacity.Current;
        if (previewOpacity > 0.0f)
        {
            var preview = new RoundedRectangle(
                _preview.Current,
                UiDesign.PanelCornerRadius,
                UiDesign.PanelCornerRadius);
            context.FillRoundedRectangle(
                preview,
                context.Palette.Accent,
                PreviewFillOpacity * previewOpacity);
            context.DrawRoundedRectangle(
                preview,
                context.Palette.Accent,
                strokeWidthPixels: 2.0f,
                opacity: previewOpacity);
        }

        DrawGhost(context.WithOpacity(GhostOpacity));
    }

    public void Dispose() => ReleaseThumbnail();

    private void ReleaseThumbnail()
    {
        _thumbnailRequest?.Dispose();
        _thumbnailRequest = null;
        _thumbnail?.Dispose();
        _thumbnail = null;
    }

    private void DrawGhost(in UiDrawContext context)
    {
        RectangleF bounds = GetGhostBounds();
        var card = new RoundedRectangle(
            bounds,
            UiDesign.ControlCornerRadius,
            UiDesign.ControlCornerRadius);
        context.FillRoundedRectangle(card, context.Palette.OverlaySurface);
        context.DrawRoundedRectangle(card, context.Palette.Accent);

        // The card keeps its size while the thumbnail loads, so it does not jump when it arrives.
        if (_thumbnail?.Bitmap.Bitmap is { } bitmap)
        {
            context.DrawBitmapFitted(
                bitmap,
                new RectangleF(
                    bounds.X + GhostPadding,
                    bounds.Y + GhostPadding,
                    bounds.Width - 2.0f * GhostPadding,
                    GhostThumbnailHeight));
        }

        context.DrawText(
            _label,
            LabelFont,
            new Rect(
                bounds.X + GhostPadding,
                bounds.Bottom - GhostLabelHeight,
                bounds.Width - 2.0f * GhostPadding,
                GhostLabelHeight),
            context.Palette.PrimaryText,
            DrawTextOptions.Clip);
    }

    private RectangleF GetGhostBounds()
    {
        float height = _hasThumbnail
            ? GhostPadding + GhostThumbnailHeight + GhostLabelHeight
            : GhostLabelHeight;
        float x = Math.Clamp(
            _pointer.X + GhostOffset,
            0.0f,
            MathF.Max(0.0f, Bounds.Width - GhostWidth));
        float y = Math.Clamp(
            _pointer.Y + GhostOffset,
            0.0f,
            MathF.Max(0.0f, Bounds.Height - height));
        return new RectangleF(x, y, MathF.Min(GhostWidth, Bounds.Width), MathF.Min(height, Bounds.Height));
    }
}
