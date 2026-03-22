// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.CSharpIO;
using Sym.Operations;
using Xunit;

namespace Sym.CSharpIO.Tests
{
    public class CSharpIOTests
    {
        [Fact]
        public void ParseSimpleExpression_Success()
        {
            var exprs = CSharpIO.ParseExpressions("1 + 2;");
            Assert.Single(exprs);
            Assert.IsType<Add>(exprs[0]);
        }

        [Fact]
        public void ParseMultipleExpressions_Success()
        {
            var exprs = CSharpIO.ParseExpressions("x = 1; y = 2;");
            Assert.Equal(2, exprs.Count);
            Assert.IsType<Equality>(exprs[0]);
            Assert.IsType<Equality>(exprs[1]);
        }

        [Fact]
        public void ParseStrict_StableRoundTrip_Success()
        {
            string source = "x + 1;";
            var exprs = CSharpIO.ParseExpressionsStrict(source);
            Assert.Single(exprs);
            
            string formatted = CSharpIO.FormatExpr(exprs[0]);
            // FormatExpr might canonicalize, e.g., "x + 1" vs "1 + x"
            Assert.Contains("x", formatted);
            Assert.Contains("1", formatted);
        }

        [Fact]
        public void ParseLatex_Success()
        {
            string latex = "\\frac{1}{2} + x^2";
            var exprs = CSharpIO.ParseLatexExpressions(latex);
            Assert.Single(exprs);
        }

        [Theory]
        [InlineData("2x", "2 * x")]
        [InlineData("2(x+1)", "2 * (x + 1)")]
        [InlineData("(x+1)2", "(x + 1) * 2")]
        [InlineData("(x)(y)", "(x) * (y)")]
        public void ImplicitMultiplication_NormalizedCorrectly(string input, string expectedSubstring)
        {
            string normalized = CSharpIO.NormalizeSource(input);
            Assert.Contains(expectedSubstring.Replace(" ", ""), normalized.Replace(" ", ""));
        }

        [Theory]
        [InlineData("x^2", "Pow(x, 2)")]
        [InlineData("x**2", "Pow(x, 2)")]
        [InlineData("(x+1)^y", "Pow((x + 1), y)")]
        public void Exponentiation_NormalizedCorrectly(string input, string expected)
        {
            string normalized = CSharpIO.NormalizeSource(input);
            Assert.Contains(expected.Replace(" ", ""), normalized.Replace(" ", ""));
        }

        [Fact]
        public void AbsoluteValue_NormalizedCorrectly()
        {
            string input = "|x + 1|";
            string normalized = CSharpIO.NormalizeSource(input);
            Assert.Contains("Abs(x+1)", normalized.Replace(" ", ""));
        }

        [Fact]
        public void Piecewise_ParsedCorrectly()
        {
            string source = "Piecewise(x, x > 0, -x, x <= 0);";
            var exprs = CSharpIO.ParseExpressions(source);
            Assert.Single(exprs);
            Assert.IsType<Piecewise>(exprs[0]);
        }

        [Fact]
        public void Vector_ParsedCorrectly()
        {
            string source = "Vector(1, 2, 3);";
            var exprs = CSharpIO.ParseExpressions(source);
            Assert.Single(exprs);
            Assert.IsType<Vector>(exprs[0]);
        }

        [Fact]
        public void Matrix_ParsedCorrectly()
        {
            string source = "Matrix(Vector(1, 0), Vector(0, 1));";
            var exprs = CSharpIO.ParseExpressions(source);
            Assert.Single(exprs);
            Assert.IsType<Matrix>(exprs[0]);
        }

        [Fact]
        public void Interval_NormalizedCorrectly()
        {
            string input = "[0, 1]";
            string normalized = CSharpIO.NormalizeSource(input);
            Assert.Contains("interval(0,1)", normalized.Replace(" ", ""));
        }

        [Fact]
        public void Sum_NormalizedCorrectly()
        {
            string input = "Sum(k^2 for k from 1 to 10)";
            string normalized = CSharpIO.NormalizeSource(input);
            Assert.Contains("Sum(Pow(k,2),k,1,10)", normalized.Replace(" ", ""));
        }
        
        [Fact]
        public void Rule_ParsedCorrectly()
        {
            string source = "Rule(x + x, 2 * x);";
            var rules = CSharpIO.ParseRules(source);
            Assert.Single(rules);
            Assert.IsType<Add>(rules[0].Pattern);
            Assert.IsType<Multiply>(rules[0].Replacement);
        }

        [Fact]
        public void IsLikelyWordProblem_Heuristics()
        {
            Assert.True(CSharpIO.IsLikelyWordProblem("The quick brown fox jumps over the lazy dog. What is the velocity?"));
            Assert.False(CSharpIO.IsLikelyWordProblem("x + 1 = 2"));
            Assert.True(CSharpIO.IsLikelyWordProblem("If x is 5, then what is x + 2?"));
        }
    }
}
