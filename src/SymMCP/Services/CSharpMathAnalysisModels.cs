using SymSolvers.CSharpAnalysis;

namespace SymMCP.Services;

public sealed record CSharpMathAnalysisRequest(
    string SourceText,
    string FileName = "input.cs");

public sealed record CSharpMathAnalysisToolResult(
    int FindingsCount,
    bool IsComplete,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<CSharpMathFindingDto> Findings);

public sealed record CSharpMathFindingDto(
    string BugId,
    string Severity,
    string Message,
    string FilePath,
    int? Line,
    int? Column);
