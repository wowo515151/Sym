// Copyright Warren Harding 2026
namespace SymCobra.Telemetry;

public enum CobraSyncReason
{
    Unknown,
    InitialGraphAuthority,
    LegacyBoundaryBeforeUnion,
    PostRebuildAuthorityRefresh,
    AnalysisMetadata
}
