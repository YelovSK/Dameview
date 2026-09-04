using System.Collections.Concurrent;
using Dameview.Imaging;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class ImageLoadCoordinatorTests
{
    [TestMethod]
    public void ActiveDecodeFinishesButOnlyTheNewestPendingRequestIsDelivered()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        var decodedPaths = new ConcurrentQueue<string>();
        var deliveredPaths = new ConcurrentQueue<string>();
        using var decoder = new FakeImageDecoder(path =>
        {
            decodedPaths.Enqueue(path);
            if (path == "first")
            {
                firstStarted.Set();
                releaseFirst.Wait();
            }

            return CreateImage();
        });
        using var coordinator = new ImageLoadCoordinator(
            action => action(),
            () => decoder);

        coordinator.Load("first", RecordResult);
        Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(5)));

        coordinator.Load("second", RecordResult);
        coordinator.Load("third", RecordResult);
        releaseFirst.Set();

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
        CollectionAssert.AreEqual(
            new[] { "first", "third" },
            decodedPaths.ToArray());
        CollectionAssert.AreEqual(new[] { "third" }, deliveredPaths.ToArray());

        void RecordResult(ImageLoadResult result)
        {
            deliveredPaths.Enqueue(result.Path);
            completed.Set();
        }
    }

    [TestMethod]
    public void DecodeFailuresAreDeliveredAsResults()
    {
        using var completed = new ManualResetEventSlim();
        ImageLoadResult? deliveredResult = null;
        using var decoder = new FakeImageDecoder(
            _ => throw new InvalidDataException("Broken image"));
        using var coordinator = new ImageLoadCoordinator(
            action => action(),
            () => decoder);

        coordinator.Load("broken.jpg", result =>
        {
            deliveredResult = result;
            completed.Set();
        });

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsInstanceOfType<ImageLoadFailed>(deliveredResult);
        Assert.AreEqual("broken.jpg", deliveredResult.Path);
    }

    [TestMethod]
    public void PostedResultIsIgnoredIfANewerRequestArrivesBeforeUiDelivery()
    {
        using var resultPosted = new SemaphoreSlim(0);
        var postedActions = new ConcurrentQueue<Action>();
        var deliveredPaths = new List<string>();
        using var decoder = new FakeImageDecoder(_ => CreateImage());
        using var coordinator = new ImageLoadCoordinator(
            action =>
            {
                postedActions.Enqueue(action);
                resultPosted.Release();
            },
            () => decoder);

        coordinator.Load("first", result => deliveredPaths.Add(result.Path));
        Assert.IsTrue(resultPosted.Wait(TimeSpan.FromSeconds(5)));

        coordinator.Load("second", result => deliveredPaths.Add(result.Path));
        Assert.IsTrue(postedActions.TryDequeue(out Action? deliverFirst));
        deliverFirst();

        Assert.IsTrue(resultPosted.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(postedActions.TryDequeue(out Action? deliverSecond));
        deliverSecond();

        CollectionAssert.AreEqual(new[] { "second" }, deliveredPaths);
    }

    private static DecodedImage CreateImage()
    {
        return new DecodedImage(1, 1, 4, new byte[4]);
    }

    private sealed class FakeImageDecoder : IImageDecoder
    {
        private readonly Func<string, DecodedImage> _decode;

        internal FakeImageDecoder(Func<string, DecodedImage> decode)
        {
            _decode = decode;
        }

        public DecodedImage Decode(string path)
        {
            return _decode(path);
        }

        public void Dispose()
        {
        }
    }
}
