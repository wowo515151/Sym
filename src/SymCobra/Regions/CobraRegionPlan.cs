using System.Collections.Generic;
using System.Linq;

namespace SymCobra.Regions;

public sealed record CobraRegionPlan
{
    public CobraRegionPlan(
        IReadOnlyList<CobraRegion> regions,
        IReadOnlySet<int> hotClassIds,
        IReadOnlySet<int> boundaryClassIds,
        IReadOnlySet<int> residualClassIds,
        IReadOnlySet<int> suppressedClassIds)
        : this(
            regions,
            hotClassIds,
            boundaryClassIds,
            residualClassIds,
            suppressedClassIds,
            regions.Select(static region => region.RegionId).ToHashSet(),
            new HashSet<int>(),
            regions.Count(static region => IsPackFamily(region.Family)),
            0.0,
            0.0)
    {
    }

    public CobraRegionPlan(
        IReadOnlyList<CobraRegion> regions,
        IReadOnlySet<int> hotClassIds,
        IReadOnlySet<int> boundaryClassIds,
        IReadOnlySet<int> residualClassIds,
        IReadOnlySet<int> suppressedClassIds,
        IReadOnlySet<int> selectedRegionIds,
        IReadOnlySet<int> suppressedRegionIds,
        int packCount,
        double conflictDensity,
        double reductionRatio)
    {
        Regions = regions;
        HotClassIds = hotClassIds;
        BoundaryClassIds = boundaryClassIds;
        ResidualClassIds = residualClassIds;
        SuppressedClassIds = suppressedClassIds;
        SelectedRegionIds = selectedRegionIds;
        SuppressedRegionIds = suppressedRegionIds;
        PackCount = packCount;
        ConflictDensity = conflictDensity;
        ReductionRatio = reductionRatio;
    }

    public IReadOnlyList<CobraRegion> Regions { get; init; }

    public IReadOnlySet<int> HotClassIds { get; init; }

    public IReadOnlySet<int> BoundaryClassIds { get; init; }

    public IReadOnlySet<int> ResidualClassIds { get; init; }

    public IReadOnlySet<int> SuppressedClassIds { get; init; }

    public IReadOnlySet<int> SelectedRegionIds { get; init; }

    public IReadOnlySet<int> SuppressedRegionIds { get; init; }

    public int PackCount { get; init; }

    public double ConflictDensity { get; init; }

    public double ReductionRatio { get; init; }

    public int SelectedRegionCount => SelectedRegionIds.Count;

    public int SuppressedRegionCount => SuppressedRegionIds.Count;

    internal static bool IsPackFamily(CobraRegionFamily family)
    {
        return family is CobraRegionFamily.SharedSink
            or CobraRegionFamily.LeftFactorPack
            or CobraRegionFamily.RightFactorPack
            or CobraRegionFamily.BilinearOverlap
            or CobraRegionFamily.ResidualCoreBundle
            or CobraRegionFamily.TransposeBoundaryCore;
    }
}
