using System.Drawing;
using Dameview.Imaging;
using Dameview.Rendering;
using Dameview.UI.Animation;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class TabPreview : UiElement, IDisposable
{
    private const double HoverDelaySeconds = 0.45;
    private const float PreviewWidthDips = 280.0f;
    private const float PreviewHeightDips = 190.0f;
    private const float MarginDips = 8.0f;
    private const float GapDips = 4.0f;
    private const float PaddingDips = 8.0f;
    private const float InitialScale = 0.9f;

    private readonly ID2D1DeviceContext _deviceContext;
    private readonly IThumbnailLoader _thumbnailLoader;
    private IDisposable? _request;
    private ID2D1Bitmap1? _bitmap;
    private string? _path;
    private RectangleF _anchor;
    private double _hoverSeconds;
    private bool _waiting;
    private int _version;
    private AnimatedFloat _visibility = new(0.0f, 24.0);

    internal TabPreview(ID2D1DeviceContext deviceContext, IThumbnailLoader thumbnailLoader)
    {
        _deviceContext = deviceContext;
        _thumbnailLoader = thumbnailLoader;
    }

    internal override bool IsHitTestVisible => false;

    internal void Show(string path, RectangleF anchor)
    {
        Reset();
        _path = path;
        _anchor = anchor;
        _hoverSeconds = 0.0;
        _waiting = true;
        InvalidateVisual();
    }

    internal void Hide()
    {
        if (_path is null && !_waiting && _request is null)
        {
            return;
        }

        _version++;
        _request?.Dispose();
        _request = null;
        _path = null;
        _waiting = false;
        _visibility.SetTarget(0.0f);
        InvalidateVisual();
    }

    protected override SizeF MeasureCore(SizeF availableSize) => availableSize;

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        bool continues = _visibility.Update(context);
        if (_bitmap is not null && _path is null && _visibility.Current == 0.0f)
        {
            _bitmap.Dispose();
            _bitmap = null;
        }

        if (_waiting && _path is not null)
        {
            _hoverSeconds += context.ElapsedSeconds;
            if (_hoverSeconds < HoverDelaySeconds)
            {
                return true;
            }

            _waiting = false;
            int version = _version;
            string path = _path;
            IDisposable request = _thumbnailLoader.Request(
                path,
                ThumbnailPriority.Foreground,
                image => CompleteThumbnail(version, image));
            if (_version == version && _bitmap is null)
            {
                _request = request;
            }
            else
            {
                request.Dispose();
            }
        }

        return continues;
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        if (_bitmap is null)
        {
            return;
        }

        RectangleF panelBounds = AnimateBounds(
            GetPanelBounds(Bounds.Size, _anchor),
            InitialScale + (1.0f - InitialScale) * _visibility.Current);
        if (panelBounds.Width <= 2.0f * PaddingDips || panelBounds.Height <= 2.0f * PaddingDips)
        {
            return;
        }

        UiDrawContext previewContext = context.WithOpacity(_visibility.Current);
        var panel = new RoundedRectangle(
            panelBounds,
            UiDesign.PanelCornerRadius,
            UiDesign.PanelCornerRadius);
        previewContext.FillRoundedRectangle(panel, context.Palette.OverlaySurface);
        previewContext.DrawRoundedRectangle(panel, context.Palette.SurfaceBorder);

        float availableWidth = panelBounds.Width - 2.0f * PaddingDips;
        float availableHeight = panelBounds.Height - 2.0f * PaddingDips;
        float scale = MathF.Min(
            availableWidth / _bitmap.PixelSize.Width,
            availableHeight / _bitmap.PixelSize.Height);
        float width = _bitmap.PixelSize.Width * scale;
        float height = _bitmap.PixelSize.Height * scale;
        var destination = new Rect(
            panelBounds.X + (panelBounds.Width - width) / 2.0f,
            panelBounds.Y + (panelBounds.Height - height) / 2.0f,
            width,
            height);
        previewContext.RenderTarget.DrawBitmap(
            _bitmap,
            destination,
            previewContext.Opacity,
            BitmapInterpolationMode.Linear,
            new Rect(0.0f, 0.0f, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height));
    }

    public void Dispose() => Reset();

    private static RectangleF AnimateBounds(RectangleF bounds, float scale)
    {
        float width = bounds.Width * scale;
        float height = bounds.Height * scale;
        return new RectangleF(
            bounds.X + (bounds.Width - width) / 2.0f,
            bounds.Y,
            width,
            height);
    }

    private static RectangleF GetPanelBounds(SizeF availableSize, RectangleF anchor)
    {
        float width = MathF.Min(PreviewWidthDips, MathF.Max(0.0f, availableSize.Width - 2.0f * MarginDips));
        float height = MathF.Min(
            PreviewHeightDips,
            MathF.Max(0.0f, availableSize.Height - anchor.Bottom - GapDips - MarginDips));
        float x = Math.Clamp(
            anchor.Left + (anchor.Width - width) / 2.0f,
            MarginDips,
            MathF.Max(MarginDips, availableSize.Width - MarginDips - width));
        return new RectangleF(x, anchor.Bottom + GapDips, width, height);
    }

    private void CompleteThumbnail(int version, DecodedImage image)
    {
        if (_version != version || _path is null)
        {
            return;
        }

        _request?.Dispose();
        _request = null;
        _bitmap = D2DBitmapFactory.Create(_deviceContext, image);
        _visibility.SetTarget(1.0f);
        InvalidateVisual();
    }

    private void Reset()
    {
        _version++;
        _request?.Dispose();
        _request = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _path = null;
        _waiting = false;
        _visibility = new AnimatedFloat(0.0f, 24.0);
        InvalidateVisual();
    }
}
