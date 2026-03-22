using System.Collections.Generic;
using SymSolvers.Stability;

namespace SymSolvers.StableForms;

public sealed class StableLossSynthesisResult
{
    public string OriginalExpression { get; init; } = string.Empty;
    public string StableExpression { get; init; } = string.Empty;
    public List<string> Guards { get; init; } = new();
    public string EquivalenceStatus { get; init; } = "Unknown";
    public string? Counterexample { get; init; }
    public IReadOnlyList<StabilityMetrics> StabilityMetrics { get; init; } = new List<StabilityMetrics>();
    public IReadOnlyList<string> SuggestedUnitTests { get; init; } = new List<string>();
    public IReadOnlyList<string> Notes { get; init; } = new List<string>();
}
