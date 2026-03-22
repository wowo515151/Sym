using System;
using Sym.Atoms;
using Sym.Calculus;
using Sym.Core;
using Sym.Core.Rewriters;
using Sym.CSharpIO;
using Sym.Operations;

namespace SymRules.Calculus
{
    /// <summary>
    /// Provides a minimal, stable differentiation facade that maps simple text or expression inputs
    /// into the core symbolic derivative pipeline without returning null.
    /// </summary>
    public static class DerivativeRule
    {
        public static object Differentiate(object expr, string variable)
        {
            if (expr is null) throw new ArgumentNullException(nameof(expr));
            if (string.IsNullOrWhiteSpace(variable)) throw new ArgumentException("Variable name cannot be null or whitespace.", nameof(variable));

            IExpression expression = expr switch
            {
                IExpression e => e,
                string text => ParseExpression(text),
                _ => throw new ArgumentException("Expression must be a Sym.Core.IExpression or a string.", nameof(expr))
            };

            var variableSymbol = new Symbol(variable);
            var derivative = new Derivative(expression, variableSymbol);

            // Apply calculus differentiation rules; fall back to the raw derivative expression on failure.
            try
            {
                var result = Rewriter.RewriteFully(derivative, CalculusRules.DifferentiationRules);
                return result.RewrittenExpression;
            }
            catch
            {
                return derivative;
            }
        }

        private static IExpression ParseExpression(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException("Expression text cannot be null or whitespace.", nameof(source));
            }

            string normalized = NormalizeForCSharp(source);

            var expressions = CSharpIO.ParseExpressions(normalized);
            if (expressions is null || expressions.Count == 0)
            {
                throw new InvalidOperationException("Unable to parse expression text into a symbolic expression.");
            }

            return expressions[0];
        }

        internal static string NormalizeForCSharp(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;

            // Unicode substitutions and simple caret exponent handling.
            s = s.Replace("×", "*").Replace("÷", "/").Replace("–", "-").Replace("—", "-");

            // Replace simple a^b with Pow(a,b)
            s = System.Text.RegularExpressions.Regex.Replace(
                s,
                @"(?<base>[0-9A-Za-z\)\]]+)\s*\^\s*(?<exp>[0-9A-Za-z\(\[]+)",
                m => $"Pow({m.Groups["base"].Value},{m.Groups["exp"].Value})");

            // Insert explicit multiplication in a few common cases.
            // 1) 2(x+1) or )( : digit/close-paren/close-bracket before '('
            s = System.Text.RegularExpressions.Regex.Replace(
                s,
                @"(?<a>(?:\d|\)|\]))\s*(?=\()",
                "${a}*");

            // 2) x(x+1) : single-letter symbol immediately followed by '('
            // Avoid treating function calls like Pow(x,2) as implicit multiplication.
            s = System.Text.RegularExpressions.Regex.Replace(
                s,
                @"(?<![A-Za-z0-9_])(?<a>[A-Za-z])\s*(?=\()",
                "${a}*");
            s = System.Text.RegularExpressions.Regex.Replace(
                s,
                @"(?<=\))\s*(?=[A-Za-z0-9(])",
                "*");

            return s.Trim();
        }
    }
}
