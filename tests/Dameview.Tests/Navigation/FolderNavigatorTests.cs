using Dameview.Navigation;

namespace Dameview.Tests.Navigation;

[TestClass]
public sealed class FolderNavigatorTests
{
    [TestMethod]
    public void MovesInNameOrderAndWrapsAround()
    {
        var directory = new FolderFiles();
        string first = directory.CreateFile("a.jpg", 1);
        string middle = directory.CreateFile("b.jpg", 1);
        string last = directory.CreateFile("c.jpg", 1);
        _ = directory.CreateFile("ignored.txt", 1);

        var navigator = new FolderNavigator();
        navigator.SetFiles(directory.Files, middle);

        Assert.AreEqual(last, navigator.GetNextPath());
        Assert.AreEqual(first, navigator.GetPreviousPath());

        navigator.SetCurrent(last);
        Assert.AreEqual(first, navigator.GetNextPath());
    }

    [TestMethod]
    public void ChangingSortKeepsTheCurrentFileSelected()
    {
        var directory = new FolderFiles();
        string smaller = directory.CreateFile("a.jpg", 1);
        string larger = directory.CreateFile("b.jpg", 10);

        var navigator = new FolderNavigator();
        navigator.SetFiles(directory.Files, smaller);
        navigator.SetSort(FolderSort.SizeLargest);

        Assert.AreEqual(larger, navigator.GetPreviousPath());
    }

    [TestMethod]
    public void LoadedFileIsNavigableEvenWhenItsExtensionWasNotPredicted()
    {
        var directory = new FolderFiles();
        string predicted = directory.CreateFile("a.jpg", 1);
        string loaded = directory.CreateFile("b.unknown", 1);

        var navigator = new FolderNavigator();
        navigator.SetFiles(directory.Files, loaded);

        Assert.AreEqual(predicted, navigator.GetNextPath());
    }

    [TestMethod]
    public void MovingRepeatedlyAdvancesTheSelectionImmediately()
    {
        var directory = new FolderFiles();
        string first = directory.CreateFile("a.jpg", 1);
        string middle = directory.CreateFile("b.jpg", 1);
        string last = directory.CreateFile("c.jpg", 1);
        var navigator = new FolderNavigator();
        navigator.SetFiles(directory.Files, first);

        Assert.AreEqual(middle, navigator.MoveToNextPath());
        Assert.AreEqual(last, navigator.MoveToNextPath());
        Assert.AreEqual(first, navigator.MoveToNextPath());
        Assert.AreEqual(last, navigator.MoveToPreviousPath());
    }

    private sealed class FolderFiles
    {
        internal string Path => @"C:\virtual-images";
        internal List<FolderEntry> Files { get; } = [];

        internal string CreateFile(string name, int size)
        {
            string path = System.IO.Path.Combine(Path, name);
            if (name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                Files.Add(new FolderEntry(path, size, default, default));
            }

            return path;
        }

    }
}
