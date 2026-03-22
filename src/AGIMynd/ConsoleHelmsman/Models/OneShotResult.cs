// Copyright Warren Harding 2026
namespace ConsoleHelmsman;

public sealed class OneShotResult
{
    private OneShotResult(bool started, bool usedPromptArgument, string? error)
    {
        Started = started;
        UsedPromptArgument = usedPromptArgument;
        Error = error;
    }

    public bool Started { get; }
    public bool UsedPromptArgument { get; }
    public string? Error { get; }

    public static OneShotResult Failed(string error)
    {
        return new OneShotResult(false, false, error);
    }

    public static OneShotResult CreateStarted(bool usedPromptArgument, string? error)
    {
        return new OneShotResult(true, usedPromptArgument, error);
    }
}
