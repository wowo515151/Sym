using System.Collections.Generic;

namespace SymCobra.Regions;

public sealed record CobraUnionBatchPlan(
    IReadOnlyList<CobraUnionBatchGroup> Groups,
    CobraUnionPreparationSource Source);
