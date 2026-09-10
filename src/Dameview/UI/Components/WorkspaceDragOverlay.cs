using System.Drawing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal enum WorkspaceDragTargetKind
{
    None,
    TabInsertion,
    SplitLeft,
    SplitUp,
    SplitRight,
    SplitDown,
}

internal readonly record struct WorkspaceDockTargets(
    RectangleF Left,
    RectangleF Up,
    RectangleF Right,
    RectangleF Down);

internal sealed class WorkspaceDragOverlay : UiElement, IDisposable
{
    private const float DockTargetSize = 56.0f;
    private const float DockTargetGap = 10.0f;
    private const float GhostWidth = 180.0f;
    private const float GhostHeight = 36.0f;
    private const float GhostOffset = 14.0f;

    private readonly IDWriteTextFormat _labelFormat;
    private readonly IDWriteTextFormat _arrowFormat;
    private string _label = string.Empty;
    private PointF _pointer;
    private RectangleF _paneBounds;
    private RectangleF _insertionMarker;
    private WorkspaceDockTargets _dockTargets;
    private WorkspaceDragTargetKind _targetKind;

    internal WorkspaceDragOverlay(IDWriteFactory factory)
    {
        _labelFormat = factory.CreateTextFormat(
            UiTypography.FontFamily,
            FontWeight.SemiBold,
            FontStyle.Normal,
            UiDesign.BodyFontSize);
        _labelFormat.TextAlignment = TextAlignment.Leading;
        _labelFormat.ParagraphAlignment = ParagraphAlignment.Center;
        _labelFormat.WordWrapping = WordWrapping.NoWrap;
        _arrowFormat = factory.CreateTextFormat(
            UiTypography.FontFamily,
            FontWeight.SemiBold,
            FontStyle.Normal,
            22.0f);
        _arrowFormat.TextAlignment = TextAlignment.Center;
        _arrowFormat.ParagraphAlignment = ParagraphAlignment.Center;
        _arrowFormat.WordWrapping = WordWrapping.NoWrap;
        IsVisible = false;
    }

    internal override bool IsHitTestVisible => false;

    internal void Show(string label)
    {
        _label = label;
        IsVisible = true;
    }

    internal void Update(
        PointF pointer,
        RectangleF paneBounds,
        RectangleF insertionMarker,
        WorkspaceDragTargetKind targetKind)
    {
        _pointer = pointer;
        _paneBounds = paneBounds;
        _insertionMarker = insertionMarker;
        _dockTargets = CalculateDockTargets(paneBounds);
        _targetKind = targetKind;
        InvalidateVisual();
    }

    internal void Hide()
    {
        IsVisible = false;
        _paneBounds = RectangleF.Empty;
        _insertionMarker = RectangleF.Empty;
        _targetKind = WorkspaceDragTargetKind.None;
    }

    internal static WorkspaceDockTargets CalculateDockTargets(RectangleF paneBounds)
    {
        if (paneBounds.Width <= 0.0f || paneBounds.Height <= 0.0f)
        {
            return default;
        }

        float centerX = paneBounds.Left + paneBounds.Width / 2.0f;
        float centerY = paneBounds.Top + paneBounds.Height / 2.0f;
        float size = MathF.Min(
            DockTargetSize,
            MathF.Max(
                0.0f,
                (MathF.Min(paneBounds.Width, paneBounds.Height) / 2.0f - DockTargetGap) / 1.5f));
        float offset = size + DockTargetGap;
        return new WorkspaceDockTargets(
            new RectangleF(
                centerX - offset - size / 2.0f,
                centerY - size / 2.0f,
                size,
                size),
            new RectangleF(
                centerX - size / 2.0f,
                centerY - offset - size / 2.0f,
                size,
                size),
            new RectangleF(
                centerX + offset - size / 2.0f,
                centerY - size / 2.0f,
                size,
                size),
            new RectangleF(
                centerX - size / 2.0f,
                centerY + offset - size / 2.0f,
                size,
                size));
    }

    protected override SizeF MeasureCore(SizeF availableSize) => availableSize;

    protected override void DrawCore(in UiDrawContext context)
    {
        if (!_insertionMarker.IsEmpty)
        {
            context.FillRoundedRectangle(
                new RoundedRectangle(_insertionMarker, 1.5f, 1.5f),
                context.Palette.Accent);
        }

        if (!_paneBounds.IsEmpty)
        {
            context.DrawRoundedRectangle(
                new RoundedRectangle(
                    _paneBounds,
                    UiDesign.PanelCornerRadius,
                    UiDesign.PanelCornerRadius),
                context.Palette.Accent,
                strokeWidth: 2.0f,
                opacity: 0.75f);
            DrawDockTarget(context, _dockTargets.Left, "←", WorkspaceDragTargetKind.SplitLeft);
            DrawDockTarget(context, _dockTargets.Up, "↑", WorkspaceDragTargetKind.SplitUp);
            DrawDockTarget(context, _dockTargets.Right, "→", WorkspaceDragTargetKind.SplitRight);
            DrawDockTarget(context, _dockTargets.Down, "↓", WorkspaceDragTargetKind.SplitDown);
        }

        RectangleF ghost = GetGhostBounds();
        var panel = new RoundedRectangle(
            ghost,
            UiDesign.ControlCornerRadius,
            UiDesign.ControlCornerRadius);
        context.FillRoundedRectangle(panel, context.Palette.OverlaySurface);
        context.DrawRoundedRectangle(panel, context.Palette.Accent, strokeWidth: 2.0f);
        context.DrawText(
            _label,
            _labelFormat,
            new Rect(ghost.X + UiDesign.Spacing, ghost.Y, ghost.Width - 2.0f * UiDesign.Spacing, ghost.Height),
            context.Palette.PrimaryText,
            DrawTextOptions.Clip);
    }

    public void Dispose()
    {
        _arrowFormat.Dispose();
        _labelFormat.Dispose();
    }

    private void DrawDockTarget(
        in UiDrawContext context,
        RectangleF bounds,
        string label,
        WorkspaceDragTargetKind kind)
    {
        bool selected = _targetKind == kind;
        var target = new RoundedRectangle(
            bounds,
            UiDesign.ControlCornerRadius,
            UiDesign.ControlCornerRadius);
        context.FillRoundedRectangle(
            target,
            selected ? context.Palette.Accent : context.Palette.OverlaySurface,
            selected ? 0.85f : 1.0f);
        context.DrawRoundedRectangle(target, context.Palette.Accent, strokeWidth: selected ? 2.0f : 1.0f);
        context.DrawText(
            label,
            _arrowFormat,
            new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            context.Palette.PrimaryText);
    }

    private RectangleF GetGhostBounds()
    {
        float x = Math.Clamp(
            _pointer.X + GhostOffset,
            0.0f,
            MathF.Max(0.0f, Bounds.Width - GhostWidth));
        float y = Math.Clamp(
            _pointer.Y + GhostOffset,
            0.0f,
            MathF.Max(0.0f, Bounds.Height - GhostHeight));
        return new RectangleF(x, y, MathF.Min(GhostWidth, Bounds.Width), MathF.Min(GhostHeight, Bounds.Height));
    }
}
