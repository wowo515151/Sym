// Copyright Warren Harding 2026
using System.Collections.Immutable;
using SymCobra.Core;

namespace SymCobra.Telemetry;

public sealed record CobraDiagnosticsSnapshot(
    int FullSyncCount,
    int IncrementalSyncCount,
    int FallbackPhaseCount,
    int ExtractionFallbackCount,
    int BindingMaterializationCount,
    int SkippedSimpleBindingMaterializationCount,
    int BindingExtractionCacheHitCount,
    int BindingExtractionCacheMissCount,
    int BindingDictionaryCacheHitCount,
    int BindingDictionaryCacheMissCount,
    ImmutableArray<CobraPhaseDiagnosticsSnapshot> Phases,
    ImmutableArray<CobraKernelTelemetrySnapshot> KernelTelemetry,
    ImmutableArray<CobraPhaseSourceEventSnapshot> PhaseSourceEvents,
    ImmutableArray<CobraSyncEventSnapshot> SyncEvents,
    ImmutableArray<CobraFallbackEventSnapshot> FallbackEvents)
{
    public bool HasFallbacks => FallbackPhaseCount > 0;

    public bool TryGetPhase(CobraPhase phase, out CobraPhaseDiagnosticsSnapshot snapshot)
    {
        foreach (var candidate in Phases)
        {
            if (candidate.Phase == phase)
            {
                snapshot = candidate;
                return true;
            }
        }

        snapshot = null!;
        return false;
    }
}
