// Copyright Warren Harding 2026
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.IO;
using Sym.Core; // Assuming IExpression is here or similar

namespace SymSolvers.CSharpAnalysis
{
    public class CSharpMathBugAnalyzer
    {
        private const int MaxReportedDiagnostics = 60;
        private const int MaxReportedDiagnosticsPerCode = 6;
        private const int MaxSyntaxTreesPerCompilationBatch = 40;

        public CSharpMathBugAnalysisResult AnalyzeText(string source, CSharpMathBugAnalyzerOptions? options = null, CancellationToken ct = default)
        {
            return AnalyzeProject(new[] { (source, "input.cs") }, options, ct);
        }

        public CSharpMathBugAnalysisResult AnalyzeProject(IEnumerable<(string Content, string FilePath)> files, CSharpMathBugAnalyzerOptions? options = null, CancellationToken ct = default)
        {
            options ??= CSharpMathBugAnalyzerOptions.Default;
            options = options.Normalize();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(options.AnalysisTimeoutSeconds));
            var effectiveCt = timeoutCts.Token;

            // Materialize once to avoid double-reading in lazy enumerables (e.g., SymCLI reads files via Select(File.ReadAllText)).
            var fileList = files as IList<(string Content, string FilePath)> ?? files.ToList();
            var diagnostics = new List<string>();
            var securityFlowModel = ResolveSecurityFlowModel(options, diagnostics);

            if (fileList.Count == 0)
            {
                return new CSharpMathBugAnalysisResult(
                    Findings: Array.Empty<CSharpMathBugFinding>(),
                    Diagnostics: diagnostics.Concat(new[] { "No input C# files were provided to the analyzer." }).ToList(),
                    CandidateCount: 0,
                    LoweredExpressionCount: 0,
                    IsComplete: true);
            }

            var sourceTextByPath = fileList
                .Where(f => !string.IsNullOrWhiteSpace(f.FilePath))
                .GroupBy(f => f.FilePath)
                .ToDictionary(g => g.Key, g => g.Last().Content, StringComparer.OrdinalIgnoreCase);

            // 1. Parse (user inputs only)
            var syntaxTrees = new List<SyntaxTree>(fileList.Count);
            try
            {
                foreach (var (content, filePath) in fileList)
                {
                    effectiveCt.ThrowIfCancellationRequested();
                    var tree = CSharpSyntaxTree.ParseText(
                        content,
                        options: new CSharpParseOptions(kind: SourceCodeKind.Regular),
                        path: filePath,
                        cancellationToken: effectiveCt);
                    syntaxTrees.Add(tree);
                }
            }
            catch (OperationCanceledException)
            {
                return new CSharpMathBugAnalysisResult(
                    Findings: Array.Empty<CSharpMathBugFinding>(),
                    Diagnostics: diagnostics.Concat(new[] { "Analysis cancelled or timed out during parsing." }).ToList(),
                    CandidateCount: 0,
                    LoweredExpressionCount: 0,
                    IsComplete: false);
            }

            // 2. Compile+Lower+Search (batched)
            var references = CreateDefaultReferences();
            var stubTree = CSharpSyntaxTree.ParseText(
                GetCommonTestStubs(),
                options: new CSharpParseOptions(kind: SourceCodeKind.Regular),
                path: "CommonTestStubs.g.cs",
                cancellationToken: effectiveCt);

            var findings = new List<CSharpMathBugFinding>();
            int loweredExpressionCount = 0;
            int totalCandidateCount = 0;

            bool loweredComplete = true;
            bool searchComplete = true;
            bool interproceduralComplete = true;
            var parallelism = options.MaxDegreeOfParallelism;
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = effectiveCt,
                MaxDegreeOfParallelism = parallelism
            };

            bool runInterproceduralSecurityFlow =
                options.SecurityFlowMode == CSharpSecurityFlowMode.InterproceduralIfds ||
                options.SecurityFlowMode == CSharpSecurityFlowMode.InterproceduralIde;
            bool replaceLegacySecuritySinkRules = options.SecurityFlowMode != CSharpSecurityFlowMode.SinkOnly;

