using Dameview.Navigation;

namespace Dameview.Tests.Navigation;

[TestClass]
public sealed class FolderScannerTests
{
    private static readonly FolderScanner Scanner = new(path => path.EndsWith(".jpg", StringComparison.Ordinal));

    [TestMethod]
    public void ScanProducesMetadataIndependentOfDisk()
    {
        string directory = Directory.CreateTempSubdirectory("Dameview.Scan.Tests.").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "a.jpg"), new byte[10]);
            File.WriteAllBytes(Path.Combine(directory, "b.jpg"), new byte[20]);
            File.WriteAllBytes(Path.Combine(directory, "ignored.txt"), []);
            Directory.CreateDirectory(Path.Combine(directory, "sub"));
            File.WriteAllBytes(Path.Combine(directory, "sub", "nested.jpg"), []);

            FolderEntry[] files =
                [.. Scanner.Scan(new FolderScope(directory, Recursive: false), CancellationToken.None)];
            Assert.HasCount(2, files);
            Directory.Delete(directory, recursive: true);

            // The entries carry their own metadata, so the folder going away does not blank them.
            Assert.AreEqual(30L, files.Sum(file => file.Length));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void RecursiveScanIncludesSubfoldersExceptHiddenOnes()
    {
        string directory = Directory.CreateTempSubdirectory("Dameview.Scan.Tests.").FullName;
        try
        {
            string top = Path.Combine(directory, "a.jpg");
            string nested = Path.Combine(directory, "sub", "deeper", "b.jpg");
            string hidden = Path.Combine(directory, ".hidden", "c.jpg");
            foreach (string file in new[] { top, nested, hidden })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllBytes(file, []);
            }

            var hiddenFolder = new DirectoryInfo(Path.GetDirectoryName(hidden)!);
            hiddenFolder.Attributes |= FileAttributes.Hidden;

            string[] files = [.. Scanner.Scan(new FolderScope(directory, Recursive: true), CancellationToken.None)
                .Select(file => file.FullName)
                .Order()];
            CollectionAssert.AreEqual(new[] { top, nested }, files);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ChangesAreJudgedByTheSameFoldersTheScanSkips()
    {
        string directory = Directory.CreateTempSubdirectory("Dameview.Scan.Tests.").FullName;
        try
        {
            string hiddenFolder = Path.Combine(directory, ".hidden");
            Directory.CreateDirectory(Path.Combine(hiddenFolder, "deeper"));
            Directory.CreateDirectory(Path.Combine(directory, "sub"));
            File.SetAttributes(hiddenFolder, FileAttributes.Directory | FileAttributes.Hidden);
            var scope = new FolderScope(directory, Recursive: true);

            Assert.IsTrue(Scanner.WouldInclude(scope, Path.Combine(directory, "sub", "a.jpg")));
            Assert.IsFalse(Scanner.WouldInclude(scope, Path.Combine(directory, "sub", "a.txt")));
            Assert.IsFalse(Scanner.WouldInclude(scope, Path.Combine(hiddenFolder, "deeper", "a.jpg")));
            Assert.IsTrue(
                Scanner.WouldInclude(new FolderScope(hiddenFolder, Recursive: true), Path.Combine(hiddenFolder, "deeper", "a.jpg")),
                "A hidden folder that was flattened itself is still watched.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(@"C:\images\a.jpg", false, true)]
    [DataRow(@"C:\images\sub\a.jpg", false, false)]
    [DataRow(@"C:\images\sub\a.jpg", true, true)]
    [DataRow(@"C:\images-other\a.jpg", true, false)]
    [DataRow(@"C:\a.jpg", true, false)]
    public void ScopeContainsFilesOfItsFolderAndOptionallyItsSubfolders(string path, bool recursive, bool expected)
    {
        Assert.AreEqual(expected, new FolderScope(@"C:\images", recursive).Contains(path));
    }

    [TestMethod]
    public void RecursiveDriveRootContainsEverythingOnTheDrive()
    {
        Assert.IsTrue(new FolderScope(@"C:\", Recursive: true).Contains(@"C:\images\a.jpg"));
        Assert.IsFalse(new FolderScope(@"C:\", Recursive: true).Contains(@"D:\a.jpg"));
    }

    [TestMethod]
    public void ScopesDifferingOnlyInCasingOrTrailingSeparatorAreEqual()
    {
        var scope = new FolderScope(@"C:\images", Recursive: false);
        Assert.AreEqual(scope, new FolderScope(@"C:\IMAGES\", Recursive: false));
        Assert.AreEqual(scope.GetHashCode(), new FolderScope(@"c:\images\", Recursive: false).GetHashCode());
        Assert.AreNotEqual(scope, new FolderScope(@"C:\images", Recursive: true));
        Assert.AreEqual(@"C:\", new FolderScope(@"C:\", Recursive: false).DirectoryPath);
    }
}
