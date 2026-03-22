using SymCobra.Regions;
using SymCobra.Runtime;

namespace SymCobra.Telemetry;

public sealed record CobraTelemetrySnapshot(
    CobraRuntimeInfo Runtime,
    int Iteration,
    int MatchCount,
    int RegionCount,
    int HotRegionCount,
    int SuppressedRegionCount,
    int PackCount,
    double ConflictDensity,
    double ReductionRatio,
    string FrontierPrioritySource,
    bool UsedCompatibilityFallback)
{
    public static CobraTelemetrySnapshot Create(
        CobraRuntimeInfo runtime,
        int iteration,
        int matchCount,
        CobraRegionPlan? plan,
        string frontierPrioritySource,
        bool usedCompatibilityFallback)
    {
        return new CobraTelemetrySnapshot(
            runtime,
            iteration,
            matchCount,
            plan?.Regions.Count ?? 0,
            plan?.HotClassIds.Count ?? 0,
            plan?.SuppressedClassIds.Count ?? 0,
            plan?.PackCount ?? 0,
            plan?.ConflictDensity ?? 0.0,
            plan?.ReductionRatio ?? 0.0,
            frontierPrioritySource,
            usedCompatibilityFallback);
    }
}
