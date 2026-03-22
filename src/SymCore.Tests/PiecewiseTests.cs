// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using System.Collections.Immutable;

namespace SymCore.Tests;

[TestClass]
public class PiecewiseTests
{
    [TestMethod]
        [Timeout(10000)]
    public void Canonicalize_TrueGuard_ReturnsValue()
    {
        var val = new Number(1);
        var guard = new Symbol("true");
        var pw = new Piecewise(val, guard);
        
        var result = pw.Canonicalize();
        Assert.AreEqual(val, result);
    }

    [TestMethod]
        [Timeout(10000)]
    public void Canonicalize_FalseGuard_SkipsBranch()
    {
        var val1 = new Number(1);
        var guard1 = new Symbol("false");
        var val2 = new Number(2);
        var guard2 = new Symbol("true");
        
        var pw = new Piecewise(val1, guard1, val2, guard2);
        var result = pw.Canonicalize();
        
        Assert.AreEqual(val2, result);
    }

    [TestMethod]
        [Timeout(10000)]
    public void Canonicalize_NestedPiecewise_Flattens()
    {
        // Piecewise(Piecewise(1, true), true) -> 1
        var inner = new Piecewise(new Number(1), new Symbol("true"));
        var outer = new Piecewise(inner, new Symbol("true"));
        
        var result = outer.Canonicalize();
        Assert.AreEqual("1", result.ToDisplayString());
    }

    [TestMethod]
        [Timeout(10000)]
    public void Canonicalize_NestedPiecewise_DistributesGuard()
    {
        // Piecewise(Piecewise(1, c1, 2, c2), guard) -> Piecewise(1, and(guard, c1), 2, and(guard, c2))
        var c1 = new Symbol("c1");
        var c2 = new Symbol("c2");
        var guard = new Symbol("g");
        
        var inner = new Piecewise(new Number(1), c1, new Number(2), c2);
        var outer = new Piecewise(inner, guard);
        
        var result = outer.Canonicalize();
        Assert.IsInstanceOfType(result, typeof(Piecewise));
        var pw = (Piecewise)result;
        
        // Expected 4 arguments: 1, and(g, c1), 2, and(g, c2)
        Assert.AreEqual(4, pw.Arguments.Count);
        Assert.AreEqual("1", pw.Arguments[0].ToDisplayString());
        // and(g, c1) might be represented as Function("and", ...)
        Assert.IsTrue(pw.Arguments[1].ToDisplayString().Contains("and"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void Canonicalize_NestedPiecewiseInDefault_Flattens()
    {
        // Piecewise(1, c1, Piecewise(2, c2, 3), true) 
        // -> Piecewise(1, c1, 2, and(true, c2), 3) 
        // -> Piecewise(1, c1, 2, c2, 3) (if and(true, c2) -> c2)
        
        var c1 = new Symbol("c1");
        var c2 = new Symbol("c2");
        var inner = new Piecewise(new Number(2), c2, new Number(3));
        var outer = new Piecewise(new Number(1), c1, inner); // Default branch is inner
        
        var result = outer.Canonicalize();
        Assert.IsInstanceOfType(result, typeof(Piecewise));
        var pw = (Piecewise)result;
        
        // Expected arguments: 1, c1, 2, c2, 3
        Assert.AreEqual(5, pw.Arguments.Count);
        Assert.AreEqual("1", pw.Arguments[0].ToDisplayString());
        Assert.AreEqual("c1", pw.Arguments[1].ToDisplayString());
        Assert.AreEqual("2", pw.Arguments[2].ToDisplayString());
        Assert.AreEqual("c2", pw.Arguments[3].ToDisplayString());
        Assert.AreEqual("3", pw.Arguments[4].ToDisplayString());
    }

    [TestMethod]
        [Timeout(10000)]
    public void Canonicalize_PiecewiseWithContradictionGuard_Simplifies()
    {
        // Piecewise(1, and(c1, not(c1)), 2, true) -> 2
        var c1 = new Symbol("c1");
        var guard1 = new Function("and", c1, new Function("not", c1));
        var pw = new Piecewise(new Number(1), guard1, new Number(2), new Symbol("true"));
        
        var result = pw.Canonicalize();
        Assert.AreEqual("2", result.ToDisplayString());
    }

    [TestMethod]
        [Timeout(10000)]
    public void Canonicalize_PiecewiseWithTautologyGuard_Simplifies()
    {
        // Piecewise(1, or(c1, true), 2, c2) -> 1
        var c1 = new Symbol("c1");
        var guard1 = new Function("or", c1, new Symbol("true"));
        var pw = new Piecewise(new Number(1), guard1, new Number(2), new Symbol("c2"));
        
        var result = pw.Canonicalize();
        Assert.AreEqual("1", result.ToDisplayString());
    }

    [TestMethod]
        [Timeout(10000)]
    public void Canonicalize_PiecewiseWithRedundantBranches_Simplifies()
    {
        // Piecewise(1, false, 2, false, 3, true) -> 3
        var pw = new Piecewise(new Number(1), new Symbol("false"), new Number(2), new Symbol("false"), new Number(3), new Symbol("true"));
        var result = pw.Canonicalize();
        Assert.AreEqual("3", result.ToDisplayString());
    }
}
