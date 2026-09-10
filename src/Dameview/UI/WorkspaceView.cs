using System.Drawing;
using Dameview.UI.Layout;
using Dameview.Viewing;

namespace Dameview.UI;

// Mirrors the workspace model while retaining views for panes that survive a layout change.
internal sealed class WorkspaceView : UiElement, IDisposable
{
    private readonly Func<ViewerPane, ViewerPaneView> _createPaneView;
    private readonly Dictionary<ViewerPane, ViewerPaneView> _paneViews = [];
    private UiElement? _content;
    private ViewerPane? _closingPane;
    private Action? _pendingCloseCompletion;

    internal WorkspaceView(
        WorkspaceNode root,
        Func<ViewerPane, ViewerPaneView> createPaneView)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(createPaneView);
        _createPaneView = createPaneView;
        ApplyLayout(root);
    }

    internal TimeSpan? NextAnimationFrameDelay
    {
        get
        {
            TimeSpan? nextDelay = null;
            foreach (ViewerPaneView paneView in _paneViews.Values)
            {
                if (!paneView.IsVisible || paneView.NextAnimationFrameDelay is not { } delay)
                {
                    continue;
                }

                nextDelay = nextDelay is null || delay < nextDelay ? delay : nextDelay;
            }

            return nextDelay;
        }
    }

    internal ViewerPaneView? FindPaneView(ViewerPane pane)
    {
        return _paneViews.GetValueOrDefault(pane);
    }

    internal bool IsClosingPane => _closingPane is not null;

    internal bool BeginClosePane(ViewerPane pane, Action completed)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(completed);
        if (_closingPane is not null)
        {
            return true;
        }

        ViewerPaneView paneView = FindPaneView(pane)
            ?? throw new ArgumentException("The pane is not attached to this workspace view.", nameof(pane));
        if (paneView.Parent is not SplitPanel parent)
        {
            return false;
        }

        _closingPane = pane;
        parent.Collapse(paneView, () => _pendingCloseCompletion = completed);
        return true;
    }

    internal void CompletePendingClose()
    {
        if (_pendingCloseCompletion is not { } completed)
        {
            return;
        }

        _pendingCloseCompletion = null;
        _closingPane = null;
        completed();
    }

    internal void SetActivePane(ViewerPane activePane)
    {
        bool showIndicator = _paneViews.Count > 1;
        foreach ((ViewerPane pane, ViewerPaneView paneView) in _paneViews)
        {
            paneView.ShowActivePaneIndicator = showIndicator && ReferenceEquals(pane, activePane);
        }
    }

    internal void ApplyLayout(WorkspaceNode root, WorkspaceSplit? openingSplit = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        DetachLayout();

        HashSet<ViewerPane> retainedPanes = [];
        _content = Build(root, retainedPanes, openingSplit);
        foreach (ViewerPane removedPane in _paneViews.Keys.Where(pane => !retainedPanes.Contains(pane)).ToArray())
        {
            _paneViews.Remove(removedPane, out ViewerPaneView? removedView);
            removedView?.Dispose();
        }

        AddChild(_content);
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
        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        _content?.Arrange(new RectangleF(PointF.Empty, finalSize));
    }

    protected override bool HitTestCore(PointF position) => false;

    public void Dispose()
    {
        _closingPane = null;
        _pendingCloseCompletion = null;
        DetachLayout();
        foreach (ViewerPaneView paneView in _paneViews.Values)
        {
            paneView.Dispose();
        }

        _paneViews.Clear();
    }

    private UiElement Build(
        WorkspaceNode node,
        HashSet<ViewerPane> retainedPanes,
        WorkspaceSplit? openingSplit)
    {
        return node switch
        {
            ViewerPane pane => GetOrCreatePaneView(pane, retainedPanes),
            WorkspaceSplit split => new SplitPanel(
                Build(split.First, retainedPanes, openingSplit),
                Build(split.Second, retainedPanes, openingSplit),
                split.Orientation == WorkspaceSplitOrientation.Horizontal
                    ? UiOrientation.Horizontal
                    : UiOrientation.Vertical,
                split.Ratio,
                split.SetRatio,
                animateOpening: ReferenceEquals(split, openingSplit)),
            _ => throw new InvalidOperationException($"Unsupported workspace node: {node.GetType().Name}."),
        };
    }

    private ViewerPaneView GetOrCreatePaneView(ViewerPane pane, HashSet<ViewerPane> retainedPanes)
    {
        retainedPanes.Add(pane);
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
}
