using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using System.Numerics;

namespace SymCore.Tests;

[TestClass]
public class RationalTests
{
    [TestMethod]
        [Timeout(10000)]
    public void Constructor_SimplifiesFraction()
    {
        var r = new Rational(2, 4);
        Assert.AreEqual(new BigInteger(1), r.Numerator);
        Assert.AreEqual(new BigInteger(2), r.Denominator);
    }

    [TestMethod]
        [Timeout(10000)]
    public void Constructor_HandlesNegativeDenominator()
    {
        var r = new Rational(1, -2);
        Assert.AreEqual(new BigInteger(-1), r.Numerator);
        Assert.AreEqual(new BigInteger(2), r.Denominator);
    }

    [TestMethod]
        [Timeout(10000)]
    public void Add_RationalNumbers()
    {
        var r1 = new Rational(1, 2);
        var r2 = new Rational(1, 3);
        var result = r1 + r2;
        Assert.AreEqual(new BigInteger(5), result.Numerator);
        Assert.AreEqual(new BigInteger(6), result.Denominator);
    }

    [TestMethod]
        [Timeout(10000)]
    public void Multiply_RationalNumbers()
    {
        var r1 = new Rational(2, 3);
        var r2 = new Rational(3, 4);
        var result = r1 * r2;
        Assert.AreEqual(new BigInteger(1), result.Numerator);
        Assert.AreEqual(new BigInteger(2), result.Denominator);
    }

    [TestMethod]
        [Timeout(10000)]
    public void FromDecimal_ExactConversion()
    {
        var r = Rational.FromDecimal(0.75m);
        Assert.AreEqual(new BigInteger(3), r.Numerator);
        Assert.AreEqual(new BigInteger(4), r.Denominator);
    }

    [TestMethod]
        [Timeout(10000)]
    public void ToDecimal_Conversion()
    {
        var r = new Rational(1, 8);
        Assert.AreEqual(0.125m, r.ToDecimal());
    }

    [TestMethod]
        [Timeout(10000)]
    public void TrySqrt_PerfectSquare()
    {
        var r = new Rational(4, 9);
        bool success = r.TrySqrt(out var sqrt);
        Assert.IsTrue(success);
        Assert.AreEqual(new BigInteger(2), sqrt.Numerator);
        Assert.AreEqual(new BigInteger(3), sqrt.Denominator);
    }

    [TestMethod]
        [Timeout(10000)]
    public void TrySqrt_NonPerfectSquare()
    {
        var r = new Rational(2, 3);
        bool success = r.TrySqrt(out var _);
        Assert.IsFalse(success);
    }
}
