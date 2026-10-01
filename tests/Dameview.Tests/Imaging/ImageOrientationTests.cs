using Dameview.Imaging;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class ImageOrientationTests
{
    private const int StoredWidth = 3;
    private const int StoredHeight = 2;

    [TestMethod]
    public void CopiesEveryExifOrientationInShownOrder()
    {
        Dictionary<int, byte[]> expected = new()
        {
            [1] = [1, 2, 3, 4, 5, 6],
            [2] = [3, 2, 1, 6, 5, 4],
            [3] = [6, 5, 4, 3, 2, 1],
            [4] = [4, 5, 6, 1, 2, 3],
            [5] = [1, 4, 2, 5, 3, 6],
            [6] = [4, 1, 5, 2, 6, 3],
            [7] = [6, 3, 5, 2, 4, 1],
            [8] = [3, 6, 2, 5, 1, 4],
        };

        foreach ((int exif, byte[] expectedIds) in expected)
        {
            CollectionAssert.AreEqual(expectedIds, Show(ImageOrientation.FromExif(exif)).Ids, $"EXIF {exif}");
        }
    }

    [TestMethod]
    public void TurnsAndFlipsActOnTheShownImage()
    {
        foreach (ImageOrientation orientation in AllOrientations())
        {
            Shown shown = Show(orientation);
            Shown clockwise = TurnClockwise(shown);
            Shown upsideDown = TurnClockwise(clockwise);

            AssertShown(clockwise, Show(orientation.Rotate(1)), $"{orientation} turned right");
            AssertShown(TurnClockwise(upsideDown), Show(orientation.Rotate(-1)), $"{orientation} turned left");
            AssertShown(Mirror(shown), Show(orientation.FlipHorizontal()), $"{orientation} flipped horizontally");
            AssertShown(Mirror(upsideDown), Show(orientation.FlipVertical()), $"{orientation} flipped vertically");
        }
    }

    private static IEnumerable<ImageOrientation> AllOrientations() =>
        Enumerable.Range(1, 8).Select(ImageOrientation.FromExif);

    private static Shown Show(ImageOrientation orientation)
    {
        using var stored = DecodedImageUpload.Allocate(
            StoredWidth,
            StoredHeight,
            StoredWidth * 4,
            orientation);
        Span<byte> pixels = stored.Span;
        for (int index = 0; index < StoredWidth * StoredHeight; index++)
        {
            pixels[index * 4] = (byte)(index + 1);
        }

        using DecodedImageUpload oriented = stored.CopyOriented();
        byte[] ids = new byte[oriented.Width * oriented.Height];
        for (int index = 0; index < ids.Length; index++)
        {
            ids[index] = oriented.Span[index * 4];
        }

        return new Shown(oriented.Width, oriented.Height, ids);
    }

    private static Shown TurnClockwise(Shown image)
    {
        byte[] ids = new byte[image.Ids.Length];
        for (int y = 0; y < image.Width; y++)
        {
            for (int x = 0; x < image.Height; x++)
            {
                ids[(y * image.Height) + x] = image.Ids[((image.Height - 1 - x) * image.Width) + y];
            }
        }

        return new Shown(image.Height, image.Width, ids);
    }

    private static Shown Mirror(Shown image)
    {
        byte[] ids = new byte[image.Ids.Length];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                ids[(y * image.Width) + x] = image.Ids[(y * image.Width) + image.Width - 1 - x];
            }
        }

        return image with { Ids = ids };
    }

    private static void AssertShown(Shown expected, Shown actual, string because)
    {
        Assert.AreEqual(expected.Width, actual.Width, because);
        CollectionAssert.AreEqual(expected.Ids, actual.Ids, because);
    }

    private sealed record Shown(int Width, int Height, byte[] Ids);
}
