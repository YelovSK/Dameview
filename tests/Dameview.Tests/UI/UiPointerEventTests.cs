using Dameview.Platform;
using System.Drawing;
using Dameview.UI;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class UiPointerEventTests
{
    [TestMethod]
    public void ToLocalSubtractsTheElementOriginAndPreservesInputData()
    {
        var input = new UiPointerEvent(
            UiPointerEventKind.Wheel,
            new PointF(500.0f, 250.0f),
            PointerButton.Primary,
            120);
        var bounds = new RectangleF(300.0f, 50.0f, 900.0f, 700.0f);

        UiPointerEvent local = input.ToLocal(bounds);

        Assert.AreEqual(new PointF(200.0f, 200.0f), local.Position);
        Assert.AreEqual(UiPointerEventKind.Wheel, local.Kind);
        Assert.AreEqual(PointerButton.Primary, local.Button);
        Assert.AreEqual(120, local.WheelDelta);
    }
}
