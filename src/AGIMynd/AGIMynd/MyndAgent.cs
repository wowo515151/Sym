// Copyright Warren Harding 2026
using System.Runtime.CompilerServices;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using HAMM;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using Sym.CSharpIO;

[assembly: InternalsVisibleTo("AGIMynd.Tests")]

namespace AGIMynd
{
    public interface IToolRunner
    {
        Task<string> RunToolAsync(string toolName, string args);
    }

    public class MyndAgent : IDisposable
    {
        // Config filename to persist agent settings (Goal etc.) outside of memory root
        private readonly string _configDir;
        private readonly string _settingsFilePath;

        // The current goal the agent is operating under. Mutable at runtime (UI or file updates).
        public string Goal { get; set; } = "";
        private const string CreateConceptToolName = "CreateConcept";
        private const string DeleteConceptToolName = "DeleteConcept";
        private const string AgentIsFinishedToolName = "AgentIsFinished";

        private const string HammRememberToolName = "HammRemember";
        private const string HammQueryToolName = "HammQuery";
        private const string HammForgetToolName = "HammForget";
        private const string HammSetScopeToolName = "HammSetScope";
        private const string HammReasonToolName = "HammReason";
        private const string HammFoldToolName = "HammFold";
        private const string HammMaintenanceToolName = "HammMaintenance";
        private const string SearchVerifyToolName = "SearchVerify";
        private const string MultiSearchVerifyToolName = "MultiSearchVerify";
        private const string TraceSourceToolName = "TraceSource";
        private const string SearchGuardsToolName = "SearchGuards";
        private const string SecurityPolicyToolName = "SecurityPolicy";
        private const string VerifiedSecurityFindingToolName = "VerifiedSecurityFinding";
        private const string SafeObservationToolName = "SafeObservation";
        private const string CreateProcedureToolName = "CreateProcedure";
        private const string ProcedureIfToolName = "If";
        private const string ProcedureElseToolName = "Else";
        private const string ProcedureForToolName = "For";
        private const string ProcedureEndForToolName = "EndFor";
        private const string ProcedureBreakToolName = "Break";
        private const string ProcedureContinueToolName = "Continue";
        private const string ProcedureGotoToolName = "Goto";
        private const string ProcedureCallToolName = "Call";
        private const string ProcedureReturnToolName = "Return";
        private const string ProcedureParallelToolName = "Parallel";
        private const string ProcedureLabelToolName = "Label";
        private const int MaxProcedureTransitionsPerEpoch = 128;
        private const int MaxProcedureGuardEpochHits = 3;

        private readonly IMyndLLM _llm;
        private readonly IToolRunner? _toolRunner;
        private readonly string _memoryRoot;
        private readonly string _repoRoot;
        private readonly string _savedFromToolsRoot;
        private readonly string _pinnedRoot;
        private readonly string _fullMemoryRoot; // Cached for path safety
        private readonly string _fullPinnedRoot; // Cached for path safety
        private readonly string _fullSavedFromToolsRoot; // Cached for path safety
        private readonly string _fullRepoRoot; // Cached for path safety
        
        private readonly string _logRoot;
        private CancellationTokenSource? _loopCts;
        private readonly SemaphoreSlim _loopSemaphore = new SemaphoreSlim(1, 1);
        
        public readonly HeuristicAssociativeMemoryModel _hamm = new HeuristicAssociativeMemoryModel();

        // In-memory buffer for recent tool interactions (replacing file-based FromTools)
        // Stores (ToolName, Input, Output, Timestamp)
        private readonly List<(string Tool, string Input, string Output, DateTime Time)> _recentToolOutputs = new();
        private readonly object _toolHistoryLock = new object();
        private const int MaxToolHistory = 10;
        // Tracks consecutive per-epoch transition-guard hits for a specific procedure step.
        private readonly Dictionary<string, int> _procedureGuardHits = new(StringComparer.OrdinalIgnoreCase);

        public int MaxDeletionsPerEpoch { get; set; } = 5;
        public long MaxPromptSize { get; set; } = 250000;
        public int MaxRecallFactsPerEpoch { get; set; } = 20;
        public int MaxSubQueriesPerEpoch { get; set; } = 6;
        public int MinSubQueryTokenBudget { get; set; } = 96;
        // 0 means unlimited epochs. Positive value limits number of epochs the agent will run.
        public int MaxEpochs { get; set; } = 0;

        private readonly IFileSystem _fs;
        private readonly List<ToolDescription> _externalToolDescriptions = new();

        public MyndAgent(string memoryRoot, IMyndLLM? llm = null, string? pinnedSource = null, IFileSystem? fileSystem = null, IToolRunner? toolRunner = null, IEnumerable<ToolDescription>? externalTools = null, string? repoRoot = null)
        {
            _memoryRoot = memoryRoot;
            _fullMemoryRoot = GetCanonicalPath(memoryRoot);
            _repoRoot = repoRoot ?? MemoryConfig.FindRepoRoot() ?? AppContext.BaseDirectory;
            _fullRepoRoot = GetCanonicalPath(_repoRoot);
            _configDir = Path.Combine(memoryRoot, "Config");
            _toolRunner = toolRunner;
            if (externalTools != null) _externalToolDescriptions.AddRange(externalTools);

            _savedFromToolsRoot = Path.Combine(memoryRoot, "SavedFromTools");
            _fullSavedFromToolsRoot = GetCanonicalPath(_savedFromToolsRoot);

            _pinnedRoot = Path.Combine(memoryRoot, "Pinned");
            _fullPinnedRoot = GetCanonicalPath(_pinnedRoot);

            _logRoot = Path.Combine(memoryRoot, "EpochLog");
            _llm = llm ?? new SwitchLLMWrapper();
            _fs = fileSystem ?? new DefaultFileSystem();
            
            EnsureDirectories();
            try { _hamm.Store.Load(_memoryRoot); } catch (Exception ex) { LogErrorAsync($"Failed to load HAMM: {ex.Message}").Wait(); }

            // Settings persistence path
            _settingsFilePath = Path.Combine(_configDir, "MyndAgentSettings.json");
            try
            {
                if (!Directory.Exists(_configDir)) Directory.CreateDirectory(_configDir);
                LoadSettings();
            }
            catch { /* non-fatal */ }

            // Honor global config to delete AgentErrors.log once on startup to avoid replaying
            // long error logs into UI viewers. Write/delete diagnostics under the EpochLog
            // directory (not the memory root) so they are excluded from memory context.
            try { MemoryConfig.EnsureDeleteLogOnStartup(_logRoot); } catch { }

            if (!string.IsNullOrEmpty(pinnedSource) && Directory.Exists(pinnedSource))
            {
                // Run pinned sync in background to avoid blocking constructor
                _ = Task.Run(async () => await SyncPinnedFilesAsync(pinnedSource));
            }
        }

        public void Dispose()
        {
            _loopCts?.Dispose();
            _loopSemaphore.Dispose();
            _hamm.Dispose();
        }

        private async Task SyncPinnedFilesAsync(string sourceDir)
        {
            try
            {
                // Ensure the pinned root exists
                if (!Directory.Exists(_pinnedRoot)) Directory.CreateDirectory(_pinnedRoot);

                // Create all subdirectories from the source under the pinned root
                foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(sourceDir, dir);
                    var destDir = Path.Combine(_pinnedRoot, relative);
                    if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                }

                // Copy all files (any extension) from source to pinned root, preserving relative paths
                foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(sourceDir, file);
                    var destFile = Path.Combine(_pinnedRoot, relative);
                    var destDir = Path.GetDirectoryName(destFile);
                    if (destDir != null && !Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    File.Copy(file, destFile, true);
                }
            }
            catch (Exception ex)
            {
                await LogErrorAsync($"Failed to sync pinned files: {ex.Message}");
            }
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

