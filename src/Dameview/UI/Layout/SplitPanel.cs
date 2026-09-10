using System.Drawing;
using Dameview.UI.Animation;

namespace Dameview.UI.Layout;

// Symmetrically divides its bounds between two workspace children.
internal sealed class SplitPanel : UiElement, ISplitResizerTarget
{
    internal const float MinimumPaneSizeDips = 120.0f;
    internal const float SplitterSizeDips = 8.0f;
    private const double TransitionResponse = 28.0;

    private readonly UiOrientation _orientation;
    private readonly Action<float>? _ratioChanged;
    private readonly SplitResizer _resizer;
    private readonly AnimatedFloat _ratio;
    private readonly AnimatedFloat _transition;
    private Action? _collapseCompleted;
    private bool _collapsingFirstPane;
    private bool _collapseFirstAfterOpening;

    internal SplitPanel(
        UiElement firstPane,
        UiElement secondPane,
        UiOrientation orientation,
        float ratio,
        Action<float>? ratioChanged = null,
        bool animateOpening = false)
    {
        ArgumentNullException.ThrowIfNull(firstPane);
        ArgumentNullException.ThrowIfNull(secondPane);
        if (!(ratio > 0.0f && ratio < 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(ratio));
        }

        FirstPane = firstPane;
        SecondPane = secondPane;
        _orientation = orientation;
        _ratio = new AnimatedFloat(ratio, TransitionResponse);
        _ratioChanged = ratioChanged;
        _transition = new AnimatedFloat(
            animateOpening ? 0.0f : 1.0f,
            TransitionResponse,
            completionDistance: 0.002f);
        _transition.SetTarget(1.0f);
        _resizer = new SplitResizer(this);
        AddChild(FirstPane);
        AddChild(SecondPane);
        AddChild(_resizer);
    }

    internal RectangleF FirstPaneBounds { get; private set; }
    internal RectangleF SecondPaneBounds { get; private set; }
    internal UiElement FirstPane { get; }
    internal UiElement SecondPane { get; }

    internal void SetRatio(float ratio)
    {
        if (!(ratio > 0.0f && ratio < 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(ratio));
        }

        if (_ratio.SetTarget(ratio))
        {
            InvalidateLayout();
        }
    }

    internal void Collapse(UiElement pane, Action completed)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(completed);
        if (_collapseCompleted is not null)
        {
            throw new InvalidOperationException("A pane is already collapsing.");
        }

        bool collapseFirstPane = ReferenceEquals(pane, FirstPane);
        if (!collapseFirstPane && !ReferenceEquals(pane, SecondPane))
        {
            throw new ArgumentException("The pane is not a child of this split.", nameof(pane));
        }

        _collapseCompleted = completed;
        if (collapseFirstPane && _transition.Current != 1.0f)
        {
            _collapseFirstAfterOpening = true;
        }
        else
        {
            _collapsingFirstPane = collapseFirstPane;
            _transition.SetTarget(0.0f);
        }

