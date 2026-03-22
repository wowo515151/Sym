using System.Collections.Generic;

namespace SymCobra.Regions;

public sealed record CobraRegion(
    int RegionId,
    CobraRegionFamily Family,
    IReadOnlyCollection<int> MemberClassIds,
    IReadOnlyCollection<int> BoundaryClassIds,
    double BenefitScore,
    double ConflictScore,
    CobraScoreSource ScoreSource,
    bool HasResidualBranch,
    bool HasTransposeBoundary,
    string Summary);
