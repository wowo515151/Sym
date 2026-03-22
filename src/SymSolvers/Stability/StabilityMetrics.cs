// Copyright Warren Harding 2026
using System;

namespace SymSolvers.Stability;

public sealed record StabilityMetrics(
    string Model,
    int Samples,
    int NaNCount,
    int InfCount,
    int UnderflowToZeroCount,
    double MaxAbsErrorVsFp64,
    double P95AbsErrorVsFp64,
    double MaxRelErrorVsFp64,
    double P95RelErrorVsFp64)
{
    public static StabilityMetrics Empty(string model) =>
        new(model, 0, 0, 0, 0, 0, 0, 0, 0);
}
