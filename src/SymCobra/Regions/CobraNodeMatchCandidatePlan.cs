// Copyright Warren Harding 2026
using System.Collections.Generic;
using Sym.Core;
using Sym.Core.EGraph;

namespace SymCobra.Regions;

public sealed record CobraNodeMatchCandidatePlan(
    IReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>> EligibleNodesByClass,
    CobraNodeMatchCandidateSource Source);
