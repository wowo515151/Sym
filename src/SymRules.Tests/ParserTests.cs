using System;
using System.Diagnostics;
using System.Linq;
using SymRules;
namespace SymRules.Tests
{
    public static class ParserTests
    {
        // Simple smoke method (not using a test framework yet) to validate loading
        public static void SmokeLoad()
        {
            var folder = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "SymRules", "Algebraic");
            var rules = RuleLoader.LoadRules(folder).ToList();
            Debug.Assert(rules.Count > 0, "Expected at least one rule to be loaded from sample.rule");
        }
    }
}

