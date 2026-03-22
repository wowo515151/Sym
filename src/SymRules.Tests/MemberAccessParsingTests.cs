// Copyright Warren Harding 2026
using System;
using Sym.CSharpIO;
using Sym.Operations;
using Sym.Atoms;
using Xunit;

namespace SymRules.Tests
{
    public class MemberAccessParsingTests
    {
        [Fact]
        public void Parse_MemberAccess_As_DotProduct()
        {
            string src = "a.b;";
            var program = CSharpIO.ParseProgram(src);
            Assert.False(program.HasErrors);
            Assert.NotEmpty(program.Expressions);
            var expr = program.Expressions[0];
            Assert.IsType<DotProduct>(expr);
            var dot = (DotProduct)expr;
            Assert.IsType<Symbol>(dot.LeftOperand);
            Assert.IsType<Symbol>(dot.RightOperand);
            Assert.Equal("a", ((Symbol)dot.LeftOperand).Name);
            Assert.Equal("b", ((Symbol)dot.RightOperand).Name);
        }

        [Fact]
        public void Parse_ElementAccess_As_Index_Function()
        {
            string src = "arr[2];";
            var program = CSharpIO.ParseProgram(src);
            Assert.False(program.HasErrors);
            Assert.NotEmpty(program.Expressions);
            var expr = program.Expressions[0];
            Assert.IsType<Function>(expr);
            var func = (Function)expr;
            Assert.Equal("Index", func.Name);
            Assert.Equal(2, func.Arguments.Count);
            Assert.IsType<Symbol>(func.Arguments[0]);
            var arrSym = (Symbol)func.Arguments[0];
            Assert.Equal("arr", arrSym.Name);
            Assert.IsType<Sym.Atoms.Number>(func.Arguments[1]);
            var num = (Sym.Atoms.Number)func.Arguments[1];
            Assert.Equal(2m, num.Value);
        }
    }
}
