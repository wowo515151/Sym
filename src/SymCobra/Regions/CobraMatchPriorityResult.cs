// Copyright Warren Harding 2026
using System.Collections.Generic;
using Sym.Core.EGraph;

namespace SymCobra.Regions;

public sealed record CobraMatchPriorityResult(
    List<Match> Matches,
    CobraMatchPrioritySource PrioritySource);
