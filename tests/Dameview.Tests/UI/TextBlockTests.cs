using System.Drawing;
using Dameview.UI.Components;
using Dameview.UI.Foundation;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class TextBlockTests
{
    [TestMethod]
    public void WrappedTextMeasuresEnoughHeightForEveryLine()
    {
        var text = new TextBlock(
            "Updates are only available when running the installed copy of Dameview.",
            UiTextStyle.Body,
            UiTextTone.Secondary,
            UiTextWrapping.Wrap);
        _ = new UiRoot(text, UiDpi.Default, TestTextLayouts.Shared);

        SizeF desiredSize = text.Measure(new SizeF(392.0f, float.PositiveInfinity));

        Assert.IsGreaterThan(24.0f, desiredSize.Height);
    }
}
