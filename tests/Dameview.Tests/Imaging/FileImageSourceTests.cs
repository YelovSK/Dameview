using Dameview.Imaging.Decoding;
using Dameview.Imaging.Loading;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class FileImageSourceTests
{
    [TestMethod]
    [DataRow("large.png")]
    [DataRow("large.webp")]
    [DataRow("large.gif")]
    public void UsesTiledRepresentationWithoutFullDecode(string path)
    {
        int decodeCount = 0;
        var tiles = new FakeTileSource();
        var source = new FileImageSource(
            new FakeImageLoadingBackend(
                () => new FakeImageDecoder(
                    _ =>
                    {
                        decodeCount++;
                        return FakeImageDecoder.CreateImage();
                    },
                    _ => new ImageInfo(20_000, 10_000, 1)),
                new FakeAnimatedImageDecoder(_ => throw new AssertFailedException("Static image opened as animation.")),
                _ => tiles),
            PipelineHarness.TestPolicy);

        using ImageRepresentation? image = Load(source, path, new FakeLoadContext());

        Assert.IsInstanceOfType<TiledImageRepresentation>(image);
        Assert.AreEqual(0, decodeCount);
        image.Dispose();
        Assert.IsTrue(tiles.IsDisposed);
    }

    [TestMethod]
    public void TellsTheContextWhatAStaticImageWouldTakeInTheCache()
    {
        var context = new FakeLoadContext();
        var source = new FileImageSource(
            new FakeImageLoadingBackend(() => new FakeImageDecoder(
                _ => FakeImageDecoder.CreateImage(10, 10),
                _ => new ImageInfo(10, 10, 1))),
            PipelineHarness.TestPolicy);

        using ImageRepresentation? image = Load(source, "image", context);

        CollectionAssert.AreEqual(new long?[] { 400 }, context.Asked);
        Assert.IsInstanceOfType<UploadImageRepresentation>(image);
    }

    [TestMethod]
    [DataRow(20_000, 10_000, 1)]
    [DataRow(1, 1, 2)]
    public void TellsTheContextThatTiledAndAnimatedImagesWontBeCached(int width, int height, int frames)
    {
        var context = new FakeLoadContext(_ => false);
        var source = new FileImageSource(
            new FakeImageLoadingBackend(
                () => new FakeImageDecoder(
                    _ => throw new AssertFailedException("Decoded a skipped image."),
                    _ => new ImageInfo(width, height, frames)),
                new FakeAnimatedImageDecoder(_ => throw new AssertFailedException("Opened a skipped animation.")),
                _ => throw new AssertFailedException("Opened a skipped tiled image.")),
            PipelineHarness.TestPolicy);

        Assert.IsNull(Load(source, "image", context));
        CollectionAssert.AreEqual(new long?[] { null }, context.Asked);
    }

    [TestMethod]
    [DataRow(1, true, false)]
    [DataRow(2, true, true)]
    [DataRow(2, false, false)]
    public void AnimatesOnlyMultipleFramesWithAnAnimationDecoder(int frameCount, bool supported, bool animated)
    {
        var source = new FileImageSource(
            new FakeImageLoadingBackend(
                () => new FakeImageDecoder(_ => FakeImageDecoder.CreateImage(), _ => new ImageInfo(1, 1, frameCount)),
                supported ? new FakeAnimatedImageDecoder(_ => new FakeAnimationSession()) : null),
            PipelineHarness.TestPolicy);

        using ImageRepresentation? image = Load(source, "image", new FakeLoadContext());

        if (animated)
        {
            Assert.IsInstanceOfType<AnimatedImageRepresentation>(image);
        }
        else
        {
            Assert.IsInstanceOfType<UploadImageRepresentation>(image);
        }
    }

    private static ImageRepresentation? Load(FileImageSource source, string path, ImageLoadContext context)
    {
        using var decoder = (IImageDecoder)source.CreateWorkerState()!;
        return source.Load(decoder, path, context);
    }
}
