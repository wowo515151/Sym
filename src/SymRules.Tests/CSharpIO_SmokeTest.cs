// Copyright Warren Harding 2026
using System;
using Sym.CSharpIO;
using Xunit;

namespace SymRules.Tests
{
    public class CSharpIO_SmokeTest
    {
        [Fact]
        public void ParseSimpleExpression_NoErrors()
        {
            string src = "1 + 2;";
            var program = CSharpIO.ParseProgram(src);
            Assert.False(program.HasErrors);
            Assert.NotEmpty(program.Expressions);
        }
    }
}
