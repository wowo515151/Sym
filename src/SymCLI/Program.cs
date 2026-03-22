// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using SymSolvers.CSharpAnalysis;
using WordsToSym;

namespace SymCLI
{
public class Program
{
        private static readonly HashSet<string> ExcludedAnalyzeDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            "bin",
            "obj",
            ".git",
            ".vs",
            ".vscode",
            "TestResults",
            "Artifacts",
            "node_modules",
            "packages",
            "ref",
            "test",
            "tests",
            "example",
            "examples",
            "sample",
            "samples",
            "GeneratedAlgorithms",
            "Generated",
            "generated"
        };

        public static int Main(string[] args)
        {
            if (args.Length >= 2 &&
                string.Equals(args[0], "analyze", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(args[1], "csharp-math", StringComparison.OrdinalIgnoreCase))
            {
                return RunCSharpMathAnalysis(args);
            }

            if (args.Length == 2)
            {
                return RunSolve(args[0], args[1]);
            }

            PrintUsage();
            return 1;
        }

        private static int RunSolve(string inputPath, string outputPath)
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    Console.WriteLine($"Error: Input file not found: {inputPath}");
                    return 1;
                }

                string problemScript = File.ReadAllText(inputPath);
                var wrapper = new ProblemScriptEGraphWrapper();
                
                string result = wrapper.SolveWithEGraph(problemScript);

                EnsureOutputDirectory(outputPath);
                File.WriteAllText(outputPath, result);
                
                if (result.StartsWith("Error:"))
                {
                    Console.WriteLine("Solving failed with errors. See output file for details.");
                    return 2;
                }

