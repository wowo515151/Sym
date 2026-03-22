namespace SymCore.Coverage.Tests;

[TestClass]
public sealed class Test1
{
    [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
    public void TestMethod1()
    {
    }
}
