// Copyright Warren Harding 2026
using System.Collections.Generic;

namespace SymCobra.Regions;

public sealed record CobraUnionBatchPlan(
    IReadOnlyList<CobraUnionBatchGroup> Groups,
    CobraUnionPreparationSource Source);
