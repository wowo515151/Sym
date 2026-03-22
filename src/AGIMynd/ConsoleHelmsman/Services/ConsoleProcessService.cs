// Copyright Warren Harding 2026
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleHelmsman;

public sealed class ConsoleProcessService : IDisposable
{
    // Optional working directory to use when starting processes.
    public string? WorkingDirectory { get; set; }

    private readonly TimeSpan _gracefulTimeout = TimeSpan.FromMilliseconds(1000);
    private CancellationTokenSource? _readCts;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private bool _suppressExitMessage;
    private bool _exitMessageLogged;

    public ConsoleAppInstance? CurrentInstance { get; private set; }
    public ConsoleLog? CurrentLog { get; private set; }

    // Maximum size in bytes for any file written to External/FromTools for streamed output.
    // Defaults to 5000 bytes as requested.
    public int MaxFromToolsFileSize { get; set; } = 5000;

    public event Action? ProcessExited;

    public bool Start(ConsoleAppBase definition, ConsoleLog log, out string? error)
    {
        var arguments = definition.Arguments ?? string.Empty;
        return StartInternal(definition, log, arguments, out error);
    }

    public bool StartWithArguments(ConsoleAppBase definition, ConsoleLog log, string arguments, out string? error)
    {
        return StartInternal(definition, log, arguments ?? string.Empty, out error);
    }

    private bool StartInternal(ConsoleAppBase definition, ConsoleLog log, string arguments, out string? error)
    {
        error = null;

        if (definition == null)
        {
            error = "Missing definition.";
            return false;
        }

        if (CurrentInstance is { IsRunning: true })
        {
            error = "Process already running.";
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = definition.Path,
            Arguments = arguments,
            // Use the service-level WorkingDirectory (or empty string).
            WorkingDirectory = WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // Ensure we read UTF-8 output to avoid mojibake when external tools emit UTF-8.
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                error = "Failed to start process.";
                return false;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        process.Exited += OnProcessExited;
        CurrentInstance = new ConsoleAppInstance(definition, process);
        CurrentLog = log;
        // Do not create per-instance FromTools files at Start.
        // File creation is now lazy and happens on first write in WriteToFromTools().
        _exitMessageLogged = false;
        _suppressExitMessage = false;
        _readCts = new CancellationTokenSource();
        // Use wrapper append actions so we both update the in-memory log and stream to FromTools.
        _stdoutTask = Task.Run(() => ReadLoopAsync(process.StandardOutput, s =>
        {
            try { log.AppendStdOut(s); } catch { }
            try { WriteToFromTools(CurrentInstance, s); } catch { }
        }, _readCts.Token));

        _stderrTask = Task.Run(() => ReadLoopAsync(process.StandardError, s =>
        {
            try { log.AppendStdErr(s); } catch { }
            try { WriteToFromTools(CurrentInstance, s); } catch { }
        }, _readCts.Token));

        return true;
    }

    public async Task StopAsync()
    {
        var instance = CurrentInstance;
        if (instance == null)
        {
            return;
        }

        var process = instance.Process;
        if (!IsProcessRunning(process))
        {
            return;
        }

        _suppressExitMessage = true;

        if (IsShell(instance.Definition.Name))
        {
            TrySendExit(process);
        }

        await WaitForExitAsync(process, _gracefulTimeout).ConfigureAwait(false);

        if (IsProcessRunning(process))
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
            }

            await WaitForExitAsync(process, _gracefulTimeout).ConfigureAwait(false);
        }

        _readCts?.Cancel();

