// Copyright Warren Harding 2026
using SymCobra.Core;

namespace SymCobra.Telemetry;

public sealed record CobraPhaseSourceEventSnapshot(
    CobraPhase Phase,
    string Source,
    bool IsGpuBacked);
