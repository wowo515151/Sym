using SymSolvers.CSharpAnalysis;

namespace SymMCP.Services;

public interface ICSharpMathAnalysisService
{
    Task<CSharpMathAnalysisToolResult> AnalyzeAsync(CSharpMathAnalysisRequest request, CancellationToken cancellationToken = default);
}

public sealed class CSharpMathAnalysisService : ICSharpMathAnalysisService
{
    private readonly CSharpMathBugAnalyzer _analyzer = new();

    public Task<CSharpMathAnalysisToolResult> AnalyzeAsync(CSharpMathAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceText);

        var result = _analyzer.AnalyzeProject(new[] { (request.SourceText, request.FileName) }, CSharpMathBugAnalyzerOptions.Default, cancellationToken);
        var findings = result.Findings
            .Select(f => new CSharpMathFindingDto(
                f.BugId,
                f.Severity.ToString(),
                f.Message,
                f.SourceSpan?.FilePath ?? string.Empty,
                f.SourceSpan?.StartLine,
                f.SourceSpan?.StartColumn))
            .ToArray();

        return Task.FromResult(new CSharpMathAnalysisToolResult(
            result.Findings.Count,
            result.IsComplete,
            result.Diagnostics,
            findings));
    }
}
