using System;

namespace SymCobra.Telemetry;

public sealed record CobraKernelTelemetrySnapshot(
    string KernelName,
    int CallCount,
    TimeSpan Elapsed);
