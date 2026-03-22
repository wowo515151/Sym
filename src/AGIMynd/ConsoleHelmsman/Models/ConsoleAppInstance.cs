//Copyright Warren Harding 2025.
using System.Diagnostics;

namespace ConsoleHelmsman;

public sealed class ConsoleAppInstance
{
    public ConsoleAppInstance(ConsoleAppBase definition, Process process)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Process = process ?? throw new ArgumentNullException(nameof(process));
        IsRunning = !process.HasExited;
    }

    public ConsoleAppBase Definition { get; }
    public Process Process { get; }
    public bool IsRunning { get; private set; }
    public int? ExitCode { get; private set; }
    public string? LastError { get; set; }

    public void MarkExited()
    {
        IsRunning = false;
        try
        {
            ExitCode = Process.HasExited ? Process.ExitCode : null;
        }
        catch
        {
            ExitCode = null;
        }
    }
    
    // Optional path to a file in External/FromTools where streamed output
    // for this instance should be written. May be null if streaming disabled.
    public string? FromToolsPath { get; set; }
    
    // Gate for synchronizing writes to the FromToolsPath file.
    public object FromToolsGate { get; } = new();
}
