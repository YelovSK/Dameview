using Dameview.Imaging;
using Dameview.Imaging.Decoding;
using Dameview.Imaging.Loading;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class WicImageTileSourceTests
{
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
