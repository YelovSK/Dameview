using Dameview.Navigation;

namespace Dameview.Tests.Navigation;

[TestClass]
public sealed class FolderSortMemoryTests
{
    private const string A = @"C:\Photos\A";
    private const string B = @"C:\Photos\A\B";
    private const string C = @"C:\Photos\A\C";

    [TestMethod]
    public void SubfoldersInheritTheNearestSortThatIncludesThem()
    {
        var memory = new FolderSortMemory();
        memory.Set(A, FolderSort.SizeLargest, includesSubfolders: true);
        memory.Set(B, FolderSort.NameDescending, includesSubfolders: false);

        Assert.AreEqual(FolderSort.NameDescending, memory.Resolve(B).Own);
        Assert.IsNull(memory.Resolve(C).Own);
        Assert.AreEqual(new InheritedFolderSort(FolderSort.SizeLargest, A), memory.Resolve(C).Inherited);
        // B's own sort covers only B, so its subfolders keep looking further up.
        Assert.AreEqual(new InheritedFolderSort(FolderSort.SizeLargest, A), memory.Resolve(B + @"\D").Inherited);
    }

    [TestMethod]
    public void SortForOneFolderIsNotInherited()
    {
        var memory = new FolderSortMemory();
        memory.Set(A, FolderSort.SizeLargest, includesSubfolders: false);

        Assert.IsNull(memory.Resolve(C).Inherited);
    }

    [TestMethod]
    public void ForgettingASortInheritsAgain()
    {
        var memory = new FolderSortMemory();
        memory.Set(A, FolderSort.SizeLargest, includesSubfolders: true);
        memory.Set(C, FolderSort.NameDescending, includesSubfolders: false);

        memory.Set(C, null, includesSubfolders: false);

        Assert.IsNull(memory.Resolve(C).Own);
        Assert.AreEqual(FolderSort.SizeLargest, memory.Resolve(C).Inherited?.Sort);
    }

    [TestMethod]
    public void PathsIgnoreCase()
    {
        var memory = new FolderSortMemory();
        memory.Set(A, FolderSort.SizeLargest, includesSubfolders: true);

        Assert.AreEqual(FolderSort.SizeLargest, memory.Resolve(A.ToUpperInvariant()).Own);
        Assert.AreEqual(FolderSort.SizeLargest, memory.Resolve(C.ToLowerInvariant()).Inherited?.Sort);
    }

    [TestMethod]
    public void DropsTheLeastRecentlyUsedFolderWhenFull()
    {
        var memory = new FolderSortMemory();
        for (int index = 0; index < 1000; index++)
        {
            memory.Set($@"C:\{index}", FolderSort.SizeLargest, includesSubfolders: false);
        }

        memory.Resolve(@"C:\0");
        memory.Set(@"C:\new", FolderSort.SizeLargest, includesSubfolders: false);

        Assert.IsNotNull(memory.Resolve(@"C:\0").Own);
        Assert.IsNull(memory.Resolve(@"C:\1").Own);
        Assert.IsNotNull(memory.Resolve(@"C:\new").Own);
    }

    [TestMethod]
    public void ReadsWhatItWritesInUseOrder()
    {
        var memory = new FolderSortMemory();
        memory.Set(A, FolderSort.SizeLargest, includesSubfolders: true);
        memory.Set(B, FolderSort.NameDescending, includesSubfolders: false);
        memory.Resolve(A);

        string text = memory.Write();
        var read = FolderSortMemory.Read(text + "garbage\nSizeLargest\tfolder\n99\tfolder\tC:\\X\n");

        Assert.AreEqual(text, read.Write());
        Assert.AreEqual(FolderSort.SizeLargest, read.Resolve(C).Inherited?.Sort);
        Assert.AreEqual(FolderSort.NameDescending, read.Resolve(B).Own);
    }

    [TestMethod]
    public void ReportsOnlyActualChanges()
    {
        var memory = new FolderSortMemory();
        int changes = 0;
        memory.Changed += () => changes++;

        memory.Set(A, null, includesSubfolders: false);
        memory.Set(A, FolderSort.SizeLargest, includesSubfolders: false);
        memory.Set(A, null, includesSubfolders: false);

        Assert.AreEqual(2, changes);
    }
}
