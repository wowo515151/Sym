// Copyright Warren Harding 2026
namespace SymRules.Inequality
{
    // Minimal inequality helper to provide a concrete implementation for SymRules.
    // Keeps implementation tiny to avoid changing solver behavior.
    public static class InequalityRule
    {
        public static bool IsLess(int a, int b) => a < b;
    }
}
