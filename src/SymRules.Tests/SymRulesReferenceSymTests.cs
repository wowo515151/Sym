// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SymRules.Tests
{
    public class SymRulesReferenceSymTests
    {
        [Fact]
        public void SymRules_ProjectReferences_Sym_Project()
        {
            // Search upward for repository root then locate SymRules.csproj under src\SymRules
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            DirectoryInfo repoRoot = dir;
            while (repoRoot != null && !Directory.Exists(Path.Combine(repoRoot.FullName, "src"))) repoRoot = repoRoot.Parent;
            var rootPath = repoRoot?.FullName ?? dir.FullName;
            var matches = Directory.GetFiles(rootPath, "SymRules.csproj", SearchOption.AllDirectories)
                .Where(p => p.Replace('/', '\\').Contains("\\src\\SymRules\\")).ToArray();
            Assert.True(matches.Length > 0, $"SymRules.csproj not found under src in repository rooted at {rootPath}");
            var projPath = matches[0];
            var doc = XDocument.Load(projPath);
            var hasProjRef = doc.Descendants("ProjectReference").Any(e =>
                (e.Attribute("Include")?.Value ?? string.Empty).Replace('/', '\\').Contains("..\\Sym\\Sym.csproj"));
            Assert.True(hasProjRef, "SymRules.csproj does not reference ..\\Sym\\Sym.csproj");
        }
    }
}
