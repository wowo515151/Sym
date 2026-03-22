using System.Collections.Generic;

namespace SymCobra.Regions;

public sealed record CobraUnionPreparationResult(
    IReadOnlyList<CobraPreparedUnion> PreparedUnions,
    CobraUnionPreparationSource PreparationSource);
