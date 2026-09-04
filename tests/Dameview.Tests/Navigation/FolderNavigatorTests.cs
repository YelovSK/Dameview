using Dameview.Navigation;

namespace Dameview.Tests.Navigation;

[TestClass]
public sealed class FolderNavigatorTests
{
    [TestMethod]
    public void MovesInNameOrderAndWrapsAround()
    {
        using var directory = new TemporaryDirectory();
        string first = directory.CreateFile("a.jpg", 1);
        string middle = directory.CreateFile("b.jpg", 1);
        string last = directory.CreateFile("c.jpg", 1);
        _ = directory.CreateFile("ignored.txt", 1);

        var navigator = new FolderNavigator(IsJpeg);
        navigator.SetCurrent(middle);

        Assert.AreEqual(last, navigator.GetNextPath());
        Assert.AreEqual(first, navigator.GetPreviousPath());

        navigator.SetCurrent(last);
        Assert.AreEqual(first, navigator.GetNextPath());
    }

    [TestMethod]
    public void ChangingSortKeepsTheCurrentFileSelected()
    {
        using var directory = new TemporaryDirectory();
        string smaller = directory.CreateFile("a.jpg", 1);
        string larger = directory.CreateFile("b.jpg", 10);

        var navigator = new FolderNavigator(IsJpeg);
        navigator.SetCurrent(smaller);
        navigator.SetSort(FolderSort.SizeLargest);

        Assert.AreEqual(larger, navigator.GetPreviousPath());
    }

    [TestMethod]
    public void LoadedFileIsNavigableEvenWhenItsExtensionWasNotPredicted()
    {
        using var directory = new TemporaryDirectory();
        string predicted = directory.CreateFile("a.jpg", 1);
        string loaded = directory.CreateFile("b.unknown", 1);

        var navigator = new FolderNavigator(IsJpeg);
        navigator.SetCurrent(loaded);

        Assert.AreEqual(predicted, navigator.GetNextPath());
    }

    private static bool IsJpeg(string path)
    {
        return string.Equals(Path.GetExtension(path), ".jpg", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("Dameview.Tests.").FullName;
        }

        internal string Path { get; }

        internal string CreateFile(string name, int size)
        {
            string path = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(path, new byte[size]);
            return path;
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
