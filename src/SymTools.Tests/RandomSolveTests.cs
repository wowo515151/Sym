// Copyright Warren Harding 2026
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.Strategies;
using Sym.Operations;
using SymSolvers;

namespace SymTools.Tests;

[TestClass]
public sealed class RandomSolveTests
{
    private const int RandomSolveCaseCount = 100;
    private static readonly Symbol X = new("x");
    private static readonly Symbol Y = new("y");
    private static readonly decimal[] FiniteDivisors = { -10m, -8m, -5m, -4m, -2m, 2m, 4m, 5m, 8m, 10m };
    private const decimal SolveTolerance = 0.00000000000000000000000001m;

    [TestMethod]
    [Timeout(30000)]
    public void TestSolve_RunsHundredRandomCases()
    {
        RunRandomSolveCases(CreateDirectStrategy, RandomSolveCaseCount);
    }

    [TestMethod]
    [Timeout(60000)]
    public void TestSolve_EGraph_RunsHundredRandomCases()
    {
        RunRandomSolveCases(CreateEGraphStrategy, RandomSolveCaseCount);
    }

    [TestMethod]
    [Timeout(120000)]
    public void TestSolve_Cobra_RunsHundredRandomCases()
    {
        RunRandomSolveCases(CreateCobraStrategy, RandomSolveCaseCount);
    }

    private static void RunRandomSolveCases(Func<ISolverStrategy> strategyFactory, int caseCount)
    {
        var random = new Random(24680);

        for (int i = 0; i < caseCount; i++)
        {
            TestSolve(random, i + 1, strategyFactory());
        }
    }

    private static void TestSolve(Random random, int caseIndex, ISolverStrategy strategy)
    {
        IExpression expression = GenerateSingleOccurrenceExpression(random);
        decimal expectedX = random.Next(-20, 21);

        Assert.IsTrue(
            NumericEvaluator.TryEvaluate(
                expression,
                new Dictionary<string, decimal> { [X.Name] = expectedX },
                out decimal yValue,
                out string? yError),
            $"Failed to evaluate generated expression on case {caseIndex}: {expression.ToDisplayString()}. Error: {yError}");

        var equation = new Equality(Y, expression);
        var solveResult = strategy.Solve(equation, new SolveContext(X, rules: null, maxIterations: 100, enableTracing: false, additionalData: null));

        Assert.IsTrue(
            solveResult.IsSuccess,
            $"Solve failed on case {caseIndex}. Equation: {equation.ToDisplayString()}. Message: {solveResult.Message}");
        Assert.IsInstanceOfType(
            solveResult.ResultExpression,
            typeof(Equality),
            $"Expected an equality result on case {caseIndex}. Got: {solveResult.ResultExpression?.ToDisplayString() ?? "null"}");

        var solvedEquality = (Equality)solveResult.ResultExpression!;
        Assert.IsTrue(
            TryGetSolvedExpressionForTarget(solvedEquality, X, out IExpression solvedExpression),
            $"Solver did not isolate target x on case {caseIndex}. Result: {solvedEquality.ToDisplayString()}");

        IExpression substituted = SubstituteSymbol(solvedExpression, Y, new Number(yValue)).Canonicalize();
        Assert.IsTrue(
            NumericEvaluator.TryEvaluate(substituted, ImmutableDictionary<string, decimal>.Empty, out decimal recoveredX, out string? recoveredError),
            $"Failed to evaluate solved expression on case {caseIndex}. Expression: {substituted.ToDisplayString()}. Error: {recoveredError}");

        Assert.IsTrue(
            decimal.Abs(expectedX - recoveredX) <= SolveTolerance,
            $"Recovered x did not match on case {caseIndex}. Original x={expectedX}, recovered x={recoveredX}, F(x)={expression.ToDisplayString()}, solved={solvedEquality.ToDisplayString()}, y={yValue}");
    }

    private static ISolverStrategy CreateDirectStrategy()
    {
        return new EquationSolverStrategy();
    }

