// Copyright Warren Harding 2026
using System;
using SymCobra.Telemetry;

namespace SymCobra.Core;

public enum CobraPhase
{
    Ingest,
    Canonicalize,
    RegionDiscovery,
    FrontierBuild,
    RuleCompatibility,
    MatchCandidateBuild,
    Match,
    Instantiate,
    UnionPreparation,
    UnionApplication,
    Rebuild,
    Analysis,
    Extraction
}

public class CobraPhaseCoordinator
{
    private readonly CobraGraphState _graphState;
    private readonly CobraDiagnostics _diagnostics;
    private readonly CobraFallbackPolicy _fallbackPolicy;
    
    public CobraPhaseCoordinator(
        CobraGraphState graphState, 
        CobraDiagnostics diagnostics, 
        CobraFallbackPolicy fallbackPolicy)
    {
        _graphState = graphState;
        _diagnostics = diagnostics;
        _fallbackPolicy = fallbackPolicy;
    }

    public void ExecutePhase(CobraPhase phase, Action executionAction)
    {
        _diagnostics.BeginPhase(phase);
        try
        {
            if (_fallbackPolicy.RequiresFallback(phase, _graphState))
            {
                _diagnostics.RecordFallback(phase, CobraFallbackReason.Policy);
                ExecuteFallback(phase, executionAction);
            }
            else
            {
                executionAction();
            }
        }
        finally
        {
            _diagnostics.EndPhase(phase);
        }
    }

    private void ExecuteFallback(CobraPhase phase, Action executionAction)
    {
        // Placeholder for phase-specific fallback execution.
        // During migration, the caller (CobraSolverStrategy) will still handle much of this.
        // This coordinates logging and graph synchronization bounds.
        // For now, execute the action as it contains the CPU fallback logic that we are migrating away from.
        executionAction();
    }
}
