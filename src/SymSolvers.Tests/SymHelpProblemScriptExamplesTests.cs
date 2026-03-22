using Microsoft.VisualStudio.TestTools.UnitTesting;
using WordsToSym;

namespace SymSolvers.Tests;

[TestClass]
public class SymHelpProblemScriptExamplesTests
{
    private ProblemScriptEGraphWrapper _wrapper = null!;

    [TestInitialize]
    public void Setup()
    {
        _wrapper = new ProblemScriptEGraphWrapper();
    }

    [TestMethod]
    [Timeout(10000)]
    public void SymHelp_MinimalSolveExample_SolvesAsWritten()
    {
        const string script = """
<Options>
  Target: x
  RulePacks: EquationSolving, Algebraic
  MaxIterations: 50
</Options>
x + 5 = 10
""";

        string result = _wrapper.SolveWithEGraph(script);

        AssertSolved("Minimal solve", result);
        StringAssert.Contains(result, "x = 5");
    }

    [TestMethod]
    [Timeout(10000)]
    public void SymHelp_AlgebraicSimplificationExample_SolvesAsWritten()
    {
        const string script = """
(x + 0) * 1 + x - x
""";

        string result = _wrapper.SolveWithEGraph(script);

        AssertSolved("Algebraic simplification", result);
    }

    [TestMethod]
    [Timeout(10000)]
    public void SymHelp_TargetSolveExample_SolvesAsWritten()
    {
        const string script = """
<Options>
  Target: x
</Options>

x + 5 = 10
""";

        string result = _wrapper.SolveWithEGraph(script);

        AssertSolved("Target solve", result);
        StringAssert.Contains(result, "x = 5");
    }

    [TestMethod]
    [Timeout(10000)]
    public void SymHelp_InlineCustomRuleExample_SolvesAsWritten()
    {
        const string script = """
<Rules>
  Rule(f(Wild("x")), Pow(Wild("x"), 2));
</Rules>

f(5) = y
""";

        string result = _wrapper.SolveWithEGraph(script);

        AssertSolved("Inline custom rule", result);
        Assert.IsTrue(result.Contains("y = 25") || result.Contains("25 = y"), $"Inline custom rule example returned unexpected output: {result}");
    }

    [TestMethod]
    [Timeout(10000)]
    public void SymHelp_AssumptionsExample_SolvesAsWritten()
    {
        const string script = """
<Options>
  AssumePositive: x
  AssumeReal: y
  RulePacks: Algebraic, Trigonometry
</Options>

sqrt(x^2) + sin(-y)
""";

        string result = _wrapper.SolveWithEGraph(script);

        AssertSolved("Assumptions", result);
    }

    [TestMethod]
    [Timeout(10000)]
    public void SymHelp_TensorOptimizationExample_SolvesAsWritten()
    {
        const string script = """
<Options>
  RulePacks: Tensor
  CostModel: Tensor
</Options>

Relu(TensorAdd(MatMul(A, B), C))
""";

        string result = _wrapper.SolveWithEGraph(script);

        AssertSolved("Tensor optimization", result);
        StringAssert.Contains(result, "FusedMatMulAddRelu(A, B, C)");
    }

    [TestMethod]
    [Timeout(10000)]
    public void SymHelp_CalculusExample_SolvesAsWritten()
    {
        const string script = """
<Options>
  RulePacks: Calculus, Algebraic
</Options>

diff(x^3 + 2*x, x)
""";

        string result = _wrapper.SolveWithEGraph(script);

        AssertSolved("Calculus", result);
    }

    private static void AssertSolved(string exampleName, string result)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(result), $"{exampleName} example returned no output.");
        Assert.IsFalse(result.StartsWith("Error:"), $"{exampleName} example failed with error: {result}");
    }
}