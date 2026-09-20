using System.Drawing;
using Dameview.Notifications;
using Dameview.UI.Foundation;
using Vortice.DirectWrite;

namespace Dameview.UI.Components;

/// <summary>Stacks the current toasts in a corner, above everything the window is showing.</summary>
internal sealed class ToastHost : UiElement, IDisposable
{
    private const float Gap = 8.0f;

    private readonly IDWriteFactory _factory;
    private readonly ToastService _toasts;
    private readonly List<ToastView> _views = [];

    internal ToastHost(IDWriteFactory factory, ToastService toasts)
    {
        _factory = factory;
        _toasts = toasts;
        _toasts.Changed += Refresh;
        Refresh();
    }

    internal override bool IsHitTestVisible => _views.Count > 0;

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        foreach (ToastView view in _views)
        {
            view.Measure(new SizeF(ToastView.Width, availableSize.Height));
        }

        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        // Newest nearest the corner, so a burst does not shuffle the one being read.
        float bottom = finalSize.Height - UiDesign.WindowMargin;
        for (int index = _views.Count - 1; index >= 0; index--)
        {
            ToastView view = _views[index];
            float height = view.DesiredSize.Height;
            view.PlaceAt(bottom - height);
            view.Arrange(new RectangleF(
                finalSize.Width - UiDesign.WindowMargin - ToastView.Width,
                bottom - height,
                ToastView.Width,
                height));
            bottom -= height + Gap;
        }
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        bool expired = _toasts.RemoveExpired();
        for (int index = _views.Count - 1; index >= 0; index--)
        {
            if (_views[index].HasLeft)
            {
                ToastView left = _views[index];
                _views.RemoveAt(index);
                RemoveChild(left);
                left.Dispose();
                InvalidateLayout();
            }
        }

        return expired || _views.Count > 0;
    }

    protected override bool HitTestCore(PointF position) => false;

    public void Dispose()
    {
        _toasts.Changed -= Refresh;
        foreach (ToastView view in _views)
        {
            view.Dispose();
        }

        _views.Clear();
    }

    // A view outlives its message so that it can animate away; until then it holds its place.
    private void Refresh()
    {
        foreach (Toast toast in _toasts.Toasts)
        {
            if (_views.Any(view => view.Toast.Id == toast.Id))
            {
                continue;
            }

            var view = new ToastView(_factory, toast, () => _toasts.Dismiss(toast.Id));
            _views.Add(view);
            AddChild(view);
        }

        foreach (ToastView view in _views)
        {
            view.IsLeaving = !_toasts.Toasts.Any(toast => toast.Id == view.Toast.Id);
        }

        InvalidateLayout();
    }
}
