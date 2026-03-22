//Copyright Warren Harding 2025.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using Sym.Operations;
using SymSolvers.EGraphSolver;

namespace SymSolvers
{
    internal static class EGraphTimeoutBudget
    {
        public static (int SaturationTimeoutSeconds, int ExtractionTimeoutSeconds) Resolve(SolveContext context)
        {
            int saturationTimeoutSeconds = context.GetInt(SolverOptionKeys.SaturationTimeoutSeconds, 30);
            int extractionTimeoutSeconds = saturationTimeoutSeconds;

            if (context.AdditionalData is not null &&
                context.AdditionalData.ContainsKey(SolverOptionKeys.ExtractionTimeoutSeconds))
            {
                extractionTimeoutSeconds = context.GetInt(SolverOptionKeys.ExtractionTimeoutSeconds, saturationTimeoutSeconds);
            }

            return (saturationTimeoutSeconds, extractionTimeoutSeconds);
        }
    }

    public class EGraphSolverStrategy : ISolverStrategy
    {
                        public SolveResult Solve(IExpression? problem, SolveContext context)
                        {
                            if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1") 
                                Console.WriteLine($"DEBUG: EGraphSolverStrategy.Solve started for: {problem?.ToDisplayString()} Target: {context.TargetVariable?.ToDisplayString() ?? "null"}");
                            if (problem is null) return SolveResult.Failure(null, "Problem cannot be null.");
                            if (context is null) return SolveResult.Failure(problem, "Context cannot be null.");
                
                            var graph = context.SharedEGraph ?? new EGraph();
                            var trace = context.EnableTracing ? ImmutableList.CreateBuilder<IExpression>() : null;
                            trace?.Add(problem);
                
                            // 1. Ingest Problem
                            if (context.AdditionalData != null && context.AdditionalData.TryGetValue("Attributes", out var attrData) && attrData is Dictionary<string, Dictionary<string, double>> symbolAttributes)
                            {
                                foreach (var symAttr in symbolAttributes)
                                {
                                    int classId = graph.Add(new Symbol(symAttr.Key));
                                    var eClass = graph.GetClass(classId);
                                    foreach (var prop in symAttr.Value) eClass.Metadata[prop.Key] = prop.Value;
                                }
                            }
                            
                            int rootId; 
                            if (problem is Equality eq)
                            {
                                graph.Add(eq);
                                int lhs = graph.Add(eq.LeftOperand);
                                int rhs = graph.Add(eq.RightOperand);
                                graph.Union(lhs, rhs);
                                rootId = (context.TargetVariable != null) ? graph.Add(context.TargetVariable) : lhs;
                            }
                            else if (problem is Vector vec && vec.Arguments.All(a => a is Equality))
                            {
                                graph.Add(vec);
                                foreach (Equality item in vec.Arguments)
                                {
                                    context.ThrowIfCancellationRequested();
                                    graph.Add(item);
                                    int lhs = graph.Add(item.LeftOperand);
                                    int rhs = graph.Add(item.RightOperand);
                                    graph.Union(lhs, rhs);
                                }
                                rootId = (context.TargetVariable != null) ? graph.Add(context.TargetVariable) : graph.Add(problem);
                            }
                            else
                            {
                                rootId = graph.Add(problem);
                            }
                
                            graph.Rebuild();
                            ShapeAnalysis.Analyze(graph, context.CancellationToken);
                
                            // 2. Saturation Loop
                            int iterations = 0;
                            bool changed = true;
                            const int MaxNodes = 5000;
                            var history = new MatchHistory();
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            var timeoutBudget = EGraphTimeoutBudget.Resolve(context);
                            int timeoutSeconds = timeoutBudget.SaturationTimeoutSeconds;
                            int extractionTimeoutSeconds = timeoutBudget.ExtractionTimeoutSeconds;
                            
                            while (changed && iterations < context.MaxIterations)
                            {
                                context.ThrowIfCancellationRequested();
                                if (graph.NodeCount > MaxNodes) break;
                                if (sw.Elapsed.TotalSeconds > timeoutSeconds)
                                {
                                    if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                                        Console.WriteLine($"DEBUG: EGraph Saturation Timeout reached ({timeoutSeconds}s).");
                                    break;
                                }
                                changed = false;
                                iterations++;
                
                                var matches = EGraphMatcher.FindMatches(graph, context.Rules, history, context.MaxConcurrency, context.CancellationToken);
                                var costModel = CostModelFactory.GetCostModel(context);
                                var currentCostFunction = costModel.GetCostFunction(context, graph);
                
                                if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1") Console.WriteLine($"DEBUG: EGraph Iteration {iterations}: matches={matches.Count}");
                                
                                foreach (var match in matches)
                                {
                                    context.ThrowIfCancellationRequested();
                                    int newClassId;
                                    var materializedBindings = ImmutableDictionary.CreateBuilder<string, IExpression>();
                                    CancellationTokenSource? bindingExtractionCts = null;
                                    CancellationToken bindingExtractionToken = context.CancellationToken;
                                    if (extractionTimeoutSeconds > 0)
                                    {
                                        bindingExtractionCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                                        bindingExtractionCts.CancelAfter(TimeSpan.FromSeconds(extractionTimeoutSeconds));
                                        bindingExtractionToken = bindingExtractionCts.Token;
                                    }
                
                                    try
                                    {
                                        foreach (var kvp in match.Bindings)
                                            materializedBindings.Add(
                                                kvp.Key,
                                                EGraphExtract.ExtractBestEffort(
                                                    graph,
                                                    kvp.Value,
                                                    costFunction: currentCostFunction,
                                                    softCt: bindingExtractionToken,
                                                    hardCt: context.CancellationToken));
                                    }
                                    catch (OperationCanceledException) when (bindingExtractionCts != null && bindingExtractionCts.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested)
                                    {
                                        continue;
                                    }
                                    finally
                                    {
                                        bindingExtractionCts?.Dispose();
                                    }
                                    var immutableBindings = materializedBindings.ToImmutable();
                
                                    if (match.Rule.Condition != null && !match.Rule.Condition(immutableBindings)) continue;
                                    if (match.Rule.AssumptionCondition != null && !match.Rule.AssumptionCondition(immutableBindings, context.Assumptions)) continue;
                
                                    if (match.Rule.Transform != null)
                                    {
                                        var transformed = match.Rule.Transform(immutableBindings);
                                        if (transformed == null) continue;
                                        newClassId = graph.Add(transformed.Canonicalize());
                                    }
                                    else
                                    {
                                        newClassId = EGraphInstantiator.Instantiate(graph, match.Rule.Replacement, match.Bindings);
                                    }
                                    
                                    if (graph.Find(match.RootClassId) != graph.Find(newClassId))
                                    {
                                        graph.Union(match.RootClassId, newClassId);
                                        changed = true;
                                    }
                                }
                
                                bool discovered = false;
                                foreach (var classId in graph.GetRootIds())
                                {
                                    var eClass = graph.GetClass(classId);
                                    foreach (var node in eClass.Nodes)
                                    {
                                        if (node.Head == "Equality" && node.Children.Count == 2)
                                        {
                                            int lhs = node.Children[0];
                                            int rhs = node.Children[1];
                                            if (graph.Find(lhs) != graph.Find(rhs))
                                            {
                                                graph.Union(lhs, rhs);
                                                changed = true;
                                                discovered = true;
                                            }
                                        }
                                    }
                                }
                
                                if (discovered || ResolveInequalities(graph, context))
                                {
                                    graph.Rebuild(context.CancellationToken);
                                    ShapeAnalysis.Analyze(graph, context.CancellationToken);
                                    changed = true;
                                    if (CheckContradiction(graph)) return SolveResult.Failure(problem, "Contradiction found in EGraph.");
                                }
                
                                if (changed && !discovered)
                                {
                                    graph.Rebuild(context.CancellationToken);
                                    ShapeAnalysis.Analyze(graph, context.CancellationToken);
                                    if (CheckContradiction(graph)) return SolveResult.Failure(problem, "Contradiction found in EGraph.");
                                }
                            }
                
                            // 3. Extraction
                            ShapeAnalysis.Analyze(graph, context.CancellationToken);
                            var baseCostModel = CostModelFactory.GetCostModel(context);
                            int targetClassId = (context.TargetVariable != null) ? graph.Find(graph.Add(context.TargetVariable)) : -1;
                            var finalCostModel = new TargetPenaltyCostModel(baseCostModel, targetClassId);
                            var costFunction = finalCostModel.GetCostFunction(context, graph);
                
                            bool isEquationProblem = problem is Equality || (problem is Vector v && v.Arguments.All(a => a is Equality));
                            CancellationToken extractionToken = context.CancellationToken;
                            CancellationTokenSource? extractionCts = null;
                            if (extractionTimeoutSeconds > 0)
                            {
                                extractionCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                                extractionCts.CancelAfter(TimeSpan.FromSeconds(extractionTimeoutSeconds));
                                extractionToken = extractionCts.Token;
                            }
                
                            try
                            {
                                if (context.TargetVariable != null && isEquationProblem)
                                {
                                    string targetHead = ENode.GetHead(context.TargetVariable);
                                    int targetId = graph.Find(graph.Add(context.TargetVariable));
                                    var filter = new AvoidSymbolFilter(targetHead);
                                    var bestExpr = EGraphExtract.ExtractBestEffort(graph, targetId, filter.Accept, costFunction, extractionToken, context.CancellationToken);
                                    if (bestExpr.InternalEquals(context.TargetVariable))
                                        bestExpr = EGraphExtract.ExtractBestEffort(graph, targetId, null, costFunction, extractionToken, context.CancellationToken);
                                    
                                    var resultEq = new Equality(context.TargetVariable, bestExpr).Canonicalize();
                                    trace?.Add(resultEq);
                                    return SolveResult.Success(resultEq, $"Solved via EGraph in {iterations} iterations.", trace?.ToImmutable());
                                }
                
                                if (problem is Vector vec && vec.Arguments.All(a => a is Equality))
                                {
                                    var results = new List<IExpression>();
                                    foreach (var arg in vec.Arguments)
                                    {
                                        if (arg is Equality argEq)
                                        {
                                            var bl = (argEq.LeftOperand is Symbol) ? argEq.LeftOperand : 
                                                     EGraphExtract.ExtractBestEffort(graph, graph.Find(graph.Add(argEq.LeftOperand)), null, costFunction, extractionToken, context.CancellationToken).Canonicalize();
                                            string? lh = (bl is Symbol s) ? ENode.GetHead(s) : null;
                                            var br = EGraphExtract.ExtractBestEffort(graph, graph.Find(graph.Add(argEq.RightOperand)), new AvoidSymbolFilter(lh).Accept, costFunction, extractionToken, context.CancellationToken).Canonicalize();
                                            results.Add(new Equality(bl, br).Canonicalize());
                                        }
                                        else results.Add(arg);
                                    }
                                    var resultVec = new Vector(results.ToImmutableList());
                                    trace?.Add(resultVec);
                                    return SolveResult.Success(resultVec, $"Simplified system of equations in {iterations} iterations.", trace?.ToImmutable());
                                }
                
                                if (problem is Equality eqProblem)
                                {
                                    var bestLhs = (eqProblem.LeftOperand is Symbol) ? eqProblem.LeftOperand : 
                                                  EGraphExtract.ExtractBestEffort(graph, graph.Find(graph.Add(eqProblem.LeftOperand)), null, costFunction, extractionToken, context.CancellationToken).Canonicalize();
                                    string? lhsHead = (bestLhs is Symbol sym) ? ENode.GetHead(sym) : null;
                                    var bestRhs = EGraphExtract.ExtractBestEffort(graph, graph.Find(graph.Add(eqProblem.RightOperand)), new AvoidSymbolFilter(lhsHead).Accept, costFunction, extractionToken, context.CancellationToken).Canonicalize();
                                    var resultEq = new Equality(bestLhs, bestRhs).Canonicalize();
                                    trace?.Add(resultEq);
                                    return SolveResult.Success(resultEq, $"Simplified equality via EGraph in {iterations} iterations.", trace?.ToImmutable());
                                }
                                else
                                {
                                    var bestExpr = EGraphExtract.ExtractBestEffort(graph, rootId, null, costFunction, extractionToken, context.CancellationToken).Canonicalize();
                                    if (bestExpr.InternalEquals(problem)) return SolveResult.Failure(problem, "EGraph could not simplify the expression.");
                                    trace?.Add(bestExpr);
                                    return SolveResult.Success(bestExpr, $"Simplified via EGraph in {iterations} iterations.", trace?.ToImmutable());
                                }
                            }
                            catch (OperationCanceledException) when (extractionCts != null && extractionCts.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested)
                            {
                                return SolveResult.Failure(problem, $"EGraph extraction timeout reached ({extractionTimeoutSeconds}s) before any result could be reconstructed.");
                            }
                            catch (Exception ex)
                            {
                                if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1") Console.WriteLine($"DEBUG: Unexpected exception in Solve: {ex}");
                                return SolveResult.Failure(problem, $"Unexpected error: {ex.Message}");
                            }
                            finally
                            {
                                extractionCts?.Dispose();
                            }
                        }

