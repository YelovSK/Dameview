using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Dameview.Win32;

namespace Dameview.Tests.Platform;

[TestClass]
public sealed class ClipboardBitmapTests
{
    [TestMethod]
    public void StoresTheTopRowLastAsBitmapsExpect()
    {
        using var bitmap = ClipboardBitmap.Allocate(2, 3);

        ref byte top = ref MemoryMarshal.GetReference(bitmap.GetRow(0));
        ref byte middle = ref MemoryMarshal.GetReference(bitmap.GetRow(1));
        ref byte bottom = ref MemoryMarshal.GetReference(bitmap.GetRow(2));

        Assert.AreEqual(8, Unsafe.ByteOffset(ref bottom, ref middle));
        Assert.AreEqual(8, Unsafe.ByteOffset(ref middle, ref top));
    }
}