        InvalidateLayout();
    }

    internal (UiElement First, UiElement Second) DetachChildren()
    {
        RemoveChild(FirstPane);
        RemoveChild(SecondPane);
        RemoveChild(_resizer);
        return (FirstPane, SecondPane);
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        FirstPane.Measure(availableSize);
        SecondPane.Measure(availableSize);
        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        float mainLength = _orientation == UiOrientation.Horizontal
            ? finalSize.Width
            : finalSize.Height;
        float finalSplitterSize = MathF.Min(SplitterSizeDips, MathF.Max(0.0f, mainLength));
        float usableLength = MathF.Max(0.0f, mainLength - finalSplitterSize);
        float finalFirstLength = CalculateFirstLength(usableLength);
        float finalSecondLength = MathF.Max(0.0f, usableLength - finalFirstLength);
        float splitterSize = finalSplitterSize * _transition.Current;
        float firstLength;
        float secondLength;
        if (_collapsingFirstPane)
        {
            firstLength = finalFirstLength * _transition.Current;
            secondLength = MathF.Max(0.0f, mainLength - splitterSize - firstLength);
        }
        else
        {
            secondLength = finalSecondLength * _transition.Current;
            firstLength = MathF.Max(0.0f, mainLength - splitterSize - secondLength);
        }

        if (_orientation == UiOrientation.Horizontal)
        {
            FirstPaneBounds = new RectangleF(0.0f, 0.0f, firstLength, finalSize.Height);
            SecondPaneBounds = new RectangleF(
                firstLength + splitterSize,
                0.0f,
                secondLength,
                finalSize.Height);
        }
        else
        {
            FirstPaneBounds = new RectangleF(0.0f, 0.0f, finalSize.Width, firstLength);
            SecondPaneBounds = new RectangleF(
                0.0f,
                firstLength + splitterSize,
                finalSize.Width,
                secondLength);
        }

        FirstPane.Arrange(FirstPaneBounds);
        SecondPane.Arrange(SecondPaneBounds);
        _resizer.Arrange(GetResizerBounds(firstLength, splitterSize, finalSize));
    }

    protected override bool HitTestCore(PointF position) => false;

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        float previous = _transition.Current;
        bool continues = _transition.Update(context);
        float previousRatio = _ratio.Current;
        continues |= _ratio.Update(context);
        if (_collapseFirstAfterOpening && _transition.Current == 1.0f)
        {
            _collapseFirstAfterOpening = false;
            _collapsingFirstPane = true;
            continues = _transition.SetTarget(0.0f);
        }

        if (_transition.Current != previous || _ratio.Current != previousRatio)
        {
            InvalidateLayout();
        }

        if (_collapseCompleted is not null
            && !_collapseFirstAfterOpening
            && _transition.Current == 0.0f)
        {
            Action completed = _collapseCompleted;
            _collapseCompleted = null;
            completed();
        }

        return continues;
    }

    private float CalculateFirstLength(float usableLength)
    {
        float requested = usableLength * _ratio.Current;
        if (usableLength < 2.0f * MinimumPaneSizeDips)
        {
            return Math.Clamp(requested, 0.0f, usableLength);
        }

        return Math.Clamp(
            requested,
            MinimumPaneSizeDips,
            usableLength - MinimumPaneSizeDips);
    }

    UiOrientation ISplitResizerTarget.Orientation => _orientation;

    bool ISplitResizerTarget.CanResize
    {
        get
        {
            float mainLength = _orientation == UiOrientation.Horizontal
                ? Bounds.Width
                : Bounds.Height;
            return _transition.Current == 1.0f
                && _collapseCompleted is null
                && mainLength - SplitterSizeDips >= 2.0f * MinimumPaneSizeDips;
        }
    }

    float ISplitResizerTarget.DividerPosition
    {
        get => _orientation == UiOrientation.Horizontal
            ? FirstPaneBounds.Width
            : FirstPaneBounds.Height;
        set => SetFirstPaneLength(value);
    }

    private RectangleF GetResizerBounds(float firstLength, float splitterSize, SizeF finalSize)
    {
        return _orientation == UiOrientation.Horizontal
            ? new RectangleF(firstLength, 0.0f, splitterSize, finalSize.Height)
            : new RectangleF(0.0f, firstLength, finalSize.Width, splitterSize);
    }

    private void SetFirstPaneLength(float length)
    {
        float mainLength = _orientation == UiOrientation.Horizontal
            ? Bounds.Width
            : Bounds.Height;
        float usableLength = mainLength - SplitterSizeDips;
        if (usableLength < 2.0f * MinimumPaneSizeDips)
        {
            return;
        }

        float firstLength = Math.Clamp(
            length,
            MinimumPaneSizeDips,
            usableLength - MinimumPaneSizeDips);
        float ratio = firstLength / usableLength;
        if (!_ratio.SetValue(ratio))
        {
            return;
        }

        _ratioChanged?.Invoke(ratio);
        InvalidateLayout();
    }
}
