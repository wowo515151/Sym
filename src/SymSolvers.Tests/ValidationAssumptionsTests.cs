// Copyright Warren Harding 2026
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;
using SymCore;

namespace SymSolvers.Tests;

[TestClass]
public sealed class ValidationAssumptionsTests
{
    [TestMethod]
        [Timeout(10000)]
    public void RespectsPositiveAssumptionInValidation()
    {
        var x = new Symbol("x");
        var expr = new Equality(new Function("exp", ImmutableList.Create<IExpression>(new Function("log", ImmutableList.Create<IExpression>(x)))), x);
        var assumptions = new Assumptions(positive: new[] { "x" });

        var result = ExpressionPropertyValidator.ValidateEquality(expr, x, samples: null, tolerance: 1e-6m, assumptions: assumptions);

        Assert.IsTrue(result.Success, $"Validation failed: {result.Message}");
    }
}
