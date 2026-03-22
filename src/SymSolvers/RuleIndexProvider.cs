using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Core;
using Sym.Core.Rewriters;
using SymRules;

namespace SymSolvers
{
    public static class RuleIndexProvider
    {
        private static SymRules.IRuleIndex? _cachedIndex;
        private static SymRules.IRuleIndex? _cachedTrigIndex;
        private static readonly object _lock = new();

        public static SymRules.IRuleIndex GetRuleIndex(SolveContext context)
        {
            if (_cachedIndex != null) return _cachedIndex;
            lock (_lock)
            {
                if (_cachedIndex != null) return _cachedIndex;
                var baseRules = RuleProvider.BuildRules(context);
                var created = Sym.Core.RuleIndex.Create(baseRules);
                _cachedIndex = new SymRules.RuleIndexAdapter(created);
                return _cachedIndex;
            }
        }

        public static SymRules.IRuleIndex GetTrigRuleIndex(SolveContext context)
        {
            if (_cachedTrigIndex != null) return _cachedTrigIndex;
            lock (_lock)
            {
                if (_cachedTrigIndex != null) return _cachedTrigIndex;
                var baseRules = RuleProvider.BuildRules(context);
                var trigRules = LoadTrigRules();
                var combined = baseRules.AddRange(trigRules);
                var created = Sym.Core.RuleIndex.Create(combined);
                _cachedTrigIndex = new SymRules.RuleIndexAdapter(created);
                return _cachedTrigIndex;
            }
        }

        private static IReadOnlyList<Sym.Core.Rule> LoadTrigRules()
        {
            var pack = RulePackLibrary.GetRulePacks().FirstOrDefault(p => p.Name.Equals("Trigonometry", System.StringComparison.OrdinalIgnoreCase));
            if (pack == null) return ImmutableList<Sym.Core.Rule>.Empty;
            return pack.Rules;
        }
    }
}
