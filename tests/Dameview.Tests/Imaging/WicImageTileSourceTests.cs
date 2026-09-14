using Dameview.Imaging;
using Dameview.Imaging.Decoding;
using Dameview.Imaging.Loading;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class WicImageTileSourceTests
{
    [TestMethod]
    public void FullTileAppliesEveryExifOrientation()
    {
        const int rawWidth = 3;
        const int rawHeight = 2;
        byte[] rawPixels = CreatePixels(rawWidth, rawHeight);
        Dictionary<ExifOrientation, byte[]> expected = new()
        {
            [ExifOrientation.Normal] = [1, 2, 3, 4, 5, 6],
            [ExifOrientation.MirrorHorizontal] = [3, 2, 1, 6, 5, 4],
            [ExifOrientation.Rotate180] = [6, 5, 4, 3, 2, 1],
            [ExifOrientation.MirrorVertical] = [4, 5, 6, 1, 2, 3],
            [ExifOrientation.Transpose] = [1, 4, 2, 5, 3, 6],
            [ExifOrientation.Rotate90Clockwise] = [4, 1, 5, 2, 6, 3],
            [ExifOrientation.Transverse] = [6, 3, 5, 2, 4, 1],
            [ExifOrientation.Rotate270Clockwise] = [3, 6, 2, 5, 1, 4],
        };

        foreach ((ExifOrientation orientation, byte[] expectedPixels) in expected)
        {
            (int width, int height) = WicImageTileSource.GetDisplaySize(
                rawWidth,
                rawHeight,
                orientation);
            DecodedImage image = Transform(
                orientation, rawWidth, rawHeight, rawPixels, 0, 0, width, height);

            CollectionAssert.AreEqual(expectedPixels, GetPixelIds(image));
            if (orientation == ExifOrientation.Normal)
            {
                Assert.AreSame(rawPixels, image.Pixels);
            }
        }
    }

    [TestMethod]
    public void NonSquareEdgeTilesMatchTheFullyOrientedImage()
    {
        const int rawWidth = 9;
        const int rawHeight = 7;
        byte[] rawPixels = CreatePixels(rawWidth, rawHeight);

        foreach (ExifOrientation orientation in Enum.GetValues<ExifOrientation>())
        {
            foreach (int sourceScale in new[] { 1, 2 })
            {
                (int displayWidth, int displayHeight) = WicImageTileSource.GetDisplaySize(
                    (rawWidth + sourceScale - 1) / sourceScale,
                    (rawHeight + sourceScale - 1) / sourceScale,
                    orientation);
                DecodedImage fullImage = Transform(
                    orientation,
                    rawWidth,
                    rawHeight,
                    rawPixels,
                    0,
                    0,
                    displayWidth,
                    displayHeight,
                    sourceScale);
                int tileWidth = Math.Min(2, displayWidth);
                int tileHeight = Math.Min(2, displayHeight);
                int tileX = displayWidth - tileWidth;
                int tileY = displayHeight - tileHeight;
                DecodedImage tile = Transform(
                    orientation,
                    rawWidth,
                    rawHeight,
                    rawPixels,
                    tileX,
                    tileY,
                    tileWidth,
                    tileHeight,
                    sourceScale);

                CollectionAssert.AreEqual(
                    Crop(fullImage.Pixels, displayWidth, tileX, tileY, tileWidth, tileHeight),
                    tile.Pixels,
                    $"{orientation}, scale {sourceScale}");
            }
        }
    }

    [TestMethod]
    public async Task RegionDecodeMatchesTheFullWicDecode()
    {
        string path = Path.Combine(Path.GetTempPath(), $"Dameview.Tile.{Guid.NewGuid():N}.png");
        try
        {
            await using (Stream source = typeof(ImageDecoder).Assembly.GetManifestResourceStream(
                "Dameview.Assets.dameview.png")!)
            await using (FileStream destination = File.Create(path))
            {
                await source.CopyToAsync(destination);
            }

            using IImageTileSource tiles = WicImageTileSource.Open(path);
            using IImageTileDecoder tileDecoder = tiles.CreateTileDecoder();
            using var decoder = new ImageDecoder();
            DecodedImage fullImage = decoder.Decode(path);
            using DecodedImageUpload upload = decoder.DecodeUpload(path);
            Assert.AreEqual(fullImage.Width, upload.Width);
            Assert.AreEqual(fullImage.Height, upload.Height);
            CollectionAssert.AreEqual(fullImage.Pixels, upload.Span.ToArray());
            int width = Math.Min(17, tiles.Width);
            int height = Math.Min(11, tiles.Height);
            int x = tiles.Width - width;
            int y = tiles.Height - height;

            DecodedImage tile = tileDecoder.DecodeTile(
                new ImageTile(x, y, width, height),
                CancellationToken.None);

            Assert.AreEqual(512, tiles.TileSize);
            CollectionAssert.AreEqual(
                Crop(fullImage.Pixels, fullImage.Width, x, y, width, height),
                tile.Pixels);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                tileDecoder.DecodeTile(new ImageTile(-1, 0, 1, 1), CancellationToken.None));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                tileDecoder.DecodeTile(new ImageTile(0, 0, 0, 1), CancellationToken.None));

            int mipWidth = ImageTile.GetLevelDimension(tiles.Width, 1);
            int mipHeight = ImageTile.GetLevelDimension(tiles.Height, 1);
            DecodedImage fullMip = tileDecoder.DecodeTile(
                new ImageTile(0, 0, mipWidth, mipHeight, Level: 1),
                CancellationToken.None);
            int mipTileWidth = Math.Min(17, mipWidth);
            int mipTileHeight = Math.Min(11, mipHeight);
            int mipX = mipWidth - mipTileWidth;
            int mipY = mipHeight - mipTileHeight;
            DecodedImage mipTile = tileDecoder.DecodeTile(
                new ImageTile(mipX, mipY, mipTileWidth, mipTileHeight, Level: 1),
                CancellationToken.None);

            Assert.AreEqual(mipWidth, fullMip.Width);
            Assert.AreEqual(mipHeight, fullMip.Height);
            CollectionAssert.AreEqual(
                Crop(fullMip.Pixels, mipWidth, mipX, mipY, mipTileWidth, mipTileHeight),
                mipTile.Pixels);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static DecodedImage Transform(
        ExifOrientation orientation,
        int rawWidth,
        int rawHeight,
        byte[] rawPixels,
        int x,
        int y,
        int width,
        int height,
        int sourceScale = 1)
    {
        int displayX = x * sourceScale;
        int displayY = y * sourceScale;
        (int fullDisplayWidth, int fullDisplayHeight) = WicImageTileSource.GetDisplaySize(
            rawWidth,
            rawHeight,
            orientation);
        int displayRight = Math.Min(fullDisplayWidth, (x + width) * sourceScale);
        int displayBottom = Math.Min(fullDisplayHeight, (y + height) * sourceScale);
        (int rawX, int rawY, int clippedWidth, int clippedHeight) =
            WicImageTileSource.GetRawBounds(
                orientation,
                rawWidth,
                rawHeight,
                displayX,
                displayY,
                displayRight - displayX,
                displayBottom - displayY);
        byte[] clippedPixels = rawX == 0 &&
            rawY == 0 &&
            clippedWidth == rawWidth &&
            clippedHeight == rawHeight
                ? rawPixels
                : Crop(rawPixels, rawWidth, rawX, rawY, clippedWidth, clippedHeight);
        if (sourceScale > 1)
        {
            clippedPixels = Downsample(clippedPixels, clippedWidth, clippedHeight, sourceScale);
        }

        int scaledWidth = (clippedWidth + sourceScale - 1) / sourceScale;
        int scaledHeight = (clippedHeight + sourceScale - 1) / sourceScale;

        return WicImageTileSource.TransformTile(
            orientation,
            scaledWidth,
            scaledHeight,
            clippedPixels,
            scaledWidth * 4);
    }

    private static byte[] Downsample(byte[] pixels, int width, int height, int scale)
    {
        int outputWidth = (width + scale - 1) / scale;
        int outputHeight = (height + scale - 1) / scale;
        byte[] output = new byte[outputWidth * outputHeight * 4];
        for (int y = 0; y < outputHeight; y++)
        {
            for (int x = 0; x < outputWidth; x++)
            {
                Buffer.BlockCopy(
                    pixels,
                    (y * scale * width + x * scale) * 4,
                    output,
                    (y * outputWidth + x) * 4,
                    4);
            }
        }

        return output;
    }

    private static byte[] CreatePixels(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int index = 0; index < width * height; index++)
        {
            int offset = index * 4;
            pixels[offset] = (byte)(index + 1);
            pixels[offset + 1] = (byte)(index + 21);
            pixels[offset + 2] = (byte)(index + 41);
            pixels[offset + 3] = 255;
        }

        return pixels;
    }

    private static byte[] GetPixelIds(DecodedImage image)
    {
        byte[] ids = new byte[image.Width * image.Height];
        for (int index = 0; index < ids.Length; index++)
        {
            ids[index] = image.Pixels[index * 4];
        }

        return ids;
    }

    private static byte[] Crop(
        byte[] pixels,
        int sourceWidth,
        int x,
        int y,
        int width,
        int height)
    {
        int sourceStride = sourceWidth * 4;
        int outputStride = width * 4;
        byte[] output = new byte[outputStride * height];
        for (int row = 0; row < height; row++)
        {
            Buffer.BlockCopy(
                pixels,
                (y + row) * sourceStride + x * 4,
                output,
                row * outputStride,
                outputStride);
        }

        return output;
    }
}
