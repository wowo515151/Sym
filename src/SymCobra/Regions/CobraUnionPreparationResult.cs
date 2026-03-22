// Copyright Warren Harding 2026
using System.Collections.Generic;

namespace SymCobra.Regions;

public sealed record CobraUnionPreparationResult(
    IReadOnlyList<CobraPreparedUnion> PreparedUnions,
    CobraUnionPreparationSource PreparationSource);
