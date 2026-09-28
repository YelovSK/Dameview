using System.Drawing;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

/// <summary>Shows the tooltip of the hovered element once the pointer has rested on it.</summary>
internal sealed class ToolTipHost : UiElement
{
    private const double DelaySeconds = 0.5;
    private const float PaddingXDips = 8.0f;
    private const float PaddingYDips = 4.0f;
    private const float GapDips = 6.0f;
    private const float MarginDips = 8.0f;
    private const float MaximumWidthDips = 320.0f;
    private static readonly UiFont Font = new(12.0f);

    private readonly Bubble _bubble;
    // The tooltip being waited on or shown, and what it describes in this host's coordinates,
    // which span the whole window.
    private string? _text;
    private RectangleF _anchor;
    private bool _waiting;
    private double _waitedSeconds;

    internal ToolTipHost()
    {
        _bubble = new Bubble();
        AddChild(_bubble);
    }

    internal override bool IsHitTestVisible => false;

    internal void Show(UiElement? hovered)
    {
        UiElement? owner = hovered;
        while (owner is not null && owner.ToolTip is null)
        {
            owner = owner.Parent;
        }

        string? text = owner?.ToolTip?.Text;
        RectangleF anchor = owner is null ? RectangleF.Empty : GetAnchor(owner);
        if (text is not null && text == _text && anchor == _anchor && (_waiting || _bubble.IsPresent))
        {
            return;
        }

        _text = text;
        _anchor = anchor;
        _waiting = text is not null;
        _waitedSeconds = 0.0;
        _bubble.IsPresent = false;
    }

    internal void Hide()
    {
        _text = null;
        _waiting = false;
        _bubble.IsPresent = false;
    }

    private static RectangleF GetAnchor(UiElement owner)
    {
        RectangleF anchor = owner.ToolTip?.Bounds ?? new RectangleF(PointF.Empty, owner.Bounds.Size);
        for (UiElement? element = owner; element is not null; element = element.Parent)
        {
            anchor.Offset(element.Bounds.Location);
        }

        return anchor;
    }

    protected override SizeF MeasureCore(SizeF availableSize) => availableSize;

    protected override void ArrangeCore(SizeF finalSize)
    {
        if (_bubble.Text.Length == 0)
        {
            return;
        }

        TextMetrics text = TextLayouts.Get(_bubble.Text, Font, new SizeF(MaximumWidthDips, float.MaxValue)).Metrics;
        float width = text.WidthIncludingTrailingWhitespace + (2.0f * PaddingXDips);
        float height = text.Height + (2.0f * PaddingYDips);
        float x = Math.Clamp(
            _anchor.X + ((_anchor.Width - width) / 2.0f),
            MarginDips,
            MathF.Max(MarginDips, finalSize.Width - MarginDips - width));
        float y = _anchor.Bottom + GapDips + height <= finalSize.Height - MarginDips
            ? _anchor.Bottom + GapDips
            : _anchor.Top - GapDips - height;
        _bubble.Arrange(new RectangleF(x, y, width, height));
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        if (!_waiting || _text is null)
        {
            return false;
        }

        _waitedSeconds += context.ElapsedSeconds;
        if (_waitedSeconds < DelaySeconds)
        {
            return true;
        }

        _bubble.Text = _text;
        _waiting = false;
        _bubble.IsPresent = true;
        InvalidateLayout();
        return false;
    }

    private sealed class Bubble : UiElement
    {
        internal Bubble()
        {
            Transition = new UiTransition(Fade: true, Response: 24.0);
            IsPresent = false;
        }

        internal string Text { get; set; } = string.Empty;

        protected override void DrawCore(in UiDrawContext context)
        {
            var bounds = new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height);
            var panel = new RoundedRectangle(bounds, UiDesign.ControlCornerRadius, UiDesign.ControlCornerRadius);
            context.FillRoundedRectangle(panel, context.Palette.OverlaySurface);
            context.DrawRoundedRectangle(panel, context.Palette.SurfaceBorder);
            context.DrawText(
                Text,
                Font,
                new Rect(PaddingXDips, PaddingYDips, Bounds.Width - (2.0f * PaddingXDips), Bounds.Height - (2.0f * PaddingYDips)),
                context.Palette.PrimaryText,
                DrawTextOptions.Clip);
        }
    }
}