            if (runInterproceduralSecurityFlow)
            {
                try
                {
                    var securityCompilation = CSharpCompilation.Create("SymSecurityFlowAnalysis")
                        .AddReferences(references)
                        .AddSyntaxTrees(syntaxTrees)
                        .AddSyntaxTrees(stubTree)
                        // Be explicit about overflow-checking behavior so lowering is stable across environments.
                        // The analyzer's rule library assumes the default C# behavior (unchecked arithmetic unless explicitly in a checked context).
                        .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, checkOverflow: false));

                    var interproceduralAnalyzer = new CSharpInterproceduralSecurityFlowAnalyzer(options.SecurityFlowMode, securityFlowModel);
                    var interproceduralResult = interproceduralAnalyzer.AnalyzeCompilation(
                        securityCompilation,
                        syntaxTrees,
                        options,
                        effectiveCt);

                    if (interproceduralResult.Diagnostics.Count > 0)
                    {
                        diagnostics.AddRange(interproceduralResult.Diagnostics);
                    }

                    if (interproceduralResult.Findings.Count > 0)
                    {
                        findings.AddRange(interproceduralResult.Findings);
                        findings = PruneTopFindings(findings, options.MaxFindings);
                        totalCandidateCount += interproceduralResult.Findings.Count;
                    }
                }
                catch (OperationCanceledException)
                {
                    interproceduralComplete = false;
                    diagnostics.Add("Analysis cancelled or timed out during interprocedural security flow.");
                }
                catch (Exception ex)
                {
                    interproceduralComplete = false;
                    diagnostics.Add($"Interprocedural security flow failed: {ex.Message}");
                }
            }

            try
            {
                for (int batchStart = 0; batchStart < syntaxTrees.Count; batchStart += MaxSyntaxTreesPerCompilationBatch)
                {
                    effectiveCt.ThrowIfCancellationRequested();

                    var batch = syntaxTrees
                        .Skip(batchStart)
                        .Take(MaxSyntaxTreesPerCompilationBatch)
                        .ToList();

                    var compilation = CSharpCompilation.Create("SymAnalysis")
                        .AddReferences(references)
                        .AddSyntaxTrees(batch)
                        .AddSyntaxTrees(stubTree)
                        // Be explicit about overflow-checking behavior so lowering is stable across environments.
                        // The analyzer's rule library assumes the default C# behavior (unchecked arithmetic unless explicitly in a checked context).
                        .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, checkOverflow: false));

                    // Parallel per-tree scan: each tree is lowered and searched independently.
                    // This enables multicore scaling while keeping the EGraph engine single-threaded.
                    var batchDiagnostics = new ConcurrentBag<string>();
                    var batchFindings = new ConcurrentBag<CSharpMathBugFinding>();
                    int batchCandidateCount = 0;
                    int batchLoweredCount = 0;

                    try
                    {
                        Parallel.ForEach(batch, parallelOptions, tree =>
                        {
                            effectiveCt.ThrowIfCancellationRequested();

                            // Safety valve: Roslyn semantic binding + IOperation lowering can become extremely expensive
                            // on very large source files, and in practice may not respond quickly to cancellation.
                            // Skipping these files avoids repository-wide hangs while still producing useful results.
                            const int MaxSemanticLoweringSourceChars = 80_000;
                            if (tree.Length > MaxSemanticLoweringSourceChars)
                            {
                                batchDiagnostics.Add($"Skipping semantic lowering for large file '{tree.FilePath}' ({tree.Length:N0} chars)." );
                                return;
                            }

                            try
                            {
                                var semanticModel = compilation.GetSemanticModel(tree);
                                var lowerer = new CSharpSemanticLowerer();
                                var root = tree.GetRoot(effectiveCt);
                                var exprs = lowerer.Lower(semanticModel, root, effectiveCt);
                                Interlocked.Add(ref batchLoweredCount, exprs.Count);

                                foreach (var diag in semanticModel.GetDiagnostics(cancellationToken: effectiveCt)
                                             .Where(d => d.Severity == DiagnosticSeverity.Error)
                                             .Select(d => d.ToString()))
                                {
                                    batchDiagnostics.Add(diag);
                                }

                                // Search this tree in isolation to keep the EGraph bounded.
                                // Interprocedural security flow (when enabled) runs in a separate project-wide pass.
                                var searcher = new CSharpBugSearchStrategy();
                                var treeFindings = searcher.Search(exprs, options, sourceTextByPath, effectiveCt, semanticModel);

                                // Source/sink flow modes can replace legacy sink-only CSSEC003/CSSEC004 detections.
                                if (replaceLegacySecuritySinkRules)
                                {
                                    treeFindings = treeFindings
                                        .Where(f => f.BugId != "CSSEC003" && f.BugId != "CSSEC004")
                                        .ToList();
                                }

                                if (options.SecurityFlowMode == CSharpSecurityFlowMode.IntraProcedural)
                                {
                                    var flowAnalyzer = new CSharpSecurityFlowAnalyzer(securityFlowModel);
                                    var flowFindings = flowAnalyzer.AnalyzeTree(semanticModel, root, options, effectiveCt);
                                    foreach (var diag in flowAnalyzer.Diagnostics)
                                    {
                                        batchDiagnostics.Add(diag);
                                    }
                                    if (flowFindings.Count > 0)
                                    {
                                        treeFindings.AddRange(flowFindings);
                                    }
                                }

                                Interlocked.Add(ref batchCandidateCount, treeFindings.Count);
                                foreach (var finding in treeFindings)
                                {
                                    batchFindings.Add(finding);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                batchDiagnostics.Add($"Failed to analyze '{tree.FilePath}': {ex.Message}");
                            }
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        searchComplete = false;
                        loweredComplete = false;
                        diagnostics.Add("Analysis cancelled or timed out during per-tree scan." );
                        break;
                    }

                    loweredExpressionCount += batchLoweredCount;
                    totalCandidateCount += batchCandidateCount;
                    diagnostics.AddRange(batchDiagnostics);

                    if (!batchFindings.IsEmpty)
                    {
                        findings.AddRange(batchFindings);
                        findings = PruneTopFindings(findings, options.MaxFindings);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                loweredComplete = false;
                diagnostics.Add("Analysis cancelled or timed out during lowering." );
            }

            var isComplete = loweredComplete && searchComplete && interproceduralComplete && !effectiveCt.IsCancellationRequested;
            if (!isComplete && !diagnostics.Any(d => d.Contains("timed out", StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add("Analysis did not complete (timeout/cancellation). Results may be partial.");
            }

            var normalizedDiagnostics = NormalizeDiagnostics(diagnostics);

            return new CSharpMathBugAnalysisResult(
                findings, 
                normalizedDiagnostics, 
                totalCandidateCount == 0 ? findings.Count : totalCandidateCount,
                loweredExpressionCount,
                isComplete
            );
        }

        private static CSharpSecurityFlowModel ResolveSecurityFlowModel(
            CSharpMathBugAnalyzerOptions options,
            ICollection<string> diagnostics)
        {
            var baseModel = CSharpSecurityFlowModelCatalog.Default;
            if (string.IsNullOrWhiteSpace(options.RepoScanInfoPath))
            {
                return baseModel;
            }

            // RepoScanInfo parsing and merging is centralized here so all flow modes behave consistently.
            var scanInfo = CSharpRepoScanInfoLoader.LoadFromFile(options.RepoScanInfoPath);
            foreach (var diagnostic in scanInfo.Diagnostics)
            {
                diagnostics.Add(diagnostic);
            }

            if (!scanInfo.HasEntries)
            {
                return scanInfo.ReplaceDefaultModel ? CSharpSecurityFlowModel.Empty : baseModel;
            }

            return scanInfo.ReplaceDefaultModel
                ? scanInfo.Model
                : CSharpSecurityFlowModelCatalog.Merge(baseModel, scanInfo.Model);
        }

        private static List<CSharpMathBugFinding> PruneTopFindings(List<CSharpMathBugFinding> findings, int maxFindings)
        {
            if (findings.Count <= maxFindings)
            {
                return findings;
            }

            return findings
                .OrderByDescending(f => f.SecurityRisk)
                .ThenByDescending(f => f.Severity)
                .ThenByDescending(f => f.Confidence)
                .ThenByDescending(f => f.ConfidenceScore)
                .ThenBy(f => f.SourceSpan?.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.SourceSpan?.StartLine ?? 0)
                .Take(maxFindings)
                .ToList();
        }

        private static List<MetadataReference> CreateDefaultReferences()
        {
            var references = new List<MetadataReference>();
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddReference(string path)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !added.Add(path))
                {
                    return;
                }

                references.Add(MetadataReference.CreateFromFile(path));
            }

            // Use the full runtime Trusted Platform Assemblies set when available.
            // This drastically reduces missing-BCL-type diagnostics (e.g., Uri/Json/Concurrent*) when
            // compiling repository source in a standalone analyzer context.
            try
            {
                if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa && !string.IsNullOrWhiteSpace(tpa))
                {
                    foreach (var path in tpa.Split(Path.PathSeparator))
                    {
                        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            AddReference(path);
                        }
                    }
                }
            }
            catch
            {
                // Best-effort: fall back to the minimal set below.
            }

            // Ensure a minimal baseline even if TPA is unavailable.
            AddReference(typeof(object).Assembly.Location);
            AddReference(typeof(Console).Assembly.Location);
            AddReference(typeof(System.Linq.Enumerable).Assembly.Location);

            // Retain the old "sibling DLL" heuristic as a fallback/extra coverage.
            try
            {
                var assemblyDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
                if (assemblyDir is not null)
                {
                    AddReferenceIfExists(references, Path.Combine(assemblyDir, "System.Runtime.dll"));
                    AddReferenceIfExists(references, Path.Combine(assemblyDir, "netstandard.dll"));
                    AddReferenceIfExists(references, Path.Combine(assemblyDir, "System.Threading.Tasks.Parallel.dll"));
                    AddReferenceIfExists(references, Path.Combine(assemblyDir, "System.Collections.dll"));
                    AddReferenceIfExists(references, Path.Combine(assemblyDir, "System.Runtime.Serialization.Formatters.dll"));
                    AddReferenceIfExists(references, Path.Combine(assemblyDir, "System.Diagnostics.Process.dll"));
                    AddReferenceIfExists(references, Path.Combine(assemblyDir, "System.Diagnostics.Debug.dll"));
                    AddReferenceIfExists(references, Path.Combine(assemblyDir, "System.Security.Cryptography.dll"));
                    AddReferenceIfExists(references, Path.Combine(assemblyDir, "System.ComponentModel.Primitives.dll"));
                    AddReferenceIfExists(references, Path.Combine(assemblyDir, "System.Runtime.Serialization.dll"));
                    AddReferenceIfExists(references, Path.Combine(assemblyDir, "System.Linq.Expressions.dll"));
                }
            }
            catch
            {
                // Best-effort: analysis should proceed even if we cannot locate optional framework references.
            }

            return references;
        }

        private static void AddReferenceIfExists(List<MetadataReference> references, string path)
        {
            if (File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        private static IReadOnlyList<string> NormalizeDiagnostics(IReadOnlyList<string> diagnostics)
        {
            if (diagnostics.Count == 0)
            {
                return diagnostics;
            }

            var unique = new List<string>(Math.Min(MaxReportedDiagnostics, diagnostics.Count));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var countByCode = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var diagnostic in diagnostics)
            {
                if (string.IsNullOrWhiteSpace(diagnostic))
                {
                    continue;
                }

                var normalized = diagnostic.Trim();
                var key = CreateDiagnosticFingerprint(normalized);
                if (!seen.Add(key))
                {
                    continue;
                }

                var code = TryExtractDiagnosticCode(normalized);
                if (!string.IsNullOrEmpty(code))
                {
                    countByCode.TryGetValue(code, out var currentCount);
                    if (currentCount >= MaxReportedDiagnosticsPerCode)
                    {
                        continue;
                    }

                    countByCode[code] = currentCount + 1;
                }

                unique.Add(normalized);
                if (unique.Count >= MaxReportedDiagnostics)
                {
                    break;
                }
            }

            return unique;
        }

        private static string CreateDiagnosticFingerprint(string diagnostic)
        {
            // Collapse file/line prefixes so repeated project-wide missing-reference diagnostics
            // do not flood reports while still preserving distinct compiler messages.
            int errorIndex = diagnostic.IndexOf(": error ", StringComparison.OrdinalIgnoreCase);
            int warningIndex = diagnostic.IndexOf(": warning ", StringComparison.OrdinalIgnoreCase);
            int splitIndex = -1;

            if (errorIndex >= 0 && warningIndex >= 0)
            {
                splitIndex = Math.Min(errorIndex, warningIndex);
            }
            else if (errorIndex >= 0)
            {
                splitIndex = errorIndex;
            }
            else if (warningIndex >= 0)
            {
                splitIndex = warningIndex;
            }

            return splitIndex >= 0
                ? diagnostic.Substring(splitIndex + 2).Trim()
                : diagnostic;
        }

        private static string? TryExtractDiagnosticCode(string diagnostic)
        {
            if (diagnostic.Length < 6)
            {
                return null;
            }

            for (int i = 0; i <= diagnostic.Length - 6; i++)
            {
                if (diagnostic[i] != 'C' || diagnostic[i + 1] != 'S')
                {
                    continue;
                }

                if (char.IsDigit(diagnostic[i + 2]) &&
                    char.IsDigit(diagnostic[i + 3]) &&
                    char.IsDigit(diagnostic[i + 4]) &&
                    char.IsDigit(diagnostic[i + 5]))
                {
                    return diagnostic.Substring(i, 6);
                }
            }

            return null;
        }
        
        private string GetCommonTestStubs()
        {
            return @"
public class ParallelizeAttribute : System.Attribute
{
    public ExecutionScope Scope { get; set; }
    public int Workers { get; set; }
}
public enum ExecutionScope { MethodLevel, ClassLevel }

namespace Microsoft.VisualStudio.TestTools.UnitTesting
{
    using System;
    public class TestClassAttribute : Attribute {}
    public class TestMethodAttribute : Attribute {}
    public class TestInitializeAttribute : Attribute {}
    public class TestCleanupAttribute : Attribute {}
    public class ParallelizeAttribute : Attribute { public ExecutionScope Scope { get; set; } }
    public enum ExecutionScope { MethodLevel, ClassLevel }
        public static class Assert
        {
            public static void IsTrue(bool condition, string? message = null) {}
            public static void IsFalse(bool condition, string? message = null) {}
            public static void IsNotNull(object? value, string? message = null) {}
            public static void IsNull(object? value, string? message = null) {}
            public static void AreEqual(object? expected, object? actual, string? message = null) {}
            public static void AreEqual<T>(T expected, T actual, string? message = null) {}
            public static void AreEqual(double expected, double actual, double delta, string? message = null) {}
            public static void AreEqual(decimal expected, decimal actual, decimal delta, string? message = null) {}
            public static void AreNotEqual(double expected, double actual, double delta, string? message = null) {}
            public static void AreNotEqual(decimal expected, decimal actual, decimal delta, string? message = null) {}
            public static void Fail(string? message = null) {}
        }
}

namespace NUnit.Framework
{
    using System;
    public class TestFixtureAttribute : Attribute {}
    public class TestAttribute : Attribute {}
    public class SetUpAttribute : Attribute {}
    public class TearDownAttribute : Attribute {}
    public static class Assert
    {
        public static void IsTrue(bool condition, string? message = null) {}
        public static void IsFalse(bool condition, string? message = null) {}
        public static void NotNull(object? value, string? message = null) {}
        public static void Null(object? value, string? message = null) {}
        public static void AreEqual(object? expected, object? actual, string? message = null) {}
    }
}

namespace Xunit
{
    using System;
    public class FactAttribute : Attribute {}
    public class TheoryAttribute : Attribute {}
    public static class Assert
    {
        public static void True(bool condition) {}
        public static void False(bool condition) {}
        public static void Equal<T>(T expected, T actual) {}
        public static void NotEqual<T>(T expected, T actual) {}
        public static void NotNull(object? @object) {}
        public static void Null(object? @object) {}
    }
}
";
        }
    }
}
