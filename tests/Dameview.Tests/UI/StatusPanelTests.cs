using Dameview.UI.Panels;

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
}
