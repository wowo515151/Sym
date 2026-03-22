// Copyright Warren Harding 2026
using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Xml.Serialization;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ConsoleHelmsman;

public sealed class PlanBridgeService : IDisposable
{
    private readonly Func<string, ConsoleAppBase?> _getDefinitionByName;
    private readonly Func<string?> _getSelectedConsoleName;
    private readonly Func<string?> _getWorkingDirectory;
    private readonly Action<string> _log;
    private readonly Func<string, string, Task<string?>>? _sendToConsoleAsync;

    private readonly string _toToolsDir;
    private readonly string _fromToolsDir;

    private readonly FileSystemWatcher _watcher;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly ConcurrentDictionary<string, DateTime> _lastEnqueue = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private Task? _worker;

    private const int InToolsBufferSize = 10;
    private const int FromToolsBufferSize = 10;

    public PlanBridgeService(
        string repoRoot,
        Func<string, ConsoleAppBase?> getDefinitionByName,
        Func<string?> getSelectedConsoleName,
        Func<string?> getWorkingDirectory,
        Action<string> log,
        Func<string, string, Task<string?>>? sendToConsoleAsync = null)
    {
        if (string.IsNullOrWhiteSpace(repoRoot)) throw new ArgumentException("Missing repoRoot.", nameof(repoRoot));
        _getDefinitionByName = getDefinitionByName ?? throw new ArgumentNullException(nameof(getDefinitionByName));
        _getSelectedConsoleName = getSelectedConsoleName ?? throw new ArgumentNullException(nameof(getSelectedConsoleName));
        _getWorkingDirectory = getWorkingDirectory ?? throw new ArgumentNullException(nameof(getWorkingDirectory));
        _log = log ?? (_ => { });
        _sendToConsoleAsync = sendToConsoleAsync;

        _toToolsDir = Path.Combine(repoRoot, "External", "ToTools");
        _fromToolsDir = Path.Combine(repoRoot, "External", "FromTools");

        Directory.CreateDirectory(_toToolsDir);
        Directory.CreateDirectory(_fromToolsDir);

        _watcher = new FileSystemWatcher(_toToolsDir)
        {
            IncludeSubdirectories = false,
            EnableRaisingEvents = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size
        };

        _watcher.Created += (_, e) => TryEnqueue(e.FullPath);
        _watcher.Changed += (_, e) => TryEnqueue(e.FullPath);
        _watcher.Renamed += (_, e) => TryEnqueue(e.FullPath);
    }

    public void Start()
    {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => WorkerAsync(_cts.Token));
        _watcher.EnableRaisingEvents = true;

        // Seed existing files (in case the service starts late)
        foreach (var file in Directory.GetFiles(_toToolsDir))
        {
            TryEnqueue(file);
        }

        // Ensure buffer limits at startup
        try { EnforceDirectoryLimit(_toToolsDir, InToolsBufferSize); } catch { }
        try { EnforceDirectoryLimit(_fromToolsDir, FromToolsBufferSize); } catch { }

