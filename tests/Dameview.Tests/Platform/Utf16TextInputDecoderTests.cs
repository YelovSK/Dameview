using Dameview.Win32.Input;

namespace Dameview.Tests.Platform;

[TestClass]
public sealed class Utf16TextInputDecoderTests
{
    [TestMethod]
    public void BasicCharactersAreEmittedImmediatelyAndControlsAreIgnored()
    {
        var decoder = new Utf16TextInputDecoder();

        Assert.AreEqual("a", decoder.Push('a'));
        Assert.IsNull(decoder.Push('\b'));
        Assert.AreEqual("ž", decoder.Push('ž'));
    }

    [TestMethod]
    public void SurrogatePairsAreEmittedAsOneTextInput()
    {
        var decoder = new Utf16TextInputDecoder();
        string emoji = "😀";

        Assert.IsNull(decoder.Push(emoji[0]));
        Assert.AreEqual(emoji, decoder.Push(emoji[1]));
    }
}
