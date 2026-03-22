// Copyright Warren Harding 2026
namespace SymCobra.Telemetry;

public sealed record CobraSyncEventSnapshot(
    CobraSyncDirection Direction,
    CobraSyncReason Reason,
    bool IsFullSync);
