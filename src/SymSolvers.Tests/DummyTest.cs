// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SymSolvers.Tests;

[TestClass]
public class DummyTest
{
    [TestMethod]
        [Timeout(10000)]
    public void TestTrue()
    {
        Assert.IsTrue(true);
    }
}
