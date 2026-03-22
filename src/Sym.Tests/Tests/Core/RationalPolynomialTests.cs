// Copyright Warren Harding 2026
using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace Sym.Tests.Core
{
    [TestClass]
    public class RationalPolynomialTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Rational_Basic_Arithmetic()
        {
            var r1 = new Rational(1, 2);
            var r2 = new Rational(1, 3);
            
            var sum = r1 + r2; // 5/6
            Assert.AreEqual(new BigInteger(5), sum.Numerator);
            Assert.AreEqual(new BigInteger(6), sum.Denominator);
            
            var prod = r1 * r2; // 1/6
            Assert.AreEqual(new BigInteger(1), prod.Numerator);
            Assert.AreEqual(new BigInteger(6), prod.Denominator);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Rational_FromDecimal()
        {
            var r = Rational.FromDecimal(0.75m);
            Assert.AreEqual(new BigInteger(3), r.Numerator);
            Assert.AreEqual(new BigInteger(4), r.Denominator);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Polynomial_CreateAndEvaluate()
        {
            var x = new Symbol("x");
            // x^2 - 1
            bool success = Polynomial.TryCreate(new Add(new Power(x, new Number(2)), new Number(-1)), x, out var poly);
            
            Assert.IsTrue(success);
            Assert.AreEqual(2, poly.Degree);
            
            // Evaluate at x = 2: 2^2 - 1 = 3
            var val = poly.Evaluate(Rational.FromInteger(2));
            Assert.AreEqual(new BigInteger(3), val.Numerator);
            Assert.AreEqual(BigInteger.One, val.Denominator);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Polynomial_Derivative()
        {
            var x = new Symbol("x");
            // x^3
            Polynomial.TryCreate(new Power(x, new Number(3)), x, out var poly);
            var deriv = poly.Derivative();
            
            // Expect 3x^2
            Assert.AreEqual(2, deriv.Degree);
            Assert.AreEqual(new BigInteger(3), deriv.LeadingCoefficient.Numerator);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Polynomial_FactorLinear()
        {
            var x = new Symbol("x");
            // x^2 - 3x + 2 = (x-1)(x-2)
            var expr = new Add(new Power(x, new Number(2)), new Multiply(new Number(-3), x), new Number(2));
            Polynomial.TryCreate(expr, x, out var poly);
            
            var result = poly.FactorLinear();
            Assert.AreEqual(2, result.LinearRoots.Count);
            
            var roots = result.LinearRoots;
            // Roots are 1 and 2
            Assert.IsTrue(roots.Any(r => r.Equals(Rational.One)));
            Assert.IsTrue(roots.Any(r => r.Equals(Rational.FromInteger(2))));
        }
    }
}
