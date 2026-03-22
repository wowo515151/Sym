using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AGIMynd
{
    // Responsible for reading buffer files (ToTools/FromTools) exactly once per logical write
    // and invoking a provided logger. This class is testable in isolation.
    public class BufferFileLogger : IDisposable
    {
        private readonly string _memoryRoot;
        private readonly Action<string> _logAction;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceCts = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DateTime> _lastLogged = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new(StringComparer.OrdinalIgnoreCase);

        public BufferFileLogger(string memoryRoot, Action<string> logAction)
        {
            _memoryRoot = memoryRoot ?? throw new ArgumentNullException(nameof(memoryRoot));
            _logAction = logAction ?? throw new ArgumentNullException(nameof(logAction));
        }

        public void NotifyFileChanged(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            // schedule debounce
            DebouncePath(path);
        }

        private void DebouncePath(string path, int ms = 200)
        {
            if (_debounceCts.TryGetValue(path, out var existing))
            {
                try { existing.Cancel(); existing.Dispose(); } catch { }
            }

            var cts = new CancellationTokenSource();
            _debounceCts[path] = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(ms, cts.Token);
                    if (cts.IsCancellationRequested) return;
                    await ProcessPathAsync(path).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch { }
                finally
                {
                    _debounceCts.TryRemove(path, out _);
                    try { cts.Dispose(); } catch { }
                }
            });
        }

        private async Task ProcessPathAsync(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return;
            if (fullPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return;

            var fileLock = _fileLocks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
            await fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Read content
                string content;
                try
                {
                    using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs);
                    content = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (ex is FileNotFoundException || ex is DirectoryNotFoundException || (ex.Message != null && ex.Message.Contains("Could not find file")))
                    {
                        return;
                    }
                    _logAction($"Error reading buffer file {fullPath}: {ex.Message}");
                    return;
                }

                // Determine folder label
                var name = Path.GetFileName(fullPath);
                var toToolsPath = Path.Combine(_memoryRoot, "ToTools") + Path.DirectorySeparatorChar;
                var fromToolsPath = Path.Combine(_memoryRoot, "FromTools") + Path.DirectorySeparatorChar;
                string folderLabel;
                if (fullPath.StartsWith(toToolsPath, StringComparison.OrdinalIgnoreCase)) folderLabel = "ToTools";
                else if (fullPath.StartsWith(fromToolsPath, StringComparison.OrdinalIgnoreCase)) folderLabel = "FromTools";
                else
                {
                    var parent = Path.GetDirectoryName(fullPath);
                    folderLabel = string.IsNullOrEmpty(parent) ? "Memory" : Path.GetFileName(parent) ?? "Memory";
                }

                var now = DateTime.UtcNow;
                if (_lastLogged.TryGetValue(fullPath, out var last) && (now - last).TotalMilliseconds < 1000)
                {
                    return;
                }

                _lastLogged[fullPath] = now;
                _logAction($"Buffer file ({folderLabel}): {name} ->\n{content}");
            }
            finally
            {
                try { fileLock.Release(); } catch { }
            }
        }

        public void Dispose()
        {
            foreach (var kv in _debounceCts)
            {
                try { kv.Value.Cancel(); kv.Value.Dispose(); } catch { }
            }
            _debounceCts.Clear();

            foreach (var kv in _fileLocks)
            {
                try { kv.Value.Dispose(); } catch { }
            }
            _fileLocks.Clear();
        }
    }
}
