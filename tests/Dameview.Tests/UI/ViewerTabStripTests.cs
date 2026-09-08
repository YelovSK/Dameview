using System.Drawing;
using Dameview.Platform;
using Dameview.UI;
using Dameview.UI.Components;
using Vortice.DirectWrite;
using static Vortice.DirectWrite.DWrite;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class ViewerTabStripTests
{
    [TestMethod]
    public void WheelScrollsOverflowingTabsHorizontally()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        using var tabs = new ViewerTabStrip(
            factory,
            ["One", "Two", "Three", "Four"],
            0,
            _ => { },
            _ => { });
        var root = new UiRoot(tabs, UiDpi.Default);
        root.Arrange(new SizeF(300.0f, ViewerTabStrip.HeightDips));

        UiPointerResult result = tabs.OnPointerEvent(new UiPointerEvent(
            UiPointerEventKind.Wheel,
            PointF.Empty,
            WheelDelta: -120));
        root.Update(new UiUpdateContext(1.0 / 60.0));

        Assert.IsTrue(result.Consumed);
        Assert.IsTrue(tabs.ScrollOffset > 0.0f);
    }

    [TestMethod]
    public void SelectingAnOffscreenTabRevealsIt()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        string[] labels = ["One", "Two", "Three", "Four"];
        using var tabs = new ViewerTabStrip(factory, labels, 0, _ => { }, _ => { });
        var root = new UiRoot(tabs, UiDpi.Default);
        root.Arrange(new SizeF(300.0f, ViewerTabStrip.HeightDips));

        tabs.SetTabs(labels, 3);

        Assert.IsTrue(tabs.ScrollOffset > 0.0f);
    }

    [TestMethod]
    public void CloseButtonReportsItsTabIndex()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        int closed = -1;
        using var tabs = new ViewerTabStrip(
            factory,
            ["One", "Two"],
            0,
            _ => { },
            index => closed = index);
        var root = new UiRoot(tabs, UiDpi.Default);
        root.Arrange(new SizeF(400.0f, ViewerTabStrip.HeightDips));
        root.HandlePointer(new UiPointerEvent(
            UiPointerEventKind.Pressed,
            new PointF(180.0f + UiDesign.SmallSpacing + 164.0f, ViewerTabStrip.HeightDips / 2.0f),
            PointerButton.Primary));

        Assert.AreEqual(1, closed);
    }
}
