using Dameview.Imaging;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class DecodedImageUploadTests
{
    [TestMethod]
    public void PoolReusesReturnedNativeBuffer()
    {
        using var pool = new NativePixelBufferPool();
        nint firstPointer;
        using (var first = DecodedImageUpload.Rent(pool, 4, 4, 16))
        {
            firstPointer = first.Pixels;
        }

        using var second = DecodedImageUpload.Rent(pool, 2, 2, 8);
        Assert.AreEqual(firstPointer, second.Pixels);
    }

    [TestMethod]
    public void RetainedLeaseKeepsNativeBufferOutOfPoolUntilEveryLeaseIsDisposed()
    {
        using var pool = new NativePixelBufferPool();
        var first = DecodedImageUpload.Rent(pool, 4, 4, 16);
        using DecodedImageUpload retained = first.Retain();
        nint sharedPointer = first.Pixels;
        first.Dispose();

        using var other = DecodedImageUpload.Rent(pool, 4, 4, 16);
        Assert.AreNotEqual(sharedPointer, other.Pixels);
        Assert.AreEqual(sharedPointer, retained.Pixels);
    }
}
