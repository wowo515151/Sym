// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;

namespace SymEGraph.Tests;

[TestClass]
public class TimeoutTests
{
    [TestMethod]
    [Timeout(10000)]
    public void Solve_SaturationTimeout_ProceedsToExtraction()
    {
        var rulesBuilder = ImmutableList.CreateBuilder<Rule>();
        for (int i = 0; i < 10; i++)
        {
            int captured = i; // Ensure unique delegate per rule
            rulesBuilder.Add(new Rule(
                new Wild("a"), 
                new Wild("a"),
                condition: b => { Thread.Sleep(200); return captured >= 0; }
            ) { Name = "SlowRule" + i });
        }
        
        // Add a rule that actually does something
        rulesBuilder.Add(new Rule(new Add(new Number(1), new Number(1)), new Number(2)) { Name = "AddRule" });
        
        var problem = new Add(new Number(1), new Number(1));
        
        // Set timeout to 1 second.
        var additionalData = ImmutableDictionary.CreateRange(new Dictionary<string, object>
        {
            { SolverOptionKeys.SaturationTimeoutSeconds, 1 }
        });
        var context = new SolveContext(targetVariable: null, rules: rulesBuilder.ToImmutable(), additionalData: additionalData);
        var strategy = new EGraphSolverStrategy();

        var startTime = System.DateTime.UtcNow;
        var result = strategy.Solve(problem, context);
        var duration = System.DateTime.UtcNow - startTime;

        // With 10 rules and 2 classes (1, 1+1), we expect 20 matches for SlowRules + 1 for AddRule.
        // Each SlowRule application takes 200ms. 
        // 5 slow rules = 1 second.
        
        Assert.IsTrue(duration.TotalSeconds >= 0.9, $"Duration was {duration.TotalSeconds}s, expected >= 0.9s");
        Assert.IsTrue(result.IsSuccess, $"Result should be success. Message: {result.Message}");
    }

    [TestMethod]
    public void Resolve_UsesSaturationTimeoutForExtraction_WhenExtractionTimeoutMissing()
    {
        var context = new SolveContext(
            additionalData: ImmutableDictionary<string, object>.Empty.Add(SolverOptionKeys.SaturationTimeoutSeconds, 15));

        var budget = EGraphTimeoutBudget.Resolve(context);

        Assert.AreEqual(15, budget.SaturationTimeoutSeconds);
        Assert.AreEqual(15, budget.ExtractionTimeoutSeconds);
    }

    [TestMethod]
    public void Resolve_UsesExplicitExtractionTimeout_WhenProvided()
    {
        var context = new SolveContext(
            additionalData: ImmutableDictionary<string, object>.Empty
                .Add(SolverOptionKeys.SaturationTimeoutSeconds, 15)
                .Add(SolverOptionKeys.ExtractionTimeoutSeconds, 4));

        var budget = EGraphTimeoutBudget.Resolve(context);

        Assert.AreEqual(15, budget.SaturationTimeoutSeconds);
        Assert.AreEqual(4, budget.ExtractionTimeoutSeconds);
    }
}
