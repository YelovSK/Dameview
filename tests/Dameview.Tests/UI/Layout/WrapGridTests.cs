using System.Drawing;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;

namespace Dameview.Tests.UI.Layout;

[TestClass]
public sealed class WrapGridTests
{
    [TestMethod]
    public void ChildrenShareOneRowWhenTheyFit()
    {
        var first = new Cell();
        var second = new Cell();
        var grid = new WrapGrid(30.0f, 20.0f, first, second) { Spacing = 10.0f };

        SizeF desired = grid.Measure(new SizeF(110.0f, 500.0f));
        grid.Arrange(new RectangleF(0.0f, 0.0f, 110.0f, 20.0f));

        Assert.AreEqual(new SizeF(110.0f, 20.0f), desired);
        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 50.0f, 20.0f), first.Bounds);
        Assert.AreEqual(new RectangleF(60.0f, 0.0f, 50.0f, 20.0f), second.Bounds);
    }

    [TestMethod]
    public void ChildrenWrapAndAShortLastRowFillsTheWidth()
    {
        var first = new Cell();
        var second = new Cell();
        var third = new Cell();
        var grid = new WrapGrid(40.0f, 20.0f, first, second, third) { Spacing = 10.0f };

        SizeF desired = grid.Measure(new SizeF(100.0f, 500.0f));
        grid.Arrange(new RectangleF(0.0f, 0.0f, 100.0f, 50.0f));

        Assert.AreEqual(new SizeF(100.0f, 50.0f), desired);
        Assert.AreEqual(new RectangleF(0.0f, 0.0f, 45.0f, 20.0f), first.Bounds);
        Assert.AreEqual(new RectangleF(55.0f, 0.0f, 45.0f, 20.0f), second.Bounds);
        Assert.AreEqual(new RectangleF(0.0f, 30.0f, 100.0f, 20.0f), third.Bounds);
    }

    [TestMethod]
    public void HiddenChildrenTakeNoCell()
    {
        var first = new Cell();
        var hidden = new Cell() { IsVisible = false };
        var second = new Cell();
        var grid = new WrapGrid(40.0f, 20.0f, first, hidden, second);

        grid.Measure(new SizeF(100.0f, 500.0f));
        grid.Arrange(new RectangleF(0.0f, 0.0f, 100.0f, 20.0f));

        Assert.AreEqual(new RectangleF(50.0f, 0.0f, 50.0f, 20.0f), second.Bounds);
        Assert.AreEqual(RectangleF.Empty, hidden.Bounds);
    }

    [TestMethod]
    public void TooNarrowForOneCellStillKeepsEveryChildInItsOwnRow()
    {
        var first = new Cell();
        var second = new Cell();
        var grid = new WrapGrid(40.0f, 20.0f, first, second);

        SizeF desired = grid.Measure(new SizeF(10.0f, 500.0f));
        grid.Arrange(new RectangleF(0.0f, 0.0f, 10.0f, 40.0f));

        Assert.AreEqual(new SizeF(10.0f, 40.0f), desired);
        Assert.AreEqual(new RectangleF(0.0f, 20.0f, 10.0f, 20.0f), second.Bounds);
    }

    private sealed class Cell : UiElement;
}
