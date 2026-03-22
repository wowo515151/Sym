using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SymRules;
using Xunit;

namespace SymRules.Tests
{
    public class PackDiscoveryTests
    {
        [Fact]
        public void DefaultRulePacks_AreAvailableAndNonEmpty()
        {
            var packs = RulePackLibrary.GetRulePacks();
            var expected = new[]
            {
                "AlgebraicStrategy",
                "Trigonometry",
                "Vector",
                "MatrixStrategy",
                "Inequality",
                "Logic",
                "SpecialFunctions",
                "EquationSolving",
                "Rules",
                "Generating",
                "DifferentiationStrategy",
                "LimitStrategy",
                "IntegrationStrategy",
                "RecurrenceStrategy",
                "NumberTheoryStrategy"
            };

            foreach (var name in expected)
            {
                var pack = packs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                Assert.True(pack != null, $"Default pack '{name}' not found.");
                Assert.True(pack.Rules.Count > 0, $"Default pack '{name}' is empty.");
            }
        }

        [Fact]
        public void SourceTree_DoesNotContainPlaceholderMarkers()
        {
            var root = FindRepoRoot();
            var symRulesRoot = Path.Combine(root, "src", "SymRules");
            var files = Directory.EnumerateFiles(symRulesRoot, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

            foreach (var file in files)
            {
                var content = File.ReadAllText(file);
                Assert.DoesNotContain("PLACEHOLDER_NOTE", content);
                Assert.DoesNotContain("RulePlaceholder", content);
            }
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "SymRules");
                if (Directory.Exists(candidate)) return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Repo root not found.");
        }
    }
}
