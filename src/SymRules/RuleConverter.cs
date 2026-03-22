// Copyright Warren Harding 2026
// Minimal converter to bridge SymRules.Rule and Sym.Core.Rule
using System;
using System.Collections.Generic;
using Sym.CSharpIO;
using Sym.Core;

namespace SymRules
{
    public static class RuleConverter
    {
        public static Sym.Core.Rule? ToCoreRule(this RuleDefinition rule)
        {
            if (rule is null) throw new ArgumentNullException(nameof(rule));
            if (string.IsNullOrWhiteSpace(rule.CoreSource)) return null;

            try
            {
                var parsed = CSharpIO.ParseRules(rule.CoreSource);
                if (parsed == null || parsed.Count == 0) return null;

                var coreRule = parsed[0];
                return new Sym.Core.Rule(
                    coreRule.Pattern,
                    coreRule.Replacement,
                    coreRule.Condition,
                    null,
                    null)
                {
                    Name = rule.Name,
                    Transform = null
                };
            }
            catch (Exception ex)
            {
                if (Environment.GetEnvironmentVariable("SYM_DEBUG_RULES") == "1")
                    Console.WriteLine($"DEBUG: RuleConverter failed to parse '{rule.CoreSource}': {ex.Message}");
                return null;
            }
        }
    }
}