// Copyright Warren Harding 2026
using System.Collections.Immutable;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using Sym.Operations;
using SymRules;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public class RulePackIntegrationTests
{
    [TestMethod]
        [Timeout(10000)]
    public void CalculusHelper_DifferentiatesConstant()
    {
        var x = new Symbol("x");
        var c = new Number(10m);
        var diff = CalculusHelper.DifferentiateExpression(c, x);
        Assert.IsTrue(diff.InternalEquals(new Number(0m)), $"Expected 0 but got {diff.ToDisplayString()}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void RulePackStrategy_DifferentiationPack_Loaded()
    {
        var packs = RulePackLibrary.GetRulePacks();
        var pack = packs.FirstOrDefault(p => p.Name == "DifferentiationStrategy");
        Assert.IsNotNull(pack, "DifferentiationStrategy pack not found.");
        Assert.IsTrue(pack.Rules.Count > 0, "DifferentiationStrategy pack has no rules.");
    }

    [TestMethod]
        [Timeout(10000)]
    public void EGraph_SimplifyResidual_Scenario()
    {
        // Simulate DifferentialEquationStrategy.SimplifyResidual scenario
        // Derivative(-1, x) should simplify to 0.
        var x = new Symbol("x");
        var residual = new Derivative(new Number(-1m), x);
        
        // Use RuleProvider to get rules as DifferentialEquationStrategy does now
        var context = new SolveContext();
        var rules = RuleProvider.BuildRules(context);
        
        var strategy = new EGraphSolverStrategy();
        var result = strategy.Solve(residual, new SolveContext(null, rules));
        
        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.ResultExpression);
        Assert.IsTrue(result.ResultExpression.InternalEquals(new Number(0m)), $"Expected 0 but got {result.ResultExpression.ToDisplayString()}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void RuleProvider_ArithmeticBenchmarkProfile_LoadsEquationRulesButSkipsIrrelevantBuiltins()
    {
        var additionalData = ImmutableDictionary<string, object>.Empty
            .Add(SolverOptionKeys.ArithmeticBenchmarkRuleProfile, true);
        var context = new SolveContext(additionalData: additionalData);

        var rules = RuleProvider.BuildRules(context);

        Assert.IsTrue(rules.Any(r => string.Equals(r.Name, "IsolateAddLeft", StringComparison.Ordinal)));
        Assert.IsFalse(rules.Any(r => string.Equals(r.Name, "ODEFirstOrderLinear", StringComparison.Ordinal)));
        Assert.IsFalse(rules.Any(r => string.Equals(r.Name, "ODESeparable", StringComparison.Ordinal)));
    }

    [TestMethod]
        [Timeout(10000)]
    public void CalculusHelper_DifferentiatesAddArgs()
    {
        // d/dx (-5 + x) -> 1
        var x = new Symbol("x");
        var expr = new Add(new Number(-5m), x).Canonicalize();
        var diff = CalculusHelper.DifferentiateExpression(expr, x);
        
        Assert.IsTrue(diff.InternalEquals(new Number(1m)), $"Expected 1 but got {diff.ToDisplayString()}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void EGraphMatcher_MatchesConstantConstraint()
    {
        var graph = new Sym.Core.EGraph.EGraph();
        var numId = graph.Add(new Number(10m));
        var xId = graph.Add(new Symbol("x"));
        var derivId = graph.Add(new Derivative(new Number(10m), new Symbol("x")));
        graph.Rebuild();

        // Pattern: Derivative(?c:Constant, ?x)
        var pattern = new Derivative(new Wild("c", WildConstraint.Constant), new Wild("x"));
        
        var rules = ImmutableList.Create(new Sym.Core.Rule(pattern, new Number(0m)) { Name = "DerivConst" });
        var matches = EGraphMatcher.FindMatches(graph, rules);

        Assert.AreEqual(1, matches.Count, "Should find 1 match for DerivConst.");
        Assert.AreEqual("DerivConst", matches[0].Rule.Name);
    }

    [TestMethod]
        [Timeout(10000)]
    public void EGraph_SimplifiesDerivativeOfNegativeConstant()
    {
        // d/dx (-5) -> 0
        var x = new Symbol("x");
        var expr = new Derivative(new Number(-5m), x);
        
        var pack = RulePackLibrary.GetRulePacks().FirstOrDefault(p => p.Name == "DifferentiationStrategy");
        Assert.IsNotNull(pack);

        var strategy = new EGraphSolverStrategy();
        var result = strategy.Solve(expr, new SolveContext(null, pack.Rules));
        
        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.ResultExpression!.InternalEquals(new Number(0m)), 
            $"Expected 0 but got {result.ResultExpression.ToDisplayString()}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void EGraph_ExtractionPrefersSimplifiedForm()
    {
        // Add both a complex and simple node to a class and ensure extraction picks simple.
        var graph = new Sym.Core.EGraph.EGraph();
        var x = new Symbol("x");
        var derivId = graph.Add(new Derivative(x, x));
        var oneId = graph.Add(new Number(1m));
        graph.Union(derivId, oneId);
        graph.Rebuild();

        var best = EGraphExtract.ExtractBest(graph, derivId);
        Assert.IsTrue(best is Number n && n.Value == 1m, $"Expected 1 but extracted {best.ToDisplayString()}");
    }
}
