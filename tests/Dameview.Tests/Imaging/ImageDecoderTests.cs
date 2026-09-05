using Dameview.Imaging;
using Vortice.WIC;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class ImageDecoderTests
{
    [TestMethod]
    public void ParsesWicFileExtensionLists()
    {
        HashSet<string> extensions = ImageDecoder.ParseFileExtensions("*.jpg, *.JPEG;*.png\0");

        CollectionAssert.AreEquivalent(
            new[] { ".jpg", ".jpeg", ".png" },
            extensions.ToArray());
    }

    [TestMethod]
    [DataRow(1, BitmapTransformOptions.Rotate0)]
    [DataRow(2, BitmapTransformOptions.FlipHorizontal)]
    [DataRow(3, BitmapTransformOptions.Rotate180)]
    [DataRow(4, BitmapTransformOptions.FlipVertical)]
    [DataRow(5, BitmapTransformOptions.FlipHorizontal | BitmapTransformOptions.Rotate270)]
    [DataRow(6, BitmapTransformOptions.Rotate90)]
    [DataRow(7, BitmapTransformOptions.FlipHorizontal | BitmapTransformOptions.Rotate90)]
    [DataRow(8, BitmapTransformOptions.Rotate270)]
    public void MapsExifOrientation(int rawOrientation, BitmapTransformOptions expected)
    {
        Assert.AreEqual(expected, ImageDecoder.MapExifOrientation((ExifOrientation)rawOrientation));
    }
}
