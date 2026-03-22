// Copyright Warren Harding 2026
using System.Collections.Generic;

namespace SymCobra.Regions;

public sealed record CobraExtractionPlan(
    IReadOnlyList<int> OrderedClassIds,
    IReadOnlyDictionary<int, IReadOnlyList<int>> OrderedNodesByClass,
    CobraExtractionSource Source);
