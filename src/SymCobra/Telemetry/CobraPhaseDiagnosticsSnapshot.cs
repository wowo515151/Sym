// Copyright Warren Harding 2026
using System;
using SymCobra.Core;

namespace SymCobra.Telemetry;

public sealed record CobraPhaseDiagnosticsSnapshot(
    CobraPhase Phase,
    int ExecutionCount,
    int FallbackCount,
    TimeSpan Elapsed);