                Console.WriteLine("Solving completed successfully.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
                return 3;
            }
        }

        private static int RunCSharpMathAnalysis(string[] args)
        {
            if (args.Length == 3 && string.Equals(args[2], "--help", StringComparison.OrdinalIgnoreCase))
            {
                PrintAnalyzeUsage();
                return 0;
            }

            if (args.Length < 4)
            {
                PrintAnalyzeUsage();
                return 1;
            }

            string inputPath = args[2];
            string outputPath = args[3];

            if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
            {
                Console.WriteLine($"Error: Input file or directory not found: {inputPath}");
                return 1;
            }

            if (!TryParseAnalyzeOptions(
                    args,
                    4,
                    out var options,
                    out var outputJson,
                    out var includeSecurity,
                    out var failOnFindings,
                    out var parseError))
            {
                Console.WriteLine($"Error: {parseError}");
                PrintAnalyzeUsage();
                return 1;
            }

            var resolvedRepoScanInfoPath = ResolveRepoScanInfoPath(inputPath, options.RepoScanInfoPath);
            if (!string.IsNullOrWhiteSpace(resolvedRepoScanInfoPath))
            {
                options = options with { RepoScanInfoPath = resolvedRepoScanInfoPath };
                Console.WriteLine($"Using RepoScanInfo: {resolvedRepoScanInfoPath}");
            }

            try
            {
                var analyzer = new CSharpMathBugAnalyzer();
                CSharpMathBugAnalysisResult result;

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.AnalysisTimeoutSeconds));
                var ct = timeoutCts.Token;

                if (Directory.Exists(inputPath))
                {
                    Console.WriteLine($"Analyzing directory: {inputPath}");
                    var filePaths = EnumerateCSharpFiles(inputPath, ct).ToList();
                    if (filePaths.Count == 0)
                    {
                        result = new CSharpMathBugAnalysisResult(
                            Findings: Array.Empty<CSharpMathBugFinding>(),
                            Diagnostics: new[] { "No .cs files were found under the provided directory." },
                            CandidateCount: 0,
                            LoweredExpressionCount: 0,
                            IsComplete: true);
                    }
                    else
                    {
                        var loadedFiles = new List<(string Content, string FilePath)>(filePaths.Count);
                        var diagnostics = new List<string>();
                        const long MaxCliSourceFileBytes = 250_000;
                        foreach (var filePath in filePaths)
                        {
                            ct.ThrowIfCancellationRequested();
                            try
                            {
                                try
                                {
                                    var fileSize = new FileInfo(filePath).Length;
                                    if (fileSize > MaxCliSourceFileBytes)
                                    {
                                        diagnostics.Add($"Skipping large file '{filePath}' ({fileSize:N0} bytes)." );
                                        continue;
                                    }
                                }
                                catch
                                {
                                    // Best-effort: if we can't stat, try reading.
                                }
                                loadedFiles.Add((File.ReadAllText(filePath), filePath));
                            }
                            catch (Exception ex)
                            {
                                diagnostics.Add($"Failed to read '{filePath}': {ex.Message}");
                            }
                        }

                        result = analyzer.AnalyzeProject(loadedFiles, options, ct);
                        if (diagnostics.Count > 0)
                        {
                            result = result with
                            {
                                Diagnostics = result.Diagnostics.Concat(diagnostics).ToList(),
                                IsComplete = result.IsComplete && !ct.IsCancellationRequested
                            };
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Analyzing file: {inputPath}");
                    string source = File.ReadAllText(inputPath);
                    result = analyzer.AnalyzeProject(new[] { (source, inputPath) }, options, ct);
                }

                result = FilterFindingsForCli(result, includeSecurity);

                string report = outputJson
                    ? JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
                    : FormatAnalysisReport(result);

                EnsureOutputDirectory(outputPath);
                File.WriteAllText(outputPath, report);

                Console.WriteLine($"C# math analysis completed. Findings: {result.Findings.Count}.");
                if (result.Diagnostics.Count > 0)
                {
                    Console.WriteLine($"Diagnostics: {result.Diagnostics.Count} (written to output).");
                }

                if (failOnFindings && result.Findings.Count > 0)
                {
                    return 4;
                }

                return 0;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    var cancelled = new CSharpMathBugAnalysisResult(
                        Findings: Array.Empty<CSharpMathBugFinding>(),
                        Diagnostics: new[] { "Analysis cancelled or timed out." },
                        CandidateCount: 0,
                        LoweredExpressionCount: 0,
                        IsComplete: false);

                    var report = outputJson
                        ? JsonSerializer.Serialize(cancelled, new JsonSerializerOptions { WriteIndented = true })
                        : FormatAnalysisReport(cancelled);

                    EnsureOutputDirectory(outputPath);
                    File.WriteAllText(outputPath, report);
                }
                catch
                {
                    // Best-effort: if we cannot write the report, fall through to exit code.
                }

                Console.WriteLine("Analysis cancelled or timed out.");
                return 5;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
                return 3;
            }
        }

        private static bool TryParseAnalyzeOptions(
            string[] args,
            int startIndex,
            out CSharpMathBugAnalyzerOptions options,
            out bool outputJson,
            out bool includeSecurity,
            out bool failOnFindings,
            out string error)
        {
            bool enableStability = false;
            int maxFindings = CSharpMathBugAnalyzerOptions.Default.MaxFindings;
            double confidenceThreshold = CSharpMathBugAnalyzerOptions.Default.ConfidenceThreshold;
            int maxIterations = CSharpMathBugAnalyzerOptions.Default.MaxSaturationIterations;
            int sampleBudget = CSharpMathBugAnalyzerOptions.Default.MaxSamples;
            double saturationTimeoutSeconds = CSharpMathBugAnalyzerOptions.Default.SaturationTimeoutSeconds;
            double analysisTimeoutSeconds = CSharpMathBugAnalyzerOptions.Default.AnalysisTimeoutSeconds;
            int maxParallelism = CSharpMathBugAnalyzerOptions.Default.MaxDegreeOfParallelism;
            var securityFlowMode = CSharpMathBugAnalyzerOptions.Default.SecurityFlowMode;
            int securityMaxTraceSteps = CSharpMathBugAnalyzerOptions.Default.SecurityMaxTraceSteps;
            bool prioritizeUserSources = CSharpMathBugAnalyzerOptions.Default.PrioritizeUserSources;
            string? repoScanInfoPath = CSharpMathBugAnalyzerOptions.Default.RepoScanInfoPath;
            bool enableGuardProver = CSharpMathBugAnalyzerOptions.Default.EnableGuardProver;
            int guardMaxFacts = CSharpMathBugAnalyzerOptions.Default.GuardMaxFacts;
            int guardMaxIterations = CSharpMathBugAnalyzerOptions.Default.GuardMaxIterations;
            double guardTimeoutSeconds = CSharpMathBugAnalyzerOptions.Default.GuardTimeoutSeconds;
            outputJson = false;
            includeSecurity = true;
            failOnFindings = false;
            error = string.Empty;

            for (int i = startIndex; i < args.Length; i++)
            {
                string option = args[i];
                switch (option.ToLowerInvariant())
                {
                    case "--stability":
                        enableStability = true;
                        break;
                    case "--user-source-only":
                    case "--external-source-only":
                        // Keep both flags to preserve existing scripts while making AI/user semantics explicit.
                        prioritizeUserSources = true;
                        break;
                    case "--repo-scan-info":
                        if (!TryReadString(args, ref i, out repoScanInfoPath, out error))
                        {
                            options = CSharpMathBugAnalyzerOptions.Default;
                            return false;
                        }
                        break;
                    case "--disable-guard-prover":
                        enableGuardProver = false;
                        break;
                    case "--guard-max-facts":
                        if (!TryReadInt(args, ref i, out guardMaxFacts, out error))
                        {
                            options = CSharpMathBugAnalyzerOptions.Default;
                            return false;
                        }
                        break;
                    case "--guard-max-iterations":
                        if (!TryReadInt(args, ref i, out guardMaxIterations, out error))
                        {
                            options = CSharpMathBugAnalyzerOptions.Default;
                            return false;
                        }
                        break;
                    case "--guard-timeout-seconds":
                        if (!TryReadDouble(args, ref i, out guardTimeoutSeconds, out error))
                        {
                            options = CSharpMathBugAnalyzerOptions.Default;
                            return false;
                        }
                        break;
                    case "--json":
                        outputJson = true;
                        break;
                    case "--include-security":
                        includeSecurity = true;
                        break;
                    case "--math-only":
                        includeSecurity = false;
                        break;
                    case "--fail-on-findings":
                        failOnFindings = true;
                        break;
                    case "--max-findings":
                        if (!TryReadInt(args, ref i, out maxFindings, out error))
                        {
                            options = CSharpMathBugAnalyzerOptions.Default;
                            return false;
                        }
                        break;
                    case "--confidence-threshold":
                        if (!TryReadDouble(args, ref i, out confidenceThreshold, out error))
                        {
                            options = CSharpMathBugAnalyzerOptions.Default;
                            return false;
                        }
                        break;
                    case "--max-iterations":
                        if (!TryReadInt(args, ref i, out maxIterations, out error))
                        {
                            options = CSharpMathBugAnalyzerOptions.Default;
                            return false;
                        }
                        break;
                    case "--sample-budget":
                        if (!TryReadInt(args, ref i, out sampleBudget, out error))
                        {
                            options = CSharpMathBugAnalyzerOptions.Default;
                            return false;
                        }
                        break;
                    case "--saturation-timeout-seconds":
                        if (!TryReadDouble(args, ref i, out saturationTimeoutSeconds, out error))
                        {
                            options = CSharpMathBugAnalyzerOptions.Default;
                            return false;
                        }
                        break;
                    case "--analysis-timeout-seconds":
                        if (!TryReadDouble(args, ref i, out analysisTimeoutSeconds, out error))
                        {
                            options = CSharpMathBugAnalyzerOptions.Default;
                            return false;
                        }
                        break;
                    case "--max-parallelism":
                        if (!TryReadInt(args, ref i, out maxParallelism, out error))
                        {
                            options = CSharpMathBugAnalyzerOptions.Default;
                            return false;
                        }
                        break;
                    case "--security-flow-mode":
                        if (!TryReadSecurityFlowMode(args, ref i, out securityFlowMode, out error))
                        {
                            options = CSharpMathBugAnalyzerOptions.Default;
                            return false;
                        }
                        break;
                    case "--security-max-trace-steps":
                        if (!TryReadInt(args, ref i, out securityMaxTraceSteps, out error))
                        {
                            options = CSharpMathBugAnalyzerOptions.Default;
                            return false;
                        }
                        break;
                    default:
                        options = CSharpMathBugAnalyzerOptions.Default;
                        error = $"Unknown option '{option}'.";
                        return false;
                }
            }

            options = new CSharpMathBugAnalyzerOptions(
                EnableStabilityAnalysis: enableStability,
                MaxFindings: maxFindings,
                ConfidenceThreshold: confidenceThreshold,
                MaxSaturationIterations: maxIterations,
                MaxSamples: sampleBudget,
                SaturationTimeoutSeconds: saturationTimeoutSeconds,
                AnalysisTimeoutSeconds: analysisTimeoutSeconds,
                MaxDegreeOfParallelism: maxParallelism,
                SecurityFlowMode: securityFlowMode,
                SecurityMaxTraceSteps: securityMaxTraceSteps,
                PrioritizeUserSources: prioritizeUserSources,
                RepoScanInfoPath: repoScanInfoPath,
                EnableGuardProver: enableGuardProver,
                GuardMaxFacts: guardMaxFacts,
                GuardMaxIterations: guardMaxIterations,
                GuardTimeoutSeconds: guardTimeoutSeconds).Normalize();

            return true;
        }

        private static CSharpMathBugAnalysisResult FilterFindingsForCli(CSharpMathBugAnalysisResult result, bool includeSecurity)
        {
            if (includeSecurity)
            {
                return result;
            }

            // Support optional math-only reporting for narrower output when desired.
            var mathFindings = result.Findings
                .Where(f => f.BugId.StartsWith("CSMATH", StringComparison.Ordinal))
                .ToList();

            return result with
            {
                Findings = mathFindings,
                CandidateCount = mathFindings.Count
            };
        }

        private static bool TryReadInt(string[] args, ref int index, out int value, out string error)
        {
            value = 0;
            error = string.Empty;

            if (index + 1 >= args.Length)
            {
                error = $"Missing value for option '{args[index]}'.";
                return false;
            }

            if (!int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                error = $"Invalid integer value '{args[index + 1]}' for option '{args[index]}'.";
                return false;
            }

            index++;
            return true;
        }

        private static bool TryReadDouble(string[] args, ref int index, out double value, out string error)
        {
            value = 0;
            error = string.Empty;

            if (index + 1 >= args.Length)
            {
                error = $"Missing value for option '{args[index]}'.";
                return false;
            }

            if (!double.TryParse(args[index + 1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value))
            {
                error = $"Invalid floating-point value '{args[index + 1]}' for option '{args[index]}'.";
                return false;
            }

            index++;
            return true;
        }

        private static bool TryReadString(string[] args, ref int index, out string? value, out string error)
        {
            value = null;
            error = string.Empty;

            if (index + 1 >= args.Length)
            {
                error = $"Missing value for option '{args[index]}'.";
                return false;
            }

            var parsed = args[index + 1];
            if (string.IsNullOrWhiteSpace(parsed))
            {
                error = $"Invalid empty value for option '{args[index]}'.";
                return false;
            }

            value = parsed.Trim();
            index++;
            return true;
        }

        private static string? ResolveRepoScanInfoPath(string inputPath, string? configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                var candidate = configuredPath.Trim();
                if (Path.IsPathRooted(candidate))
                {
                    return Path.GetFullPath(candidate);
                }

                // Relative RepoScanInfo paths are resolved against the analysis input root.
                if (Directory.Exists(inputPath))
                {
                    return Path.GetFullPath(Path.Combine(inputPath, candidate));
                }

                if (File.Exists(inputPath))
                {
                    var fileDir = Path.GetDirectoryName(Path.GetFullPath(inputPath));
                    if (!string.IsNullOrWhiteSpace(fileDir))
                    {
                        return Path.GetFullPath(Path.Combine(fileDir, candidate));
                    }
                }

                return Path.GetFullPath(candidate);
            }

            var discoveryRoot = Directory.Exists(inputPath)
                ? Path.GetFullPath(inputPath)
                : Path.GetDirectoryName(Path.GetFullPath(inputPath));
            if (string.IsNullOrWhiteSpace(discoveryRoot))
            {
                return null;
            }

            var discovered = Path.Combine(discoveryRoot, "RepoScanInfo.txt");
            return File.Exists(discovered) ? discovered : null;
        }

        private static bool TryReadSecurityFlowMode(
            string[] args,
            ref int index,
            out CSharpSecurityFlowMode mode,
            out string error)
        {
            mode = CSharpSecurityFlowMode.IntraProcedural;
            error = string.Empty;

            if (index + 1 >= args.Length)
            {
                error = $"Missing value for option '{args[index]}'.";
                return false;
            }

            if (!Enum.TryParse(args[index + 1], ignoreCase: true, out mode))
            {
                error = $"Invalid security flow mode '{args[index + 1]}'. Expected one of: SinkOnly, IntraProcedural, Disabled, InterproceduralIfds, InterproceduralIde.";
                return false;
            }

            index++;
            return true;
        }

        private static string FormatAnalysisReport(CSharpMathBugAnalysisResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("C# Math Bug Analysis");
            sb.AppendLine("===================");
            sb.AppendLine($"Lowered expressions: {result.LoweredExpressionCount}");
            sb.AppendLine($"Candidate markers: {result.CandidateCount}");
            sb.AppendLine($"Findings: {result.Findings.Count}");
            sb.AppendLine();

            if (result.Diagnostics.Count > 0)
            {
                sb.AppendLine("Diagnostics (First 20)");
                sb.AppendLine("-----------");
                foreach (var diagnostic in result.Diagnostics.Take(20))
                {
                    sb.AppendLine($"- {diagnostic}");
                }
                if (result.Diagnostics.Count > 20)
                {
                    sb.AppendLine($"- ... and {result.Diagnostics.Count - 20} more.");
                }
                sb.AppendLine();
            }

            if (result.Findings.Count == 0)
            {
                sb.AppendLine("No findings.");
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine("Findings");
            sb.AppendLine("--------");
            var sortedFindings = result.Findings
                .OrderByDescending(f => f.SecurityRisk)
                .ThenByDescending(f => f.Severity)
                .ToList();
            for (int i = 0; i < sortedFindings.Count; i++)
            {
                var finding = sortedFindings[i];
                sb.AppendLine($"{i + 1}. {finding.BugId} - {finding.Title}");
                sb.AppendLine($"   Severity: {finding.Severity}");
                sb.AppendLine($"   Security Risk: {finding.SecurityRisk}");
                sb.AppendLine($"   Confidence: {finding.Confidence} ({finding.ConfidenceScore:F2})");
                if (finding.SourceSpan is not null)
                {
                    sb.AppendLine($"   Location: {finding.SourceSpan}");
                }
                sb.AppendLine($"   Expression: {finding.Expression}");
                sb.AppendLine($"   Message: {finding.Message}");
                sb.AppendLine($"   Suggestion: {finding.Suggestion}");
                if (finding.Evidence.Count > 0)
                {
                    sb.AppendLine("   Evidence:");
                    foreach (var evidence in finding.Evidence)
                    {
                        sb.AppendLine($"   - {evidence}");
                    }
                }
                if (finding.WitnessAssignments is not null && finding.WitnessAssignments.Count > 0)
                {
                    var witness = string.Join(", ", finding.WitnessAssignments.Select(kv => $"{kv.Key}={kv.Value.ToString("G9", CultureInfo.InvariantCulture)}"));
                    sb.AppendLine($"   Witness: {witness}");
                }

                if (i + 1 < result.Findings.Count)
                {
                    sb.AppendLine();
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static IEnumerable<string> EnumerateCSharpFiles(string rootPath, CancellationToken ct)
        {
            var pending = new Stack<string>();
            pending.Push(rootPath);

            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var current = pending.Pop();
                IEnumerable<string> childDirectories;
                IEnumerable<string> files;

                try
                {
                    childDirectories = Directory.EnumerateDirectories(current);
                    files = Directory.EnumerateFiles(current, "*.cs", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    yield return file;
                }

                foreach (var child in childDirectories)
                {
                    ct.ThrowIfCancellationRequested();
                    var dirName = Path.GetFileName(child);
                    if (ExcludedAnalyzeDirectories.Contains(dirName))
                    {
                        continue;
                    }

                    pending.Push(child);
                }
            }
        }

        private static void EnsureOutputDirectory(string outputPath)
        {
            var fullPath = Path.GetFullPath(outputPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  SymCLI <inputPath> <outputPath>");
            Console.WriteLine("  SymCLI analyze csharp-math <inputPath> <outputPath> [options]");
            Console.WriteLine();
            Console.WriteLine("  <inputPath> can be a file or a directory.");
            Console.WriteLine("Run 'SymCLI analyze csharp-math --help' to see analysis options.");
        }

        private static void PrintAnalyzeUsage()
        {
            Console.WriteLine("Usage: SymCLI analyze csharp-math <inputPath> <outputPath> [options]");
            Console.WriteLine("  <inputPath> can be a file or a directory (recursive search).");
            Console.WriteLine("Options:");
            Console.WriteLine("  --stability                 Enable stability scoring.");
            Console.WriteLine("  --max-findings <int>        Maximum findings to emit.");
            Console.WriteLine("  --confidence-threshold <f>  Minimum confidence score in [0,1].");
            Console.WriteLine("  --max-iterations <int>      Max EGraph saturation iterations.");
            Console.WriteLine("  --sample-budget <int>       Sample budget for stability checks.");
            Console.WriteLine("  --saturation-timeout-seconds <f>  Time budget for EGraph saturation.");
            Console.WriteLine("  --analysis-timeout-seconds <f>     End-to-end analysis wall-clock timeout.");
            Console.WriteLine("  --max-parallelism <int>     Max worker threads for per-tree analysis (default: half cores).");
            Console.WriteLine("  --security-flow-mode <mode> Source/sink mode: SinkOnly, IntraProcedural, Disabled, InterproceduralIfds, InterproceduralIde.");
            Console.WriteLine("  --security-max-trace-steps <int>  Max evidence steps to emit for flow findings.");
            Console.WriteLine("  --user-source-only          Only report findings rooted in unverified external sources (user/AI).");
            Console.WriteLine("  --external-source-only      Alias for --user-source-only.");
            Console.WriteLine("  --repo-scan-info <path>     Load repo-specific source/sink/sanitizer rules from RepoScanInfo.txt.");
            Console.WriteLine("                             If omitted, RepoScanInfo.txt is auto-loaded from the input directory when present.");
            Console.WriteLine("  --disable-guard-prover      Disable the path-sensitive guard prover.");
            Console.WriteLine("  --guard-max-facts <int>     Maximum number of derived facts per branch (default: 32).");
            Console.WriteLine("  --guard-max-iterations <int> Maximum EGraph iterations for guard derivation (default: 8).");
            Console.WriteLine("  --guard-timeout-seconds <f>  Timeout for guard prover EGraph saturation (default: 2.0).");
            Console.WriteLine("  --json                      Emit JSON output.");
            Console.WriteLine("  --include-security          Include CSSEC findings (default behavior).");
            Console.WriteLine("  --math-only                 Emit only CSMATH findings.");
            Console.WriteLine("  --fail-on-findings          Return exit code 4 when findings are present.");
        }
    }
}
