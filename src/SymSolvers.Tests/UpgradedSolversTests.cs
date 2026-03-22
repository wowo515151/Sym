using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using Sym.CSharpIO;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public class UpgradedSolversTests
{
    [TestMethod]
        [Timeout(10000)]
    public void EquationSolverStrategy_FactorsCubicIntoRoots()
    {
        var x = new Symbol("x");
        var expr = new Equality(
            new Add(ImmutableList.Create<IExpression>(
                new Power(x, new Number(3m)),
                new Multiply(new Number(-6m), new Power(x, new Number(2m)).Canonicalize()).Canonicalize(),
                new Multiply(new Number(11m), x).Canonicalize(),
                new Number(-6m)
            )).Canonicalize(),
            new Number(0m)).Canonicalize();

        var strategy = new Sym.Core.Strategies.EquationSolverStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(expr, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsNotNull(result.ResultExpression);
        var formatted = CSharpIO.FormatExpr(result.ResultExpression!);
        Assert.IsTrue(formatted.Contains("x = 1") && formatted.Contains("x = 2") && formatted.Contains("x = 3"),
            $"Expected roots 1,2,3 but got {formatted}");
        Assert.IsTrue(result.Trace?.Count >= 2);
    }

    [TestMethod]
        [Timeout(10000)]
    public void EquationSolverStrategy_SolvesAffineExponent()
    {
        var x = new Symbol("x");
        var equation = new Equality(
            new Function("exp", ImmutableList.Create<IExpression>(new Add(new Multiply(new Number(2m), x).Canonicalize(), new Number(1m)).Canonicalize())).Canonicalize(),
            new Function("exp", ImmutableList.Create<IExpression>(new Number(5m))).Canonicalize()).Canonicalize();

        var strategy = new Sym.Core.Strategies.EquationSolverStrategy();
        var context = new SolveContext(targetVariable: x);
        var result = strategy.Solve(equation, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var expected = CSharpIO.FormatExpr(new Equality(x, new Number(2m)).Canonicalize());
        Assert.AreEqual(expected, CSharpIO.FormatExpr(result.ResultExpression!));
    }

    [TestMethod]
        [Timeout(10000)]
    public void InequalitySolveStrategy_IsolatesLinearInequality()
    {
        var x = new Symbol("x");
        var inequality = new Function("le", ImmutableList.Create<IExpression>(
            new Add(new Multiply(new Number(3m), x).Canonicalize(), new Number(5m)).Canonicalize(),
            new Number(11m))).Canonicalize();

        var strategy = new InequalitySolveStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(inequality, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var expected = new Function("le", ImmutableList.Create<IExpression>(x, new Number(2m))).Canonicalize();
        Assert.IsTrue(result.ResultExpression!.InternalEquals(expected));
        Assert.IsTrue(result.Trace?.Count >= 2);
    }

    [TestMethod]
        [Timeout(10000)]
    public void EquationSolverStrategy_SolvesTrigAffine()
    {
        var x = new Symbol("x");
        var rhs = new Number(0.5m);
        var angle = new Number((decimal)System.Math.Asin(0.5));
        var equation = new Equality(
            new Function("sin", ImmutableList.Create<IExpression>(new Add(new Multiply(new Number(2m), x).Canonicalize(), angle).Canonicalize())).Canonicalize(),
            rhs).Canonicalize();

        var strategy = new Sym.Core.Strategies.EquationSolverStrategy();
        var context = new SolveContext(targetVariable: x);
        var result = strategy.Solve(equation, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var solved = result.ResultExpression as Equality;
        Assert.IsNotNull(solved);
        Assert.IsTrue(solved!.LeftOperand.InternalEquals(x));
        Assert.IsTrue(solved.RightOperand is Number n && n.Value == 0m, $"Expected x=0 but got {CSharpIO.FormatExpr(result.ResultExpression!)}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void IntegrationStrategy_ByPartsPolyExp()
    {
        var x = new Symbol("x");
        var expx = new Function("exp", ImmutableList.Create<IExpression>(x)).Canonicalize();
        var integral = new Integral(new Multiply(x, expx).Canonicalize(), x);
        var strategy = new IntegrationStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(integral, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var actual = CSharpIO.FormatExpr(result.ResultExpression!);
        var expectedFactored = CSharpIO.FormatExpr(new Multiply(expx, new Add(x, new Number(-1m)).Canonicalize()).Canonicalize());
        var expectedExpanded = CSharpIO.FormatExpr(new Add(new Multiply(new Number(-1m), expx).Canonicalize(), new Multiply(x, expx).Canonicalize()).Canonicalize());
        Assert.IsTrue(
            actual == expectedFactored || actual == expectedExpanded,
            $"Unexpected antiderivative.\nExpected: {expectedFactored}\nOr: {expectedExpanded}\nActual: {actual}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void IntegrationStrategy_DefiniteIntegralDetectsDiscontinuity()
    {
        var x = new Symbol("x");
        var definite = new DefiniteIntegral(new Divide(new Number(1m), x).Canonicalize(), x, new Number(-1m), new Number(1m));
        var strategy = new IntegrationStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(definite, context);

        Assert.IsFalse(result.IsSuccess, "Discontinuous integral should fail.");
    }

    [TestMethod]
        [Timeout(10000)]
    public void LimitStrategy_SinxOverX()
    {
        var x = new Symbol("x");
        var expr = new Divide(new Function("sin", ImmutableList.Create<IExpression>(x)).Canonicalize(), x).Canonicalize();
        var limit = new Limit(expr, x, new Number(0m));

        var strategy = new LimitStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(limit, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsTrue(result.ResultExpression is Number n && n.Value == 1m);
    }

    [TestMethod]
        [Timeout(10000)]
    public void IntegrationStrategy_TrigonometricSubstitutionForm()
    {
        var x = new Symbol("x");
        var integral = new Integral(new Divide(new Number(1m), new Add(new Power(x, new Number(2m)).Canonicalize(), new Power(new Number(2m), new Number(2m)).Canonicalize()).Canonicalize()).Canonicalize(), x);
        var strategy = new IntegrationStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(integral, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsTrue(CSharpIO.FormatExpr(result.ResultExpression!).Contains("atan"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void EquationSolverStrategy_SolvesHyperbolic()
    {
        var x = new Symbol("x");
        var equation = new Equality(
            new Function("sinh", ImmutableList.Create<IExpression>(new Add(new Multiply(new Number(3m), x).Canonicalize(), new Number(1m)).Canonicalize())).Canonicalize(),
            new Number(5m)).Canonicalize();

        var strategy = new Sym.Core.Strategies.EquationSolverStrategy();
        var context = new SolveContext(targetVariable: x);
        var result = strategy.Solve(equation, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var expected = new Equality(x, new Divide(new Add(new Function("asinh", ImmutableList.Create<IExpression>(new Number(5m))).Canonicalize(), new Number(-1m)).Canonicalize(), new Number(3m)).Canonicalize()).Canonicalize();
        Assert.IsTrue(result.ResultExpression!.InternalEquals(expected));
    }

    [TestMethod]
        [Timeout(10000)]
    public void InequalitySolveStrategy_ConjunctionProducesInterval()
    {
        var x = new Symbol("x");
        var le = new Function("le", ImmutableList.Create<IExpression>(x, new Number(5m))).Canonicalize();
        var ge = new Function("ge", ImmutableList.Create<IExpression>(x, new Number(1m))).Canonicalize();
        var combo = new Function("and", ImmutableList.Create<IExpression>(le, ge)).Canonicalize();

        var strategy = new InequalitySolveStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(combo, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var interval = result.ResultExpression as Function;
        Assert.IsNotNull(interval);
        Assert.AreEqual("interval", interval!.Name.ToLowerInvariant());
    }

    [TestMethod]
        [Timeout(10000)]
    public void SeriesExpansionStrategy_Log1p()
    {
        var x = new Symbol("x");
        var series = new SeriesExpansion(new Function("log", ImmutableList.Create<IExpression>(new Add(new Number(1m), x).Canonicalize())).Canonicalize(), x, new Number(1m), 4);
        var strategy = new SeriesExpansionStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(series, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var formatted = CSharpIO.FormatExpr(result.ResultExpression!);
        Assert.IsTrue(
            formatted.Contains("x") && (formatted.Contains("Pow(x, 2)") || formatted.Contains("x^2")),
            $"Unexpected expansion: {formatted}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void IntegrationStrategy_SqrtA2MinusX2()
    {
        var x = new Symbol("x");
        var integrand = new Power(new Add(new Number(4m), new Multiply(new Number(-1m), new Power(x, new Number(2m)).Canonicalize()).Canonicalize()).Canonicalize(), new Number(0.5m)).Canonicalize();
        var integral = new Integral(integrand, x);
        var strategy = new IntegrationStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(integral, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsTrue(CSharpIO.FormatExpr(result.ResultExpression!).Contains("asin"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void IntegrationStrategy_SplitsDiscontinuity()
    {
        var x = new Symbol("x");
        var definite = new DefiniteIntegral(new Divide(new Number(1m), new Add(x, new Number(-1m)).Canonicalize()).Canonicalize(), x, new Number(0m), new Number(2m));
        var strategy = new IntegrationStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(definite, context);

        Assert.IsFalse(result.IsSuccess, "Improper integral with internal pole should fail or split to failure.");
    }

    [TestMethod]
        [Timeout(10000)]
    public void InequalitySolveStrategy_WithGuardPiecewise()
    {
        var x = new Symbol("x");
        var y = new Symbol("y");
        var le = new Function("le", ImmutableList.Create<IExpression>(x, new Number(5m))).Canonicalize();
        var guard = new Function("ge", ImmutableList.Create<IExpression>(y, new Number(2m))).Canonicalize();
        var combo = new Function("and", ImmutableList.Create<IExpression>(le, guard)).Canonicalize();

        var strategy = new InequalitySolveStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(combo, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var pw = result.ResultExpression as Function;
        Assert.IsNotNull(pw);
        Assert.AreEqual("piecewise", pw!.Name.ToLowerInvariant());
    }

    [TestMethod]
        [Timeout(10000)]
    public void EquationSolverStrategy_HyperbolicBranchGuard()
    {
        var x = new Symbol("x");
        var equation = new Equality(
            new Function("cosh", ImmutableList.Create<IExpression>(new Multiply(new Number(2m), x).Canonicalize())).Canonicalize(),
            new Number(3m)).Canonicalize();

        var strategy = new Sym.Core.Strategies.EquationSolverStrategy();
        var context = new SolveContext(targetVariable: x);
        var result = strategy.Solve(equation, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsTrue(CSharpIO.FormatExpr(result.ResultExpression!).Contains("acosh"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void IntegrationStrategy_BySubstitutionHandlesExponentialLinear()
    {
        var x = new Symbol("x");
        var integral = new Integral(
            new Function("exp", ImmutableList.Create<IExpression>(new Add(new Multiply(new Number(2m), x).Canonicalize(), new Number(1m)).Canonicalize())),
            x);

        var strategy = new IntegrationStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(integral, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var expected = new Divide(
            new Function("exp", ImmutableList.Create<IExpression>(new Add(new Multiply(new Number(2m), x).Canonicalize(), new Number(1m)).Canonicalize())),
            new Number(2m)).Canonicalize();
        Assert.IsTrue(result.ResultExpression!.InternalEquals(expected), $"Expected {CSharpIO.FormatExpr(expected)} but got {CSharpIO.FormatExpr(result.ResultExpression!)}");
        Assert.IsTrue(result.Trace?.Count >= 2);
    }

    [TestMethod]
        [Timeout(10000)]
    public void IntegrationStrategy_DefiniteIntegralPolynomial()
    {
        var x = new Symbol("x");
        var definite = new DefiniteIntegral(new Multiply(x, x).Canonicalize(), x, new Number(0m), new Number(2m));
        var strategy = new IntegrationStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(definite, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var n = result.ResultExpression as Number;
        Assert.IsNotNull(n, "Expected numeric result.");
        var expected = 8m / 3m;
        Assert.IsTrue(System.Math.Abs((double)(n.Value - expected)) < 1e-12, $"Expected ~{expected} but got {n.Value}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void SeriesExpansionStrategy_SinMaclaurin()
    {
        var x = new Symbol("x");
        var series = new SeriesExpansion(
            new Function("sin", ImmutableList.Create<IExpression>(x)).Canonicalize(),
            x,
            new Number(0m),
            5);
        var strategy = new SeriesExpansionStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(series, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var formatted = CSharpIO.FormatExpr(result.ResultExpression!);
        Assert.IsTrue(formatted.Contains("x") && formatted.Contains("Pow(x, 3)") && formatted.Contains("Pow(x, 5)"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void LimitStrategy_SymmetricProbeForSinc()
    {
        var x = new Symbol("x");
        var expr = new Divide(new Function("sin", ImmutableList.Create<IExpression>(x)).Canonicalize(), x).Canonicalize();
        var limit = new Limit(expr, x, new Number(0m));

        var strategy = new LimitStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(limit, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsTrue(result.ResultExpression is Number n && n.Value > 0.9m && n.Value < 1.1m);
    }

    [TestMethod]
        [Timeout(10000)]
    public void NewtonHybridStrategy_ConvergesWithSymbolicDerivative()
    {
        var x = new Symbol("x");
        var equation = new Equality(
            new Add(ImmutableList.Create<IExpression>(
                new Power(x, new Number(2m)),
                new Number(-4m)
            )).Canonicalize(),
            new Number(0m)).Canonicalize();

        var strategy = new NewtonHybridStrategy();
        var context = new SolveContext(targetVariable: x, enableTracing: true,
            additionalData: ImmutableDictionary<string, object>.Empty.Add("InitialGuess", 1));
        var result = strategy.Solve(equation, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var expected = new Equality(x, new Number(2m)).Canonicalize();
        Assert.IsTrue(result.ResultExpression!.InternalEquals(expected));
    }

    [TestMethod]
        [Timeout(10000)]
    public void DifferentiationStrategy_HandlesProductAndQuotientChain()
    {
        var x = new Symbol("x");
        var expression = new Divide(
            new Multiply(new Function("sin", ImmutableList.Create<IExpression>(new Power(x, new Number(2m)).Canonicalize())).Canonicalize(), new Power(x, new Number(3m)).Canonicalize()).Canonicalize(),
            x).Canonicalize();

        var pack = SymRules.RulePackLibrary.GetRulePacks().FirstOrDefault(p => p.Name == "DifferentiationStrategy");
        var strategy = new RulePackStrategy(pack!);
        var derivative = new Derivative(expression, x);
        var context = new SolveContext(null, enableTracing: true);
        var result = strategy.Solve(derivative, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsFalse(result.ResultExpression!.InternalEquals(new Number(0m)), "Derivative should not be trivially zero.");
    }
}
