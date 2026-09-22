using System.Drawing;
using Dameview.Notifications;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

/// <summary>One message in the toast stack, which slides in and fades out on its way back.</summary>
internal sealed class ToastView : UiElement
{
    internal const float Width = 320.0f;
    internal const float VerticalPadding = 12.0f;

    private const float HorizontalPadding = 14.0f;
    private const float DismissWidth = 32.0f;
    private const double Response = 20.0;
    private const float EntryOffset = 24.0f;
    // The layout is measured in a tall box, so the text has to start at its top.
    private static readonly UiFont MessageFont = new(
        13.0f, VerticalAlignment: ParagraphAlignment.Near, Wrapping: WordWrapping.Wrap);

    private readonly DismissButton _dismiss;
    private readonly AnimatedFloat _shift;
    private float? _top;

    internal ToastView(Toast toast, Action dismissed)
    {
        Toast = toast;
        _shift = Animate(0.0f, Response, completionDistance: 0.25f);
        _dismiss = new DismissButton(dismissed);
        AddChild(_dismiss);
        Transition = new UiTransition(Fade: true, HiddenOffset: new PointF(EntryOffset, 0.0f), Response: Response);
        // Absent until the host adds it, so that it enters rather than starting in place.
        IsPresent = false;
    }

    internal Toast Toast { get; }

    /// <summary>Whether the message has gone from the service and the view has finished animating away.</summary>
    internal bool HasLeft => !IsPresent && !IsVisible;

    internal override PointF VisualOffset
    {
        get
        {
            PointF entry = base.VisualOffset;
            return new PointF(entry.X, entry.Y + _shift.Current);
        }
    }

    /// <summary>
    /// Tells the toast where the stack now wants it, before it is arranged there.
    /// </summary>
    /// <remarks>
    /// Arranging moves a toast the instant a neighbour appears or leaves. Holding the
    /// difference as a visual offset and easing it away turns that jump into a slide.
    /// </remarks>
    internal void PlaceAt(float top)
    {
        if (_top is float previous && previous != top)
        {
            _shift.SetValue(_shift.Current + (previous - top));
        }

        _top = top;
        _shift.SetTarget(0.0f);
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        _dismiss.Measure(new SizeF(DismissWidth, DismissWidth));
        float height = MathF.Ceiling(Layout.Metrics.Height) + (2.0f * VerticalPadding);
        return new SizeF(Width, MathF.Max(DismissWidth + VerticalPadding, height));
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        _dismiss.Arrange(new RectangleF(
            finalSize.Width - DismissWidth,
            (finalSize.Height - DismissWidth) / 2.0f,
            DismissWidth,
            DismissWidth));
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        var bounds = new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height);
        var surface = new RoundedRectangle(bounds, UiDesign.ControlCornerRadius, UiDesign.ControlCornerRadius);
        context.FillRoundedRectangle(surface, context.Palette.OverlaySurface);

        // The severity is carried by the border, which every other panel already draws.
        context.DrawRoundedRectangle(surface, GetAccent(context.Palette));

        context.DrawTextLayout(
            Layout,
            new System.Numerics.Vector2(HorizontalPadding, VerticalPadding),
            context.Palette.PrimaryText,
            DrawTextOptions.Clip);
    }

    private Color4 GetAccent(UiTheme palette) => Toast.Severity switch
    {
        ToastSeverity.Success => palette.SuccessText,
        ToastSeverity.Warning => palette.WarningText,
        ToastSeverity.Error => palette.ErrorText,
        _ => palette.SecondaryText,
    };

    private IDWriteTextLayout Layout => TextLayouts.Get(
        Toast.Message,
        MessageFont,
        new SizeF(Width - HorizontalPadding - DismissWidth, 10_000.0f));

    private sealed class DismissButton(Action dismissed) : InteractiveControl
    {
        private static readonly UiFont GlyphFont = new(15.0f, FontWeight.SemiBold, TextAlignment.Center);

        protected override SizeF MeasureCore(SizeF availableSize) => availableSize;

        protected override void DrawCore(in UiDrawContext context)
        {
            Color4 text = context.Palette.SecondaryText;
            context.DrawText(
                "×",
                GlyphFont,
                new Rect(0.0f, 0.0f, Bounds.Width, Bounds.Height),
                HoverAmount > 0.0f ? context.Palette.PrimaryText : text);
        }

        protected override void Activate() => dismissed();
    }
}
