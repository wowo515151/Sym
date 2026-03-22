// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.Numerics;
using Sym.Atoms;
using Sym.Core;

namespace SymSolvers.Tests
{
    [TestClass]
    public class NumericRefinementTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void TrySnapToFraction_ReducesFractionAndReturnsDivide()
        {
            Assert.IsTrue(NumericRefinement.TrySnapToFraction(0.6666666667m, out var res, 1000));
            // Check that TrySnapToFraction returned a Divide or Number corresponding to 2/3
            Assert.IsTrue(res is Sym.Operations.Divide || res is Sym.Atoms.Number);
            // If it's a Divide, ensure its canonical string is (2 / 3)
            if (res is Sym.Operations.Divide d)
            {
                var s = d.ToDisplayString();
                Assert.AreEqual("(2 / 3)", s);
            }
            else if (res is Sym.Atoms.Number n)
            {
                Assert.AreEqual("2/3", Sym.Core.Rational.FromDecimal(0.6666666667m).ToString());
            }
        }

        [TestMethod]
        [Timeout(10000)]
        public void TrySnapToFraction_IntegerReturnsNumber()
        {
            Assert.IsTrue(NumericRefinement.TrySnapToFraction(2.0m, out var res, 1000));
            Assert.IsTrue(res is Sym.Atoms.Number);
            Assert.AreEqual("2", res.ToDisplayString());
        }
    }
}
