using System.Collections.Immutable;
using Sym.Core;

namespace SymRules
{
    public sealed class RuleIndexAdapter : IRuleIndex
    {
        private readonly RuleIndex _inner;
        public RuleIndexAdapter(RuleIndex inner) => _inner = inner;

        public ImmutableList<Sym.Core.Rule> AllRules => _inner.AllRules;

        public ImmutableList<Sym.Core.Rule> GetCandidateRules(IExpression expr)
        {
            var candidates = _inner.GetCandidateRules(expr);
            return candidates is ImmutableList<Sym.Core.Rule> list ? list : candidates.ToImmutableList();
        }

        // Expose inner core index for consumers that need RuleIndex directly (temporary migration aid)
        public RuleIndex GetInner() => _inner;
    }
}
