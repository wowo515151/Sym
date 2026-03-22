using System;

namespace SymCobra.Core;

public class CobraFallbackPolicy
{
    public bool RequiresFallback(CobraPhase phase, CobraGraphState graphState)
    {
        // Define exact conditions for when a workload must fall back to the CPU EGraph.
        // E.g., if a graph uses unsupported nested features, or during early migration 
        // phases when COBRA matcher is not fully implemented.
        
        switch (phase)
        {
            case CobraPhase.Match:
                // Attempt native path first; CobraMatcher handles the split.
                return false;
            case CobraPhase.Extraction:
                // Attempt native path first; CobraExtractor handles the split.
                return false;
            default:
                return false;
        }
    }
}
