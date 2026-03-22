//Copyright Warren Harding 2025.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ConsoleHelmsman;

namespace ConsoleHelmsman.Tests
{
    [TestClass]
    public class ConsoleHelmsmanTests
    {
        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void Program_Main_ReturnsZero()
        {
            int result = Program.MainForTests(new string[0]);
            Assert.AreEqual(0, result);
        }
    }
}
