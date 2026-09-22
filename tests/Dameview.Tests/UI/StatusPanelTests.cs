using System.Drawing;
using Dameview.UI.Foundation;
using Dameview.UI.Panels;
using Vortice.DirectWrite;
using static Vortice.DirectWrite.DWrite;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class StatusPanelTests
{
    [TestMethod]
    [DataRow(0L, "0 B")]
    [DataRow(1023L, "1023 B")]
    [DataRow(1536L, "1.5 KB")]
    [DataRow(5_242_880L, "5 MB")]
    [DataRow(1_610_612_736L, "1.5 GB")]
    public void FileSizesAreFormattedForDisplay(long sizeBytes, string expected)
    {
        Assert.AreEqual(expected, StatusPanel.FormatFileSize(sizeBytes));
    }

    // Any pane invalidating layout re-measures every other pane, so a status that has not
    // changed has to come back out of the cache rather than be shaped again.
    [TestMethod]
    public void MeasuringAnUnchangedStatusDoesNotShapeItsTextAgain()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        using var layouts = new UiTextLayoutCache(factory);
        var panel = new StatusPanel();
        _ = new UiRoot(panel, UiDpi.Default, layouts);
        panel.SetStatus(new ViewerStatus("photo.png", 1920, 1080, 2048L, 100.0f, null, false));
        var available = new SizeF(600.0f, StatusPanel.HeightDips);

        panel.Measure(available);
        int shaped = layouts.Count;
        panel.Measure(available);

        Assert.AreEqual(2, shaped, "The file name and the details are each shaped once.");
        Assert.AreEqual(shaped, layouts.Count, "Measuring again reuses both.");
    }
}
