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

        Assert.AreEqual(middle, navigator.CurrentEntry!.FullName);
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

        Assert.IsNull(navigator.CurrentEntry);
        CollectionAssert.AreEqual(new[] { predicted }, navigator.GetFiles().Select(file => file.FullName).ToArray());
        Assert.AreEqual(predicted, navigator.MoveToNextPath());
        Assert.AreEqual(predicted, navigator.CurrentEntry!.FullName);
        Assert.AreEqual(loaded, navigator.MoveToNextPath());
        Assert.IsNull(navigator.CurrentEntry);
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

    [TestMethod]
    public void BatchesMergeIntoSortOrderAndReplaceTheStandInForTheCurrentFile()
    {
        const string first = @"C:\images\a.jpg";
        const string current = @"C:\images\sub\b.jpg";
        const string nested = @"C:\images\z\c.jpg";
        const string last = @"C:\images\d.jpg";
        var navigator = new FolderNavigator();
        navigator.SetCurrent(current);

        navigator.AddFiles([Entry(nested), Entry(first)]);
        Assert.IsNull(navigator.CurrentEntry);
        navigator.AddFiles([Entry(last), Entry(current)]);

        CollectionAssert.AreEqual(
            new[] { first, current, nested, last },
            navigator.GetFiles().Select(file => file.FullName).ToArray());
        Assert.AreEqual(current, navigator.CurrentEntry!.FullName);
        Assert.AreEqual(nested, navigator.GetNextPath());
        Assert.AreEqual(first, navigator.GetPreviousPath());
    }

    private static FolderEntry Entry(string path) => new(path, 1, default, default);

    private sealed class FolderFiles
    {
        internal const string Path = @"C:\virtual-images";
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
