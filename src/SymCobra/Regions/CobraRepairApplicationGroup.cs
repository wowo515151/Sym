// Copyright Warren Harding 2026
using System.Collections.Generic;
using Sym.Core.EGraph;

namespace SymCobra.Regions;

public sealed record CobraRepairApplicationGroup(
    int AnchorClassId,
    ENode CanonicalNode,
    IReadOnlyList<EGraphRepairCandidate> Candidates);
