// Copyright Warren Harding 2026
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public sealed class ModularReproTests
{
    [TestMethod]
        [Timeout(10000)]
    public void TestProblem46_CRT_WithBound()
    {
        // Constraints: x < 2010; mod(x, 7) == 5; mod(x, 11) == 10; mod(x, 13) == 10; x;
        var x = new Symbol("x");
        var c1 = new Equality(new Function("mod", x, new Number(7)), new Number(5));
        var c2 = new Equality(new Function("mod", x, new Number(11)), new Number(10));
        var c3 = new Equality(new Function("mod", x, new Number(13)), new Number(10));
        var bound = new Function("lt", x, new Number(2010));
        
        var system = new Vector(ImmutableList.Create<IExpression>(c1, c2, c3, bound));
        
        var strategy = new NumberTheoryStrategy();
        var context = new SolveContext();
        var result = strategy.Solve(system, context);
        
        Assert.IsTrue(result.IsSuccess);
        // CRT of (5,7), (10,11), (10,13)
        // (10,11) and (10,13) => x % 143 == 10
        // x = 143k + 10
        // 143k + 10 % 7 == 5
        // 3k + 3 % 7 == 5
        // 3k % 7 == 2
        // k = 3 works (3*3 = 9 % 7 = 2)
        // x = 143*3 + 10 = 429 + 10 = 439
        // So x % 1001 == 439
        // x < 2010 => 1001*k + 439 < 2010
        // k=1 => 1001 + 439 = 1440
        // k=2 => 2002 + 439 = 2441 (too large)
        // So x = 1440
        
        Assert.IsNotNull(result.ResultExpression);
        Assert.IsTrue(result.ResultExpression.ToDisplayString().Contains("1440"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void TestLinearModExtraction()
    {
        // mod(2*k + 6, 9) == 0
        var k = new Symbol("k");
        var c1 = new Equality(new Function("mod", new Add(new Multiply(new Number(2), k), new Number(6)), new Number(9)), new Number(0));
        
        var strategy = new NumberTheoryStrategy();
        var context = new SolveContext();
        var result = strategy.Solve(c1, context);
        
        // 2k + 6 = 0 (mod 9) => 2k = -6 = 3 (mod 9)
        // inv(2, 9) = 5
        // k = 3 * 5 = 15 = 6 (mod 9)
        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.ResultExpression);
        System.Console.WriteLine($"DEBUG: Result={result.ResultExpression.ToDisplayString()}");
        Assert.IsTrue(result.ResultExpression.ToDisplayString().Contains("mod(k, 9) = 6"));
    }
}
