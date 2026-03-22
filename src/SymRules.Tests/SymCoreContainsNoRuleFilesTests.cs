// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SymRules.Tests
{
    public class SymCoreContainsNoRuleFilesTests
    {
        [Fact]
        public void SymDirectoryContainsNoRuleFiles()
        {
            // Locate repository root by walking up until a "src" directory is found
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                dir = dir.Parent;
            }

            if (dir == null)
            {
                // Can't locate repo root; fail the test to surface environment issue
                Assert.True(false, "Could not locate repository root from test runtime directory.");
            }

            var symPath = Path.Combine(dir.FullName, "src", "Sym");
            if (!Directory.Exists(symPath))
            {
                // If Sym doesn't exist treat as unchanged (pass) since nothing to modify there
                Assert.True(true);
                return;
            }

            var files = Directory.EnumerateFiles(symPath, "*", SearchOption.AllDirectories).ToList();

            // Fail if any non-source rule files (i.e., .rule files) are placed under src\Sym
            var offending = files.Where(f => f.EndsWith(".rule", StringComparison.OrdinalIgnoreCase)
                                             || f.EndsWith(".rule.txt", StringComparison.OrdinalIgnoreCase)).ToList();

            Assert.True(offending.Count == 0, "Found rule-like files under src\\Sym: " + string.Join(", ", offending.Select(Path.GetFileName)));
        }
    }
}
