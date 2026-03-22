using System.Collections.Generic;

namespace SymCobra.Regions;

public sealed record CobraAnalysisPreparationPlan(
    IReadOnlyList<int> OrderedClassIds,
    CobraAnalysisPreparationSource Source);