        public async Task StartAsync(TimeSpan? delay = null, CancellationToken ct = default)
        {
            if (!await _loopSemaphore.WaitAsync(0))
            {
                throw new InvalidOperationException("Agent is already running.");
            }

            try
            {
                _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var interval = delay ?? TimeSpan.FromSeconds(5);

                // Use a for-loop so we can bound the number of epochs when requested.
                for (int epoch = 0; !_loopCts.Token.IsCancellationRequested && (MaxEpochs <= 0 || epoch < MaxEpochs); epoch++)
                {
                    try
                    {
                        // Use the in-memory Goal property; UI should call UpdateGoalAsync to change it at runtime.
                        var goalPrompt = string.IsNullOrEmpty(Goal) ? "No goal defined." : Goal;
                        await RunEpochAsync(goalPrompt, _loopCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        await LogErrorAsync($"Epoch Error: {ex.Message}");
                    }

                    try
                    {
                        await Task.Delay(interval, _loopCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            finally
            {
                _loopSemaphore.Release();
            }
        }

        private async Task LogErrorAsync(string message)
        {
            try
            {
                // Persist errors to the EpochLog folder (excluded from memory context)
                if (!Directory.Exists(_logRoot)) Directory.CreateDirectory(_logRoot);
                await File.AppendAllTextAsync(Path.Combine(_logRoot, "AgentErrors.log"), 
                    $"{DateTime.Now:yyyyMMdd_HHmmss}: {message}{Environment.NewLine}");
            }
            catch { /* Ignore logging errors to prevent infinite loops */ }
        }

        /// <summary>
        /// Callback the UI can set to receive deletion-related notifications.
        /// If set, messages will be posted to this delegate instead of being written to disk.
        /// </summary>
        public Func<string, Task>? DeletionNotificationAsync { get; set; }

        /// <summary>
        /// Optional high-volume trace stream for UI/debugging (LLM responses, tool commands, etc.).
        /// Keep messages reasonably sized to avoid UI flooding.
        /// </summary>
        public Func<string, Task>? TraceNotificationAsync { get; set; }

        private async Task NotifyTraceAsync(string message)
        {
            if (TraceNotificationAsync == null) return;
            try
            {
                await TraceNotificationAsync(message);
            }
            catch
            {
                // Swallow errors from UI callback to avoid destabilizing the agent.
            }
        }

        private async Task NotifyDeletionAsync(string message)
        {
            if (DeletionNotificationAsync != null)
            {
                try
                {
                    await DeletionNotificationAsync(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Deletion Callback Error] {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[DELETE] {message}");
            }
        }

        public void Stop()
        {
            _loopCts?.Cancel();
        }

        private void EnsureDirectories()
        {
            if (!Directory.Exists(_memoryRoot)) Directory.CreateDirectory(_memoryRoot);
            if (!Directory.Exists(_savedFromToolsRoot)) Directory.CreateDirectory(_savedFromToolsRoot);
            if (!Directory.Exists(_pinnedRoot)) Directory.CreateDirectory(_pinnedRoot);
            if (!Directory.Exists(_logRoot)) Directory.CreateDirectory(_logRoot);
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsFilePath)) return;
                var json = File.ReadAllText(_settingsFilePath);
                var doc = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (doc != null && doc.TryGetValue("Goal", out var g))
                {
                    Goal = g ?? string.Empty;
                }
            }
            catch { /* ignore load errors */ }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                var payload = new System.Collections.Generic.Dictionary<string, string?> { ["Goal"] = Goal };
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

                // Atomic write: write to temp and move
                var dir = Path.GetDirectoryName(_settingsFilePath) ?? _configDir;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var tmp = Path.Combine(dir, Path.GetRandomFileName() + ".tmp");
                await File.WriteAllTextAsync(tmp, json);
                if (File.Exists(_settingsFilePath)) File.Delete(_settingsFilePath);
                File.Move(tmp, _settingsFilePath);
            }
            catch { /* non-fatal */ }
        }

        /// <summary>
        /// Update the agent's goal at runtime and persist it to settings.
        /// This is safe to call from UI threads.
        /// </summary>
        public async Task UpdateGoalAsync(string newGoal)
        {
            Goal = newGoal ?? string.Empty;
            await SaveSettingsAsync();
        }

        public async Task RunEpochAsync(string goalPrompt, CancellationToken ct = default)
        {
            // Sync file system to HAMM to ensure external edits or initial state are captured
            await IngestMemoryFilesToHammAsync();

            // Check for Active Instruction Pointer (Von Neumann Fetch-Execute Cycle)
            if (await TryExecuteActiveProcedureAsync(goalPrompt))
            {
                return;
            }

            // AUTO-BOOTSTRAP for Security Audits: If no observations, run initial discovery.
            if (goalPrompt.Contains("security", StringComparison.OrdinalIgnoreCase) && 
                goalPrompt.Contains("audit", StringComparison.OrdinalIgnoreCase))
            {
                var observations = _hamm.Store.GetFacts("Observations").ToList();
                if (observations.Count == 0)
                {
                    await NotifyTraceAsync("[Agent] Bootstrapping high-precision security audit...");
                    await SecurityPolicyAsync();
                    // Initial discovery across entire repoRoot
                    await MultiSearchVerifyAsync($"{_repoRoot} ExecuteExternalCommand cmd.exe Assembly.Load BinaryFormatter Process.Start");
                }
            }

            // Build fixed/priority parts of the prompt first
            string headerPrompt = await BuildPrompt(goalPrompt);
            
            // Calculate remaining budget for memory context
            // Estimate overhead for wrapper tags and newlines (approx 500 chars)
            long used = headerPrompt.Length + 500;
            long available = MaxPromptSize - used;
            if (available < 0) available = 0;

            var memoryTokenBudget = Math.Max(0, (int)(available / 4));
            var pack = BuildHammContextPack(goalPrompt, memoryTokenBudget);
            var memoryContext = pack.Xml;
            bool memoryTrimmed = pack.Trimmed;

            await NotifyTraceAsync(
                $"[HAMM] ContextPack mode=Hierarchical scope={_hamm.CurrentScope} " +
                $"facts={pack.FactCount} usedTokens={pack.UsedTokens}/{pack.BudgetTokens} " +
                $"subQueries={pack.SubQueryCount} candidates={pack.CandidateCount}");

            string trimNotice = string.Empty;
            if (memoryTrimmed)
            {
                trimNotice = Common.WrapInTags("NOTE: Memories were truncated to fit the prompt size limit. Consider deleting memories that aren't valuable for this goal to improve relevant context.", "MemoryTrimNotice") + Environment.NewLine + Environment.NewLine;
            }

            string fullPrompt = headerPrompt + Environment.NewLine + Environment.NewLine + trimNotice
                + Common.WrapInTags("The agent can consult its mutable concept memory in the Memories section below. This section excludes pinned guidance and tool buffer folders shown above, and is provided as a MemoryFileList XML with MemoryFile entries that include FileName and Content.", "MemoriesIntro") + Environment.NewLine + Common.WrapInTags(memoryContext, "Memories");

            await NotifyTraceAsync($"[LLM] Querying model. PromptLength={fullPrompt.Length}");

            string llmResponse = await _llm.QueryAsync(fullPrompt, ct);
            
            // HAMM Integration: Record thought
            _hamm.Think(new Symbol(llmResponse));

            if (!string.IsNullOrWhiteSpace(llmResponse))
            {
                var preview = llmResponse.Length <= 8000 ? llmResponse : llmResponse.Substring(0, 8000) + "\n...<truncated>";
                await NotifyTraceAsync($"[LLM] RawResponse (preview):\n{preview}");
            }
            ToolCommandList response = await ParseResponseAsync(llmResponse);
            response ??= new ToolCommandList();

            if (response?.Commands != null && response.Commands.Count > 0)
            {
                var summary = string.Join(", ", response.Commands.Select(c => c?.ToolName ?? "(null)").Where(s => !string.IsNullOrWhiteSpace(s)));
                await NotifyTraceAsync($"[LLM] Parsed {response.Commands.Count} ToolCommand(s): {summary}");
            }

            await LogEpochAsync(goalPrompt, response);
            await ApplyChangesAsync(response, goalPrompt);
        }

        private sealed record ProcedureForState(MemoryFact Fact, int ForStep, int EndStep, int Remaining);
        private sealed record ProcedureCallFrame(MemoryFact Fact, int Depth, string CalleeProcedure, string ReturnProcedure, int ReturnStep);

        // Keep this list in sync with the CreateProcedure tool requirements text.
        private static bool IsProcedureControlTool(string toolName)
        {
            return string.Equals(toolName, ProcedureIfToolName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, ProcedureElseToolName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, ProcedureForToolName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, ProcedureEndForToolName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, ProcedureBreakToolName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, ProcedureContinueToolName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, ProcedureGotoToolName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, ProcedureCallToolName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, ProcedureReturnToolName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, ProcedureParallelToolName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, ProcedureLabelToolName, StringComparison.OrdinalIgnoreCase);
        }

        private List<MemoryFact> GetActivePointers()
        {
            var ipQuery = new Function("ActivePointer", new Wild("?proc"), new Wild("?step"));
            var inGoals = _hamm.Store.QueryV2(ipQuery, new HAMM.QueryOptions { Scope = "Goals", MinQualityScore = 0.0 }).ToList();
            if (inGoals.Count > 0)
            {
                return inGoals;
            }

            // Backward compatibility with any pointers that may still be in Global.
            return _hamm.Store.QueryV2(ipQuery, new HAMM.QueryOptions { Scope = "Global", MinQualityScore = 0.0 }).ToList();
        }

        private static bool TryParseActivePointer(MemoryFact fact, out string procedureName, out int step)
        {
            procedureName = string.Empty;
            step = -1;

            if (fact.Expression is not Function ip || !string.Equals(ip.Name, "ActivePointer", StringComparison.OrdinalIgnoreCase) || ip.Arguments.Count != 2)
            {
                return false;
            }

            if (ip.Arguments[0] is not Symbol proc || ip.Arguments[1] is not Number num)
            {
                return false;
            }

            procedureName = proc.Name;
            step = (int)num.Value;
            return !string.IsNullOrWhiteSpace(procedureName) && step >= 0;
        }

        private MemoryFact? FindActivePointer(string procedureName, int step)
        {
            return GetActivePointers()
                .Where(f => TryParseActivePointer(f, out var p, out var s) && string.Equals(p, procedureName, StringComparison.OrdinalIgnoreCase) && s == step)
                .OrderByDescending(f => f.Certainty)
                .FirstOrDefault();
        }

        private MemoryFact MoveActivePointer(MemoryFact currentPointerFact, string procedureName, int step)
        {
            _hamm.Store.UpdateState(currentPointerFact, new Function("ActivePointer", new Symbol(procedureName), new Number(step)));
            return FindActivePointer(procedureName, step) ?? currentPointerFact;
        }

        private static string GetExpressionText(IExpression expr)
        {
            if (expr is Symbol s) return s.Name;
            if (expr is Number n) return n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return expr.ToDisplayString();
        }

        private static ToolCommand? BuildProcedureToolCommand(Function cmdExpr)
        {
            if (!string.Equals(cmdExpr.Name, "Cmd", StringComparison.OrdinalIgnoreCase) || cmdExpr.Arguments.Count != 4)
            {
                return null;
            }

            return new ToolCommand
            {
                ToolName = GetExpressionText(cmdExpr.Arguments[0]),
                ToolInput = GetExpressionText(cmdExpr.Arguments[1]),
                Path = GetExpressionText(cmdExpr.Arguments[2]),
                Tags = GetExpressionText(cmdExpr.Arguments[3])
            };
        }

        private bool TryGetProcedureCommands(string procedureName, out List<IExpression> commands)
        {
            commands = new List<IExpression>();
            var procQuery = new Function("Procedure", new Symbol(procedureName), new Wild("?cmds"));
            var procFacts = _hamm.Store.QueryV2(procQuery, new HAMM.QueryOptions { Scope = "Procedures", MinQualityScore = 0.0 }).ToList();
            if (procFacts.Count == 0)
            {
                return false;
            }

            var procFact = procFacts.First();
            if (procFact.Expression is not Function procOp || procOp.Arguments.Count != 2)
            {
                return false;
            }

            if (procOp.Arguments[1] is not Function listOp || !string.Equals(listOp.Name, "List", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            commands = listOp.Arguments.ToList();
            return true;
        }

        private bool TryResolveProcedureTarget(IReadOnlyList<IExpression> commands, string? targetText, out int targetStep)
        {
            targetStep = -1;
            if (string.IsNullOrWhiteSpace(targetText))
            {
                return false;
            }

            var trimmed = targetText.Trim();
            if (int.TryParse(trimmed, out var parsedIndex))
            {
                if (parsedIndex >= 0 && parsedIndex < commands.Count)
                {
                    targetStep = parsedIndex;
                    return true;
                }
                return false;
            }

            for (int i = 0; i < commands.Count; i++)
            {
                if (commands[i] is not Function cmdExpr) continue;
                var cmd = BuildProcedureToolCommand(cmdExpr);
                if (cmd == null) continue;
                if (!string.Equals(cmd.ToolName, ProcedureLabelToolName, StringComparison.OrdinalIgnoreCase)) continue;

                if (string.Equals(cmd.ToolInput, trimmed, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(cmd.Path, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    targetStep = i;
                    return true;
                }
            }

            return false;
        }

        private static int FindMatchingEndFor(IReadOnlyList<IExpression> commands, int forStep)
        {
            int depth = 0;
            for (int i = forStep + 1; i < commands.Count; i++)
            {
                if (commands[i] is not Function cmdExpr) continue;
                var cmd = BuildProcedureToolCommand(cmdExpr);
                if (cmd == null) continue;

                if (string.Equals(cmd.ToolName, ProcedureForToolName, StringComparison.OrdinalIgnoreCase))
                {
                    depth++;
                }
                else if (string.Equals(cmd.ToolName, ProcedureEndForToolName, StringComparison.OrdinalIgnoreCase))
                {
                    if (depth == 0) return i;
                    depth--;
                }
            }

            return -1;
        }

        private List<ProcedureForState> GetProcedureForStates(string procedureName)
        {
            var states = new List<ProcedureForState>();
            foreach (var fact in _hamm.Store.GetFacts("Procedures"))
            {
                if (fact.Expression is not Function fn || !string.Equals(fn.Name, "ForState", StringComparison.OrdinalIgnoreCase) || fn.Arguments.Count != 4)
                {
                    continue;
                }

                if (fn.Arguments[0] is not Symbol proc || !string.Equals(proc.Name, procedureName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (fn.Arguments[1] is not Number forStep || fn.Arguments[2] is not Number endStep || fn.Arguments[3] is not Number remaining)
                {
                    continue;
                }

                states.Add(new ProcedureForState(fact, (int)forStep.Value, (int)endStep.Value, (int)remaining.Value));
            }

            return states;
        }

        private ProcedureForState? GetForState(string procedureName, int forStep)
        {
            return GetProcedureForStates(procedureName).FirstOrDefault(s => s.ForStep == forStep);
        }

        private ProcedureForState? GetInnermostLoopState(string procedureName, int currentStep)
        {
            return GetProcedureForStates(procedureName)
                .Where(s => currentStep > s.ForStep && currentStep <= s.EndStep)
                .OrderByDescending(s => s.ForStep)
                .FirstOrDefault();
        }

        private ProcedureForState? GetLoopStateByEndStep(string procedureName, int endStep)
        {
            return GetProcedureForStates(procedureName)
                .Where(s => s.EndStep == endStep)
                .OrderByDescending(s => s.ForStep)
                .FirstOrDefault();
        }

        private ProcedureForState SaveForState(string procedureName, int forStep, int endStep, int remaining)
        {
            var existing = GetForState(procedureName, forStep);
            var expr = new Function("ForState", new Symbol(procedureName), new Number(forStep), new Number(endStep), new Number(remaining));

            if (existing != null)
            {
                _hamm.Store.UpdateState(existing.Fact, expr);
            }
            else
            {
                _hamm.Remember(expr, scope: "Procedures");
            }

            return GetForState(procedureName, forStep)!;
        }

        private void RemoveForState(ProcedureForState? state)
        {
            if (state == null) return;
            _hamm.Store.InvalidateFact(state.Fact, 1.0);
        }

        private List<ProcedureCallFrame> GetCallFrames()
        {
            var frames = new List<ProcedureCallFrame>();
            foreach (var fact in _hamm.Store.GetFacts("Procedures"))
            {
                if (fact.Expression is not Function fn || !string.Equals(fn.Name, "CallFrame", StringComparison.OrdinalIgnoreCase) || fn.Arguments.Count != 4)
                {
                    continue;
                }

                if (fn.Arguments[0] is not Number depth || fn.Arguments[1] is not Symbol callee || fn.Arguments[2] is not Symbol returnProc || fn.Arguments[3] is not Number returnStep)
                {
                    continue;
                }

                frames.Add(new ProcedureCallFrame(fact, (int)depth.Value, callee.Name, returnProc.Name, (int)returnStep.Value));
            }
            return frames;
        }

        private void PushCallFrame(string calleeProcedure, string returnProcedure, int returnStep)
        {
            var depth = GetCallFrames().Select(f => f.Depth).DefaultIfEmpty(0).Max() + 1;
            var frame = new Function("CallFrame", new Number(depth), new Symbol(calleeProcedure), new Symbol(returnProcedure), new Number(returnStep));
            _hamm.Remember(frame, scope: "Procedures");
        }

        private bool TryPopCallFrame(string calleeProcedure, out ProcedureCallFrame frame)
        {
            frame = null!;
            var top = GetCallFrames()
                .Where(f => string.Equals(f.CalleeProcedure, calleeProcedure, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.Depth)
                .FirstOrDefault();

            if (top == null) return false;
            _hamm.Store.InvalidateFact(top.Fact, 1.0);
            frame = top;
            return true;
        }

        private bool EvaluateProcedureCondition(string conditionText)
        {
            if (string.IsNullOrWhiteSpace(conditionText))
            {
                return false;
            }

            var text = conditionText.Trim();
            bool negate = false;
            if (text.StartsWith("!", StringComparison.Ordinal))
            {
                negate = true;
                text = text.Substring(1).Trim();
            }

            if (bool.TryParse(text, out var boolValue))
            {
                return negate ? !boolValue : boolValue;
            }

            if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase))
            {
                return !negate;
            }
            if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase))
            {
                return negate;
            }

            if (text.StartsWith("Exists(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(")", StringComparison.Ordinal))
            {
                text = text.Substring(7, text.Length - 8).Trim();
            }

            try
            {
                var expressions = CSharpIO.ParseExpressions(text);
                if (expressions.Count == 0)
                {
                    return false;
                }

                var expr = expressions[0];
                // Evaluate Exists(...) in the active scope chain first, then stable shared scopes.
                var activeScope = string.IsNullOrWhiteSpace(_hamm.CurrentScope) ? "Global" : _hamm.CurrentScope;
                bool exists = _hamm.Store.QueryV2(expr, new HAMM.QueryOptions { Scope = activeScope, MinQualityScore = 0.0 }).Any()
                    || _hamm.Store.QueryV2(expr, new HAMM.QueryOptions { Scope = "Global", MinQualityScore = 0.0 }).Any()
                    || _hamm.Store.QueryV2(expr, new HAMM.QueryOptions { Scope = "Goals", MinQualityScore = 0.0 }).Any()
                    || _hamm.Store.QueryV2(expr, new HAMM.QueryOptions { Scope = "Procedures", MinQualityScore = 0.0 }).Any();

                return negate ? !exists : exists;
            }
            catch
            {
                return false;
            }
        }

        private async Task CompleteProcedurePointerAsync(MemoryFact pointerFact, string completedProcedure)
        {
            ClearProcedureGuardHits(completedProcedure);

            if (TryPopCallFrame(completedProcedure, out var frame))
            {
                MoveActivePointer(pointerFact, frame.ReturnProcedure, frame.ReturnStep);
                await NotifyTraceAsync($"[Agent] Returned from '{completedProcedure}' to '{frame.ReturnProcedure}' step {frame.ReturnStep}.");
                return;
            }

            _hamm.Store.InvalidateFact(pointerFact, 1.0);
            await NotifyTraceAsync($"[Agent] Procedure '{completedProcedure}' completed.");
        }

        private static string GetProcedureGuardKey(string procedureName, int step)
        {
            return $"{procedureName}|{step}";
        }

        private void ClearProcedureGuardHits(string procedureName)
        {
            var prefix = procedureName + "|";
            var keys = _procedureGuardHits.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var key in keys)
            {
                _procedureGuardHits.Remove(key);
            }
        }

        private async Task<bool> TryExecuteActiveProcedureAsync(string goalPrompt)
        {
            var activePointerFact = GetActivePointers().OrderByDescending(f => f.Certainty).FirstOrDefault();
            if (activePointerFact == null)
            {
                return false;
            }

            int transitions = 0;
            while (activePointerFact != null && transitions < MaxProcedureTransitionsPerEpoch)
            {
                transitions++;

                if (!TryParseActivePointer(activePointerFact, out var procedureName, out var step))
                {
                    _hamm.Store.InvalidateFact(activePointerFact, 1.0);
                    return true;
                }

                if (!TryGetProcedureCommands(procedureName, out var commands))
                {
                    _hamm.Store.InvalidateFact(activePointerFact, 1.0);
                    await NotifyTraceAsync($"[Agent] ActivePointer refers to unknown Procedure '{procedureName}'. Removing pointer.");
                    return true;
                }

                if (step < 0 || step >= commands.Count)
                {
                    await CompleteProcedurePointerAsync(activePointerFact, procedureName);
                    return true;
                }

                if (commands[step] is not Function cmdExpr)
                {
                    activePointerFact = MoveActivePointer(activePointerFact, procedureName, step + 1);
                    continue;
                }

                var stepCommand = BuildProcedureToolCommand(cmdExpr);
                if (stepCommand == null || string.IsNullOrWhiteSpace(stepCommand.ToolName))
                {
                    activePointerFact = MoveActivePointer(activePointerFact, procedureName, step + 1);
                    continue;
                }

                if (IsProcedureControlTool(stepCommand.ToolName))
                {
                    if (string.Equals(stepCommand.ToolName, ProcedureLabelToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        activePointerFact = MoveActivePointer(activePointerFact, procedureName, step + 1);
                        continue;
                    }

                    if (string.Equals(stepCommand.ToolName, ProcedureGotoToolName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(stepCommand.ToolName, ProcedureElseToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        var gotoTarget = string.IsNullOrWhiteSpace(stepCommand.Path) ? stepCommand.ToolInput : stepCommand.Path;
                        if (TryResolveProcedureTarget(commands, gotoTarget, out var gotoStep))
                        {
                            activePointerFact = MoveActivePointer(activePointerFact, procedureName, gotoStep);
                        }
                        else
                        {
                            await NotifyTraceAsync($"[Agent] {stepCommand.ToolName} target '{gotoTarget}' is invalid in Procedure '{procedureName}'.");
                            activePointerFact = MoveActivePointer(activePointerFact, procedureName, step + 1);
                        }
                        continue;
                    }

                    if (string.Equals(stepCommand.ToolName, ProcedureIfToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        var condition = EvaluateProcedureCondition(stepCommand.ToolInput);
                        string? targetText = condition ? stepCommand.Path : stepCommand.Tags;
                        if (TryResolveProcedureTarget(commands, targetText, out var targetStep))
                        {
                            activePointerFact = MoveActivePointer(activePointerFact, procedureName, targetStep);
                        }
                        else
                        {
                            activePointerFact = MoveActivePointer(activePointerFact, procedureName, step + 1);
                        }
                        continue;
                    }

                    if (string.Equals(stepCommand.ToolName, ProcedureForToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        var endForStep = FindMatchingEndFor(commands, step);
                        if (endForStep < 0)
                        {
                            await NotifyTraceAsync($"[Agent] For at step {step} has no matching EndFor in Procedure '{procedureName}'.");
                            activePointerFact = MoveActivePointer(activePointerFact, procedureName, step + 1);
                            continue;
                        }

                        var state = GetForState(procedureName, step);
                        if (state == null)
                        {
                            int totalIterations = 0;
                            int.TryParse((stepCommand.ToolInput ?? string.Empty).Trim(), out totalIterations);
                            if (totalIterations < 0) totalIterations = 0;
                            state = SaveForState(procedureName, step, endForStep, totalIterations);
                        }

                        if (state.Remaining <= 0)
                        {
                            RemoveForState(state);
                            activePointerFact = MoveActivePointer(activePointerFact, procedureName, endForStep + 1);
                        }
                        else
                        {
                            activePointerFact = MoveActivePointer(activePointerFact, procedureName, step + 1);
                        }
                        continue;
                    }

                    if (string.Equals(stepCommand.ToolName, ProcedureEndForToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        var state = GetLoopStateByEndStep(procedureName, step);
                        if (state == null)
                        {
                            activePointerFact = MoveActivePointer(activePointerFact, procedureName, step + 1);
                            continue;
                        }

                        int nextRemaining = state.Remaining - 1;
                        if (nextRemaining > 0)
                        {
                            SaveForState(procedureName, state.ForStep, state.EndStep, nextRemaining);
                            activePointerFact = MoveActivePointer(activePointerFact, procedureName, state.ForStep + 1);
                        }
                        else
                        {
                            RemoveForState(state);
                            activePointerFact = MoveActivePointer(activePointerFact, procedureName, step + 1);
                        }
                        continue;
                    }

                    if (string.Equals(stepCommand.ToolName, ProcedureBreakToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        var state = GetInnermostLoopState(procedureName, step);
                        if (state == null)
                        {
                            activePointerFact = MoveActivePointer(activePointerFact, procedureName, step + 1);
                        }
                        else
                        {
                            RemoveForState(state);
                            activePointerFact = MoveActivePointer(activePointerFact, procedureName, state.EndStep + 1);
                        }
                        continue;
                    }

                    if (string.Equals(stepCommand.ToolName, ProcedureContinueToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        var state = GetInnermostLoopState(procedureName, step);
                        if (state == null)
                        {
                            activePointerFact = MoveActivePointer(activePointerFact, procedureName, step + 1);
                        }
                        else
                        {
                            activePointerFact = MoveActivePointer(activePointerFact, procedureName, state.EndStep);
                        }
                        continue;
                    }

                    if (string.Equals(stepCommand.ToolName, ProcedureCallToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        var calledProcedure = string.IsNullOrWhiteSpace(stepCommand.Path) ? stepCommand.ToolInput : stepCommand.Path;
                        if (string.IsNullOrWhiteSpace(calledProcedure) || !TryGetProcedureCommands(calledProcedure, out _))
                        {
                            await NotifyTraceAsync($"[Agent] Call target '{calledProcedure}' not found from Procedure '{procedureName}'.");
                            activePointerFact = MoveActivePointer(activePointerFact, procedureName, step + 1);
                            continue;
                        }

                        PushCallFrame(calledProcedure, procedureName, step + 1);
                        activePointerFact = MoveActivePointer(activePointerFact, calledProcedure, 0);
                        continue;
                    }

                    if (string.Equals(stepCommand.ToolName, ProcedureReturnToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryPopCallFrame(procedureName, out var frame))
                        {
                            activePointerFact = MoveActivePointer(activePointerFact, frame.ReturnProcedure, frame.ReturnStep);
                            continue;
                        }

                        _hamm.Store.InvalidateFact(activePointerFact, 1.0);
                        await NotifyTraceAsync($"[Agent] Return at Procedure '{procedureName}' had no call frame; pointer removed.");
                        return true;
                    }

                    if (string.Equals(stepCommand.ToolName, ProcedureParallelToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        var parsed = await ParseResponseAsync(stepCommand.ToolInput ?? string.Empty);
                        var directCommands = (parsed.Commands ?? new List<ToolCommand>())
                            .Where(c => c != null && !string.IsNullOrWhiteSpace(c.ToolName) && !IsProcedureControlTool(c.ToolName))
                            .ToList();

                        if (directCommands.Count > 0)
                        {
                            await NotifyTraceAsync($"[Agent] Executing Parallel batch ({directCommands.Count} tools) in Procedure '{procedureName}'.");
                            var tasks = directCommands.Select(DispatchToolCommandAsync).ToArray();
                            await Task.WhenAll(tasks);
                        }

                        int nextStep = step + 1;
                        if (nextStep < commands.Count)
                        {
                            MoveActivePointer(activePointerFact, procedureName, nextStep);
                        }
                        else
                        {
                            await CompleteProcedurePointerAsync(activePointerFact, procedureName);
                        }

                        return true;
                    }

                    activePointerFact = MoveActivePointer(activePointerFact, procedureName, step + 1);
                    continue;
                }

                await NotifyTraceAsync($"[Agent] Executing Procedure '{procedureName}' step {step}: {stepCommand.ToolName}");
                ClearProcedureGuardHits(procedureName);

                int defaultNextStep = step + 1;
                if (defaultNextStep < commands.Count)
                {
                    MoveActivePointer(activePointerFact, procedureName, defaultNextStep);
                }
                else
                {
                    await CompleteProcedurePointerAsync(activePointerFact, procedureName);
                }

                var responseObj = new ToolCommandList { Commands = new List<ToolCommand> { stepCommand } };
                await ApplyChangesAsync(responseObj, goalPrompt);
                return true;
            }

            if (transitions >= MaxProcedureTransitionsPerEpoch)
            {
                if (activePointerFact != null && TryParseActivePointer(activePointerFact, out var procedureName, out var step))
                {
                    var guardKey = GetProcedureGuardKey(procedureName, step);
                    _procedureGuardHits.TryGetValue(guardKey, out var hits);
                    hits++;
                    _procedureGuardHits[guardKey] = hits;

                    if (hits >= MaxProcedureGuardEpochHits)
                    {
                        _hamm.Store.InvalidateFact(activePointerFact, 1.0);
                        _procedureGuardHits.Remove(guardKey);
                        await NotifyTraceAsync($"[Agent] Procedure pointer '{procedureName}' step {step} invalidated after {hits} transition-guard hits.");
                        return true;
                    }

                    await NotifyTraceAsync($"[Agent] Procedure transition guard hit ({MaxProcedureTransitionsPerEpoch}) for '{procedureName}' step {step}; hit {hits}/{MaxProcedureGuardEpochHits}.");
                    return true;
                }

                await NotifyTraceAsync($"[Agent] Procedure transition guard hit ({MaxProcedureTransitionsPerEpoch}).");
                return true;
            }

            return false;
        }

        private async Task IngestMemoryFilesToHammAsync()
        {
            try
            {
                if (!_fs.DirectoryExists(_memoryRoot)) return;

                var untaggedFacts = new List<(MemoryFact Fact, string Content)>();

                // Sort by LastWriteTime to ensure most recent files have higher initial potency
                var files = _fs.GetFiles(_memoryRoot, "*.*", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.LastWriteTime) // Oldest first, so newest are added last (highest potency/freshness)
                    .Select(f => f.FullName);

                foreach (var file in files)
                {
                    string relativePath = Path.GetRelativePath(_memoryRoot, file);
                    if (!ShouldIncludeInMemoryContext(relativePath)) continue;

                    try
                    {
                        var content = await File.ReadAllTextAsync(file);
                        var contentSymbol = MemoryContentEncoding.EncodeContentSymbol(content, _hamm.Store.MaxInlineSymbolChars);
                        
                        // Check if we already have this file in HAMM
                        var existing = _hamm.Store.GetFacts().FirstOrDefault(f => 
                            f.Expression is Equality eq && 
                            eq.LeftOperand is Symbol path && path.Name == relativePath);

                        if (existing != null)
                        {
                            var existingEq = (Equality)existing.Expression;
                            var existingContent = (Symbol)existingEq.RightOperand;
                            
                            if (existingContent.Name == contentSymbol.Name)
                            {
                                // Content unchanged, skip
                                continue;
                            }
                            
                            // Content changed, update state
                            _hamm.Store.UpdateState(existing, new Equality(new Symbol(relativePath), contentSymbol));
                            
                            // If it's a new version, we might want to re-tag it if it's large?
                            // For now, let's just mark it for tagging if it has no tags
                            if (existing.NextVersion != null && existing.NextVersion.Tags.Count == 0)
                            {
                                untaggedFacts.Add((existing.NextVersion, content));
                            }
                        }
                        else
                        {
                            // New file, add fresh fact
                            var fact = _hamm.Store.AddFact(new Equality(new Symbol(relativePath), contentSymbol), certainty: 1.0);
                            
                            if (fact.Tags.Count == 0)
                            {
                                untaggedFacts.Add((fact, content));
                            }
                        }
                    }
                    catch { }
                }

                if (untaggedFacts.Count > 0)
                {
                    // await GenerateTagsBatchedAsync(untaggedFacts);
                    await NotifyTraceAsync($"[HAMM] Skipping tagging for {untaggedFacts.Count} files to ensure stability.");
                }
            }
            catch { }
        }

        private async Task GenerateTagsBatchedAsync(List<(MemoryFact Fact, string Content)> untagged)
        {
            try
            {
                // Batch size limit to prevent prompt overflow
                int batchSize = 5;
                for (int i = 0; i < untagged.Count; i += batchSize)
                {
                    var currentBatch = untagged.Skip(i).Take(batchSize).ToList();
                    
                    var request = new StringBuilder();
                    request.AppendLine("Generate 5-10 concise semantic tags (keywords) for each of the following files. These tags will be used for long-term associative recall. Respond with a simple comma-separated list per file, following the format: [FileName]: tag1, tag2, ...");
                    request.AppendLine();

                    foreach (var item in currentBatch)
                    {
                        var path = (item.Fact.Expression as Equality)?.LeftOperand as Symbol;
                        var preview = item.Content.Length > 1000 ? item.Content.Substring(0, 1000) + "..." : item.Content;
                        request.AppendLine($"--- FILE: {path?.Name ?? "Unknown"} ---");
                        request.AppendLine(preview);
                        request.AppendLine();
                    }

                    await NotifyTraceAsync($"[HAMM] Requesting tags for {currentBatch.Count} file(s)...");
                    string response = await _llm.QueryAsync(request.ToString());
                    
                    // Simple parse: look for lines starting with file names
                    var lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var item in currentBatch)
                    {
                        var pathSym = (item.Fact.Expression as Equality)?.LeftOperand as Symbol;
                        var path = pathSym?.Name;
                        if (path == null) continue;

                        var line = lines.FirstOrDefault(l => l.Contains(path, StringComparison.OrdinalIgnoreCase));
                        if (line != null)
                        {
                            var parts = line.Split(':');
                            if (parts.Length > 1)
                            {
                                var tags = parts[1].Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var t in tags)
                                {
                                    var clean = t.Trim().TrimEnd('.', ',');
                                    if (!string.IsNullOrWhiteSpace(clean) && !item.Fact.Tags.Contains(clean))
                                    {
                                        item.Fact.Tags.Add(clean);
                                    }
                                }
                            }
                        }
                    }
                }
                
                // Re-index all at once after tagging
                _hamm.Store.RebuildEGraph();
                await NotifyTraceAsync("[HAMM] Batched tagging complete.");
            }
            catch (Exception ex)
            {
                await LogErrorAsync($"GenerateTagsBatched Error: {ex.Message}");
            }
        }

        private async Task LogEpochAsync(string goal, ToolCommandList? response)
        {
            string logFile = Path.Combine(_logRoot, $"epoch_{DateTime.Now:yyyyMMdd_HHmmss}_{DateTime.Now.Ticks.ToString()}.xml");

            var logEntry = new LogEpochEntry
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
                Goal = goal,
                Response = response ?? new ToolCommandList()
            };

            var xml = Common.ToXml(logEntry);
            await File.WriteAllTextAsync(logFile, xml);
        }

        // Helper type for XML epoch logging
        public class LogEpochEntry
        {
            public string Timestamp { get; set; } = string.Empty;
            public string Goal { get; set; } = string.Empty;
            public ToolCommandList Response { get; set; } = new ToolCommandList();
        }

        private sealed class HammContextPack
        {
            public string Xml { get; set; } = string.Empty;
            public bool Trimmed { get; set; }
            public int BudgetTokens { get; set; }
            public int UsedTokens { get; set; }
            public int FactCount { get; set; }
            public int CandidateCount { get; set; }
            public int SubQueryCount { get; set; }
        }

        private Task<(string Xml, bool Trimmed)> BuildMemoryContextAsync(long maxBytes)
        {
            var pack = BuildHammContextPack(Goal, Math.Max(0, (int)(maxBytes / 4)));
            return Task.FromResult((pack.Xml, pack.Trimmed));
        }

        private HammContextPack BuildHammContextPack(string goalPrompt, int budgetTokens)
        {
            var result = new HammContextPack { BudgetTokens = Math.Max(0, budgetTokens) };
            var memoryList = new MemoryFileList();

            if (result.BudgetTokens <= 0)
            {
                result.Xml = Common.ToXml(memoryList);
                return result;
            }

            var selected = new Dictionary<string, MemoryFact>(StringComparer.Ordinal);
            int usedTokens = 0;
            bool trimmed = false;
            int candidateCount = 0;

            static string GetDedupKey(MemoryFact fact)
            {
                if (!string.IsNullOrWhiteSpace(fact.SemanticKey)) return fact.SemanticKey;
                if (!string.IsNullOrWhiteSpace(fact.ContentHash)) return fact.ContentHash;
                return fact.Id;
            }

            void TryTakeFacts(IEnumerable<MemoryFact> facts)
            {
                foreach (var fact in facts)
                {
                    if (selected.Count >= MaxRecallFactsPerEpoch)
                    {
                        trimmed = true;
                        break;
                    }

                    var key = GetDedupKey(fact);
                    if (selected.ContainsKey(key)) continue;

                    int factTokens = Math.Max(1, fact.Tokens);
                    if (usedTokens + factTokens > result.BudgetTokens)
                    {
                        trimmed = true;
                        continue;
                    }

                    selected[key] = fact;
                    usedTokens += factTokens;

                    if (usedTokens >= result.BudgetTokens)
                    {
                        trimmed = true;
                        break;
                    }
                }
            }

            var safeGoal = string.IsNullOrWhiteSpace(goalPrompt) ? "Goal" : goalPrompt;
            var rootQuery = new Symbol(safeGoal);
            int rootBudget = (int)Math.Floor(result.BudgetTokens * 0.55);
            if (rootBudget <= 0) rootBudget = result.BudgetTokens;

            var rootFacts = _hamm
                .RecallFacts(rootQuery, scope: _hamm.CurrentScope, tokenLimit: rootBudget, diversityWeight: 0.35, profile: RetrievalProfile.DefaultTask)
                .ToList();
            candidateCount += rootFacts.Count;
            TryTakeFacts(rootFacts);

            int remaining = result.BudgetTokens - usedTokens;
            if (remaining > 0)
            {
                int goalsBudget = Math.Min(128, remaining);
                var goalsFacts = _hamm
                    .RecallFacts(rootQuery, scope: "Goals", tokenLimit: goalsBudget, diversityWeight: 0.2, profile: RetrievalProfile.SafetyCritical)
                    .ToList();
                candidateCount += goalsFacts.Count;
                TryTakeFacts(goalsFacts);
            }

            var subQueries = BuildGoalSubQueries(safeGoal).Take(MaxSubQueriesPerEpoch).ToList();
            result.SubQueryCount = subQueries.Count;

            if (subQueries.Count > 0)
            {
                remaining = result.BudgetTokens - usedTokens;
                int totalSubBudget = Math.Min((int)Math.Floor(result.BudgetTokens * 0.40), remaining);
                int perSubBudget = subQueries.Count > 0 ? Math.Max(MinSubQueryTokenBudget, totalSubBudget / subQueries.Count) : 0;

                foreach (var subQuery in subQueries)
                {
                    remaining = result.BudgetTokens - usedTokens;
                    if (remaining <= 0)
                    {
                        trimmed = true;
                        break;
                    }

                    int subBudget = Math.Min(perSubBudget, remaining);
                    if (subBudget <= 0) continue;

                    var facts = _hamm
                        .RecallFacts(new Symbol(subQuery), scope: _hamm.CurrentScope, tokenLimit: subBudget, diversityWeight: 0.6, profile: RetrievalProfile.DefaultTask)
                        .ToList();
                    candidateCount += facts.Count;
                    TryTakeFacts(facts);
                }
            }

            if (selected.Count == 0)
            {
                var fallbackFacts = _hamm.Store.GetFacts(_hamm.CurrentScope)
                    .Where(f => !f.IsInvalidated && !f.IsSuperseded)
                    .Where(f => f.Kind != FactKind.Noise && f.Kind != FactKind.ToolTrace && f.ContentType != MemoryContentType.Artifact)
                    .OrderByDescending(f => (f.Potency * f.Certainty) / Math.Max(1, f.Tokens))
                    .ThenBy(f => f.Id, StringComparer.Ordinal)
                    .Take(MaxRecallFactsPerEpoch)
                    .ToList();

                candidateCount += fallbackFacts.Count;
                TryTakeFacts(fallbackFacts);
            }

            var orderedFacts = selected.Values
                .OrderByDescending(f => (f.Potency * f.Certainty) / Math.Max(1, f.Tokens))
                .ThenBy(f => f.Id, StringComparer.Ordinal)
                .ToList();

            foreach (var fact in orderedFacts)
            {
                memoryList.Items.Add(ToMemoryFile(fact));
            }

            result.FactCount = orderedFacts.Count;
            result.UsedTokens = usedTokens;
            result.CandidateCount = candidateCount;
            result.Trimmed = trimmed;
            result.Xml = Common.ToXml(memoryList);
            return result;
        }

        private static MemoryFile ToMemoryFile(MemoryFact fact)
        {
            var summary = $"HAMM scope={fact.Scope}; tokens={fact.Tokens}; certainty={fact.Certainty:F2}; potency={fact.Potency:F2}";
            if (fact.Expression is Equality eq && eq.LeftOperand is Symbol path && eq.RightOperand is Symbol content)
            {
                return new MemoryFile
                {
                    FileName = path.Name,
                    Content = content.Name,
                    Summary = summary
                };
            }

            return new MemoryFile
            {
                FileName = $"HAMM/{fact.Scope}/{fact.Id}.fact",
                Content = string.IsNullOrWhiteSpace(fact.CanonicalText) ? fact.Expression.ToDisplayString() : fact.CanonicalText,
                Summary = summary
            };
        }

        private IEnumerable<string> BuildGoalSubQueries(string goalPrompt)
        {
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queries = new List<string>();

            if (string.IsNullOrWhiteSpace(goalPrompt)) return queries;

            var normalized = Regex.Replace(goalPrompt, @"\s+", " ").Trim();
            if (normalized.Length == 0) return queries;

            var split = Regex.Split(
                normalized,
                @"[.!?;\n]+|\band then\b|\bthen\b|\bafter that\b|\bwhile\b|\bbut\b|\band\b",
                RegexOptions.IgnoreCase);

            foreach (var raw in split)
            {
                var sub = raw.Trim();
                if (sub.Length < 12) continue;
                if (!unique.Add(sub)) continue;
                queries.Add(sub);
                if (queries.Count >= MaxSubQueriesPerEpoch) break;
            }

            if (queries.Count == 0)
            {
                queries.Add(normalized);
            }

            return queries;
        }

        private bool ShouldIncludeInMemoryContext(string relativePath)
        {
            if (relativePath.StartsWith("EpochLog", StringComparison.OrdinalIgnoreCase)) return false;
            if (relativePath.Equals("AgentErrors.log", StringComparison.OrdinalIgnoreCase)) return false;
            if (relativePath.Equals("HAMM.index.json", StringComparison.OrdinalIgnoreCase)) return false;
            if (IsUnderFolder(relativePath, "Facts")) return false;
            if (IsUnderFolder(relativePath, "Pinned")) return false;
            if (IsUnderFolder(relativePath, "SavedFromTools")) return false;

            // Exclude large binary and data formats that don't belong in semantic memory
            var ext = Path.GetExtension(relativePath).ToLowerInvariant();
            var excludedExtensions = new[] { ".parquet", ".dll", ".exe", ".zip", ".pdb", ".bin" };
            if (excludedExtensions.Contains(ext)) return false;

            return true;
        }

        private static bool IsUnderFolder(string relativePath, string folderName)
        {
            if (relativePath.Equals(folderName, StringComparison.OrdinalIgnoreCase)) return true;
            var prefix = folderName + Path.DirectorySeparatorChar;
            var altPrefix = folderName + Path.AltDirectorySeparatorChar;
            return relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith(altPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string> BuildFileListXmlAsync(
            string rootDir,
            Func<string, bool>? includeFile = null,
            Func<string, Task>? logError = null,
            long? maxBytes = null,
            bool mostRecentOnly = false)
        {
            var list = new MemoryFileList();

            try
            {
                if (!Directory.Exists(rootDir))
                {
                    return Common.ToXml(list);
                }

                var files = Directory.GetFiles(rootDir, "*.*", SearchOption.AllDirectories);
                IEnumerable<string> fileList = files;

                if (mostRecentOnly)
                {
                    fileList = files
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.LastWriteTime)
                        .Select(f => f.FullName);
                }

                long currentSize = 0;

                foreach (var file in fileList)
                {
                    string relativePath;
                    try
                    {
                        relativePath = Path.GetRelativePath(rootDir, file);
                    }
                    catch
                    {
                        continue;
                    }

                    if (includeFile != null && !includeFile(relativePath))
                    {
                        continue;
                    }

                    try
                    {
                        var content = await File.ReadAllTextAsync(file);
                        
                        long itemSize = relativePath.Length + content.Length + 65; // Approx XML overhead

                        if (maxBytes.HasValue && (currentSize + itemSize) > maxBytes.Value)
                        {
                            if (mostRecentOnly) break; 
                            break; 
                        }

                        list.Items.Add(new MemoryFile { FileName = relativePath, Content = content });
                        currentSize += itemSize;
                    }
                    catch (Exception ex)
                    {
                        list.Items.Add(new MemoryFile
                        {
                            FileName = relativePath,
                            Content = $"[ERROR: Could not read {file}: {ex.Message}]"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                if (logError != null)
                {
                    await logError(ex.Message);
                }
            }

            return Common.ToXml(list);
        }

        private async Task<string> BuildPrompt(string goal)
        {
            // Introduction (not wrapped)
            var intro = $@"You are AGIMynd, an expert autonomous auditor.
The Goal is of higher priority than the Pinned memories. The Pinned memories are of higher priority than the normal Concept memories.
Goal: {goal}

";
            if (goal.Contains("security", StringComparison.OrdinalIgnoreCase))
            {
                intro += @"ROLE: You are currently acting as a SENIOR SECURITY RESEARCHER.
MANDATE: You MUST use 'MultiSearchVerify', 'TraceSource', and 'SearchGuards' for analysis.
FORBIDDEN: Do NOT use powershell or copilot for scanning. They are unreliable for high-precision audits.
METHODOLOGY: For every sink found, find the source and verify guards.
";
            }

            intro += @"If you output a ToolCommand with ToolName 'AgentIsFinished', the agent will stop its epoch loop and exit further processing.
IMPORTANT: Do not output AgentIsFinished unless the active InstructionPointer indicates the procedure is complete (typically instruction 0 / Done).

TIP: You can use '$output' as the ToolInput for a command to automatically insert the output of the immediately preceding command in the same batch. This is useful for piping 'gh' results into 'CreateConcept' or 'DriveCLI'.

MEMORY OPTIMIZATION: When using 'CreateConcept' for large files, always provide descriptive semantic keywords in the 'Tags' field (comma-separated). HAMM uses these tags for long-term recall when the raw content is too large to index directly.

Respond only with XML that conforms to the provided XML Schema. Use CDATA for any ToolInput values to avoid escaping problems.";

            // Schema section (XML Schema generated from ToolCommandList type)
            var schemaSection = "The following XML Schema (XSD) describes the ToolCommandList format I expect you to emit." + Environment.NewLine + Common.ToXmlSchema<ToolCommandList>();

            // Tool descriptions
            var descriptions = new ToolDescriptionList();
            descriptions.Tools.Add(new ToolDescription
            {
                ToolName = CreateProcedureToolName,
                ToolInputRequirements = "REQUIRED FIELD MAPPING: Put the procedure name in Path. Put the full XML sequence of instructions (a ToolCommandList inside CDATA) in ToolInput. Procedure Cmd ToolNames can be direct tools or control primitives: Label, Goto, If, Else, For, EndFor, Break, Continue, Call, Return, Parallel. If uses ToolInput as condition, Path as true target, Tags as false target. For uses ToolInput as iteration count and must be paired with EndFor."
            });
            descriptions.Tools.Add(new ToolDescription
            {
                ToolName = CreateConceptToolName,
                ToolInputRequirements = "REQUIRED FIELD MAPPING: Put the destination file location in Path (relative to the memory root, e.g. 'concepts/user.txt'). Put the exact file contents in ToolInput (use CDATA; preserve newlines). If Path is empty, a ticks-based filename will be used. Optional: Put comma-separated descriptive keywords in Tags to aid long-term semantic search."
            });
            descriptions.Tools.Add(new ToolDescription
            {
                ToolName = DeleteConceptToolName,
                ToolInputRequirements = "REQUIRED FIELD MAPPING: Put the target file location in Path (relative to the memory root, e.g. 'concepts/user.txt'). Leave ToolInput empty for deletes."
            });
            descriptions.Tools.Add(new ToolDescription { ToolName = AgentIsFinishedToolName, ToolInputRequirements = "No input required. Emitting this ToolName tells the agent to stop its epoch loop and exit." });

            descriptions.Tools.Add(new ToolDescription
            {
                ToolName = HammRememberToolName,
                ToolInputRequirements = "Add a semantic fact to HAMM. ToolInput should be a C# expression. Path is the optional scope (e.g. 'Research')."
            });
            descriptions.Tools.Add(new ToolDescription
            {
                ToolName = HammQueryToolName,
                ToolInputRequirements = "Query HAMM using symbolic logic. ToolInput is a C# expression (can include Wild variables like ?x). Path is the optional scope."
            });
            descriptions.Tools.Add(new ToolDescription
            {
                ToolName = HammForgetToolName,
                ToolInputRequirements = "Invalidate a fact in HAMM. ToolInput is the expression to forget."
            });
            descriptions.Tools.Add(new ToolDescription
            {
                ToolName = HammSetScopeToolName,
                ToolInputRequirements = "Change the active operating scope. ToolInput is the scope name."
            });
            descriptions.Tools.Add(new ToolDescription
            {
                ToolName = HammReasonToolName,
                ToolInputRequirements = "Apply rewrite rules to infer new connections. ToolInput is a list of rules like 'Rule(a+b, b+a)'."
            });
            descriptions.Tools.Add(new ToolDescription
            {
                ToolName = HammFoldToolName,
                ToolInputRequirements = "Summarize related memories. ToolInput is a query expression."
            });
            descriptions.Tools.Add(new ToolDescription
            {
                ToolName = HammMaintenanceToolName,
                ToolInputRequirements = "Trigger decay and archiving of low-relevance facts."
            });

            // Add injected tools
            foreach (var et in _externalToolDescriptions)
            {
                // Avoid duplicates
                if (!descriptions.Tools.Any(t => string.Equals(t.ToolName, et.ToolName, StringComparison.OrdinalIgnoreCase)))
                {
                    descriptions.Tools.Add(et);
                }
            }

            // Attempt to include known ConsoleHelmsman CLIs.
            // Priority 1: Check for ActiveTools.json (exported by ConsoleHelmsman)
            bool toolsLoaded = false;
            try
            {
                var repoRoot = MemoryConfig.FindRepoRoot();
                if (!string.IsNullOrWhiteSpace(repoRoot))
                {
                    var activeToolsPath = Path.Combine(repoRoot, "External", "ActiveTools.json");
                    if (File.Exists(activeToolsPath))
                    {
                        var json = await File.ReadAllTextAsync(activeToolsPath);
                        var loaded = JsonSerializer.Deserialize<ToolDescriptionList>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (loaded != null && loaded.Tools != null && loaded.Tools.Count > 0)
                        {
                            foreach (var t in loaded.Tools)
                            {
                                descriptions.Tools.Add(t);
                            }
                            toolsLoaded = true;
                        }
                    }
                }
            }
            catch { }

            if (!toolsLoaded)
            {
                // Priority 2: consoles.config or fallback
                try
                {
                    var appBase = AppDomain.CurrentDomain.BaseDirectory;
                    var consolesPath = Path.Combine(appBase, "consoles.config");
                    if (!File.Exists(consolesPath))
                    {
                        // also try sibling ConsoleHelmsman folder
                        consolesPath = Path.Combine("..", "ConsoleHelmsman", "consoles.config");
                    }

                    if (File.Exists(consolesPath))
                    {
                        var text = await File.ReadAllTextAsync(consolesPath);
                        // Robust parse for <ConsoleAppBase> entries
                        var blocks = System.Text.RegularExpressions.Regex.Matches(text, "<ConsoleAppBase>(.*?)</ConsoleAppBase>", System.Text.RegularExpressions.RegexOptions.Singleline);
                        foreach (System.Text.RegularExpressions.Match block in blocks)
                        {
                            var content = block.Groups[1].Value;
                            var nameMatch = System.Text.RegularExpressions.Regex.Match(content, "<Name>(.*?)</Name>");
                            var selectedMatch = System.Text.RegularExpressions.Regex.Match(content, "<Selected>(.*?)</Selected>");
                            
                            if (nameMatch.Success)
                            {
                                var n = nameMatch.Groups[1].Value.Trim();
                                var isSelected = selectedMatch.Success && string.Equals(selectedMatch.Groups[1].Value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
                                
                                if (!string.IsNullOrEmpty(n) && isSelected)
                                {
                                    // Avoid duplicates if already added via external tools
                                    if (!descriptions.Tools.Any(t => string.Equals(t.ToolName, n, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        descriptions.Tools.Add(new ToolDescription { ToolName = n, ToolInputRequirements = n == "codex" || n == "copilot" || n == "gemini" ? "Concise English instructions for code generation." : "Command string for the target CLI (powershell, git, etc.)." });
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // fallback set
                        var fallbacks = new[] { "codex", "copilot", "gemini", "powershell", "cmd", "git", "gh" };
                        foreach (var f in fallbacks) descriptions.Tools.Add(new ToolDescription { ToolName = f, ToolInputRequirements = f == "codex" || f == "copilot" || f == "gemini" ? "Concise English instructions for code generation." : "CLI command string." });
                    }
                }
                catch { descriptions.Tools.Add(new ToolDescription { ToolName = "copilot", ToolInputRequirements = "Concise English instructions." }); }
            }

            // Convert descriptions to XML so the LLM sees examples in XML
            var descriptionXml = Common.ToXml(descriptions);

            // Pinned memories
            var pinned = Common.ToXml(new MemoryFileList());
            try
            {
                pinned = await BuildFileListXmlAsync(_pinnedRoot);
            }
            catch { }

            // Recent Tool Outputs (Direct Pipeline)
            var recentOutputs = new MemoryFileList();
            foreach (var (tool, input, output, time) in _recentToolOutputs)
            {
                recentOutputs.Items.Add(new MemoryFile 
                { 
                    FileName = $"{tool}_{time.Ticks}.txt", 
                    Content = $"Tool: {tool}\nInput: {input}\nTimestamp: {time}\nOutput:\n{output}" 
                });
            }
            var fromTools = Common.ToXml(recentOutputs);

            var savedFromTools = Common.ToXml(new MemoryFileList());
            try
            {
                savedFromTools = await BuildFileListXmlAsync(_savedFromToolsRoot);
            }
            catch { }

            // Memory summary will be attached by the caller as specified.

            var advice = "The agent has a Heuristic Associative Memory Model (HAMM) with an EGraph for semantic equivalence. Scopes provide contextual segregation. Facts have Bayesian Certainty and Potency (relevance decay). You can use 'Hamm*' tools to manage this long-term reasoning structure.";

            var parts = new[] {
                intro,
                Common.WrapInTags(advice, "MemoryStructureAdvice"),
                Common.WrapInTags(schemaSection, "ToolCommandListSchema"),
                Common.WrapInTags("This section lists available tools and their input requirements." + Environment.NewLine + descriptionXml, "ToolDescriptionList"),
                Common.WrapInTags("This section contains pinned memories that remain read-only and provide stable guidance across epochs." + Environment.NewLine + pinned, "PinnedMemories"),
                Common.WrapInTags("This section lists recent tool execution results (replacing file-based FromTools)." + Environment.NewLine + fromTools, "FromTools"),
                Common.WrapInTags("This section holds tool outputs explicitly preserved in Memory/SavedFromTools for longer-term reference and optional pruning." + Environment.NewLine + savedFromTools, "SavedFromTools")
            };

            return string.Join(Environment.NewLine + Environment.NewLine, parts);
        }

        private async Task<ToolCommandList> ParseResponseAsync(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return new ToolCommandList();

            try
            {
                // Robustness: Some models wrap the list in ArrayOfToolCommand or an extra Commands tag
                // even when the schema says not to. Strip them if they appear.
                var cleaned = response.Replace("<ArrayOfToolCommand>", "").Replace("</ArrayOfToolCommand>", "")
                                      .Replace("<Commands>", "").Replace("</Commands>", "");

                // Attempt to parse XML from any surrounding text. Common.FromXml will extract the root element and deserialize.
                var parsed = Common.FromXml<ToolCommandList>(cleaned);
                return parsed ?? new ToolCommandList();
            }
            catch (Exception ex)
            {
                await LogErrorAsync($"ParseResponse Error: {ex.Message}");
                return new ToolCommandList();
            }
        }

        private async Task ApplyChangesAsync(ToolCommandList? response, string goalPrompt)
        {
            if (response?.Commands == null || response.Commands.Count == 0)
            {
                return;
            }

            int deletionsDone = 0;
            int toolsRun = 0;
            const int MaxToolsPerEpoch = 10;

            foreach (var cmd in response.Commands)
            {
                if (cmd == null) continue;
                if (string.IsNullOrWhiteSpace(cmd.ToolName)) continue;

                // Support for $output and $path variable substitution from previous tool execution
                if (!string.IsNullOrEmpty(cmd.ToolInput))
                {
                    var last = _recentToolOutputs.LastOrDefault();
                    if (!string.IsNullOrEmpty(last.Output))
                    {
                        var cleanOutput = last.Output.Trim();
                        
                        // REPORTING SAFETY: Truncate massive outputs if being used for findings/observations
                        if (string.Equals(cmd.ToolName, VerifiedSecurityFindingToolName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(cmd.ToolName, SafeObservationToolName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (cleanOutput.Length > 500)
                            {
                                cleanOutput = cleanOutput.Substring(0, 500) + "... [TRUNCATED]";
                            }
                        }

                        if (cmd.ToolInput.Contains("$output"))
                        {
                            cmd.ToolInput = cmd.ToolInput.Replace("$output", cleanOutput);
                        }

                        if (cmd.ToolInput.Contains("$path"))
                        {
                            var firstLine = cleanOutput;
                            if (firstLine.Contains(Environment.NewLine))
                            {
                                firstLine = firstLine.Split(new[] { Environment.NewLine }, StringSplitOptions.None)[0].Trim();
                            }
                            else if (firstLine.Contains("\n"))
                            {
                                firstLine = firstLine.Split('\n')[0].Trim();
                            }
                            cmd.ToolInput = cmd.ToolInput.Replace("$path", firstLine);
                        }
                    }
                }

                // HAMM Integration: Record action
                _hamm.Act(new Symbol(cmd.ToolName + ":" + (cmd.ToolInput ?? "")));

                try
                {
                    // AUTO-REDIRECTION: If the LLM tried to use powershell/copilot for a security task, 
                    // try to infer what it wanted and run the correct direct tool instead.
                    if (goalPrompt.Contains("security", StringComparison.OrdinalIgnoreCase) && 
                        (string.Equals(cmd.ToolName, "powershell", StringComparison.OrdinalIgnoreCase) || 
                         string.Equals(cmd.ToolName, "copilot", StringComparison.OrdinalIgnoreCase)))
                    {
                        var script = cmd.ToolInput ?? "";
                        if (script.Contains("search", StringComparison.OrdinalIgnoreCase) || script.Contains("scan", StringComparison.OrdinalIgnoreCase))
                        {
                            await NotifyTraceAsync("[Agent] REDIRECTING script to MultiSearchVerify...");
                            await MultiSearchVerifyAsync($"{_repoRoot} ExecuteExternalCommand cmd.exe Assembly.Load BinaryFormatter Process.Start");
                            continue;
                        }
                    }

                    // If the LLM explicitly signals the agent is finished, stop the epoch loop.
                    if (string.Equals(cmd.ToolName, AgentIsFinishedToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (await ShouldHonorAgentIsFinishedAsync())
                        {
                            await LogErrorAsync("AgentIsFinished received from LLM; stopping epoch loop.");
                            await NotifyTraceAsync("[Agent] AgentIsFinished honored; stopping.");
                            _loopCts?.Cancel();
                            return;
                        }

                        await LogErrorAsync("AgentIsFinished received from LLM but ignored because InstructionPointer indicates work remains.");
                        await NotifyTraceAsync("[Agent] AgentIsFinished ignored (InstructionPointer not at Done).");
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, DeleteConceptToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (deletionsDone >= MaxDeletionsPerEpoch)
                        {
                            await NotifyDeletionAsync($"Delete skipped for '{cmd.Path}': reached MaxDeletionsPerEpoch ({MaxDeletionsPerEpoch}).");
                            continue;
                        }

                        if (await TryDeleteConceptFileAsync(cmd.Path))
                        {
                            deletionsDone++;
                        }

                        continue;
                    }

                    if (string.Equals(cmd.ToolName, CreateConceptToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await CreateConceptFileAsync(cmd.Path, cmd.ToolInput ?? string.Empty, cmd.Tags);
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, CreateProcedureToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await CreateProcedureAsync(cmd.Path, cmd.ToolInput ?? string.Empty);
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, HammRememberToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await HammRememberAsync(cmd.ToolInput ?? string.Empty, cmd.Path);
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, HammQueryToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await HammQueryAsync(cmd.ToolInput ?? string.Empty, cmd.Path);
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, HammForgetToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await HammForgetAsync(cmd.ToolInput ?? string.Empty);
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, HammSetScopeToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await HammSetScopeAsync(cmd.ToolInput ?? string.Empty);
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, HammReasonToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await HammReasonAsync(cmd.ToolInput ?? string.Empty);
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, HammFoldToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await HammFoldAsync(cmd.ToolInput ?? string.Empty);
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, HammMaintenanceToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await HammMaintenanceAsync();
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, SearchVerifyToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await SearchVerifyAsync(cmd.ToolInput ?? string.Empty);
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, MultiSearchVerifyToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await MultiSearchVerifyAsync(cmd.ToolInput ?? string.Empty);
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, TraceSourceToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await TraceSourceAsync(cmd.ToolInput ?? string.Empty);
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, SearchGuardsToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await SearchGuardsAsync(cmd.ToolInput ?? string.Empty);
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, SecurityPolicyToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await SecurityPolicyAsync();
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, VerifiedSecurityFindingToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await VerifiedSecurityFindingAsync(cmd.ToolInput ?? string.Empty);
                        continue;
                    }

                    if (string.Equals(cmd.ToolName, SafeObservationToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        await SafeObservationAsync(cmd.ToolInput ?? string.Empty);
                        continue;
                    }

                    // Direct Tool Execution
                    if (_toolRunner != null)
                    {
                        // SECURITY POLICY: Block powershell/pwsh/copilot for security scans to force direct, observable tool usage.
                        if (goalPrompt.Contains("security scan", StringComparison.OrdinalIgnoreCase) && 
                            (string.Equals(cmd.ToolName, "powershell", StringComparison.OrdinalIgnoreCase) || 
                             string.Equals(cmd.ToolName, "pwsh", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(cmd.ToolName, "cmd", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(cmd.ToolName, "copilot", StringComparison.OrdinalIgnoreCase)))
                        {
                            var msg = $"Tool '{cmd.ToolName}' is STRICTLY FORBIDDEN for security audits. You must use TraceSource, SearchGuards, and MultiSearchVerify directly.";
                            await NotifyTraceAsync($"[Agent] {msg}");
                            _hamm.Remember(new Equality(new Symbol("ToolError"), new Symbol(msg)), scope: "Observations");
                            continue;
                        }

                        if (toolsRun >= MaxToolsPerEpoch)
                        {
                            await NotifyTraceAsync($"[Agent] Skipping tool '{cmd.ToolName}': reached MaxToolsPerEpoch ({MaxToolsPerEpoch}).");
                            continue;
                        }
                        await DispatchToolCommandAsync(cmd);
                        toolsRun++;
                    }
                    else
                    {
                        await LogErrorAsync($"Tool '{cmd.ToolName}' requested but no IToolRunner configured.");
                    }
                }
                catch (Exception ex)
                {
                    await LogErrorAsync($"ToolCommand Error ({cmd.ToolName}): {ex.Message}");
                }
            }
        }

        private async Task<bool> ShouldHonorAgentIsFinishedAsync()
        {
            try
            {
                // If an AIMath InstructionPointer exists and isn't at instruction 0, do not honor AgentIsFinished.
                // This prevents the LLM from prematurely exiting while the procedure-based orchestrator is active.
                var ip = Path.Combine(_memoryRoot, "AIMath", "Work", "InstructionPointer.txt");
                if (!File.Exists(ip)) return true;

                var lines = await File.ReadAllLinesAsync(ip);
                if (lines.Length < 3) return false;
                var instruction = (lines[2] ?? string.Empty).Trim();
                return string.Equals(instruction, "0", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // If we can't read, be conservative and do not stop.
                return false;
            }
        }

        private async Task CreateProcedureAsync(string procedureName, string procedureXml)
        {
            if (string.IsNullOrWhiteSpace(procedureName))
            {
                await LogErrorAsync("CreateProcedure failed: No procedure name provided in Path.");
                return;
            }

            ToolCommandList? procList = null;
            try 
            {
                // Attempt direct XML deserialization for pure blocks
                procList = Common.FromXml<ToolCommandList>(procedureXml);
                
                // Fallback to searching for blocks if direct parse failed (e.g. if LLM added preamble)
                if (procList == null || procList.Commands == null || procList.Commands.Count == 0)
                {
                    procList = await ParseResponseAsync(procedureXml);
                }
            } 
            catch { }
            
            if (procList == null || procList.Commands == null || procList.Commands.Count == 0)
            {
                await LogErrorAsync($"CreateProcedure '{procedureName}' failed: Could not parse instructions.");
                return;
            }

            var cmdExprs = new List<IExpression>();
            foreach(var c in procList.Commands)
            {
                if (c == null) continue;
                var args = new List<IExpression> { 
                    new Symbol(c.ToolName ?? ""), 
                    new Symbol(c.ToolInput ?? ""), 
                    new Symbol(c.Path ?? ""), 
                    new Symbol(c.Tags ?? "") 
                };
                cmdExprs.Add(new Function("Cmd", args.ToArray()));
            }
            
            var procExpr = new Function("Procedure", new Symbol(procedureName), new Function("List", cmdExprs.ToArray()));
            
            _hamm.Remember(procExpr, scope: "Procedures");
            
            var ipExpr = new Function("ActivePointer", new Symbol(procedureName), new Number(0));
            _hamm.Remember(ipExpr, scope: "Global"); 
            
            await NotifyTraceAsync($"[Agent] Created Procedure '{procedureName}' with {cmdExprs.Count} instructions. Instruction Pointer set to 0.");
        }

        private async Task CreateConceptFileAsync(string path, string content, string? tags = null)
        {
            try
            {
                string fullPath;

                if (string.IsNullOrWhiteSpace(path))
                {
                    var id = DateTime.Now.Ticks.ToString();
                    var fileName = id + ".txt";
                    fullPath = Path.GetFullPath(Path.Combine(_memoryRoot, fileName));
                }
                else
                {
                    // Treat provided path as relative to memory root. Trim any leading separators to avoid rooted paths.
                    var safeRelative = path.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/');
                    fullPath = Path.GetFullPath(Path.Combine(_memoryRoot, safeRelative));
                }

                if (!fullPath.StartsWith(_fullMemoryRoot, StringComparison.OrdinalIgnoreCase))
                {
                    await LogErrorAsync("CreateConcept safety violation: resolved path outside memory root.");
                    return;
                }

                // Prevent creating files under the pinned folder
                if (fullPath.StartsWith(_fullPinnedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    await LogErrorAsync("CreateConcept prevented: target path is under Pinned and read-only.");
                    return;
                }

                // Ensure parent dir exists
                var dir = Path.GetDirectoryName(fullPath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                await File.WriteAllTextAsync(fullPath, content ?? string.Empty);
                
                // HAMM Integration: Remember
                var relPath = Path.GetRelativePath(_memoryRoot, fullPath);
                var contentSymbol = MemoryContentEncoding.EncodeContentSymbol(content ?? string.Empty, _hamm.Store.MaxInlineSymbolChars);
                var fact = _hamm.Store.AddFact(new Equality(new Symbol(relPath), contentSymbol));
                
                if (!string.IsNullOrWhiteSpace(tags))
                {
                    var tagList = tags.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var tag in tagList)
                    {
                        if (!fact.Tags.Contains(tag)) fact.Tags.Add(tag);
                    }
                    _hamm.Store.RebuildEGraph(); // Re-index with new tags
                }

                // Trace to UI for observability (especially for procedure-driven runs like AIMath verification).
                var rel = Path.GetRelativePath(_memoryRoot, fullPath);
                string preview;
                var safe = content ?? string.Empty;
                if (safe.Length <= 8000) preview = safe;
                else preview = safe.Substring(0, 8000) + "\n...<truncated>";
                await NotifyTraceAsync($"[CreateConcept] {rel} (Tags: {tags ?? "none"})\n{preview}");
            }
            catch (Exception ex)
            {
                await LogErrorAsync($"CreateConcept Error: {ex.Message}");
            }
        }

        private async Task<bool> TryDeleteConceptFileAsync(string? relativeFileNameOrPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relativeFileNameOrPath))
                {
                    await NotifyDeletionAsync("Delete failed: empty or null filename provided.");
                    return false;
                }

                var safeRelative = relativeFileNameOrPath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/');
                string fullPath = Path.GetFullPath(Path.Combine(_memoryRoot, safeRelative));
                if (!fullPath.StartsWith(_fullMemoryRoot, StringComparison.OrdinalIgnoreCase))
                {
                    await NotifyDeletionAsync($"Delete failed for '{relativeFileNameOrPath}': resolved path outside memory root ('{fullPath}').");
                    return false;
                }

                // Safety: Pinned files cannot be deleted
                if (relativeFileNameOrPath.StartsWith("Pinned", StringComparison.OrdinalIgnoreCase) ||
                    fullPath.StartsWith(_fullPinnedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    await NotifyDeletionAsync($"Delete prevented for '{relativeFileNameOrPath}': file is pinned.");
                    return false;
                }

                if (!File.Exists(fullPath))
                {
                    await NotifyDeletionAsync($"Delete failed for '{relativeFileNameOrPath}': file not found at '{fullPath}'.");
                    return false;
                }

                File.Delete(fullPath);
                
                // HAMM Integration: Invalidate
                // We find facts where Path == relativeFileNameOrPath
                var facts = _hamm.Store.GetFacts().Where(f => 
                    f.Expression is Equality eq && 
                    eq.LeftOperand is Symbol s && 
                    s.Name == safeRelative).ToList();
                
                foreach (var f in facts)
                {
                    _hamm.Store.InvalidateFact(f);
                }

                await NotifyDeletionAsync($"Deleted concept file: '{relativeFileNameOrPath}' (full path: '{fullPath}').");
                return true;
            }
            catch (Exception ex)
            {
                await NotifyDeletionAsync($"Delete error for '{relativeFileNameOrPath}': {ex.Message}");
                return false;
            }
        }

        private string GetUniquePath(string fullPath)
        {
            if (!File.Exists(fullPath)) return fullPath;

            var dir = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(dir)) dir = _savedFromToolsRoot;

            var name = Path.GetFileNameWithoutExtension(fullPath);
            var ext = Path.GetExtension(fullPath);
            return Path.Combine(dir, $"{name}_{DateTime.Now.Ticks}{ext}");
        }

        private async Task SearchVerifyAsync(string input)
        {
            if (_toolRunner == null) return;
            
            var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return;
            
            var dir = parts[0].Trim('"');
            var pattern = parts[1];

            if (!Directory.Exists(dir))
            {
                var tryPath = Path.Combine(_repoRoot, dir);
                if (Directory.Exists(tryPath)) dir = tryPath;
                else dir = _repoRoot;
            }
            
            await NotifyTraceAsync($"[SearchVerify] Searching for '{pattern}' in '{dir}'...");
            string searchOutput = await _toolRunner.RunToolAsync("drivecli", $"search \"{dir}\" \"{pattern}\"");
            
            var lines = searchOutput.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .ToList();
                
            if (lines.Count == 0)
            {
                _hamm.Remember(new Equality(new Symbol($"Search({pattern})"), new Symbol("No results")), scope: "Observations");
                await NotifyTraceAsync($"[SearchVerify] No results for '{pattern}'.");
                return;
            }
            
            await NotifyTraceAsync($"[SearchVerify] Found {lines.Count} matches. Verifying first 20...");
            
            foreach (var line in lines.Take(20)) 
            {
                try
                {
                    // Line format: path(line): content
                    var match = Regex.Match(line, @"^(.*)\((\d+)\): (.*)$");
                    if (match.Success)
                    {
                        var file = match.Groups[1].Value;
                        var lineNum = match.Groups[2].Value;
                        var content = match.Groups[3].Value;
                        
                        var observation = $"File({file}) line {lineNum} contains '{pattern}'";
                        _hamm.Remember(new Equality(new Symbol(observation), new Symbol(content)), scope: "Observations");
                    }
                    else
                    {
                        _hamm.Remember(new Equality(new Symbol($"Match({pattern})"), new Symbol(line)), scope: "Observations");
                    }
                }
                catch { }
            }
        }

        private async Task TraceSourceAsync(string input)
        {
            if (_toolRunner == null) return;
            
            var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return;
            
            var dir = parts[0].Trim('"');
            var methodName = parts[1];
            
            await NotifyTraceAsync($"[TraceSource] Finding callers of '{methodName}' in '{dir}'...");
            // Search for method calls (pattern: methodName followed by parenthesis)
            await SearchVerifyAsync($"{dir} {methodName}(");
        }

        private async Task SearchGuardsAsync(string input)
        {
            if (_toolRunner == null) return;
            
            var dir = input.Trim('"');
            if (!Directory.Exists(dir))
            {
                var tryPath = Path.Combine(_repoRoot, dir);
                if (Directory.Exists(tryPath)) dir = tryPath;
                else dir = _repoRoot;
            }
            
            await NotifyTraceAsync($"[SearchGuards] Checking for sanitization logic in '{dir}'...");
            // Common sanitization patterns
            var guardPatterns = new[] { "Regex", "Replace", "Sanitize", "Whitelist", "Validat", "Check", "Path.GetFileName" };
            foreach (var pattern in guardPatterns)
            {
                string searchOutput = await _toolRunner.RunToolAsync("drivecli", $"search \"{dir}\" \"{pattern}\"");
                var lines = searchOutput.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                if (lines.Count > 0)
                {
                    _hamm.Remember(new Equality(new Symbol($"GuardCheck({pattern})"), new Symbol($"Found {lines.Count} matches in {dir}")), scope: "Observations");
                }
            }
        }

        private async Task MultiSearchVerifyAsync(string input)
        {
            if (_toolRunner == null) return;

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return;

            var dir = parts[0].Trim('"');
            var patterns = parts.Skip(1).ToList();

            if (!Directory.Exists(dir))
            {
                var tryPath = Path.Combine(_repoRoot, dir);
                if (Directory.Exists(tryPath)) dir = tryPath;
                else dir = _repoRoot;
            }

            foreach (var pattern in patterns)
            {
                await SearchVerifyAsync($"{dir} {pattern}");
            }
        }

        private async Task SecurityPolicyAsync()
        {
            var rules = new[]
            {
                "RULE 1: SINK DISCOVERY. Find dangerous functions.",
                "RULE 2: SOURCE TRACING. Find where data comes from.",
                "RULE 3: GUARD CHECK. Find sanitization (Regex, Replace, etc.).",
                "RULE 4: MANDATORY TOOLS. Use VerifiedSecurityFinding to report bugs. Use SafeObservation to report safe code.",
                "RULE 5: EVIDENCE. You must provide the Sink, Source, and evidence of Missing Guards for every vulnerability."
            };
            
            foreach (var rule in rules)
            {
                _hamm.Remember(new Equality(new Symbol("SecurityPolicy"), new Symbol(rule)), scope: "Observations");
            }
            
            await NotifyTraceAsync("[SecurityPolicy] Mandatory audit rules recorded in HAMM Observations.");
        }

        private async Task VerifiedSecurityFindingAsync(string input)
        {
            // Parse for evidence markers (e.g. File: <path> or Sink: <path>)
            var regex = new Regex(@"([A-Za-z0-9_\-\.\/\\]+\.cs)");
            var matches = regex.Matches(input);
            var missingFiles = new List<string>();

            foreach (Match match in matches)
            {
                var path = match.Value;
                var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(_repoRoot, path);
                if (!File.Exists(fullPath))
                {
                    missingFiles.Add(path);
                }
            }

            if (missingFiles.Count > 0)
            {
                var error = $"HALLUCINATION DETECTED: The following files cited as evidence do not exist: {string.Join(", ", missingFiles)}. Finding REJECTED.";
                await NotifyTraceAsync($"[VerifiedSecurityFinding] {error}");
                _hamm.Remember(new Equality(new Symbol("HallucinationError"), new Symbol(error)), scope: "Observations");
                return;
            }

            var findingsPath = Path.Combine(_repoRoot, "SecurityFindings.txt");
            var entry = $"[VERIFIED VULNERABILITY]\n{input}\n---\n";
            await File.AppendAllTextAsync(findingsPath, entry);
            await NotifyTraceAsync("[VerifiedSecurityFinding] Recorded a high-precision finding.");
        }

        private async Task SafeObservationAsync(string input)
        {
            _hamm.Remember(new Equality(new Symbol("VerifiedSafe"), new Symbol(input)), scope: "Observations");
            await NotifyTraceAsync("[SafeObservation] Recorded a verified safe observation.");
        }

        private void AddRecentToolOutput(string toolName, string input, string output)
        {
            lock (_toolHistoryLock)
            {
                _recentToolOutputs.Add((toolName, input, output, DateTime.Now));
                if (_recentToolOutputs.Count > MaxToolHistory)
                {
                    _recentToolOutputs.RemoveAt(0);
                }
            }
        }

        private async Task DispatchToolCommandAsync(ToolCommand cmd)
        {
            if (_toolRunner == null) return;
            
            try
            {
                var toolInput = cmd.ToolInput ?? "";

                // AUTO-ROOTING: If using drivecli, ensure paths are rooted to repoRoot if they aren't already.
                if (string.Equals(cmd.ToolName, "drivecli", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = toolInput.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        var command = parts[0];
                        var remaining = parts[1];
                        
                        // Commands that take paths as the first argument after the command name
                        var pathCommands = new[] { "read", "write", "append", "exists", "delete", "mkdir", "list", "search" };
                        if (pathCommands.Contains(command.ToLowerInvariant()))
                        {
                            var pathParts = remaining.Split(' ', 2);
                            var path = pathParts[0].Trim('"');
                            if (!Path.IsPathRooted(path))
                            {
                                // Check if path starts with repo folder name to avoid nesting
                                var repoDirName = Path.GetFileName(_repoRoot);
                                if (path.StartsWith(repoDirName + Path.DirectorySeparatorChar) || path.StartsWith(repoDirName + "/"))
                                {
                                    // Path already includes the repo folder, just root it to the parent
                                    var parent = Path.GetDirectoryName(_repoRoot) ?? _repoRoot;
                                    path = Path.GetFullPath(Path.Combine(parent, path));
                                }
                                else
                                {
                                    path = Path.GetFullPath(Path.Combine(_repoRoot, path));
                                }

                                if (pathParts.Length > 1)
                                    toolInput = $"{command} \"{path}\" {pathParts[1]}";
                                else
                                    toolInput = $"{command} \"{path}\"";
                            }
                        }
                    }
                }

                await NotifyTraceAsync($"[DirectTool] Running {cmd.ToolName}...");
                string output = await _toolRunner.RunToolAsync(cmd.ToolName, toolInput);
                
                // Store result
                AddRecentToolOutput(cmd.ToolName, toolInput, output);

                // HAMM Auto-Observation: Record tool output as a fact for long-term memory
                try
                {
                    var cleanInput = (cmd.ToolInput ?? "").Trim();
                    var cleanOutput = output.Trim();
                    
                    // We only store reasonably small outputs as immediate facts to avoid bloating HAMM.
                    // Larger outputs are still available via the 'FromTools' context injection.
                    if (cleanOutput.Length > 0 && cleanOutput.Length < 1000)
                    {
                        var toolKey = $"Observation({cmd.ToolName}: {cleanInput})";
                        _hamm.Remember(new Equality(new Symbol(toolKey), new Symbol(cleanOutput)), scope: "Observations");
                    }
                    else if (cleanOutput.Length == 0)
                    {
                        var toolKey = $"Observation({cmd.ToolName}: {cleanInput})";
                        _hamm.Remember(new Equality(new Symbol(toolKey), new Symbol("Empty Output")), scope: "Observations");
                    }
                }
                catch { }
                
                await NotifyTraceAsync($"[DirectTool] Finished {cmd.ToolName}. Output: {output.Length} chars.");
            }
            catch (Exception ex)
            {
                 await LogErrorAsync($"Direct Tool Execution Error: {ex.Message}");
                  AddRecentToolOutput(cmd.ToolName, cmd.ToolInput ?? "", $"[Execution Error: {ex.Message}]");

                 try
                 {
                    var toolKey = $"ObservationError({cmd.ToolName}: {cmd.ToolInput ?? ""})";
                    _hamm.Remember(new Equality(new Symbol(toolKey), new Symbol(ex.Message)), scope: "Observations");
                 }
                 catch { }
            }
        }

        private async Task HammRememberAsync(string input, string? scope)
        {
            try
            {
                var exprs = CSharpIO.ParseExpressions(input);
                foreach (var expr in exprs)
                {
                    _hamm.Remember(expr, scope: string.IsNullOrWhiteSpace(scope) ? "Global" : scope);
                }
                await NotifyTraceAsync($"[HAMM] Remembered {exprs.Count} fact(s) in scope '{scope ?? "Global"}'.");
            }
            catch (Exception ex) { await LogErrorAsync($"HammRemember Error: {ex.Message}"); }
        }

        private async Task HammQueryAsync(string input, string? scope)
        {
            try
            {
                var expressions = CSharpIO.ParseExpressions(input);
                if (expressions.Count == 0)
                {
                    await LogErrorAsync($"HammQuery failed: Could not parse expression '{input}'");
                    return;
                }

                var query = expressions[0];
                var results = _hamm.Store.QueryV2(query, new HAMM.QueryOptions { Scope = scope ?? "Global" }).ToList();
                
                var sb = new StringBuilder();
                sb.AppendLine($"Query: {CSharpIO.FormatExpr(query)}");
                sb.AppendLine($"Results found: {results.Count}");
                foreach(var r in results)
                {
                    sb.AppendLine($"- [{r.Scope}] {CSharpIO.FormatExpr(r.Expression)} (Certainty: {r.Certainty:F2})");
                }
                
                var output = sb.ToString();
                await NotifyTraceAsync($"[HAMM] Query result:\n{output}");
                
                // Store output so LLM can see it in next epoch
                AddRecentToolOutput(HammQueryToolName, input, output);
            }
            catch (Exception ex)
            {
                await LogErrorAsync($"HammQuery Error: {ex.Message}");
            }
        }

        private async Task HammForgetAsync(string input)
        {
            try
            {
                var exprs = CSharpIO.ParseExpressions(input);
                int count = 0;
                foreach (var expr in exprs)
                {
                    var facts = _hamm.Store.GetFacts().Where(f => f.Expression.InternalEquals(expr)).ToList();
                    foreach (var f in facts)
                    {
                        _hamm.Store.InvalidateFact(f);
                        count++;
                    }
                }
                await NotifyTraceAsync($"[HAMM] Invalidated {count} fact(s).");
            }
            catch (Exception ex) { await LogErrorAsync($"HammForget Error: {ex.Message}"); }
        }

        private async Task HammSetScopeAsync(string scope)
        {
            _hamm.CurrentScope = string.IsNullOrWhiteSpace(scope) ? "Global" : scope.Trim();
            await NotifyTraceAsync($"[HAMM] Set active scope to '{_hamm.CurrentScope}'.");
        }

        private async Task HammReasonAsync(string input)
        {
            try
            {
                var rules = CSharpIO.ParseRules(input);
                _hamm.Reason(rules);
                await NotifyTraceAsync($"[HAMM] Applied {rules.Count} reasoning rule(s).");
            }
            catch (Exception ex) { await LogErrorAsync($"HammReason Error: {ex.Message}"); }
        }

        private async Task HammFoldAsync(string input)
        {
            try
            {
                var exprs = CSharpIO.ParseExpressions(input);
                if (exprs.Count > 0)
                {
                    _hamm.Fold(exprs.ToList());
                }
                await NotifyTraceAsync($"[HAMM] Folded memories matching {exprs.Count} expression(s).");
            }
            catch (Exception ex) { await LogErrorAsync($"HammFold Error: {ex.Message}"); }
        }

        private async Task HammMaintenanceAsync()
        {
            _hamm.Maintenance();
            await NotifyTraceAsync("[HAMM] Maintenance completed (decay and archiving applied).");
        }

        public void SaveHAMM()
        {
            _hamm.Store.Save(_memoryRoot);
        }
    }
}
