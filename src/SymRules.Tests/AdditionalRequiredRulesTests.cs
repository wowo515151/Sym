// Copyright Warren Harding 2026

using System;

using System.IO;

using Xunit;



namespace SymRules.Tests

{

    public class AdditionalRequiredRulesTests

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

        public void Trigonometry_Vector_Matrix_RulesExist()

        {

            var folder = FindRulesFolder();

            Assert.True(File.Exists(Path.Combine(folder, "Trigonometry", "sin.rule")), "sin.rule missing");

            Assert.True(File.Exists(Path.Combine(folder, "Vector", "dot.rule")), "dot.rule missing");

            Assert.True(File.Exists(Path.Combine(folder, "Matrix", "identity.rule")), "identity.rule missing");

        }

    }

}

