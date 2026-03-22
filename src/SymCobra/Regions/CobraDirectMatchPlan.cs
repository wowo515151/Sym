using System.Collections.Generic;
using Sym.Core;

namespace SymCobra.Regions;

public sealed record CobraDirectMatchPlan(
    IReadOnlyDictionary<int, IReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>>> PairsByClass,
    CobraDirectMatchSource Source,
    IReadOnlyDictionary<int, IReadOnlySet<Rule>>? ExhaustivelyHandledRulesByClass = null);
