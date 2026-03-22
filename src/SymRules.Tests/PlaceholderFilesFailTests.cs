using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SymRules.Tests
{
    public class PlaceholderFilesFailTests
    {
        [Fact]
        public void NoPlaceholderRuleFilesExist()
        {
            var rulesDir = FindRulesDir();
            Assert.True(Directory.Exists(rulesDir), $"Expected rules dir at {rulesDir}");
            var files = Directory.EnumerateFiles(rulesDir, "*placeholder*", SearchOption.AllDirectories).ToList();
            Assert.Empty(files);

            static string FindRulesDir()
            {
                var di = new DirectoryInfo(AppContext.BaseDirectory);
                while (di is not null)
                {
                    var candidateFromSrc = Path.Combine(di.FullName, "SymRules", "Rules");
                    if (Directory.Exists(candidateFromSrc)) return Path.GetFullPath(candidateFromSrc);

                    var candidateFromRepoRoot = Path.Combine(di.FullName, "src", "SymRules", "Rules");
                    if (Directory.Exists(candidateFromRepoRoot)) return Path.GetFullPath(candidateFromRepoRoot);

                    di = di.Parent;
                }

                // Fallback: typical layout when base dir is under <repo>\src\SymRules.Tests\bin\...
                return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SymRules", "Rules"));
            }
        }
    }
}
