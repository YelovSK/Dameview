using System.Drawing;
using Dameview.UI.Layout;
using Dameview.Win32.Input;

namespace Dameview.Tests.UI.Layout;

[TestClass]
public sealed class ScrollbarTests
{
    [TestMethod]
    public void HorizontalThumbUsesWidthAndDraggingChangesOffsetAlongXAxis()
    {
        float offset = 0.0f;
        var scrollbar = new Scrollbar(value => offset = value, UiOrientation.Horizontal);
        scrollbar.Arrange(new RectangleF(0.0f, 0.0f, 200.0f, 12.0f));
        scrollbar.SetMetrics(contentExtent: 1000.0f, viewportExtent: 200.0f, offset: 100.0f);

        RectangleF thumb = scrollbar.ThumbBounds;
        Assert.AreEqual(21.5f, thumb.X, 0.001f);
        Assert.AreEqual(2.0f, thumb.Y);
        Assert.AreEqual(40.0f, thumb.Width);
        Assert.AreEqual(8.0f, thumb.Height);

        scrollbar.OnPointerEvent(new WindowPointerEvent(
            WindowPointerEventKind.Pressed,
            new PointF(thumb.X + thumb.Width / 2.0f, thumb.Y + thumb.Height / 2.0f),
            PointerButton.Primary));
        scrollbar.OnPointerEvent(new WindowPointerEvent(
            WindowPointerEventKind.Moved,
            new PointF(180.0f, thumb.Y + thumb.Height / 2.0f)));

        Assert.IsGreaterThan(100.0f, offset);
    }
}