        _log($"PlanBridge watching ToTools: {_toToolsDir}");
    }

    private void TryEnqueue(string fullPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return;
            if (Directory.Exists(fullPath)) return;

            var fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(fileName)) return;

            if (fileName.StartsWith(".", StringComparison.Ordinal)) return;
            if (fileName.StartsWith("_", StringComparison.Ordinal)) return;
            if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return;
            // Enforce buffer limit when a new file is detected to keep External/ToTools bounded.
            try { EnforceDirectoryLimit(_toToolsDir, InToolsBufferSize); } catch { }

            // Basic debounce: avoid spamming the queue during write bursts.
            var now = DateTime.UtcNow;
            var last = _lastEnqueue.GetOrAdd(fullPath, _ => DateTime.MinValue);
            if ((now - last) < TimeSpan.FromMilliseconds(250))
            {
                return;
            }

            _lastEnqueue[fullPath] = now;
            _queue.Writer.TryWrite(fullPath);
        }
        catch
        {
        }
    }

    private async Task WorkerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? planPath = null;
            try
            {
                planPath = await _queue.Reader.ReadAsync(ct).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(planPath) || !File.Exists(planPath))
                {
                    continue;
                }

                // (No _processed archive in this simplified flow.)

                // Wait until the file is readable and stable.
                var planText = await ReadAllTextWithRetryAsync(planPath, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(planText))
                {
                    await FinalizePlanAsync(planPath, suffix: "empty", ct).ConfigureAwait(false);
                    continue;
                }
                // Try to parse a ToolCommand XML from the plan text. If present, route accordingly.
                var toolCommand = TryParseToolCommand(planText);
                if (toolCommand != null && !string.IsNullOrWhiteSpace(toolCommand.ToolName))
                {
                    // Found a ToolCommand; attempt to route it.
                    try
                    {
                        if (_sendToConsoleAsync != null)
                        {
                            var result = await _sendToConsoleAsync(toolCommand.ToolName, toolCommand.ToolInput ?? string.Empty).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(result))
                            {
                                await WriteResponseAsync(planPath, result, ct).ConfigureAwait(false);
                            }
                            else
                            {
                                await WriteResponseAsync(planPath, $"[ConsoleCommander] Sent command to {toolCommand.ToolName}", ct).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            var def = _getDefinitionByName(toolCommand.ToolName);
                            if (def == null)
                            {
                                await WriteResponseAsync(planPath, $"[ConsoleCommander error] Unknown CLI: {toolCommand.ToolName}", ct).ConfigureAwait(false);
                            }
                            else
                            {
                                // For ToTools invocations, always run CLIs in one-shot mode.
                                var workingDirectory = _getWorkingDirectory();
                                var resultText = await RunCopilotAsync(def, toolCommand.ToolInput ?? string.Empty, workingDirectory, ct).ConfigureAwait(false);
                                await WriteResponseAsync(planPath, resultText, ct).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await WriteResponseAsync(planPath, $"[ConsoleCommander exception] {ex.Message}", CancellationToken.None).ConfigureAwait(false);
                    }

                    await FinalizePlanAsync(planPath, suffix: "done", ct).ConfigureAwait(false);
                    continue;
                }

                // No ToolCommand XML detected. Route raw plan to selected console (or default to copilot if selected not found).
                var selectedName = _getSelectedConsoleName();
                ConsoleAppBase? targetDef = null;
                if (!string.IsNullOrWhiteSpace(selectedName))
                {
                    targetDef = _getDefinitionByName(selectedName!);
                }

                if (targetDef == null)
                {
                    // Fallback to copilot-like behavior: try a console named "copilot".
                    targetDef = _getDefinitionByName("copilot");
                }

                if (targetDef == null)
                {
                    await WriteResponseAsync(planPath, "[PlanBridge error] No target console available.", ct).ConfigureAwait(false);
                    await FinalizePlanAsync(planPath, suffix: "error", ct).ConfigureAwait(false);
                    continue;
                }

                // For ToTools (External/ToTools) raw plans, always run the target CLI in one-shot mode.
                // This intentionally ignores per-console interactive mode for external toolinvocations.
                {
                    var workingDirectory = _getWorkingDirectory();
                    var resultText = await RunCopilotAsync(targetDef, planText, workingDirectory, ct).ConfigureAwait(false);
                    await WriteResponseAsync(planPath, resultText, ct).ConfigureAwait(false);
                    await FinalizePlanAsync(planPath, suffix: "done", ct).ConfigureAwait(false);
                    continue;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                try
                {
                    _log($"PlanBridge error: {ex.Message}");
                    if (!string.IsNullOrWhiteSpace(planPath))
                    {
                        await WriteResponseAsync(planPath, "[PlanBridge exception] " + ex.Message, CancellationToken.None).ConfigureAwait(false);
                        await FinalizePlanAsync(planPath, suffix: "exception", CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch
                {
                }
            }
        }
    }

    private static async Task<string> ReadAllTextWithRetryAsync(string path, CancellationToken ct)
    {
        // Retry for a short window to handle partial writes.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        Exception? last = null;

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }

        return last != null ? $"[PlanBridge read error] {last.Message}" : string.Empty;
    }

    private ConsoleHelmsman.Models.ToolCommand? TryParseToolCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            var trimmed = text.Trim();
            var startTag = "<ToolCommand";
            var endTag = "</ToolCommand>";
            var start = trimmed.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            var end = trimmed.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);
            if (start >= 0 && end > start)
            {
                var xml = trimmed.Substring(start, end - start + endTag.Length);
                var serializer = new XmlSerializer(typeof(ConsoleHelmsman.Models.ToolCommand));
                using var reader = new StringReader(xml);
                if (serializer.Deserialize(reader) is ConsoleHelmsman.Models.ToolCommand cmd)
                {
                    return cmd;
                }
            }
        }
        catch
        {
            // ignore parse errors
        }

        return null;
    }

    private static bool IsShell(string name)
    {
        return string.Equals(name, "powershell", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "cmd", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> RunInteractiveCommandAsync(ConsoleAppBase definition, string command, string? workingDirectory, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = definition.Path,
            Arguments = definition.Arguments,
            // Use the workingDirectory supplied by the caller (typically the UI/global setting).
            WorkingDirectory = !string.IsNullOrWhiteSpace(workingDirectory) ? workingDirectory : string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        _log($"PlanBridge running interactive: {definition.Path} {definition.Arguments}");

        using var proc = new Process { StartInfo = startInfo };
        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            return "[PlanBridge start error] " + ex.Message;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(command))
            {
                await proc.StandardInput.WriteLineAsync(command).ConfigureAwait(false);
                await proc.StandardInput.FlushAsync().ConfigureAwait(false);
            }

            if (IsShell(definition.Name))
            {
                await proc.StandardInput.WriteLineAsync("exit").ConfigureAwait(false);
                await proc.StandardInput.FlushAsync().ConfigureAwait(false);
            }

            try { proc.StandardInput.Close(); } catch { }
        }
        catch
        {
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        stdout = stdout?.TrimEnd() ?? string.Empty;
        stderr = stderr?.TrimEnd() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(stdout))
        {
            return stderr;
        }

        if (string.IsNullOrWhiteSpace(stderr))
        {
            return stdout;
        }

        return stdout + Environment.NewLine + stderr;
    }

    private async Task<string> RunCopilotAsync(ConsoleAppBase definition, string prompt, string? workingDirectory, CancellationToken ct)
    {
        var args = OneShotRunner.BuildOneShotArguments(definition, prompt, out _);

        var startInfo = new ProcessStartInfo
        {
            FileName = definition.Path,
            Arguments = args,
            // Use the workingDirectory supplied by the caller (typically the UI/global setting).
            WorkingDirectory = !string.IsNullOrWhiteSpace(workingDirectory) ? workingDirectory : string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        _log($"PlanBridge running copilot: {definition.Path} {definition.Arguments}");

        using var proc = new Process { StartInfo = startInfo };
        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            return "[PlanBridge start error] " + ex.Message;
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        // Raw pass-through: return stdout and stderr with no extra formatting.
        stdout = stdout?.TrimEnd() ?? string.Empty;
        stderr = stderr?.TrimEnd() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(stdout))
        {
            return stderr;
        }

        if (string.IsNullOrWhiteSpace(stderr))
        {
            return stdout;
        }

        return stdout + Environment.NewLine + stderr;
    }

    private async Task WriteResponseAsync(string planPath, string responseText, CancellationToken ct)
    {
        var planName = Path.GetFileName(planPath);
        var responseName = planName + ".fromtool.txt";
        var dest = Path.Combine(_fromToolsDir, responseName);
        var temp = dest + ".tmp";

        await File.WriteAllTextAsync(temp, responseText ?? string.Empty, Encoding.UTF8, ct).ConfigureAwait(false);

        try
        {
            if (File.Exists(dest))
            {
                File.Delete(dest);
            }
        }
        catch
        {
        }

        File.Move(temp, dest);
        _log($"PlanBridge wrote response: {dest}");

        try
        {
            EnforceDirectoryLimit(_fromToolsDir, FromToolsBufferSize);
        }
        catch { }
    }

    private async Task FinalizePlanAsync(string planPath, string suffix, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(planPath))
            {
                return;
            }

            var fileName = Path.GetFileName(planPath);
                // Rename the processed plan with a leading underscore so it remains in the folder
                // but won't be reprocessed by TryEnqueue (which ignores names starting with '_').
                var stamped = $"_{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{DateTime.UtcNow.Ticks.ToString()}_{suffix}{Path.GetExtension(fileName)}";
                var dest = Path.Combine(_toToolsDir, stamped);

                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                        File.Move(planPath, dest);
                        try { EnforceDirectoryLimit(_toToolsDir, InToolsBufferSize); } catch { }
                        return;
                    }
                    catch
                    {
                        await Task.Delay(100, ct).ConfigureAwait(false);
                    }
                }
        }
        catch
        {
        }
    }

    private void EnforceDirectoryLimit(string dir, int limit)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            var files = new DirectoryInfo(dir).GetFiles()
                .Where(f => !f.Name.StartsWith("."))
                .OrderBy(f => f.CreationTimeUtc)
                .ToList();

            if (files.Count <= limit) return;

            int toDelete = files.Count - limit;
            for (int i = 0; i < toDelete; i++)
            {
                try
                {
                    files[i].Delete();
                    _log($"PlanBridge pruned file from {dir}: {files[i].Name}");
                }
                catch { }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        try
        {
            _watcher.EnableRaisingEvents = false;
        }
        catch
        {
        }

        try { _watcher.Dispose(); } catch { }

        var cts = _cts;
        _cts = null;
        try { cts?.Cancel(); } catch { }
        try { cts?.Dispose(); } catch { }

        try { _queue.Writer.TryComplete(); } catch { }

        try { _worker?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _worker = null;
    }
}
