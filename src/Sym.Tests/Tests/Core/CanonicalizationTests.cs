// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using System.Collections.Immutable;

namespace SymTest
{
    [TestClass]
    public class CanonicalizationTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Multiply_Canonicalize_Distributes()
        {
            var a = new Symbol("a");
            var b = new Symbol("b");
            // (a - b) * (a + b)
            var expr = new Multiply(
                new Subtract(a, b),
                new Add(a, b)
            );
            
            var canonical = expr.Canonicalize();
            
            // Expected: a^2 - b^2 (which is Add(Power(a, 2), Multiply(-1, Power(b, 2))))
            var expected = new Subtract(
                new Power(a, new Number(2)),
                new Power(b, new Number(2))
            ).Canonicalize();
            
            Assert.AreEqual(expected.ToDisplayString(), canonical.ToDisplayString());
            Assert.IsTrue(expected.InternalEquals(canonical));
        }
    }
}
