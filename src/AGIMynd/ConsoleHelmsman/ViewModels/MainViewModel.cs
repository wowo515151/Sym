//Copyright Warren Harding 2025.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;

namespace ConsoleHelmsman;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private const int LogCapacityChars = 200000;
    private const int MaxDisplayChars = 20000;
    private const int MaxInternalReportChars = 20000;
    private readonly ConsoleConfigService _configService = new();
    private readonly ConsoleAppBases _config;
    private readonly ConsoleProcessService _processService = new();
    private readonly OneShotRunner _oneShotRunner;
    private readonly Dictionary<string, ConsoleAppBase> _definitions;
    private readonly DispatcherTimer _displayTimer;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private ConsoleLog? _currentLog;
    private ConsoleAppBase? _currentDefinition;
    private int _displayDirty;
    private string? _selectedConsole;

    private readonly StringBuilder _internalReportBuilder = new();

    private string _consoleDisplayText = string.Empty;
    private string _consoleInputText = string.Empty;
    private bool _isSendEnabled = true;
    private int _pendingExitToReenableSend;
    private string _statusText = "Idle";
    private bool _isProcessRunning;
    private bool _isCurrentDirectoryValid;
    private int _maxEpochs = 500;
    private bool _repeat = false;
    private OneShotMode _selectedOneShotMode = OneShotMode.Auto;
    private bool _isOneShotComboVisible = true;
    private bool _isEmbeddedHost = false;

    private PlanBridgeService? _planBridge;
    private bool _isPlanBridgeEnabled = true;
    private string _planBridgeStatusText = "PlanBridge: (starting)";

    public MainViewModel()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _config = _configService.LoadOrCreate();
        var config = _config; // local alias for existing code

        _definitions = config.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

        AvailableConsoles = new ObservableCollection<string>();
        RefreshAvailableConsoles();

        SendCommand = new RelayCommand(SendInput, () => IsSendEnabled);
        StartCommand = new RelayCommand(() => _ = StartCurrentAsync(), CanStartCurrent);
        StopCommand = new RelayCommand(() => _ = StopCurrentAsync());
        ClearLogCommand = new RelayCommand(ClearLog);

        // Initialize CurrentDirectory from config (or fallback to process cwd).
        CurrentDirectory = string.IsNullOrWhiteSpace(_config.CurrentDirectory)
            ? Environment.CurrentDirectory
            : _config.CurrentDirectory;
        _processService.WorkingDirectory = CurrentDirectory;
        ValidateCurrentDirectory(CurrentDirectory);

        _processService.ProcessExited += OnProcessExited;
        _oneShotRunner = new OneShotRunner(_processService);

        OpenSelectorCommand = new RelayCommand(OpenSelector);
        OpenConfigCommand = new RelayCommand(OpenConfig);

        _displayTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _displayTimer.Tick += DisplayTimerOnTick;
        _displayTimer.Start();

        SetCurrentLog(new ConsoleLog(LogCapacityChars));

        UpdatePlanBridgeStatusText();
        StartPlanBridgeIfPossible();
    }

    public enum OneShotMode
    {
        Auto,
        OneShot,
        Interactive
    }

    public OneShotMode SelectedOneShotMode
    {
        get => _selectedOneShotMode;
        set => SetField(ref _selectedOneShotMode, value);
    }

    public IEnumerable<OneShotMode> OneShotModeItems => Enum.GetValues(typeof(OneShotMode)).Cast<OneShotMode>();

    public bool IsOneShotComboVisible
    {
        get => _isOneShotComboVisible;
        set => SetField(ref _isOneShotComboVisible, value);
    }

    public bool IsEmbeddedHost
    {
        get => _isEmbeddedHost;
        set
        {
            if (SetField(ref _isEmbeddedHost, value))
            {
                // When hosted by AGIHelmsman, hide control and force one-shot behavior
                IsOneShotComboVisible = !value;
                if (value)
                {
                    SelectedOneShotMode = OneShotMode.OneShot;
                }
            }
        }
    }

    public bool IsPlanBridgeEnabled
    {
        get => _isPlanBridgeEnabled;
        set
        {
            if (!SetField(ref _isPlanBridgeEnabled, value))
            {
                return;
            }

            if (_isPlanBridgeEnabled)
            {
                StartPlanBridgeIfPossible();
            }
            else
            {
                StopPlanBridge();
            }

            UpdatePlanBridgeStatusText();
        }
    }

    public string PlanBridgeStatusText
    {
        get => _planBridgeStatusText;
        set => SetField(ref _planBridgeStatusText, value);
    }

    private void StartPlanBridgeIfPossible()
    {
        if (!_isPlanBridgeEnabled)
        {
            return;
        }

        if (_planBridge != null)
        {
            UpdatePlanBridgeStatusText();
            return;
        }

        try
        {
            var repoRoot = RepoPathService.FindRepoRoot();
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                AppendInternalReport("PlanBridge disabled: repo root not found.");
                PlanBridgeStatusText = "PlanBridge: repo root not found";
                return;
            }

            _planBridge = new PlanBridgeService(
                repoRoot,
                getDefinitionByName: (name) => _definitions.TryGetValue(name ?? string.Empty, out var d) ? d : null,
                getSelectedConsoleName: () => SelectedConsole,
                getWorkingDirectory: () => CurrentDirectory,
                log: AppendInternalReport,
                sendToConsoleAsync: async (name, command) => await HandleExternalCommandAsync(name, command));

            _planBridge.Start();
            AppendInternalReport("PlanBridge enabled.");
            UpdatePlanBridgeStatusText();
        }
        catch (Exception ex)
        {
            AppendInternalReport("PlanBridge failed to start: " + ex.Message);
            PlanBridgeStatusText = "PlanBridge: error";
        }
    }

    private void StopPlanBridge()
    {
        try
        {
            _planBridge?.Dispose();
        }
        catch
        {
        }

        _planBridge = null;
        AppendInternalReport("PlanBridge disabled.");
    }

    private void UpdatePlanBridgeStatusText()
    {
        if (!_isPlanBridgeEnabled)
        {
            PlanBridgeStatusText = "PlanBridge: Off";
            return;
        }

        PlanBridgeStatusText = _planBridge != null ? "PlanBridge: On" : "PlanBridge: On (pending)";
    }

    public ObservableCollection<string> AvailableConsoles { get; }

    public string ConsoleDisplayText
    {
        get => _consoleDisplayText;
        set => SetField(ref _consoleDisplayText, value);
    }

    public string ConsoleInputText
    {
        get => _consoleInputText;
        set => SetField(ref _consoleInputText, value);
    }

    public bool IsSendEnabled
    {
        get => _isSendEnabled;
        set
        {
            if (SetField(ref _isSendEnabled, value))
            {
                SendCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public string? SelectedConsole
    {
        get => _selectedConsole;
        set
        {
            if (SetField(ref _selectedConsole, value))
            {
                // Do not auto-start or switch the console when the user selects it in the UI dropdown.
                // Selection should only change which console name is selected; starting/stopping
                // is controlled by the Start/Stop buttons.
                StartCommand?.RaiseCanExecuteChanged();
            }
        }
    }


    public bool IsProcessRunning
    {
        get => _isProcessRunning;
        set => SetField(ref _isProcessRunning, value);
    }

    public int MaxEpochs
    {
        get => _maxEpochs;
        set => SetField(ref _maxEpochs, value);
    }

    public bool Repeat
    {
        get => _repeat;
        set => SetField(ref _repeat, value);
    }

    private bool CanStartCurrent()
    {
        return !string.IsNullOrWhiteSpace(SelectedConsole) && _isCurrentDirectoryValid && !_isProcessRunning;
    }

    private void ValidateCurrentDirectory(string? path)
    {
        var exists = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        if (SetField(ref _isCurrentDirectoryValid, exists, nameof(IsCurrentDirectoryValid)))
        {
            // Validation feedback is implicit via StartCommand CanExecute.
            StartCommand?.RaiseCanExecuteChanged();
        }
    }

    public bool IsCurrentDirectoryValid => _isCurrentDirectoryValid;

    // CurrentDirectoryValidationMessage removed: validation feedback is implicit via Start button enablement.

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    private string _currentDirectory = string.Empty;
    public string CurrentDirectory
    {
        get => _currentDirectory;
        set
        {
            if (SetField(ref _currentDirectory, value))
            {
                _processService.WorkingDirectory = value;
                ValidateCurrentDirectory(value);
                StartCommand?.RaiseCanExecuteChanged();
                try
                {
                    _config.CurrentDirectory = value ?? string.Empty;
                    _configService.Save(_config);
                    AppendInternalReport("Saved CurrentDirectory to config.");
                }
                catch
                {
                    AppendInternalReport("Failed to save CurrentDirectory to config.");
                }
            }
        }
    }

    public RelayCommand SendCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ClearLogCommand { get; }
    public RelayCommand StartCommand { get; }
    public RelayCommand OpenSelectorCommand { get; }
    public RelayCommand OpenConfigCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Shutdown()
    {
        _displayTimer.Stop();

        // Persist current directory to config.
        try
        {
            _config.CurrentDirectory = CurrentDirectory ?? string.Empty;
            _configService.Save(_config);
        }
        catch
        {
        }

        Dispose();
    }

    private string[] GetSelectedConsoleNames()
    {
        // Return all configured console names (non-empty) so the UI dropdown shows every available CLI.
        return _definitions.Keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .OrderBy(k => k)
            .ToArray();
    }

    private void RefreshAvailableConsoles()
    {
        AvailableConsoles.Clear();
        var names = GetSelectedConsoleNames();
        foreach (var n in names)
        {
            AvailableConsoles.Add(n);
        }

        // Do not auto-select a console on startup. If a previously selected console
        // is no longer available, clear the selection so the UI shows no selection
        // until the user picks one.
        if (!string.IsNullOrWhiteSpace(SelectedConsole) && !AvailableConsoles.Contains(SelectedConsole))
        {
            SelectedConsole = null;
        }

        ExportActiveTools();
    }

    private void ExportActiveTools()
    {
        try
        {
            var repoRoot = RepoPathService.FindRepoRoot();
            if (string.IsNullOrWhiteSpace(repoRoot)) return;

            var externalDir = Path.Combine(repoRoot, "External");
            if (!Directory.Exists(externalDir)) Directory.CreateDirectory(externalDir);

            var path = Path.Combine(externalDir, "ActiveTools.json");

            string GetDescription(string name, bool oneShot)
            {
                if (string.Equals(name, "codex", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "copilot", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "gemini", StringComparison.OrdinalIgnoreCase))
                {
                    return "Concise English instructions for code generation.";
                }
                return oneShot ? "Concise English instructions." : "Command string for the target CLI.";
            }

            var toolList = new
            {
                Tools = _config.Items
                    .Where(d => d.Selected && !string.IsNullOrWhiteSpace(d.Name))
                    .Select(d => new
                    {
                        ToolName = d.Name,
                        ToolInputRequirements = GetDescription(d.Name, d.OneShot)
                    })
                    .ToList()
            };

            var json = JsonSerializer.Serialize(toolList, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }

    private void OpenSelector()
    {
        // Ensure UI interaction on dispatcher
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(new Action(OpenSelector));
            return;
        }

        var window = new CLISelectorWindow(_config, _configService)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        var result = window.ShowDialog();
        if (result == true)
        {
            // Config saved by selector; refresh available list
            RefreshAvailableConsoles();
        }
    }

    private void OpenConfig()
    {
        try
        {
            // Ensure config is persisted before opening
            _configService.Save(_config);
        }
        catch { }

        try
        {
            var path = _configService.ConfigPath;
            var psi = new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true };
            Process.Start(psi);
        }
        catch
        {
            AppendInternalReport("Failed to open config file in Notepad.");
        }
    }

    private void AppendInternalReport(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _internalReportBuilder.AppendLine(line);

        if (_internalReportBuilder.Length > MaxInternalReportChars)
        {
            var text = _internalReportBuilder.ToString();
            text = text.Substring(Math.Max(0, text.Length - MaxInternalReportChars));
            _internalReportBuilder.Clear();
            _internalReportBuilder.Append(text);
        }
    }

    public void Dispose()
    {
        _processService.ProcessExited -= OnProcessExited;
        if (_currentLog != null)
        {
            _currentLog.Updated -= OnLogUpdated;
        }

        StopPlanBridge();

        _processService.Dispose();
        _displayTimer.Stop();
        _switchLock.Dispose();
    }

    private Task SwitchConsoleAsync(string? consoleName)
        => SwitchConsoleAsync(consoleName, requireSelectedMatch: true);

    private async Task SwitchConsoleAsync(string? consoleName, bool requireSelectedMatch)
    {
        if (string.IsNullOrWhiteSpace(consoleName))
        {
            return;
        }

        // If called from a background thread, marshal to the dispatcher.
        if (!_dispatcher.CheckAccess())
        {
            await _dispatcher.InvokeAsync(() => SwitchConsoleAsync(consoleName, requireSelectedMatch)).Task.Unwrap();
            return;
        }

        await _switchLock.WaitAsync();
        try
        {
            if (!string.Equals(_selectedConsole, consoleName, StringComparison.OrdinalIgnoreCase))
            {
                _selectedConsole = consoleName;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedConsole)));
                StartCommand?.RaiseCanExecuteChanged();
            }

            if (requireSelectedMatch && !string.Equals(consoleName, SelectedConsole, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!_definitions.TryGetValue(consoleName, out var definition))
            {
                _currentLog?.AppendSystemMessage($"[Unknown console: {consoleName}]");
                StatusText = "Error";
                return;
            }

            await StopCurrentAsyncInternal();

            var newLog = new ConsoleLog(LogCapacityChars);
            SetCurrentLog(newLog);
            _currentDefinition = definition;

            // No per-console start directory: keep using global CurrentDirectory.

            if (definition.OneShot)
            {
                StatusText = "Ready";
                IsProcessRunning = false;
                return;
            }

            if (!_processService.Start(definition, newLog, out var error))
            {
                newLog.AppendSystemMessage($"[Failed to start: {error}]");
                StatusText = "Error";
                IsProcessRunning = false;
                return;
            }

            StatusText = "Running";
            IsProcessRunning = true;
        }
        finally
        {
            _switchLock.Release();
        }
    }

    private async Task StopCurrentAsync()
    {
        await _switchLock.WaitAsync();
        try
        {
            await StopCurrentAsyncInternal();
        }
        finally
        {
            _switchLock.Release();
        }

        // When Stop is invoked (ConsoleAppStop), re-enable the Send button on the UI thread.
        await _dispatcher.InvokeAsync(new Action(() =>
        {
            IsSendEnabled = true;
            SendCommand?.RaiseCanExecuteChanged();
        })).Task;
    }

    private async Task StopCurrentAsyncInternal()
    {
        await _processService.StopAsync();

        // Any pending one-shot run(s) are cancelled by a Stop.
        _pendingExitToReenableSend = 0;

        // Ensure UI reflects a stopped process immediately.
        IsProcessRunning = false;
        if (StatusText == "Running")
        {
            StatusText = "Stopped";
        }
    }

    private async Task ReenableSend()
    {
        if (_dispatcher.CheckAccess())
        {
            IsSendEnabled = true;
            SendCommand?.RaiseCanExecuteChanged();
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            IsSendEnabled = true;
            SendCommand?.RaiseCanExecuteChanged();
        });
    }

    private async Task StartCurrentAsync()
    {
        await _switchLock.WaitAsync();
        try
        {
            // Ensure we have a definition for the currently selected console name.
            if (_currentDefinition == null)
            {
                if (string.IsNullOrWhiteSpace(SelectedConsole))
                {
                    _currentLog?.AppendSystemMessage("[Cannot start: no console selected]");
                    return;
                }

                if (!_definitions.TryGetValue(SelectedConsole, out var def))
                {
                    _currentLog?.AppendSystemMessage($"[Unknown console: {SelectedConsole}]");
                    StatusText = "Error";
                    return;
                }

                // Stop any existing process and prepare a fresh log for the newly selected console.
                await StopCurrentAsyncInternal();
                var newLog = new ConsoleLog(LogCapacityChars);
                SetCurrentLog(newLog);
                _currentDefinition = def;
            }

            if (_currentDefinition.OneShot)
            {
                _currentLog?.AppendSystemMessage("[Cannot start: current console is OneShot]");
                return;
            }

            if (_processService.Start(_currentDefinition, _currentLog ?? new ConsoleLog(LogCapacityChars), out var error))
            {
                StatusText = "Running";
                IsProcessRunning = true;
            }
            else
            {
                _currentLog?.AppendSystemMessage($"[Failed to start: {error}]");
                StatusText = "Error";
                IsProcessRunning = false;
            }
        }
        finally
        {
            _switchLock.Release();
        }
    }

    private async void SendInput()
    {
        // Preserve existing behavior for non-UI callers by forwarding to the new implementation.
        await SendInputFromUI(ConsoleInputText ?? string.Empty);
    }

    // Public entry used by UI code-behind to send using the live TextBox.Text value
    public async Task SendInputFromUI(string input)
    {
        // Do not clear the Console Input text on send per UI requirements.
        // Disable the Send button and clear the Console Display while the send is in progress.
        if (_dispatcher.CheckAccess())
        {
            IsSendEnabled = false;
            SendCommand?.RaiseCanExecuteChanged();
        }
        else
        {
            await _dispatcher.InvokeAsync(() =>
            {
                IsSendEnabled = false;
                SendCommand?.RaiseCanExecuteChanged();
            });
        }

        // Clear both UI and underlying log so old output doesn't immediately re-appear.
        ClearLog();

        if (_currentLog == null)
        {
            await ReenableSend();
            return;
        }

        var definition = _currentDefinition;
        if (definition == null)
        {
            _currentLog.AppendSystemMessage("[Cannot send: no console selected]");
            await ReenableSend();
            return;
        }

        // If Repeat is enabled, always use one-shot mode
        if (_repeat || SelectedOneShotMode == OneShotMode.OneShot || (SelectedOneShotMode == OneShotMode.Auto && definition.OneShot))
        {
            // Keep Send disabled until the spawned process (or repeat batch) finishes.
            _pendingExitToReenableSend = _repeat ? Math.Max(1, _maxEpochs) : 1;
            await RunOneShotAsync(definition, input);
            return;
        }

        // Interactive selected explicitly
        if (SelectedOneShotMode == OneShotMode.Interactive || (SelectedOneShotMode == OneShotMode.Auto && !definition.OneShot))
        {
            if (_processService.TrySendInput(input, out var sentText, out _))
            {
                _currentLog.AppendInput(sentText);
            }
            else
            {
                _currentLog.AppendSystemMessage("[Cannot send: process not running]");
            }

            await ReenableSend();
        }
    }

    private async Task<string?> RunOneShotAsync(ConsoleAppBase definition, string input)
    {
        if (_currentLog == null)
        {
            _pendingExitToReenableSend = 0;
            await ReenableSend();
            return null;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            _currentLog.AppendSystemMessage("[Cannot send: empty input]");
            _pendingExitToReenableSend = 0;
            await ReenableSend();
            return null;
        }

        await _switchLock.WaitAsync();
        try
        {
            if (!ReferenceEquals(definition, _currentDefinition))
            {
                return null;
            }

            int runs = _repeat ? Math.Max(1, _maxEpochs) : 1;
            const int timeoutMs = 15 * 60 * 1000; // 15 minutes per run

            for (int i = 0; i < runs; i++)
            {
                int attempt = 0;
                const int maxAttempts = 2; // allow one restart on timeout

                while (attempt < maxAttempts)
                {
                    var result = _oneShotRunner.Run(definition, _currentLog, input);
                    if (!result.Started)
                    {
                        _currentLog.AppendSystemMessage($"[Failed to start: {result.Error}]");
                        StatusText = "Error";
                        IsProcessRunning = false;
                        _pendingExitToReenableSend = 0;
                        await ReenableSend();
                        return null;
                    }

                    StatusText = "Running";
                    IsProcessRunning = true;

                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        _currentLog.AppendSystemMessage($"[Failed to send: {result.Error}]");
                    }

                    // If repeating or if this is a restarted attempt, wait for the current one-shot process to complete
                    int waited = 0;
                    while (_processService.CurrentInstance?.IsRunning == true && waited < timeoutMs)
                    {
                        await Task.Delay(200);
                        waited += 200;
                    }

                    // update running state
                    var stillRunning = _processService.CurrentInstance?.IsRunning == true;
                    IsProcessRunning = stillRunning;

                    if (stillRunning && waited >= timeoutMs)
                    {
                        // Timeout reached: kill the hung process and retry this run (once).
                        try
                        {
                            _currentLog.AppendSystemMessage("[Run timed out; killing process and retrying]");
                            try
                            {
                                _processService.CurrentInstance?.Process.Kill(true);
                            }
                            catch
                            {
                                // ignore kill exceptions
                            }

                            // Give the process a short moment to exit.
                            var swWait = 0;
                            while (_processService.CurrentInstance?.IsRunning == true && swWait < 2000)
                            {
                                await Task.Delay(100);
                                swWait += 100;
                            }
                        }
                        catch
                        {
                            // ignore
                        }

                        // update state and prepare to retry
                        IsProcessRunning = _processService.CurrentInstance?.IsRunning == true;
                        attempt++;

                        if (attempt >= maxAttempts)
                        {
                            // Give up after allowed attempts
                            _currentLog.AppendSystemMessage("[Run failed after timeout and retry]");
                            _pendingExitToReenableSend = 0;
                            await ReenableSend();
                            return null;
                        }

                        // retry: continue to next attempt loop iteration
                        continue;
                    }

                    // Normal completion (process exited or not running). Proceed to next run.
                    break;
                }
            }

            return null;
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public Task<string?> HandleExternalCommandAsync(string consoleName, string command)
    {
        // Ensure UI-bound operations run on the dispatcher and reuse existing helpers.
        return _dispatcher.InvokeAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(consoleName)) return (string?)null;

            // Switch to the requested console (force switch even if not selected).
            await SwitchConsoleAsync(consoleName, requireSelectedMatch: false);

            if (!_definitions.TryGetValue(consoleName, out var def))
            {
                _currentLog?.AppendSystemMessage($"[ConsoleCommander] Unknown console: {consoleName}");
                return (string?)$"Error: Unknown console {consoleName}";
            }

            if (def.OneShot)
            {
                // Run as one-shot
                return await RunOneShotAsync(def, command);
            }

            // Non-one-shot: ensure process is started
            if (_currentDefinition == null || !ReferenceEquals(def, _currentDefinition))
            {
                await SwitchConsoleAsync(consoleName, requireSelectedMatch: false);
            }

            if (_processService.CurrentInstance == null || !_processService.CurrentInstance.IsRunning)
            {
                if (!_processService.Start(def, _currentLog ?? new ConsoleLog(LogCapacityChars), out var startErr))
                {
                    _currentLog?.AppendSystemMessage($"[ConsoleCommander] Failed to start {consoleName}: {startErr}");
                    return (string?)$"Error: Failed to start {consoleName}: {startErr}";
                }
            }

            string? output = null;
            if (_processService.TrySendInput(command, out var sentText, out var sendErr))
            {
                if (_currentLog != null)
                {
                    _currentLog.AppendInput(sentText);

                    // Wait for interactive output to settle
                    await Task.Delay(1000);

                    // Best-effort: return the latest tail of output.
                    output = _currentLog.GetTail(20000);
                }
            }
            else
            {
                _currentLog?.AppendSystemMessage($"[ConsoleCommander] Failed to send: {sendErr}");
                return (string?)$"Error: Failed to send: {sendErr}";
            }

            return output;
        }).Task.Unwrap();
    }

    private void ClearLog()
    {
        // Clear the underlying log as well as the UI display.
        try
        {
            _currentLog?.Clear();
        }
        catch
        {
            // ignore
        }

        ConsoleDisplayText = string.Empty;
        Interlocked.Exchange(ref _displayDirty, 0);
    }

    private void SetCurrentLog(ConsoleLog log)
    {
        if (_currentLog != null)
        {
            _currentLog.Updated -= OnLogUpdated;
        }

        _currentLog = log;
        _currentLog.Updated += OnLogUpdated;
        ConsoleDisplayText = log.GetTail(MaxDisplayChars);
        Interlocked.Exchange(ref _displayDirty, 0);
    }

    private void OnLogUpdated(object? sender, EventArgs e)
    {
        Interlocked.Exchange(ref _displayDirty, 1);
    }

    private void DisplayTimerOnTick(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _displayDirty, 0) == 0)
        {
            return;
        }

        if (_currentLog == null)
        {
            return;
        }

        ConsoleDisplayText = _currentLog.GetTail(MaxDisplayChars);
    }

    private async void OnProcessExited()
    {
        await _dispatcher.InvokeAsync(async () =>
        {
            IsProcessRunning = false;
            if (StatusText != "Error")
            {
                StatusText = "Exited";
            }

            if (_pendingExitToReenableSend > 0)
            {
                _pendingExitToReenableSend--;
                if (_pendingExitToReenableSend <= 0)
                {
                    _pendingExitToReenableSend = 0;
                    await ReenableSend();
                }
            }
            else
            {
                // If Send was disabled for any reason, process exit should make the UI usable again.
                if (!IsSendEnabled)
                {
                    await ReenableSend();
                }
            }
        });
    }



    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
