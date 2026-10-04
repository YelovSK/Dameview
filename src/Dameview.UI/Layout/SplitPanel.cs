using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;

namespace Dameview.UI.Layout;

// Symmetrically divides its bounds between two workspace children.
internal sealed class SplitPanel : UiElement, ISplitResizerTarget
{
    internal const float MinimumPaneSizeDips = 120.0f;
    internal const float SplitterSizeDips = 8.0f;
    private const double TransitionResponse = 20.0;

    private readonly UiOrientation _orientation;
    private readonly Action<float>? _ratioChanged;
    private readonly SplitResizer _resizer;
    private readonly AnimatedFloat _ratio;
    private readonly AnimatedFloat _transition;
    private Action? _collapseCompleted;
    private bool _collapsing;
    private bool _collapsingFirstPane;

    internal SplitPanel(
        UiElement firstPane,
        UiElement secondPane,
        UiOrientation orientation,
        float ratio,
        Action<float>? ratioChanged = null,
        bool animateOpening = false)
    {
        if (!(ratio > 0.0f && ratio < 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(ratio));
        }

        _orientation = orientation;
        _ratio = new AnimatedFloat(ratio, TransitionResponse);
        _ratioChanged = ratioChanged;
        _transition = new AnimatedFloat(
            animateOpening ? 0.0f : 1.0f,
            TransitionResponse,
            completionDistance: 0.002f);
        _transition.SetTarget(1.0f);
        _resizer = new SplitResizer(this);
        SetPanes(firstPane, secondPane);
    }

    /// <summary>How far open the split is, from 0 when a pane has collapsed to 1 when both are shown.</summary>
    internal float Openness => _transition.Current;
    internal RectangleF FirstPaneBounds { get; private set; }
    internal RectangleF SecondPaneBounds { get; private set; }
    internal UiElement FirstPane { get; private set; }
    internal UiElement SecondPane { get; private set; }

    /// <summary>Puts panes in, after <see cref="DetachChildren"/> took the previous ones out.</summary>
    [MemberNotNull(nameof(FirstPane), nameof(SecondPane))]
    internal void SetPanes(UiElement firstPane, UiElement secondPane)
    {
        ArgumentNullException.ThrowIfNull(firstPane);
        ArgumentNullException.ThrowIfNull(secondPane);
        FirstPane = firstPane;
        SecondPane = secondPane;
        AddChild(firstPane);
        AddChild(secondPane);
        AddChild(_resizer);
    }

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

    /// <summary>Swaps one pane for another in the same place.</summary>
    internal void ReplacePane(UiElement pane, UiElement replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        bool first = ReferenceEquals(pane, FirstPane);
        if (!first && !ReferenceEquals(pane, SecondPane))
        {
            throw new ArgumentException("The pane is not a child of this split.", nameof(pane));
        }

        RemoveChild(pane);
        AddChild(replacement);
        if (first)
        {
            FirstPane = replacement;
        }
        else
        {
            SecondPane = replacement;
        }

        SetChildOrder([FirstPane, SecondPane, _resizer]);
    }

    /// <summary>Shrinks a pane away from wherever the split is now, even partway through opening.</summary>
    internal void Collapse(UiElement pane, Action? completed = null)
    {
        ArgumentNullException.ThrowIfNull(pane);
        if (_collapsing)
        {
            throw new InvalidOperationException("A pane is already collapsing.");
        }

        bool collapseFirstPane = ReferenceEquals(pane, FirstPane);
        if (!collapseFirstPane && !ReferenceEquals(pane, SecondPane))
        {
            throw new ArgumentException("The pane is not a child of this split.", nameof(pane));
        }

        _collapsing = true;
        _collapseCompleted = completed;
        _collapsingFirstPane = collapseFirstPane;
        _transition.SetTarget(0.0f);
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
        if (_transition.Current != previous || _ratio.Current != previousRatio)
        {
            InvalidateLayout();
        }

        if (_collapsing && _transition.Current == 0.0f)
        {
            _collapsing = false;
            Action? completed = _collapseCompleted;
            _collapseCompleted = null;
            completed?.Invoke();
        }

        return continues;
    }

    private float CalculateFirstLength(float usableLength)
    {
        MinimumLength first = GetMinimumLength(FirstPane, _orientation);
        MinimumLength second = GetMinimumLength(SecondPane, _orientation);
        // Without room for every pane's minimum, the splitters keep their size and the rest is
        // shared out by how many panes each side holds, so all the panes end up the same size.
        float maximum = usableLength - second.Total;
        if (maximum < first.Total)
        {
            float paneRoom = MathF.Max(0.0f, usableLength - first.Splitters - second.Splitters);
            return first.Splitters + paneRoom * first.Panes / (first.Panes + second.Panes);
        }

        return Math.Clamp(usableLength * _ratio.Current, first.Total, maximum);
    }

    /// <summary>The room this split needs along an axis to give every pane in it the minimum size.</summary>
    private MinimumLength GetMinimumLength(UiOrientation axis)
    {
        MinimumLength first = GetMinimumLength(FirstPane, axis);
        MinimumLength second = GetMinimumLength(SecondPane, axis);
        if (axis != _orientation)
        {
            return first.Total >= second.Total ? first : second;
        }

        // A side that is opening or collapsing only needs the share of its room it has right now.
        var splitter = new MinimumLength(0.0f, SplitterSizeDips);
        float transition = _transition.Current;
        return _collapsingFirstPane
            ? (first + splitter).Scale(transition) + second
            : first + (splitter + second).Scale(transition);
    }

    private static MinimumLength GetMinimumLength(UiElement pane, UiOrientation axis) =>
        pane is SplitPanel split ? split.GetMinimumLength(axis) : new MinimumLength(MinimumPaneSizeDips, 0.0f);

    UiOrientation ISplitResizerTarget.Orientation => _orientation;

    bool ISplitResizerTarget.CanResize
    {
        get
        {
            float mainLength = _orientation == UiOrientation.Horizontal
                ? Bounds.Width
                : Bounds.Height;
            return _transition.Current == 1.0f
                && !_collapsing
                && mainLength - SplitterSizeDips
                    >= (GetMinimumLength(FirstPane, _orientation) + GetMinimumLength(SecondPane, _orientation)).Total;
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
        float firstMinimum = GetMinimumLength(FirstPane, _orientation).Total;
        float maximum = usableLength - GetMinimumLength(SecondPane, _orientation).Total;
        if (maximum < firstMinimum)
        {
            return;
        }

        float firstLength = Math.Clamp(length, firstMinimum, maximum);
        float ratio = firstLength / usableLength;
        if (!_ratio.SetValue(ratio))
        {
            return;
        }

        _ratioChanged?.Invoke(ratio);
        InvalidateLayout();
    }

    /// <param name="Panes">What the panes need, which can be shared out when room runs short.</param>
    /// <param name="Splitters">What the splitters take, which stays the same however small the panes get.</param>
    private readonly record struct MinimumLength(float Panes, float Splitters)
    {
        internal float Total => Panes + Splitters;

        public static MinimumLength operator +(MinimumLength first, MinimumLength second) =>
            new(first.Panes + second.Panes, first.Splitters + second.Splitters);

        internal MinimumLength Scale(float factor) => new(Panes * factor, Splitters * factor);
    }
}
