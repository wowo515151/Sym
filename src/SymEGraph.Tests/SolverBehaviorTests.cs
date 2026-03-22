//Copyright Warren Harding 2025.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Immutable;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;

namespace SymEGraph.Tests;

[TestClass]
public class SolverBehaviorTests
{
    [TestMethod]
        [Timeout(10000)]
    public void Solve_WithoutTargetVariable_ReturnsSimplifiedExpression()
    {
        // 1 + 1 -> 2
        var rule = new Rule(new Add(new Number(1), new Number(1)), new Number(2));
        var problem = new Add(new Number(1), new Number(1));
        var context = new SolveContext(targetVariable: null, rules: ImmutableList.Create(rule));
        var strategy = new EGraphSolverStrategy();

        var result = strategy.Solve(problem, context);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsInstanceOfType(result.ResultExpression, typeof(Number));
        Assert.AreEqual("2", result.ResultExpression!.ToDisplayString());
    }

    [TestMethod]
        [Timeout(10000)]
    public void Solve_WithTargetVariable_ReturnsEquality()
    {
        // x + 0 -> x
        var x = new Symbol("x");
        var rule = new Rule(new Add(new Wild("a"), new Number(0)), new Wild("a"));
        
        // We known x = 2
        var eq = new Equality(x, new Number(2));
        // Problem is just x
        var problem = x; 
        
        var context = new SolveContext(targetVariable: x, rules: ImmutableList.Create(rule));
        
        // We need to inject the knowledge x=2. 
        // EGraphSolverStrategy treats Equality in problem as assertions.
        var system = new Vector(ImmutableList.Create<IExpression>(eq));
        
        var strategy = new EGraphSolverStrategy();
        var result = strategy.Solve(system, context);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsInstanceOfType(result.ResultExpression, typeof(Equality));
        var resEq = (Equality)result.ResultExpression!;
        Assert.IsTrue(resEq.LeftOperand.InternalEquals(x));
        Assert.AreEqual("2", resEq.RightOperand.ToDisplayString());
    }

    [TestMethod]
        [Timeout(10000)]
    public void Solve_WithTargetVariable_EvenIfInputIsExpression_ReturnsEquality()
    {
        // Scenario: Problem is 'x + 0', target variable is 'x'.
        // Even if we just want to simplify x+0, if we provide a TargetVariable, 
        // EGraphSolverStrategy currently wraps it in an Equality.
        
        var x = new Symbol("x");
        var rule = new Rule(new Add(new Wild("a"), new Number(0)), new Wild("a"));
        var problem = new Add(x, new Number(0));
        
        var context = new SolveContext(targetVariable: x, rules: ImmutableList.Create(rule));
        var strategy = new EGraphSolverStrategy();
        
        var result = strategy.Solve(problem, context);
        
        Assert.IsTrue(result.IsSuccess);
        // We now expect the simplified expression back, not an Equality, if the input was not an equation.
        // This avoids confusing other strategies like IntegrationStrategy that might set a target variable.
        Assert.IsInstanceOfType(result.ResultExpression, typeof(Symbol));
        Assert.AreEqual("x", result.ResultExpression!.ToDisplayString());
    }

    [TestMethod]
        [Timeout(10000)]
    public void PartialFractionSolve_RecursingIntoEquality_DoesNotProduceNestedEquality()
    {
        var x = new Symbol("x");
        // Problem is an equality containing a rational function that needs PF expansion
        // (1/((x-1)*(x+1))) = 0
        var xMinus1 = new Subtract(x, new Number(1)).Canonicalize();
        var xPlus1 = new Add(x, new Number(1)).Canonicalize();
        var denom = new Multiply(xMinus1, xPlus1).Canonicalize();
        var problem = new Equality(
            new Divide(new Number(1), denom).Canonicalize(),
            new Number(0)
        ).Canonicalize();

        // Even with target variable x, the expansion should happen INSIDE the equality operands
        var context = new SolveContext(targetVariable: x);
        var strategy = new PartialFractionStrategy();

        var result = strategy.Solve(problem, context);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsInstanceOfType(result.ResultExpression, typeof(Equality));
        var resEq = (Equality)result.ResultExpression!;
        
        System.Console.WriteLine($"DEBUG: PF Result LHS: {resEq.LeftOperand.ToDisplayString()}");
        
        // Ensure LHS was expanded but didn't become "x = ..."
        Assert.IsFalse(resEq.LeftOperand is Equality, "LHS should not be a nested Equality");
    }
}
