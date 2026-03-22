using System;
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public sealed class CalculusExtendedTests
{
    [TestMethod]
        [Timeout(10000)]
    public void IntegrationStrategy_ReversesDerivative()
    {
        var x = new Symbol("x");
        var expr = new Integral(new Derivative(new Power(x, new Number(2m)), x), x);
        var context = new SolveContext(x, ImmutableList<Rule>.Empty, maxIterations: 64, enableTracing: false);
        var strategy = new IntegrationStrategy();

        var result = strategy.Solve(expr, context);

        Assert.IsTrue(result.IsSuccess, $"Integration failed: {result.Message}");
        Assert.IsNotNull(result.ResultExpression);
        Assert.IsTrue(result.ResultExpression!.Canonicalize().InternalEquals(new Power(x, new Number(2m)).Canonicalize()),
            $"Expected x^2 but got {result.ResultExpression!.ToDisplayString()}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void DifferentiationStrategy_ComputesAtanChain()
    {
        var x = new Symbol("x");
        var expr = new Derivative(new Function("atan", ImmutableList.Create<IExpression>(x)), x);
        var context = new SolveContext(null, ImmutableList<Rule>.Empty, maxIterations: 64, enableTracing: false);
        var pack = SymRules.RulePackLibrary.GetRulePacks().FirstOrDefault(p => p.Name == "DifferentiationStrategy");
        var strategy = new RulePackStrategy(pack!);

        var result = strategy.Solve(expr, context);

        Assert.IsTrue(result.IsSuccess, $"Differentiation failed: {result.Message}");
        Assert.IsNotNull(result.ResultExpression);
        var numeric = Evaluate(result.ResultExpression!, ("x", 1m));
        Assert.IsTrue(Math.Abs((double)(numeric - 0.5m)) < 1e-6, $"Expected 0.5 at x=1 but got {numeric}");
    }

    private static decimal Evaluate(IExpression expr, (string name, decimal value) binding)
    {
        var dict = ImmutableDictionary.CreateBuilder<string, decimal>();
        dict[binding.name] = binding.value;
        if (!NumericEvaluator.TryEvaluate(expr, dict.ToImmutable(), out var value, out var error))
        {
            Assert.Fail($"Numeric evaluation failed: {error}");
        }
        return value;
    }
}
