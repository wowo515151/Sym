using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymCore;
using System;

namespace SymCore.Tests;

[TestClass]
public class NumericConvertTests
{
    [TestMethod]
        [Timeout(10000)]
    public void SafeToDecimal_ValidDouble_ReturnsDecimal()
    {
        double d = 123.45;
        decimal result = NumericConvert.SafeToDecimal(d);
        Assert.AreEqual(123.45m, result);
    }

    [TestMethod]
        [Timeout(10000)]
    [ExpectedException(typeof(InvalidOperationException))]
    public void SafeToDecimal_NaNDouble_ThrowsInvalidOperationException()
    {
        NumericConvert.SafeToDecimal(double.NaN);
    }

    [TestMethod]
        [Timeout(10000)]
    [ExpectedException(typeof(OverflowException))]
    public void SafeToDecimal_PositiveInfinity_ThrowsOverflowException()
    {
        NumericConvert.SafeToDecimal(double.PositiveInfinity);
    }

    [TestMethod]
        [Timeout(10000)]
    [ExpectedException(typeof(OverflowException))]
    public void SafeToDecimal_NegativeInfinity_ThrowsOverflowException()
    {
        NumericConvert.SafeToDecimal(double.NegativeInfinity);
    }

    [TestMethod]
        [Timeout(10000)]
    [ExpectedException(typeof(OverflowException))]
    public void SafeToDecimal_TooLarge_ThrowsOverflowException()
    {
        NumericConvert.SafeToDecimal((double)decimal.MaxValue + 1e30);
    }

    [TestMethod]
        [Timeout(10000)]
    [ExpectedException(typeof(OverflowException))]
    public void SafeToDecimal_TooSmall_ThrowsOverflowException()
    {
        NumericConvert.SafeToDecimal((double)decimal.MinValue - 1e30);
    }
}
