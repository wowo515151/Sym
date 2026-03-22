using System.Collections.Generic;

namespace SymCobra.Regions;

public sealed record CobraRebuildPreparationPlan(
    IReadOnlyList<int> OrderedClassIds,
    CobraRebuildPreparationSource Source);
