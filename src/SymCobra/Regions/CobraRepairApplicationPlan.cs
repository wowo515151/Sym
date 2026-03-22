using System.Collections.Generic;
using Sym.Core.EGraph;

namespace SymCobra.Regions;

public sealed record CobraRepairApplicationPlan(
    IReadOnlyList<EGraphRepairCandidate> OrderedCandidates,
    IReadOnlyList<CobraRepairApplicationGroup> Groups,
    IReadOnlyList<IReadOnlyList<EGraphRepairCandidate>> GroupCandidates,
    CobraRepairApplicationSource Source);
