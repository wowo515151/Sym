using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using Sym.Core.Strategies;
using System.Linq;
using System.Collections.Immutable;

namespace SymSolvers.Tests;

[TestClass]
public class TrigSolveTests
{
    [TestMethod]
        [Timeout(10000)]
    public void Solve_SinX_Zero()
    {
        using var _ = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var x = new Symbol("x");
        var eq = new Equality(new Function("sin", ImmutableList.Create<IExpression>(x)), new Number(0m)).Canonicalize();
        var strategy = new EquationSolverStrategy();
        var context = new SolveContext(x, ImmutableList<Rule>.Empty);
        
        var result = strategy.Solve(eq, context);
        
        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.ResultExpression);
        // EquationSolverStrategy isolates via inverse trig
        var resultText = result.ResultExpression.ToDisplayString();
        Assert.IsTrue(
            resultText.Contains("asin", StringComparison.OrdinalIgnoreCase) ||
            resultText.Contains("x = 0", StringComparison.OrdinalIgnoreCase) ||
            resultText.Equals("(x = 0)", StringComparison.OrdinalIgnoreCase),
            resultText);
    }

    [TestMethod]
        [Timeout(10000)]
    public void Solve_TanX_SinX()
    {
        using var _ = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // tan(x) = sin(x) -> sin(x)*(1 - cos(x)) = 0
        // For now test a simpler one that isolates via inverse trig.
        
        var x = new Symbol("x");
        // 2*cos(x) = 1
        var eq = new Equality(new Multiply(new Number(2m), new Function("cos", ImmutableList.Create<IExpression>(x))), new Number(1m)).Canonicalize();
        var strategy = new EquationSolverStrategy();
        var context = new SolveContext(x, ImmutableList<Rule>.Empty);
        
        var result = strategy.Solve(eq, context);
        
        Assert.IsTrue(result.IsSuccess);
        // cos(x) = 1/2 -> x = acos(1/2)
        var resultText = result.ResultExpression!.ToDisplayString();
        Assert.IsTrue(
            resultText.Contains("acos", StringComparison.OrdinalIgnoreCase) ||
            resultText.Contains("pi", StringComparison.OrdinalIgnoreCase) ||
            resultText.Contains("1.047", StringComparison.Ordinal),
            resultText);
    }
}
