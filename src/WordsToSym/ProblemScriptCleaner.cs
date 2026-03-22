// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace WordsToSym;

public sealed record ProblemScriptCleanupOptions(
    bool NormalizePowCalls = true,
    bool NormalizeCaretPowers = true,
    bool ConvertPlaceholderTargets = true,
    bool NormalizeImplicitMultiplication = true,
    bool NormalizeIntegerConstraints = true,
    bool NormalizeNotEqualConstraints = true,
    bool SplitConjunctions = true,
    bool RemoveMarkdown = true,
    bool RemoveMetadata = false);

public static class ProblemScriptCleaner
{
    public static string NormalizeProblemScript(string? script, ProblemScriptCleanupOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(script)) return string.Empty;
        if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1") Console.WriteLine("DEBUG: NormalizeProblemScript starting...");
        options ??= new ProblemScriptCleanupOptions();

        var rawLines = script.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var lines = JoinContinuedLines(rawLines);
        var cleaned = new List<string>();
        bool inNotes = false;
        bool inTags = false;
        bool inOptions = false;
        bool inRules = false;
        bool inBlockComment = false;

        foreach (var raw in lines)
        {
            var line = raw ?? string.Empty;
            var trimmed = line.Trim();

            if (inBlockComment)
            {
                if (trimmed.Contains("*/", StringComparison.Ordinal))
                {
                    inBlockComment = false;
                    var after = trimmed.Substring(trimmed.IndexOf("*/") + 2).Trim();
                    if (after.Length > 0)
                    {
                         // Process the rest of the line? 
                         // For simplicity, let's just skip the line if it has a block comment end.
                    }
                }
                continue;
            }

            if (trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                if (!trimmed.Contains("*/", StringComparison.Ordinal))
                {
                    inBlockComment = true;
                }
                continue;
            }

            if (inNotes || inTags || inOptions || inRules)
            {
                if (!options.RemoveMetadata) cleaned.Add(line);
                if (trimmed.Contains("</Notes>", StringComparison.OrdinalIgnoreCase)) inNotes = false;
                if (trimmed.Contains("</Tags>", StringComparison.OrdinalIgnoreCase)) inTags = false;
                if (trimmed.Contains("</Options>", StringComparison.OrdinalIgnoreCase)) inOptions = false;
                if (trimmed.Contains("</Rules>", StringComparison.OrdinalIgnoreCase)) inRules = false;
                continue;
            }

            if (trimmed.Contains("<Notes>", StringComparison.OrdinalIgnoreCase))
            {
                if (!options.RemoveMetadata) cleaned.Add(line);
                if (!trimmed.Contains("</Notes>", StringComparison.OrdinalIgnoreCase))
                {
                    inNotes = true;
                }
                continue;
            }

            if (trimmed.Contains("<Tags>", StringComparison.OrdinalIgnoreCase))
            {
                if (!options.RemoveMetadata) cleaned.Add(line);
                if (!trimmed.Contains("</Tags>", StringComparison.OrdinalIgnoreCase))
                {
                    inTags = true;
                }
                continue;
            }

            if (trimmed.Contains("<Options>", StringComparison.OrdinalIgnoreCase))
            {
                if (!options.RemoveMetadata) cleaned.Add(line);
                if (!trimmed.Contains("</Options>", StringComparison.OrdinalIgnoreCase))
                {
                    inOptions = true;
                }
                continue;
            }

            if (trimmed.Contains("<Rules>", StringComparison.OrdinalIgnoreCase))
            {
                if (!options.RemoveMetadata) cleaned.Add(line);
                if (!trimmed.Contains("</Rules>", StringComparison.OrdinalIgnoreCase))
                {
                    inRules = true;
                }
                continue;
            }

            if (options.RemoveMarkdown)
            {
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("```", StringComparison.Ordinal)) continue;
                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                {
                    var rest = trimmed[2..].TrimStart();
                    if (IsLikelyMarkdownBullet(rest))
                    {
                        continue;
                    }
                }
            }

            var withoutComments = StripLineComment(line);
            if (string.IsNullOrWhiteSpace(withoutComments)) continue;

            if (IsMetadataLine(withoutComments))
            {
                cleaned.Add(withoutComments);
                continue;
            }

