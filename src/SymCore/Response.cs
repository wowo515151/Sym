// Copyright Warren Harding 2026
namespace SymCore;

public class Response
{
    public bool Succeeded;
    public string Result;

    public string? Command;
    public string? Arguments;
    public string? WorkingDirectory;
    public int? ExitCode;
    public string? StandardOutput;
    public string? StandardError;
    public bool TimedOut;

    public Response(bool succeeded, string result)
    {
        Succeeded = succeeded;
        Result = result;
    }

    public Response(
        bool succeeded,
        string result,
        string? command,
        string? arguments,
        string? workingDirectory,
        int? exitCode,
        string? standardOutput,
        string? standardError,
        bool timedOut)
    {
        Succeeded = succeeded;
        Result = result;
        Command = command;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        TimedOut = timedOut;
    }
}
