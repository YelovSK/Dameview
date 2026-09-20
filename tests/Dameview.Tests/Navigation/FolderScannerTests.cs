using Dameview.Navigation;

namespace Dameview.Tests.Navigation;

[TestClass]
public sealed class FolderScannerTests
{
    [TestMethod]
    public async Task ScanRunsOffCallerThreadAndProducesMetadataIndependentOfDisk()
    {
        string directory = Directory.CreateTempSubdirectory("Dameview.Scan.Tests.").FullName;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<FolderEntry[]>? scan = null;
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "a.jpg"), new byte[10]);
            File.WriteAllBytes(Path.Combine(directory, "b.jpg"), new byte[20]);
            File.WriteAllBytes(Path.Combine(directory, "ignored.txt"), []);
            int callerThread = Environment.CurrentManagedThreadId;
            int workerThread = callerThread;
            var scanner = new FolderScanner(path =>
            {
                workerThread = Environment.CurrentManagedThreadId;
                entered.Set();
                release.Wait();
                return Path.GetExtension(path) == ".jpg";
            });
            scan = scanner.ScanAsync(directory, CancellationToken.None);
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.AreNotEqual(callerThread, workerThread, "The scan must not block its caller.");
            release.Set();
            FolderEntry[] files = await scan;
            Assert.HasCount(2, files);
            Directory.Delete(directory, recursive: true);

            // The entries carry their own metadata, so the folder going away does not blank them.
            Assert.AreEqual(30L, files.Sum(file => file.Length));
        }
        finally
        {
            release.Set();
            if (scan is not null)
            {
                await scan;
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

}
