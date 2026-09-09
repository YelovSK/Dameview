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
            Tabs("One", "Two", "Three", "Four"),
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
        ViewerTabInfo[] tabItems = Tabs("One", "Two", "Three", "Four");
        using var tabs = new ViewerTabStrip(factory, tabItems, 0, _ => { }, _ => { });
        var root = new UiRoot(tabs, UiDpi.Default);
        root.Arrange(new SizeF(300.0f, ViewerTabStrip.HeightDips));

        tabs.SetTabs(tabItems, 3);

        Assert.IsTrue(tabs.ScrollOffset > 0.0f);
    }

    [TestMethod]
    public void CloseButtonReportsItsTabIndex()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        int closed = -1;
        using var tabs = new ViewerTabStrip(
            factory,
            Tabs("One", "Two"),
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

    [TestMethod]
    public void MiddleClickReportsItsTabIndexForClosing()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        int closed = -1;
        using var tabs = new ViewerTabStrip(
            factory,
            Tabs("One", "Two"),
            0,
            _ => { },
            index => closed = index);
        var root = new UiRoot(tabs, UiDpi.Default);
        root.Arrange(new SizeF(400.0f, ViewerTabStrip.HeightDips));
        root.HandlePointer(new UiPointerEvent(
            UiPointerEventKind.Pressed,
            new PointF(180.0f + UiDesign.SmallSpacing + 60.0f, ViewerTabStrip.HeightDips / 2.0f),
            PointerButton.Middle));

        Assert.AreEqual(1, closed);
    }

    [TestMethod]
    public void MovingAcrossTabsReportsHoveredTab()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        var hovered = new List<string?>();
        using var tabs = new ViewerTabStrip(
            factory,
            Tabs("One", "Two"),
            0,
            _ => { },
            _ => { },
            (tab, _) => hovered.Add(tab?.Label));
        var root = new UiRoot(tabs, UiDpi.Default);
        root.Arrange(new SizeF(400.0f, ViewerTabStrip.HeightDips));

        root.HandlePointer(new UiPointerEvent(
            UiPointerEventKind.Moved,
            new PointF(30.0f, ViewerTabStrip.HeightDips / 2.0f)));
        root.HandlePointer(new UiPointerEvent(
            UiPointerEventKind.Moved,
            new PointF(210.0f, ViewerTabStrip.HeightDips / 2.0f)));
        root.HandlePointer(new UiPointerEvent(
            UiPointerEventKind.Moved,
            new PointF(399.0f, ViewerTabStrip.HeightDips / 2.0f)));

        CollectionAssert.AreEqual(new string?[] { "One", "Two", null }, hovered);
    }

    private static ViewerTabInfo[] Tabs(params string[] labels) =>
        [.. labels.Select(label => new ViewerTabInfo(label, $"C:\\{label}.png"))];
}
