// Copyright Warren Harding 2026
#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
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
        [DynamicData(nameof(GetHomeDropdownExampleFiles), DynamicDataSourceType.Method)]
        public void ValidateExampleScript(string exampleFileName)
        {
            string baseDir = AppContext.BaseDirectory;
            string? projectRoot = FindProjectRoot(baseDir);

            Assert.IsNotNull(projectRoot, "Could not find project root directory.");

            string examplePath = Path.Combine(projectRoot, "SymBlazor", "wwwroot", "sym", "examples", exampleFileName);

            Assert.IsTrue(File.Exists(examplePath), $"Example file not found at: {examplePath}");

            string script = ApplyBlazorDefaultRulePacks(File.ReadAllText(examplePath));
            string result = _wrapper.SolveWithEGraph(script);

            Assert.IsFalse(result.StartsWith("Error:"), $"Example '{exampleFileName}' failed with error:\n{result}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result), $"Example '{exampleFileName}' returned an empty result.");
            
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

        public static IEnumerable<object[]> GetHomeDropdownExampleFiles()
        {
            string baseDir = AppContext.BaseDirectory;
            string? projectRoot = FindProjectRoot(baseDir);

            if (projectRoot is null)
            {
                throw new DirectoryNotFoundException("Could not locate project root for example discovery.");
            }

            string manifestPath = Path.Combine(projectRoot, "SymBlazor", "wwwroot", "sym", "examples", "examples.json");
            string manifestJson = File.ReadAllText(manifestPath);
            var exampleFiles = JsonSerializer.Deserialize<List<string>>(manifestJson) ?? new List<string>();

            return exampleFiles.Select(file => new object[] { file });
        }

        private static string ApplyBlazorDefaultRulePacks(string script)
        {
            const string blazorDefaultRulePacks = "AlgebraicStrategy, EquationSolving";
            Regex rulePacksOptionRegex = new(@"^\s*(?:@option\s+)?RulePacks\s*[:=]", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            Regex xmlOptionsBlockRegex = new(@"<Options>(.*?)</Options>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (string.IsNullOrWhiteSpace(script) || rulePacksOptionRegex.IsMatch(script))
            {
                return script;
            }

            string lineEnding = script.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            Match xmlOptionsMatch = xmlOptionsBlockRegex.Match(script);
            if (xmlOptionsMatch.Success)
            {
                string optionsContent = xmlOptionsMatch.Groups[1].Value;
                string trimmedOptions = optionsContent.TrimEnd();
                string updatedOptions = string.IsNullOrWhiteSpace(trimmedOptions)
                    ? $"{lineEnding}  RulePacks: {blazorDefaultRulePacks}{lineEnding}"
                    : $"{lineEnding}{trimmedOptions}{lineEnding}  RulePacks: {blazorDefaultRulePacks}{lineEnding}";

                return script[..xmlOptionsMatch.Groups[1].Index] +
                       updatedOptions +
                       script[(xmlOptionsMatch.Groups[1].Index + xmlOptionsMatch.Groups[1].Length)..];
            }

            string trimmedScript = script.TrimStart();
            return $"<Options>{lineEnding}  RulePacks: {blazorDefaultRulePacks}{lineEnding}</Options>{lineEnding}{lineEnding}{trimmedScript}";
        }

        private static string? FindProjectRoot(string startDir)
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
