// Copyright Warren Harding 2026
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Core.EGraph;

namespace SymCobra.Regions;

public sealed record CobraDirectMatchExecutionResult(
    IReadOnlyList<Match> ManagedMatches,
    IReadOnlyList<CobraDirectRuleApplication> SimpleApplications)
{
    public IReadOnlyList<Match> ToMatches()
    {
        if (SimpleApplications.Count == 0)
        {
            return ManagedMatches;
        }

        return ManagedMatches
            .Concat(SimpleApplications.Select(static app => new Match(
                app.Rule,
                app.RootClassId,
                app.Bindings.ToImmutableDictionary(System.StringComparer.Ordinal))))
            .ToArray();
    }
}
