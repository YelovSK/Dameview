using Dameview.Win32;

namespace Dameview.Tests.Platform;

[TestClass]
public sealed class ShellPropertiesTests
{
    [TestMethod]
    public void PropertiesComeBackFormattedAndMissingOnesAsNull()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "properties");
        try
        {
            using var queue = new ComWorkerQueue<object?>("test", 1, () => null);
            string?[] values = queue.Enqueue((_, _) => ShellProperties.Read(
                path,
                ["System.DateModified", "System.Photo.FNumber"])).GetAwaiter().GetResult();

            Assert.IsFalse(string.IsNullOrEmpty(values[0]), "Every file has a modified date.");
            Assert.IsNull(values[1], "A text file has no aperture.");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
