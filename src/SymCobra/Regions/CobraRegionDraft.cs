// Copyright Warren Harding 2026
using System.Collections.Generic;

namespace SymCobra.Regions;

internal sealed record CobraRegionDraft(
    int RegionId,
    CobraRegionFamily Family,
    IReadOnlyCollection<int> MemberClassIds,
    IReadOnlyCollection<int> BoundaryClassIds,
    int NodeCount,
    int BoundaryCount,
    bool HasResidualBranch,
    bool HasTransposeBoundary,
    string Summary)
{
    public CobraRegion ToRegion(double benefitScore, double conflictScore, CobraScoreSource scoreSource)
    {
        return new CobraRegion(
            RegionId,
            Family,
            MemberClassIds,
            BoundaryClassIds,
            benefitScore,
            conflictScore,
            scoreSource,
            HasResidualBranch,
            HasTransposeBoundary,
            Summary);
    }
}
