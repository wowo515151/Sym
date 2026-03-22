using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public class DiscreteRecurrenceStrategyTests
{
    [TestMethod]
        [Timeout(10000)]
    public void Solve_WithNullProblem_ReturnsFailure()
    {
        var strategy = new DiscreteRecurrenceStrategy();
        var context = new SolveContext();

        var result = strategy.Solve(null, context);

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Message, "Problem expression cannot be null.");
    }

    [TestMethod]
        [Timeout(10000)]
    public void Solve_WithValidProblem_ReturnsNotImplementedFailure()
    {
        var strategy = new DiscreteRecurrenceStrategy();
        var problem = new Number(1m);
        var context = new SolveContext();

        var result = strategy.Solve(problem, context);

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Message, "Only single recurrence relation supported.");
    }
}
