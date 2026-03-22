//Copyright Warren Harding 2025.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Collections.Concurrent;
using System.Windows.Controls;
using AGIMynd;

namespace AGIMyndWPF.Controls
{
    public partial class AGIMyndControl : UserControl
    {
        private MyndAgent? _agent;
        private Task? _agentTask;
        private BufferManager? _bufferManager;
        private Task? _bufferTask;
        private CancellationTokenSource? _bufferCts;
        private readonly SemaphoreSlim _startStopSemaphore = new SemaphoreSlim(1, 1);
        private string _memoryRoot;
        private string _pinnedSource;
        private FileSystemWatcher? _watcher;
        private FileSystemWatcher? _toToolsWatcher;
        private FileSystemWatcher? _fromToolsWatcher;
        // Track how much of each log file we've already read so we only append new content
        private readonly ConcurrentDictionary<string, long> _logOffsets = new();
        // Per-file read locks to prevent concurrent handlers from reading the same file twice (used for log-tail)
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileReadLocks = new(StringComparer.OrdinalIgnoreCase);
        private BufferFileLogger? _bufferFileLogger;

        public AGIMyndControl()
        {
            InitializeComponent();
            _memoryRoot = MemoryConfig.GetDefaultMemoryRoot();
            MemoryRootBox.Text = _memoryRoot;

            _pinnedSource = MemoryConfig.GetDefaultPinnedSource();
            
            // Do not create directory immediately here; wait until start or check if user changed path.
            // However, existing code expects _memoryRoot to be valid.
            // Let's create it if it's the default.
            try 
            { 
                Directory.CreateDirectory(_memoryRoot);
                Directory.CreateDirectory(Path.Combine(_memoryRoot, "Pinned"));
                Directory.CreateDirectory(Path.Combine(_memoryRoot, "ToTools"));
                Directory.CreateDirectory(Path.Combine(_memoryRoot, "FromTools"));

                // Honor config: optionally delete AgentErrors.log on startup to keep UI from replaying long logs
                // Delete logs from the EpochLog folder (not the memory root) so logs are excluded from memory context.
                try { MemoryConfig.EnsureDeleteLogOnStartup(Path.Combine(_memoryRoot, "EpochLog")); } catch { }

                // Refresh pinned files on startup
                if (!string.IsNullOrWhiteSpace(_pinnedSource) && Directory.Exists(_pinnedSource))
                {
                    var pinnedDest = Path.Combine(_memoryRoot, "Pinned");
                    
                    // Copy recursively
                    foreach (var dir in Directory.GetDirectories(_pinnedSource, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(_pinnedSource, dir);
                        var destDir = Path.Combine(pinnedDest, relative);
                        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    }

                    foreach (var file in Directory.GetFiles(_pinnedSource, "*.*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(_pinnedSource, file);
                        var dest = Path.Combine(pinnedDest, relative);
                        File.Copy(file, dest, overwrite: true);
                    }
                    AppendLog("Pinned files refreshed on startup.");
                }
            } catch { }

            AppendLog($"Default Memory root: {_memoryRoot}");
            if (!string.IsNullOrWhiteSpace(_pinnedSource))
            {
                AppendLog($"Pinned source: {_pinnedSource}");
            }
            
            // Auto-start only the tool pipeline (BufferManager + watchers).
            // Do NOT start the agent LLM loop on startup.
            Loaded += async (s, e) => await StartToolPipelineAsync();
            
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }

        private void AppendLog(string text)
        {
            Dispatcher.Invoke(() =>
            {
                LogPane.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
                LogPane.ScrollToEnd();
            });
        }

        private static string ShortenForLog(string? s, int max = 200)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var single = s.Replace("\r\n", "\\n").Replace("\n", "\\n");
            if (single.Length <= max) return single;
            return single.Substring(0, max) + "...";
        }

        private void ClearMemoriesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!StartButton.IsEnabled)
            {
                AppendLog("Cannot clear memories while agent is running.");
                return;
            }

            // Update configuration from UI
            string newRoot = MemoryRootBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(newRoot) && !string.Equals(newRoot, _memoryRoot, StringComparison.OrdinalIgnoreCase))
            {
                _memoryRoot = newRoot;
                MemoryConfig.SetMemoryRoot(_memoryRoot);
                AppendLog($"Memory root updated to: {_memoryRoot}");
            }

