using Dameview.Navigation;

namespace Dameview.Tests.Navigation;

[TestClass]
public sealed class NaturalOrderTests
{
    [TestMethod]
    [DataRow("11", "109")]
    [DataRow("img2.jpg", "img10.jpg")]
    [DataRow("a9b", "a10a")]
    [DataRow("file 1", "file 1a")]
    [DataRow("apple", "Banana")]
    [DataRow("1", "01")]
    [DataRow("01a", "1b")]
    [DataRow("A", "a")]
    [DataRow("99999999999999999999", "100000000000000000000")]
    public void OrdersLeftBeforeRight(string left, string right)
    {
        Assert.IsLessThan(0, NaturalOrder.Compare(left, right));
        Assert.IsGreaterThan(0, NaturalOrder.Compare(right, left));
    }

    [TestMethod]
    public void IdenticalTextIsEqual()
    {
        Assert.AreEqual(0, NaturalOrder.Compare("img010.jpg", "img010.jpg"));
    }

    [TestMethod]
    public void SortsNumberedNames()
    {
        string[] names = ["110", "11", "109", "2", "a", "1"];
        Array.Sort(names, (left, right) => NaturalOrder.Compare(left, right));

        CollectionAssert.AreEqual(new[] { "1", "2", "11", "109", "110", "a" }, names);
    }
}
