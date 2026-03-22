using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using SymCobra.Runtime;
using SymCobra.Telemetry;

namespace SymCobra.Core;

public class CobraDiagnostics
{
    private readonly ImmutableDictionary<string, CobraKernelTelemetrySnapshot> _kernelTelemetryBaseline;

    public CobraDiagnostics()
    {
        _kernelTelemetryBaseline = CobraCudaNative.GetKernelTelemetrySnapshot()
            .ToImmutableDictionary(static entry => entry.KernelName, StringComparer.Ordinal);
    }

    public int FullSyncCount { get; private set; }
    public int IncrementalSyncCount { get; private set; }
    public int FallbackPhaseCount { get; private set; }
    public int ExtractionFallbackCount { get; private set; }
    public int BindingMaterializationCount { get; private set; }
    public int SkippedSimpleBindingMaterializationCount { get; private set; }
    public int BindingExtractionCacheHitCount { get; private set; }
    public int BindingExtractionCacheMissCount { get; private set; }
    public int BindingDictionaryCacheHitCount { get; private set; }
    public int BindingDictionaryCacheMissCount { get; private set; }
    
    private readonly Dictionary<CobraPhase, Stopwatch> _phaseTimers = new();
    private readonly Dictionary<CobraPhase, int> _phaseFallbackCounts = new();
    private readonly Dictionary<CobraPhase, int> _phaseExecutionCounts = new();
    private readonly List<CobraPhaseSourceEventSnapshot> _phaseSourceEvents = new();
    private readonly List<CobraSyncEventSnapshot> _syncEvents = new();
    private readonly List<CobraFallbackEventSnapshot> _fallbackEvents = new();

    public void RecordFullSync()
    {
        RecordSync(CobraSyncDirection.LegacyToCobra, CobraSyncReason.Unknown, isFullSync: true);
    }

    public void RecordIncrementalSync()
    {
        RecordSync(CobraSyncDirection.LegacyMetadataToCobra, CobraSyncReason.Unknown, isFullSync: false);
    }

    public void RecordSync(CobraSyncDirection direction, CobraSyncReason reason, bool isFullSync)
    {
        if (isFullSync)
        {
            FullSyncCount++;
        }
        else
        {
            IncrementalSyncCount++;
        }

        _syncEvents.Add(new CobraSyncEventSnapshot(direction, reason, isFullSync));
    }

    public void RecordBindingMaterialization()
    {
        BindingMaterializationCount++;
    }

    public void RecordSkippedSimpleBindingMaterialization()
    {
        SkippedSimpleBindingMaterializationCount++;
    }

    public void RecordBindingExtractionCacheHit()
    {
        BindingExtractionCacheHitCount++;
    }

    public void RecordBindingExtractionCacheMiss()
    {
        BindingExtractionCacheMissCount++;
    }

    public void RecordBindingDictionaryCacheHit()
    {
        BindingDictionaryCacheHitCount++;
    }

    public void RecordBindingDictionaryCacheMiss()
    {
        BindingDictionaryCacheMissCount++;
    }

    public void RecordFallback(CobraPhase phase, CobraFallbackReason reason = CobraFallbackReason.Unknown)
    {
        FallbackPhaseCount++;
        if (phase == CobraPhase.Extraction)
        {
            ExtractionFallbackCount++;
        }
        
        if (!_phaseFallbackCounts.ContainsKey(phase))
        {
            _phaseFallbackCounts[phase] = 0;
        }
        _phaseFallbackCounts[phase]++;
        _fallbackEvents.Add(new CobraFallbackEventSnapshot(phase, reason));
    }

