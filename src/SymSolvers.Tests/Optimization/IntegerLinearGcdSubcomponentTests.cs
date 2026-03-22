// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;

namespace SymSolvers.Tests.Optimization;

[TestClass]
public sealed class IntegerLinearGcdSubcomponentTests
{
    [TestMethod]
    [Timeout(5000)]
    public void Objective_Canonicalize_Preserves_Two_Multiply_Terms()
    {
        var m = new Symbol("m");
        var n = new Symbol("n");

        var objective = new Add(
            new Multiply(new Number(2002m), m),
            new Multiply(new Number(44444m), n)
        ).Canonicalize();

        Assert.IsInstanceOfType<Add>(objective);
        var add = (Add)objective;
        Assert.AreEqual(2, add.Arguments.Count, objective.ToDisplayString());

        Assert.IsInstanceOfType<Multiply>(add.Arguments[0]);
        Assert.IsInstanceOfType<Multiply>(add.Arguments[1]);
    }

    [TestMethod]
    [Timeout(5000)]
    public void Constraint_Encoding_Is_Evaluatable_When_Assignment_Satisfies_Guard()
    {
        var m = new Symbol("m");
        var n = new Symbol("n");

        var objective = new Add(
            new Multiply(new Number(2002m), m),
            new Multiply(new Number(44444m), n)
        ).Canonicalize();

        var gt = new Function("gt", objective, new Number(0m)).Canonicalize();

        // m=0,n=1 => objective=44444 > 0
        var assignments = new Dictionary<string, decimal>
        {
            ["m"] = 0m,
            ["n"] = 1m
        };

        Assert.IsTrue(NumericEvaluator.TryEvaluate(objective, assignments, out var obj, out var err), err);
        Assert.IsTrue(obj > 0m, $"Expected objective > 0 but got {obj}; guard={gt.ToDisplayString()}");
    }

    [TestMethod]
    [Timeout(5000)]
    public void Objective_With_Flat_Add_Terms_Evaluates_As_Expected()
    {
        var m = new Symbol("m");
        var n = new Symbol("n");

        var objective = new Add(
            new Multiply(new Number(2002m), m),
            new Multiply(new Number(44444m), n)
        ).Canonicalize();

        var assignments = new Dictionary<string, decimal>
        {
            ["m"] = 1m,
            ["n"] = 1m
        };

        Assert.IsTrue(NumericEvaluator.TryEvaluate(objective, assignments, out var res, out var err), err);
        Assert.AreEqual(46446m, res);
    }

    [TestMethod]
    [Timeout(5000)]
    public void Minimize_VectorPayload_Shape_Is_Objective_Then_Constraints()
    {
        var m = new Symbol("m");
        var n = new Symbol("n");

        var objective = new Add(
            new Multiply(new Number(2002m), m),
            new Multiply(new Number(44444m), n)
        ).Canonicalize();

        var c1 = new Function("gt", objective, new Number(0m)).Canonicalize();
        var c2 = new Function("integer", m).Canonicalize();
        var c3 = new Function("integer", n).Canonicalize();

        var payloadExpr = new Vector(ImmutableList.Create<IExpression>(objective, c1, c2, c3)).Canonicalize();
        Assert.IsInstanceOfType<Vector>(payloadExpr);
        var payload = (Vector)payloadExpr;

        Assert.AreEqual(4, payload.Arguments.Count);

        Assert.IsTrue(payload.Arguments[0].InternalEquals(objective), payload.ToDisplayString());
        Assert.IsTrue(payload.Arguments[1].InternalEquals(c1), payload.ToDisplayString());
        Assert.IsTrue(payload.Arguments[2].InternalEquals(c2), payload.ToDisplayString());
        Assert.IsTrue(payload.Arguments[3].InternalEquals(c3), payload.ToDisplayString());

        var problemExpr = new Function("Minimize", payload).Canonicalize();
        Assert.IsInstanceOfType<Function>(problemExpr);
        var fn = (Function)problemExpr;
        Assert.AreEqual("Minimize", fn.Name);
        Assert.AreEqual(1, fn.Arguments.Count);
        Assert.IsInstanceOfType<Vector>(fn.Arguments[0]);
    }
}