            try
            {
                if (Directory.Exists(_memoryRoot))
                {
                    // Clean up existing memory
                    var dirInfo = new DirectoryInfo(_memoryRoot);
                    foreach (var file in dirInfo.GetFiles())
                    {
                        try { file.Delete(); } catch { }
                    }
                    foreach (var dir in dirInfo.GetDirectories())
                    {
                        try { dir.Delete(true); } catch { }
                    }
                    AppendLog("Memory folder wiped.");
                }
                else
                {
                    Directory.CreateDirectory(_memoryRoot);
                }

                // Restore pinned files
                if (!string.IsNullOrWhiteSpace(_pinnedSource) && Directory.Exists(_pinnedSource))
                {
                    var pinnedDest = Path.Combine(_memoryRoot, "Pinned");
                    Directory.CreateDirectory(pinnedDest);

                    // Copy recursively to match MyndAgent behavior
                    foreach (var dir in Directory.GetDirectories(_pinnedSource, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(_pinnedSource, dir);
                        var destDir = Path.Combine(pinnedDest, relative);
                        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    }

                    foreach (var file in Directory.GetFiles(_pinnedSource, "*.*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(_pinnedSource, file);
                        var dest = Path.Combine(pinnedDest, relative);
                        File.Copy(file, dest, overwrite: true);
                    }
                    AppendLog("Pinned files restored from master copies.");
                }
                else
                {
                    AppendLog("No pinned source found to restore from.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Error clearing memories: {ex.Message}");
            }
        }

        private void BrowseMemoriesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(_memoryRoot))
                {
                    Directory.CreateDirectory(_memoryRoot);
                }
                System.Diagnostics.Process.Start("explorer.exe", _memoryRoot);
            }
            catch (Exception ex)
            {
                AppendLog($"Error opening memory folder: {ex.Message}");
            }
        }

        private void MemoryRootBox_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                try
                {
                    string newRoot = MemoryRootBox.Text.Trim();
                    if (!string.IsNullOrWhiteSpace(newRoot) && !string.Equals(newRoot, _memoryRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        _memoryRoot = newRoot;
                        MemoryConfig.SetMemoryRoot(_memoryRoot);
                        AppendLog($"Memory root updated to: {_memoryRoot}");
                    }
                }
                catch { }

                e.Handled = true;
            }
        }

        private void MaxEpochsBox_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                try
                {
                    var txt = MaxEpochsBox.Text ?? string.Empty;
                    if (int.TryParse(txt.Trim(), out var me) && me >= 0)
                    {
                        if (_agent != null)
                        {
                            _agent.MaxEpochs = me;
                            AppendLog($"MaxEpochs updated on agent: {me}");
                        }
                        else
                        {
                            AppendLog($"MaxEpochs stored in UI (will be applied when agent starts): {me}");
                        }
                    }
                }
                catch { }

                e.Handled = true;
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            _ = StartAgentLoopAsync();
        }

        private Task StartToolPipelineAsync() => StartToolPipelineAsyncCore(acquireLock: true);

        private async Task StartToolPipelineAsyncCore(bool acquireLock)
        {
            if (acquireLock) await _startStopSemaphore.WaitAsync();
            try
            {
                // Update configuration from UI
                string newRoot = MemoryRootBox.Text.Trim();
                var requestedRoot = string.IsNullOrWhiteSpace(newRoot) ? _memoryRoot : newRoot;
                var rootChanged = !string.Equals(requestedRoot, _memoryRoot, StringComparison.OrdinalIgnoreCase);

                if (rootChanged)
                {
                    _memoryRoot = requestedRoot;
                    MemoryConfig.SetMemoryRoot(_memoryRoot);
                    AppendLog($"Memory root updated to: {_memoryRoot}");
                }

                var pipelineRunning = (_bufferTask != null && !_bufferTask.IsCompleted) || _watcher != null || _toToolsWatcher != null || _fromToolsWatcher != null;
                if (pipelineRunning && !rootChanged)
                {
                    // Pipeline already running for the current root.
                    return;
                }

                // If the root changed, stop any existing pipeline resources so we can restart cleanly.
                if (pipelineRunning && rootChanged)
                {
                    try
                    {
                        _bufferCts?.Cancel();
                        _bufferManager?.Stop();
                        if (_bufferTask != null)
                        {
                            try { await _bufferTask; } catch { }
                        }
                    }
                    catch { }

                    _bufferCts?.Dispose();
                    _bufferCts = null;
                    _bufferManager = null;
                    _bufferTask = null;

                    _watcher?.Dispose();
                    _toToolsWatcher?.Dispose();
                    _fromToolsWatcher?.Dispose();
                    _bufferFileLogger?.Dispose();
                    _watcher = null;
                    _toToolsWatcher = null;
                    _fromToolsWatcher = null;
                    _bufferFileLogger = null;
                }

                // Ensure directory exists
                try { Directory.CreateDirectory(_memoryRoot); }
                catch (Exception ex) { AppendLog($"Error creating memory root: {ex.Message}"); return; }

                try
                {
                    // Do NOT persist goal to a goal.txt file here anymore.
                    // The agent will receive the UI goal via UpdateGoalAsync when started.
                    AppendLog("Skipping write to goal file; using in-memory goal instead.");
                }
                catch { }

                try
                {
                    var repoRoot = MemoryConfig.FindRepoRoot();
                    if (!string.IsNullOrWhiteSpace(repoRoot))
                    {
                        var externalPlans = Path.Combine(repoRoot, "External", "ToTools");
                        var externalResponses = Path.Combine(repoRoot, "External", "FromTools");

                        _bufferCts = new CancellationTokenSource();
                        _bufferManager = new BufferManager(_memoryRoot, externalPlans, externalResponses);
                        _bufferTask = Task.Run(async () =>
                        {
                            AppendLog($"Starting BufferManager: {externalPlans} <-> {externalResponses}");
                            try
                            {
                                await _bufferManager.StartAsync(_bufferCts.Token);
                            }
                            catch (OperationCanceledException)
                            {
                            }
                            catch (Exception ex)
                            {
                                AppendLog($"BufferManager threw: {ex.Message}");
                            }
                            AppendLog("BufferManager ended.");
                        });
                    }
                    else
                    {
                        AppendLog("Repo root not found; BufferManager not started (External/ToTools will not be used).");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"Failed to start BufferManager: {ex.Message}");
                }

                _watcher?.Dispose();
                _watcher = new FileSystemWatcher(_memoryRoot)
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                _watcher.Created += OnFileChanged;
                _watcher.Changed += OnFileChanged;

                // Instantiate the buffer-file logger that will handle debouncing and reading
                _bufferFileLogger?.Dispose();
                _bufferFileLogger = new BufferFileLogger(_memoryRoot, AppendLog);

                // Watch ToTools and FromTools buffers to log full contents when files are dropped in
                try
                {
                    var toToolsPath = Path.Combine(_memoryRoot, "ToTools");
                    var fromToolsPath = Path.Combine(_memoryRoot, "FromTools");

                    if (!Directory.Exists(toToolsPath)) Directory.CreateDirectory(toToolsPath);
                    if (!Directory.Exists(fromToolsPath)) Directory.CreateDirectory(fromToolsPath);

                    _toToolsWatcher?.Dispose();
                    _toToolsWatcher = new FileSystemWatcher(toToolsPath)
                    {
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };
                    _toToolsWatcher.Created += (s, e) => _bufferFileLogger?.NotifyFileChanged(e.FullPath);
                    _toToolsWatcher.Changed += (s, e) => _bufferFileLogger?.NotifyFileChanged(e.FullPath);

                    _fromToolsWatcher?.Dispose();
                    _fromToolsWatcher = new FileSystemWatcher(fromToolsPath)
                    {
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };
                    _fromToolsWatcher.Created += (s, e) => _bufferFileLogger?.NotifyFileChanged(e.FullPath);
                    _fromToolsWatcher.Changed += (s, e) => _bufferFileLogger?.NotifyFileChanged(e.FullPath);
                }
                catch { }
            }
            finally
            {
                if (acquireLock) _startStopSemaphore.Release();
            }
        }

        private async Task StartAgentLoopAsync()
        {
            await _startStopSemaphore.WaitAsync();
            try
            {
                if (_agentTask != null && !_agentTask.IsCompleted)
                {
                    AppendLog("Agent already running.");
                    return;
                }

                await StartToolPipelineAsyncCore(acquireLock: false);

                _agent = new MyndAgent(_memoryRoot, pinnedSource: string.IsNullOrWhiteSpace(_pinnedSource) ? null : _pinnedSource);

                // Stream agent trace into the UI log pane for observability.
                _agent.DeletionNotificationAsync = (msg) => { AppendLog(msg); return Task.CompletedTask; };
                _agent.TraceNotificationAsync = (msg) => { AppendLog(msg); return Task.CompletedTask; };

                // Ensure agent has the UI goal value before starting the loop
                try
                {
                    var g = GoalPane.Text ?? string.Empty;
                    AppendLog($"Updating agent goal from UI before start: {ShortenForLog(g)}");
                    await _agent.UpdateGoalAsync(g);
                }
                catch (Exception ex) { AppendLog($"Error updating agent goal: {ex.Message}"); }

                // Read MaxEpochs from UI and apply to agent (0 = unlimited)
                try
                {
                    var txt = MaxEpochsBox.Text ?? string.Empty;
                    if (int.TryParse(txt.Trim(), out var me) && me >= 0)
                    {
                        _agent.MaxEpochs = me;
                        AppendLog($"MaxEpochs set to {me} on agent.");
                    }
                    else
                    {
                        // default to 0 (unlimited)
                        _agent.MaxEpochs = 0;
                    }
                }
                catch { }

                _agentTask = Task.Run(async () =>
                {
                    AppendLog("Starting agent loop.");
                    try
                    {
                        // Start the agent; agent uses its in-memory `Goal` property.
                        await _agent.StartAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Agent threw: {ex.Message}");
                    }
                    AppendLog("Agent loop ended.");
                });

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
            }
            finally
            {
                _startStopSemaphore.Release();
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_agent == null) { AppendLog("Agent not running."); return; }
            AppendLog("Stopping agent...");
            _agent.Stop();
            if (_agentTask != null)
            {
                try { await _agentTask; } catch { }
            }
            _agent = null;
            _agentTask = null;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }

        private void UpdateGoalButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_agent != null)
                {
                    var g = GoalPane.Text ?? string.Empty;
                    _ = _agent.UpdateGoalAsync(g);
                    AppendLog($"Goal updated on agent: {ShortenForLog(g)}");
                    try
                    {
                        var txt = MaxEpochsBox.Text ?? string.Empty;
                        if (int.TryParse(txt.Trim(), out var me) && me >= 0)
                        {
                            _agent.MaxEpochs = me;
                            AppendLog($"MaxEpochs updated on agent: {me}");
                        }
                    }
                    catch { }
                }
                else
                {
                    // If agent not running, persist a copy in the local memory root so StartAsync can pick it up when started.
                    try
                    {
                        var g = GoalPane.Text ?? string.Empty;
                        // Do not write to the goal file when agent isn't running; keep goal in UI until agent starts.
                        AppendLog($"Goal stored in UI (will be applied when agent starts): {ShortenForLog(g)}");
                    }
                    catch (Exception ex) { AppendLog($"Failed to handle goal update: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to update goal: {ex.Message}");
            }
        }

        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Tail JSON and log files (AgentErrors/AgentDiagnostics and other *.log under EpochLog).
                if (!(e.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                      || e.FullPath.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                      || Path.GetFileName(e.FullPath).Equals("AgentErrors.log", StringComparison.OrdinalIgnoreCase)))
                    return;

                // Small debounce to let writers finish
                await Task.Delay(200).ConfigureAwait(false);

                // Serialize reads per-file to avoid race where Created and Changed handlers run concurrently
                var fileLock = _fileReadLocks.GetOrAdd(e.FullPath, _ => new SemaphoreSlim(1, 1));
                await fileLock.WaitAsync().ConfigureAwait(false);
                string newContent = string.Empty;
                try
                {
                    long lastOffset = _logOffsets.GetOrAdd(e.FullPath, 0);

                    // Determine current length
                    long length = 0;
                    try
                    {
                        var fi = new FileInfo(e.FullPath);
                        length = fi.Exists ? fi.Length : 0;
                    }
                    catch { length = 0; }

                    // If file was truncated, reset offset so we read from start
                    if (length < lastOffset) lastOffset = 0;

                    if (length <= lastOffset)
                    {
                        // Nothing new to read
                        _logOffsets[e.FullPath] = lastOffset;
                        return;
                    }

                    try
                    {
                        using var fs = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        fs.Seek(lastOffset, SeekOrigin.Begin);
                        using var reader = new StreamReader(fs);
                        newContent = await reader.ReadToEndAsync().ConfigureAwait(false);
                        _logOffsets[e.FullPath] = fs.Position;
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Error reading {e.FullPath}: {ex.Message}");
                        return;
                    }
                }
                finally
                {
                    try { fileLock.Release(); } catch { }
                }

                if (!string.IsNullOrEmpty(newContent))
                {
                    AppendLog($"Log file: {Path.GetFileName(e.FullPath)}\n{newContent}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Error reading {e.FullPath}: {ex.Message}");
            }
        }

        // Buffer file handling is delegated to BufferFileLogger to allow unit testing and single authoritative behavior.

        // Called by hosting window to stop all background work and release resources.
        public void Shutdown()
        {
            try { _bufferCts?.Cancel(); } catch { }

            try { _agent?.Stop(); } catch { }

            try
            {
                if (_agentTask != null)
                {
                    try { _agentTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
                }
            }
            catch { }

            try { _bufferManager?.Stop(); } catch { }

            try
            {
                if (_bufferTask != null)
                {
                    try { _bufferTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
                }
            }
            catch { }

            try { _bufferCts?.Dispose(); } catch { }
            _bufferCts = null;
            _bufferManager = null;
            _bufferTask = null;

            try { _watcher?.Dispose(); } catch { }
            try { _toToolsWatcher?.Dispose(); } catch { }
            try { _fromToolsWatcher?.Dispose(); } catch { }
            _watcher = null;
            _toToolsWatcher = null;
            _fromToolsWatcher = null;

            try { _bufferFileLogger?.Dispose(); } catch { }
            _bufferFileLogger = null;
        }
    }
}