    private static ISolverStrategy CreateEGraphStrategy()
    {
        var seedContext = new SolveContext(X, rules: null, maxIterations: 100, enableTracing: false, additionalData: null);
        var rules = RuleProvider.BuildRules(seedContext);
        return new EGraphSolveAdapter(rules);
    }

    private static ISolverStrategy CreateCobraStrategy()
    {
        var seedContext = new SolveContext(X, rules: null, maxIterations: 100, enableTracing: false, additionalData: null);
        var rules = RuleProvider.BuildRules(seedContext);
        return new CobraSolveAdapter(rules);
    }

    private static IExpression GenerateSingleOccurrenceExpression(Random random)
    {
        IExpression expression = X;
        int steps = random.Next(1, 5);

        for (int i = 0; i < steps; i++)
        {
            int operation = random.Next(7);
            decimal constant = operation is 4 or 5 or 6 ? NextFiniteDivisor(random) : NextNonZeroConstant(random);
            IExpression constantExpression = new Number(constant);

            expression = ((IExpression)(operation switch
            {
                0 => new Add(expression, constantExpression),
                1 => new Add(constantExpression, expression),
                2 => new Subtract(expression, constantExpression),
                3 => new Subtract(constantExpression, expression),
                4 => new Multiply(expression, constantExpression),
                5 => new Multiply(constantExpression, expression),
                6 => new Divide(expression, constantExpression),
                _ => throw new InvalidOperationException("Unsupported random operation."),
            })).Canonicalize();
        }

        return expression;
    }

    private static decimal NextNonZeroConstant(Random random)
    {
        decimal value;
        do
        {
            value = random.Next(-9, 10);
        }
        while (value == 0m);

        return value;
    }

    private static decimal NextFiniteDivisor(Random random)
    {
        return FiniteDivisors[random.Next(FiniteDivisors.Length)];
    }

    private static IExpression SubstituteSymbol(IExpression expression, Symbol target, IExpression replacement)
    {
        if (expression is Symbol symbol && symbol.InternalEquals(target))
        {
            return replacement;
        }

        if (expression is Operation operation)
        {
            var substitutedArguments = operation.Arguments
                .Select(argument => SubstituteSymbol(argument, target, replacement))
                .ToImmutableList();
            return operation.WithArguments(substitutedArguments);
        }

        return expression;
    }

    private static bool TryGetSolvedExpressionForTarget(Equality equality, Symbol target, out IExpression solvedExpression)
    {
        solvedExpression = null!;

        if (equality.LeftOperand is Symbol leftSymbol && leftSymbol.InternalEquals(target) && !equality.RightOperand.ContainsSymbol(target))
        {
            solvedExpression = equality.RightOperand;
            return true;
        }

        if (equality.RightOperand is Symbol rightSymbol && rightSymbol.InternalEquals(target) && !equality.LeftOperand.ContainsSymbol(target))
        {
            solvedExpression = equality.LeftOperand;
            return true;
        }

        return false;
    }

    private sealed class EGraphSolveAdapter : ISolverStrategy
    {
        private readonly ImmutableList<Rule> _rules;

        public EGraphSolveAdapter(ImmutableList<Rule> rules)
        {
            _rules = rules;
        }

        public SolveResult Solve(IExpression? problem, SolveContext context)
        {
            var eGraphContext = new SolveContext(context.TargetVariable, _rules, context.MaxIterations, context.EnableTracing, context.AdditionalData, context.CancellationToken, context.SharedEGraph, context.MaxConcurrency);
            return new EGraphSolverStrategy().Solve(problem, eGraphContext);
        }
    }

    private sealed class CobraSolveAdapter : ISolverStrategy
    {
        private readonly ImmutableList<Rule> _rules;

        public CobraSolveAdapter(ImmutableList<Rule> rules)
        {
            _rules = rules;
        }

        public SolveResult Solve(IExpression? problem, SolveContext context)
        {
            var cobraContext = new SolveContext(context.TargetVariable, _rules, context.MaxIterations, context.EnableTracing, context.AdditionalData, context.CancellationToken, context.SharedEGraph, context.MaxConcurrency);
            return new CobraSolverStrategy().Solve(problem, cobraContext);
        }
    }
}
