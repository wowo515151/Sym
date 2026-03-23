// Copyright Warren Harding 2026
#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using SymSolvers;
using WordsToSym;

namespace SymSolvers.Tests
{
    [TestClass]
    public class BlazorDemoExampleTests
    {
        private ProblemScriptEGraphWrapper _wrapper = null!;

        [TestInitialize]
        public void Setup()
        {
            _wrapper = new ProblemScriptEGraphWrapper();
        }

        [TestMethod]
        [Timeout(15000)]
        [DynamicData(nameof(GetAllDemoExamples), DynamicDataSourceType.Method)]
        public void DemoDropdownExampleSolves(string category, string name, string script, string? expectedSubstring)
        {
            string result = _wrapper.SolveWithEGraph(script);

            Assert.IsFalse(result.StartsWith("Error:"), $"{category} example '{name}' failed with error:\n{result}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result), $"{category} example '{name}' returned an empty result.");

            if (!string.IsNullOrWhiteSpace(expectedSubstring))
            {
                StringAssert.Contains(result, expectedSubstring, $"{category} example '{name}' did not contain expected marker '{expectedSubstring}'. Result:\n{result}");
            }
        }

        public static IEnumerable<object[]> GetAllDemoExamples()
        {
            return EnumerateExamples("Derivative", BlazorDemoCatalog.DerivativeExamples)
                .Concat(EnumerateExamples("Rewrite", BlazorDemoCatalog.RewriteExamples))
                .Concat(EnumerateExamples("Tensor", BlazorDemoCatalog.TensorExamples));
        }

        private static IEnumerable<object[]> EnumerateExamples(string category, IReadOnlyList<BlazorDemoExample> examples)
        {
            return examples.Select(example => new object[] { category, example.Name, example.Script, example.ExpectedSubstring });
        }
    }
}