            // Split by semicolon to handle multiple statements on one line, but avoid splitting inside parentheses or brackets
            var segments = SplitBySemicolon(withoutComments);
            foreach (var segment in segments)
            {
                var normalized = NormalizeLine(segment, options);
                if (string.IsNullOrWhiteSpace(normalized)) continue;

                if (options.SplitConjunctions && normalized.Contains("&&", StringComparison.Ordinal))
                {
                    var parts = SplitByTopLevelConjunctions(normalized).ToList();
                    if (parts.Count > 1)
                    {
                        foreach (var part in parts)
                        {
                            var piece = NormalizeLine(part, options);
                            if (!string.IsNullOrWhiteSpace(piece))
                            {
                                cleaned.Add(piece.EndsWith(";", StringComparison.Ordinal) ? piece : piece + ";");
                            }
                        }
                        continue;
                    }
                }

                cleaned.Add(normalized.EndsWith(";", StringComparison.Ordinal) ? normalized : normalized + ";");
            }
        }

        cleaned = NormalizeIdentifiers(cleaned);
        cleaned = AppendFractionConstraintIfNeeded(cleaned, includeSemicolons: true);

        return string.Join(Environment.NewLine, cleaned);
    }

    private static List<string> JoinContinuedLines(IEnumerable<string> lines)
    {
        var joined = new List<string>();
        string? pending = null;
        bool inMetadata = false;

        foreach (var raw in lines)
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0) continue;

            // Don't join metadata blocks
            bool isMetadata = IsMetadataLine(line) || line.Contains("</Notes>") || line.Contains("</Tags>") || line.Contains("</Options>") || line.Contains("</Rules>");
            if (isMetadata)
            {
                if (pending != null) 
                {
                    joined.Add(pending);
                    pending = null;
                }
                joined.Add(line);
                
                // Track if we are entering or exiting a metadata block
                if (line.Contains("<Notes>") || line.Contains("<Tags>") || line.Contains("<Options>") || line.Contains("<Rules>"))
                {
                    if (!(line.Contains("</Notes>") || line.Contains("</Tags>") || line.Contains("</Options>") || line.Contains("</Rules>")))
                    {
                        inMetadata = true;
                    }
                }
                else
                {
                    inMetadata = false;
                }
                continue;
            }

            if (inMetadata)
            {
                joined.Add(line);
                continue;
            }

            // Strip comment for continuity check
            var content = StripLineComment(line).Trim();
            if (content.Length == 0)
            {
                // Line was just a comment or empty
                if (pending != null)
                {
                    // Keep pending, ignore this line
                }
                else
                {
                    joined.Add(line);
                }
                continue;
            }

            if (pending != null)
            {
                pending += " " + line;
            }
            else
            {
                pending = line;
            }

            if (EndsWithContinuator(pending))
            {
                // Continue
            }
            else
            {
                joined.Add(pending);
                pending = null;
            }
        }

        if (pending != null) joined.Add(pending);
        return joined;
    }

    private static bool EndsWithContinuator(string line)
    {
        var s = StripLineComment(line).TrimEnd();
        if (s.EndsWith(";", StringComparison.Ordinal)) return false;
        if (s.Length == 0) return false;

        // Check for operators
        return s.EndsWith("+", StringComparison.Ordinal) || 
               s.EndsWith("-", StringComparison.Ordinal) || 
               s.EndsWith("*", StringComparison.Ordinal) || 
               s.EndsWith("/", StringComparison.Ordinal) || 
               s.EndsWith("^", StringComparison.Ordinal) || 
               s.EndsWith("&&", StringComparison.Ordinal) || 
               s.EndsWith("||", StringComparison.Ordinal) || 
               s.EndsWith("(", StringComparison.Ordinal) || 
               s.EndsWith("[", StringComparison.Ordinal) ||
               s.EndsWith("=", StringComparison.Ordinal) || 
               s.EndsWith("<", StringComparison.Ordinal) || 
               s.EndsWith(">", StringComparison.Ordinal) || 
               s.EndsWith(",", StringComparison.Ordinal) ||
               s.EndsWith("!", StringComparison.Ordinal) ||
               s.EndsWith("|", StringComparison.Ordinal);
    }

    public static IReadOnlyList<string> NormalizeConstraintLines(IEnumerable<string> constraints, ProblemScriptCleanupOptions? options = null)
    {
        options ??= new ProblemScriptCleanupOptions();

        var results = new List<string>();
        foreach (var raw in constraints ?? Enumerable.Empty<string>())
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0) continue;
            line = line.TrimEnd(';').Trim();

            var segments = SplitBySemicolon(line)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            if (segments.Count == 0)
            {
                continue;
            }

            foreach (var segment in segments)
            {
                var normalized = NormalizeLine(segment, options);
                if (string.IsNullOrWhiteSpace(normalized)) continue;

                if (options.SplitConjunctions && normalized.Contains("&&", StringComparison.Ordinal))
                {
                    var parts = SplitByTopLevelConjunctions(normalized).ToList();
                    if (parts.Count > 1)
                    {
                        foreach (var part in parts)
                        {
                            var piece = NormalizeLine(part, options);
                            if (!string.IsNullOrWhiteSpace(piece))
                            {
                                results.Add(piece);
                            }
                        }
                        continue;
                    }
                }

                results.Add(normalized);
            }
        }

        results = NormalizeIdentifiers(results);
        results = AppendFractionConstraintIfNeeded(results, includeSemicolons: false);
        return results;
    }

    private static List<string> NormalizeIdentifiers(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return lines.ToList();

        var map = BuildIdentifierMap(lines);
        if (map.Count == 0) return lines.ToList();

        if (Environment.GetEnvironmentVariable("SYM_TRACE_WORDSTOSYM") == "1")
        {
            foreach (var kvp in map)
            {
                Console.WriteLine($"DEBUG: ProblemScriptCleaner normalized identifier '{kvp.Key}' -> '{kvp.Value}'");
            }
        }

        var updated = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            if (IsMetadataLine(line))
            {
                updated.Add(line);
            }
            else
            {
                updated.Add(ReplaceIdentifiers(line, map));
            }
        }
        return updated;
    }

    private static Dictionary<string, string> BuildIdentifierMap(IReadOnlyList<string> lines)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var typePrefixRegex = new Regex(@"\b(real|int|decimal|complex)_([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase);

        foreach (var raw in lines)
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0) continue;
            if (IsMetadataLine(line)) continue;
            if (IsQuantifierLine(line)) continue;

            var trimmed = line.TrimEnd(';').Trim();
            
            // Heuristic 1: LHS or Standalone
            if (TryExtractIdentifierCandidate(trimmed, out var candidate))
            {
                var normalized = NormalizeIdentifier(candidate);
                if (!string.Equals(candidate, normalized, StringComparison.Ordinal) && !map.ContainsKey(candidate))
                {
                    map[candidate] = normalized;
                }
            }

            // Heuristic 2: Scan for type-prefixed identifiers anywhere in the line
            foreach (Match m in typePrefixRegex.Matches(trimmed))
            {
                var fullMatch = m.Value;
                if (!map.ContainsKey(fullMatch))
                {
                    map[fullMatch] = NormalizeIdentifier(fullMatch);
                }
            }
        }

        // Reserved single-letter identifiers (e.g., 'j') often appear only inside expressions (e.g., 'b + j == 40')
        // and won't be discovered by the LHS/standalone identifier heuristic above. If we don't normalize them here,
        // later parsing can treat 'j' as an imaginary unit alias and rewrite it to 'i'.
        if (!map.ContainsKey("j") && lines.Any(l => !IsMetadataLine(l) && Regex.IsMatch(l, @"\bj\b", RegexOptions.IgnoreCase)))
        {
            map["j"] = "j_var";
        }

        return map;
    }

    private static string ReplaceIdentifiers(string line, IReadOnlyDictionary<string, string> map)
    {
        var updated = line;
        foreach (var kvp in map)
        {
            var pattern = BuildIdentifierPattern(kvp.Key);

            // Be lenient for single-letter reserved variables (e.g., J vs j).
            var options = kvp.Key.Length == 1 ? RegexOptions.IgnoreCase : RegexOptions.None;
            updated = Regex.Replace(updated, pattern, kvp.Value, options);
        }
        return updated;
    }

    private static string BuildIdentifierPattern(string raw)
    {
        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length <= 1)
        {
            return $@"\b{Regex.Escape(raw)}\b";
        }

        var joined = string.Join(@"\s+", tokens.Select(Regex.Escape));
        return $@"\b{joined}\b";
    }

    private static bool TryExtractIdentifierCandidate(string line, out string candidate)
    {
        candidate = string.Empty;

        if (TryExtractLhs(line, out var lhs))
        {
            if (TryNormalizeIdentifierCandidate(lhs, out candidate))
            {
                return true;
            }
        }
        else if (TryNormalizeIdentifierCandidate(line, out candidate))
        {
            return true;
        }

        return false;
    }

    private static bool TryNormalizeIdentifierCandidate(string raw, out string candidate)
    {
        candidate = string.Empty;
        var lhs = raw.Trim();
        if (lhs.Length == 0) return false;
        if (IsBooleanLiteral(lhs)) return false;
        if (lhs.Contains('(') || lhs.Contains(')') || lhs.Contains('[') || lhs.Contains(']')) return false;

        if (lhs.Contains(' ', StringComparison.Ordinal))
        {
            var parts = lhs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && IsTypeKeyword(parts[0]) && IsIdentifier(parts[1]))
            {
                return false;
            }
        }

        if (IsIdentifier(lhs))
        {
            if (IsReservedIdentifier(lhs))
            {
                candidate = lhs;
                return true;
            }
            return false;
        }

        if (Regex.IsMatch(lhs, @"^[A-Za-z_][A-Za-z0-9_]*(\s+[A-Za-z_][A-Za-z0-9_]*)+$"))
        {
            candidate = lhs;
            return true;
        }

        return false;
    }

    private static bool TryExtractLhs(string line, out string lhs)
    {
        lhs = string.Empty;
        var match = Regex.Match(line, @"^(?<lhs>[^<>=!]+?)(==|=|<=|>=|!=|<|>)");
        if (!match.Success) return false;
        lhs = match.Groups["lhs"].Value.Trim();
        return lhs.Length > 0;
    }

    private static string NormalizeIdentifier(string raw)
    {
        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cleanedTokens = tokens
            .Select(t => Regex.Replace(t, @"[^A-Za-z0-9_]", string.Empty))
            .Where(t => t.Length > 0)
            .ToList();

        if (cleanedTokens.Count == 0) return raw.Trim();

        var combined = string.Join("_", cleanedTokens);

        // Strip type prefixes if present (e.g. real_x -> x, int_y -> y)
        // We do this BEFORE checking for reserved words or leading digits
        foreach (var prefix in new[] { "real_", "int_", "decimal_", "complex_" })
        {
            if (combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && combined.Length > prefix.Length)
            {
                combined = combined.Substring(prefix.Length);
                break; // Only strip one prefix
            }
        }

        if (!char.IsLetter(combined[0]) && combined[0] != '_')
        {
            combined = "_" + combined;
        }

        if (IsReservedIdentifier(combined) && !combined.EndsWith("_var", StringComparison.OrdinalIgnoreCase))
        {
            combined += "_var";
        }

        return combined;
    }

    private static bool IsReservedIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var keywordKind = SyntaxFacts.GetKeywordKind(name);
        if (keywordKind != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None)
        {
            return true;
        }
        return ReservedMathIdentifiers.Contains(name);
    }

    private static bool IsTypeKeyword(string token)
    {
        var kind = SyntaxFacts.GetKeywordKind(token);
        return kind == Microsoft.CodeAnalysis.CSharp.SyntaxKind.IntKeyword
            || kind == Microsoft.CodeAnalysis.CSharp.SyntaxKind.DecimalKeyword
            || kind == Microsoft.CodeAnalysis.CSharp.SyntaxKind.DoubleKeyword
            || kind == Microsoft.CodeAnalysis.CSharp.SyntaxKind.FloatKeyword
            || kind == Microsoft.CodeAnalysis.CSharp.SyntaxKind.LongKeyword
            || kind == Microsoft.CodeAnalysis.CSharp.SyntaxKind.ShortKeyword
            || kind == Microsoft.CodeAnalysis.CSharp.SyntaxKind.BoolKeyword
            || kind == Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringKeyword
            || kind == Microsoft.CodeAnalysis.CSharp.SyntaxKind.VarKeyword;
    }

    private static bool IsMetadataLine(string line)
    {
        return Regex.IsMatch(line, @"^\s*<\/?(Tags|Notes|Options|Rules)\b", RegexOptions.IgnoreCase);
    }

    private static bool IsQuantifierLine(string line)
    {
        return line.StartsWith("ForAll(", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Exists(", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("forall(", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("exists(", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> AppendFractionConstraintIfNeeded(IReadOnlyList<string> lines, bool includeSemicolons)
    {
        var cleaned = new List<string>(lines);

        bool HasMatch(string value) => cleaned.Any(l => l.Trim().TrimEnd(';').Equals(value, StringComparison.OrdinalIgnoreCase));
        bool HasAssignment(string value) => cleaned.Any(l => l.Contains(value, StringComparison.Ordinal) && l.Contains("=="));

        if (cleaned.Any(l => l.Contains("numerator", StringComparison.OrdinalIgnoreCase) || l.Contains("numerator_val", StringComparison.OrdinalIgnoreCase)) &&
            cleaned.Any(l => l.Contains("denominator", StringComparison.OrdinalIgnoreCase) || l.Contains("denominator_val", StringComparison.OrdinalIgnoreCase)))
        {
            var targets = new[] { "simplified_fraction", "result", "ans", "answer" };
            foreach (var t in targets)
            {
                if (HasMatch(t) && !HasAssignment(t))
                {
                    var num = ExtractAssignmentLhs(cleaned, "numerator");
                    var den = ExtractAssignmentLhs(cleaned, "denominator");
                    if (string.IsNullOrWhiteSpace(num) || string.IsNullOrWhiteSpace(den)) break;

                    var constraint = $"{t} == {num} / {den}";
                    cleaned.Add(includeSemicolons ? constraint + ";" : constraint);
                    break;
                }
            }
        }

        return cleaned;
    }

    private static string ExtractAssignmentLhs(IReadOnlyList<string> lines, string token)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim().TrimEnd(';');
            if (!line.Contains(token, StringComparison.OrdinalIgnoreCase)) continue;
            if (!TryExtractLhs(line, out var lhs)) continue;
            return lhs.Trim();
        }
        return string.Empty;
    }

    private static readonly HashSet<string> ReservedMathIdentifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "i",
        "j",
        "e"
    };

    private static string NormalizeLine(string line, ProblemScriptCleanupOptions options)
    {
        var cleaned = (line ?? string.Empty).Trim();
        if (cleaned.Length == 0) return string.Empty;

        cleaned = NormalizeIndexedAggregates(cleaned);
        cleaned = NormalizeLatexSubscripts(cleaned);
        cleaned = NormalizeAbsoluteValueBars(cleaned);

        // Strip metadata labels
        var junkLabels = new[] { "Variables:", "Tags:", "Intermediate Variable:", "Functions:", "Constants:", "Script:", "Constraints:", "Intermediate Variables:", "Assume:" };
        foreach (var label in junkLabels)
        {
            if (cleaned.Equals(label, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (cleaned.StartsWith(label, StringComparison.OrdinalIgnoreCase) && cleaned.Split(' ').Length < 3) return string.Empty;
        }

        // Special handling for Word Problem labels to ensure they pass through to ProblemStruct
        if (cleaned.StartsWith("Problem:", StringComparison.OrdinalIgnoreCase) || 
            cleaned.StartsWith("Word Problem:", StringComparison.OrdinalIgnoreCase))
        {
            return cleaned;
        }

        if (cleaned.Contains("..", StringComparison.Ordinal) || cleaned.Contains("…", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // Remove bold markdown
        cleaned = cleaned.Replace("**", "", StringComparison.Ordinal);

        // Handle type-prefixed identifiers: real_x, int_y, decimal_z
        // These often appear as standalone declarations or in equalities.
        if (Regex.IsMatch(cleaned, @"^(real|int|decimal)_([A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.IgnoreCase))
        {
            var match = Regex.Match(cleaned, @"^(real|int|decimal)_([A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.IgnoreCase);
            var type = match.Groups[1].Value.ToLowerInvariant();
            var name = match.Groups[2].Value;
            cleaned = type switch
            {
                "real" => $"real({name})",
                "int" => $"Integer({name})",
                "decimal" => $"real({name})",
                _ => cleaned
            };
        }
        else if (Regex.IsMatch(cleaned, @"^(real|int|decimal)_([A-Za-z_][A-Za-z0-9_]*)\s*([<>=!]+.*)$", RegexOptions.IgnoreCase))
        {
            var match = Regex.Match(cleaned, @"^(real|int|decimal)_([A-Za-z_][A-Za-z0-9_]*)\s*([<>=!]+.*)$", RegexOptions.IgnoreCase);
            var type = match.Groups[1].Value.ToLowerInvariant();
            var name = match.Groups[2].Value;
            var rest = match.Groups[3].Value;
            
            // Convert to "name rest" and add a separate type constraint if it was int
            if (type == "int")
            {
                return $"{name} {rest}; Integer({name})";
            }
            else
            {
                cleaned = $"{name} {rest}";
            }
        }

        // Handle natural language constraints
        // "x is a positive real number" -> x > 0
        cleaned = Regex.Replace(cleaned, @"^([A-Za-z_][A-Za-z0-9_]*)\s+is\s+a\s+positive\s+real\s+number$", "$1 > 0", RegexOptions.IgnoreCase);
        // "x is a positive integer" -> x > 0 && Integer(x)
        cleaned = Regex.Replace(cleaned, @"^([A-Za-z_][A-Za-z0-9_]*)\s+is\s+a\s+positive\s+integer$", "$1 > 0 && Integer($1)", RegexOptions.IgnoreCase);
        // "x == positive real number" -> x > 0
        cleaned = Regex.Replace(cleaned, @"^([A-Za-z_][A-Za-z0-9_]*)\s*==\s*positive\s+real\s+number$", "$1 > 0", RegexOptions.IgnoreCase);
        // "x is an integer" -> Integer(x)
        cleaned = Regex.Replace(cleaned, @"^([A-Za-z_][A-Za-z0-9_]*)\s+is\s+an?\s+integer$", "Integer($1)", RegexOptions.IgnoreCase);
        // "x is positive" -> x > 0
        cleaned = Regex.Replace(cleaned, @"^([A-Za-z_][A-Za-z0-9_]*)\s+is\s+positive$", "$1 > 0", RegexOptions.IgnoreCase);

        // Handle Divisor functions
        cleaned = Regex.Replace(cleaned, @"\bsum\s+of\s+(the\s+)?proper\s+divisors\s+of\s+(\d+|[A-Za-z_][A-Za-z0-9_]*)", "SumProperDivisors($2)", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bsum\s+of\s+(the\s+)?divisors\s+of\s+(\d+|[A-Za-z_][A-Za-z0-9_]*)", "SumDivisors($2)", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\binteger\s+divisors\s+of\s+(\d+|[A-Za-z_][A-Za-z0-9_]*)", "IntegerDivisors($1)", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bpositive\s+divisors\s+of\s+(\d+|[A-Za-z_][A-Za-z0-9_]*)", "Divisors($1)", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bdivisors\s+of\s+(\d+|[A-Za-z_][A-Za-z0-9_]*)", "Divisors($1)", RegexOptions.IgnoreCase);

        // Handle Unicode roots
        cleaned = Regex.Replace(cleaned, @"∛\s*(\d+(?:\.\d+)?|[A-Za-z_][A-Za-z0-9_]*|\(.+?\))", "Pow($1, 1/3)");
        cleaned = Regex.Replace(cleaned, @"∜\s*(\d+(?:\.\d+)?|[A-Za-z_][A-Za-z0-9_]*|\(.+?\))", "Pow($1, 1/4)");
        cleaned = Regex.Replace(cleaned, @"√\s*(\d+(?:\.\d+)?|[A-Za-z_][A-Za-z0-9_]*|\(.+?\))", "Sqrt($1)");

        // Normalize logical symbols into words
        cleaned = cleaned.Replace("∧", "and", StringComparison.Ordinal)
                         .Replace("∨", "or", StringComparison.Ordinal)
                         .Replace("¬", "not", StringComparison.Ordinal)
                         .Replace("→", "implies", StringComparison.Ordinal)
                         .Replace("⇒", "implies", StringComparison.Ordinal)
                         .Replace("↔", "iff", StringComparison.Ordinal)
                         .Replace("⇔", "iff", StringComparison.Ordinal);

        cleaned = NormalizeLogicSyntax(cleaned);

        // Skip lines that look like numbered list items with explanations
        if (Regex.IsMatch(cleaned, @"^\d+[\.\)]\s+"))
        {
            var afterNumber = Regex.Replace(cleaned, @"^\d+[\.\)]\s+", "").Trim();
            if (IsLikelyExplanation(afterNumber)) return string.Empty;
            cleaned = afterNumber;
        }

        if (IsLikelyExplanation(cleaned)) return string.Empty;

        // Strip common invalid control flow keywords if they appear as standalone markers
        if (cleaned.Equals("then", StringComparison.OrdinalIgnoreCase) || 
            cleaned.Equals("else", StringComparison.OrdinalIgnoreCase) ||
            cleaned.StartsWith("then ", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // Handle leading = or == typos (e.g. "= total_spent > 100")
        if (cleaned.StartsWith("=="))
        {
            cleaned = cleaned[2..].Trim();
        }
        else if (cleaned.StartsWith("="))
        {
            cleaned = cleaned[1..].Trim();
        }

        // Strip "Let " prefix from variable declarations/assignments
        if (cleaned.StartsWith("Let ", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(4).Trim();
        }

        if (TryUnwrapLabeledComparison(cleaned, out var unwrappedComparison))
        {
            cleaned = unwrappedComparison;
        }

        // Handle "double equality" artifacts like "Eq1 == 2010*a + 2014*b = 2018"
        if (TryFindTopLevelDoubleEquals(cleaned, out var eqIdx))
        {
            var before = cleaned[..eqIdx].Trim();
            // Only strip if LHS is a simple identifier (no parentheses, so not a function definition like f(x) == ...)
            if (!before.Contains('(') && !before.Contains(' ') &&
                HasTopLevelEquals(cleaned[(eqIdx + 2)..]))
            {
                cleaned = cleaned[(eqIdx + 2)..].Trim();
            }
        }

        // Comment out lines with '±' as they usually indicate intermediate derivation with ambiguous syntax for the solver
        if (cleaned.Contains("±") || cleaned.Contains("\\pm"))
        {
            return "// " + cleaned;
        }

        // Handle LaTeX modular arithmetic
        cleaned = Regex.Replace(cleaned, @"\\equiv", "==", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\\pmod\s*\{?([^}]+)\}?", " % $1", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\(mod(?!\s*\()\s*([^)]+)\)", " % $1", RegexOptions.IgnoreCase);

        // Fix imaginary unit '1 i' or '2i' or '2 j' or '2j'
        cleaned = Regex.Replace(cleaned, @"(\d+(?:\.\d+)?)\s*([ij])\b", "$1 * $2", RegexOptions.IgnoreCase);
        
        // Handle modular arithmetic normalization
        // a % m -> mod(a, m)
        // Check for 'a mod m' or 'a % m'
        // We use a broader pattern for RHS to allow expressions, but be careful of trailing operators
        
        // a % m -> mod(a, m)
        cleaned = Regex.Replace(cleaned, @"(?<lhs>[^<>=!]+?)\s*%\s*(?<rhs>[A-Za-z0-9_\(\)\.\+\-\*\/\^]+)", m => {
            var lhs = m.Groups["lhs"].Value.Trim();
            var rhs = m.Groups["rhs"].Value.Trim();
            return $" mod({lhs}, {rhs})";
        });

        // a mod m -> mod(a, m)
        cleaned = Regex.Replace(cleaned, @"(?<lhs>[^<>=!]+?)\s+\bmod\s+(?<rhs>[A-Za-z0-9_\(\)\.\+\-\*\/\^]+)", m => {
            var lhs = m.Groups["lhs"].Value.Trim();
            var rhs = m.Groups["rhs"].Value.Trim();
            return $" mod({lhs}, {rhs})";
        });

        // Filter out asy drawing commands
        var asyCommands = new[] { "label(", "dot(", "draw(", "fill(", "filldraw(", "drawanglemark(", "drawrightanglemark(", "anglemark(", "rightanglemark(", "shipout(", "clip(" };
        if (asyCommands.Any(cmd => cleaned.StartsWith(cmd, StringComparison.OrdinalIgnoreCase))) return string.Empty;
        
        // Remove hallucinated radical simplification logic (e.g. floor(sqrt(N)) patterns)
        if (Regex.IsMatch(cleaned, @"\bfloor\s*\(\s*sqrt\s*\(", RegexOptions.IgnoreCase)) return string.Empty;
        if (cleaned.Contains("remainder", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(cleaned, @"\-\s*Pow\s*\(", RegexOptions.IgnoreCase)) return string.Empty;
        if (Regex.IsMatch(cleaned, @"\*\s*sqrt\s*\(\s*remainder\s*\)", RegexOptions.IgnoreCase)) return string.Empty;
        if (cleaned.Contains("simplified_form", StringComparison.OrdinalIgnoreCase) && cleaned.Contains("remainder", StringComparison.OrdinalIgnoreCase)) return string.Empty;

        // Map common number words to digits if they appear as standalone terms in constraints
        cleaned = ReplaceNumberWords(cleaned);

        // Handle nth roots sqrt[n](x) -> Pow(x, 1/n)
        cleaned = Regex.Replace(cleaned, @"\bsqrt\[\s*(\d+)\s*\]\s*\((.+?)\)", "Pow(($2), 1/($1))", RegexOptions.IgnoreCase);

        // Fix lost carets in common power patterns: h12 -> h1^2, k12 -> k1^2
        // Specifically handle indexed variables followed by 2 or 3.
        cleaned = Regex.Replace(cleaned, @"\b([A-Za-z_][A-Za-z0-9_]*[0-9])([23])\b", "$1^$2");

        // Fix distance formula extraction error: (a-b)*2 + (c-d)*2 -> (a-b)^2 + (c-d)^2
        cleaned = Regex.Replace(cleaned, @"(\([^()]+\))\s*\*\s*2\s*\+\s*(\([^()]+\))\s*\*\s*2", "Pow($1, 2) + Pow($2, 2)");

        // Normalize Sum(1 * for i in interval(...) if condition) -> Count(i, interval(...), condition)
        cleaned = Regex.Replace(cleaned, @"\bSum\s*\(\s*1\s*\*\s*for\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+interval\((.+?)\)\s+\*\s+if\s+(.+?)\s*\)", "Count($1, interval($2), $3)", RegexOptions.IgnoreCase);
        // Normalize Sum(1 * for i in interval(...) where condition)
        cleaned = Regex.Replace(cleaned, @"\bSum\s*\(\s*1\s*\*\s*for\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+interval\((.+?)\)\s+if\s+(.+?)\s*\)", "Count($1, interval($2), $3)", RegexOptions.IgnoreCase);
        // Normalize Sum(1 for i in interval(...) if condition)
        cleaned = Regex.Replace(cleaned, @"\bSum\s*\(\s*1\s+for\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+interval\((.+?)\)\s+if\s+(.+?)\s*\)", "Count($1, interval($2), $3)", RegexOptions.IgnoreCase);
        // More general count normalization: Count(1 * for n in interval(...) if condition)
        cleaned = Regex.Replace(cleaned, @"\bCount\s*\(\s*1\s*\*\s*for\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+interval\((.+?)\)\s+\*\s+if\s+(.+?)\s*\)", "Count($1, interval($2), $3)", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bCount\s*\(\s*1\s+for\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+interval\((.+?)\)\s+if\s+(.+?)\s*\)", "Count($1, interval($2), $3)", RegexOptions.IgnoreCase);
        // Sum(1 * if condition) -> Count(condition) if it's over a single variable or just general
        cleaned = Regex.Replace(cleaned, @"\bSum\s*\(\s*1\s*\*\s*if\s+(.+?)\s*\)", "Count($1)", RegexOptions.IgnoreCase);

        // Normalize Sum(expr for i from start to end) -> Sum(expr, i, start, end)
        cleaned = Regex.Replace(cleaned, @"\bSum\s*\(\s*(.+?)\s+for\s+([A-Za-z_][A-Za-z0-9_]*)\s+from\s+(.+?)\s+to\s+(.+?)\s*\)", "Sum($1, $2, $3, $4)", RegexOptions.IgnoreCase);

        if (options.NormalizePowCalls)
        {
            cleaned = MathPowRegex.Replace(cleaned, "Pow($1, $2)");
            cleaned = PowCallRegex.Replace(cleaned, "Pow($1, $2)");
        }

        // Normalize absPow/floorPow/ceilPow into standard Pow + Abs/Floor/Ceil
        cleaned = AbsPowRegex.Replace(cleaned, "Pow(Abs($1), $2)");
        cleaned = FloorPowRegex.Replace(cleaned, "Pow(floor($1), $2)");
        cleaned = CeilPowRegex.Replace(cleaned, "Pow(ceil($1), $2)");

        // Normalize Sqrt
        cleaned = Regex.Replace(cleaned, @"\bMath\.Sqrt\s*\(", "Sqrt(", RegexOptions.IgnoreCase);
        
        // Normalize Abs
        cleaned = Regex.Replace(cleaned, @"\bMath\.Abs\s*\(", "Abs(", RegexOptions.IgnoreCase);

        // Normalize Pi
        cleaned = Regex.Replace(cleaned, @"\bMath\.PI\b", "pi", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bpi_val\b", "pi", RegexOptions.IgnoreCase);

        if (options.NormalizeCaretPowers)
        {
            // First handle simple cases like x^2
            cleaned = CaretPowRegex.Replace(cleaned, "Pow($1, $2)");
            
            // Then handle more complex cases with parenthesized bases or exponents: (a+b)^(c+d)
            // This is a simple recursive-ish replacement for common parenthesized patterns.
            cleaned = Regex.Replace(cleaned, @"\(([^()]+)\)\s*\^\s*\(([^()]+)\)", "Pow(($1), ($2))");
            cleaned = Regex.Replace(cleaned, @"([A-Za-z0-9_]+)\s*\^\s*\(([^()]+)\)", "Pow($1, ($2))");
            cleaned = Regex.Replace(cleaned, @"\(([^()]+)\)\s*\^\s*([A-Za-z0-9_]+)", "Pow(($1), $2)");
        }

        // Normalize [a, b] -> interval(a, b)
        cleaned = Regex.Replace(cleaned, @"\[([^,]+),\s*([^\]]+)\]", "interval($1, $2)");

        cleaned = cleaned.Replace("∀", "forall", StringComparison.Ordinal)
                         .Replace("∃", "exists", StringComparison.Ordinal);
        cleaned = Regex.Replace(cleaned, @"\bfor\s+all\b", "forall", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bthere\s+exists\b", "exists", RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(
            cleaned,
            @"\bforall\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s*\{(.+?)\}\s*(?:,|:|if|where|such\s+that)?\s*(.+)",
            "ForAll($1, Vector($2), $3)",
            RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(
            cleaned,
            @"\bexists\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s*\{(.+?)\}\s*(?:,|:|if|where|such\s+that)?\s*(.+)",
            "Exists($1, Vector($2), $3)",
            RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(
            cleaned,
            @"\bforall\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+Vector\((.+?)\)\s*(?:,|:|if|where|such\s+that)?\s*(.+)",
            "ForAll($1, Vector($2), $3)",
            RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(
            cleaned,
            @"\bexists\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+Vector\((.+?)\)\s*(?:,|:|if|where|such\s+that)?\s*(.+)",
            "Exists($1, Vector($2), $3)",
            RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(
            cleaned,
            @"\bforall\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+interval\((.+?)\)\s*(?:,|:|if|where|such\s+that)?\s*(.+)",
            "ForAll($1, interval($2), $3)",
            RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(
            cleaned,
            @"\bexists\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+interval\((.+?)\)\s*(?:,|:|if|where|such\s+that)?\s*(.+)",
            "Exists($1, interval($2), $3)",
            RegexOptions.IgnoreCase);

        // 1. Coordinate tuples to vectors: P == (-3, 4) -> P == Vector(-3, 4)
        // Ensure we don't match function calls like diamond(x, y) by checking for an identifier prefix.
        cleaned = Regex.Replace(cleaned, @"(?<===\s*)\(([^,()]+,\s*)+([^,()]+)\)", m => "Vector" + m.Value);
        cleaned = Regex.Replace(cleaned, @"(?<![A-Za-z0-9_])\(([^,()]+,\s*)+([^,()]+)\)(?=\s*==)", m => "Vector" + m.Value);
        // Standalone tuples (preceded by start, space, or operator, and followed by end, space, or operator)
        cleaned = Regex.Replace(cleaned, @"(?<=^|[\s=+\-*/,;])(?<![A-Za-z0-9_])\(([^,()]+,\s*)+([^,()]+)\)(?=$|[\s=+\-*/,;])", m => "Vector" + m.Value.Trim());

        // 2. Dot coordinate access to vector indexing: Q.x -> Q[1], Q.y -> Q[2], Q.z -> Q[3]
        cleaned = Regex.Replace(cleaned, @"([A-Za-z_][A-Za-z0-9_]*)\.x\b", "$1[1]");
        cleaned = Regex.Replace(cleaned, @"([A-Za-z_][A-Za-z0-9_]*)\.y\b", "$1[2]");
        cleaned = Regex.Replace(cleaned, @"([A-Za-z_][A-Za-z0-9_]*)\.z\b", "$1[3]");

        // 3. Repair malformed Piecewise separators: semicolons to commas
        if (cleaned.Contains("Piecewise", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = Regex.Replace(cleaned, @"Piecewise\s*\(([^)]+)\)", m => {
                var inner = m.Groups[1].Value;
                if (inner.Contains(';'))
                {
                    var repaired = inner.Replace(';', ',');
                    // Clean up leading/trailing commas and double commas
                    repaired = Regex.Replace(repaired, @",\s*,", ",");
                    repaired = repaired.Trim().TrimStart(',').TrimEnd(',').Trim();
                    return "Piecewise(" + repaired + ")";
                }
                return m.Value;
            }, RegexOptions.IgnoreCase);
        }

        // 4. Translate "perfect square" phrasing: x == a perfect square -> issquare(x)
        cleaned = Regex.Replace(cleaned, @"([A-Za-z_][A-Za-z0-9_]*)\s*==\s*(a\s+)?perfect\s+square", "issquare($1)", RegexOptions.IgnoreCase);

        // Filter out verbose assignments like 'x == pounds of flour' where RHS is natural language
        if (Regex.IsMatch(cleaned, @"^[A-Za-z_][A-Za-z0-9_]*\s*={1,2}\s*[A-Za-z]+[']?[s]?\s+[A-Za-z]+.*$"))
        {
            // If RHS contains "Vector", "Matrix", "Piecewise", "Sum", "Integral", "Derivative" etc, keep it.
            // But if it looks like plain text, drop it.
            var parts = cleaned.Split(new[] { "==", "=" }, 2, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                var rhs = parts[1].Trim();
                // Only drop if RHS is truly natural language (multi-word) and has no operators.
                // This avoids dropping valid expressions with multi-word identifiers, e.g.
                // probability == number of favorable outcomes / total possible outcomes
                if (!rhs.Contains("(") && rhs.Split(' ').Length > 1 && !ContainsOperator(rhs))
                {
                    // Likely natural language description
                    return string.Empty;
                }
            }
        }

        // 5. Fix mismatched bracket/indexer artifacts: range[1 ) or rangeinterval(1 ] -> range[1]
        // Spec: rangeinterval(1 ] -> range[1]
        cleaned = Regex.Replace(cleaned, @"\b(rangeinterval|range)\s*[(\[]\s*([^\]\)\s]+)\s*\s*[)\]]", "range[$2]");

        // Fix mangled intervals like interval(1] or interval(1 ) or f_rangeinterval(1]
        // Any other identifier ending in 'interval' becomes just 'interval'
        cleaned = Regex.Replace(cleaned, @"\b[A-Za-z0-9_]+interval\s*[(\[]\s*([^\]\)\s]+)\s*\s*[)\]]", "interval($1)");
        cleaned = Regex.Replace(cleaned, @"\binterval\s*[(\[]\s*([^\]\)\s]+)\s*\s*[)\]]", "interval($1)");
        
        // General ones for other identifiers (excluding interval which uses parens)
        cleaned = Regex.Replace(cleaned, @"(?<!\binterval|range|rangeinterval)([A-Za-z_][A-Za-z0-9_]*)\[\s*([^\]\)]+)\s*\s*\)", "$1[$2]");
        cleaned = Regex.Replace(cleaned, @"(?<!\binterval|range|rangeinterval)([A-Za-z_][A-Za-z0-9_]*)\(\s*([^\]\)]+)\s*\s*\]", "$1[$2]");

        // Fix weird Vector(x | x, ...) syntax
        cleaned = Regex.Replace(cleaned, @"\bVector\s*\(\s*[A-Za-z0-9_]+\s*\|\s*", "Vector(");

        // Fix ne(Var == Val) -> ne(Var, Val)
        cleaned = Regex.Replace(cleaned, @"\bne\s*\(\s*([^=,]+)\s*==\s*([^,)]+)\s*\)", m => "ne(" + m.Groups[1].Value.Trim() + ", " + m.Groups[2].Value.Trim() + ")");
        
        // Fix ne(Domain == Vector(...)) junk
        if (cleaned.Contains("ne(Domain ==", StringComparison.OrdinalIgnoreCase) || 
            cleaned.Contains("ne( Domain ==", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Contains("ne(Domain)", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (options.NormalizeImplicitMultiplication)
        {
            cleaned = ImplicitNumberSymbolRegex.Replace(cleaned, "$1 * $2");
            cleaned = ImplicitNumberSymbolNoSpaceRegex.Replace(cleaned, "$1 * $2");
            cleaned = ImplicitNumberParenRegex.Replace(cleaned, "$1 * (");
            cleaned = ImplicitCloseParenSymbolRegex.Replace(cleaned, ") * $1");
            cleaned = ImplicitParenParenRegex.Replace(cleaned, ") * (");
            cleaned = ImplicitSymbolParenNoSpaceRegex.Replace(cleaned, "$1 * (");
            // Treat (x)2 and (x)3 as powers (likely lost caret)
            cleaned = Regex.Replace(cleaned, @"\)\s*([23])\b", ")^$1");
            // Other digits follow standard implicit multiplication
            cleaned = Regex.Replace(cleaned, @"\)\s*([014-9]|\d{2,})", ") * $1");
        }

        if (options.NormalizeNotEqualConstraints && TryNormalizeNotEqual(cleaned, out var notEqual))
        {
            cleaned = notEqual;
        }

        if (options.ConvertPlaceholderTargets && TryNormalizePlaceholderTarget(cleaned, out var normalized))
        {
            if (normalized is null) return string.Empty;
            cleaned = normalized;
        }

        if (options.NormalizeIntegerConstraints && TryNormalizeIntegerConstraint(cleaned, out var integerConstraint))
        {
            cleaned = integerConstraint;
        }

        // Fix standalone declarations like 'x;' or 'x = ?' to just 'x'
        if (cleaned.Contains('?'))
        {
            var withoutQ = cleaned.Replace("?", "").Trim();
            if (withoutQ.EndsWith("==")) withoutQ = withoutQ[..^2].Trim();
            else if (withoutQ.EndsWith("=")) withoutQ = withoutQ[..^1].Trim();
            
            if (IdentifierRegex.IsMatch(withoutQ)) 
            {
                cleaned = withoutQ;
            }
            else if (string.IsNullOrWhiteSpace(withoutQ))
            {
                return string.Empty;
            }
            else
            {
                cleaned = withoutQ;
            }
        }

        // Final sanity check for implicit multiplication: 2pi, 2(x), etc.
        cleaned = Regex.Replace(cleaned, @"(\d+)(pi|theta|alpha|beta|gamma|delta|phi|sigma|omega)\b", "$1 * $2", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"(\d+)\(", "$1 * (");

        // Filter out lines that look like natural language explanations even if they contain operators.
        if (IsLikelyNaturalLanguage(cleaned))
        {
            return string.Empty;
        }

        // Final sanity check: if it still contains a lot of spaces and no assignment/comparison, it's probably not code.
        // BUT: if it has many parentheses, it's likely a nested function call (code).
        int parenCount = cleaned.Count(c => c == '(' || c == ')');
        if (cleaned.Split(' ').Length > 4 && 
            !cleaned.Contains('=', StringComparison.Ordinal) && 
            !cleaned.Contains('<', StringComparison.Ordinal) && 
            !cleaned.Contains('>', StringComparison.Ordinal) &&
            parenCount < 4)
        {
            return string.Empty;
        }

        // Safety: drop lines with unmatched brackets or empty identifiers
        if (!IsBalanced(cleaned)) return string.Empty;

        return cleaned.Trim();
    }

    private static string NormalizeLogicSyntax(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var cleaned = text;

        cleaned = Regex.Replace(cleaned, @"\bif\s+and\s+only\s+if\b", "iff", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"^\s*if\s+(.+?)\s+then\s+(.+)$", "implies($1, $2)", RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(cleaned, @"\bAnd\s*\(", "and(", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bOr\s*\(", "or(", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bNot\s*\(", "not(", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bImplies\s*\(", "implies(", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bIff\s*\(", "iff(", RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(cleaned, @"\btrue\b", "true", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bfalse\b", "false", RegexOptions.IgnoreCase);

        if (ContainsOperator(cleaned) || LooksLikeQuantifierHint(cleaned))
        {
            cleaned = Regex.Replace(cleaned, @"\bnot\s+([A-Za-z_][A-Za-z0-9_]*|\([^()]*\))", "not($1)", RegexOptions.IgnoreCase);
            cleaned = NormalizeLogicInfix(cleaned);
        }

        return cleaned;
    }

    private static string NormalizeLogicInfix(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var commaParts = SplitByTopLevelDelimiter(text, ',');
        if (commaParts.Count > 1)
        {
            var normalized = commaParts.Select(NormalizeLogicInfix).ToList();
            return string.Join(", ", normalized);
        }

        var rebuilt = NormalizeLogicInfixInParens(text);
        rebuilt = NormalizeInfixAtTopLevel(rebuilt, "iff", "iff");
        rebuilt = NormalizeInfixAtTopLevel(rebuilt, "implies", "implies");
        rebuilt = NormalizeInfixAtTopLevel(rebuilt, "or", "or");
        rebuilt = NormalizeInfixAtTopLevel(rebuilt, "and", "and");
        return rebuilt;
    }

    private static string NormalizeLogicInfixInParens(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '(' && TryFindMatchingParen(text, i, out int end))
            {
                var inner = text.Substring(i + 1, end - i - 1);
                var normalizedInner = NormalizeLogicInfix(inner);
                sb.Append('(').Append(normalizedInner).Append(')');
                i = end + 1;
                continue;
            }

            sb.Append(text[i]);
            i++;
        }
        return sb.ToString();
    }

    private static string NormalizeInfixAtTopLevel(string text, string keyword, string functionName)
    {
        var parts = SplitByTopLevelKeyword(text, keyword);
        if (parts.Count <= 1) return text;
        var normalizedParts = parts.Select(NormalizeLogicInfix).ToList();
        return $"{functionName}({string.Join(", ", normalizedParts)})";
    }

    private static List<string> SplitByTopLevelKeyword(string text, string keyword)
    {
        var parts = new List<string>();
        int parens = 0;
        int brackets = 0;
        int lastStart = 0;
        for (int i = 0; i <= text.Length - keyword.Length; i++)
        {
            var ch = text[i];
            if (ch == '(') { parens++; continue; }
            if (ch == ')') { parens--; continue; }
            if (ch == '[') { brackets++; continue; }
            if (ch == ']') { brackets--; continue; }
            if (parens != 0 || brackets != 0) continue;

            if (IsKeywordAt(text, i, keyword))
            {
                var segment = text.Substring(lastStart, i - lastStart).Trim();
                if (segment.Length > 0) parts.Add(segment);
                i += keyword.Length - 1;
                lastStart = i + 1;
            }
        }

        var tail = text.Substring(lastStart).Trim();
        if (tail.Length > 0) parts.Add(tail);
        return parts;
    }

    private static List<string> SplitByTopLevelDelimiter(string text, char delimiter)
    {
        var parts = new List<string>();
        int parens = 0;
        int brackets = 0;
        int lastStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '(') parens++;
            else if (ch == ')') parens--;
            else if (ch == '[') brackets++;
            else if (ch == ']') brackets--;
            else if (parens == 0 && brackets == 0 && ch == delimiter)
            {
                var segment = text.Substring(lastStart, i - lastStart).Trim();
                if (segment.Length > 0) parts.Add(segment);
                lastStart = i + 1;
            }
        }

        var tail = text.Substring(lastStart).Trim();
        if (tail.Length > 0) parts.Add(tail);
        return parts;
    }

    private static bool TryFindTopLevelDoubleEquals(string text, out int index)
    {
        index = -1;
        int parens = 0;
        int brackets = 0;
        for (int i = 0; i < text.Length - 1; i++)
        {
            var ch = text[i];
            if (ch == '(') { parens++; continue; }
            if (ch == ')') { parens--; continue; }
            if (ch == '[') { brackets++; continue; }
            if (ch == ']') { brackets--; continue; }
            if (parens != 0 || brackets != 0) continue;

            if (text[i] == '=' && text[i + 1] == '=')
            {
                index = i;
                return true;
            }
        }
        return false;
    }

    private static bool HasTopLevelEquals(string text)
    {
        int parens = 0;
        int brackets = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '(') { parens++; continue; }
            if (ch == ')') { parens--; continue; }
            if (ch == '[') { brackets++; continue; }
            if (ch == ']') { brackets--; continue; }
            if (parens != 0 || brackets != 0) continue;

            if (ch == '=') return true;
        }
        return false;
    }

    private static bool IsKeywordAt(string text, int index, string keyword)
    {
        if (index < 0 || index + keyword.Length > text.Length) return false;
        if (!text.AsSpan(index, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase)) return false;

        if (index > 0 && IsIdentifierChar(text[index - 1])) return false;
        int end = index + keyword.Length;
        if (end < text.Length && IsIdentifierChar(text[end])) return false;

        bool hasLeftOperand = false;
        for (int k = index - 1; k >= 0; k--)
        {
            if (char.IsWhiteSpace(text[k])) continue;
            var prev = text[k];
            hasLeftOperand = prev == ')' || prev == ']' || char.IsLetterOrDigit(prev) || prev == '_' || prev == '.';
            break;
        }

        int j = end;
        while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
        if (j < text.Length && text[j] == '(' && !hasLeftOperand) return false;

        return true;
    }

    private static bool IsIdentifierChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';

    private static bool TryFindMatchingParen(string text, int start, out int end)
    {
        end = -1;
        if (start < 0 || start >= text.Length || text[start] != '(') return false;
        int depth = 0;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    end = i;
                    return true;
                }
            }
        }
        return false;
    }

    private static string NormalizeLatexSubscripts(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        return Regex.Replace(text, @"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*_\s*\{(?<sub>[^}]+)\}", match =>
        {
            var name = match.Groups["name"].Value.Trim();
            var sub = match.Groups["sub"].Value;
            var normalized = NormalizeSubscriptToken(sub);
            return string.IsNullOrEmpty(normalized) ? name : $"{name}_{normalized}";
        });
    }

    private static string NormalizeSubscriptToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var cleaned = Regex.Replace(raw, @"\s+", "");
        cleaned = cleaned.Replace("-", "_minus_", StringComparison.Ordinal)
            .Replace("+", "_plus_", StringComparison.Ordinal)
            .Replace("*", "_mul_", StringComparison.Ordinal)
            .Replace("/", "_div_", StringComparison.Ordinal)
            .Replace("^", "_pow_", StringComparison.Ordinal);
        cleaned = Regex.Replace(cleaned, @"[^A-Za-z0-9_]+", "_");
        cleaned = Regex.Replace(cleaned, @"_+", "_").Trim('_');
        return cleaned;
    }

    private static string NormalizeAbsoluteValueBars(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("|", StringComparison.Ordinal)) return text;
        if (text.Contains("||", StringComparison.Ordinal)) return text;

        var normalized = text;
        var pattern = new Regex(@"\|([^|]+)\|");
        while (true)
        {
            var next = pattern.Replace(normalized, "Abs($1)");
            if (string.Equals(next, normalized, StringComparison.Ordinal)) break;
            normalized = next;
        }
        return normalized;
    }

    private static string NormalizeIndexedAggregates(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        text = NormalizeIndexedAggregate(text, "Sum");
        text = NormalizeIndexedAggregate(text, "Product");
        return text;
    }

    private static string NormalizeIndexedAggregate(string text, string keyword)
    {
        var replaced = Regex.Replace(text, $@"\b{keyword}\s*_\s*\{{[^}}]+\}}\s*", $"{keyword} ");
        replaced = Regex.Replace(replaced, $@"\b{keyword}\s+\(", $"{keyword}(");
        replaced = Regex.Replace(replaced, $@"\b{keyword}\s+(?!\()(?<arg>[A-Za-z_][A-Za-z0-9_]*)", $"{keyword}(${{arg}})");
        return replaced;
    }

    private static string ReplaceNumberWords(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "zero", "0" }, { "one", "1" }, { "two", "2" }, { "three", "3" }, { "four", "4" },
            { "five", "5" }, { "six", "6" }, { "seven", "7" }, { "eight", "8" }, { "nine", "9" },
            { "ten", "10" }, { "eleven", "11" }, { "twelve", "12" }, { "thirteen", "13" }, { "fourteen", "14" },
            { "fifteen", "15" }, { "sixteen", "16" }, { "seventeen", "17" }, { "eighteen", "18" }, { "nineteen", "19" },
            { "twenty", "20" }, { "twice", "2" }, { "thrice", "3" },
            { "half", "0.5" }, { "quarter", "0.25" }
        };

        var result = text;
        foreach (var kv in map)
        {
            // Only replace if it's a standalone word (bounded by whitespace, operators, or start/end)
            var pattern = @"(?<=^|[\s=+\-*/,;\(\)\[\]])" + kv.Key + @"(?=$|[\s=+\-*/,;\(\)\[\]])";
            result = Regex.Replace(result, pattern, kv.Value, RegexOptions.IgnoreCase);
        }
        return result;
    }

    private static bool IsBalanced(string text)
    {
        int parens = 0;
        int brackets = 0;
        foreach (var c in text)
        {
            if (c == '(') parens++;
            else if (c == ')') parens--;
            else if (c == '[') brackets++;
            else if (c == ']') brackets--;

            if (parens < 0 || brackets < 0) return false;
        }
        return parens == 0 && brackets == 0;
    }

    private static bool TryUnwrapLabeledComparison(string line, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(line)) return false;

        var match = Regex.Match(line, @"^(?<lhs>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?![=])(?<rhs>.+)$");
        if (!match.Success) return false;

        var rhs = match.Groups["rhs"].Value.Trim();
        if (rhs.Length == 0) return false;

        rhs = StripOuterParentheses(rhs);

        if (!ContainsComparisonOperator(rhs))
        {
            return false;
        }

        normalized = rhs;
        return true;
    }

    private static bool ContainsComparisonOperator(string text)
    {
        if (text.Contains("==", StringComparison.Ordinal) ||
            text.Contains("!=", StringComparison.Ordinal) ||
            text.Contains("<=", StringComparison.Ordinal) ||
            text.Contains(">=", StringComparison.Ordinal))
        {
            return true;
        }

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '<' || ch == '>')
            {
                if (i + 1 < text.Length && text[i + 1] == '=')
                {
                    continue;
                }

                if (i > 0 && text[i - 1] == '=')
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static string StripOuterParentheses(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length < 2) return trimmed;
        if (trimmed[0] != '(' || trimmed[^1] != ')') return trimmed;

        var inner = trimmed[1..^1];
        return IsBalanced(inner) ? inner.Trim() : trimmed;
    }

    private static IEnumerable<string> SplitBySemicolon(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        int parens = 0;
        int brackets = 0;
        int lastStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') parens++;
            else if (text[i] == ')') parens--;
            else if (text[i] == '[') brackets++;
            else if (text[i] == ']') brackets--;
            else if (text[i] == ';' && parens == 0 && brackets == 0)
            {
                var segment = text.Substring(lastStart, i - lastStart).Trim();
                if (segment.Length > 0) yield return segment;
                lastStart = i + 1;
            }
        }
        if (lastStart < text.Length)
        {
            var segment = text.Substring(lastStart).Trim();
            if (segment.Length > 0) yield return segment;
        }
    }

    private static IEnumerable<string> SplitByTopLevelConjunctions(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        int parens = 0;
        int brackets = 0;
        int lastStart = 0;
        for (int i = 0; i < text.Length - 1; i++)
        {
            var ch = text[i];
            if (ch == '(') parens++;
            else if (ch == ')') parens--;
            else if (ch == '[') brackets++;
            else if (ch == ']') brackets--;

            if (parens == 0 && brackets == 0 && ch == '&' && text[i + 1] == '&')
            {
                var segment = text.Substring(lastStart, i - lastStart).Trim();
                if (segment.Length > 0) yield return segment;
                i++;
                lastStart = i + 1;
            }
        }

        if (lastStart < text.Length)
        {
            var segment = text.Substring(lastStart).Trim();
            if (segment.Length > 0) yield return segment;
        }
    }

    private static bool IsLikelyNaturalLanguage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        
        // If it contains a colon and many words, it's likely a labelled explanation.
        if (text.Contains(':') && text.Split(' ').Length > 3) return true;

        // If it contains "is" or "are" or "the" and many words
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 5)
        {
            var lowercase = text.ToUpperInvariant();
            if (lowercase.Contains(" IS ", StringComparison.Ordinal) || 
                lowercase.Contains(" ARE ", StringComparison.Ordinal) || 
                lowercase.Contains(" THE ", StringComparison.Ordinal) || 
                lowercase.Contains(" THAT ", StringComparison.Ordinal) || 
                lowercase.Contains(" WHICH ", StringComparison.Ordinal) ||
                lowercase.Contains(" SHOULD ", StringComparison.Ordinal) ||
                lowercase.Contains(" WOULD ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        // If it starts with "Note that" or similar and contains operators but no explicit equality
        var startsWithMeta = text.StartsWith("Note", StringComparison.OrdinalIgnoreCase) || 
                             text.StartsWith("Thus", StringComparison.OrdinalIgnoreCase) || 
                             text.StartsWith("Therefore", StringComparison.OrdinalIgnoreCase) ||
                             text.StartsWith("We ", StringComparison.OrdinalIgnoreCase) ||
                             text.StartsWith("If ", StringComparison.OrdinalIgnoreCase) ||
                             text.StartsWith("Let ", StringComparison.OrdinalIgnoreCase) ||
                             text.StartsWith("Then ", StringComparison.OrdinalIgnoreCase);

        if (startsWithMeta && text.Split(' ').Length > 4 && !text.Contains("==", StringComparison.Ordinal) && !text.Contains("<", StringComparison.Ordinal) && !text.Contains(">", StringComparison.Ordinal))
        {
            return true;
        }

        // Check for common LLM prefix phrases
        if (text.StartsWith("Here is", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("This can be", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("The answer is", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsLikelyExplanation(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (IsIdentifier(text)) return false;
        if (StartsWithMetaPrefix(text)) return true;
        if (LooksLikeQuantifierHint(text) || LooksLikeLogicHint(text)) return false;
        
        // If it starts with common "meta" keywords and has no operators, it's an explanation.
        var metaKeywords = new[] { "Note", "Constraint", "Bound", "Goal", "Target", "Extract", "The ", "This ", "Where ", "Find ", "Suppose ", "Given ", "If ", "What ", "Calculate ", "Solve ", "Assume " };
        var hasMeta = metaKeywords.Any(k => text.StartsWith(k, StringComparison.OrdinalIgnoreCase));
        
        if (!ContainsOperator(text))
        {
            if (hasMeta || text.Split(' ').Length > 3) return true;
        }

        // If it contains "extracted from" or similar phrases
        if (text.Contains("extracted from", StringComparison.Ordinal)) return true;
        if (text.Contains("ensure", StringComparison.Ordinal) && !ContainsOperator(text)) return true;
        if (text.Contains(" where ", StringComparison.Ordinal) && !ContainsOperator(text) && !LooksLikeQuantifierHint(text)) return true;
        if (text.Contains(" such that ", StringComparison.Ordinal) && !LooksLikeQuantifierHint(text)) return true;
        if (text.Contains(" satisfy ", StringComparison.Ordinal) && !LooksLikeQuantifierHint(text)) return true;
        if (text.Contains(" is ", StringComparison.Ordinal) && !ContainsOperator(text)) return true;
        if (text.Contains(" are ", StringComparison.Ordinal) && !ContainsOperator(text)) return true;
        if (text.Contains(" units ", StringComparison.Ordinal) && !ContainsOperator(text)) return true;

        return false;
    }

    private static bool StartsWithMetaPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.StartsWith("Note", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Thus", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Therefore", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeQuantifierHint(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("forall", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("exists", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(text, @"\bfor\s+all\b", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(text, @"\bthere\s+exists\b", RegexOptions.IgnoreCase);
    }

    private static bool LooksLikeLogicHint(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Contains("&&", StringComparison.Ordinal) || text.Contains("||", StringComparison.Ordinal)) return true;
        if (text.Contains("!", StringComparison.Ordinal) && ContainsOperator(text)) return true;
        if (Regex.IsMatch(text, @"\b(implies|iff|and|or|not)\b", RegexOptions.IgnoreCase))
        {
            return ContainsOperator(text) || text.Contains("(", StringComparison.Ordinal);
        }
        return false;
    }

    private static bool TryNormalizePlaceholderTarget(string line, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(line)) return false;

        var trimmed = line.Trim().TrimEnd(';').Trim();
        if (trimmed.Length == 0) return false;

        // Strip "int " or "decimal " prefixes for placeholder checks
        var cleanLine = trimmed;
        if (cleanLine.StartsWith("int ", StringComparison.OrdinalIgnoreCase)) cleanLine = cleanLine[4..].Trim();
        else if (cleanLine.StartsWith("decimal ", StringComparison.OrdinalIgnoreCase)) cleanLine = cleanLine[8..].Trim();

        if (cleanLine.EndsWith("?"))
        {
            var withoutQ = cleanLine[..^1].Trim();
            if (withoutQ.EndsWith("==", StringComparison.Ordinal))
            {
                withoutQ = withoutQ[..^2].Trim();
            }
            else if (withoutQ.EndsWith("=", StringComparison.Ordinal))
            {
                withoutQ = withoutQ[..^1].Trim();
            }

            if (IsIdentifier(withoutQ))
            {
                normalized = withoutQ;
                return true;
            }

            if (TryExtractIdentifier(withoutQ, out var extracted))
            {
                normalized = extracted;
                return true;
            }
        }

        // Check for common assignment patterns to placeholders
        var assignmentMatch = Regex.Match(cleanLine, @"^(?<lhs>[A-Za-z_][A-Za-z0-9_]*)\s*={1,2}\s*(?<rhs>.+)$", RegexOptions.IgnoreCase);
        if (assignmentMatch.Success)
        {
            var lhs = assignmentMatch.Groups["lhs"].Value.Trim();
            var rhs = assignmentMatch.Groups["rhs"].Value.Trim();
            var placeholders = new[] { "Variable", "Unknown", "Value", "Constant", "Number", "Integer", "RealNumber", "Real", "Decimal" };
            
            bool isPlaceholder = placeholders.Any(p => 
                string.Equals(rhs, p, StringComparison.OrdinalIgnoreCase) || 
                string.Equals(rhs, p + "()", StringComparison.OrdinalIgnoreCase));

            if (isPlaceholder)
            {
                // Return false/null to indicate this line should be dropped
                normalized = null;
                return true;
            }
        }

        if (cleanLine.EndsWith("==", StringComparison.Ordinal))
        {
            var candidate = cleanLine[..^2].Trim();
            if (IsIdentifier(candidate))
            {
                normalized = candidate;
                return true;
            }
        }

        if (cleanLine.EndsWith("=", StringComparison.Ordinal))
        {
            var candidate = cleanLine[..^1].Trim();
            if (IsIdentifier(candidate))
            {
                normalized = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractIdentifier(string text, out string identifier)
    {
        identifier = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split("==", StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && string.IsNullOrWhiteSpace(parts[1]) && IsIdentifier(parts[0]))
        {
            identifier = parts[0];
            return true;
        }

        parts = text.Split("=", StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && string.IsNullOrWhiteSpace(parts[1]) && IsIdentifier(parts[0]))
        {
            identifier = parts[0];
            return true;
        }

        return false;
    }

    private static bool TryNormalizeNotEqual(string line, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(line)) return false;
        if (line.Contains("&&", StringComparison.Ordinal) || line.Contains("||", StringComparison.Ordinal)) return false;

        var trimmed = line.Trim();
        var hasSemi = trimmed.EndsWith(";", StringComparison.Ordinal);
        var content = hasSemi ? trimmed[..^1].Trim() : trimmed;

        var match = NotEqualRegex.Match(content);
        if (!match.Success) return false;

        var lhs = match.Groups["lhs"].Value.Trim();
        var rhs = match.Groups["rhs"].Value.Trim();
        if (lhs.Length == 0 || rhs.Length == 0) return false;

        normalized = $"ne({lhs}, {rhs}){(hasSemi ? ";" : "")}";
        return true;
    }

    private static bool TryNormalizeIntegerConstraint(string line, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(line)) return false;

        var trimmed = line.Trim();
        var hasSemi = trimmed.EndsWith(";", StringComparison.Ordinal);
        var content = hasSemi ? trimmed[..^1].Trim() : trimmed;

        var match = IntegerConstraintRegex.Match(content);
        if (match.Success)
        {
            var variable = match.Groups["var"].Value.Trim();
            if (variable.Length == 0) return false;
            normalized = $"Integer({variable}){(hasSemi ? ";" : "")}";
            return true;
        }

        match = IntegerConstraintReverseRegex.Match(content);
        if (match.Success)
        {
            var variable = match.Groups["var"].Value.Trim();
            if (variable.Length == 0) return false;
            normalized = $"Integer({variable}){(hasSemi ? ";" : "")}";
            return true;
        }

        return false;
    }

    private static bool IsIdentifier(string text) => IdentifierRegex.IsMatch(text);

    private static bool IsBooleanLiteral(string text)
        => text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
           text.Equals("false", StringComparison.OrdinalIgnoreCase);

    private static bool IsLikelyMarkdownBullet(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (IsIdentifier(text)) return false;
        if (ContainsOperator(text)) return false;
        return char.IsLetter(text[0]);
    }

    private static bool ContainsOperator(string text)
    {
        return text.Contains("==", StringComparison.Ordinal) ||
               text.Contains("!=", StringComparison.Ordinal) ||
               text.Contains("<=", StringComparison.Ordinal) ||
               text.Contains(">=", StringComparison.Ordinal) ||
               text.Contains("<", StringComparison.Ordinal) ||
               text.Contains(">", StringComparison.Ordinal) ||
               text.Contains("=", StringComparison.Ordinal) ||
               text.Contains("&&", StringComparison.Ordinal) ||
               text.Contains("||", StringComparison.Ordinal) ||
               text.Contains("!", StringComparison.Ordinal) ||
               text.Contains("+", StringComparison.Ordinal) ||
               text.Contains("-", StringComparison.Ordinal) ||
               text.Contains("*", StringComparison.Ordinal) ||
               text.Contains("/", StringComparison.Ordinal) ||
               text.Contains("(", StringComparison.Ordinal) ||
               text.Contains(")", StringComparison.Ordinal);
    }

    private static string StripLineComment(string line)
    {
        if (line is null) return string.Empty;
        int idx = line.IndexOf("//", StringComparison.Ordinal);
        return idx >= 0 ? line.Substring(0, idx) : line;
    }

    private static readonly Regex PowCallRegex =
        new(@"\b([A-Za-z_][A-Za-z0-9_]*)\.Pow\s*\(\s*([^\)]+)\s*\)", RegexOptions.Compiled);

    private static readonly Regex MathPowRegex =
        new(@"\bMath\.Pow\s*\(\s*([^,]+)\s*,\s*([^\)]+)\s*\)", RegexOptions.Compiled);

    private static readonly Regex CaretPowRegex =
        new(@"\b([A-Za-z_][A-Za-z0-9_]*)\s*\^\s*\{?([0-9]+)\}?\b", RegexOptions.Compiled);

    private static readonly Regex AbsPowRegex =
        new(@"\babsPow\s*\(\s*([^,]+)\s*,\s*([^\)]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FloorPowRegex =
        new(@"\bfloorPow\s*\(\s*([^,]+)\s*,\s*([^\)]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CeilPowRegex =
        new(@"\bceilPow\s*\(\s*([^,]+)\s*,\s*([^\)]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ImplicitNumberSymbolRegex =
        new(@"(?<![A-Za-z0-9_])(\d+(?:\.\d+)?)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    private static readonly Regex ImplicitNumberSymbolNoSpaceRegex =
        new(@"(?<![A-Za-z0-9_])(\d+(?:\.\d+)?)(?![eE][+-]?\d+)([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    private static readonly Regex ImplicitNumberParenRegex =
        new(@"(?<![A-Za-z0-9_])(\d+(?:\.\d+)?)\s*\(", RegexOptions.Compiled);

    private static readonly Regex ImplicitCloseParenSymbolRegex =
        new(@"\)\s*([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    private static readonly Regex ImplicitParenParenRegex =
        new(@"\)\s*\(", RegexOptions.Compiled);

    private static readonly Regex ImplicitSymbolParenNoSpaceRegex =
        new(@"(\d+)\(", RegexOptions.Compiled);

    private static readonly Regex NotEqualRegex =
        new(@"^(?<lhs>.+?)\s*!=\s*(?<rhs>.+)$", RegexOptions.Compiled);

    private static readonly Regex IntegerConstraintRegex =
        new(@"^(?<var>[A-Za-z_][A-Za-z0-9_]*)\s*==\s*(?:Is)?Integer\s*\(\s*\k<var>\s*\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex IntegerConstraintReverseRegex =
        new(@"^(?:Is)?Integer\s*\(\s*(?<var>[A-Za-z_][A-Za-z0-9_]*)\s*\)\s*==\s*\k<var>\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex IdentifierRegex =
        new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
}
