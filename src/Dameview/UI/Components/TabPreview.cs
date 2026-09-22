using System.Drawing;
using Dameview.Imaging.Loading;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Dameview.UI.Presentation;
using Vortice.Direct2D1;

namespace Dameview.UI.Components;

internal sealed class TabPreview : UiElement, IDisposable
{
    private const double HoverDelaySeconds = 0.45;
    private const float PreviewWidthDips = 280.0f;
    private const float PreviewHeightDips = 190.0f;
    private const float MarginDips = 8.0f;
    private const float GapDips = 4.0f;
    private const float PaddingDips = 8.0f;

    private readonly IThumbnailImageLoader _thumbnailLoader;
    private readonly PreviewPanel _panel;
    private IDisposable? _request;
    private CachedBitmapLease? _lease;
    private string? _path;
    private RectangleF _anchor;
    private double _hoverSeconds;
    private bool _waiting;
    private int _version;

    internal TabPreview(IThumbnailImageLoader thumbnailLoader)
    {
        _thumbnailLoader = thumbnailLoader;
        _panel = new PreviewPanel(this);
        AddChild(_panel);
    }

    internal override bool IsHitTestVisible => false;

    internal void Show(string path, RectangleF anchor)
    {
        Reset();
        _path = path;
        _anchor = anchor;
        _hoverSeconds = 0.0;
        _waiting = true;
        InvalidateLayout();
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
        _panel.IsPresent = false;
    }

    protected override SizeF MeasureCore(SizeF availableSize) => availableSize;

    protected override void ArrangeCore(SizeF finalSize) => _panel.Arrange(GetPanelBounds(finalSize, _anchor));

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        // The panel draws the thumbnail until it has faded out, so it is released only after.
        if (_lease is not null && _path is null && !_panel.IsVisible)
        {
            _lease.Dispose();
            _lease = null;
        }

        if (!_waiting || _path is null)
        {
            return false;
        }

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
            lease => CompleteThumbnail(version, lease));
        if (_version == version && _lease is null)
        {
            _request = request;
        }
        else
        {
            request.Dispose();
        }

        return false;
    }

    public void Dispose() => Reset();

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

    private void CompleteThumbnail(int version, CachedBitmapLease lease)
    {
        if (_version != version || _path is null)
        {
            lease.Dispose();
            return;
        }

        _request?.Dispose();
        _request = null;
        _lease?.Dispose();
        _lease = lease;
        _panel.IsPresent = true;
    }

    private void Reset()
    {
        _version++;
        _request?.Dispose();
        _request = null;
        _lease?.Dispose();
        _lease = null;
        _path = null;
        _waiting = false;
        _panel.IsPresent = false;
        _panel.FinishTransition();
    }

    private sealed class PreviewPanel : UiElement
    {
        private readonly TabPreview _owner;

        internal PreviewPanel(TabPreview owner)
        {
            _owner = owner;
            Transition = new UiTransition(Fade: true, HiddenScale: 0.9f, Response: 24.0);
            IsPresent = false;
        }

        protected override void DrawCore(in UiDrawContext context)
        {
            ID2D1Bitmap1? bitmap = _owner._lease?.Bitmap.Bitmap;
            if (bitmap is null || Bounds.Width <= 2.0f * PaddingDips || Bounds.Height <= 2.0f * PaddingDips)
            {
                return;
            }

            var bounds = new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height);
            var panel = new RoundedRectangle(bounds, UiDesign.PanelCornerRadius, UiDesign.PanelCornerRadius);
            context.FillRoundedRectangle(panel, context.Palette.OverlaySurface);
            context.DrawRoundedRectangle(panel, context.Palette.SurfaceBorder);
            context.DrawBitmapFitted(bitmap, RectangleF.Inflate(bounds, -PaddingDips, -PaddingDips));
        }
    }
}
