// Copyright Warren Harding 2026
using System.Collections.Generic;
using System.Linq;
using SymCobra.Runtime;

namespace SymCobra.Regions;

public static class CobraConflictResolver
{
    public static CobraRegionPlan BuildPlan(IReadOnlyList<CobraRegion> regions)
    {
        var conflictGraph = BuildConflictGraph(regions);
        var orderedRegions = OrderRegions(regions, conflictGraph);
        var hot = new HashSet<int>();
        var boundary = new HashSet<int>();
        var residual = new HashSet<int>();
        var suppressed = new HashSet<int>();
        var claimed = new HashSet<int>();
        var selectedRegionIds = new HashSet<int>();
        var suppressedRegionIds = new HashSet<int>();
        int packCount = 0;

        foreach (var region in orderedRegions)
        {
            bool conflicts = region.MemberClassIds.Any(claimed.Contains);
            if (conflicts)
            {
                suppressedRegionIds.Add(region.RegionId);
                foreach (var classId in region.MemberClassIds)
                {
                    suppressed.Add(classId);
                }
                continue;
            }

            selectedRegionIds.Add(region.RegionId);
            if (CobraRegionPlan.IsPackFamily(region.Family))
            {
                packCount++;
            }

            foreach (var classId in region.BoundaryClassIds)
            {
                boundary.Add(classId);
                if (region.HasResidualBranch)
                {
                    residual.Add(classId);
                }
            }

            foreach (var classId in region.MemberClassIds)
            {
                hot.Add(classId);
                claimed.Add(classId);
                if (region.HasResidualBranch)
                {
                    residual.Add(classId);
                }
            }
        }

        boundary.ExceptWith(hot);
        boundary.ExceptWith(suppressed);
        residual.ExceptWith(suppressed);

        double reductionRatio = regions.Count == 0
            ? 0.0
            : (double)(regions.Count - selectedRegionIds.Count) / regions.Count;

        return new CobraRegionPlan(
            orderedRegions,
            hot,
            boundary,
            residual,
            suppressed,
            selectedRegionIds,
            suppressedRegionIds,
            packCount,
            conflictGraph.ConflictDensity,
            reductionRatio);
    }

    private static IReadOnlyList<CobraRegion> OrderRegions(IReadOnlyList<CobraRegion> regions, CobraRegionConflictGraph conflictGraph)
    {
        if (regions.Count <= 1)
        {
            return regions;
        }

        var benefitScores = regions.Select(region => (int)System.Math.Round(region.BenefitScore * 100.0)).ToArray();
        var conflictScores = regions.Select(region => (int)System.Math.Round(region.ConflictScore * 100.0)).ToArray();
        var familyCodes = regions.Select(region => (int)region.Family).ToArray();
        var residualFlags = regions.Select(region => region.HasResidualBranch ? 1 : 0).ToArray();
        var transposeFlags = regions.Select(region => region.HasTransposeBoundary ? 1 : 0).ToArray();
        var boundaryCounts = regions.Select(region => region.BoundaryClassIds.Count).ToArray();
        var selectionEfficiencies = regions.Select(region => ComputeSelectionEfficiency(region, conflictGraph)).ToArray();

        if (CobraCudaNative.TryScoreRegionSelectionV2(familyCodes, benefitScores, conflictScores, residualFlags, transposeFlags, boundaryCounts, out var scores))
        {
            return regions
                .Select((region, index) => new { region, score = scores[index], efficiency = selectionEfficiencies[index] })
                .OrderByDescending(item => item.efficiency)
                .ThenByDescending(item => item.score)
                .ThenByDescending(item => item.region.MemberClassIds.Count)
                .ThenBy(item => item.region.RegionId)
                .Select(item => item.region)
                .ToArray();
        }

        if (CobraCudaNative.TryScoreRegionSelection(benefitScores, conflictScores, residualFlags, transposeFlags, boundaryCounts, out scores))
        {
            return regions
                .Select((region, index) => new { region, score = scores[index], efficiency = selectionEfficiencies[index] })
                .OrderByDescending(item => item.efficiency)
                .ThenByDescending(item => item.score)
                .ThenByDescending(item => item.region.MemberClassIds.Count)
                .ThenBy(item => item.region.RegionId)
                .Select(item => item.region)
                .ToArray();
        }

        return regions
            .OrderByDescending(region => ComputeSelectionEfficiency(region, conflictGraph))
            .ThenByDescending(region => region.BenefitScore - (region.ConflictScore * 0.5))
            .ThenByDescending(region => region.MemberClassIds.Count)
            .ThenBy(region => region.RegionId)
            .ToArray();
    }

