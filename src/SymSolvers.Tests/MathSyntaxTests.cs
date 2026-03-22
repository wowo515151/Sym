using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Sym.CSharpIO;
using Sym.Operations;
using Sym.Atoms;

namespace SymSolvers.Tests;

[TestClass]
public class MathSyntaxTests
{
    [TestMethod]
        [Timeout(10000)]
    public void ParsesLatexFractionAndTrig()
    {
        var fromLatex = CSharpIO.ParseLatexExpressions(@"\frac{x^2}{y} + \sin x")[0];
        var expected = CSharpIO.ParseExpressionsStrict("x^2 / y + sin(x)")[0];
        Assert.IsTrue(fromLatex.InternalEquals(expected), $"Got {CSharpIO.FormatExpr(fromLatex)}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void StrictParseRejectsPartial()
    {
        Assert.ThrowsException<InvalidOperationException>(() => CSharpIO.ParseExpressionsStrict("x+;"));
    }
}
