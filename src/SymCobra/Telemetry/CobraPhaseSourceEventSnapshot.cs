using SymCobra.Core;

namespace SymCobra.Telemetry;

public sealed record CobraPhaseSourceEventSnapshot(
    CobraPhase Phase,
    string Source,
    bool IsGpuBacked);
