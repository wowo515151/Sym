//Copyright Warren Harding 2025.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace AGIMynd
{
    public class BufferManager
    {
        private readonly string _memoryRoot;
        private readonly string _plansBuffer;
        private readonly string _responsesBuffer;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private FileSystemWatcher? _toToolsWatcher;
        private FileSystemWatcher? _fromToolsWatcher;
        private FileSystemWatcher? _memoryFromWatcher;
        
        // Debounce helpers to coalesce noisy file system events
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _responseDebounceCts = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);
        private readonly object _plansDebounceLock = new object();
        private CancellationTokenSource? _plansDebounceCts;

        private readonly string _memoryToToolsPath;
        private readonly string _memoryFromToolsPath;
        private readonly string _externalToToolsPath;
        private readonly string _externalFromToolsPath;
        private readonly string _logRoot;
        private const int InToolsBufferSize = 10;
        // From-tools buffer size (keep conservative to avoid edit-and-continue issues during debug)
        private const int FromToolsBufferSize = 10;

        public BufferManager(string memoryRoot, string plansBuffer, string responsesBuffer)
        {
            _memoryRoot = GetCanonicalPath(memoryRoot);
            _plansBuffer = GetCanonicalPath(plansBuffer);
            _responsesBuffer = GetCanonicalPath(responsesBuffer);
            _memoryToToolsPath = Path.Combine(_memoryRoot, "ToTools") + Path.DirectorySeparatorChar;
            _memoryFromToolsPath = Path.Combine(_memoryRoot, "FromTools") + Path.DirectorySeparatorChar;
            _externalToToolsPath = GetCanonicalPath(_plansBuffer);
            _externalFromToolsPath = GetCanonicalPath(_responsesBuffer);
            _logRoot = Path.Combine(_memoryRoot, "EpochLog");

            EnsureDirectories();
        }

        private static string GetCanonicalPath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (!fullPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fullPath += Path.DirectorySeparatorChar;
            }
            return fullPath;
        }

        private void EnsureDirectories()
        {
            if (!Directory.Exists(_externalToToolsPath)) Directory.CreateDirectory(_externalToToolsPath);
            if (!Directory.Exists(_externalFromToolsPath)) Directory.CreateDirectory(_externalFromToolsPath);

            if (!Directory.Exists(_memoryToToolsPath)) Directory.CreateDirectory(_memoryToToolsPath);
            if (!Directory.Exists(_memoryFromToolsPath)) Directory.CreateDirectory(_memoryFromToolsPath);
            if (!Directory.Exists(_logRoot)) Directory.CreateDirectory(_logRoot);
        }

        public async Task StartAsync()
        {
            // Create our own CTS for this instance and run the internal loop.
            EnsureDirectories();
            if (_cts != null)
            {
                throw new InvalidOperationException("BufferManager is already running");
            }

            _cts = new CancellationTokenSource();
            try
            {
                await StartInternalAsync(_cts.Token);
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task StartInternalAsync(CancellationToken ct)
        {
            EnsureDirectories();

            _toToolsWatcher = new FileSystemWatcher(_memoryToToolsPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            // Debounce plans events: coalesce rapid events into a single ProcessPlansAsync call
            _toToolsWatcher.Created += (s, e) => DebouncePlans();
            _toToolsWatcher.Changed += (s, e) => DebouncePlans();
            _toToolsWatcher.Renamed += (s, e) => DebouncePlans();

            _fromToolsWatcher = new FileSystemWatcher(_externalFromToolsPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            // Process only the changed/created file to update memory quickly and avoid re-copying everything.
            // Debounce response events per-path to avoid copying in-progress .tmp writes and duplicate events
            _fromToolsWatcher.Created += (s, e) => DebounceResponsePath(e.FullPath);
            _fromToolsWatcher.Changed += (s, e) => DebounceResponsePath(e.FullPath);
            _fromToolsWatcher.Renamed += (s, e) => DebounceResponsePath(e.FullPath);

            // process any existing files immediately
            await ProcessPlansAsync();
            // On startup, process existing external responses by copying each file once.
            await ProcessResponsesAsync();

            // Also proactively log current external FromTools files to help trace round-trip timings
            try
            {
                if (Directory.Exists(_externalFromToolsPath))
                {
                    foreach (var f in Directory.GetFiles(_externalFromToolsPath))
                    {
                        try
                        {
                            var snippet = string.Empty;
                            try { snippet = (await File.ReadAllTextAsync(f)).Replace("\r", "").Replace("\n", "\\n"); } catch { }
                            if (snippet.Length > 200) snippet = snippet.Substring(0, 200) + "...";
                            LogDiagnostic($"Startup External FromTools file: {Path.GetFileName(f)}: \"{snippet}\"");
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // watch memory FromTools for incoming files so we can prune promptly
            _memoryFromWatcher = new FileSystemWatcher(_memoryFromToolsPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _memoryFromWatcher.Created += (_, e) => Task.Run(() => { try { EnforceDirectoryLimit(_memoryFromToolsPath, FromToolsBufferSize); } catch { } });
            _memoryFromWatcher.Changed += (_, e) => Task.Run(() => { try { EnforceDirectoryLimit(_memoryFromToolsPath, FromToolsBufferSize); } catch { } });
            _memoryFromWatcher.Renamed += (_, e) => Task.Run(() => { try { EnforceDirectoryLimit(_memoryFromToolsPath, FromToolsBufferSize); } catch { } });

            // enforce memory-side buffer limits at startup
            try { EnforceDirectoryLimit(_memoryToToolsPath, InToolsBufferSize); } catch { }
            try { EnforceDirectoryLimit(_memoryFromToolsPath, FromToolsBufferSize); } catch { }

            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _toToolsWatcher?.Dispose();
                _fromToolsWatcher?.Dispose();
                _memoryFromWatcher?.Dispose();
            }
        }

        public async Task StartAsync(CancellationToken externalCt)
        {
            // Create a linked CTS that we own and use its token for the internal loop.
            if (_cts != null)
            {
                throw new InvalidOperationException("BufferManager is already running");
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            try
            {
                await StartInternalAsync(_cts.Token);
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
            }
            catch { }
        }

        public async Task ProcessPlansAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                if (!Directory.Exists(_memoryToToolsPath)) return;
                Directory.CreateDirectory(_externalToToolsPath);
                foreach (var file in Directory.GetFiles(_memoryToToolsPath))
                {
                    // Ignore temporary files that may be created by editors or external writers
                    if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

                    // If destination already exists with same size, skip copy to avoid duplicates
                    var dest = Path.Combine(_externalToToolsPath, Path.GetFileName(file));
                    try
                    {
                        if (File.Exists(dest))
                        {
                            try
                            {
                                var sfi = new FileInfo(file);
                                var dfi = new FileInfo(dest);
                                if (sfi.Length == dfi.Length)
                                {
                                    // Same size - verify content to avoid skipping when contents differ but size matches
                                    try
                                    {
                                        var srcBytes = await File.ReadAllBytesAsync(file);
                                        var dstBytes = await File.ReadAllBytesAsync(dest);
                                        if (srcBytes.Length == dstBytes.Length)
                                        {
                                            bool same = true;
                                            for (int i = 0; i < srcBytes.Length; i++)
                                            {
                                                if (srcBytes[i] != dstBytes[i]) { same = false; break; }
                                            }
                                            if (same) continue;
                                        }
                                    }
                                    catch { /* If we can't read to compare, fall through and attempt copy */ }
                                }
                            }
                            catch { }
                        }

                        var ok = await TryCopyWithRetriesAsync(file, dest);
                        if (!ok)
                        {
                            LogError($"Failed to copy ToTools file {file} -> {dest}: retries exhausted");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to copy ToTools file {file} -> {dest}: {ex.Message}");
                    }
                }
                try { EnforceDirectoryLimit(_memoryToToolsPath, InToolsBufferSize); } catch { }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task ProcessResponsesAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                if (!Directory.Exists(_externalFromToolsPath)) return;
                Directory.CreateDirectory(_memoryFromToolsPath);
                foreach (var file in Directory.GetFiles(_externalFromToolsPath))
                {
                    // Ignore temporary files
                    if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

                    // Skip if destination already exists with same size
                    var dest = Path.Combine(_memoryFromToolsPath, Path.GetFileName(file));
                    try
                    {
                        if (File.Exists(dest))
                        {
                            try
                            {
                                var sfi = new FileInfo(file);
                                var dfi = new FileInfo(dest);
                                if (sfi.Length == dfi.Length)
                                {
                                    try
                                    {
                                        var srcBytes = await File.ReadAllBytesAsync(file);
                                        var dstBytes = await File.ReadAllBytesAsync(dest);
                                        if (srcBytes.Length == dstBytes.Length)
                                        {
                                            bool same = true;
                                            for (int i = 0; i < srcBytes.Length; i++)
                                            {
                                                if (srcBytes[i] != dstBytes[i]) { same = false; break; }
                                            }
                                            if (same) continue;
                                        }
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }

                        var ok = await TryCopyWithRetriesAsync(file, dest);
                        if (!ok)
                        {
                            LogError($"Failed to copy FromTools file {file} -> {dest}: retries exhausted");
                        }
                        else
                        {
                            try
                            {
                                // Small diagnostic: log a short snippet of the response so we can verify content was captured.
                                var snippet = string.Empty;
                                try { snippet = (await File.ReadAllTextAsync(dest)).Replace("\r", "").Replace("\n", "\\n"); } catch { }
                                if (snippet.Length > 200) snippet = snippet.Substring(0, 200) + "...";
                                LogDiagnostic($"Copied FromTools file {Path.GetFileName(file)} -> {Path.GetFileName(dest)}: \"{snippet}\"");
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to copy FromTools file {file} -> {dest}: {ex.Message}");
                    }
                }
                try { EnforceDirectoryLimit(_memoryFromToolsPath, FromToolsBufferSize); } catch { }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task ProcessResponseFileAsync(string externalFilePath)
        {
            if (string.IsNullOrWhiteSpace(externalFilePath)) return;

            await _semaphore.WaitAsync();
            try
            {
                if (!File.Exists(externalFilePath)) return;
                if (externalFilePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return; // ignore temp files
                Directory.CreateDirectory(_memoryFromToolsPath);

                var dest = Path.Combine(_memoryFromToolsPath, Path.GetFileName(externalFilePath));
                    try
                    {
                        if (File.Exists(dest))
                        {
                            try
                            {
                                var sfi = new FileInfo(externalFilePath);
                                var dfi = new FileInfo(dest);
                                if (sfi.Length == dfi.Length)
                                {
                                    try
                                    {
                                        var srcBytes = await File.ReadAllBytesAsync(externalFilePath);
                                        var dstBytes = await File.ReadAllBytesAsync(dest);
                                        if (srcBytes.Length == dstBytes.Length)
                                        {
                                            bool same = true;
                                            for (int i = 0; i < srcBytes.Length; i++)
                                            {
                                                if (srcBytes[i] != dstBytes[i]) { same = false; break; }
                                            }
                                            if (same) return;
                                        }
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }

                        var ok = await TryCopyWithRetriesAsync(externalFilePath, dest);
                    if (!ok)
                    {
                        LogError($"Failed to copy FromTools file {externalFilePath} -> {dest}: retries exhausted");
                    }
                    else
                    {
                        try
                        {
                            var snippet = string.Empty;
                            try { snippet = (await File.ReadAllTextAsync(dest)).Replace("\r", "").Replace("\n", "\\n"); } catch { }
                            if (snippet.Length > 200) snippet = snippet.Substring(0, 200) + "...";
                            LogDiagnostic($"Copied FromTools file {Path.GetFileName(externalFilePath)} -> {Path.GetFileName(dest)}: \"{snippet}\"");
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Failed to copy FromTools file {externalFilePath} -> {dest}: {ex.Message}");
                }

                try { EnforceDirectoryLimit(_memoryFromToolsPath, FromToolsBufferSize); } catch { }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task<bool> TryCopyWithRetriesAsync(string source, string dest, int maxAttempts = 3, int delayMs = 150)
        {
            var tempDest = dest + ".tmp";
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    // Ensure destination directory exists
                    var ddir = Path.GetDirectoryName(dest);
                    if (ddir != null && !Directory.Exists(ddir)) Directory.CreateDirectory(ddir);

                    // If source looks like a temp/incomplete file, bail early
                    if (source.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return false;

                    // Copy to temp file first to ensure atomicity
                    File.Copy(source, tempDest, overwrite: true);

                    // Move to final destination (atomic on same volume)
                    File.Move(tempDest, dest, overwrite: true);
                    return true;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    try { if (File.Exists(tempDest)) File.Delete(tempDest); } catch { }
                    await Task.Delay(delayMs);
                }
                catch (UnauthorizedAccessException) when (attempt < maxAttempts)
                {
                    try { if (File.Exists(tempDest)) File.Delete(tempDest); } catch { }
                    await Task.Delay(delayMs);
                }
                catch
                {
                    try { if (File.Exists(tempDest)) File.Delete(tempDest); } catch { }
                    // For other exceptions, don't retry
                    break;
                }
            }

            return false;
        }

        private void LogError(string message)
        {
            try
            {
                if (!Directory.Exists(_logRoot)) Directory.CreateDirectory(_logRoot);
                string logPath = Path.Combine(_logRoot, "AgentErrors.log");
                var line = $"{DateTime.UtcNow:o} {message}{Environment.NewLine}";
                File.AppendAllText(logPath, line);
            }
            catch { }
        }

        private void LogDiagnostic(string message)
        {
            try
            {
                if (!Directory.Exists(_logRoot)) Directory.CreateDirectory(_logRoot);
                string logPath = Path.Combine(_logRoot, "AgentDiagnostics.log");
                var line = $"{DateTime.UtcNow:o} {message}{Environment.NewLine}";
                File.AppendAllText(logPath, line);
            }
            catch { }
        }

        private void DebounceResponsePath(string path, int debounceMs = 200)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            // Cancel any existing debounce for this path
            if (_responseDebounceCts.TryGetValue(path, out var existing))
            {
                try { existing.Cancel(); existing.Dispose(); } catch { }
            }

            var cts = new CancellationTokenSource();
            _responseDebounceCts[path] = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(debounceMs, cts.Token);
                    if (cts.IsCancellationRequested) return;
                    await ProcessResponseFileAsync(path);
                }
                catch (OperationCanceledException) { }
                catch { }
                finally
                {
                    _responseDebounceCts.TryRemove(path, out _);
                    try { cts.Dispose(); } catch { }
                }
            });
        }

        private void DebouncePlans(int debounceMs = 200)
        {
            lock (_plansDebounceLock)
            {
                try { _plansDebounceCts?.Cancel(); _plansDebounceCts?.Dispose(); } catch { }
                _plansDebounceCts = new CancellationTokenSource();
                var token = _plansDebounceCts.Token;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(debounceMs, token);
                        if (token.IsCancellationRequested) return;
                        await ProcessPlansAsync();
                    }
                    catch (OperationCanceledException) { }
                    catch { }
                }, token);
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
                        LogError($"Pruned memory file from {dir}: {files[i].Name}");
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
