// Copyright Warren Harding 2026

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SymSolvers.CSharpAnalysis
{
    internal sealed record CSharpSecurityFlowAnalysisResult(
        IReadOnlyList<CSharpMathBugFinding> Findings,
        IReadOnlyList<string> Diagnostics);

    internal sealed class CSharpInterproceduralSecurityFlowAnalyzer
    {
        private readonly CSharpSecurityFlowMode _mode;
        private readonly CSharpSecurityFlowModel _model;

        public CSharpInterproceduralSecurityFlowAnalyzer(
            CSharpSecurityFlowMode mode,
            CSharpSecurityFlowModel? model = null)
        {
            if (mode != CSharpSecurityFlowMode.InterproceduralIfds &&
                mode != CSharpSecurityFlowMode.InterproceduralIde)
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported mode for interprocedural analyzer.");
            }

            _mode = mode;
            _model = model ?? CSharpSecurityFlowModelCatalog.Default;
        }

        public CSharpSecurityFlowAnalysisResult AnalyzeCompilation(
            CSharpCompilation compilation,
            IReadOnlyList<SyntaxTree> syntaxTrees,
            CSharpMathBugAnalyzerOptions options,
            CancellationToken ct)
        {
            var diagnostics = new List<string>();
            var methods = DiscoverMethods(compilation, syntaxTrees, diagnostics, ct);
            if (methods.Count == 0)
            {
                return new CSharpSecurityFlowAnalysisResult(Array.Empty<CSharpMathBugFinding>(), diagnostics);
            }

            var summaries = SolveSummaries(methods, options, diagnostics, ct);
            var findings = MaterializeFindings(summaries, options);
            return new CSharpSecurityFlowAnalysisResult(findings, diagnostics);
        }

        private Dictionary<IMethodSymbol, MethodBodyContext> DiscoverMethods(
            CSharpCompilation compilation,
            IReadOnlyList<SyntaxTree> syntaxTrees,
            List<string> diagnostics,
            CancellationToken ct)
        {
            var methods = new Dictionary<IMethodSymbol, MethodBodyContext>(SymbolEqualityComparer.Default);

            foreach (var tree in syntaxTrees)
            {
                ct.ThrowIfCancellationRequested();
                SemanticModel semanticModel;
                SyntaxNode root;
                try
                {
                    semanticModel = compilation.GetSemanticModel(tree);
                    root = tree.GetRoot(ct);
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"Interprocedural flow skipped '{tree.FilePath}': {ex.Message}");
                    continue;
                }

                var methodNodes = root.DescendantNodes().Where(static n =>
                    n is BaseMethodDeclarationSyntax ||
                    n is LocalFunctionStatementSyntax);

                foreach (var methodNode in methodNodes)
                {
                    ct.ThrowIfCancellationRequested();
                    if (semanticModel.GetDeclaredSymbol(methodNode, ct) is not IMethodSymbol methodSymbol)
                    {
                        continue;
                    }

                    var operation = semanticModel.GetOperation(methodNode, ct);
                    if (operation is null)
                    {
                        continue;
                    }

                    var canonical = methodSymbol.OriginalDefinition;
                    methods[canonical] = new MethodBodyContext(canonical, operation, semanticModel);
                }
            }

            return methods;
        }

        private Dictionary<IMethodSymbol, MethodSummary> SolveSummaries(
            IReadOnlyDictionary<IMethodSymbol, MethodBodyContext> methods,
            CSharpMathBugAnalyzerOptions options,
            List<string> diagnostics,
            CancellationToken ct)
        {
            var comparer = SymbolEqualityComparer.Default;
            var current = new Dictionary<IMethodSymbol, MethodSummary>(comparer);
            foreach (var method in methods.Keys)
            {
                current[method] = MethodSummary.Empty;
            }

            var maxIterations = Math.Clamp(options.MaxSaturationIterations, 2, 128);
            var stabilized = false;

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                ct.ThrowIfCancellationRequested();
                var next = new Dictionary<IMethodSymbol, MethodSummary>(comparer);
                var changed = false;

                foreach (var context in methods.Values)
                {
                    ct.ThrowIfCancellationRequested();
                    var builder = new MethodSummaryBuilder(context, current, _model, options, _mode, diagnostics);
                    var summary = builder.Build();
                    next[context.Symbol] = summary;

                    if (!summary.SemanticallyEquals(current[context.Symbol]))
                    {
                        changed = true;
                    }
                }

                current = next;
                if (!changed)
                {
                    stabilized = true;
                    break;
                }
            }

            if (!stabilized)
            {
                diagnostics.Add("Interprocedural security flow reached iteration budget. Results may be partial.");
            }

            return current;
        }
        private List<CSharpMathBugFinding> MaterializeFindings(
            IReadOnlyDictionary<IMethodSymbol, MethodSummary> summaries,
            CSharpMathBugAnalyzerOptions options)
        {
            var findings = new List<CSharpMathBugFinding>();
            var confidence = _mode == CSharpSecurityFlowMode.InterproceduralIde
                ? CSharpMathBugConfidence.Confirmed
                : CSharpMathBugConfidence.High;
            var confidenceScore = _mode == CSharpSecurityFlowMode.InterproceduralIde ? 1.0 : 0.85;

            foreach (var summary in summaries.Values)
            {
                foreach (var sink in summary.SinkDependencies)
                {
                    // Prioritize findings rooted in external untrusted sources (User/Ai).
                    var isExternalSource = sink.DependsOnSource &&
                                           CSharpSecurityFlowCore.IsExternalUntrustedSource(sink.MaxSourceKind);

                    if (options.PrioritizeUserSources && !isExternalSource)
                    {
                        continue;
                    }

                    // External untrusted sources get Confirmed status immediately.
                    // Otherwise we keep mode-default confidence and downgrade.
                    var confidenceForFinding = isExternalSource ? CSharpMathBugConfidence.Confirmed : confidence;
                    var scoreForFinding = isExternalSource ? 1.0 : confidenceScore;

                    var severity = ParseSeverity(CSharpBugCatalog.GetSeverity(sink.BugId));
                    var risk = CSharpBugCatalog.GetSecurityRisk(sink.BugId);
                    
                    if (!isExternalSource)
                    {
                        // Downgrade confidence and severity if not rooted in external user/AI input.
                        confidenceForFinding = CSharpMathBugConfidence.Medium;
                        scoreForFinding = 0.5;
                        if (severity > CSharpMathBugSeverity.Warning) severity = CSharpMathBugSeverity.Warning;
                        if (risk > CSharpSecurityRisk.Medium) risk = CSharpSecurityRisk.Medium;
                    }

                    var message = CSharpBugCatalog.GetMessage(sink.BugId, sink.Expression);
                    var evidence = TrimEvidence(sink.Evidence, options.SecurityMaxTraceSteps);

                    findings.Add(new CSharpMathBugFinding(
                        BugId: sink.BugId,
                        Title: sink.SinkDescription,
                        Severity: severity,
                        SecurityRisk: risk,
                        Confidence: confidenceForFinding,
                        ConfidenceScore: scoreForFinding,
                        Message: message,
                        Suggestion: "Validate and sanitize the input before using it in this sink.",
                        Expression: sink.Expression,
                        SourceSpan: sink.SourceSpan,
                        Evidence: evidence,
                        WitnessAssignments: null));
                }
            }

            return findings
                .GroupBy(ComposeFindingIdentityKey, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderByDescending(f => f.SecurityRisk)
                .ThenByDescending(f => f.Severity)
                .ThenByDescending(f => f.ConfidenceScore)
                .ThenBy(f => f.SourceSpan?.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.SourceSpan?.StartLine ?? 0)
                .ToList();
        }

        private static string ComposeFindingIdentityKey(CSharpMathBugFinding finding)
        {
            var span = finding.SourceSpan;
            var path = (span?.FilePath ?? string.Empty).ToUpperInvariant();
            var line = span?.StartLine ?? 0;
            var column = span?.StartColumn ?? 0;
            return $"{finding.BugId}|{path}|{line}|{column}|{finding.Expression}";
        }

        private static List<string> TrimEvidence(IReadOnlyList<string> evidence, int maxSteps) => CSharpSecurityFlowCore.TrimEvidence(evidence, maxSteps);

        private static CSharpMathBugSeverity ParseSeverity(string severity) => CSharpSecurityFlowCore.ParseSeverity(severity);

        private sealed record MethodBodyContext(
            IMethodSymbol Symbol,
            IOperation RootOperation,
            SemanticModel SemanticModel);

        private sealed record MethodSummary(
            ImmutableArray<int> ReturnParameterIndices,
            bool ReturnFromSource,
            SourceKind MaxReturnSourceKind,
            ImmutableArray<SinkDependencySummary> SinkDependencies)
        {
            public static MethodSummary Empty { get; } = new(
                ImmutableArray<int>.Empty,
                false,
                SourceKind.PublicParameter,
                ImmutableArray<SinkDependencySummary>.Empty);

            public bool SemanticallyEquals(MethodSummary other)
            {
                if (ReturnFromSource != other.ReturnFromSource || MaxReturnSourceKind != other.MaxReturnSourceKind)
                {
                    return false;
                }

                if (!ReturnParameterIndices.SequenceEqual(other.ReturnParameterIndices))
                {
                    return false;
                }

                if (SinkDependencies.Length != other.SinkDependencies.Length)
                {
                    return false;
                }

                for (int i = 0; i < SinkDependencies.Length; i++)
                {
                    var left = SinkDependencies[i];
                    var right = other.SinkDependencies[i];
                    if (!string.Equals(left.BugId, right.BugId, StringComparison.Ordinal) ||
                        !string.Equals(left.SinkDescription, right.SinkDescription, StringComparison.Ordinal) ||
                        !Equals(left.SourceSpan, right.SourceSpan) ||
                        !string.Equals(left.Expression, right.Expression, StringComparison.Ordinal) ||
                        left.DependsOnSource != right.DependsOnSource ||
                        left.MaxSourceKind != right.MaxSourceKind ||
                        !left.ParameterDependencies.SequenceEqual(right.ParameterDependencies) ||
                        !left.Evidence.SequenceEqual(right.Evidence) ||
                        !left.RequiredGuards.SequenceEqual(right.RequiredGuards))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private sealed record SinkDependencySummary(
            string BugId,
            string SinkDescription,
            CSharpMathBugSourceSpan SourceSpan,
            string Expression,
            ImmutableArray<int> ParameterDependencies,
            bool DependsOnSource,
            SourceKind MaxSourceKind,
            ImmutableArray<string> Evidence,
            ImmutableArray<CSharpGuardFact> RequiredGuards);

        private readonly record struct SinkGuardRequirement(
            bool IsProtected,
            ImmutableArray<CSharpGuardFact> RequiredGuards)
        {
            public static SinkGuardRequirement Protected { get; } =
                new(true, ImmutableArray<CSharpGuardFact>.Empty);
        }

        private sealed record TaintLabel(
            ImmutableArray<int> ParameterIndices,
            bool HasSource,
            SourceKind MaxSourceKind,
            ImmutableArray<string> Evidence)
        {
            public static TaintLabel None { get; } = new(
                ImmutableArray<int>.Empty,
                false,
                SourceKind.PublicParameter,
                ImmutableArray<string>.Empty);

            public bool IsTainted => HasSource || ParameterIndices.Length > 0;

            public TaintLabel Merge(TaintLabel other, int maxEvidence)
            {
                if (!other.IsTainted)
                {
                    return this;
                }

                if (!IsTainted)
                {
                    return other;
                }

                return new TaintLabel(
                    ParameterIndices.Concat(other.ParameterIndices).Distinct().OrderBy(i => i).ToImmutableArray(),
                    HasSource || other.HasSource,
                    (HasSource && other.HasSource)
                        ? CSharpSecurityFlowCore.MergeSourceKind(MaxSourceKind, other.MaxSourceKind)
                        : (HasSource ? MaxSourceKind : other.MaxSourceKind),
                    MergeEvidence(Evidence, other.Evidence, maxEvidence));
            }

            public TaintLabel WithSourceStep(string step, int maxEvidence, SourceKind kind = SourceKind.UserSource)
            {
                var newKind = HasSource ? CSharpSecurityFlowCore.MergeSourceKind(MaxSourceKind, kind) : kind;
                if (string.IsNullOrWhiteSpace(step))
                {
                    return this with { HasSource = true, MaxSourceKind = newKind };
                }

                return new TaintLabel(ParameterIndices, true, newKind, MergeEvidence(Evidence, ImmutableArray.Create(step), maxEvidence));
            }

            public TaintLabel WithPropagationStep(string step, int maxEvidence)
            {
                if (string.IsNullOrWhiteSpace(step))
                {
                    return this;
                }

                return new TaintLabel(ParameterIndices, HasSource, MaxSourceKind, MergeEvidence(Evidence, ImmutableArray.Create(step), maxEvidence));
            }

            public static TaintLabel FromParameter(int parameterIndex, string parameterName, string location)
            {
                var step = $"Propagated: parameter '{parameterName}' at {location}";
                return new TaintLabel(ImmutableArray.Create(parameterIndex), false, SourceKind.PublicParameter, ImmutableArray.Create(step));
            }

            private static ImmutableArray<string> MergeEvidence(
                ImmutableArray<string> left,
                ImmutableArray<string> right,
                int maxEvidence)
            {
                var merged = left.Concat(right)
                    .Where(static s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (maxEvidence > 0 && merged.Count > maxEvidence)
                {
                    merged = merged.Take(maxEvidence + 1).ToList();
                }

                return merged.ToImmutableArray();
            }
        }
        private sealed class MethodSummaryBuilder : OperationWalker
        {
            private readonly MethodBodyContext _context;
            private readonly IReadOnlyDictionary<IMethodSymbol, MethodSummary> _summaries;
            private readonly CSharpSecurityFlowModel _model;
            private readonly CSharpMathBugAnalyzerOptions _options;
            private readonly bool _ideMode;
            private readonly CSharpGuardProver _guardProver;
            private readonly List<string> _diagnostics;
            private readonly Dictionary<ISymbol, TaintLabel> _taintBySymbol = new(SymbolEqualityComparer.Default);
            private readonly Dictionary<IParameterSymbol, int> _parameterIndexBySymbol = new(SymbolEqualityComparer.Default);
            private readonly List<SinkDependencySummary> _sinkDependencies = new();
            private readonly List<CSharpGuardFact> _activeGuards = new();
            private TaintLabel _returnLabel = TaintLabel.None;

            public MethodSummaryBuilder(
                MethodBodyContext context,
                IReadOnlyDictionary<IMethodSymbol, MethodSummary> summaries,
                CSharpSecurityFlowModel model,
                CSharpMathBugAnalyzerOptions options,
                CSharpSecurityFlowMode mode,
                List<string> diagnostics)
            {
                _context = context;
                _summaries = summaries;
                _model = model;
                _options = options;
                _ideMode = mode == CSharpSecurityFlowMode.InterproceduralIde;
                _guardProver = new CSharpGuardProver(options);
                _diagnostics = diagnostics;

                foreach (var parameter in context.Symbol.Parameters)
                {
                    _parameterIndexBySymbol[parameter] = parameter.Ordinal;
                    _taintBySymbol[parameter] = TaintLabel.FromParameter(
                        parameter.Ordinal,
                        parameter.Name,
                        CSharpSecurityFlowCore.GetLocationString(context.RootOperation));
                }
            }

            public MethodSummary Build()
            {
                Visit(_context.RootOperation);
                return new MethodSummary(
                    _returnLabel.ParameterIndices,
                    _returnLabel.HasSource,
                    _returnLabel.MaxSourceKind,
                    DeduplicateSinkDependencies(_sinkDependencies));
            }

            public override void VisitVariableDeclarator(IVariableDeclaratorOperation operation)
            {
                base.VisitVariableDeclarator(operation);
                if (operation.Initializer is null)
                {
                    return;
                }

                var value = Evaluate(operation.Initializer.Value);
                Assign(operation.Symbol, value);
            }

            public override void VisitSimpleAssignment(ISimpleAssignmentOperation operation)
            {
                base.VisitSimpleAssignment(operation);
                var value = Evaluate(operation.Value);
                AssignFromTarget(operation.Target, value);
            }

            public override void VisitCompoundAssignment(ICompoundAssignmentOperation operation)
            {
                base.VisitCompoundAssignment(operation);
                var value = Evaluate(operation.Value);
                AssignFromTarget(operation.Target, value);
            }

            public override void VisitInvocation(IInvocationOperation operation)
            {
                base.VisitInvocation(operation);
                _ = EvaluateInvocation(operation);
            }

            public override void VisitObjectCreation(IObjectCreationOperation operation)
            {
                base.VisitObjectCreation(operation);
                _ = EvaluateObjectCreation(operation);
            }

            public override void VisitReturn(IReturnOperation operation)
            {
                base.VisitReturn(operation);
                var value = Evaluate(operation.ReturnedValue);
                _returnLabel = _returnLabel.Merge(value, _options.SecurityMaxTraceSteps);
            }

            public override void VisitIsPattern(IIsPatternOperation operation)
            {
                base.VisitIsPattern(operation);

                if (operation.Pattern is not IDeclarationPatternOperation declaration || declaration.DeclaredSymbol is null)
                {
                    return;
                }

                var valueLabel = Evaluate(operation.Value);
                if (!valueLabel.IsTainted)
                {
                    return;
                }

                Assign(
                    declaration.DeclaredSymbol,
                    valueLabel.WithPropagationStep(
                        $"Propagated: pattern variable '{declaration.DeclaredSymbol.Name}' at {CSharpSecurityFlowCore.GetLocationString(operation)}",
                        _options.SecurityMaxTraceSteps));
            }

            public override void VisitConditional(IConditionalOperation operation)
            {
                Visit(operation.Condition);

                var trueGuards = _guardProver
                    .DeriveBranchFacts(operation.Condition, branchTaken: true, _context.SemanticModel, _diagnostics)
                    .Concat(_guardProver.DeriveAllowlistSanitizerBranchFacts(operation.Condition, branchTaken: true, _context.SemanticModel, _model.Sanitizers, _diagnostics))
                    .ToList();
                var falseGuards = _guardProver
                    .DeriveBranchFacts(operation.Condition, branchTaken: false, _context.SemanticModel, _diagnostics)
                    .Concat(_guardProver.DeriveAllowlistSanitizerBranchFacts(operation.Condition, branchTaken: false, _context.SemanticModel, _model.Sanitizers, _diagnostics))
                    .ToList();

                var baselineTaint = CloneTaintMap();
                var baselineGuards = SnapshotGuards();
                var baselineReturn = _returnLabel;
                var baselineSinkCount = _sinkDependencies.Count;

                // True branch.
                RestoreTaintMap(baselineTaint);
                RestoreGuards(baselineGuards);
                _returnLabel = baselineReturn;
                TrimSinkDependencies(baselineSinkCount);
                PushGuards(trueGuards);
                if (operation.WhenTrue is not null)
                {
                    Visit(operation.WhenTrue);
                }
                var trueTaint = CloneTaintMap();
                var trueGuardsEnd = SnapshotGuards();
                var trueReturn = _returnLabel;
                var trueSinks = SnapshotSinkDependencies(baselineSinkCount);

                // False branch (or implicit continuation branch when else is omitted).
                RestoreTaintMap(baselineTaint);
                RestoreGuards(baselineGuards);
                _returnLabel = baselineReturn;
                TrimSinkDependencies(baselineSinkCount);
                PushGuards(falseGuards);
                if (operation.WhenFalse is not null)
                {
                    Visit(operation.WhenFalse);
                }
                var falseTaint = CloneTaintMap();
                var falseGuardsEnd = SnapshotGuards();
                var falseReturn = _returnLabel;
                var falseSinks = SnapshotSinkDependencies(baselineSinkCount);

                var trueExits = CSharpSecurityFlowCore.BranchDefinitelyExits(operation.WhenTrue);
                var falseExits = operation.WhenFalse is not null && CSharpSecurityFlowCore.BranchDefinitelyExits(operation.WhenFalse);

                TrimSinkDependencies(baselineSinkCount);
                _sinkDependencies.AddRange(trueSinks);
                _sinkDependencies.AddRange(falseSinks);
                _returnLabel = baselineReturn
                    .Merge(trueReturn, _options.SecurityMaxTraceSteps)
                    .Merge(falseReturn, _options.SecurityMaxTraceSteps);

                if (trueExits && !falseExits)
                {
                    RestoreTaintMap(falseTaint);
                    RestoreGuards(falseGuardsEnd);
                    return;
                }

                if (falseExits && !trueExits)
                {
                    RestoreTaintMap(trueTaint);
                    RestoreGuards(trueGuardsEnd);
                    return;
                }

                RestoreTaintMap(MergeTaintMaps(trueTaint, falseTaint));
                RestoreGuards(CSharpSecurityFlowCore.IntersectGuards(trueGuardsEnd, falseGuardsEnd));
            }

            private void AssignFromTarget(IOperation target, TaintLabel value)
            {
                if (target is ILocalReferenceOperation localRef)
                {
                    Assign(localRef.Local, value);
                    return;
                }

                if (target is IParameterReferenceOperation parameterRef)
                {
                    Assign(parameterRef.Parameter, value);
                }
            }

            private void Assign(ISymbol symbol, TaintLabel value)
            {
                if (!value.IsTainted)
                {
                    _taintBySymbol.Remove(symbol);
                    return;
                }

                _taintBySymbol[symbol] = value;
            }

            private Dictionary<ISymbol, TaintLabel> CloneTaintMap()
            {
                var clone = new Dictionary<ISymbol, TaintLabel>(SymbolEqualityComparer.Default);
                foreach (var kvp in _taintBySymbol)
                {
                    clone[kvp.Key] = kvp.Value;
                }

                return clone;
            }

            private void RestoreTaintMap(IReadOnlyDictionary<ISymbol, TaintLabel> snapshot)
            {
                _taintBySymbol.Clear();
                foreach (var kvp in snapshot)
                {
                    _taintBySymbol[kvp.Key] = kvp.Value;
                }
            }

            private Dictionary<ISymbol, TaintLabel> MergeTaintMaps(
                IReadOnlyDictionary<ISymbol, TaintLabel> left,
                IReadOnlyDictionary<ISymbol, TaintLabel> right)
            {
                var merged = new Dictionary<ISymbol, TaintLabel>(SymbolEqualityComparer.Default);
                foreach (var kvp in left)
                {
                    merged[kvp.Key] = kvp.Value;
                }

                foreach (var kvp in right)
                {
                    if (merged.TryGetValue(kvp.Key, out var existing))
                    {
                        merged[kvp.Key] = existing.Merge(kvp.Value, _options.SecurityMaxTraceSteps);
                    }
                    else
                    {
                        merged[kvp.Key] = kvp.Value;
                    }
                }

                return merged;
            }

            private List<CSharpGuardFact> SnapshotGuards()
            {
                return _activeGuards.ToList();
            }

            private void RestoreGuards(IEnumerable<CSharpGuardFact> guards)
            {
                _activeGuards.Clear();
                _activeGuards.AddRange(guards);
            }

            private void PushGuards(IEnumerable<CSharpGuardFact> guards)
            {
                _activeGuards.AddRange(guards);
            }

            private List<SinkDependencySummary> SnapshotSinkDependencies(int fromIndex)
            {
                if (fromIndex < 0 || fromIndex > _sinkDependencies.Count)
                {
                    return new List<SinkDependencySummary>();
                }

                return _sinkDependencies.Skip(fromIndex).ToList();
            }

            private void TrimSinkDependencies(int keepCount)
            {
                if (keepCount < 0)
                {
                    keepCount = 0;
                }

                if (keepCount >= _sinkDependencies.Count)
                {
                    return;
                }

                _sinkDependencies.RemoveRange(keepCount, _sinkDependencies.Count - keepCount);
            }

            private TaintLabel Evaluate(IOperation? operation)
            {
                if (operation is null)
                {
                    return TaintLabel.None;
                }

                switch (operation)
                {
                    case IConversionOperation conversion:
                        return Evaluate(conversion.Operand);

                    case ILocalReferenceOperation localRef:
                        return _taintBySymbol.TryGetValue(localRef.Local, out var localLabel) ? localLabel : TaintLabel.None;

                    case IParameterReferenceOperation parameterRef:
                        if (_taintBySymbol.TryGetValue(parameterRef.Parameter, out var parameterLabel))
                        {
                            return parameterLabel;
                        }

                        if (_parameterIndexBySymbol.TryGetValue(parameterRef.Parameter, out var parameterIndex))
                        {
                            return TaintLabel.FromParameter(parameterIndex, parameterRef.Parameter.Name, CSharpSecurityFlowCore.GetLocationString(operation));
                        }

                        return TaintLabel.None;

                    case IInvocationOperation invocation:
                        return EvaluateInvocation(invocation);

                    case IObjectCreationOperation objectCreation:
                        return EvaluateObjectCreation(objectCreation);

                    case IInterpolatedStringOperation interpolated:
                    {
                        var label = TaintLabel.None;
                        foreach (var part in interpolated.Parts)
                        {
                            if (part is IInterpolationOperation interpolation)
                            {
                                label = label.Merge(Evaluate(interpolation.Expression), _options.SecurityMaxTraceSteps);
                            }
                        }

                        return label.WithPropagationStep("Propagated: interpolated string", _options.SecurityMaxTraceSteps);
                    }

                    case IConditionalOperation conditional:
                    {
                        var whenTrue = Evaluate(conditional.WhenTrue);
                        var whenFalse = Evaluate(conditional.WhenFalse);
                        return whenTrue.Merge(whenFalse, _options.SecurityMaxTraceSteps)
                            .WithPropagationStep("Propagated: conditional expression", _options.SecurityMaxTraceSteps);
                    }

                    case ICoalesceOperation coalesce:
                    {
                        var left = Evaluate(coalesce.Value);
                        var right = Evaluate(coalesce.WhenNull);
                        return left.Merge(right, _options.SecurityMaxTraceSteps)
                            .WithPropagationStep("Propagated: null coalescing expression", _options.SecurityMaxTraceSteps);
                    }

                    case IBinaryOperation binary when binary.OperatorKind == BinaryOperatorKind.Add:
                    {
                        var left = Evaluate(binary.LeftOperand);
                        var right = Evaluate(binary.RightOperand);
                        return left.Merge(right, _options.SecurityMaxTraceSteps)
                            .WithPropagationStep("Propagated: string concatenation", _options.SecurityMaxTraceSteps);
                    }

                    case IPropertyReferenceOperation property:
                        return EvaluatePropertySource(property);
                }

                var aggregate = TaintLabel.None;
                foreach (var child in operation.ChildOperations)
                {
                    aggregate = aggregate.Merge(Evaluate(child), _options.SecurityMaxTraceSteps);
                }

                return aggregate;
            }

            private TaintLabel EvaluatePropertySource(IPropertyReferenceOperation property)
            {
                var propSym = property.Property;
                var typeName = propSym.ContainingType?.ToDisplayString();
                var propName = propSym.MetadataName;

                foreach (var source in _model.Sources)
                {
                    if (source.IsProperty &&
                        CSharpSecurityFlowCore.TypeNameMatches(typeName, source.TypeName) &&
                        string.Equals(propName, source.MethodName, StringComparison.Ordinal))
                    {
                        var step = $"Source: Property {typeName}.{propName} at {CSharpSecurityFlowCore.GetLocationString(property)}";
                        return TaintLabel.None.WithSourceStep(step, _options.SecurityMaxTraceSteps, source.Kind);
                    }
                }

                // Preserve taint when reading members from tainted objects (e.g., llmReply.Content).
                if (property.Instance is not null)
                {
                    var instanceLabel = Evaluate(property.Instance);
                    if (instanceLabel.IsTainted)
                    {
                        return instanceLabel.WithPropagationStep(
                            $"Propagated: property '{propName}'",
                            _options.SecurityMaxTraceSteps);
                    }
                }

                return TaintLabel.None;
            }
            private TaintLabel EvaluateInvocation(IInvocationOperation invocation)
            {
                if (TryCreateSourceFromInvocation(invocation, out var sourceLabel))
                {
                    return sourceLabel;
                }

                var argumentLabelsByOrdinal = BuildArgumentLabelMap(invocation.Arguments);
                CheckInvocationSink(invocation, argumentLabelsByOrdinal);

                PropagateOutArguments(invocation, argumentLabelsByOrdinal);

                if (IsSanitizer(invocation.TargetMethod))
                {
                    return TaintLabel.None;
                }

                if (IsStringPropagationMethod(invocation.TargetMethod))
                {
                    var stringLabel = MergeArgumentLabels(argumentLabelsByOrdinal.Values);
                    if (invocation.Instance is not null)
                    {
                        stringLabel = stringLabel.Merge(Evaluate(invocation.Instance), _options.SecurityMaxTraceSteps);
                    }
                    return stringLabel.WithPropagationStep(
                        $"Propagated: String.{invocation.TargetMethod.Name}",
                        _options.SecurityMaxTraceSteps);
                }

                if (TryGetSummary(invocation.TargetMethod, out var calleeSummary))
                {
                    PropagateCalleeSinkDependencies(invocation, calleeSummary, argumentLabelsByOrdinal);

                    var returnLabel = TaintLabel.None;
                    foreach (var parameterIndex in calleeSummary.ReturnParameterIndices)
                    {
                        if (argumentLabelsByOrdinal.TryGetValue(parameterIndex, out var argLabel))
                        {
                            returnLabel = returnLabel.Merge(argLabel, _options.SecurityMaxTraceSteps);
                        }
                    }

                    if (calleeSummary.ReturnFromSource)
                    {
                        returnLabel = returnLabel.WithSourceStep(
                            $"Source: call to {invocation.TargetMethod.ToDisplayString()} returns tainted data at {CSharpSecurityFlowCore.GetLocationString(invocation)}",
                            _options.SecurityMaxTraceSteps,
                            calleeSummary.MaxReturnSourceKind);
                    }

                    if (returnLabel.IsTainted)
                    {
                        returnLabel = returnLabel.WithPropagationStep(
                            $"Propagated: call {invocation.TargetMethod.ToDisplayString()} at {CSharpSecurityFlowCore.GetLocationString(invocation)}",
                            _options.SecurityMaxTraceSteps);
                    }

                    return returnLabel;
                }

                // Fallback propagation for common value-building helpers when summaries are unavailable
                // (e.g., external libraries, partial compilations). Keep it narrow to avoid over-tainting.
                var returnType = invocation.TargetMethod.ReturnType;
                var returnsString = returnType.SpecialType == SpecialType.System_String;
                var returnTypeName = returnType.ToDisplayString();
                var returnsUriLike = returnTypeName == "System.Uri" || returnTypeName.EndsWith(".Uri", StringComparison.Ordinal) ||
                                     returnTypeName == "System.UriBuilder" || returnTypeName.EndsWith(".UriBuilder", StringComparison.Ordinal);
                if (returnsString || returnsUriLike)
                {
                    var fallback = MergeArgumentLabels(argumentLabelsByOrdinal.Values);
                    if (invocation.Instance is not null)
                    {
                        fallback = fallback.Merge(Evaluate(invocation.Instance), _options.SecurityMaxTraceSteps);
                    }

                    if (fallback.IsTainted)
                    {
                        return fallback.WithPropagationStep(
                            $"Propagated: call {invocation.TargetMethod.Name} returns {returnTypeName} at {CSharpSecurityFlowCore.GetLocationString(invocation)}",
                            _options.SecurityMaxTraceSteps);
                    }
                }

                return TaintLabel.None;
            }

            private void PropagateOutArguments(IInvocationOperation invocation, IReadOnlyDictionary<int, TaintLabel> argumentLabelsByOrdinal)
            {
                var method = invocation.TargetMethod;
                if (!LooksLikeDictionaryTryGetValue(method))
                {
                    return;
                }

                // Dictionary.TryGetValue(key, out value): treat the out value as tainted when the receiver (or key) is tainted.
                var receiverLabel = invocation.Instance is not null ? Evaluate(invocation.Instance) : TaintLabel.None;
                var label = receiverLabel;
                if (argumentLabelsByOrdinal.TryGetValue(0, out var keyLabel))
                {
                    label = label.Merge(keyLabel, _options.SecurityMaxTraceSteps);
                }

                if (!label.IsTainted)
                {
                    return;
                }

                label = label.WithPropagationStep(
                    $"Propagated: {method.Name}(out) at {CSharpSecurityFlowCore.GetLocationString(invocation)}",
                    _options.SecurityMaxTraceSteps);

                // Assign taint to the out parameter target.
                var outArg = invocation.Arguments.FirstOrDefault(a => a.Parameter?.RefKind is RefKind.Out or RefKind.Ref);
                if (outArg is null)
                {
                    return;
                }

                if (TryGetByRefArgumentTargetSymbol(outArg.Value, _context.SemanticModel, ct: default, out var symbol))
                {
                    Assign(symbol, label);
                }
            }

            private static bool TryGetByRefArgumentTargetSymbol(
                IOperation argumentValue,
                SemanticModel semanticModel,
                CancellationToken ct,
                [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ISymbol? symbol)
            {
                switch (argumentValue)
                {
                    case ILocalReferenceOperation localRef:
                        symbol = localRef.Local;
                        return true;

                    case IParameterReferenceOperation parameterRef:
                        symbol = parameterRef.Parameter;
                        return true;

                    case IDeclarationExpressionOperation decl:
                    {
                        if (decl.Syntax is DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax single })
                        {
                            symbol = semanticModel.GetDeclaredSymbol(single, ct);
                            return symbol is not null;
                        }

                        break;
                    }
                }

                symbol = null;
                return false;
            }

            private static bool LooksLikeDictionaryTryGetValue(IMethodSymbol method)
            {
                if (!string.Equals(method.Name, "TryGetValue", StringComparison.Ordinal))
                {
                    return false;
                }

                if (method.Parameters.Length < 2)
                {
                    return false;
                }

                if (method.Parameters[1].RefKind != RefKind.Out)
                {
                    return false;
                }

                var type = method.ContainingType;
                if (type is null)
                {
                    return false;
                }

                if (type.Name is "Dictionary" or "IDictionary" or "IReadOnlyDictionary")
                {
                    return true;
                }

                foreach (var iface in type.AllInterfaces)
                {
                    if (iface.Name is "IDictionary" or "IReadOnlyDictionary")
                    {
                        return true;
                    }
                }

                return false;
            }

            private TaintLabel EvaluateObjectCreation(IObjectCreationOperation creation)
            {
                if (creation.Constructor is null)
                {
                    return TaintLabel.None;
                }

                var argumentLabelsByOrdinal = BuildArgumentLabelMap(creation.Arguments);
                CheckObjectCreationSink(creation, argumentLabelsByOrdinal);

                var ctorTypeName = creation.Constructor.ContainingType?.ToDisplayString();
                if (string.IsNullOrWhiteSpace(ctorTypeName))
                {
                    return TaintLabel.None;
                }

                // Propagate taint into URL-like objects so downstream sinks can observe it.
                // Example: new UriBuilder(url) { Query = ... }.Uri
                if (ctorTypeName == "System.Uri" || ctorTypeName.EndsWith(".Uri", StringComparison.Ordinal) ||
                    ctorTypeName == "System.UriBuilder" || ctorTypeName.EndsWith(".UriBuilder", StringComparison.Ordinal))
                {
                    var label = MergeArgumentLabels(argumentLabelsByOrdinal.Values);
                    if (creation.Initializer is not null)
                    {
                        label = label.Merge(Evaluate(creation.Initializer), _options.SecurityMaxTraceSteps);
                    }

                    if (label.IsTainted)
                    {
                        return label.WithPropagationStep(
                            $"Propagated: new {ctorTypeName}(...)",
                            _options.SecurityMaxTraceSteps);
                    }
                }

                return TaintLabel.None;
            }

            private Dictionary<int, TaintLabel> BuildArgumentLabelMap(ImmutableArray<IArgumentOperation> arguments)
            {
                var map = new Dictionary<int, TaintLabel>();
                foreach (var argument in arguments)
                {
                    var ordinal = argument.Parameter?.Ordinal;
                    if (ordinal is null || ordinal.Value < 0)
                    {
                        continue;
                    }

                    var label = Evaluate(argument.Value);
                    if (map.TryGetValue(ordinal.Value, out var existing))
                    {
                        map[ordinal.Value] = existing.Merge(label, _options.SecurityMaxTraceSteps);
                    }
                    else
                    {
                        map[ordinal.Value] = label;
                    }
                }

                return map;
            }

            private void CheckInvocationSink(IInvocationOperation invocation, IReadOnlyDictionary<int, TaintLabel> argumentLabelsByOrdinal)
            {
                var method = invocation.TargetMethod;
                var typeName = method.ContainingType?.ToDisplayString();

                foreach (var sink in _model.Sinks)
                {
                    if (!CSharpSecurityFlowCore.TypeNameMatches(typeName, sink.TypeName) ||
                        !string.Equals(sink.MethodName, method.Name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var label = MergeTrackedIndices(argumentLabelsByOrdinal, sink.TaintedIndices);
                    if (!label.IsTainted)
                    {
                        continue;
                    }

                    var guardRequirement = GetGuardRequirement(
                        sink.Kind,
                        invocation.Arguments,
                        sink.TaintedIndices,
                        argumentLabelsByOrdinal);

                    if (guardRequirement.IsProtected)
                    {
                        continue;
                    }

                    AddSinkDependency(
                        bugId: MapBugId(sink.Kind),
                        sinkDescription: sink.Description,
                        sinkExpression: invocation.Syntax.ToString(),
                        sinkOperation: invocation,
                        label: label,
                        extraEvidence: default,
                        requiredGuards: guardRequirement.RequiredGuards);
                }
            }

            private void CheckObjectCreationSink(IObjectCreationOperation creation, IReadOnlyDictionary<int, TaintLabel> argumentLabelsByOrdinal)
            {
                if (creation.Constructor is null)
                {
                    return;
                }

                var typeName = creation.Constructor.ContainingType?.ToDisplayString();
                foreach (var sink in _model.Sinks)
                {
                    if (!CSharpSecurityFlowCore.TypeNameMatches(typeName, sink.TypeName) ||
                        !string.Equals(sink.MethodName, ".ctor", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var label = MergeTrackedIndices(argumentLabelsByOrdinal, sink.TaintedIndices);
                    if (!label.IsTainted)
                    {
                        continue;
                    }

                    var guardRequirement = GetGuardRequirement(
                        sink.Kind,
                        creation.Arguments,
                        sink.TaintedIndices,
                        argumentLabelsByOrdinal);

                    if (guardRequirement.IsProtected)
                    {
                        continue;
                    }

                    AddSinkDependency(
                        bugId: MapBugId(sink.Kind),
                        sinkDescription: sink.Description,
                        sinkExpression: creation.Syntax.ToString(),
                        sinkOperation: creation,
                        label: label,
                        extraEvidence: default,
                        requiredGuards: guardRequirement.RequiredGuards);
                }
            }

            private void PropagateCalleeSinkDependencies(
                IInvocationOperation invocation,
                MethodSummary calleeSummary,
                IReadOnlyDictionary<int, TaintLabel> argumentLabelsByOrdinal)
            {
                foreach (var calleeSink in calleeSummary.SinkDependencies)
                {
                    var mappedLabel = MergeTrackedIndices(argumentLabelsByOrdinal, calleeSink.ParameterDependencies);
                    if (calleeSink.DependsOnSource)
                    {
                        mappedLabel = mappedLabel.WithSourceStep(
                            $"Source: propagated through call to {invocation.TargetMethod.ToDisplayString()}",
                            _options.SecurityMaxTraceSteps,
                            calleeSink.MaxSourceKind);
                    }

                    if (!mappedLabel.IsTainted)
                    {
                        continue;
                    }

                    if (TryMapBugIdToSinkKind(calleeSink.BugId, out var sinkKind))
                    {
                        var satisfiedLocally = true;
                        foreach (var requiredGuard in calleeSink.RequiredGuards)
                        {
                            if (requiredGuard.Subject.StartsWith("arg:", StringComparison.Ordinal) &&
                                int.TryParse(requiredGuard.Subject.Substring(4), out var argIndex))
                            {
                                var arg = FindInvocationArgumentByOrdinal(invocation, argIndex);
                                if (arg is null ||
                                    !_guardProver.IsSinkProtected(sinkKind, arg.Value, _activeGuards, _context.SemanticModel))
                                {
                                    satisfiedLocally = false;
                                    break;
                                }
                            }
                        }

                        if (satisfiedLocally && calleeSink.RequiredGuards.Length > 0)
                        {
                            continue;
                        }
                    }

                    var callStep = $"Propagated: sink '{calleeSink.SinkDescription}' reachable via {invocation.TargetMethod.ToDisplayString()}";
                    AddSinkDependency(
                        bugId: calleeSink.BugId,
                        sinkDescription: calleeSink.SinkDescription,
                        sinkExpression: invocation.Syntax.ToString(),
                        sinkOperation: invocation,
                        label: mappedLabel,
                        extraEvidence: ImmutableArray.Create(callStep),
                        requiredGuards: calleeSink.RequiredGuards);
                }
            }

            private static IArgumentOperation? FindInvocationArgumentByOrdinal(IInvocationOperation invocation, int parameterOrdinal)
            {
                foreach (var argument in invocation.Arguments)
                {
                    if (argument.Parameter?.Ordinal == parameterOrdinal)
                    {
                        return argument;
                    }
                }

                return null;
            }

            private SinkGuardRequirement GetGuardRequirement(
                SecurityFlowKind sinkKind,
                ImmutableArray<IArgumentOperation> arguments,
                IEnumerable<int> trackedIndices,
                IReadOnlyDictionary<int, TaintLabel> argumentLabelsByOrdinal)
            {
                var tracked = trackedIndices.ToHashSet();
                var requiredGuards = ImmutableArray.CreateBuilder<CSharpGuardFact>();
                var isProtected = true;
                var hasTrackedTaintedArgument = false;
                var hasRequiredGuardKind = CSharpSecurityFlowCore.TryGetRequiredGuardKind(sinkKind, out var requiredKind);

                foreach (var argument in arguments)
                {
                    var ordinal = argument.Parameter?.Ordinal;
                    if (ordinal is null || !tracked.Contains(ordinal.Value))
                    {
                        continue;
                    }

                    if (!argumentLabelsByOrdinal.TryGetValue(ordinal.Value, out var label) || !label.IsTainted)
                    {
                        continue;
                    }

                    hasTrackedTaintedArgument = true;
                    if (!_guardProver.IsSinkProtected(sinkKind, argument.Value, _activeGuards, _context.SemanticModel))
                    {
                        isProtected = false;
                        if (hasRequiredGuardKind)
                        {
                            requiredGuards.Add(new CSharpGuardFact(requiredKind, $"arg:{ordinal.Value}", CSharpGuardStrength.Strong, "Required by callee sink"));
                        }
                    }
                }

                if (!hasTrackedTaintedArgument || isProtected)
                {
                    return SinkGuardRequirement.Protected;
                }

                return new SinkGuardRequirement(
                    IsProtected: false,
                    RequiredGuards: requiredGuards.ToImmutable());
            }

            private static bool TryMapBugIdToSinkKind(string bugId, out SecurityFlowKind kind) =>
                CSharpSecurityFlowCore.TryMapBugIdToSinkKind(bugId, out kind);

            private void AddSinkDependency(
                string bugId,
                string sinkDescription,
                string sinkExpression,
                IOperation sinkOperation,
                TaintLabel label,
                ImmutableArray<string> extraEvidence,
                ImmutableArray<CSharpGuardFact> requiredGuards)
            {
                if (!label.IsTainted)
                {
                    return;
                }

                var lineSpan = sinkOperation.Syntax.GetLocation().GetLineSpan();
                var sourceSpan = new CSharpMathBugSourceSpan(
                    lineSpan.Path,
                    lineSpan.StartLinePosition.Line + 1,
                    lineSpan.StartLinePosition.Character + 1,
                    lineSpan.EndLinePosition.Line + 1,
                    lineSpan.EndLinePosition.Character + 1);

                var evidence = label.Evidence;
                if (!extraEvidence.IsDefaultOrEmpty)
                {
                    evidence = evidence.Concat(extraEvidence)
                        .Distinct(StringComparer.Ordinal)
                        .Take(Math.Max(1, _options.SecurityMaxTraceSteps) + 1)
                        .ToImmutableArray();
                }

                var sinkStep = $"Sink: {sinkDescription} at {CSharpSecurityFlowCore.GetLocationString(sinkOperation)}";
                evidence = evidence.Concat(new[] { sinkStep })
                    .Distinct(StringComparer.Ordinal)
                    .Take(Math.Max(1, _options.SecurityMaxTraceSteps) + 1)
                    .ToImmutableArray();

                _sinkDependencies.Add(new SinkDependencySummary(
                    bugId,
                    sinkDescription,
                    sourceSpan,
                    sinkExpression,
                    label.ParameterIndices,
                    label.HasSource,
                    label.MaxSourceKind,
                    evidence,
                    requiredGuards));
            }

            private bool TryCreateSourceFromInvocation(IInvocationOperation invocation, out TaintLabel label)
            {
                var method = invocation.TargetMethod;
                var typeName = method.ContainingType?.ToDisplayString();
                var methodName = method.Name;

                foreach (var source in _model.Sources)
                {
                    if (source.IsProperty)
                    {
                        continue;
                    }

                    if (!CSharpSecurityFlowCore.TypeNameMatches(typeName, source.TypeName) ||
                        !string.Equals(methodName, source.MethodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var step = $"Source: Call to {typeName}.{methodName} at {CSharpSecurityFlowCore.GetLocationString(invocation)}";
                    label = TaintLabel.None.WithSourceStep(step, _options.SecurityMaxTraceSteps, source.Kind);
                    return true;
                }

                if (CSharpSecurityFlowCore.LooksLikeAiSource(method))
                {
                    var step = $"Source: AI call to {typeName}.{methodName} at {CSharpSecurityFlowCore.GetLocationString(invocation)}";
                    label = TaintLabel.None.WithSourceStep(step, _options.SecurityMaxTraceSteps, SourceKind.AiSource);
                    return true;
                }

                label = TaintLabel.None;
                return false;
            }
            private bool IsSanitizer(IMethodSymbol method)
            {
                return CSharpSecurityFlowCore.IsSanitizer(method, _model.Sanitizers);
            }

            private bool TryGetSummary(IMethodSymbol method, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MethodSummary? summary)
            {
                if (_summaries.TryGetValue(method, out summary))
                {
                    return true;
                }

                var original = method.OriginalDefinition;
                if (_summaries.TryGetValue(original, out summary))
                {
                    return true;
                }

                if (method.ReducedFrom is not null && _summaries.TryGetValue(method.ReducedFrom, out summary))
                {
                    return true;
                }

                summary = MethodSummary.Empty;
                return false;
            }

            private static bool IsStringPropagationMethod(IMethodSymbol method)
            {
                return CSharpSecurityFlowCore.IsStringPropagationMethod(method);
            }

            private static string MapBugId(SecurityFlowKind kind) => CSharpSecurityFlowCore.MapBugId(kind);

            private TaintLabel MergeArgumentLabels(IEnumerable<TaintLabel> labels)
            {
                var merged = TaintLabel.None;
                foreach (var label in labels)
                {
                    merged = merged.Merge(label, _options.SecurityMaxTraceSteps);
                }

                if (_ideMode && merged.IsTainted)
                {
                    merged = merged.WithPropagationStep("Propagated: IDE value lattice merge", _options.SecurityMaxTraceSteps);
                }

                return merged;
            }

            private TaintLabel MergeTrackedIndices(IReadOnlyDictionary<int, TaintLabel> labels, IEnumerable<int> indices)
            {
                var merged = TaintLabel.None;
                foreach (var index in indices)
                {
                    if (labels.TryGetValue(index, out var label))
                    {
                        merged = merged.Merge(label, _options.SecurityMaxTraceSteps);
                    }
                }

                return merged;
            }

            private ImmutableArray<SinkDependencySummary> DeduplicateSinkDependencies(IEnumerable<SinkDependencySummary> sinks)
            {
                return sinks
                    .GroupBy(ComposeSinkKey, StringComparer.Ordinal)
                    .Select(g =>
                    {
                        var item = g.First();
                        var evidence = g.SelectMany(x => x.Evidence)
                            .Distinct(StringComparer.Ordinal)
                            .Take(Math.Max(1, _options.SecurityMaxTraceSteps) + 1)
                            .ToImmutableArray();

                        return item with { Evidence = evidence };
                    })
                    .OrderBy(s => s.BugId, StringComparer.Ordinal)
                    .ThenBy(s => s.SourceSpan.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(s => s.SourceSpan.StartLine)
                    .ThenBy(s => s.SourceSpan.StartColumn)
                    .ToImmutableArray();
            }

            private static string ComposeSinkKey(SinkDependencySummary sink)
            {
                var deps = sink.ParameterDependencies.Length == 0
                    ? string.Empty
                    : string.Join(",", sink.ParameterDependencies);
                return $"{sink.BugId}|{sink.SourceSpan.FilePath}|{sink.SourceSpan.StartLine}|{sink.SourceSpan.StartColumn}|{sink.Expression}|{deps}|{sink.DependsOnSource}|{sink.MaxSourceKind}";
            }

        }
    }
}
