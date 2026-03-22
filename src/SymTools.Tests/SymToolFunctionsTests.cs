using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.CSharpIO;
using SymSolvers;

namespace SymTools.Tests;

[TestClass]
public class SymToolFunctionsTests
{
    [TestMethod]
    [Timeout(10000)]
    public void EvalExpression_ComputesNumericResult()
    {
        var result = SymToolFunctions.EvalExpression("1 + 2 * 3");

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.AreEqual("7", result.Output);
    }

    [TestMethod]
    [Timeout(10000)]
    public void SimplifyExpression_SimplifiesIdentity()
    {
        var result = SymToolFunctions.SimplifyExpression("x + 0");

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.AreEqual("x", result.Output);
    }

    [TestMethod]
    [Timeout(10000)]
    public void SolveEquation_SolvesLinearEquation()
    {
        var result = SymToolFunctions.SolveEquation("x + 5 = 10", "x");

        Assert.IsTrue(result.IsSuccess, result.Message);
        StringAssert.Contains(result.Output, "x = 5");
    }

    [TestMethod]
    [Timeout(10000)]
    public void SolveSystem_SolvesTargetInChain()
    {
        var result = SymToolFunctions.SolveSystem(new[] { "a = b + 1", "b = c * 2", "c = 3" }, "a");

        Assert.IsTrue(result.IsSuccess, result.Message);
        StringAssert.Contains(result.Output, "a = 7");
    }

    [TestMethod]
    [Timeout(10000)]
    public void SolveProblemScript_SolvesTargetVariable()
    {
        var script = @"
<Options>
  Target: x
</Options>
x + 5 = 10
";

        var result = SymToolFunctions.SolveProblemScript(script);

        Assert.IsTrue(result.IsSuccess, result.Message);
        StringAssert.Contains(result.Output, "x = 5");
    }

    [TestMethod]
    [Timeout(10000)]
    public void OptimizeTensorExpression_FusesMatMulAddRelu()
    {
        var result = SymToolFunctions.OptimizeTensorExpression("Relu(TensorAdd(MatMul(A, B), C))");

        Assert.IsTrue(result.IsSuccess, result.Message);
        StringAssert.Contains(result.Output, "FusedMatMulAddRelu(A, B, C)");
    }

    [TestMethod]
    [Timeout(10000)]
    public void DifferentiateExpression_ComputesDerivative()
    {
        var result = SymToolFunctions.DifferentiateExpression("atan(x)", "x");

        Assert.IsTrue(result.IsSuccess, result.Message);
        var parsed = CSharpIO.ParseExpressionsStrict(result.Output);
        Assert.AreEqual(1, parsed.Count);
        Assert.IsTrue(NumericEvaluator.TryEvaluate(parsed[0], new Dictionary<string, decimal> { ["x"] = 1m }, out var value, out var error), error);
        Assert.AreEqual(0.5m, value);
    }

    [TestMethod]
    [Timeout(10000)]
    public void IntegrateExpression_ComputesAntiderivative()
    {
        var result = SymToolFunctions.IntegrateExpression("x^3", "x");

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.AreEqual("0.25 * Pow(x, 4)", result.Output);
    }
}