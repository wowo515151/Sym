using System.ComponentModel;
using ModelContextProtocol.Server;
using SymMCP.Services;

namespace SymMCP.Tools;

[McpServerToolType]
public sealed class SymMcpTools
{
    private readonly ISymSolveService _solveService;
    private readonly ICSharpMathAnalysisService _analysisService;
    private readonly ILogger<SymMcpTools> _logger;

    public SymMcpTools(
        ISymSolveService solveService,
        ICSharpMathAnalysisService analysisService,
        ILogger<SymMcpTools> logger)
    {
        _solveService = solveService;
        _analysisService = analysisService;
        _logger = logger;
    }

    [McpServerTool, Description("Solve or simplify a Sym ProblemScript input using the Sym e-graph backend.")]
    public async Task<SymSolveResult> Solve(
        [Description("ProblemScript text to run through Sym.")] string problemScript,
        [Description("Optional timeout in seconds, bounded by server policy.")] int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _solveService.SolveAsync(new SymSolveRequest(problemScript, timeoutSeconds), cancellationToken);
        _logger.LogInformation("sym.solve completed ok={Ok} elapsedMs={ElapsedMs} estimatedWorkUnits={EstimatedWorkUnits}",
            result.Ok,
            result.ElapsedMs,
            result.EstimatedWorkUnits);
        return result;
    }

    [McpServerTool(Name = "sym.analyze.csharp_math"), Description("Analyze C# source for mathematical and security-oriented bug patterns using Sym's analyzer.")]
    public async Task<CSharpMathAnalysisToolResult> AnalyzeCSharpMath(
        [Description("C# source text to analyze.")] string sourceText,
        [Description("Optional file name used in diagnostics.")] string fileName = "input.cs",
        CancellationToken cancellationToken = default)
    {
        var result = await _analysisService.AnalyzeAsync(new CSharpMathAnalysisRequest(sourceText, fileName), cancellationToken);
        _logger.LogInformation("sym.analyze.csharp_math completed findings={FindingsCount} complete={IsComplete}",
            result.FindingsCount,
            result.IsComplete);
        return result;
    }
}
