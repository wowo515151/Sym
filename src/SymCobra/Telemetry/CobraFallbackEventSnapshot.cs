// Copyright Warren Harding 2026
using SymCobra.Core;

namespace SymCobra.Telemetry;

public sealed record CobraFallbackEventSnapshot(
    CobraPhase Phase,
    CobraFallbackReason Reason);
