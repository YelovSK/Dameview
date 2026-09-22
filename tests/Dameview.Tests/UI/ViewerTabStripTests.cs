using System.Drawing;
using Dameview.UI;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Workspace;
using Dameview.Win32.Input;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class ViewerTabStripTests
{
    [TestMethod]
    public void WheelScrollsOverflowingTabsHorizontally()
    {
        var tabs = new ViewerTabStrip(
            Tabs("One", "Two", "Three", "Four"),
            0,
            _ => { },
            _ => { },
            () => { });
        var root = new UiRoot(tabs, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(300.0f, ViewerTabStrip.HeightDips));

        UiPointerResult result = tabs.OnPointerEvent(new WindowPointerEvent(
            WindowPointerEventKind.Wheel,
            PointF.Empty,
            WheelDelta: -120));
        root.Update(new UiUpdateContext(1.0 / 60.0));

        Assert.IsTrue(result.Consumed);
        Assert.IsTrue(tabs.ScrollOffset > 0.0f);
    }

    [TestMethod]
    public void SelectingAnOffscreenTabRevealsIt()
    {
        ViewerTabInfo[] tabItems = Tabs("One", "Two", "Three", "Four");
        var tabs = new ViewerTabStrip(tabItems, 0, _ => { }, _ => { }, () => { });
        var root = new UiRoot(tabs, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(300.0f, ViewerTabStrip.HeightDips));

        tabs.SetTabs(tabItems, 3);

        Assert.IsTrue(tabs.ScrollOffset > 0.0f);
    }

    [TestMethod]
    public void CloseButtonReportsItsTabIndex()
    {
        int closed = -1;
        var tabs = new ViewerTabStrip(
            Tabs("One", "Two"),
            0,
            _ => { },
            index => closed = index,
            () => { });
        var root = new UiRoot(tabs, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(400.0f, ViewerTabStrip.HeightDips));
        root.HandlePointer(new WindowPointerEvent(
            WindowPointerEventKind.Pressed,
            new PointF(180.0f + UiDesign.SmallSpacing + 164.0f, ViewerTabStrip.HeightDips / 2.0f),
            PointerButton.Primary));

        Assert.AreEqual(1, closed);
    }

    [TestMethod]
    public void AddButtonRequestsANewTab()
    {
        int additions = 0;
        var tabs = new ViewerTabStrip(
            Tabs("One"),
            0,
            _ => { },
            _ => { },
            () => additions++);
        var root = new UiRoot(tabs, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(400.0f, ViewerTabStrip.HeightDips));

        root.HandlePointer(new WindowPointerEvent(
            WindowPointerEventKind.Pressed,
            new PointF(382.0f, ViewerTabStrip.HeightDips / 2.0f),
            PointerButton.Primary));

        Assert.AreEqual(1, additions);
    }

    [TestMethod]
    public void MiddleClickReportsItsTabIndexForClosing()
    {
        int closed = -1;
        var tabs = new ViewerTabStrip(
            Tabs("One", "Two"),
            0,
            _ => { },
            index => closed = index,
            () => { });
        var root = new UiRoot(tabs, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(400.0f, ViewerTabStrip.HeightDips));
        root.HandlePointer(new WindowPointerEvent(
            WindowPointerEventKind.Pressed,
            new PointF(180.0f + UiDesign.SmallSpacing + 60.0f, ViewerTabStrip.HeightDips / 2.0f),
            PointerButton.Middle));

        Assert.AreEqual(1, closed);
    }

    [TestMethod]
    public void MovingAcrossTabsReportsHoveredTab()
    {
        var hovered = new List<string?>();
        var tabs = new ViewerTabStrip(
            Tabs("One", "Two"),
            0,
            _ => { },
            _ => { },
            () => { },
            (tab, _) => hovered.Add(tab?.Label));
        var root = new UiRoot(tabs, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(400.0f, ViewerTabStrip.HeightDips));

        root.HandlePointer(new WindowPointerEvent(
            WindowPointerEventKind.Moved,
            new PointF(30.0f, ViewerTabStrip.HeightDips / 2.0f)));
        root.HandlePointer(new WindowPointerEvent(
            WindowPointerEventKind.Moved,
            new PointF(210.0f, ViewerTabStrip.HeightDips / 2.0f)));
        root.HandlePointer(new WindowPointerEvent(
            WindowPointerEventKind.Moved,
            new PointF(399.0f, ViewerTabStrip.HeightDips / 2.0f)));

        CollectionAssert.AreEqual(new string?[] { "One", "Two", null }, hovered);
    }

    [TestMethod]
    public void DraggingATabReportsTheGestureAfterTheThreshold()
    {
        var events = new List<WorkspaceDragEventKind>();
        var tabs = new ViewerTabStrip(
            Tabs("One", "Two"),
            0,
            _ => { },
            _ => { },
            () => { },
            dragPointer: (_, input) => events.Add(input.Kind));
        var root = new UiRoot(tabs, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(400.0f, ViewerTabStrip.HeightDips));

        root.HandlePointer(new WindowPointerEvent(
            WindowPointerEventKind.Pressed,
            new PointF(40.0f, 18.0f),
            PointerButton.Primary));
        root.HandlePointer(new WindowPointerEvent(
            WindowPointerEventKind.Moved,
            new PointF(42.0f, 18.0f)));
        root.HandlePointer(new WindowPointerEvent(
            WindowPointerEventKind.Moved,
            new PointF(48.0f, 18.0f)));
        root.HandlePointer(new WindowPointerEvent(
            WindowPointerEventKind.Released,
            new PointF(220.0f, 18.0f),
            PointerButton.Primary));

        CollectionAssert.AreEqual(
            new[]
            {
                WorkspaceDragEventKind.Started,
                WorkspaceDragEventKind.Moved,
                WorkspaceDragEventKind.Completed,
            },
            events);
    }

    [TestMethod]
    public void TabInsertionUsesTheNearestGap()
    {
        var tabs = new ViewerTabStrip(
            Tabs("One", "Two"),
            0,
            _ => { },
            _ => { },
            () => { });
        var root = new UiRoot(tabs, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(500.0f, ViewerTabStrip.HeightDips));

        Assert.AreEqual(0, tabs.GetInsertionIndex(new PointF(20.0f, 18.0f)));
        Assert.AreEqual(1, tabs.GetInsertionIndex(new PointF(150.0f, 18.0f)));
        Assert.AreEqual(2, tabs.GetInsertionIndex(new PointF(340.0f, 18.0f)));
    }

    private static ViewerTabInfo[] Tabs(params string[] labels) =>
        [.. labels.Select(label => new ViewerTabInfo(label, $"C:\\{label}.png"))];
}
