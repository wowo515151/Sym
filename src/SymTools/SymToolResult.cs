// Copyright Warren Harding 2026
namespace SymTools;

public sealed record SymToolResult(
    bool IsSuccess,
    string Output,
    string Message,
    IReadOnlyList<string>? Trace = null);