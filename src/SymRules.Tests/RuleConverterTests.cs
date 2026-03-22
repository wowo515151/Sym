// Copyright Warren Harding 2026
using System;
using Xunit;

namespace SymRules.Tests
{
    public class RuleConverterTests
    {
        [Fact]
        public void EmptyCoreSource_ReturnsNull()
        {
            var r = new SymRules.RuleDefinition { Name = "test", CoreSource = "" };
            var result = r.ToCoreRule();
            Assert.Null(result);
        }
    }
}
