using System.Linq;
using SymCobra.Runtime;

namespace SymCobra.Regions;

internal static class CobraCudaRegionScorer
{
    public static bool TryScore(CobraRegionDraft[] drafts, out CobraRegion[] scoredRegions)
    {
        scoredRegions = [];
        if (drafts.Length == 0) return true;

        var familyCodes = drafts.Select(d => (int)d.Family).ToArray();
        var nodeCounts = drafts.Select(d => d.NodeCount).ToArray();
        var boundaryCounts = drafts.Select(d => d.BoundaryCount).ToArray();

        if (CobraCudaNative.TryScoreRegionsV2(familyCodes, nodeCounts, boundaryCounts, out var benefits, out var conflicts))
        {
            scoredRegions = drafts
                .Select((draft, i) => draft.ToRegion(benefits[i], conflicts[i], CobraScoreSource.Cuda))
                .ToArray();
            return true;
        }

        // Fallback to older scorer if V2 fails
        if (CobraCudaNative.TryScoreRegions(familyCodes, nodeCounts, boundaryCounts, out var fallbackBenefits, out var fallbackConflicts))
        {
            scoredRegions = drafts
                .Select((draft, i) => draft.ToRegion(fallbackBenefits[i], fallbackConflicts[i], CobraScoreSource.Cuda))
                .ToArray();
            return true;
        }

        return false;
    }
}
