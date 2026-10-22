using System.Drawing;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.Viewing;

namespace Dameview.UI.Workspace;

// Mirrors the workspace model while retaining views for panes and splits that survive a layout change.
internal sealed class WorkspaceView : UiElement, IDisposable
{
    private const double MaximizeResponse = 20.0;

    private readonly Func<ViewerPane, ViewerPaneView> _createPaneView;
    private readonly Func<UiElement, UiSnapshot?> _createSnapshot;
    private readonly Dictionary<ViewerPane, ViewerPaneView> _paneViews = [];
    private Dictionary<WorkspaceSplit, SplitPanel> _splitPanels = [];
    // A removed pane is swapped for a picture of itself, which its old split collapses away.
    private readonly List<Collapse> _collapses = [];
    // A maximized pane leaves its split for a layer above the layout and grows from the slot it left.
    private readonly MaximizedLayer _maximizedLayer = new() { IsVisible = false };
    private readonly EmptySlot _maximizedSlot = new();
    private readonly AnimatedFloat _maximizeProgress = new(0.0f, MaximizeResponse, completionDistance: 0.002f);
    private WorkspaceNode _root;
    private UiElement? _content;
    private TitleBarInsets? _titleBarInsets;

    internal WorkspaceView(
        WorkspaceNode root,
        Func<ViewerPane, ViewerPaneView> createPaneView,
        Func<UiElement, UiSnapshot?> createSnapshot)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(createPaneView);
        ArgumentNullException.ThrowIfNull(createSnapshot);
        _createPaneView = createPaneView;
        _createSnapshot = createSnapshot;
        _root = root;
        AddChild(_maximizedLayer);
        Rebuild(root, openingSplit: null);
    }

    internal ViewerPaneView? FindPaneView(ViewerPane pane)
    {
        return _paneViews.GetValueOrDefault(pane);
    }

    internal IEnumerable<ViewerPaneView> PaneViews => _paneViews.Values;

    internal void SetActivePane(ViewerPane activePane)
    {
        bool single = _paneViews.Count == 1;
        foreach ((ViewerPane pane, ViewerPaneView paneView) in _paneViews)
        {
            paneView.IsActivePane = single || ReferenceEquals(pane, activePane);
        }
    }

    /// <param name="insets">How much of the title bar the window's buttons cover, or <see langword="null"/> when the workspace's top edge isn't the title bar.</param>
    internal void SetTitleBar(TitleBarInsets? insets)
    {
        _titleBarInsets = insets;
        ApplyTitleBars();
    }

    /// <summary>Grows a pane over the whole layout, or with <see langword="null"/> shrinks it back.</summary>
    internal void SetMaximizedPane(ViewerPane? pane)
    {
        ViewerPaneView? paneView = pane is null ? null : FindPaneView(pane);
        if (paneView is null)
        {
            if (_maximizedLayer.Pane is not null)
            {
                _maximizeProgress.SetTarget(0.0f);
                SetOtherPanesVisible(true);
                ApplyTitleBars();
                InvalidateLayout();
            }

            return;
        }

        // Another pane may still be shrinking back from an earlier maximize.
        if (!ReferenceEquals(_maximizedLayer.Pane, paneView))
        {
            EndMaximize();
            ((SplitPanel)paneView.Parent!).ReplacePane(paneView, _maximizedSlot);
            _maximizedLayer.SetPane(paneView);
        }

        _maximizeProgress.SetTarget(1.0f);
        ApplyTitleBars();
        InvalidateLayout();
    }

    internal void ApplyLayout(WorkspaceNode root, WorkspaceSplit? openingSplit = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        // The workspace ends a maximize whenever its layout changes.
        EndMaximize();
        _root = root;
        HashSet<ViewerPane> panes = [.. root.Panes];
        ViewerPaneView[] removedViews = [.. _paneViews.Values.Where(paneView => !panes.Contains(paneView.Pane))];
        // When panes only went away, the views stay where they are and each removed pane collapses
        // in place, even one closed while an earlier collapse is still running. The tree is rebuilt
        // from the model once the collapses are done.
        if (removedViews.Length > 0
            && panes.All(_paneViews.ContainsKey)
            && removedViews.All(CollapseInPlace))
        {
            foreach (ViewerPaneView removedView in removedViews)
            {
                _paneViews.Remove(removedView.Pane);
                removedView.Dispose();
            }

            ApplyPaneRatios(root);
            return;
        }

        Rebuild(root, openingSplit);
    }

    internal void ApplyPaneRatios(WorkspaceNode node)
    {
        if (node is not WorkspaceSplit split)
        {
            return;
        }

        _splitPanels[split].SetRatio(split.Ratio);
        ApplyPaneRatios(split.First);
        ApplyPaneRatios(split.Second);
    }

    /// <summary>Ends every collapse at once, for when their pictures can no longer be drawn.</summary>
    internal void FinishCollapses()
    {
        if (_collapses.Count > 0)
        {
            Rebuild(_root, openingSplit: null);
        }
    }

    internal void UpdateStatuses()
    {
        foreach (ViewerPaneView paneView in _paneViews.Values)
        {
            if (paneView.IsVisible)
            {
                paneView.UpdateStatus();
            }
        }
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        _content?.Measure(availableSize);
        _maximizedLayer.Measure(availableSize);
        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        var bounds = new RectangleF(PointF.Empty, finalSize);
        _content?.Arrange(bounds);
        if (_maximizedLayer.Pane is not null)
        {
            _maximizedLayer.Arrange(Lerp(_maximizedSlot.GetBoundsRelativeTo(this), bounds, _maximizeProgress.Current));
        }
    }

    protected override bool HitTestCore(PointF position) => false;

    // Collapses finish while the children update, so the layout is replaced on the next update.
    protected override bool UpdateCore(in UiUpdateContext context)
    {
        if (_collapses.Count > 0 && _collapses.TrueForAll(static collapse => collapse.Panel.Openness == 0.0f))
        {
            Rebuild(_root, openingSplit: null);
        }

        if (_maximizedLayer.Pane is null)
        {
            return false;
        }

        float previous = _maximizeProgress.Current;
        bool continues = _maximizeProgress.Update(context);
        if (_maximizeProgress.Current != previous)
        {
            InvalidateLayout();
        }

        if (!continues)
        {
            // Covered panes stop drawing and animating, but keep their views and images for the way back.
            if (_maximizeProgress.Target == 1.0f)
            {
                SetOtherPanesVisible(false);
            }
            else
            {
                EndMaximize();
            }
        }

        return continues;
    }

    public void Dispose()
    {
        EndMaximize();
        DetachLayout();
        EndCollapses();
        foreach (ViewerPaneView paneView in _paneViews.Values)
        {
            paneView.Dispose();
        }

        _paneViews.Clear();
        _splitPanels.Clear();
    }

    private void ApplyTitleBars()
    {
        ApplyTitleBar(_root, _titleBarInsets);
        if (_maximizedLayer.Pane is { } maximized && _maximizeProgress.Target == 1.0f)
        {
            maximized.SetTitleBar(_titleBarInsets);
        }
    }

    // The panes along the top share the title bar, and the ones in its corners keep clear of its buttons.
    private void ApplyTitleBar(WorkspaceNode node, TitleBarInsets? insets)
    {
        switch (node)
        {
            case ViewerPane pane:
                FindPaneView(pane)?.SetTitleBar(insets);
                break;

            case WorkspaceSplit { Orientation: WorkspaceSplitOrientation.Horizontal } sideBySide:
                ApplyTitleBar(sideBySide.First, insets is { } first ? first with { End = 0.0f } : null);
                ApplyTitleBar(sideBySide.Second, insets is { } second ? second with { Start = 0.0f } : null);
                break;

            case WorkspaceSplit stacked:
                ApplyTitleBar(stacked.First, insets);
                ApplyTitleBar(stacked.Second, null);
                break;
        }
    }

    /// <summary>Puts a maximized pane straight back into its slot.</summary>
    private void EndMaximize()
    {
        if (_maximizedLayer.TakePane() is not { } paneView)
        {
            return;
        }

        ((SplitPanel)_maximizedSlot.Parent!).ReplacePane(_maximizedSlot, paneView);
        _maximizeProgress.SetValue(0.0f);
        SetOtherPanesVisible(true);
        ApplyTitleBars();
    }

    private void SetOtherPanesVisible(bool visible)
    {
        foreach (ViewerPaneView paneView in _paneViews.Values)
        {
            if (!ReferenceEquals(paneView, _maximizedLayer.Pane))
            {
                paneView.IsVisible = visible;
            }
        }
    }

    private static RectangleF Lerp(RectangleF from, RectangleF to, float amount) => new(
        from.X + ((to.X - from.X) * amount),
        from.Y + ((to.Y - from.Y) * amount),
        from.Width + ((to.Width - from.Width) * amount),
        from.Height + ((to.Height - from.Height) * amount));

    private bool CollapseInPlace(ViewerPaneView paneView)
    {
        // A pane that is taking in a collapsing picture takes that picture with it when it goes.
        UiElement leaving = paneView;
        while (leaving.Parent is SplitPanel panel && _collapses.Exists(collapse => ReferenceEquals(collapse.Panel, panel)))
        {
            leaving = panel;
        }

        if (leaving.Parent is not SplitPanel parent || _createSnapshot(leaving) is not { } snapshot)
        {
            return false;
        }

        // Stays against the sibling, so the picture looks pushed out rather than squeezed.
        snapshot.AlignToEnd = ReferenceEquals(parent.FirstPane, leaving);
        parent.ReplacePane(leaving, snapshot);
        parent.Collapse(snapshot);
        foreach (Collapse absorbed in _collapses.FindAll(collapse => IsWithin(collapse.Panel, leaving)))
        {
            _collapses.Remove(absorbed);
            absorbed.Snapshot.Dispose();
        }

        _collapses.Add(new Collapse(parent, snapshot));
        return true;
    }

    private static bool IsWithin(UiElement element, UiElement ancestor)
    {
        for (UiElement? current = element; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private void Rebuild(WorkspaceNode root, WorkspaceSplit? openingSplit)
    {
        DetachLayout();
        EndCollapses();
        Dictionary<WorkspaceSplit, SplitPanel> splitPanels = [];
        _content = Build(root, openingSplit, splitPanels);
        _splitPanels = splitPanels;
        HashSet<ViewerPane> panes = [.. root.Panes];
        foreach (ViewerPaneView removedView in _paneViews.Values.Where(paneView => !panes.Contains(paneView.Pane)).ToArray())
        {
            _paneViews.Remove(removedView.Pane);
            removedView.Dispose();
        }

        AddChild(_content);
        SetChildOrder([_content, _maximizedLayer]);
    }

    private void EndCollapses()
    {
        foreach (Collapse collapse in _collapses)
        {
            collapse.Snapshot.Dispose();
        }

        _collapses.Clear();
    }

    private UiElement Build(
        WorkspaceNode node,
        WorkspaceSplit? openingSplit,
        Dictionary<WorkspaceSplit, SplitPanel> splitPanels)
    {
        if (node is ViewerPane pane)
        {
            ViewerPaneView paneView = GetOrCreatePaneView(pane);
            return ReferenceEquals(paneView, _maximizedLayer.Pane) ? _maximizedSlot : paneView;
        }

        if (node is not WorkspaceSplit split)
        {
            throw new InvalidOperationException($"Unsupported workspace node: {node.GetType().Name}.");
        }

        UiElement first = Build(split.First, openingSplit, splitPanels);
        UiElement second = Build(split.Second, openingSplit, splitPanels);
        if (_splitPanels.TryGetValue(split, out SplitPanel? panel))
        {
            panel.SetPanes(first, second);
            panel.SetRatio(split.Ratio);
        }
        else
        {
            panel = new SplitPanel(
                first,
                second,
                split.Orientation == WorkspaceSplitOrientation.Horizontal ? UiOrientation.Horizontal : UiOrientation.Vertical,
                split.Ratio,
                split.SetRatio,
                animateOpening: ReferenceEquals(split, openingSplit));
        }

        splitPanels.Add(split, panel);
        return panel;
    }

    private ViewerPaneView GetOrCreatePaneView(ViewerPane pane)
    {
        if (_paneViews.TryGetValue(pane, out ViewerPaneView? paneView))
        {
            return paneView;
        }

        paneView = _createPaneView(pane);
        _paneViews.Add(pane, paneView);
        return paneView;
    }

    private void DetachLayout()
    {
        if (_content is null)
        {
            return;
        }

        RemoveChild(_content);
        DetachChildren(_content);
        _content = null;
    }

    private static void DetachChildren(UiElement element)
    {
        if (element is not SplitPanel split)
        {
            return;
        }

        (UiElement first, UiElement second) = split.DetachChildren();
        DetachChildren(first);
        DetachChildren(second);
    }

    private sealed record Collapse(SplitPanel Panel, UiSnapshot Snapshot);

    private sealed class EmptySlot : UiElement
    {
        protected override bool HitTestCore(PointF position) => false;
    }

    // Panes draw nothing behind their images, so the layer hides the layout it grows over.
    private sealed class MaximizedLayer : UiElement
    {
        internal ViewerPaneView? Pane { get; private set; }

        internal void SetPane(ViewerPaneView pane)
        {
            Pane = pane;
            AddChild(pane);
            IsVisible = true;
        }

        internal ViewerPaneView? TakePane()
        {
            if (Pane is not { } pane)
            {
                return null;
            }

            RemoveChild(pane);
            Pane = null;
            IsVisible = false;
            return pane;
        }

        protected override bool HitTestCore(PointF position) => false;

        protected override void DrawCore(in UiDrawContext context) =>
            context.FillRoundedRectangle(new(new RectangleF(PointF.Empty, Bounds.Size), 0.0f, 0.0f), context.Palette.Background);
    }
}

/// <summary>How far in from each end of the title bar its own buttons reach.</summary>
internal readonly record struct TitleBarInsets(float Start, float End);