    public void RecordPhaseSource(CobraPhase phase, string source, bool isGpuBacked)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        _phaseSourceEvents.Add(new CobraPhaseSourceEventSnapshot(phase, source, isGpuBacked));
    }

    public void BeginPhase(CobraPhase phase)
    {
        if (!_phaseExecutionCounts.ContainsKey(phase))
        {
            _phaseExecutionCounts[phase] = 0;
        }

        _phaseExecutionCounts[phase]++;

        if (!_phaseTimers.ContainsKey(phase))
        {
            _phaseTimers[phase] = new Stopwatch();
        }
        _phaseTimers[phase].Start();
    }

    public void EndPhase(CobraPhase phase)
    {
        if (_phaseTimers.TryGetValue(phase, out var timer))
        {
            timer.Stop();
        }
    }

    public int GetPhaseExecutionCount(CobraPhase phase)
    {
        return _phaseExecutionCounts.TryGetValue(phase, out int count) ? count : 0;
    }

    public int GetPhaseFallbackCount(CobraPhase phase)
    {
        return _phaseFallbackCounts.TryGetValue(phase, out int count) ? count : 0;
    }

    public TimeSpan GetPhaseElapsed(CobraPhase phase)
    {
        return _phaseTimers.TryGetValue(phase, out var timer) ? timer.Elapsed : TimeSpan.Zero;
    }

    public CobraDiagnosticsSnapshot CreateSnapshot()
    {
        var currentKernelTelemetry = CobraCudaNative.GetKernelTelemetrySnapshot();
        var phaseSnapshots = ImmutableArray.CreateBuilder<CobraPhaseDiagnosticsSnapshot>();
        foreach (CobraPhase phase in Enum.GetValues<CobraPhase>())
        {
            int executionCount = GetPhaseExecutionCount(phase);
            int fallbackCount = GetPhaseFallbackCount(phase);
            TimeSpan elapsed = GetPhaseElapsed(phase);
            if (executionCount == 0 && fallbackCount == 0 && elapsed == TimeSpan.Zero)
            {
                continue;
            }

            phaseSnapshots.Add(new CobraPhaseDiagnosticsSnapshot(phase, executionCount, fallbackCount, elapsed));
        }

        var kernelTelemetry = currentKernelTelemetry
            .Select(current =>
            {
                _kernelTelemetryBaseline.TryGetValue(current.KernelName, out var baseline);
                int callCount = current.CallCount - (baseline?.CallCount ?? 0);
                TimeSpan elapsed = current.Elapsed - (baseline?.Elapsed ?? TimeSpan.Zero);
                return callCount > 0 || elapsed > TimeSpan.Zero
                    ? new CobraKernelTelemetrySnapshot(current.KernelName, callCount, elapsed)
                    : null;
            })
            .Where(static snapshot => snapshot is not null)
            .Select(static snapshot => snapshot!)
            .OrderByDescending(static snapshot => snapshot.Elapsed)
            .ThenBy(static snapshot => snapshot.KernelName, StringComparer.Ordinal)
            .ToImmutableArray();

        return new CobraDiagnosticsSnapshot(
            FullSyncCount,
            IncrementalSyncCount,
            FallbackPhaseCount,
            ExtractionFallbackCount,
            BindingMaterializationCount,
            SkippedSimpleBindingMaterializationCount,
            BindingExtractionCacheHitCount,
            BindingExtractionCacheMissCount,
            BindingDictionaryCacheHitCount,
            BindingDictionaryCacheMissCount,
            phaseSnapshots.ToImmutable(),
            kernelTelemetry,
            _phaseSourceEvents.ToImmutableArray(),
            _syncEvents.ToImmutableArray(),
            _fallbackEvents.ToImmutableArray());
    }

    public void Report()
    {
        var snapshot = CreateSnapshot();
        Console.WriteLine($"--- COBRA Diagnostics ---");
        Console.WriteLine($"Full Syncs: {snapshot.FullSyncCount}");
        Console.WriteLine($"Incremental Syncs: {snapshot.IncrementalSyncCount}");
        Console.WriteLine($"Total Phase Fallbacks: {snapshot.FallbackPhaseCount}");
        Console.WriteLine($"Extraction Fallbacks: {snapshot.ExtractionFallbackCount}");
        Console.WriteLine($"Binding Materializations: {snapshot.BindingMaterializationCount}");
        Console.WriteLine($"Skipped Simple Binding Materializations: {snapshot.SkippedSimpleBindingMaterializationCount}");
        Console.WriteLine($"Binding Extraction Cache Hits: {snapshot.BindingExtractionCacheHitCount}");
        Console.WriteLine($"Binding Extraction Cache Misses: {snapshot.BindingExtractionCacheMissCount}");
        Console.WriteLine($"Binding Dictionary Cache Hits: {snapshot.BindingDictionaryCacheHitCount}");
        Console.WriteLine($"Binding Dictionary Cache Misses: {snapshot.BindingDictionaryCacheMissCount}");
        foreach (var syncEvent in snapshot.SyncEvents)
        {
            Console.WriteLine($"Sync {syncEvent.Direction} ({syncEvent.Reason}): full={syncEvent.IsFullSync}");
        }
        foreach (var fallbackEvent in snapshot.FallbackEvents)
        {
            Console.WriteLine($"Fallback {fallbackEvent.Phase}: {fallbackEvent.Reason}");
        }
        foreach (var phaseSourceEvent in snapshot.PhaseSourceEvents)
        {
            Console.WriteLine($"Phase Source {phaseSourceEvent.Phase}: {phaseSourceEvent.Source}, gpu={phaseSourceEvent.IsGpuBacked}");
        }
        foreach (var kernel in snapshot.KernelTelemetry)
        {
            Console.WriteLine($"Kernel {kernel.KernelName}: calls={kernel.CallCount}, elapsedMs={kernel.Elapsed.TotalMilliseconds:F2}");
        }
        foreach (var phase in snapshot.Phases)
        {
            Console.WriteLine($"Phase {phase.Phase}: executions={phase.ExecutionCount}, fallbacks={phase.FallbackCount}, elapsedMs={phase.Elapsed.TotalMilliseconds:F2}");
        }
    }
}