        if (!_exitMessageLogged)
        {
            CurrentLog?.AppendSystemMessage("[Process stopped]");
            _exitMessageLogged = true;
        }
    }

    public bool TrySendInput(string text, out string sentText, out string? error, bool closeAfterSend = false)
    {
        sentText = text ?? string.Empty;
        error = null;

        var instance = CurrentInstance;
        if (instance == null || !instance.IsRunning)
        {
            error = "Process not running.";
            return false;
        }

        if (!sentText.EndsWith("\n", StringComparison.Ordinal))
        {
            sentText += Environment.NewLine;
        }

        try
        {
            instance.Process.StandardInput.Write(sentText);
            instance.Process.StandardInput.Flush();
            if (closeAfterSend)
            {
                try
                {
                    instance.Process.StandardInput.Close();
                }
                catch
                {
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            instance.LastError = ex.Message;
            error = ex.Message;
            return false;
        }
    }

    public bool TryCloseInput(out string? error)
    {
        error = null;

        var instance = CurrentInstance;
        if (instance == null || !instance.IsRunning)
        {
            error = "Process not running.";
            return false;
        }

        try
        {
            instance.Process.StandardInput.Close();
            return true;
        }
        catch (Exception ex)
        {
            instance.LastError = ex.Message;
            error = ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        _readCts?.Cancel();
        _readCts?.Dispose();

        if (CurrentInstance?.Process != null)
        {
            try
            {
                CurrentInstance.Process.Dispose();
            }
            catch
            {
            }
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process exitedProcess)
        {
            return;
        }

        var instance = CurrentInstance;
        if (instance == null || !ReferenceEquals(instance.Process, exitedProcess))
        {
            return;
        }

        instance.MarkExited();

        if (!_suppressExitMessage)
        {
            if (instance.ExitCode.HasValue)
            {
                CurrentLog?.AppendSystemMessage($"[Process exited: code {instance.ExitCode.Value}]");
            }
            else
            {
                CurrentLog?.AppendSystemMessage("[Process exited]");
            }

            _exitMessageLogged = true;
        }

        ProcessExited?.Invoke();
    }

    private static bool IsShell(string name)
    {
        return string.Equals(name, "powershell", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "cmd", StringComparison.OrdinalIgnoreCase);
    }

    private static void TrySendExit(Process process)
    {
        try
        {
            process.StandardInput.WriteLine("exit");
            process.StandardInput.Flush();
        }
        catch
        {
        }
    }

    private static async Task ReadLoopAsync(StreamReader reader, Action<string> append, CancellationToken token)
    {
        var buffer = new char[4096];

        while (!token.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            if (read <= 0)
            {
                break;
            }

            append(new string(buffer, 0, read));
        }
    }

        private static string SanitizeFileName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return "console";
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        private void WriteToFromTools(ConsoleAppInstance? instance, string text)
        {
            if (instance == null) return;
            var path = instance.FromToolsPath;
            if (string.IsNullOrWhiteSpace(path)) return;

            lock (instance.FromToolsGate)
            {
                try
                {
                    // If no per-instance path was configured at Start, create it lazily now
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        try
                        {
                            var repoRoot = RepoPathService.FindRepoRoot();
                            if (!string.IsNullOrWhiteSpace(repoRoot))
                            {
                                var dir = Path.Combine(repoRoot, "External", "FromTools");
                                try { Directory.CreateDirectory(dir); } catch { }
                                var fileName = $"console_{SanitizeFileName(instance.Definition.Name)}_{instance.Process.Id}_{DateTime.Now.Ticks.ToString()}.fromtool.txt";
                                path = Path.Combine(dir, fileName);
                                instance.FromToolsPath = path;
                            }
                        }
                        catch
                        {
                            // ignore failures to set up streaming path
                        }
                    }

                    if (string.IsNullOrWhiteSpace(path)) return;

                    // Append text as UTF8
                    File.AppendAllText(path, text ?? string.Empty, Encoding.UTF8);

                    // Enforce size limit by keeping the last MaxFromToolsFileSize bytes.
                    try
                    {
                        var fi = new FileInfo(path);
                        var max = Math.Max(0, MaxFromToolsFileSize);
                        if (max <= 0) return;

                        if (fi.Exists && fi.Length > max)
                        {
                            // Read last 'max' bytes
                            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                            fs.Seek(-max, SeekOrigin.End);
                            var buffer = new byte[max];
                            var read = 0;
                            while (read < max)
                            {
                                var r = fs.Read(buffer, read, max - read);
                                if (r <= 0) break;
                                read += r;
                            }

                            // Convert to string (replacement fallback to avoid exceptions on partial sequences)
                            var str = Encoding.UTF8.GetString(buffer, 0, read);

                            // Overwrite file with truncated tail
                            File.WriteAllText(path, str, Encoding.UTF8);
                        }
                    }
                    catch
                    {
                        // If truncation/read fails, ignore - best-effort streaming only.
                    }
                }
                catch
                {
                    // ignore IO failures
                }
            }
        }

    private static bool IsProcessRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static Task WaitForExitAsync(Process process, TimeSpan timeout)
    {
        return Task.Run(() =>
        {
            try
            {
                process.WaitForExit((int)timeout.TotalMilliseconds);
            }
            catch
            {
            }
        });
    }
}
