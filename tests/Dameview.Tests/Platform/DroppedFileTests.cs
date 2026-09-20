using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Dameview.Win32;

namespace Dameview.Tests.Platform;

[TestClass]
public sealed class DroppedFileTests
{
    [TestMethod]
    public void ReadsAllDroppedPathsInOrderIncludingUnicodeAndLongPaths()
    {
        string[] paths =
        [
            @"C:\pictures\first.png",
            @"C:\pictures\žltý obrázok 日本語.jpg",
            @"C:\pictures\" + new string('a', 280) + ".png",
        ];

        AssertDropPaths(paths);
    }

    [TestMethod]
    public void ReadsEmptyDrop() => AssertDropPaths([]);

    private static void AssertDropPaths(string[] paths)
    {
        // DROPFILES has a 20-byte header followed by a double-null-terminated file list.
        byte[] names = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
        byte[] payload = new byte[20 + names.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 20); // pFiles
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4), 1); // fWide
        names.CopyTo(payload, 20);

        nint drop = GlobalAlloc(0, (nuint)payload.Length);
        Assert.AreNotEqual(nint.Zero, drop);
        try
        {
            Marshal.Copy(payload, 0, drop, payload.Length);
            CollectionAssert.AreEqual(paths, NativeMethods.GetDroppedFilePaths(drop));
        }
        finally
        {
            _ = GlobalFree(drop);
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GlobalFree(nint memory);
}
