using System.Collections.Generic;
using Sym.Core.EGraph;

namespace SymCobra.Regions;

public sealed record CobraRepairCandidatePlan(
    IReadOnlyList<EGraphRepairCandidate> Candidates,
    CobraRepairCandidateSource Source);
