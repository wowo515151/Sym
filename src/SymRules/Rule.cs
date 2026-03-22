// Copyright Warren Harding 2026
using System;
namespace SymRules
{
    public class RuleDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        // Generated C# core source (e.g., "Rule(a + 0, a);") when parsing textual rules.
        public string CoreSource { get; set; } = string.Empty;
        // If parser produced diagnostics for this rule line, they are captured here.
        public string? Diagnostics { get; set; }
    }
}

