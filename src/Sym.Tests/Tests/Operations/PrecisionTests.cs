// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Operations;
using Sym.Core;
using System;

namespace Sym.Tests.Tests.Operations;

[TestClass]
public class PrecisionTests
{
    [TestMethod]
        [Timeout(10000)]
    public void TestPowerPrecision()
    {
        // (2 * sqrt(t-2))^2 should be 4 * (t-2) => 4*t - 8
        var t = new Symbol("t");
        var inner = new Function("sqrt", new Add(t, new Number(-2m)));
        var mul = new Multiply(new Number(2m), inner);
        var p = new Power(mul, new Number(2m));
        
        var result = p.Canonicalize();
        Console.WriteLine($"Result: {result.ToDisplayString()}");
        
        // Check if result is 4 * (t-2) => 4 * t - 8
        // Result is Add(-8, 4*t) due to distribution
        Assert.IsInstanceOfType(result, typeof(Add));
        var add = (Add)result;
        Assert.IsTrue(add.Arguments.Any(a => a is Number num && num.Value == -8m));
        Assert.IsTrue(add.Arguments.Any(a => a is Multiply m && m.Arguments.Any(ma => ma is Number n && n.Value == 4m) && m.Arguments.Contains(t)));
    }
}
