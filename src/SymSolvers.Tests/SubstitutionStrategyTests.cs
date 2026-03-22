// Copyright Warren Harding 2026
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public class SubstitutionStrategyTests
{
    [TestMethod]
        [Timeout(10000)]
    public void NullContext_ReturnsFailure()
    {
        var result = new SubstitutionStrategy().Solve(new Symbol("x"), null!);

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Message, "SolveContext cannot be null");
    }

    [TestMethod]
        [Timeout(10000)]
    public void NoSubstitutionsProvided_IsNoopSuccess()
    {
        var expr = new Add(new Symbol("x"), new Number(2m));
        var result = new SubstitutionStrategy().Solve(expr, new SolveContext());

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.ResultExpression!.InternalEquals(expr));
        StringAssert.Contains(result.Message, "No substitutions provided");
    }

    [TestMethod]
        [Timeout(10000)]
    public void AppliesSubstitutionsFromContext()
    {
        var expr = new Add(new Symbol("x"), new Multiply(new Symbol("y"), new Number(2m)));
        var substitutions = ImmutableDictionary.CreateRange(new Dictionary<string, IExpression>
        {
            { "x", new Number(5m) },
            { "y", new Number(3m) }
        });
        var additional = ImmutableDictionary<string, object>.Empty.Add(SolverOptionKeys.Substitutions, substitutions);
        var context = new SolveContext(additionalData: additional);

        var result = new SubstitutionStrategy().Solve(expr, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var expected = new Add(new Number(5m), new Multiply(new Number(3m), new Number(2m))).Canonicalize();
        Assert.IsTrue(result.ResultExpression!.InternalEquals(expected));
        StringAssert.Contains(result.Message, "Substitution applied");
    }

    [TestMethod]
        [Timeout(10000)]
    public void TargetSymbol_IsPreservedDuringSubstitution()
    {
        var expr = new Equality(new Symbol("x"), new Number(2m));
        var substitutions = ImmutableDictionary.CreateRange(new Dictionary<string, IExpression>
        {
            { "x", new Number(2m) }
        });
        var additional = ImmutableDictionary<string, object>.Empty.Add(SolverOptionKeys.Substitutions, substitutions);
        var context = new SolveContext(targetVariable: new Symbol("x"), additionalData: additional);

        var result = new SubstitutionStrategy().Solve(expr, context);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.ResultExpression!.InternalEquals(expr));
    }
}
