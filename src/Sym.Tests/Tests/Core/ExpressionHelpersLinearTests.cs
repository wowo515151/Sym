using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using System.Collections.Immutable;

namespace SymTest
{
    [TestClass]
    public sealed class ExpressionHelpersLinearTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void TryExtractLinearStruct_SimpleLinear_ReturnsCorrectValues()
        {
            var x = new Symbol("x");
            var expr = new Add(new Multiply(new Number(2m), x), new Number(3m)).Canonicalize();
            
            var coeffs = new decimal[1];
            decimal constant = 0m;
            bool success = ExpressionHelpers.TryExtractLinearStruct(expr, new[] { x }, ref coeffs, ref constant);
            
            Assert.IsTrue(success);
            Assert.AreEqual(2m, coeffs[0]);
            Assert.AreEqual(3m, constant);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TryExtractLinearStruct_NonLinear_ReturnsFalse()
        {
            var x = new Symbol("x");
            var expr = new Power(x, new Number(2m)).Canonicalize();
            
            var coeffs = new decimal[1];
            decimal constant = 0m;
            bool success = ExpressionHelpers.TryExtractLinearStruct(expr, new[] { x }, ref coeffs, ref constant);
            
            Assert.IsFalse(success);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TryExtractLinearStruct_MultipleVariables_Works()
        {
            var x = new Symbol("x");
            var y = new Symbol("y");
            // 2x + 3y + 5
            var expr = new Add(new Multiply(new Number(2m), x), new Multiply(new Number(3m), y), new Number(5m)).Canonicalize();
            
            var coeffs = new decimal[2];
            decimal constant = 0m;
            bool success = ExpressionHelpers.TryExtractLinearStruct(expr, new[] { x, y }, ref coeffs, ref constant);
            
            Assert.IsTrue(success);
            Assert.AreEqual(2m, coeffs[0]);
            Assert.AreEqual(3m, coeffs[1]);
            Assert.AreEqual(5m, constant);
        }
    }
}
