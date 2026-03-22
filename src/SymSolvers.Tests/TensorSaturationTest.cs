#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using WordsToSym;

namespace SymSolvers.Tests
{
    [TestClass]
    public class TensorSaturationTest
    {
        private ProblemScriptEGraphWrapper _wrapper = null!;

        [TestInitialize]
        public void Setup()
        {
            _wrapper = new ProblemScriptEGraphWrapper();
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestTensorSaturationIdentity()
        {
            string baseDir = AppContext.BaseDirectory;
            string? projectRoot = FindProjectRoot(baseDir);
            Assert.IsNotNull(projectRoot, "Could not find project root directory.");
            
            string examplePath = Path.Combine(projectRoot, "SymBlazor", "wwwroot", "examples", "TensorSaturationIdentity.txt");
            Assert.IsTrue(File.Exists(examplePath), $"Example file not found at: {examplePath}");
            
            string script = File.ReadAllText(examplePath);
            string result = _wrapper.SolveWithEGraph(script);
            
            Assert.IsFalse(result.StartsWith("Error:"), $"TensorSaturationIdentity failed with error:\n{result}");
            
            // We expect the expression to be simplified.
            // Factoring (MatMul(A, B) + MatMul(A, C) -> MatMul(A, B+C)) should happen.
            // Transpose/Relu commutation might happen.
            
            // The result should contain MatMul(A, TensorAdd(B, C)) in some form,
            // or the transposed/commuted version.
            
            // Given the cost model, Relu(Transpose(MatMul(A, TensorAdd(B, C)))) is a likely candidate.
            // Or Transpose(Relu(MatMul(A, TensorAdd(B, C)))).
            
            StringAssert.Contains(result, "TensorAdd(B, C)", "Result should have factored B and C.");
            StringAssert.Contains(result, "MatMul(A", "Result should have A as a multiplier.");
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
