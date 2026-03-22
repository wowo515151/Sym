// Copyright Warren Harding 2026
using System;
using System.Text.RegularExpressions;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace SymRules
{
    public static class RuleTextParser
    {
        private static readonly Regex TypedWildRegex = new Regex(@"\?([a-zA-Z0-9_]+):([a-zA-Z0-9_]+)", RegexOptions.Compiled);
        private static readonly Regex SimpleWildRegex = new Regex(@"\?([a-zA-Z0-9_]+)", RegexOptions.Compiled);
        private static readonly Regex ConventionWildRegex = new Regex(@"\b([a-df-hj-z])\b(?!\s*\()", RegexOptions.Compiled); // a-d, f-h, j-z (skip e, i for math constants)

        public static bool TryGenerateCoreSource(string text, out string coreSource, out string? diagnostic)
        {
            diagnostic = null;
            coreSource = string.Empty;
            if (string.IsNullOrWhiteSpace(text)) { diagnostic = "Empty rule"; return false; }

            var t = text.Trim();
            if (t.StartsWith("Rule(", StringComparison.OrdinalIgnoreCase) && t.EndsWith(")", StringComparison.Ordinal))
            {
                coreSource = t;
                return true;
            }

            int arrowIdx = t.IndexOf("->", StringComparison.Ordinal);
            if (arrowIdx == -1) arrowIdx = t.IndexOf("=>", StringComparison.Ordinal);

            if (arrowIdx > 0)
            {
                var prefixPart = t.Substring(0, arrowIdx);
                var lastColonBeforeArrow = prefixPart.LastIndexOf(':');
                if (lastColonBeforeArrow > 0)
                {
                    var nameCandidate = t.Substring(0, lastColonBeforeArrow).Trim();
                    if (IsValidName(nameCandidate)) t = t.Substring(lastColonBeforeArrow + 1).Trim();
                }
            }

            t = t.Replace("=>", "->");
            var parts = t.Split(new[] { "->" }, StringSplitOptions.None);
            if (parts.Length != 2) { diagnostic = "Rule must contain a single '->' separator"; return false; }

            var left = ProcessWildcards(parts[0].Trim());
            var right = ProcessWildcards(parts[1].Trim());

            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) { diagnostic = "Left or right side is empty"; return false; }

            coreSource = $"Rule({left}, {right})";
            return true;
        }

        private static string ProcessWildcards(string text)
        {
            text = TypedWildRegex.Replace(text, "Wild(\"$1\", $2)");
            text = SimpleWildRegex.Replace(text, "Wild(\"$1\")");
            
            // Also handle single-letter symbols as wildcards by convention if they aren't already Wild()
            // We skip 'e' and 'i' as they are often math constants.
            return ConventionWildRegex.Replace(text, m =>
            {
                int start = m.Index;
                if (start > 0 && text[start - 1] == '"' && start + 1 < text.Length && text[start + 1] == '"')
                    return m.Value;
                return $"Wild(\"{m.Value}\")";
            });
        }

        public static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.Any(c => c == '(' || c == ')' || c == ',' || c == '?' || c == '+' || c == '-' || c == '*' || c == '/' || char.IsWhiteSpace(c))) return false;
            return !new[] { "Num", "Sym", "Wild" }.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        public static string ExtractName(string text, string defaultName)
        {
            var trimmed = text.Trim();
            int arrowIdx = trimmed.IndexOf("->", StringComparison.Ordinal);
            if (arrowIdx == -1) arrowIdx = trimmed.IndexOf("=>", StringComparison.Ordinal);
            if (arrowIdx > 0)
            {
                var prefixPart = trimmed.Substring(0, arrowIdx);
                var lastColonBeforeArrow = prefixPart.LastIndexOf(':');
                if (lastColonBeforeArrow > 0)
                {
                    var nameCandidate = trimmed.Substring(0, lastColonBeforeArrow).Trim();
                    if (IsValidName(nameCandidate)) return nameCandidate;
                }
            }
            return defaultName;
        }

        public static RuleDefinition? Parse(string text, out string? diagnostic)
        {
            if (TryGenerateCoreSource(text, out var core, out diagnostic))
                return new RuleDefinition { Name = ExtractName(text, string.Empty), Text = text.Trim(), CoreSource = core };
            return null;
        }

        public static RuleDefinition[] LoadFromDirectory(string path, out string[] diagnostics)
        {
            var results = RuleLoader.LoadRules(path).ToList();
            diagnostics = results.Where(r => r.Diagnostics != null).Select(r => r.Diagnostics!).ToArray();
            return results.ToArray();
        }
    }
}