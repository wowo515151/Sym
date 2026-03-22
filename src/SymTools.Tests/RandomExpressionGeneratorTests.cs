using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.CSharpIO;
using Sym.Core;
using SymSolvers;

namespace SymTools.Tests;

[TestClass]
public sealed class RandomExpressionGeneratorTests
{
    private static readonly Regex NumericLiteralPattern = new(@"(?<![A-Za-z_\.])\d+(?:\.\d+)?", RegexOptions.Compiled);

    [TestMethod]
    [Timeout(30000)]
    public async Task NumericExpressions_Match_RoslynScript()
    {
        var generator = new RandomExpressionGenerator(seed: 1729);
        var options = new RandomExpressionGeneratorOptions
        {
            MaxDepth = 4,
            MinLiteralValue = -9,
            MaxLiteralValue = 9,
            IncludeVariables = false,
            AllowDivision = true,
            LeafProbability = 0.35,
        };

        var scriptOptions = ScriptOptions.Default;

        for (int i = 0; i < 10; i++)
        {
            string expressionText = generator.GenerateExpression(options).ToDisplayString();
            var parsed = CSharpIO.ParseExpressionsStrict(expressionText);

            Assert.AreEqual(1, parsed.Count, $"Expected a single expression for '{expressionText}'.");
            Assert.IsTrue(
                NumericEvaluator.TryEvaluate(parsed[0], ImmutableDictionary<string, decimal>.Empty, out decimal symValue, out string? symError),
                $"Sym failed to evaluate '{expressionText}': {symError}");

            string roslynExpression = ConvertToDecimalExpression(expressionText);
            decimal roslynValue;
            try
            {
                roslynValue = await CSharpScript.EvaluateAsync<decimal>(roslynExpression, scriptOptions);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Roslyn failed to evaluate '{expressionText}' as '{roslynExpression}': {ex.Message}");
                return;
            }

            Assert.AreEqual(
                roslynValue,
                symValue,
                $"Mismatch for expression '{expressionText}'. Roslyn='{roslynValue}', Sym='{symValue}'.");
        }
    }

    private static string ConvertToDecimalExpression(string expression)
    {
        return NumericLiteralPattern.Replace(expression, match => match.Value + "m");
    }
}