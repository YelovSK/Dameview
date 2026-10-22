using System.Diagnostics;
using System.Drawing;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Dameview.UI.Foundation;

/// <summary>A debugging aid that tints every redrawn area for a moment, to show what each frame redraws.</summary>
internal sealed class UiRedrawFlashes
{
    private static readonly long FadeTicks = Stopwatch.Frequency / 6;
    private static readonly Color4 Color = new(1.0f, 0.1f, 0.6f, 1.0f);

    private readonly List<Flash> _flashes = [];

    internal bool Enabled { get; set; }

    /// <summary>Whether flashes are still fading, which needs further frames.</summary>
    internal bool IsFading => _flashes.Count > 0;

    /// <summary>Starts flashes where the tree changed, and adds the fading ones to what gets redrawn.</summary>
    internal void Track(UiDirtyRegion changed)
    {
        if (!Enabled && _flashes.Count == 0)
        {
            return;
        }

        // The fading flashes are added after new ones start from what the tree changed, or each
        // flash would keep starting new ones.
        long now = Stopwatch.GetTimestamp();
        int fading = _flashes.Count;
        if (Enabled)
        {
            // An area redrawn every frame restarts its flash, since stacked flashes would turn it solid.
            foreach (Rectangle rectangle in changed.Rectangles)
            {
                int existing = IndexOf(rectangle);
                if (existing >= 0)
                {
                    _flashes[existing] = new Flash(rectangle, now);
                }
                else
                {
                    _flashes.Add(new Flash(rectangle, now));
                }
            }
        }

        // A finished flash is redrawn once more to erase it.
        for (int index = fading - 1; index >= 0; index--)
        {
            changed.Add(_flashes[index].Bounds);
            if (now - _flashes[index].Started >= FadeTicks)
            {
                _flashes.RemoveAt(index);
            }
        }
    }

    internal void Draw(ID2D1RenderTarget target, ID2D1SolidColorBrush brush, float dpi)
    {
        if (_flashes.Count == 0)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        float stroke = UiDpi.PixelsToDips(1.0f, dpi);
        brush.Color = Color;
        foreach (Flash flash in _flashes)
        {
            float fade = 1.0f - Math.Clamp((float)(now - flash.Started) / FadeTicks, 0.0f, 1.0f);
            if (fade <= 0.0f)
            {
                continue;
            }

            RectangleF bounds = UiDpi.PixelsToDips(flash.Bounds, dpi);
            brush.Opacity = 0.3f * fade;
            target.FillRectangle(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), brush);
            // A stroke straddles its edge, so it is drawn inset to stay inside what gets redrawn to erase it.
            brush.Opacity = fade;
            target.DrawRectangle(
                new Rect(
                    bounds.X + (stroke / 2.0f),
                    bounds.Y + (stroke / 2.0f),
                    bounds.Width - stroke,
                    bounds.Height - stroke),
                brush,
                stroke);
        }
    }

    private int IndexOf(Rectangle bounds)
    {
        for (int index = 0; index < _flashes.Count; index++)
        {
            if (_flashes[index].Bounds == bounds)
            {
                return index;
            }
        }

        return -1;
    }

    private readonly record struct Flash(Rectangle Bounds, long Started);
}