    private static double ComputeSelectionEfficiency(CobraRegion region, CobraRegionConflictGraph conflictGraph)
    {
        int memberConflictDegree = conflictGraph.MemberConflictCountsByRegionId.GetValueOrDefault(region.RegionId);
        int boundaryPressure = conflictGraph.BoundaryPressureCountsByRegionId.GetValueOrDefault(region.RegionId);
        double packBonus = CobraRegionPlan.IsPackFamily(region.Family) ? 2.0 : 0.0;
        double residualPenalty = region.HasResidualBranch ? 0.75 : 0.0;
        double transposePenalty = region.HasTransposeBoundary ? 0.75 : 0.0;
        double numerator = (region.BenefitScore + packBonus) - (region.ConflictScore * 0.5) + (region.MemberClassIds.Count * 0.75);
        double denominator = 1.0 +
                             memberConflictDegree +
                             (boundaryPressure * 0.5) +
                             (region.BoundaryClassIds.Count * 0.25) +
                             residualPenalty +
                             transposePenalty;
        return numerator / denominator;
    }

    private static CobraRegionConflictGraph BuildConflictGraph(IReadOnlyList<CobraRegion> regions)
    {
        var memberConflictCountsByRegionId = regions.ToDictionary(static region => region.RegionId, static _ => 0);
        var boundaryPressureCountsByRegionId = regions.ToDictionary(static region => region.RegionId, static _ => 0);
        if (regions.Count <= 1)
        {
            return new CobraRegionConflictGraph(memberConflictCountsByRegionId, boundaryPressureCountsByRegionId, 0.0);
        }

        var memberSets = regions.Select(static region => region.MemberClassIds.ToHashSet()).ToArray();
        var boundarySets = regions.Select(static region => region.BoundaryClassIds.ToHashSet()).ToArray();
        int overlapEdgeCount = 0;
        int possibleEdgeCount = (regions.Count * (regions.Count - 1)) / 2;

        for (int i = 0; i < regions.Count; i++)
        {
            for (int j = i + 1; j < regions.Count; j++)
            {
                bool memberConflict = memberSets[i].Overlaps(memberSets[j]);
                bool boundaryPressure =
                    memberSets[i].Overlaps(boundarySets[j]) ||
                    memberSets[j].Overlaps(boundarySets[i]) ||
                    boundarySets[i].Overlaps(boundarySets[j]);

                if (memberConflict || boundaryPressure)
                {
                    overlapEdgeCount++;
                }

                if (memberConflict)
                {
                    memberConflictCountsByRegionId[regions[i].RegionId]++;
                    memberConflictCountsByRegionId[regions[j].RegionId]++;
                }

                if (boundaryPressure)
                {
                    boundaryPressureCountsByRegionId[regions[i].RegionId]++;
                    boundaryPressureCountsByRegionId[regions[j].RegionId]++;
                }
            }
        }

        double conflictDensity = possibleEdgeCount == 0
            ? 0.0
            : (double)overlapEdgeCount / possibleEdgeCount;
        return new CobraRegionConflictGraph(memberConflictCountsByRegionId, boundaryPressureCountsByRegionId, conflictDensity);
    }

    private sealed record CobraRegionConflictGraph(
        IReadOnlyDictionary<int, int> MemberConflictCountsByRegionId,
        IReadOnlyDictionary<int, int> BoundaryPressureCountsByRegionId,
        double ConflictDensity);
}
