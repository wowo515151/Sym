using System;
using System.IO;
using System.Linq;
using Sym.CSharpIO;
using Xunit;

namespace SymRules.Tests
{
    public class RuleFiles_CSharpIORoundTripTests
    {
        [Fact]
        public void AllRules_RoundTripThroughCSharpIO_ParseBackWithoutErrors()
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "SymRules");
            var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.rule", SearchOption.AllDirectories) : Array.Empty<string>();

            foreach (var f in files)
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}Bad{Path.DirectorySeparatorChar}")) continue;
                var text = File.ReadAllText(f).Trim();
                var parsed = RuleTextParser.Parse(text, out var diag);
                Assert.NotNull(parsed);
                Assert.True(string.IsNullOrEmpty(diag));

                var coreRule = RuleConverter.ToCoreRule(parsed!);
                Assert.NotNull(coreRule);

                var formatted = CSharpIO.FormatRule(coreRule);
                // Ensure CSharpIO can parse the formatted rule back into a CSharpProgram
                var program = CSharpIO.ParseProgram(formatted);
                Assert.False(program.HasErrors, $"CSharpIO reported diagnostics for formatted rule from file {f}: {string.Join("; ", program.Diagnostics.Select(d=>d.Message))}");
                Assert.NotEmpty(program.Rules);
            }
        }
    }
}
