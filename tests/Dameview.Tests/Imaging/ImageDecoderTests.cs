using Dameview.Imaging;

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
}
