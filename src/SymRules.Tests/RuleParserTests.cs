// Copyright Warren Harding 2026
using Xunit;
using System.IO;
using SymRules;
using System;
using System.Linq;
namespace SymRules.Tests
{
    public class RuleParserTests
    {
        [Fact]
        public void ReadsRuleFile()
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(temp);
            var file = Path.Combine(temp, "sample.rule.txt");
            File.WriteAllText(file, "a + b = b + a\n");
            var rules = RuleParser.ParseRulesFromDirectory(temp);
            Assert.Contains(rules, r => r == "a + b = b + a");
        }
    }
}
