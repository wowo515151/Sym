using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using Sym.Atoms;
using Sym.CSharpIO;
using SymSolvers.Numerics;
using SymSolvers.Validation;

namespace SymSolvers.Tests.Validation;

[TestClass]
public sealed class CounterexampleMinimizerTests
{
    [TestMethod]
        [Timeout(10000)]
    public void Minimizer_Shrinks_And_KeepsFailure()
    {
        var left = CSharpIO.ParseExpressions("x^2")[0];
        var right = CSharpIO.ParseExpressions("x")[0];
        var model = new Float64Model();

        var sample = new Dictionary<string, double> { { "x", 1.23456 } };

        bool StillFails(IReadOnlyDictionary<string, double> assignments)
        {
            var okL = PrecisionExpressionEvaluator.TryEvaluate(left, assignments, model, out var lv, out _);
            var okR = PrecisionExpressionEvaluator.TryEvaluate(right, assignments, model, out var rv, out _);
            return okL && okR && Math.Abs(lv - rv) > 1e-6;
        }

        Assert.IsTrue(StillFails(sample), "Baseline sample should fail equality.");

        var minimized = CounterexampleMinimizer.Minimize(new Dictionary<string, double>(sample), StillFails);

        Assert.IsTrue(StillFails(minimized), "Minimized sample must preserve failure.");
        Assert.AreEqual(0.5d, minimized["x"], 1e-9, "Value should snap toward a simpler rational that still fails.");
    }

    [TestMethod]
        [Timeout(10000)]
    public void CounterexampleFinder_FindsBoundaryMismatch()
    {
        var finder = new CounterexampleFinder(new Float64Model(), tolerance: 1e-6);
        var left = CSharpIO.ParseExpressions("log(1 - x)")[0];
        var right = CSharpIO.ParseExpressions("log1p(-x) + 0.05")[0]; // intentional bias
        var symbols = new List<Symbol> { new("x") };

        var ce = finder.Find(left, right, symbols, maxSamples: 64);
        Assert.IsNotNull(ce, "Should detect mismatch on biased expression.");
    }
}
