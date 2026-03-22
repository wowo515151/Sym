// Minimal canonical rule index interface for Phase 3 M2
using System.Collections.Immutable;
using Sym.Core;

namespace SymRules
{
    public interface IRuleIndex
    {
        ImmutableList<Sym.Core.Rule> AllRules { get; }
        ImmutableList<Sym.Core.Rule> GetCandidateRules(IExpression expr);
    }
}
