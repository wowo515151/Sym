// Copyright Warren Harding 2026
using System;
using System.IO;
using Xunit;

namespace SymRules.Tests
{
    public class RequiredRulesForSolversTests
    {
        private static string FindRulesFolder()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "SymRules"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "SymRules"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "src", "SymRules")
            };
            foreach (var c in candidates)
            {
                var p = Path.GetFullPath(c);
                if (Directory.Exists(p)) return p;
            }

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "SymRules"));
        }

        [Fact]
        public void Calculus_RuleFilesExist()
        {
            var folder = FindRulesFolder();
            Assert.True(File.Exists(Path.Combine(folder, "Calculus", "differentiation.rule")), "differentiation.rule missing");
            Assert.True(File.Exists(Path.Combine(folder, "Calculus", "pack.json")), "pack.json missing");
        }

        [Fact]
        public void Algebraic_RuleFilesExist()
        {
            var folder = FindRulesFolder();
            Assert.True(File.Exists(Path.Combine(folder, "Algebraic", "algebraic.rule")), "algebraic.rule missing");
            Assert.True(File.Exists(Path.Combine(folder, "Algebraic", "sample.rule")), "sample.rule missing");
        }
    }
}
