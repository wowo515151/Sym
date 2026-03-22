#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using WordsToSym;

namespace SymSolvers.Tests
{
    [TestClass]
    public class ExampleScriptValidationTests
    {
        private ProblemScriptEGraphWrapper _wrapper = null!;

        [TestInitialize]
        public void Setup()
        {
            _wrapper = new ProblemScriptEGraphWrapper();
        }

        [TestMethod]
        [Timeout(10000)]
        [DataRow("NumericEvaluation.txt")]
        [DataRow("SolveForTarget.txt")]
        [DataRow("AlgebraicSimplification.txt")]
        [DataRow("CalculusQuery.txt")]
        [DataRow("InlineCustomRule.txt")]
        [DataRow("TrigSaturation.txt")]
        [DataRow("TensorOptimization.txt")]
        public void ValidateExampleScript(string exampleFileName)
        {
            // Find the examples directory relative to the solution root
            string baseDir = AppContext.BaseDirectory;
            string? projectRoot = FindProjectRoot(baseDir);
            
            Assert.IsNotNull(projectRoot, "Could not find project root directory.");
            
            string examplePath = Path.Combine(projectRoot, "SymBlazor", "wwwroot", "examples", exampleFileName);
            
            Assert.IsTrue(File.Exists(examplePath), $"Example file not found at: {examplePath}");
            
            string script = File.ReadAllText(examplePath);
            string result = _wrapper.SolveWithEGraph(script);
            
            Assert.IsFalse(result.StartsWith("Error:"), $"Example '{exampleFileName}' failed with error:\n{result}");
            
            if (exampleFileName == "TensorOptimization.txt")
            {
                StringAssert.Contains(result, "FusedMatMulAddRelu", "TensorOptimization should produce a fused operation.");
            }
            else if (exampleFileName == "SolveForTarget.txt")
            {
                StringAssert.Contains(result, "x = 5", "SolveForTarget should solve x = 5.");
            }
            else if (exampleFileName == "AlgebraicSimplification.txt")
            {
                Assert.AreEqual("0", result.Trim(), "AlgebraicSimplification should simplify to 0.");
            }
            else if (exampleFileName == "CalculusQuery.txt")
            {
                StringAssert.Contains(result, "cos", "CalculusQuery should simplify to cos(x).");
            }
            else if (exampleFileName == "InlineCustomRule.txt")
            {
                Assert.IsTrue(result.Contains("y = 25") || result.Contains("25 = y"), "InlineCustomRule should solve y = 25.");
            }
            else if (exampleFileName == "NumericEvaluation.txt")
            {
                Assert.AreEqual("7", result.Trim(), "NumericEvaluation should simplify to 7.");
            }
            else if (exampleFileName == "TrigSaturation.txt")
            {
                Assert.AreEqual("1", result.Trim(), "TrigSaturation should simplify to 1.");
            }
        }

        private string? FindProjectRoot(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Sym.sln")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return null;
        }
    }
}