        private bool ResolveInequalities(EGraph graph, SolveContext context)
        {
            bool changed = false;
            int trueId = graph.Add(new Symbol("true"));
            int falseId = graph.Add(new Symbol("false"));

            IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>? symbolAttributes = null;
            if (context.AdditionalData != null && context.AdditionalData.TryGetValue("Attributes", out var attrObj))
            {
                symbolAttributes = attrObj as IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>;
                if (symbolAttributes == null && attrObj is Dictionary<string, Dictionary<string, double>> dict)
                {
                    var builder = new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in dict) builder[kvp.Key] = kvp.Value;
                    symbolAttributes = builder;
                }
            }

            var unionsToPerform = new List<(int ClassId, int TargetId)>();

            foreach (var classId in graph.GetRootIds())
            {
                var eClass = graph.GetClass(classId);
                if (classId == trueId || classId == falseId) continue;

                foreach (var node in eClass.Nodes)
                {
                    var head = node.Head.StartsWith("Func:", StringComparison.OrdinalIgnoreCase) ? node.Head.Substring(5) : node.Head;
                    if (head == "gt" || head == "lt" || head == "ge" || head == "le" || 
                        head == "and" || head == "or" || head == "not" || head == "eq" || head == "ne")
                    {
                        var assignments = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                        // Also include metadata from the class itself as a fallback/legacy
                        foreach(var prop in eClass.Metadata) assignments[prop.Key] = (decimal)prop.Value;
                        
                        var childrenExprs = new List<IExpression>();
                        bool allExtractable = true;
                        foreach(var cid in node.Children)
                        {
                            try { childrenExprs.Add(EGraphExtract.ExtractBest(graph, cid, ct: context.CancellationToken)); }
                            catch { allExtractable = false; break; }
                        }
                        if (!allExtractable) continue;

                        var conditionExpr = ENode.CreateExpression(head, childrenExprs.ToImmutableList());
                        if (NumericEvaluator.TryEvaluateCondition(conditionExpr, assignments, out bool result, out _, symbolAttributes: symbolAttributes))
                        {
                            int targetId = result ? trueId : falseId;
                            if (graph.Find(classId) != graph.Find(targetId))
                            {
                                unionsToPerform.Add((classId, targetId));
                            }
                        }
                    }
                }
            }

            foreach (var (cid, tid) in unionsToPerform)
            {
                if (graph.Find(cid) != graph.Find(tid))
                {
                    graph.Union(cid, tid);
                    changed = true;
                }
            }

            return changed;
        }

        private bool CheckContradiction(EGraph graph)
        {
            foreach (var id in graph.GetRootIds())
            {
                var eClass = graph.GetClass(id);
                decimal? firstNum = null;
                foreach (var node in eClass.Nodes)
                {
                    if (node.Head.StartsWith("Num:", StringComparison.Ordinal))
                    {
                        if (decimal.TryParse(node.Head.Substring(4), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal currentNum))
                        {
                            if (firstNum != null)
                            {
                                if (Math.Abs(firstNum.Value - currentNum) > 1e-9m) return true;
                            }
                            else firstNum = currentNum;
                        }
                    }
                }
            }
            return false;
        }
    }
}
