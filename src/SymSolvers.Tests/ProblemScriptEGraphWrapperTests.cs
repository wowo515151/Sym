// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Threading;
using WordsToSym;

namespace SymSolvers.Tests
{
    [TestClass]
    public class ProblemScriptEGraphWrapperTests
    {
        private ProblemScriptEGraphWrapper _wrapper;

        [TestInitialize]
        public void Setup()
        {
            _wrapper = new ProblemScriptEGraphWrapper();
        }

        [TestMethod]
        [Timeout(10000)]
        public void SolveWithEGraph_TensorOptimization_FusesOps()
        {
            // Note: Tensor rule pack must be available in src/SymRules/Tensor
            string script = @"
<Options>
  RulePacks: Tensor
  CostModel: Tensor
</Options>
Relu(TensorAdd(MatMul(A, B), C))
";
            string result = _wrapper.SolveWithEGraph(script);
            
            Assert.IsFalse(result.StartsWith("Error:"), $"Wrapper returned error: {result}");
            Assert.IsTrue(result.Contains("FusedMatMulAddRelu(A, B, C)"), $"Expected fused op but got: {result}");
        }

        [TestMethod]
        [Timeout(10000)]
        public void SolveWithEGraph_InlineRule_Works()
        {
            string script = @"
<Rules>
  Rule(f(Wild(""x"")), Pow(Wild(""x""), 2));
</Rules>
f(5) = y
";
            string result = _wrapper.SolveWithEGraph(script);
            
            // Should simplify f(5) to 25 and return y = 25 (via inference of target y)
            Assert.IsTrue(result.Contains("y = 25") || result.Contains("25 = y"), $"Expected simplified equation but got: {result}");
        }

        [TestMethod]
        [Timeout(10000)]
        public void SolveWithEGraph_TargetVariable_Works()
        {
            string script = @"
<Options>
  Target: x
</Options>
x + 5 = 10
";
            string result = _wrapper.SolveWithEGraph(script);
            
            Assert.IsTrue(result.Contains("x = 5"), $"Expected x = 5 but got: {result}");
        }

        [TestMethod]
        [Timeout(10000)]
        public void SolveWithEGraph_MixedConstraints_DefaultPolicy()
        {
            string script = @"
x = 5
gt(x, 0)
";
            // Default policy: solve only equalities in EGraph.
            // Result should be x = 5 (simplified form of the equality, via inference of target x)
            string result = _wrapper.SolveWithEGraph(script);
            
            Assert.IsFalse(result.StartsWith("Error:"), $"Wrapper returned error: {result}");
            Assert.IsTrue(result.Contains("x = 5"), $"Expected x = 5 but got: {result}");
        }

        [TestMethod]
        [Timeout(10000)]
        public void SolveWithEGraph_TensorSystem_SolvesForX()
        {
            // This test uses the example file we just created
            string examplePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "examples", "TensorSystem.txt");
            
            // If running in tests, the path might be different. Let's try to find it.
            if (!File.Exists(examplePath))
            {
                // Fallback for different test execution environments
                examplePath = "../../../../SymBlazor/wwwroot/examples/TensorSystem.txt";
            }
            
            string script;
            if (File.Exists(examplePath))
            {
                 script = File.ReadAllText(examplePath);
            }
            else
            {
                // Absolute fallback if file not found
                script = @"
<Options>
  RulePacks: Tensor, EquationSolving
  CostModel: Tensor
  Target: x
</Options>
MatMul(A, x) = B
MatMul(C, x) = D
";
            }

            string result = _wrapper.SolveWithEGraph(script);

            Assert.IsFalse(result.StartsWith("Error:"), $"Wrapper returned error: {result}");
            // It should find either A^-1 * B or C^-1 * D
            Assert.IsTrue(result.Contains("MatMul(inverse(A), B)") || result.Contains("MatMul(inverse(C), D)"), $"Expected solved x but got: {result}");
        }

        [TestMethod]
        [Timeout(10000)]
        public void SolveWithEGraph_ComplexTensorSaturation_OptimizesCorrectly()
        {
            string examplePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "examples", "ComplexTensorSaturation.txt");
            if (!File.Exists(examplePath))
            {
                examplePath = "../../../../SymBlazor/wwwroot/examples/ComplexTensorSaturation.txt";
            }

            string script;
            if (File.Exists(examplePath))
            {
                script = File.ReadAllText(examplePath);
            }
            else
            {
                script = @"
<Options>
  RulePacks: Tensor
  CostModel: Tensor
  MaxIterations: 200
</Options>
Relu(Transpose(Transpose(TensorAdd(MatMul(A, B), TensorAdd(MatMul(A, C), TensorAdd(MatMul(D, E), MatMul(D, G)))))))
";
            }

            string result = _wrapper.SolveWithEGraph(script);

            Assert.IsFalse(result.StartsWith("Error:"), $"Wrapper returned error: {result}");
            
            // The result should be significantly simplified. 
            // We expect double Transpose to be gone.
            Assert.IsFalse(result.Contains("Transpose"), $"Result still contains Transpose: {result}");
            
            // We expect factoring and fusion to occur.
            // Current rules result in Relu(FusedMatMulAdd(D, TensorAdd(E, G), MatMul(A, TensorAdd(B, C))))
            // which is better than the original nested mess.
            
            Assert.IsTrue(result.Contains("FusedMatMulAdd"), $"Expected FusedMatMulAdd fusion but got: {result}");
            Assert.IsTrue(result.Contains("TensorAdd(B, C)") || result.Contains("TensorAdd(C, B)"), $"Expected B+C factoring but got: {result}");
            Assert.IsTrue(result.Contains("TensorAdd(E, G)") || result.Contains("TensorAdd(G, E)"), $"Expected E+G factoring but got: {result}");
            Assert.IsTrue(result.Contains("Relu"), $"Expected Relu but got: {result}");
        }

        [TestMethod]
        [Timeout(10000)]
        public void SolveWithEGraph_TrigSaturation_SimplifiesToOne()
        {
            string examplePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "examples", "TrigSaturation.txt");
            if (!File.Exists(examplePath))
            {
                examplePath = "../../../../SymBlazor/wwwroot/examples/TrigSaturation.txt";
            }

            string script;
            if (File.Exists(examplePath))
            {
                script = File.ReadAllText(examplePath);
            }
            else
            {
                script = @"
<Options>
  RulePacks: Trigonometry, AlgebraicStrategy
  MaxIterations: 50
</Options>
sin(x)^2 + cos(x)^2
";
            }

            string result = _wrapper.SolveWithEGraph(script);

            Assert.IsFalse(result.StartsWith("Error:"), $"Wrapper returned error: {result}");
            Assert.AreEqual("1", result.Trim(), $"Expected 1 but got: {result}");
        }
    }
}
