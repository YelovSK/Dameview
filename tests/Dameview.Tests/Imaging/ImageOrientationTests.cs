using System.Drawing;
using System.Numerics;
using Dameview.Imaging;
using Dameview.Imaging.Decoding;
using Vortice.WIC;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class ImageOrientationTests
{
    private const int StoredWidth = 3;
    private const int StoredHeight = 2;

    // Stored pixels are numbered 1 to 6 row by row; each list is how they read once shown.
    private static readonly Dictionary<int, byte[]> ShownIdsByExif = new()
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

    [TestMethod]
    public void CopiesEveryExifOrientationInShownOrder()
    {
        foreach ((int exif, byte[] expectedIds) in ShownIdsByExif)
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

    [TestMethod]
    public void WicTurnsEveryExifOrientationTheSameWay()
    {
        byte[] stored = new byte[StoredWidth * StoredHeight * 4];
        for (int index = 0; index < StoredWidth * StoredHeight; index++)
        {
            stored[index * 4] = (byte)(index + 1);
        }

        using var factory = new IWICImagingFactory2();
        using IWICBitmap bitmap = factory.CreateBitmapFromMemory(
            StoredWidth,
            StoredHeight,
            PixelFormat.Format32bppBGRA,
            stored,
            StoredWidth * 4);
        foreach ((int exif, byte[] expectedIds) in ShownIdsByExif)
        {
            using IWICBitmapFlipRotator rotator = factory.CreateBitmapFlipRotator();
            rotator.Initialize(bitmap, ImageDecoder.GetTransformOptions(ImageOrientation.FromExif(exif)));
            byte[] shown = new byte[stored.Length];
            rotator.CopyPixels((uint)(rotator.Size.Width * 4), shown);

            CollectionAssert.AreEqual(expectedIds, shown.Where((_, index) => index % 4 == 0).ToArray(), $"EXIF {exif}");
        }
    }

    private static IEnumerable<ImageOrientation> AllOrientations() =>
        Enumerable.Range(1, 8).Select(ImageOrientation.FromExif);

    // Places each stored pixel id where the orientation's transform puts the pixel's center.
    private static Shown Show(ImageOrientation orientation)
    {
        var storedSize = new SizeF(StoredWidth, StoredHeight);
        Size size = orientation.Apply(new Size(StoredWidth, StoredHeight));
        Matrix3x2 transform = orientation.GetTransform(storedSize);
        byte[] ids = new byte[StoredWidth * StoredHeight];
        for (int y = 0; y < StoredHeight; y++)
        {
            for (int x = 0; x < StoredWidth; x++)
            {
                var shown = Vector2.Transform(new Vector2(x + 0.5f, y + 0.5f), transform);
                ids[((int)shown.Y * size.Width) + (int)shown.X] = (byte)((y * StoredWidth) + x + 1);
            }
        }

        return new Shown(size.Width, size.Height, ids);
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
