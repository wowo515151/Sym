using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SymSolvers.CSharpAnalysis
{
    public class CSharpSecurityFlowAnalyzer
    {
        private readonly CSharpSecurityFlowModel _model;
        private readonly List<string> _diagnostics = new();

        public IReadOnlyList<string> Diagnostics => _diagnostics;

        public CSharpSecurityFlowAnalyzer()
            : this(CSharpSecurityFlowModelCatalog.Default)
        {
        }

        public CSharpSecurityFlowAnalyzer(CSharpSecurityFlowModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
        }

        public List<CSharpMathBugFinding> AnalyzeTree(
            SemanticModel semanticModel,
            SyntaxNode root,
            CSharpMathBugAnalyzerOptions options,
            CancellationToken ct)
        {
            _diagnostics.Clear();
            var findings = new List<CSharpMathBugFinding>();
            
            // We need to visit methods to perform intra-procedural analysis
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var method in methods)
            {
                ct.ThrowIfCancellationRequested();
                var methodFlow = AnalyzeMethod(method, semanticModel, options, ct);
                findings.AddRange(methodFlow);
            }

            return findings;
        }

        private List<CSharpMathBugFinding> AnalyzeMethod(
            MethodDeclarationSyntax method,
            SemanticModel semanticModel,
            CSharpMathBugAnalyzerOptions options,
            CancellationToken ct)
        {
            var findings = new List<CSharpMathBugFinding>();
            var operation = semanticModel.GetOperation(method, ct);
            if (operation == null) return findings;

            // Basic walker to find sources and track assignments to locals
            // This is a simplified forward flow.
            
            // We need a custom walker that maintains state
            var walker = new SecurityFlowWalker(_model.Sources, _model.Sinks, _model.Sanitizers, options, semanticModel, _diagnostics);
            
            // Treat parameters as tainted for flow analysis in library mode or partial analysis
            var methodSymbol = semanticModel.GetDeclaredSymbol(method, ct) as IMethodSymbol;
            if (methodSymbol != null)
            {
                foreach(var param in methodSymbol.Parameters)
                {
                    walker.MarkParameterAsSource(param);
                }
            }
            
            walker.Visit(operation);

            return walker.Findings;
        }

        private class SecurityFlowWalker : OperationWalker
        {
            private readonly IReadOnlyList<SourceSpec> _sources;
            private readonly IReadOnlyList<SinkSpec> _sinks;
            private readonly IReadOnlyList<SanitizerSpec> _sanitizers;
            private readonly CSharpMathBugAnalyzerOptions _options;
            private readonly SemanticModel _semanticModel;
            private readonly CSharpGuardProver _guardProver;
            private readonly List<CSharpGuardFact> _activeGuards = new();
            private readonly List<string>? _diagnostics;
            
            public List<CSharpMathBugFinding> Findings { get; } = new();
            
            // Taint state
            private readonly Dictionary<ISymbol, TaintTraceStep> _taintedSymbols = new(SymbolEqualityComparer.Default);

            public SecurityFlowWalker(
                IReadOnlyList<SourceSpec> sources,
                IReadOnlyList<SinkSpec> sinks,
                IReadOnlyList<SanitizerSpec> sanitizers,
                CSharpMathBugAnalyzerOptions options,
                SemanticModel semanticModel,
                List<string>? diagnostics)
            {
                _sources = sources;
                _sinks = sinks;
                _sanitizers = sanitizers;
                _options = options;
                _semanticModel = semanticModel;
                _guardProver = new CSharpGuardProver(options);
                _diagnostics = diagnostics;
            }

            public override void VisitVariableDeclarator(IVariableDeclaratorOperation op)
            {
                base.VisitVariableDeclarator(op);
                
                if (op.Initializer != null)
                {
                    CheckAssignment(op.Symbol, op.Initializer.Value);
                }
            }

            public override void VisitSimpleAssignment(ISimpleAssignmentOperation op)
            {
                base.VisitSimpleAssignment(op);

                if (op.Target is ILocalReferenceOperation localRef)
                {
                    CheckAssignment(localRef.Local, op.Value);
                }
                else if (op.Target is IParameterReferenceOperation paramRef)
                {
                    CheckAssignment(paramRef.Parameter, op.Value);
                }
            }

            public override void VisitCompoundAssignment(ICompoundAssignmentOperation op)
            {
                base.VisitCompoundAssignment(op);

                if (op.Target is ILocalReferenceOperation localRef)
                {
                    CheckAssignment(localRef.Local, op.Value);
                }
                else if (op.Target is IParameterReferenceOperation paramRef)
                {
                    CheckAssignment(paramRef.Parameter, op.Value);
                }
            }

            public override void VisitConditional(IConditionalOperation operation)
            {
                Visit(operation.Condition);

                var trueGuards = _guardProver
                    .DeriveBranchFacts(operation.Condition, branchTaken: true, _semanticModel, _diagnostics)
                    .Concat(_guardProver.DeriveAllowlistSanitizerBranchFacts(operation.Condition, branchTaken: true, _semanticModel, _sanitizers, _diagnostics))
                    .ToList();
                var falseGuards = _guardProver
                    .DeriveBranchFacts(operation.Condition, branchTaken: false, _semanticModel, _diagnostics)
                    .Concat(_guardProver.DeriveAllowlistSanitizerBranchFacts(operation.Condition, branchTaken: false, _semanticModel, _sanitizers, _diagnostics))
                    .ToList();

                var baselineTaint = CloneTaintMap();
                var baselineGuards = SnapshotGuards();

                // True branch
                RestoreTaintMap(baselineTaint);
                RestoreGuards(baselineGuards);
                PushGuards(trueGuards);
                if (operation.WhenTrue is not null)
                {
                    Visit(operation.WhenTrue);
                }
                var trueTaint = CloneTaintMap();
                var trueGuardState = SnapshotGuards();

                // False branch (if omitted, use baseline + derived false guards).
                RestoreTaintMap(baselineTaint);
                RestoreGuards(baselineGuards);
                PushGuards(falseGuards);
                if (operation.WhenFalse is not null)
                {
                    Visit(operation.WhenFalse);
                }
                var falseTaint = CloneTaintMap();
                var falseGuardState = SnapshotGuards();

                var trueExits = CSharpSecurityFlowCore.BranchDefinitelyExits(operation.WhenTrue);
                var falseExits = operation.WhenFalse is not null && CSharpSecurityFlowCore.BranchDefinitelyExits(operation.WhenFalse);

                if (trueExits && !falseExits)
                {
                    RestoreTaintMap(falseTaint);
                    RestoreGuards(falseGuardState);
                    return;
                }

                if (falseExits && !trueExits)
                {
                    RestoreTaintMap(trueTaint);
                    RestoreGuards(trueGuardState);
                    return;
                }

                RestoreTaintMap(MergeTaintMaps(trueTaint, falseTaint));
                RestoreGuards(CSharpSecurityFlowCore.IntersectGuards(trueGuardState, falseGuardState));
            }

            public void MarkParameterAsSource(IParameterSymbol param)
            {
                _taintedSymbols[param] = new TaintTraceStep(
                    $"{param.ContainingSymbol.Name}:{param.Name}",
                    $"Source parameter '{param.Name}'",
                    param.Name,
                    Kind: SourceKind.PublicParameter
                );
            }

            private void CheckAssignment(ISymbol target, IOperation value)
            {
                // 1. Is value a direct source?
                var sourceTrace = GetSourceTrace(value);
                if (sourceTrace != null)
                {
                    _taintedSymbols[target] = sourceTrace;
                    return;
                }

                // 2. Is value propagating taint?
                var propagationTrace = GetPropagationTrace(value);
                if (propagationTrace != null)
                {
                     _taintedSymbols[target] = propagationTrace;
                     return;
                }

                // 3. If neither, untaint (re-assignment clears taint in this simple model)
                _taintedSymbols.Remove(target);
            }

            private TaintTraceStep? GetSourceTrace(IOperation? op)
            {
                if (op == null) return null;

                if (op is IConversionOperation conv)
                {
                    return GetSourceTrace(conv.Operand);
                }

                if (op is IInvocationOperation inv)
                {
                    var method = inv.TargetMethod;
                    var typeName = method.ContainingType?.ToDisplayString();
                    var methodName = method.Name;

                    foreach (var source in _sources)
                    {
                        if (CSharpSecurityFlowCore.TypeNameMatches(typeName, source.TypeName) && methodName == source.MethodName && !source.IsProperty)
                        {
                            return new TaintTraceStep(
                                CSharpSecurityFlowCore.GetLocationString(op),
                                $"Source: Call to {typeName}.{methodName}",
                                op.Syntax.ToString(),
                                Kind: source.Kind
                            );
                        }
                    }

                    if (CSharpSecurityFlowCore.LooksLikeAiSource(method))
                    {
                        return new TaintTraceStep(
                            CSharpSecurityFlowCore.GetLocationString(op),
                            $"Source: AI call to {typeName}.{methodName}",
                            op.Syntax.ToString(),
                            Kind: SourceKind.AiSource
                        );
                    }
                }
                else if (op is IPropertyReferenceOperation prop)
                {
                    var propSym = prop.Property;
                    var typeName = propSym.ContainingType?.ToDisplayString();
                    var propName = propSym.MetadataName;

                    foreach (var source in _sources)
                    {
                        if (CSharpSecurityFlowCore.TypeNameMatches(typeName, source.TypeName) && propName == source.MethodName && source.IsProperty) // MethodName used for PropName in spec
                        {
                             return new TaintTraceStep(
                                CSharpSecurityFlowCore.GetLocationString(op),
                                $"Source: Property {typeName}.{propName}",
                                op.Syntax.ToString(),
                                Kind: source.Kind
                            );
                        }
                    }
                }
                
                return null;
            }

            private TaintTraceStep? GetPropagationTrace(IOperation? op)
            {
                if (op == null) return null;

                if (op is IConversionOperation conv)
                {
                    return GetPropagationTrace(conv.Operand);
                }

                // Check locals
                if (op is ILocalReferenceOperation localRef)
                {
                    if (_taintedSymbols.TryGetValue(localRef.Local, out var trace))
                    {
                        return new TaintTraceStep(
                            CSharpSecurityFlowCore.GetLocationString(op),
                            $"Propagated: via local '{localRef.Local.Name}'",
                            op.Syntax.ToString(),
                            trace, // Link to previous step
                            Kind: trace.Kind
                        );
                    }
                }
                else if (op is IParameterReferenceOperation paramRef)
                {
                    if (_taintedSymbols.TryGetValue(paramRef.Parameter, out var trace))
                    {
                        return trace;
                    }
                    return new TaintTraceStep(
                        $"{paramRef.Parameter.ContainingSymbol.Name}:{paramRef.Parameter.Name}",
                        $"Source parameter '{paramRef.Parameter.Name}'",
                        paramRef.Parameter.Name,
                        Kind: SourceKind.PublicParameter
                    );
                }
                else if (op is IInterpolatedStringOperation interp)
                {
                    foreach(var part in interp.Parts)
                    {
                         TaintTraceStep? trace = null;
                         if (part is IInterpolationOperation iop)
                         {
                             trace = GetPropagationTrace(iop.Expression) ?? GetSourceTrace(iop.Expression);
                         }
                         if (trace != null)
                         {
                             return new TaintTraceStep(
                                 CSharpSecurityFlowCore.GetLocationString(op),
                                 "Propagated: Interpolated String",
                                 op.Syntax.ToString(),
                                 trace
                             );
                         }
                    }
                }
                else if (op is IConditionalOperation cond)
                {
                    var trueTrace = GetPropagationTrace(cond.WhenTrue) ?? GetSourceTrace(cond.WhenTrue);
                    if (trueTrace != null) return new TaintTraceStep(CSharpSecurityFlowCore.GetLocationString(op), "Propagated: Conditional (True)", op.Syntax.ToString(), trueTrace);
                    var falseTrace = GetPropagationTrace(cond.WhenFalse) ?? GetSourceTrace(cond.WhenFalse);
                    if (falseTrace != null) return new TaintTraceStep(CSharpSecurityFlowCore.GetLocationString(op), "Propagated: Conditional (False)", op.Syntax.ToString(), falseTrace);
                }
                else if (op is ICoalesceOperation coalesce)
                {
                    var left = GetPropagationTrace(coalesce.Value) ?? GetSourceTrace(coalesce.Value);
                    if (left != null) return new TaintTraceStep(CSharpSecurityFlowCore.GetLocationString(op), "Propagated: Null Coalescing (Left)", op.Syntax.ToString(), left);
                    var right = GetPropagationTrace(coalesce.WhenNull) ?? GetSourceTrace(coalesce.WhenNull);
                    if (right != null) return new TaintTraceStep(CSharpSecurityFlowCore.GetLocationString(op), "Propagated: Null Coalescing (Right)", op.Syntax.ToString(), right);
                }
                else if (op is IBinaryOperation binOp && binOp.OperatorKind == BinaryOperatorKind.Add) // String concat
                {
                    var left = GetPropagationTrace(binOp.LeftOperand) ?? GetSourceTrace(binOp.LeftOperand);
                    if (left != null) return new TaintTraceStep(CSharpSecurityFlowCore.GetLocationString(op), "Propagated: Concat (Left)", op.Syntax.ToString(), left);
                    var right = GetPropagationTrace(binOp.RightOperand) ?? GetSourceTrace(binOp.RightOperand);
                    if (right != null) return new TaintTraceStep(CSharpSecurityFlowCore.GetLocationString(op), "Propagated: Concat (Right)", op.Syntax.ToString(), right);
                }
                else if (op is IInvocationOperation inv)
                {
                    var method = inv.TargetMethod;
                    var typeName = method.ContainingType?.ToDisplayString();

                    // Check for sanitizers
                     if (_sanitizers.Any(s => CSharpSecurityFlowCore.TypeNameMatches(typeName, s.TypeName) && s.MethodName == method.Name && s.ReturnsSanitized))
                     {
                         return null; // Sanitized!
                     }

                     // Propagate String methods
                     if (CSharpSecurityFlowCore.IsStringPropagationMethod(method))
                     {
                         var instanceTrace = inv.Instance is not null
                             ? (GetPropagationTrace(inv.Instance) ?? GetSourceTrace(inv.Instance))
                             : null;
                         if (instanceTrace is not null)
                         {
                             return new TaintTraceStep(
                                 CSharpSecurityFlowCore.GetLocationString(op),
                                 $"Propagated: String.{method.Name} (instance)",
                                 op.Syntax.ToString(),
                                 instanceTrace,
                                 Kind: instanceTrace.Kind);
                         }

                         foreach (var arg in inv.Arguments)
                         {
                             var trace = GetPropagationTrace(arg.Value) ?? GetSourceTrace(arg.Value);
                             if (trace != null)
                             {
                                 return new TaintTraceStep(
                                     CSharpSecurityFlowCore.GetLocationString(op),
                                     $"Propagated: String.{method.Name}",
                                     op.Syntax.ToString(),
                                     trace,
                                     Kind: trace.Kind
                                 );
                             }
                         }
                     }
                }
                else if (op is IObjectCreationOperation creation)
                {
                    var ctor = creation.Constructor;
                    var createdTypeName = ctor?.ContainingType?.ToDisplayString();
                    if (!string.IsNullOrWhiteSpace(createdTypeName) &&
                        (createdTypeName == "System.Uri" || createdTypeName.EndsWith(".Uri", StringComparison.Ordinal) ||
                         createdTypeName == "System.UriBuilder" || createdTypeName.EndsWith(".UriBuilder", StringComparison.Ordinal)))
                    {
                        foreach (var arg in creation.Arguments)
                        {
                            var trace = GetPropagationTrace(arg.Value) ?? GetSourceTrace(arg.Value);
                            if (trace != null)
                            {
                                return new TaintTraceStep(
                                    CSharpSecurityFlowCore.GetLocationString(op),
                                    $"Propagated: new {createdTypeName}(...)",
                                    op.Syntax.ToString(),
                                    trace,
                                    Kind: trace.Kind);
                            }
                        }
                    }
                }
                else if (op is IPropertyReferenceOperation propertyRef)
                {
                    var upstream = GetPropagationTrace(propertyRef.Instance) ?? GetSourceTrace(propertyRef.Instance);
                    if (upstream != null)
                    {
                        return new TaintTraceStep(
                            CSharpSecurityFlowCore.GetLocationString(op),
                            $"Propagated: via property '{propertyRef.Property.Name}'",
                            op.Syntax.ToString(),
                            upstream,
                            Kind: upstream.Kind
                        );
                    }
                }

                return null;
            }

            public override void VisitInvocation(IInvocationOperation op)
            {
                base.VisitInvocation(op);

                var method = op.TargetMethod;
                var typeName = method.ContainingType?.ToDisplayString();
                
                foreach (var sink in _sinks)
                {
                    if (CSharpSecurityFlowCore.TypeNameMatches(typeName, sink.TypeName) && sink.MethodName == method.Name)
                    {
                        // Match by parameter ordinal (robust to named/optional arguments).
                        foreach (var arg in op.Arguments)
                        {
                            var ordinal = arg.Parameter?.Ordinal;
                            if (ordinal is null || !sink.TaintedIndices.Contains(ordinal.Value))
                            {
                                continue;
                            }

                            var trace = GetPropagationTrace(arg.Value);
                            if (trace == null) trace = GetSourceTrace(arg.Value);
                            if (trace is null)
                            {
                                continue;
                            }

                            var protection = _guardProver.GetSinkProtectionStrength(sink.Kind, arg.Value, _activeGuards, _semanticModel);
                            if (protection == CSharpGuardStrength.Strong)
                            {
                                continue;
                            }

                            ReportFinding(op, sink, trace, arg.Value, protection);
                        }
                    }
                }
            }

            public override void VisitObjectCreation(IObjectCreationOperation op)
            {
                base.VisitObjectCreation(op);

                 var method = op.Constructor;
                 if (method == null) return;
                 var typeName = method.ContainingType?.ToDisplayString();

                 foreach (var sink in _sinks)
                 {
                    // For constructors, MethodName usually .ctor, but we might have mapped it differently? 
                    // SinkSpec uses ".ctor" for constructors.
                    if (CSharpSecurityFlowCore.TypeNameMatches(typeName, sink.TypeName) && sink.MethodName == ".ctor")
                    {
                        foreach (var arg in op.Arguments)
                        {
                            var ordinal = arg.Parameter?.Ordinal;
                            if (ordinal is null || !sink.TaintedIndices.Contains(ordinal.Value))
                            {
                                continue;
                            }

                            var trace = GetPropagationTrace(arg.Value);
                            if (trace == null) trace = GetSourceTrace(arg.Value);
                            if (trace is null)
                            {
                                continue;
                            }

                            var protection = _guardProver.GetSinkProtectionStrength(sink.Kind, arg.Value, _activeGuards, _semanticModel);
                            if (protection == CSharpGuardStrength.Strong)
                            {
                                continue;
                            }

                            ReportFinding(op, sink, trace, arg.Value, protection);
                        }
                    }
                 }
            }

            private void ReportFinding(IOperation sinkOp, SinkSpec sink, TaintTraceStep sourceTrace, IOperation argOp, CSharpGuardStrength? protectionStrength)
            {
                string bugId = CSharpSecurityFlowCore.MapBugId(sink.Kind);
                var message = CSharpBugCatalog.GetMessage(bugId, argOp.Syntax.ToString());
                var severity = CSharpSecurityFlowCore.ParseSeverity(CSharpBugCatalog.GetSeverity(bugId));
                var securityRisk = CSharpBugCatalog.GetSecurityRisk(bugId);

                var isExternalSource = CSharpSecurityFlowCore.IsExternalUntrustedSource(sourceTrace.Kind);

                if (_options.PrioritizeUserSources && !isExternalSource)
                {
                    return;
                }

                var confidence = isExternalSource ? CSharpMathBugConfidence.Confirmed : CSharpMathBugConfidence.Medium;
                var confidenceScore = isExternalSource ? 1.0 : 0.5;

                // Medium proof keeps the finding but downgrades its impact.
                if (protectionStrength == CSharpGuardStrength.Medium)
                {
                    confidence = CSharpMathBugConfidence.Medium;
                    confidenceScore = Math.Min(confidenceScore, 0.5);
                    if (severity > CSharpMathBugSeverity.Warning) severity = CSharpMathBugSeverity.Warning;
                    if (securityRisk > CSharpSecurityRisk.Medium) securityRisk = CSharpSecurityRisk.Medium;
                }

                if (!isExternalSource)
                {
                    if (severity > CSharpMathBugSeverity.Warning) severity = CSharpMathBugSeverity.Warning;
                    if (securityRisk > CSharpSecurityRisk.Medium) securityRisk = CSharpSecurityRisk.Medium;
                }

                var location = sinkOp.Syntax.GetLocation();
                var lineSpan = location.GetLineSpan();
                
                var evidence = new List<string>();
                var current = sourceTrace;
                while (current != null)
                {
                    evidence.Add(current.Description + " at " + current.Location);
                    current = current.Previous;
                }
                evidence.Reverse(); // Source first
                
                evidence.Add($"Sink: {sink.Description} at {CSharpSecurityFlowCore.GetLocationString(sinkOp)}");
                evidence = CSharpSecurityFlowCore.TrimEvidence(evidence, _options.SecurityMaxTraceSteps);

                var finding = new CSharpMathBugFinding(
                    BugId: bugId,
                    Title: sink.Kind.ToString(),
                    Severity: severity,
                    SecurityRisk: securityRisk,
                    Confidence: confidence,
                    ConfidenceScore: confidenceScore,
                    Message: message,
                    Suggestion: "Validate and sanitize the input before using it in this sink.",
                    Expression: sinkOp.Syntax.ToString(),
                    SourceSpan: new CSharpMathBugSourceSpan(
                        lineSpan.Path,
                        lineSpan.StartLinePosition.Line + 1,
                        lineSpan.StartLinePosition.Character + 1,
                        lineSpan.EndLinePosition.Line + 1,
                        lineSpan.EndLinePosition.Character + 1
                    ),
                    Evidence: evidence,
                    WitnessAssignments: null
                );

                Findings.Add(finding);
            }

            private Dictionary<ISymbol, TaintTraceStep> CloneTaintMap()

            {
                var clone = new Dictionary<ISymbol, TaintTraceStep>(SymbolEqualityComparer.Default);
                foreach (var kvp in _taintedSymbols)
                {
                    clone[kvp.Key] = kvp.Value;
                }

                return clone;
            }

            private void RestoreTaintMap(IReadOnlyDictionary<ISymbol, TaintTraceStep> snapshot)
            {
                _taintedSymbols.Clear();
                foreach (var kvp in snapshot)
                {
                    _taintedSymbols[kvp.Key] = kvp.Value;
                }
            }

            private Dictionary<ISymbol, TaintTraceStep> MergeTaintMaps(
                IReadOnlyDictionary<ISymbol, TaintTraceStep> left,
                IReadOnlyDictionary<ISymbol, TaintTraceStep> right)
            {
                var merged = new Dictionary<ISymbol, TaintTraceStep>(SymbolEqualityComparer.Default);
                foreach (var kvp in left)
                {
                    merged[kvp.Key] = kvp.Value;
                }

                foreach (var kvp in right)
                {
                    if (!merged.TryGetValue(kvp.Key, out var existing))
                    {
                        merged[kvp.Key] = kvp.Value;
                        continue;
                    }

                    var mergedKind = CSharpSecurityFlowCore.MergeSourceKind(existing.Kind, kvp.Value.Kind);
                    if (mergedKind != existing.Kind)
                    {
                        merged[kvp.Key] = kvp.Value with { Kind = mergedKind };
                    }
                }

                return merged;
            }

            private List<CSharpGuardFact> SnapshotGuards()
            {
                return _activeGuards.ToList();
            }

            private void RestoreGuards(IEnumerable<CSharpGuardFact> snapshot)
            {
                _activeGuards.Clear();
                _activeGuards.AddRange(snapshot);
            }

            private void PushGuards(IEnumerable<CSharpGuardFact> guards)
            {
                foreach (var guard in guards)
                {
                    _activeGuards.Add(guard);
                }
            }

        }
    }
}
