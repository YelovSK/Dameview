using Dameview.Installation;

namespace Dameview.Tests.Installation;

[TestClass]
public sealed class AppInstallationTests
{
    [TestMethod]
    public void ReplacingExecutablePreservesThePreviousVersionUnderAUniqueName()
    {
        string directory = CreateTestDirectory();
        try
        {
            string installedPath = Path.Combine(directory, "Dameview.exe");
            string stagedPath = Path.Combine(directory, "Dameview.new.exe");
            string previousBackup = Path.Combine(directory, "Dameview.old.previous.exe");
            File.WriteAllText(installedPath, "old");
            File.WriteAllText(stagedPath, "new");
            File.WriteAllText(previousBackup, "older");

            AppInstallation.ReplaceExecutable(stagedPath, installedPath);

            Assert.AreEqual("new", File.ReadAllText(installedPath));
            string[] backups = Directory.GetFiles(directory, "Dameview.old.*.exe");
            Assert.HasCount(2, backups);
            Assert.AreEqual("older", File.ReadAllText(previousBackup));
            Assert.AreEqual("old", File.ReadAllText(backups.Single(path => path != previousBackup)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void FailedPlacementRestoresThePreviousExecutable()
    {
        string directory = CreateTestDirectory();
        try
        {
            string installedPath = Path.Combine(directory, "Dameview.exe");
            File.WriteAllText(installedPath, "old");

            Assert.ThrowsExactly<FileNotFoundException>(() =>
                AppInstallation.ReplaceExecutable(Path.Combine(directory, "missing.exe"), installedPath));

            Assert.AreEqual("old", File.ReadAllText(installedPath));
            Assert.HasCount(0, Directory.GetFiles(directory, "Dameview.old.*.exe"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"Dameview.Installation.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
