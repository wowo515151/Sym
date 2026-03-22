namespace SymCobra.Telemetry;

public enum CobraSyncReason
{
    Unknown,
    InitialGraphAuthority,
    LegacyBoundaryBeforeUnion,
    PostRebuildAuthorityRefresh,
    AnalysisMetadata
}
