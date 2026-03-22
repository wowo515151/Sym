using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymCore;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Sym.CSharpIO
{
    public static class CSharpIO
    {
        public static CSharpProgram ParseProgram(string source)
        {
            Parser parser = new Parser();
            return parser.ParseProgram(source);
        }

        public static IReadOnlyList<IExpression> ParseExpressions(string source)
        {
            CSharpProgram program = ParseProgram(source);
            return program.Expressions;
        }

        /// <summary>
        /// Parses expressions and enforces a strict, canonical round-trip.
        /// The input must parse without errors, and the formatted output must be stable under re-parse + re-format
        /// (whitespace-insensitive). This guards against partial parses while allowing canonicalization to reorder
        /// or rewrite equivalent forms.
        /// </summary>
        public static IReadOnlyList<IExpression> ParseExpressionsStrict(string source)
        {
            var program = ParseProgram(source);
            if (program.HasErrors)
            {
                Logging.LogError("CSharpIOParseExpressionsStrict", "Strict parse failed: input contains parse errors.", string.Join("; ", program.Diagnostics.Select(d => d.Message)) + "\nSource:\n" + source);
                throw new InvalidOperationException("Strict parse failed: input contains parse errors.");
            }

            var exprs = program.Expressions;
            var formatted1 = string.Join(";", exprs.Select(FormatExpr));

            // Ensure the formatted output is stable under re-parse + re-format.
            var roundTrip = ParseProgram(formatted1);
            if (roundTrip.HasErrors)
            {
                Logging.LogError("CSharpIOParseExpressionsStrictRoundTrip", "Strict parse failed: formatted output does not re-parse cleanly.", string.Join("; ", roundTrip.Diagnostics.Select(d => d.Message)) + "\nFormatted:\n" + formatted1);
                throw new InvalidOperationException("Strict parse failed: formatted output does not re-parse cleanly.");
            }

            var formatted2 = string.Join(";", roundTrip.Expressions.Select(FormatExpr));
            if (!Normalize(formatted1).Equals(Normalize(formatted2), StringComparison.Ordinal))
            {
                Logging.LogError("CSharpIOParseExpressionsStrictStability", "Strict parse failed: input does not round-trip cleanly.", $"Formatted1: {formatted1}\nFormatted2: {formatted2}");
                throw new InvalidOperationException("Strict parse failed: input does not round-trip cleanly.");
            }

            return exprs;
        }

        /// <summary>
        /// Parses LaTeX-like math by converting it to C#-style expressions before parsing.
        /// The conversion step may change syntactic form (e.g., add parentheses); this method validates that the
        /// converted input parses without errors.
        /// </summary>
        public static IReadOnlyList<IExpression> ParseLatexExpressions(string latex)
        {
            var converted = MathSyntax.FromLatex(latex);

            var program = ParseProgram(converted);
            if (program.HasErrors)
            {
                Logging.LogError("CSharpIOParseLatexExpressions", "Latex parse failed.", string.Join("; ", program.Diagnostics.Select(d => d.Message)) + "\nLatex:\n" + latex + "\nConverted:\n" + converted);
                throw new InvalidOperationException(string.Join(
                    Environment.NewLine,
                    program.Diagnostics.Select(d => $"{d.Severity}: {d.Message}")));
            }

            return program.Expressions;
        }

        public static IReadOnlyList<Rule> ParseRules(string source)
        {
            CSharpProgram program = ParseProgram(source);
            return program.Rules;
        }

        public static string FormatExpr(IExpression expr)
        {
            if (expr is null)
            {
                throw new ArgumentNullException(nameof(expr));
            }
            return Formatter.FormatExpression(expr.Canonicalize(), 0);
        }

        public static string FormatRule(Rule rule)
        {
            if (rule is null)
            {
                throw new ArgumentNullException(nameof(rule));
            }
            string pattern = FormatExpr(rule.Pattern);
            string replacement = FormatExpr(rule.Replacement);
            return $"Rule({pattern}, {replacement})";
        }

        public static string FormatProgram(CSharpProgram program)
        {
            if (program is null)
            {
                throw new ArgumentNullException(nameof(program));
            }

            List<string> parts = new List<string>();
            foreach (Rule rule in program.Rules)
            {
                parts.Add($"{FormatRule(rule)};");
            }
            foreach (IExpression expr in program.Expressions)
            {
                parts.Add($"{FormatExpr(expr)};");
            }
            return string.Join(Environment.NewLine, parts);
        }

        public static string RunAndFormat(
            string source,
            Func<CSharpProgram, IEnumerable<IExpression>> runner)
        {
            if (runner is null)
            {
                throw new ArgumentNullException(nameof(runner));
            }

            CSharpProgram program = ParseProgram(source);
            if (program.HasErrors)
            {
                return string.Join(
                    Environment.NewLine,
                    program.Diagnostics.Select(d => $"{d.Severity}: {d.Message}"));
            }

            IEnumerable<IExpression> results = runner(program);
            List<IExpression> output = results?.ToList() ?? new List<IExpression>();

            CSharpProgram formattedProgram = new CSharpProgram(
                output,
                Array.Empty<Rule>(),
                program.Diagnostics);

            return FormatProgram(formattedProgram);
        }

        public static string NormalizeSource(string source)
        {
            return Parser.NormalizeSource(source);
        }

        public static bool IsLikelyWordProblem(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            // Heuristic: If it contains many spaces, it's likely a sentence rather than a math expression.
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 8) return true;

            // If it contains characters not common in math expressions but common in English sentences.
            if (text.Contains('?') || text.Contains('!'))
                return true;

            // If it contains many lowercase words that aren't common math functions
            int nonMathWordCount = 0;
            var mathWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "sin", "cos", "tan", "log", "exp", "pow", "sqrt", "abs", "sum", "product",
                "forall", "exists", "and", "or", "not", "if", "then", "else", "matrix", "vector",
                "derivative", "integral", "grad", "div", "curl", "wild", "scalar", "constant",
                "pi", "e", "i", "j", "x", "y", "z", "t", "a", "b", "c", "d", "n", "k", "m", "int", "integer"
            };

            foreach (var word in words)
            {
                var cleanWord = new string(word.Where(char.IsLetter).ToArray());
                if (cleanWord.Length > 1 && !mathWords.Contains(cleanWord))
                {
                    nonMathWordCount++;
                }
            }

            return nonMathWordCount > 3;
        }

        private static string Normalize(string text)
        {
            if (text is null) return string.Empty;
            return new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray()).TrimEnd(';');
        }
    }

    internal sealed class Parser
    {
        public CSharpProgram ParseProgram(string source)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            List<IExpression> expressions = new();
            List<Rule> rules = new();
            List<CSharpDiagnostic> diagnostics = new();

            source = NormalizeSource(source);
            CSharpSyntaxTree tree = (CSharpSyntaxTree)CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(kind: SourceCodeKind.Script));
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

            foreach (Diagnostic diagnostic in tree.GetDiagnostics())
            {
                diagnostics.Add(ConvertDiagnostic(diagnostic));
            }

            foreach (GlobalStatementSyntax statement in root.Members.OfType<GlobalStatementSyntax>())
            {
                ParseStatement(tree, statement.Statement, expressions, rules, diagnostics);
            }

            return new CSharpProgram(expressions, rules, diagnostics);
        }

        internal static string NormalizeSource(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }

            // Strip line comments line-by-line to be robust against different newline types
            var lines = s.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                int commentIdx = lines[i].IndexOf("//");
                if (commentIdx >= 0) lines[i] = lines[i].Substring(0, commentIdx);
            }
            s = string.Join(Environment.NewLine, lines);

            // Strip block comments
            s = System.Text.RegularExpressions.Regex.Replace(s, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);

            s = s
                .Replace('\u2013', '-') // en dash
                .Replace('\u2014', '-') // em dash
                .Replace('\u2011', '-') // non-breaking hyphen
                .Replace('\u2212', '-') // minus sign
                .Replace('\u2018', '\'') // left single quotation mark
                .Replace('\u2019', '\'') // right single quotation mark
                .Replace('\u201C', '\"') // left double quotation mark
                .Replace('\u201D', '\"') // right double quotation mark
                .Replace("\u2026", "..."); // ellipsis

            // Handle Unicode roots
            s = s.Replace("∛", "Pow(").Replace("∜", "Pow("); // Note: this is a bit too simple, need to handle arguments
            // Actually, a better way:
            s = System.Text.RegularExpressions.Regex.Replace(s, @"∛\s*(\d+(?:\.\d+)?|[A-Za-z_][A-Za-z0-9_]*|\(.+?\))", "Pow($1, 1/3)");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"∜\s*(\d+(?:\.\d+)?|[A-Za-z_][A-Za-z0-9_]*|\(.+?\))", "Pow($1, 1/4)");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"√\s*(\d+(?:\.\d+)?|[A-Za-z_][A-Za-z0-9_]*|\(.+?\))", "Sqrt($1)");

            // Convert exponentiation to Pow(base, exp) with math-style precedence.
            // We do this with a small balanced scan (not regex) so nested parentheses like
            // 1/(x*(x-1)^2) rewrite correctly to 1/(x*Pow((x-1),2)).
            s = RewriteExponentiation(s);

            // Handle absolute value bars |x| -> Abs(x)
            s = RewriteAbsoluteValue(s);

            // Fix common WordsToSym artifacts
            // "1 i" or "1i" -> "1 * i" (imaginary unit spacing issue)
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\b(\d+(?:\.\d+)?)\s+[ij]\b", "$1 * i");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\b(\d+(?:\.\d+)?)[ij]\b", "$1 * i");
            
            // Handle nth roots sqrt[n](x) -> Pow(x, 1/n)
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\bsqrt\[\s*(\d+)\s*\]\s*\((.+?)\)", "Pow(($2), 1/($1))", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Handle lonely i or j if preceded by + or - or space at start/after punctuation
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<=\+|\-|\*|\/|\(|\,|^)\s*[ij]\b", "1 * i");

            // Implicit multiplication: ) ( -> ) * (
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\)\s*\(", ") * (");
            // Implicit multiplication: ) x -> ) * x (excluding keywords)
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\)\s*(?!(?:for|from|to|if|then|else)\b)([A-Za-z_][A-Za-z0-9_]*)", ") * $1");
            // Implicit multiplication: 2x -> 2 * x (excluding keywords)
            // Do not split function invocations like 2log1p(x); keep as 2 * log1p(x) via later rewriting,
            // and also avoid breaking identifiers that are immediately followed by '(' (function call).
            s = System.Text.RegularExpressions.Regex.Replace(
                s,
                @"(\d+(?:\.\d+)?)\s*(?!(?:for|from|to|if|then|else|in|and|or|not)\b)([A-Za-z_][A-Za-z0-9_]*)(?!\s*\()",
                "$1 * $2",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Scientific notation: keep 1e-12 / 2E+3 as numeric literals so the implicit-multiplication rule above
            // doesn't turn them into "1 * e - 12".
            s = System.Text.RegularExpressions.Regex.Replace(
                s,
                @"\b(\d+(?:\.\d+)?)\s*([eE])\s*([\+\-])\s*(\d+)\b",
                "$1$2$3$4");
            // Implicit multiplication: 2(x) -> 2 * (x)
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<![A-Za-z0-9_])(\d+(?:\.\d+)?)\s*\(", "$1 * (");
            // Implicit multiplication: (x)2 -> (x) * 2
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\)\s*(\d+(?:\.\d+)?)", ") * $1");

            // Normalize "Sum(body for var from start to end)" -> "Sum(body, var, start, end)"
            // Improved to handle nested parentheses in body
            s = System.Text.RegularExpressions.Regex.Replace(s, 
                @"\bSum\s*\(\s*(.+?)\s+for\s+([A-Za-z_][A-Za-z0-9_]*)\s+from\s+(.+?)\s+to\s+(.+?)\s*\)", 
                "Sum($1, $2, $3, $4)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Normalize "[a, b]" -> "interval(a, b)"
            // Ensure we don't match matrix rows or list indexing if possible, but for WordProblems this is usually an interval.
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<![A-Za-z0-9_\]\)])\[\s*([A-Za-z0-9_.\-\+\*\/ \(\)]+?)\s*,\s*([A-Za-z0-9_.\-\+\*\/ \(\)]+?)\s*\]", "interval($1, $2)");
            
            // Normalize "Ceiling" -> "ceil"
            s = s.Replace("Ceiling(", "ceil(", System.StringComparison.OrdinalIgnoreCase);
            
            // Handle "x = Variable" or "x = ?" placeholders
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\b[A-Za-z_][A-Za-z0-9_]*\s*==?\s*(?:Variable(?:\(\))?|Unknown|\?)\s*;?", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return s;
        }

        private static string RewriteAbsoluteValue(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            
            // Simple approach: find balanced pairs of | that don't look like || (logical OR).
            // This is a heuristic and might fail on complex nested | | | | cases.
            var sb = new System.Text.StringBuilder();
            bool inAbs = false;
            int lastIdx = 0;
            
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '|')
                {
                    // Check for || (OR)
                    if (i + 1 < s.Length && s[i + 1] == '|')
                    {
                        i++; // skip both
                        continue;
                    }
                    if (i > 0 && s[i - 1] == '|')
                    {
                        continue; // already handled
                    }

                    sb.Append(s.Substring(lastIdx, i - lastIdx));
                    if (!inAbs)
                    {
                        sb.Append("Abs(");
                        inAbs = true;
                    }
                    else
                    {
                        sb.Append(")");
                        inAbs = false;
                    }
                    lastIdx = i + 1;
                }
            }
            sb.Append(s.Substring(lastIdx));
            return sb.ToString();
        }

        private static string RewriteExponentiation(string s)
        {
            // Handle Python-style '**' first, then caret '^'.
            s = RewriteExponentiationOperator(s, "**");
            s = RewriteExponentiationOperator(s, "^");
            return s;
        }

        private static string RewriteExponentiationOperator(string s, string op)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }

            int opLen = op.Length;
            if (opLen is < 1 or > 2)
            {
                return s;
            }

            int searchFrom = s.Length - 1;
            while (searchFrom >= 0)
            {
                int opIndex = opLen == 1
                    ? s.LastIndexOf(op[0], searchFrom)
                    : s.LastIndexOf(op, searchFrom, StringComparison.Ordinal);

                if (opIndex < 0)
                {
                    return s;
                }

                // Skip XOR-assignment and similar constructs (e.g., x ^= 2).
                if (op == "^" && opIndex + 1 < s.Length && s[opIndex + 1] == '=')
                {
                    searchFrom = opIndex - 1;
                    continue;
                }

                if (!TryGetLeftAtomBounds(s, opIndex, out int leftStart, out int leftEndExclusive) ||
                    !TryGetRightAtomBounds(s, opIndex + opLen, out int rightStart, out int rightEndExclusive))
                {
                    searchFrom = opIndex - 1;
                    continue;
                }

                string left = s.Substring(leftStart, leftEndExclusive - leftStart);
                string right = s.Substring(rightStart, rightEndExclusive - rightStart);
                string replacement = $"Pow({left}, {right})";

                s = s.Substring(0, leftStart) + replacement + s.Substring(rightEndExclusive);
                searchFrom = leftStart - 1;
            }

            return s;
        }

        private static bool TryGetLeftAtomBounds(string s, int opIndex, out int start, out int endExclusive)
        {
            start = endExclusive = -1;
            int i = opIndex - 1;
            while (i >= 0 && char.IsWhiteSpace(s[i])) i--;
            if (i < 0) return false;

            endExclusive = i + 1;

            if (s[i] == ')')
            {
                int openParen = FindMatchingParenLeft(s, i);
                if (openParen < 0) return false;

                int j = openParen - 1;
                while (j >= 0 && char.IsWhiteSpace(s[j])) j--;
                while (j >= 0 && IsIdentifierChar(s[j])) j--;
                start = j + 1;
                return start < endExclusive;
            }

            int k = i;
            while (k >= 0 && IsAtomChar(s[k])) k--;
            start = k + 1;
            return start < endExclusive;
        }

        private static bool TryGetRightAtomBounds(string s, int indexAfterOp, out int start, out int endExclusive)
        {
            start = endExclusive = -1;
            int i = indexAfterOp;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length) return false;

            int atomStart = i;
            if (s[i] is '+' or '-')
            {
                i++;
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length) return false;
            }

            if (s[i] == '(')
            {
                int closeParen = FindMatchingParenRight(s, i);
                if (closeParen < 0) return false;
                start = atomStart;
                endExclusive = closeParen + 1;
                return true;
            }

            int j = i;
            while (j < s.Length && IsAtomChar(s[j])) j++;
            if (j == i) return false;

            // Include invocation: f(x) or member access chain ending in (...)
            int afterName = j;
            int k = afterName;
            while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
            if (k < s.Length && s[k] == '(')
            {
                int closeParen = FindMatchingParenRight(s, k);
                if (closeParen < 0) return false;
                endExclusive = closeParen + 1;
            }
            else
            {
                endExclusive = afterName;
            }

            start = atomStart;
            return start < endExclusive;
        }

        private static bool IsAtomChar(char c) =>
            char.IsLetterOrDigit(c) || c is '_' or '.';

        private static bool IsIdentifierChar(char c) =>
            char.IsLetterOrDigit(c) || c is '_' or '.';

        private static int FindMatchingParenLeft(string s, int closeParenIndex)
        {
            int depth = 0;
            for (int i = closeParenIndex; i >= 0; i--)
            {
                if (s[i] == ')') depth++;
                else if (s[i] == '(')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static int FindMatchingParenRight(string s, int openParenIndex)
        {
            int depth = 0;
            for (int i = openParenIndex; i < s.Length; i++)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private void ParseStatement(
            CSharpSyntaxTree tree,
            StatementSyntax statement,
            List<IExpression> expressions,
            List<Rule> rules,
            List<CSharpDiagnostic> diagnostics)
        {
            if (statement is ExpressionStatementSyntax exprStatement)
            {
                ParseTopLevelExpression(tree, exprStatement.Expression, expressions, rules, diagnostics);
            }
            else
            {
                diagnostics.Add(CreateDiagnostic(tree, statement, DiagnosticSeverity.Error, $"Unsupported statement: {statement.Kind()}"));
            }
        }

        private void ParseTopLevelExpression(
            CSharpSyntaxTree tree,
            ExpressionSyntax expression,
            List<IExpression> expressions,
            List<Rule> rules,
            List<CSharpDiagnostic> diagnostics)
        {
            if (expression is InvocationExpressionSyntax invocation &&
                string.Equals(GetInvocationName(invocation.Expression), "Rule", StringComparison.Ordinal))
            {
                SeparatedSyntaxList<ArgumentSyntax> args = invocation.ArgumentList.Arguments;
                if (args.Count != 2)
                {
                    if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                        Console.WriteLine($"DEBUG: Rule rejected: argument count is {args.Count}");
                    diagnostics.Add(CreateDiagnostic(tree, invocation, DiagnosticSeverity.Error, "Rule requires exactly two arguments."));
                    return;
                }

                IExpression? pattern = ParseExpression(tree, args[0].Expression, diagnostics);
                IExpression? replacement = ParseExpression(tree, args[1].Expression, diagnostics);
                    if (pattern != null && replacement != null)
                    {
                        if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                            Console.WriteLine($"DEBUG: Parsed rule: {pattern.ToDisplayString()} => {replacement.ToDisplayString()}");
                        rules.Add(new Rule(pattern, replacement, null));
                    }
                    else
                    {
                        if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                            Console.WriteLine($"DEBUG: Rule rejected: pattern={pattern?.ToDisplayString() ?? "null"}, replacement={replacement?.ToDisplayString() ?? "null"}");
                    }
                return;
            }

            IExpression? parsedExpression = ParseExpression(tree, expression, diagnostics);
            if (parsedExpression != null)
            {
                expressions.Add(parsedExpression);
            }
        }

        private IExpression? ParseExpression(
            CSharpSyntaxTree tree,
            ExpressionSyntax expression,
            List<CSharpDiagnostic> diagnostics)
        {
            switch (expression)
            {
                case LiteralExpressionSyntax literal:
                    if (literal.IsKind(SyntaxKind.TrueLiteralExpression)) return new Symbol("true");
                    if (literal.IsKind(SyntaxKind.FalseLiteralExpression)) return new Symbol("false");
                    return ParseLiteral(tree, literal, diagnostics);
                case IdentifierNameSyntax identifier:
                    return new Symbol(identifier.Identifier.ValueText);
                case ParenthesizedExpressionSyntax paren:
                    return ParseExpression(tree, paren.Expression, diagnostics);
                case PrefixUnaryExpressionSyntax prefix:
                    return ParsePrefix(tree, prefix, diagnostics);
                case AssignmentExpressionSyntax assign:
                    // Treat simple assignment (x = y) as an equality for math expressions.
                    if (assign.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    {
                        IExpression? left = ParseExpression(tree, assign.Left, diagnostics);
                        IExpression? right = ParseExpression(tree, assign.Right, diagnostics);
                        return left != null && right != null ? new Equality(left, right) : null;
                    }
                    return ReportAndReturnNull(tree, assign, diagnostics, $"Unsupported assignment expression: {assign.Kind()}");
                case BinaryExpressionSyntax binary:
                    return ParseBinary(tree, binary, diagnostics);
                case InvocationExpressionSyntax invocation:
                    return ParseInvocation(tree, invocation, diagnostics);
                case MemberAccessExpressionSyntax memberAccess:
                    // Treat member access (a.b) as dot product (a . b)
                    IExpression? targetExpr = ParseExpression(tree, memberAccess.Expression, diagnostics);
                    if (targetExpr == null)
                    {
                        return ReportAndReturnNull(tree, memberAccess, diagnostics, "Unable to parse member access target.");
                    }
                    string memberName = memberAccess.Name.Identifier.ValueText;
                    return new DotProduct(targetExpr, new Symbol(memberName));
                case ElementAccessExpressionSyntax elementAccess:
                    // Map element access (arr[idx]) to Index(arr, idx) function for downstream handling
                    IExpression? arrayExpr = ParseExpression(tree, elementAccess.Expression, diagnostics);
                    if (arrayExpr == null)
                    {
                        return ReportAndReturnNull(tree, elementAccess, diagnostics, "Unable to parse element access target.");
                    }
                    List<IExpression> idxArgs = new();
                    idxArgs.Add(arrayExpr);
                    foreach (var a in elementAccess.ArgumentList.Arguments)
                    {
                        IExpression? parsedIdx = ParseExpression(tree, a.Expression, diagnostics);
                        if (parsedIdx == null)
                        {
                            return ReportAndReturnNull(tree, a.Expression, diagnostics, "Unable to parse element access index.");
                        }
                        idxArgs.Add(parsedIdx);
                    }
                    return new Function("Index", idxArgs.ToImmutableList());
                default:
                    return ReportAndReturnNull(tree, expression, diagnostics, $"Unsupported expression: {expression.Kind()}.");
            }
        }

        private IExpression? ParseLiteral(
            CSharpSyntaxTree tree,
            LiteralExpressionSyntax literal,
            List<CSharpDiagnostic> diagnostics)
        {
            if (literal.IsKind(SyntaxKind.NumericLiteralExpression))
            {
                var text = literal.Token.Text;
                if (text.EndsWith("m", StringComparison.OrdinalIgnoreCase) || text.EndsWith("d", StringComparison.OrdinalIgnoreCase) || text.EndsWith("f", StringComparison.OrdinalIgnoreCase))
                {
                    text = text[..^1];
                }

                if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal numericValue))
                {
                    return new Number(numericValue);
                }

                // Fallback to Token.Value if text parsing fails
                object? value = literal.Token.Value;
                if (value is not null)
                {
                    try
                    {
                        return new Number(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
                    }
                    catch { }
                }

                diagnostics.Add(CreateDiagnostic(tree, literal, DiagnosticSeverity.Error, $"Unable to parse numeric literal '{literal.Token.Text}'."));
                return null;
            }

            if (literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return new Symbol("\"" + literal.Token.ValueText + "\"");
            }

            diagnostics.Add(CreateDiagnostic(tree, literal, DiagnosticSeverity.Error, "Only numeric and string literals are supported as standalone expressions."));
            return null;
        }

        private IExpression? ParsePrefix(
            CSharpSyntaxTree tree,
            PrefixUnaryExpressionSyntax prefix,
            List<CSharpDiagnostic> diagnostics)
        {
            IExpression? operand = ParseExpression(tree, prefix.Operand, diagnostics);
            if (operand is null)
            {
                return null;
            }

            return prefix.Kind() switch
            {
                SyntaxKind.UnaryMinusExpression => new Multiply(new Number(-1m), operand),
                SyntaxKind.UnaryPlusExpression => operand,
                SyntaxKind.LogicalNotExpression => new Function("not", ImmutableList.Create(operand)),
                _ => ReportAndReturnNull(tree, prefix, diagnostics, $"Unsupported unary operator {prefix.Kind()}.")
            };
        }

        private IExpression? ParseBinary(
            CSharpSyntaxTree tree,
            BinaryExpressionSyntax binary,
            List<CSharpDiagnostic> diagnostics)
        {
            IExpression? left = ParseExpression(tree, binary.Left, diagnostics);
            IExpression? right = ParseExpression(tree, binary.Right, diagnostics);
            if (left is null || right is null)
            {
                return null;
            }

            return binary.Kind() switch
            {
                SyntaxKind.AddExpression => new Add(left, right),
                SyntaxKind.SubtractExpression => new Subtract(left, right),
                SyntaxKind.MultiplyExpression => new Multiply(left, right),
                SyntaxKind.DivideExpression => new Divide(left, right),
                // In math input we interpret '^' as exponentiation.
                // Roslyn parses it as ExclusiveOrExpression, so map it to Power.
                SyntaxKind.ExclusiveOrExpression => new Power(left, right),
                SyntaxKind.ModuloExpression => new Function("Mod", ImmutableList.Create(left, right)),
                SyntaxKind.EqualsExpression => new Equality(left, right),
                SyntaxKind.NotEqualsExpression => new Function("ne", ImmutableList.Create(left, right)),
                SyntaxKind.GreaterThanExpression => new Function("gt", ImmutableList.Create(left, right)),
                SyntaxKind.LessThanExpression => new Function("lt", ImmutableList.Create(left, right)),
                SyntaxKind.GreaterThanOrEqualExpression => new Function("ge", ImmutableList.Create(left, right)),
                SyntaxKind.LessThanOrEqualExpression => new Function("le", ImmutableList.Create(left, right)),
                SyntaxKind.LogicalAndExpression => new Function("and", ImmutableList.Create(left, right)),
                SyntaxKind.LogicalOrExpression => new Function("or", ImmutableList.Create(left, right)),
                _ => ReportAndReturnNull(tree, binary, diagnostics, $"Unsupported binary operator {binary.Kind()}.")
            };
        }

        private IExpression? ParseInvocation(
            CSharpSyntaxTree tree,
            InvocationExpressionSyntax invocation,
            List<CSharpDiagnostic> diagnostics)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax)
            {
                return ReportAndReturnNull(tree, invocation, diagnostics, "Member access invocations (expr.Member()) are not supported. Use Func(expr, ...).");
            }

            string? name = GetInvocationName(invocation.Expression);
            if (string.IsNullOrWhiteSpace(name))
            {
                diagnostics.Add(CreateDiagnostic(tree, invocation, DiagnosticSeverity.Error, "Invocation must target an identifier."));
                return null;
            }

            ImmutableList<IExpression>? ParseArgs()
            {
                List<IExpression> parsed = new();
                foreach (ArgumentSyntax arg in invocation.ArgumentList.Arguments)
                {
                    IExpression? parsedArg = ParseExpression(tree, arg.Expression, diagnostics);
                    if (parsedArg is null)
                    {
                        return null;
                    }
                    parsed.Add(parsedArg);
                }
                return parsed.ToImmutableList();
            }

            switch (name)
            {
                case "Pow":
                    if (invocation.ArgumentList.Arguments.Count != 2)
                    {
                        diagnostics.Add(CreateDiagnostic(tree, invocation, DiagnosticSeverity.Error, "Pow requires two arguments."));
                        return null;
                    }
                    {
                        IExpression? @base = ParseExpression(tree, invocation.ArgumentList.Arguments[0].Expression, diagnostics);
                        IExpression? exponent = ParseExpression(tree, invocation.ArgumentList.Arguments[1].Expression, diagnostics);
                        return @base != null && exponent != null ? new Power(@base, exponent) : null;
                    }
                case "Vector":
                    {
                        ImmutableList<IExpression>? args = ParseArgs();
                        if (args is null)
                        {
                            return null;
                        }
                        Vector vector = new Vector(args);
                        if (!vector.Shape.IsValid)
                        {
                            diagnostics.Add(CreateDiagnostic(tree, invocation, DiagnosticSeverity.Error, "Vector components must be scalar."));
                        }
                        return vector;
                    }
                case "Matrix":
                    {
                        List<Vector> rows = new();
                        foreach (ArgumentSyntax arg in invocation.ArgumentList.Arguments)
                        {
                            IExpression? rowExpr = ParseExpression(tree, arg.Expression, diagnostics);
                            if (rowExpr is Vector rowVector)
                            {
                                rows.Add(rowVector);
                            }
                            else
                            {
                                diagnostics.Add(CreateDiagnostic(tree, arg.Expression, DiagnosticSeverity.Error, "Matrix rows must be Vector(...) expressions."));
                                return new Matrix(ImmutableList<Vector>.Empty);
                            }
                        }

                        if (rows.Count > 1 && rows.Select(r => r.Arguments.Count).Distinct().Count() > 1)
                        {
                            diagnostics.Add(CreateDiagnostic(tree, invocation, DiagnosticSeverity.Error, "Matrix rows must all have the same length."));
                        }

                        Matrix matrix = new Matrix(rows.ToImmutableList());
                        if (!matrix.Shape.IsValid)
                        {
                            diagnostics.Add(CreateDiagnostic(tree, invocation, DiagnosticSeverity.Error, "Matrix shape is invalid."));
                        }
                        return matrix;
                    }
                case "MatrixMultiply":
                    return ParseBinaryInvocation(tree, invocation, diagnostics, name, (a, b) => new MatrixMultiply(a, b), 2);
                case "DotProduct":
                    return ParseBinaryInvocation(tree, invocation, diagnostics, name, (a, b) => new DotProduct(a, b), 2);
                case "Derivative":
                    return ParseBinaryInvocation(tree, invocation, diagnostics, name, (a, b) => new Derivative(a, b), 2);
                case "Equality":
                    return ParseBinaryInvocation(tree, invocation, diagnostics, name, (a, b) => new Equality(a, b), 2);
                case "Integral":
                    return ParseBinaryInvocation(tree, invocation, diagnostics, name, (a, b) => new Integral(a, b), 2);
                case "Grad":
                    return ParseBinaryInvocation(tree, invocation, diagnostics, name, (a, b) => new Grad(a, b), 2);
                case "Div":
                    return ParseBinaryInvocation(tree, invocation, diagnostics, name, (a, b) => new Div(a, b), 2);
                case "Curl":
                    return ParseBinaryInvocation(tree, invocation, diagnostics, name, (a, b) => new Curl(a, b), 2);
                case "Wild":
                    return ParseWild(tree, invocation, diagnostics);
                case "Piecewise":
                    {
                        var args = new List<IExpression>();
                        foreach (var a in invocation.ArgumentList.Arguments)
                        {
                            var parsedArg = ParseExpression(tree, a.Expression, diagnostics);
                            if (parsedArg is null) return null;
                            args.Add(parsedArg);
                        }
                        return new Piecewise(args.ToImmutableList());
                    }
                // Tensor Operations
                case "MatMul":
                    {
                         ImmutableList<IExpression>? args = ParseArgs();
                         return args is null ? null : new MatMul(args);
                    }
                case "TensorAdd":
                    {
                         ImmutableList<IExpression>? args = ParseArgs();
                         if (args == null) return null;
                         if (args.Count <= 2) return new TensorAdd(args);
                         // Binarize: TensorAdd(A, B, C) -> TensorAdd(A, TensorAdd(B, C))
                         IExpression current = args[^1];
                         for (int i = args.Count - 2; i >= 0; i--)
                         {
                             current = new TensorAdd(args[i], current);
                         }
                         return current;
                    }
                case "TensorMul":
                    {
                         ImmutableList<IExpression>? args = ParseArgs();
                         if (args == null) return null;
                         if (args.Count <= 2) return new TensorMul(args);
                         // Binarize
                         IExpression current = args[^1];
                         for (int i = args.Count - 2; i >= 0; i--)
                         {
                             current = new TensorMul(args[i], current);
                         }
                         return current;
                    }
                case "Transpose":
                    {
                         ImmutableList<IExpression>? args = ParseArgs();
                         return args is null ? null : new Transpose(args);
                    }
                case "Relu":
                    {
                         ImmutableList<IExpression>? args = ParseArgs();
                         return args is null ? null : new Relu(args);
                    }
                case "Attr":
                    {
                         ImmutableList<IExpression>? args = ParseArgs();
                         return args is null ? null : new Attr(args);
                    }
                case "Conv2D":
                    {
                         ImmutableList<IExpression>? args = ParseArgs();
                         return args is null ? null : new Conv2D(args);
                    }
                case "FusedMatMulAdd":
                    {
                         ImmutableList<IExpression>? args = ParseArgs();
                         return args is null ? null : new FusedMatMulAdd(args);
                    }
                case "FusedMatMulAddRelu":
                    {
                         ImmutableList<IExpression>? args = ParseArgs();
                         return args is null ? null : new FusedMatMulAddRelu(args);
                    }
                case "FusedConv2DRelu":
                    {
                         ImmutableList<IExpression>? args = ParseArgs();
                         return args is null ? null : new FusedConv2DRelu(args);
                    }
                case "Kronecker":
                    {
                         ImmutableList<IExpression>? args = ParseArgs();
                         return args is null ? null : new Kronecker(args);
                    }
                case "vec":
                    {
                         ImmutableList<IExpression>? args = ParseArgs();
                         return args is null ? null : new TensorVec(args);
                    }
                case "inverse":
                    {
                         ImmutableList<IExpression>? args = ParseArgs();
                         return args is null ? null : new Inverse(args);
                    }
                default:
                    {
                        ImmutableList<IExpression>? args = ParseArgs();
                        return args is null ? null : new Function(name, args);
                    }
            }
        }

        private IExpression? ParseBinaryInvocation(
            CSharpSyntaxTree tree,
            InvocationExpressionSyntax invocation,
            List<CSharpDiagnostic> diagnostics,
            string name,
            Func<IExpression, IExpression, IExpression> factory,
            int expectedArgs)
        {
            if (invocation.ArgumentList.Arguments.Count != expectedArgs)
            {
                diagnostics.Add(CreateDiagnostic(tree, invocation, DiagnosticSeverity.Error, $"{name} requires {expectedArgs} arguments."));
                return null;
            }

            IExpression? first = ParseExpression(tree, invocation.ArgumentList.Arguments[0].Expression, diagnostics);
            IExpression? second = ParseExpression(tree, invocation.ArgumentList.Arguments[1].Expression, diagnostics);
            return first != null && second != null ? factory(first, second) : null;
        }

        private IExpression? ParseWild(
            CSharpSyntaxTree tree,
            InvocationExpressionSyntax invocation,
            List<CSharpDiagnostic> diagnostics)
        {
            SeparatedSyntaxList<ArgumentSyntax> args = invocation.ArgumentList.Arguments;
            if (args.Count is < 1 or > 2)
            {
                diagnostics.Add(CreateDiagnostic(tree, invocation, DiagnosticSeverity.Error, "Wild requires one or two arguments."));
                return null;
            }

            if (args[0].Expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                diagnostics.Add(CreateDiagnostic(tree, args[0].Expression, DiagnosticSeverity.Error, "Wild name must be a string literal."));
                return null;
            }

            string name = literal.Token.ValueText;
            WildConstraint constraint = WildConstraint.None;

            if (args.Count == 2)
            {
                string? constraintName = args[1].Expression switch
                {
                    IdentifierNameSyntax id => id.Identifier.ValueText,
                    _ => null
                };

                constraint = constraintName switch
                {
                    "Scalar" => WildConstraint.Scalar,
                    "Constant" => WildConstraint.Constant,
                    "Vector" => WildConstraint.Vector,
                    "Matrix" => WildConstraint.Matrix,
                    null => WildConstraint.None,
                    _ => WildConstraint.None
                };

                if (constraintName is null)
                {
                    diagnostics.Add(CreateDiagnostic(tree, args[1].Expression, DiagnosticSeverity.Error, "Wild constraint must be an identifier (Scalar, Constant, Vector, or Matrix)."));
                }
            }

            return new Wild(name, constraint);
        }

        private string? GetInvocationName(ExpressionSyntax expression)
        {
            string? name = null;
            if (expression is IdentifierNameSyntax identifier) name = identifier.Identifier.ValueText;
            else if (expression is GenericNameSyntax generic) name = generic.Identifier.ValueText;
            
            if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                Console.WriteLine($"DEBUG: GetInvocationName for {expression.Kind()} -> {name ?? "null"}");
            return name;
        }

        private CSharpDiagnostic ConvertDiagnostic(Diagnostic diagnostic)
        {
            DiagnosticSeverity severity = diagnostic.Severity switch
            {
                Microsoft.CodeAnalysis.DiagnosticSeverity.Info => DiagnosticSeverity.Info,
                Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Error
            };

            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            return new CSharpDiagnostic(
                severity,
                diagnostic.GetMessage(),
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1);
        }

        private CSharpDiagnostic CreateDiagnostic(
            CSharpSyntaxTree tree,
            SyntaxNode node,
            DiagnosticSeverity severity,
            string message)
        {
            FileLinePositionSpan span = tree.GetLineSpan(node.Span);
            return new CSharpDiagnostic(
                severity,
                message,
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1);
        }

        private static IExpression? ReportAndReturnNull(
            CSharpSyntaxTree tree,
            SyntaxNode node,
            List<CSharpDiagnostic> diagnostics,
            string message)
        {
            FileLinePositionSpan span = tree.GetLineSpan(node.Span);
            diagnostics.Add(new CSharpDiagnostic(
                DiagnosticSeverity.Error,
                message,
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1));
            return null;
        }
    }

    internal static class Formatter
    {
        private static string FormatDecimal(decimal value)
        {
            var s = value.ToString(CultureInfo.InvariantCulture);
            if (s.Contains('.', StringComparison.Ordinal))
            {
                s = s.TrimEnd('0').TrimEnd('.');
            }
            return s;
        }

        public static string FormatExpression(IExpression expr, int parentPrecedence, int depth = 0)
        {
            if (expr is null)
            {
                throw new ArgumentNullException(nameof(expr));
            }

            if (depth > 200)
            {
                return "...";
            }

            switch (expr)
            {
                case Number number:
                    return FormatDecimal(number.Value);
                case Symbol symbol:
                    return symbol.Name;
                case Wild wild:
                    return FormatWild(wild);
                case Add add:
                    return Wrap(FormatAdd(add, depth + 1), parentPrecedence, 1);
                case Subtract subtract:
                    return FormatExpression(subtract.Canonicalize(), parentPrecedence, depth + 1);
                case Multiply multiply:
                    return Wrap(FormatMultiply(multiply, depth + 1), parentPrecedence, 2);
                case Divide divide:
                    return FormatExpression(divide.Canonicalize(), parentPrecedence, depth + 1);
                case Power power:
                    if (power.Exponent is Number powNum)
                    {
                        if (powNum.Value == 1m)
                        {
                            return FormatExpression(power.Base, parentPrecedence, depth + 1);
                        }
                        if (powNum.Value == 0m)
                        {
                            return "1";
                        }
                    }
                    return $"Pow({FormatExpression(power.Base, 0, depth + 1)}, {FormatExpression(power.Exponent, 0, depth + 1)})";
                case Function function:
                    if ((function.Name.Equals("forall", StringComparison.OrdinalIgnoreCase) ||
                         function.Name.Equals("exists", StringComparison.OrdinalIgnoreCase)) &&
                        function.Arguments.Count == 3)
                    {
                        var name = function.Name.Equals("forall", StringComparison.OrdinalIgnoreCase) ? "ForAll" : "Exists";
                        return $"{name}({string.Join(", ", function.Arguments.Select(a => FormatExpression(a, 0, depth + 1)))})";
                    }
                    if (function.Name.Equals("interval", StringComparison.OrdinalIgnoreCase) && function.Arguments.Count == 2)
                    {
                        return $"[{FormatExpression(function.Arguments[0], 0, depth + 1)}, {FormatExpression(function.Arguments[1], 0, depth + 1)}]";
                    }
                    return $"{function.Name}({string.Join(", ", function.Arguments.Select(a => FormatExpression(a, 0, depth + 1)))})";
                case Vector vector:
                    return $"Vector({string.Join(", ", vector.Arguments.Select(a => FormatExpression(a, 0, depth + 1)))})";
                case Matrix matrix:
                    return FormatMatrix(matrix, depth + 1);
                case Equality equality:
                    return $"{FormatExpression(equality.LeftOperand, 0, depth + 1)} = {FormatExpression(equality.RightOperand, 0, depth + 1)}";
                case Piecewise piecewise:
                    return FormatPiecewise(piecewise, depth + 1);
                case MatrixMultiply matrixMultiply:
                    return $"MatrixMultiply({FormatExpression(matrixMultiply.LeftOperand, 0, depth + 1)}, {FormatExpression(matrixMultiply.RightOperand, 0, depth + 1)})";
                case DotProduct dotProduct:
                    return $"DotProduct({FormatExpression(dotProduct.LeftOperand, 0, depth + 1)}, {FormatExpression(dotProduct.RightOperand, 0, depth + 1)})";
                case Derivative derivative:
                    return $"Derivative({FormatExpression(derivative.TargetExpression, 0, depth + 1)}, {FormatExpression(derivative.Variable, 0, depth + 1)})";
                case Integral integral:
                    return $"Integral({FormatExpression(integral.TargetExpression, 0, depth + 1)}, {FormatExpression(integral.Variable, 0, depth + 1)})";
                case Grad grad:
                    return $"Grad({FormatExpression(grad.ScalarExpression, 0, depth + 1)}, {FormatExpression(grad.VectorVariable, 0, depth + 1)})";
                case Div div:
                    return $"Div({FormatExpression(div.VectorExpression, 0, depth + 1)}, {FormatExpression(div.VectorVariable, 0, depth + 1)})";
                case Curl curl:
                    return $"Curl({FormatExpression(curl.VectorExpression, 0, depth + 1)}, {FormatExpression(curl.VectorVariable, 0, depth + 1)})";
                case MatMul matMul:
                    return $"MatMul({string.Join(", ", matMul.Arguments.Select(a => FormatExpression(a, 0, depth + 1)))})";
                case Transpose transpose:
                    return $"Transpose({string.Join(", ", transpose.Arguments.Select(a => FormatExpression(a, 0, depth + 1)))})";
                case Relu relu:
                    return $"Relu({string.Join(", ", relu.Arguments.Select(a => FormatExpression(a, 0, depth + 1)))})";
                case Attr attr:
                    return $"Attr({string.Join(", ", attr.Arguments.Select(a => FormatExpression(a, 0, depth + 1)))})";
                case Conv2D conv2d:
                    return $"Conv2D({string.Join(", ", conv2d.Arguments.Select(a => FormatExpression(a, 0, depth + 1)))})";
                case FusedMatMulAdd fusedMatMulAdd:
                    return $"FusedMatMulAdd({string.Join(", ", fusedMatMulAdd.Arguments.Select(a => FormatExpression(a, 0, depth + 1)))})";
                case FusedMatMulAddRelu fusedMatMulAddRelu:
                    return $"FusedMatMulAddRelu({string.Join(", ", fusedMatMulAddRelu.Arguments.Select(a => FormatExpression(a, 0, depth + 1)))})";
                case FusedConv2DRelu fusedConv2DRelu:
                    return $"FusedConv2DRelu({string.Join(", ", fusedConv2DRelu.Arguments.Select(a => FormatExpression(a, 0, depth + 1)))})";
                case Kronecker kronecker:
                    return $"Kronecker({string.Join(", ", kronecker.Arguments.Select(a => FormatExpression(a, 0, depth + 1)))})";
                case TensorVec tensorVec:
                    return $"vec({string.Join(", ", tensorVec.Arguments.Select(a => FormatExpression(a, 0, depth + 1)))})";
                case Inverse inverse:
                    return $"inverse({string.Join(", ", inverse.Arguments.Select(a => FormatExpression(a, 0, depth + 1)))})";
                default:
                    return expr.ToDisplayString();
            }
        }

        private static string FormatWild(Wild wild)
        {
            return wild.Constraint switch
            {
                WildConstraint.None => $"Wild(\"{wild.Name}\")",
                WildConstraint.Scalar => $"Wild(\"{wild.Name}\", Scalar)",
                WildConstraint.Constant => $"Wild(\"{wild.Name}\", Constant)",
                WildConstraint.Vector => $"Wild(\"{wild.Name}\", Vector)",
                WildConstraint.Matrix => $"Wild(\"{wild.Name}\", Matrix)",
                _ => $"Wild(\"{wild.Name}\")"
            };
        }

        private static string FormatAdd(Add add, int depth)
        {
            List<string> terms = new();
            foreach (IExpression arg in add.Arguments)
            {
                bool isNegative = TryStripNegation(arg, out IExpression positive);
                string formatted = FormatExpression(positive, 1, depth);
                if (terms.Count == 0)
                {
                    terms.Add(isNegative ? $"-{formatted}" : formatted);
                }
                else
                {
                    terms.Add(isNegative ? $"- {formatted}" : $"+ {formatted}");
                }
            }

            return string.Join(" ", terms);
        }

        private static string FormatMultiply(Multiply multiply, int depth)
        {
            List<IExpression> numerator = new();
            List<IExpression> denominator = new();

            foreach (IExpression arg in multiply.Arguments)
            {
                if (arg is Power power &&
                    power.Exponent is Number num &&
                    num.Value < 0m)
                {
                    Number positiveExponent = new Number(Math.Abs(num.Value));
                    denominator.Add(new Power(power.Base, positiveExponent));
                }
                else
                {
                    numerator.Add(arg);
                }
            }

            bool negative = TryStripNegationFromProduct(numerator);

            string FormatProductList(IEnumerable<IExpression> factors) =>
                string.Join(" * ", factors.Select(f => FormatExpression(f, 2, depth)));

            string numText = numerator.Count == 0 ? "1" : FormatProductList(numerator);

            string result;
            if (denominator.Count > 0)
            {
                string denText = FormatProductList(denominator);
                // Parenthesize any composite denominator to preserve intended grouping.
                // Without this, e.g. "a / x * x" reparses as (a/x)*x, not a/(x*x).
                if (denominator.Count > 1 ||
                    denText.Contains(" * ", StringComparison.Ordinal) ||
                    denText.Contains(" / ", StringComparison.Ordinal) ||
                    denText.Contains(" + ", StringComparison.Ordinal) ||
                    denText.Contains(" - ", StringComparison.Ordinal))
                {
                    denText = $"({denText})";
                }
                result = $"{numText} / {denText}";
            }
            else
            {
                result = numText;
            }

            return negative ? $"-{result}" : result;
        }

        private static bool TryStripNegation(IExpression expression, out IExpression positive)
        {
            if (expression is Number number && number.Value < 0m)
            {
                positive = new Number(Math.Abs(number.Value));
                return true;
            }

            if (expression is Multiply multiply && multiply.Arguments.Count > 0 && multiply.Arguments[0] is Number num && num.Value < 0m)
            {
                Number positiveFirst = new Number(Math.Abs(num.Value));
                ImmutableList<IExpression> rest = multiply.Arguments.RemoveAt(0).Insert(0, positiveFirst);
                positive = new Multiply(rest);
                return true;
            }

            positive = expression;
            return false;
        }

        private static bool TryStripNegationFromProduct(List<IExpression> factors)
        {
            if (factors.Count == 0)
            {
                return false;
            }

            if (factors[0] is Number number && number.Value < 0m)
            {
                factors[0] = new Number(Math.Abs(number.Value));
                return true;
            }

            return false;
        }

        private static string FormatMatrix(Matrix matrix, int depth)
        {
            if (matrix.MatrixDimensions.Length == 2 &&
                matrix.MatrixDimensions[0] * matrix.MatrixDimensions[1] == matrix.Arguments.Count &&
                matrix.MatrixDimensions[1] > 0)
            {
                int rows = matrix.MatrixDimensions[0];
                int cols = matrix.MatrixDimensions[1];
                List<string> formattedRows = new();
                for (int r = 0; r < rows; r++)
                {
                    IEnumerable<IExpression> rowArgs = matrix.Arguments.Skip(r * cols).Take(cols);
                    formattedRows.Add($"Vector({string.Join(", ", rowArgs.Select(e => FormatExpression(e, 0, depth)))})");
                }
                return $"Matrix({string.Join(", ", formattedRows)})";
            }

            return $"Matrix({string.Join(", ", matrix.Arguments.Select(e => FormatExpression(e, 0, depth)))})";
        }

        private static string FormatPiecewise(Piecewise piecewise, int depth)
        {
            var parts = new List<string>();
            for (int i = 0; i < piecewise.Arguments.Count; i += 2)
            {
                if (i + 1 < piecewise.Arguments.Count)
                {
                    parts.Add($"{FormatExpression(piecewise.Arguments[i], 0, depth)}, {FormatExpression(piecewise.Arguments[i + 1], 0, depth)}");
                }
                else
                {
                    parts.Add(FormatExpression(piecewise.Arguments[i], 0, depth));
                }
            }
            return $"Piecewise({string.Join(", ", parts)})";
        }

        private static string Wrap(string text, int parentPrecedence, int currentPrecedence)
        {
            if (currentPrecedence < parentPrecedence)
            {
                return $"({text})";
            }
            return text;
        }
    }
}

