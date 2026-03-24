namespace SymMCP.Services;

public sealed record SymSolveRequest(
    string ProblemScript,
    int? TimeoutSeconds = null);

public sealed record SymSolveResult(
    bool Ok,
    string Result,
    long ElapsedMs,
    int EstimatedWorkUnits);
