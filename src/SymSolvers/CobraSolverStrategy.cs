using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using Sym.Operations;
using SymCobra.Core;
using SymCobra.Regions;
using SymCobra.Runtime;
using SymCobra.Telemetry;
using SymSolvers.EGraphSolver;

namespace SymSolvers;

public sealed class CobraSolverStrategy : ISolverStrategy
{
    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        return SolveCore(problem, context, new CobraDiagnostics());
    }

    internal SolveResult SolveWithDiagnostics(IExpression? problem, SolveContext context, out CobraDiagnostics diagnostics)
    {
        diagnostics = new CobraDiagnostics();
        return SolveCore(problem, context, diagnostics);
    }

    private SolveResult SolveCore(IExpression? problem, SolveContext context, CobraDiagnostics diagnostics)
    {
        if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
        {
            Console.WriteLine($"DEBUG: CobraSolverStrategy.Solve started for: {problem?.ToDisplayString()} Target: {context.TargetVariable?.ToDisplayString() ?? "null"}");
        }

        if (problem is null) return SolveResult.Failure(null, "Problem cannot be null.");
        if (context is null) return SolveResult.Failure(problem, "Context cannot be null.");

        var runtime = CobraRuntimeInfo.Detect();
        var graph = context.SharedEGraph ?? new EGraph();
        var cobraGraphState = new CobraGraphState();
        var fallbackPolicy = new CobraFallbackPolicy();
        var phaseCoordinator = new CobraPhaseCoordinator(cobraGraphState, diagnostics, fallbackPolicy);

        var trace = context.EnableTracing ? ImmutableList.CreateBuilder<IExpression>() : null;
        trace?.Add(problem);

        SeedAttributes(graph, context);

        int rootId = IngestProblem(problem, context, graph);

        graph.Rebuild(context.CancellationToken);
        cobraGraphState.SyncFromLegacyGraph(graph);
        diagnostics.RecordSync(CobraSyncDirection.LegacyToCobra, CobraSyncReason.InitialGraphAuthority, isFullSync: true);
        CobraCudaNative.TryWarmGraphCaches(cobraGraphState);
        var initialSnapshot = CobraPlannerSnapshot.Create(cobraGraphState);
        var initialAnalysisPlan = CobraAnalysisPreparationPlanner.Build(cobraGraphState, initialSnapshot, null);
        if (ShouldExecuteAnalysis(initialAnalysisPlan.OrderedClassIds))
        {
            phaseCoordinator.ExecutePhase(CobraPhase.Analysis, () =>
            {
                ShapeAnalysis.Analyze(graph, initialAnalysisPlan.OrderedClassIds, context.CancellationToken);
                CobraShapeAnalysis.Analyze(cobraGraphState, initialAnalysisPlan.OrderedClassIds, context.CancellationToken);
            });
        }
        bool analysisIsCurrent = true;

        int iterations = 0;
        bool changed = true;
        const int MaxNodes = 5000;
        var history = new MatchHistory();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var timeoutBudget = EGraphTimeoutBudget.Resolve(context);
        int timeoutSeconds = timeoutBudget.SaturationTimeoutSeconds;
        int extractionTimeoutSeconds = timeoutBudget.ExtractionTimeoutSeconds;

        var ruleMetadataByRule = new Dictionary<Rule, CobraRulePatternMetadata>();
        var allRules = context.Rules.ToList();

        while (changed && iterations < context.MaxIterations)
        {
            context.ThrowIfCancellationRequested();
            if (graph.NodeCount > MaxNodes) break;
            if (sw.Elapsed.TotalSeconds > timeoutSeconds)
            {
                if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                {
                    Console.WriteLine($"DEBUG: COBRA Saturation Timeout reached ({timeoutSeconds}s).");
                }
                break;
            }

            changed = false;
            iterations++;

            var iterationSnapshot = CobraPlannerSnapshot.Create(cobraGraphState);
            var allNodeCounts = iterationSnapshot.NodeCounts;
            var allGenerations = iterationSnapshot.Generations;

            CobraRegionPlan? regionPlan = null;
            phaseCoordinator.ExecutePhase(CobraPhase.RegionDiscovery, () => {
                regionPlan = CobraConflictResolver.BuildPlan(CobraRegionDetector.Detect(graph, iterationSnapshot, context.CancellationToken));
            });
            var activeRegionPlan = regionPlan ?? throw new InvalidOperationException("COBRA region discovery did not produce a plan.");
            diagnostics.RecordPhaseSource(
                CobraPhase.RegionDiscovery,
                activeRegionPlan.Regions.Any(static region => region.ScoreSource == CobraScoreSource.Cuda) ? "RegionPlan:CudaAssisted" : "RegionPlan:CpuHeuristic",
                activeRegionPlan.Regions.Any(static region => region.ScoreSource == CobraScoreSource.Cuda));

            CobraFrontierPlan? frontierPlan = null;
            phaseCoordinator.ExecutePhase(CobraPhase.FrontierBuild, () => {
                frontierPlan = CobraFrontierPlanner.Build(graph, iterationSnapshot, activeRegionPlan);
            });
            var activeFrontierPlan = frontierPlan ?? throw new InvalidOperationException("COBRA frontier planning did not produce a plan.");
            diagnostics.RecordPhaseSource(CobraPhase.FrontierBuild, $"FrontierPriority:{activeFrontierPlan.PrioritySource}", activeFrontierPlan.PrioritySource == CobraFrontierPrioritySource.Cuda);

            CobraRuleCompatibilityPlan? ruleCompatibilityPlan = null;
            IReadOnlyDictionary<int, IReadOnlyList<Rule>>? compatibilityRulesByClass = null;
            List<Rule>? compatibilityRules = null;
            CobraNodeMatchCandidatePlan? nodeMatchCandidatePlan = null;
            CobraDirectMatchPlan? directMatchPlan = null;
            IReadOnlyList<int> compatibilityClassIds = [];

            List<Match>? matches = null;
            List<Match>? directMatches = null;
            List<CobraDirectRuleApplication>? simpleDirectApplications = null;
            CobraMatchPriorityResult? matchPriorityResult = null;
            string matchFrontierScope = "full";
            bool widenedMatchFrontier = false;
            var primaryMatchClassIds = SelectPrimaryMatchFrontierClassIds(activeFrontierPlan);

            phaseCoordinator.ExecutePhase(CobraPhase.RuleCompatibility, () => {
                ruleCompatibilityPlan = CobraRuleCompatibilityPlanner.Build(graph, activeFrontierPlan.OrderedClassIds, allRules);
            });
            var fullRuleCompatibilityPlan = ruleCompatibilityPlan ?? throw new InvalidOperationException("COBRA rule compatibility planning did not produce a plan.");
            diagnostics.RecordPhaseSource(CobraPhase.RuleCompatibility, $"RuleCompatibility:{fullRuleCompatibilityPlan.Source}", fullRuleCompatibilityPlan.Source == CobraRuleCompatibilitySource.Cuda);
            IReadOnlyDictionary<int, IReadOnlyList<Rule>>? fullCompatibilityRulesByClass = null;
            CobraNodeMatchCandidatePlan? fullNodeMatchCandidatePlan = null;
            IReadOnlyDictionary<int, IReadOnlyList<Rule>>? fullFilteredCompatibilityRulesByClass = null;
            phaseCoordinator.ExecutePhase(CobraPhase.MatchCandidateBuild, () => {
                directMatchPlan = CobraDirectMatchPlanner.Build(graph, iterationSnapshot, activeFrontierPlan.OrderedClassIds, fullRuleCompatibilityPlan.RulesByClassId, ruleMetadataByRule);
                if (!context.GetBool(SolverOptionKeys.CobraSkipCompatibilityForDirectHandledRules, false))
                {
                    directMatchPlan = directMatchPlan with { ExhaustivelyHandledRulesByClass = null };
                }

                fullCompatibilityRulesByClass = CobraRulePartitioner.BuildCompatibilityRulesByClass(fullRuleCompatibilityPlan.RulesByClassId, directMatchPlan);
                var fullCompatibilityClassIds = FilterFrontierClassIdsForCompatibility(activeFrontierPlan.OrderedClassIds, fullCompatibilityRulesByClass);
                fullCompatibilityRulesByClass = CobraNodeMatchCandidatePlanner.FilterRulesByPossibleCandidates(iterationSnapshot, fullCompatibilityClassIds, fullCompatibilityRulesByClass, ruleMetadataByRule);
                fullCompatibilityClassIds = FilterFrontierClassIdsForCompatibility(activeFrontierPlan.OrderedClassIds, fullCompatibilityRulesByClass);
                fullNodeMatchCandidatePlan = CobraNodeMatchCandidatePlanner.Build(graph, iterationSnapshot, fullCompatibilityClassIds, fullCompatibilityRulesByClass, ruleMetadataByRule);
                fullFilteredCompatibilityRulesByClass = CobraMatcher.FilterRulesByPositiveCandidates(fullCompatibilityRulesByClass, fullNodeMatchCandidatePlan.EligibleNodesByClass);
            });
            var fullDirectMatchPlan = directMatchPlan ?? throw new InvalidOperationException("COBRA direct match planning did not produce a plan.");
            var activeFullCompatibilityRulesByClass = fullCompatibilityRulesByClass ?? throw new InvalidOperationException("COBRA compatibility rule filtering did not produce class mappings.");
            var activeFullNodeMatchCandidatePlan = fullNodeMatchCandidatePlan ?? throw new InvalidOperationException("COBRA node candidate planning did not produce a plan.");
            var activeFullFilteredCompatibilityRulesByClass = fullFilteredCompatibilityRulesByClass ?? throw new InvalidOperationException("COBRA filtered compatibility rules did not produce class mappings.");
            diagnostics.RecordPhaseSource(CobraPhase.MatchCandidateBuild, $"DirectPlan:{fullDirectMatchPlan.Source}", fullDirectMatchPlan.Source == CobraDirectMatchSource.Cuda);
            diagnostics.RecordPhaseSource(CobraPhase.MatchCandidateBuild, $"NodeCandidates:{activeFullNodeMatchCandidatePlan.Source}", activeFullNodeMatchCandidatePlan.Source == CobraNodeMatchCandidateSource.Cuda);

            void BuildMatchStage(IReadOnlyList<int> stageClassIds)
            {
                var activeRuleCompatibilityPlan = CobraRuleCompatibilityPlanner.Slice(fullRuleCompatibilityPlan, stageClassIds);
                ruleCompatibilityPlan = activeRuleCompatibilityPlan;
                directMatchPlan = CobraDirectMatchPlanner.Slice(fullDirectMatchPlan, stageClassIds);

                phaseCoordinator.ExecutePhase(CobraPhase.MatchCandidateBuild, () => {
                    compatibilityRulesByClass = SliceRulesByClass(activeFullFilteredCompatibilityRulesByClass, stageClassIds);
                    compatibilityClassIds = FilterFrontierClassIdsForCompatibility(stageClassIds, compatibilityRulesByClass);
                    nodeMatchCandidatePlan = CobraNodeMatchCandidatePlanner.Slice(activeFullNodeMatchCandidatePlan, compatibilityClassIds);
                    compatibilityRules = compatibilityRulesByClass.Values.SelectMany(static rules => rules).Distinct().ToList();
                });
                var activeCompatibilityRulesByClass = compatibilityRulesByClass ?? throw new InvalidOperationException("COBRA compatibility rule filtering did not produce class mappings.");
                var activeCompatibilityRules = compatibilityRules ?? throw new InvalidOperationException("COBRA compatibility rule filtering did not produce rules.");
                var activeNodeMatchCandidatePlan = nodeMatchCandidatePlan ?? throw new InvalidOperationException("COBRA node candidate planning did not produce a plan.");
                var activeDirectMatchPlan = directMatchPlan ?? throw new InvalidOperationException("COBRA direct match planning did not produce a plan.");

                phaseCoordinator.ExecutePhase(CobraPhase.Match, () => {
                    var rawMatches = CobraMatcher.FindMatches(
                        cobraGraphState,
                        graph,
                        activeCompatibilityRules,
                        compatibilityClassIds,
                        activeCompatibilityRulesByClass,
                        activeNodeMatchCandidatePlan.EligibleNodesByClass,
                        history,
                        context.MaxConcurrency,
                        context.CancellationToken,
                        diagnostics,
                        out directMatches,
                        out simpleDirectApplications,
                        activeDirectMatchPlan,
                        activeCompatibilityRulesByClass);

                    matchPriorityResult = CobraMatchPlanner.Prioritize(rawMatches, activeRegionPlan);
                    matches = matchPriorityResult.Matches.ToList();
                });
                if (matchPriorityResult is not null)
                {
                    diagnostics.RecordPhaseSource(CobraPhase.Match, $"MatchPriority:{matchPriorityResult.PrioritySource}", matchPriorityResult.PrioritySource == CobraMatchPrioritySource.Cuda);
                }
            }

            BuildMatchStage(primaryMatchClassIds);
            matchFrontierScope = primaryMatchClassIds.Count < activeFrontierPlan.OrderedClassIds.Count ? "interior-only" : "full";
            if (ShouldWidenMatchFrontier(
                    activeFrontierPlan,
                    primaryMatchClassIds,
                    matches?.Count ?? 0,
                    directMatches?.Count ?? 0,
                    simpleDirectApplications?.Count ?? 0))
            {
                widenedMatchFrontier = true;
                var deferredMatchClassIds = SelectDeferredMatchFrontierClassIds(activeFrontierPlan);
                BuildMatchStage(deferredMatchClassIds);
                matchFrontierScope = "widened-deferred";
            }

            int matchCount = matches?.Count ?? 0;
            int directMatchCount = directMatches?.Count ?? 0;

            if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
            {
                CobraTelemetrySnapshot telemetry = CobraTelemetrySnapshot.Create(
                     runtime,
                     iterations,
                     matchCount,
                     activeRegionPlan,
                     activeFrontierPlan.PrioritySource.ToString(),
                     usedCompatibilityFallback: !runtime.IsCudaAvailable);
                Console.WriteLine($"DEBUG: COBRA Iteration {telemetry.Iteration}: matches={telemetry.MatchCount}, directMatches={directMatchCount}, regions={telemetry.RegionCount}, hot={telemetry.HotRegionCount}, suppressed={telemetry.SuppressedRegionCount}, packs={telemetry.PackCount}, conflictDensity={telemetry.ConflictDensity:F2}, reduction={telemetry.ReductionRatio:F2}, runtime={telemetry.Runtime.RuntimeKind}, frontierPriority={telemetry.FrontierPrioritySource}, frontierQueues={activeFrontierPlan.InteriorQueueClassIds.Count}/{activeFrontierPlan.BoundaryQueueClassIds.Count}/{activeFrontierPlan.ResidualQueueClassIds.Count}/{activeFrontierPlan.SuppressedQueueClassIds.Count}, matchScope={matchFrontierScope}, widened={widenedMatchFrontier}, ruleCompatibility={ruleCompatibilityPlan?.Source.ToString() ?? "Unknown"}, nodeCandidates={nodeMatchCandidatePlan?.Source.ToString() ?? "Unknown"}, directPlan={directMatchPlan?.Source.ToString() ?? "Unknown"}, matchPriority={matchPriorityResult?.PrioritySource.ToString() ?? "Unknown"}");
            }

            var costModel = CostModelFactory.GetCostModel(context);
            var currentCostFunction = costModel.GetCostFunction(context, graph);
            var pendingRuleUnions = new List<(int LeftId, int RightId)>();
            var bindingExtractionCache = new Dictionary<int, IExpression>();
            var bindingDictionaryCache = new Dictionary<string, ImmutableDictionary<string, IExpression>>(StringComparer.Ordinal);

            phaseCoordinator.ExecutePhase(CobraPhase.Instantiate, () => {
                if (simpleDirectApplications != null)
                {
                    foreach (var application in simpleDirectApplications)
                    {
                        context.ThrowIfCancellationRequested();
                        TryInstantiateSimpleDirectApplication(
                            graph,
                            context,
                            extractionTimeoutSeconds,
                            currentCostFunction,
                            cobraGraphState,
                            application,
                            pendingRuleUnions,
                            diagnostics,
                            bindingExtractionCache,
                            bindingDictionaryCache);
                    }
                }

                if (matches == null) return;
                foreach (var match in matches)
                {
                    context.ThrowIfCancellationRequested();
                    if (TryInstantiateSimpleRewriteMatch(cobraGraphState, match, pendingRuleUnions, diagnostics))
                    {
                        continue;
                    }

                    if (!TryMaterializeBindings(graph, context, extractionTimeoutSeconds, currentCostFunction, match, out var immutableBindings, cobraGraphState, diagnostics, bindingExtractionCache, bindingDictionaryCache))
                    {
                        continue;
                    }

                    if (match.Rule.Condition != null && !match.Rule.Condition(immutableBindings)) continue;
                    if (match.Rule.AssumptionCondition != null && !match.Rule.AssumptionCondition(immutableBindings, context.Assumptions)) continue;

                    int newClassId;
                    if (match.Rule.Transform != null)
                    {
                        var transformed = match.Rule.Transform(immutableBindings);
                        if (transformed == null) continue;
                        newClassId = cobraGraphState.AddExpression(transformed.Canonicalize());
                    }
                    else
                    {
                        newClassId = CobraInstantiator.Instantiate(cobraGraphState, match.Rule.Replacement, match.Bindings);
                    }

                    if (cobraGraphState.Find(match.RootClassId) != cobraGraphState.Find(newClassId))
                    {
                        pendingRuleUnions.Add((match.RootClassId, newClassId));
                    }
                }
            });

            var ruleUnionResult = PrepareUnionsIfNeeded(cobraGraphState, pendingRuleUnions);
            diagnostics.RecordPhaseSource(CobraPhase.UnionPreparation, $"RuleUnions:{ruleUnionResult.PreparationSource}", ruleUnionResult.PreparationSource == CobraUnionPreparationSource.Cuda);
            if (ShouldExecuteUnionApplication(ruleUnionResult))
            {
                phaseCoordinator.ExecutePhase(CobraPhase.UnionApplication, () => {
                    if (CobraUnionEngine.ApplyUnions(cobraGraphState, graph, ruleUnionResult.PreparedUnions))
                    {
                        changed = true;
                    }
                });
            }

            var pendingEqualityUnions = DiscoverPendingEqualityUnions(cobraGraphState);

            var equalityUnionResult = PrepareUnionsIfNeeded(cobraGraphState, pendingEqualityUnions);
            diagnostics.RecordPhaseSource(CobraPhase.UnionPreparation, $"Equalities:{equalityUnionResult.PreparationSource}", equalityUnionResult.PreparationSource == CobraUnionPreparationSource.Cuda);
            if (ShouldExecuteUnionApplication(equalityUnionResult))
            {
                phaseCoordinator.ExecutePhase(CobraPhase.UnionApplication, () => {
                    if (CobraUnionEngine.ApplyUnions(cobraGraphState, graph, equalityUnionResult.PreparedUnions))
                    {
                        changed = true;
                    }
                });
            }

            bool resolvedInequalities = ResolveInequalities(graph, context, cobraGraphState, diagnostics);

            if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
            {
                Console.WriteLine(
                    $"DEBUG: COBRA Union Prep: rules={ruleUnionResult.PreparedUnions.Count}/{pendingRuleUnions.Count} ({ruleUnionResult.PreparationSource}), " +
                    $"equalities={equalityUnionResult.PreparedUnions.Count}/{pendingEqualityUnions.Count} ({equalityUnionResult.PreparationSource}), " +
                    $"inequalities={(resolvedInequalities ? "applied" : "none")}");
            }

            var repairSnapshot = CobraPlannerSnapshot.Create(cobraGraphState);
            allNodeCounts = repairSnapshot.NodeCounts;
            allGenerations = repairSnapshot.Generations;
            var repairCandidatePlan = CobraRepairCandidatePlanner.Build(cobraGraphState, repairSnapshot);
            var repairApplicationPlan = CobraRepairApplicationPlanner.Build(cobraGraphState, repairSnapshot, repairCandidatePlan);
            var rebuildPlan = CobraRebuildPreparationPlanner.Build(cobraGraphState, repairSnapshot.RootIds, allNodeCounts, allGenerations, repairCandidatePlan);
            diagnostics.RecordPhaseSource(CobraPhase.Rebuild, $"RebuildPrep:{rebuildPlan.Source}", rebuildPlan.Source == CobraRebuildPreparationSource.Cuda);
            diagnostics.RecordPhaseSource(CobraPhase.Rebuild, $"RepairCandidates:{repairCandidatePlan.Source}", repairCandidatePlan.Source == CobraRepairCandidateSource.Cuda);
            diagnostics.RecordPhaseSource(CobraPhase.Rebuild, $"RepairApply:{repairApplicationPlan.Source}", repairApplicationPlan.Source == CobraRepairApplicationSource.Cuda);

            if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
            {
                Console.WriteLine($"DEBUG: COBRA Rebuild Prep: classes={rebuildPlan.OrderedClassIds.Count}, source={rebuildPlan.Source}, repairCandidates={repairCandidatePlan.Candidates.Count}, repairSource={repairCandidatePlan.Source}, repairGroups={repairApplicationPlan.Groups.Count}, repairApply={repairApplicationPlan.Source}");
            }

            if (RequiresPostUnionMaintenance(changed, resolvedInequalities))
            {
                phaseCoordinator.ExecutePhase(CobraPhase.Rebuild, () => {
                    CobraRebuildEngine.Rebuild(
                        cobraGraphState,
                        graph,
                        rebuildPlan.OrderedClassIds,
                        repairApplicationPlan.GroupCandidates,
                        context.CancellationToken);
                });
                // Update metrics after rebuild
                CobraCudaNative.TryWarmGraphCaches(cobraGraphState);
                var rebuildSnapshot = CobraPlannerSnapshot.Create(cobraGraphState);
                allNodeCounts = rebuildSnapshot.NodeCounts;
                allGenerations = rebuildSnapshot.Generations;
                var analysisPlan = CobraAnalysisPreparationPlanner.Build(cobraGraphState, rebuildSnapshot.RootIds, allNodeCounts, allGenerations, repairCandidatePlan);
                diagnostics.RecordPhaseSource(CobraPhase.Analysis, $"AnalysisPrep:{analysisPlan.Source}", analysisPlan.Source == CobraAnalysisPreparationSource.Cuda);
                if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                {
                    Console.WriteLine($"DEBUG: COBRA Analysis Prep: classes={analysisPlan.OrderedClassIds.Count}, source={analysisPlan.Source}");
                }
                if (ShouldExecuteAnalysis(analysisPlan.OrderedClassIds))
                {
                    phaseCoordinator.ExecutePhase(CobraPhase.Analysis, () =>
                    {
                        ShapeAnalysis.Analyze(graph, analysisPlan.OrderedClassIds, context.CancellationToken);
                        CobraShapeAnalysis.Analyze(cobraGraphState, analysisPlan.OrderedClassIds, context.CancellationToken);
                    });
                }
                analysisIsCurrent = true;
                changed = true;
                if (CheckContradiction(graph)) return SolveResult.Failure(problem, "Contradiction found in COBRA.");
            }
        }

        return ExtractFinal(problem, context, graph, cobraGraphState, trace, rootId, iterations, extractionTimeoutSeconds, diagnostics, analysisIsCurrent);
    }

    internal static bool RequiresPostUnionMaintenance(bool graphChanged, bool resolvedInequalities)
    {
        return graphChanged || resolvedInequalities;
    }

    internal static List<(int LeftId, int RightId)> DiscoverPendingEqualityUnions(CobraGraphState cobraGraphState)
    {
        if (cobraGraphState.NodeCount == 0)
        {
            return [];
        }

        var headCodes = new int[cobraGraphState.NodeCount];
        var childStarts = new int[cobraGraphState.NodeCount];
        var childCounts = new int[cobraGraphState.NodeCount];
        var childIds = new List<int>();
        for (int nodeId = 0; nodeId < cobraGraphState.NodeCount; nodeId++)
        {
            var node = cobraGraphState.GetNode(nodeId);
            headCodes[nodeId] = node.HeadCode;
            childStarts[nodeId] = childIds.Count;
            childCounts[nodeId] = node.Arity;
            foreach (int childId in node.CanonicalChildIds)
            {
                childIds.Add(cobraGraphState.Find(childId));
            }
        }

        if (CobraCudaNative.TryExtractEqualityUnions(
            headCodes,
            childStarts,
            childCounts,
            childIds.ToArray(),
            out var leftIds,
            out var rightIds))
        {
            var gpuPairs = new List<(int LeftId, int RightId)>(leftIds.Length);
            for (int i = 0; i < leftIds.Length; i++)
            {
                gpuPairs.Add((leftIds[i], rightIds[i]));
            }

            return gpuPairs;
        }

        var fallbackPairs = new List<(int LeftId, int RightId)>();
        for (int nodeId = 0; nodeId < cobraGraphState.NodeCount; nodeId++)
        {
            var node = cobraGraphState.GetNode(nodeId);
            if (node.HeadCode != 6 || node.Arity != 2)
            {
                continue;
            }

            int leftId = cobraGraphState.Find(node.CanonicalChildIds[0]);
            int rightId = cobraGraphState.Find(node.CanonicalChildIds[1]);
            if (leftId != rightId)
            {
                fallbackPairs.Add((leftId, rightId));
            }
        }

        return fallbackPairs;
    }

    internal static bool ShouldExecuteAnalysis(IReadOnlyList<int>? classIds)
    {
        return classIds is null || classIds.Count > 0;
    }

    internal static IReadOnlyList<int> FilterFrontierClassIdsForCompatibility(
        IReadOnlyList<int> frontierClassIds,
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> compatibilityRulesByClass)
    {
        if (frontierClassIds.Count == 0 || compatibilityRulesByClass.Count == 0)
        {
            return [];
        }

        var filteredClassIds = new List<int>();
        foreach (int classId in frontierClassIds)
        {
            if (compatibilityRulesByClass.ContainsKey(classId))
            {
                filteredClassIds.Add(classId);
            }
        }

        return filteredClassIds;
    }

    internal static IReadOnlyDictionary<int, IReadOnlyList<Rule>> SliceRulesByClass(
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass,
        IReadOnlyList<int> classIds)
    {
        if (classIds.Count == 0 || rulesByClass.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<Rule>>();
        }

        var slicedRulesByClass = new Dictionary<int, IReadOnlyList<Rule>>(classIds.Count);
        foreach (int classId in classIds)
        {
            if (rulesByClass.TryGetValue(classId, out var rules))
            {
                slicedRulesByClass[classId] = rules;
            }
        }

        return slicedRulesByClass;
    }

    internal static IReadOnlyList<int> SelectPrimaryMatchFrontierClassIds(CobraFrontierPlan frontierPlan)
    {
        if (frontierPlan.InteriorQueueClassIds.Count > 0)
        {
            return frontierPlan.InteriorQueueClassIds;
        }

        return frontierPlan.OrderedClassIds;
    }

    internal static IReadOnlyList<int> SelectDeferredMatchFrontierClassIds(CobraFrontierPlan frontierPlan)
    {
        var deferredClassIds = new List<int>(
            frontierPlan.BoundaryQueueClassIds.Count +
            frontierPlan.ResidualQueueClassIds.Count +
            frontierPlan.SuppressedQueueClassIds.Count);
        deferredClassIds.AddRange(frontierPlan.BoundaryQueueClassIds);
        deferredClassIds.AddRange(frontierPlan.ResidualQueueClassIds);
        deferredClassIds.AddRange(frontierPlan.SuppressedQueueClassIds);

        return deferredClassIds;
    }

    internal static bool ShouldWidenMatchFrontier(
        CobraFrontierPlan frontierPlan,
        IReadOnlyList<int> activeClassIds,
        int matchCount,
        int directMatchCount,
        int simpleDirectApplicationCount)
    {
        if (activeClassIds.Count == 0 ||
            activeClassIds.Count >= frontierPlan.OrderedClassIds.Count ||
            (frontierPlan.BoundaryQueueClassIds.Count == 0 &&
             frontierPlan.ResidualQueueClassIds.Count == 0 &&
             frontierPlan.SuppressedQueueClassIds.Count == 0))
        {
            return false;
        }

        return matchCount == 0 &&
               directMatchCount == 0 &&
               simpleDirectApplicationCount == 0;
    }

    internal static bool ShouldExecuteUnionApplication(CobraUnionPreparationResult unionResult)
    {
        return unionResult.PreparedUnions.Count > 0;
    }

    private static void SeedAttributes(EGraph graph, SolveContext context)
    {
        if (context.AdditionalData is null ||
            !context.AdditionalData.TryGetValue("Attributes", out var attrData) ||
            attrData is not Dictionary<string, Dictionary<string, double>> symbolAttributes)
        {
            return;
        }

        foreach (var symAttr in symbolAttributes)
        {
            int classId = graph.Add(new Symbol(symAttr.Key));
            var eClass = graph.GetClass(classId);
            foreach (var prop in symAttr.Value)
            {
                eClass.Metadata[prop.Key] = prop.Value;
            }
        }
    }

    private static int IngestProblem(IExpression problem, SolveContext context, EGraph graph)
    {
        if (problem is Equality eq)
        {
            graph.Add(eq);
            int lhs = graph.Add(eq.LeftOperand);
            int rhs = graph.Add(eq.RightOperand);
            graph.Union(lhs, rhs);
            return context.TargetVariable != null ? graph.Add(context.TargetVariable) : lhs;
        }

        if (problem is Vector vec && vec.Arguments.All(a => a is Equality))
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

            return context.TargetVariable != null ? graph.Add(context.TargetVariable) : graph.Add(problem);
        }

        return graph.Add(problem);
    }

    internal static bool IsSimpleRewriteRule(Rule rule)
    {
        return rule.Condition == null &&
               rule.AssumptionCondition == null &&
               rule.Transform == null;
    }

    internal static bool TryInstantiateSimpleRewriteMatch(
        CobraGraphState cobraGraphState,
        Match match,
        List<(int LeftId, int RightId)> pendingRuleUnions,
        CobraDiagnostics diagnostics)
    {
        if (!IsSimpleRewriteRule(match.Rule))
        {
            return false;
        }

        diagnostics.RecordSkippedSimpleBindingMaterialization();
        int newClassId = CobraInstantiator.Instantiate(cobraGraphState, match.Rule.Replacement, match.Bindings);
        if (cobraGraphState.Find(match.RootClassId) != cobraGraphState.Find(newClassId))
        {
            pendingRuleUnions.Add((match.RootClassId, newClassId));
        }

        return true;
    }

    internal static bool TryInstantiateSimpleDirectApplication(
        EGraph graph,
        SolveContext context,
        int extractionTimeoutSeconds,
        Func<ENode, long> currentCostFunction,
        CobraGraphState cobraGraphState,
        CobraDirectRuleApplication application,
        List<(int LeftId, int RightId)> pendingRuleUnions,
        CobraDiagnostics diagnostics,
        Dictionary<int, IExpression> bindingExtractionCache,
        Dictionary<string, ImmutableDictionary<string, IExpression>> bindingDictionaryCache)
    {
        if (application.Rule.Transform != null && application.TransformedExpression is null)
        {
            return false;
        }

        if (application.Rule.Condition != null || application.Rule.AssumptionCondition != null)
        {
            if (!TryMaterializeBindings(
                    graph,
                    context,
                    extractionTimeoutSeconds,
                    currentCostFunction,
                    application.Bindings,
                    out var immutableBindings,
                    cobraGraphState,
                    diagnostics,
                    bindingExtractionCache,
                    bindingDictionaryCache))
            {
                return true;
            }

            if (application.Rule.Condition != null && !application.Rule.Condition(immutableBindings))
            {
                return true;
            }

            if (application.Rule.AssumptionCondition != null && !application.Rule.AssumptionCondition(immutableBindings, context.Assumptions))
            {
                return true;
            }
        }
        else
        {
            diagnostics.RecordSkippedSimpleBindingMaterialization();
        }

        int newClassId = application.TransformedExpression is not null
            ? cobraGraphState.AddExpression(application.TransformedExpression.Canonicalize())
            : CobraInstantiator.Instantiate(cobraGraphState, application.Rule.Replacement, application.Bindings);
        if (cobraGraphState.Find(application.RootClassId) != cobraGraphState.Find(newClassId))
        {
            pendingRuleUnions.Add((application.RootClassId, newClassId));
        }

        return true;
    }

    internal static bool TryMaterializeBindings(
        EGraph graph,
        SolveContext context,
        int extractionTimeoutSeconds,
        Func<ENode, long> currentCostFunction,
        Match match,
        out ImmutableDictionary<string, IExpression> immutableBindings,
        CobraGraphState cobraGraphState,
        CobraDiagnostics diagnostics,
        Dictionary<int, IExpression> bindingExtractionCache,
        Dictionary<string, ImmutableDictionary<string, IExpression>> bindingDictionaryCache)
    {
        return TryMaterializeBindings(
            graph,
            context,
            extractionTimeoutSeconds,
            currentCostFunction,
            match.Bindings,
            out immutableBindings,
            cobraGraphState,
            diagnostics,
            bindingExtractionCache,
            bindingDictionaryCache);
    }

    internal static bool TryMaterializeBindings(
        EGraph graph,
        SolveContext context,
        int extractionTimeoutSeconds,
        Func<ENode, long> currentCostFunction,
        IReadOnlyDictionary<string, int> bindings,
        out ImmutableDictionary<string, IExpression> immutableBindings,
        CobraGraphState cobraGraphState,
        CobraDiagnostics diagnostics,
        Dictionary<int, IExpression> bindingExtractionCache,
        Dictionary<string, ImmutableDictionary<string, IExpression>> bindingDictionaryCache)
    {
        string bindingCacheKey = CreateBindingDictionaryCacheKey(bindings, graph);
        if (bindingDictionaryCache.TryGetValue(bindingCacheKey, out var cachedBindings))
        {
            diagnostics.RecordBindingDictionaryCacheHit();
            immutableBindings = cachedBindings;
            return true;
        }

        diagnostics.RecordBindingDictionaryCacheMiss();
        diagnostics.RecordBindingMaterialization();
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
            foreach (var kvp in bindings)
            {
                materializedBindings.Add(
                    kvp.Key,
                    ResolveBindingExpression(
                        kvp.Value,
                        graph,
                        context,
                        currentCostFunction,
                        cobraGraphState,
                        diagnostics,
                        bindingExtractionCache,
                        bindingExtractionToken));
            }
        }
        catch (OperationCanceledException) when (bindingExtractionCts != null && bindingExtractionCts.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested)
        {
            immutableBindings = ImmutableDictionary<string, IExpression>.Empty;
            return false;
        }
        finally
        {
            bindingExtractionCts?.Dispose();
        }

        immutableBindings = materializedBindings.ToImmutable();
        bindingDictionaryCache[bindingCacheKey] = immutableBindings;
        return true;
    }

    internal static string CreateBindingDictionaryCacheKey(IReadOnlyDictionary<string, int> bindings, EGraph graph)
    {
        var orderedBindings = bindings
            .Select(kvp => new KeyValuePair<string, int>(kvp.Key, graph.Find(kvp.Value)))
            .OrderBy(static kvp => kvp.Key, StringComparer.Ordinal);

        var builder = new StringBuilder();
        foreach (var binding in orderedBindings)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            builder.Append(binding.Key);
            builder.Append(':');
            builder.Append(binding.Value);
        }

        return builder.ToString();
    }

    internal static IExpression ResolveBindingExpression(
        int classId,
        EGraph graph,
        SolveContext context,
        Func<ENode, long> currentCostFunction,
        CobraGraphState cobraGraphState,
        CobraDiagnostics diagnostics,
        Dictionary<int, IExpression> bindingExtractionCache,
        CancellationToken bindingExtractionToken)
    {
        int canonicalClassId = cobraGraphState.Find(classId);
        if (bindingExtractionCache.TryGetValue(canonicalClassId, out IExpression? cachedExpression))
        {
            diagnostics.RecordBindingExtractionCacheHit();
            return cachedExpression;
        }

        diagnostics.RecordBindingExtractionCacheMiss();
        IExpression extracted = CobraExtractor.ExtractBestEffort(
                        cobraGraphState,
                        canonicalClassId,
                        null,
                        currentCostFunction,
                        [],
                        new Dictionary<int, IReadOnlyList<int>>(),
                        bindingExtractionToken,
                        context.CancellationToken,
                        diagnostics);
        bindingExtractionCache[canonicalClassId] = extracted;
        return extracted;
    }

    internal static bool RequiresLegacyBoundarySyncForRuleUnions(CobraGraphState cobraGraphState, int pendingRuleUnionCount)
    {
        return false;
    }

    private static CobraUnionPreparationResult PrepareUnionsIfNeeded(CobraGraphState cobraGraphState, List<(int LeftId, int RightId)> pendingUnions)
    {
        return pendingUnions.Count == 0
            ? new CobraUnionPreparationResult([], CobraUnionPreparationSource.CpuHeuristic)
            : CobraUnionPreparationPlanner.Prepare(cobraGraphState, pendingUnions, preferCachedParentSnapshot: true);
    }

    private static bool SyncLegacyBoundaryFromCobraIfNeeded(EGraph graph, CobraGraphState cobraGraphState, CobraDiagnostics diagnostics, int pendingRuleUnionCount)
    {
        return false;
    }

    private static SolveResult ExtractFinal(
        IExpression problem,
        SolveContext context,
        EGraph graph,
        CobraGraphState cobraGraphState,
        ImmutableList<IExpression>.Builder? trace,
        int rootId,
        int iterations,
        int extractionTimeoutSeconds,
        CobraDiagnostics diagnostics,
        bool analysisIsCurrent)
    {
        var finalSnapshot = CobraPlannerSnapshot.Create(cobraGraphState);
        if (!analysisIsCurrent)
        {
            var finalAnalysisPlan = CobraAnalysisPreparationPlanner.Build(cobraGraphState, finalSnapshot, null);
            diagnostics.RecordPhaseSource(CobraPhase.Analysis, $"FinalAnalysisPrep:{finalAnalysisPlan.Source}", finalAnalysisPlan.Source == CobraAnalysisPreparationSource.Cuda);
            if (ShouldExecuteAnalysis(finalAnalysisPlan.OrderedClassIds))
            {
                ShapeAnalysis.Analyze(graph, finalAnalysisPlan.OrderedClassIds, context.CancellationToken);
                CobraShapeAnalysis.Analyze(cobraGraphState, finalAnalysisPlan.OrderedClassIds, context.CancellationToken);
            }
        }
        CobraCudaNative.TryWarmGraphCaches(cobraGraphState);
        var baseCostModel = CostModelFactory.GetCostModel(context);
        int targetClassId = context.TargetVariable != null ? graph.Find(graph.Add(context.TargetVariable)) : -1;
        var finalCostModel = new TargetPenaltyCostModel(baseCostModel, targetClassId);
        var costFunction = finalCostModel.GetCostFunction(context, graph);
        var extractionPlan = CobraExtractionPlanner.Build(cobraGraphState, finalSnapshot);

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
                var bestExpr = CobraExtractor.ExtractBestEffort(
                    cobraGraphState,
                    targetId,
                    filter.Accept,
                    costFunction,
                    extractionPlan.OrderedClassIds,
                    extractionPlan.OrderedNodesByClass,
                    extractionToken,
                    context.CancellationToken,
                    diagnostics);
                if (bestExpr.InternalEquals(context.TargetVariable))
                {
                    bestExpr = CobraExtractor.ExtractBestEffort(
                        cobraGraphState,
                        targetId,
                        null,
                        costFunction,
                        extractionPlan.OrderedClassIds,
                        extractionPlan.OrderedNodesByClass,
                        extractionToken,
                        context.CancellationToken,
                        diagnostics);
                }

                var resultEq = new Equality(context.TargetVariable, bestExpr).Canonicalize();
                trace?.Add(resultEq);
                return SolveResult.Success(resultEq, $"Solved via COBRA in {iterations} iterations.", trace?.ToImmutable());
            }

            if (problem is Vector vec && vec.Arguments.All(a => a is Equality))
            {
                var results = new List<IExpression>();
                foreach (var arg in vec.Arguments)
                {
                    if (arg is Equality argEq)
                    {
                        var bl = argEq.LeftOperand is Symbol
                            ? argEq.LeftOperand
                            : CobraExtractor.ExtractBestEffort(
                                cobraGraphState,
                                graph.Find(graph.Add(argEq.LeftOperand)),
                                null,
                                costFunction,
                                extractionPlan.OrderedClassIds,
                                extractionPlan.OrderedNodesByClass,
                                extractionToken,
                                context.CancellationToken,
                                diagnostics).Canonicalize();
                        string? lh = bl is Symbol s ? ENode.GetHead(s) : null;
                        var br = CobraExtractor.ExtractBestEffort(
                            cobraGraphState,
                            graph.Find(graph.Add(argEq.RightOperand)),
                            new AvoidSymbolFilter(lh).Accept,
                            costFunction,
                            extractionPlan.OrderedClassIds,
                            extractionPlan.OrderedNodesByClass,
                            extractionToken,
                            context.CancellationToken,
                            diagnostics).Canonicalize();
                        results.Add(new Equality(bl, br).Canonicalize());
                    }
                    else
                    {
                        results.Add(arg);
                    }
                }

                var resultVec = new Vector(results.ToImmutableList());
                trace?.Add(resultVec);
                return SolveResult.Success(resultVec, $"Simplified system of equations via COBRA in {iterations} iterations.", trace?.ToImmutable());
            }

            if (problem is Equality eqProblem)
            {
                var bestLhs = eqProblem.LeftOperand is Symbol
                    ? eqProblem.LeftOperand
                    : CobraExtractor.ExtractBestEffort(
                        cobraGraphState,
                        graph.Find(graph.Add(eqProblem.LeftOperand)),
                        null,
                        costFunction,
                        extractionPlan.OrderedClassIds,
                        extractionPlan.OrderedNodesByClass,
                        extractionToken,
                        context.CancellationToken,
                        diagnostics).Canonicalize();
                string? lhsHead = bestLhs is Symbol sym ? ENode.GetHead(sym) : null;
                var bestRhs = CobraExtractor.ExtractBestEffort(
                    cobraGraphState,
                    graph.Find(graph.Add(eqProblem.RightOperand)),
                    new AvoidSymbolFilter(lhsHead).Accept,
                    costFunction,
                    extractionPlan.OrderedClassIds,
                    extractionPlan.OrderedNodesByClass,
                    extractionToken,
                    context.CancellationToken,
                    diagnostics).Canonicalize();
                var resultEq = new Equality(bestLhs, bestRhs).Canonicalize();
                trace?.Add(resultEq);
                return SolveResult.Success(resultEq, $"Simplified equality via COBRA in {iterations} iterations.", trace?.ToImmutable());
            }

            var bestExprFinal = CobraExtractor.ExtractBestEffort(
                cobraGraphState,
                rootId,
                null,
                costFunction,
                extractionPlan.OrderedClassIds,
                extractionPlan.OrderedNodesByClass,
                extractionToken,
                context.CancellationToken,
                diagnostics).Canonicalize();
            if (bestExprFinal.InternalEquals(problem)) return SolveResult.Failure(problem, "COBRA could not simplify the expression.");
            trace?.Add(bestExprFinal);
            return SolveResult.Success(bestExprFinal, $"Simplified via COBRA in {iterations} iterations.", trace?.ToImmutable());
        }
        catch (OperationCanceledException) when (extractionCts != null && extractionCts.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested)
        {
            return SolveResult.Failure(problem, $"COBRA extraction timeout reached ({extractionTimeoutSeconds}s) before any result could be reconstructed.");
        }
        catch (Exception ex)
        {
            if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1") Console.WriteLine($"DEBUG: Unexpected exception in COBRA Solve: {ex}");
            return SolveResult.Failure(problem, $"Unexpected error: {ex.Message}");
        }
        finally
        {
            extractionCts?.Dispose();
        }
    }

    private bool ResolveInequalities(EGraph graph, SolveContext context, CobraGraphState cobraGraphState, CobraDiagnostics diagnostics)
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
            if (classId == trueId || classId == falseId) continue;

            var eClass = graph.GetClass(classId);
            var assignments = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in eClass.Metadata) assignments[prop.Key] = (decimal)prop.Value;

            if (CobraConditionEvaluator.TryEvaluate(cobraGraphState, classId, assignments, symbolAttributes, context.CancellationToken, diagnostics, out bool result))
            {
                int targetId = result ? trueId : falseId;
                if (graph.Find(classId) != graph.Find(targetId))
                {
                    unionsToPerform.Add((classId, targetId));
                }
            }
        }

        var unionResult = CobraUnionPreparationPlanner.Prepare(graph, unionsToPerform);
        changed = CobraUnionEngine.ApplyUnions(cobraGraphState, graph, unionResult.PreparedUnions);

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
                if (node.Head.StartsWith("Num:", StringComparison.Ordinal) &&
                    decimal.TryParse(node.Head.Substring(4), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal currentNum))
                {
                    if (firstNum != null)
                    {
                        if (Math.Abs(firstNum.Value - currentNum) > 1e-9m) return true;
                    }
                    else
                    {
                        firstNum = currentNum;
                    }
                }
            }
        }

        return false;
    }
}
