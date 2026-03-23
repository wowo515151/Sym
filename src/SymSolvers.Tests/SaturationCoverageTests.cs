// Copyright Warren Harding 2026
#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using WordsToSym;

namespace SymSolvers.Tests
{
    [TestClass]
    public class SaturationCoverageTests
    {
        private ProblemScriptEGraphWrapper _wrapper = null!;

        [TestInitialize]
        public void Setup()
        {
            _wrapper = new ProblemScriptEGraphWrapper();
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestTensorDoubleFactoringSaturation()
        {
            string baseDir = AppContext.BaseDirectory;
            string? projectRoot = FindProjectRoot(baseDir);
            Assert.IsNotNull(projectRoot, "Could not find project root directory.");

            string examplePath = FindExamplePath(projectRoot, "TensorSaturationSaturation.txt");
            Assert.IsTrue(File.Exists(examplePath), $"Example file not found at: {examplePath}");
            
            string script = File.ReadAllText(examplePath);
            string result = _wrapper.SolveWithEGraph(script);
            
            Assert.IsFalse(result.StartsWith("Error:"), $"TensorSaturationSaturation failed with error:\n{result}");
            
            // Expected: MatMul(TensorAdd(A, D), TensorAdd(B, C))
            // The order in MatMul might vary depending on rules, but both are TensorAdd.
            
            StringAssert.Contains(result, "MatMul", "Result should contain MatMul.");
            StringAssert.Contains(result, "TensorAdd(A, D)", "Result should have factored (A + D).");
            StringAssert.Contains(result, "TensorAdd(B, C)", "Result should have factored (B + C).");
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

        private static string FindExamplePath(string projectRoot, string fileName)
        {
            string[] candidates =
            {
                Path.Combine(projectRoot, "src", "SymBlazor", "wwwroot", "sym", "examples", fileName),
                Path.Combine(projectRoot, "src", "SymBlazor", "wwwroot", "examples", fileName),
                Path.Combine(projectRoot, "SymBlazor", "wwwroot", "sym", "examples", fileName),
                Path.Combine(projectRoot, "SymBlazor", "wwwroot", "examples", fileName)
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates[0];
        }
    }
}
