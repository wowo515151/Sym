using Microsoft.Extensions.Options;
using WordsToSym;

namespace SymMCP.Services;

public interface ISymSolveService
{
    Task<SymSolveResult> SolveAsync(SymSolveRequest request, CancellationToken cancellationToken = default);
}

public sealed class SymSolveService : ISymSolveService
{
    private readonly ProblemScriptEGraphWrapper _wrapper = new();
    private readonly IOptions<SymMcpOptions> _options;

    public SymSolveService(IOptions<SymMcpOptions> options)
    {
        _options = options;
    }

    public Task<SymSolveResult> SolveAsync(SymSolveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProblemScript);

        var options = _options.Value;
        var timeoutSeconds = request.TimeoutSeconds ?? options.DefaultSolveTimeoutSeconds;
        timeoutSeconds = Math.Clamp(timeoutSeconds, 1, options.MaxSolveTimeoutSeconds);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var startedUtc = DateTime.UtcNow;
        var result = _wrapper.SolveWithEGraph(request.ProblemScript, timeoutCts.Token);
        var elapsed = DateTime.UtcNow - startedUtc;
        var elapsedMs = (long)Math.Max(0, elapsed.TotalMilliseconds);
        var ok = !result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
        var estimatedWorkUnits = 1 + (int)Math.Ceiling(elapsed.TotalSeconds);
        return Task.FromResult(new SymSolveResult(ok, result, elapsedMs, estimatedWorkUnits));
    }
}
