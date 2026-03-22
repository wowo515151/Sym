using System.Collections.Immutable;
using System.Diagnostics;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.Strategies;
using Sym.Operations;
using SymCobra.Core;
using SymCobra.Runtime;
using SymCobra.Telemetry;
using SymSolvers;

const int RandomSolveCaseCount = 100;
const decimal SolveTolerance = 0.00000000000000000000000001m;
var finiteDivisors = new decimal[] { -10m, -8m, -5m, -4m, -2m, 2m, 4m, 5m, 8m, 10m };
var x = new Symbol("x");
var y = new Symbol("y");

Environment.SetEnvironmentVariable("SYM_DEBUG_SOLVE", "0");

Console.WriteLine("CobraBench");
Console.WriteLine($"Running {RandomSolveCaseCount} cases per workload.");
Console.WriteLine();

var workloads = new[]
{
    new WorkloadProfile("Baseline", Seed: 24680, MinSteps: 1, MaxStepsExclusive: 5, ConstantMinInclusive: -9, ConstantMaxExclusive: 10),
    new WorkloadProfile("Complex", Seed: 97531, MinSteps: 6, MaxStepsExclusive: 11, ConstantMinInclusive: -20, ConstantMaxExclusive: 21),
    new WorkloadProfile("Very Complex", Seed: 86420, MinSteps: 12, MaxStepsExclusive: 19, ConstantMinInclusive: -50, ConstantMaxExclusive: 51),
    new WorkloadProfile("Extremely Complex", Seed: 112358, MinSteps: 50, MaxStepsExclusive: 51, ConstantMinInclusive: -100, ConstantMaxExclusive: 101),
};

foreach (var workload in workloads)
{
    var cases = CreateCases(RandomSolveCaseCount, x, y, finiteDivisors, workload);
    var cpuStrategy = CreateEGraphStrategy(x);
    var cobraStrategy = CreateCobraStrategy(x);

    Console.WriteLine($"{workload.Name} workload");
    Console.WriteLine($"Generated {cases.Count} cases with {workload.MinSteps}-{workload.MaxStepsExclusive - 1} steps per expression.");

    WarmUp(cases[0], x, y, cpuStrategy);
    WarmUp(cases[0], x, y, cobraStrategy);

    var cpuResult = RunBenchmark(cases, x, y, cpuStrategy);
    var cobraResult = RunBenchmark(cases, x, y, cobraStrategy);

    decimal cobraRelativePercent = cpuResult.Elapsed.TotalMilliseconds == 0d
        ? 0m
        : (decimal)(cobraResult.Elapsed.TotalMilliseconds / cpuResult.Elapsed.TotalMilliseconds * 100d);
    decimal speedup = cobraResult.Elapsed.TotalMilliseconds == 0d
        ? 0m
        : (decimal)(cpuResult.Elapsed.TotalMilliseconds / cobraResult.Elapsed.TotalMilliseconds);

    Console.WriteLine("Results");
    Console.WriteLine($"{cpuResult.Name} : {cpuResult.Elapsed.TotalMilliseconds:F2} ms total, {cpuResult.Elapsed.TotalMilliseconds / cases.Count:F2} ms/case");
    Console.WriteLine($"{cobraResult.Name} : {cobraResult.Elapsed.TotalMilliseconds:F2} ms total, {cobraResult.Elapsed.TotalMilliseconds / cases.Count:F2} ms/case");
    Console.WriteLine($"COBRA relative solve time vs CPU e-graph: {cobraRelativePercent:F2}%");
    Console.WriteLine($"COBRA speedup vs CPU e-graph: {speedup:F2}x");

    if (cobraResult.CobraDiagnostics is { } cobraDiagnostics)
    {
        Console.WriteLine($"COBRA runtime: {cobraDiagnostics.RuntimeKind}");
        Console.WriteLine($"COBRA runtime status: {cobraDiagnostics.RuntimeStatusMessage}");
        Console.WriteLine("COBRA benchmark interpretation: current timings reflect hybrid/compatibility-first execution; phase fallback counts are reported below.");
        Console.WriteLine($"COBRA cases with fallback: {cobraDiagnostics.CasesWithFallbacks}/{cases.Count}");
        Console.WriteLine($"COBRA syncs: full={cobraDiagnostics.FullSyncCount}, incremental={cobraDiagnostics.IncrementalSyncCount}");
        Console.WriteLine($"COBRA phase fallbacks: total={cobraDiagnostics.FallbackPhaseCount}, extraction={cobraDiagnostics.ExtractionFallbackCount}");
        Console.WriteLine(
            $"COBRA binding work: materialized={cobraDiagnostics.BindingMaterializationCount}, skippedSimple={cobraDiagnostics.SkippedSimpleBindingMaterializationCount}, " +
            $"extractCacheHits={cobraDiagnostics.BindingExtractionCacheHitCount}, extractCacheMisses={cobraDiagnostics.BindingExtractionCacheMissCount}, " +
            $"dictCacheHits={cobraDiagnostics.BindingDictionaryCacheHitCount}, dictCacheMisses={cobraDiagnostics.BindingDictionaryCacheMissCount}");
        if (!cobraDiagnostics.FallbackEvents.IsDefaultOrEmpty)
        {
            Console.WriteLine("COBRA fallback reason breakdown:");
            foreach (var fallbackEvent in cobraDiagnostics.FallbackEvents)
            {
                Console.WriteLine($"  {fallbackEvent.Phase} / {fallbackEvent.Reason}: count={fallbackEvent.Count}");
            }
        }
        if (!cobraDiagnostics.SyncEvents.IsDefaultOrEmpty)
        {
            Console.WriteLine("COBRA sync boundary breakdown:");
            foreach (var syncEvent in cobraDiagnostics.SyncEvents)
            {
                Console.WriteLine($"  {syncEvent.Direction} / {syncEvent.Reason} / full={syncEvent.IsFullSync}: count={syncEvent.Count}");
            }
        }

        if (!cobraDiagnostics.Phases.IsDefaultOrEmpty)
        {
            Console.WriteLine("COBRA phase breakdown:");
            foreach (var phase in cobraDiagnostics.Phases)
            {
                Console.WriteLine($"  {phase.Phase}: exec={phase.ExecutionCount}, fallback={phase.FallbackCount}, elapsed={phase.Elapsed.TotalMilliseconds:F2} ms");
            }
        }
        if (!cobraDiagnostics.PhaseSources.IsDefaultOrEmpty)
        {
            Console.WriteLine("COBRA phase source breakdown:");
            foreach (var phaseSource in cobraDiagnostics.PhaseSources)
            {
                Console.WriteLine($"  {phaseSource.Phase} / {phaseSource.Source} / gpu={phaseSource.IsGpuBacked}: count={phaseSource.Count}");
            }
        }
        if (!cobraDiagnostics.KernelTelemetry.IsDefaultOrEmpty)
        {
            Console.WriteLine("COBRA kernel telemetry:");
            foreach (var kernel in cobraDiagnostics.KernelTelemetry)
            {
                Console.WriteLine($"  {kernel.KernelName}: calls={kernel.CallCount}, elapsed={kernel.Elapsed.TotalMilliseconds:F2} ms");
            }
        }
    }

    Console.WriteLine();
}

return;

static List<BenchmarkCase> CreateCases(int caseCount, Symbol x, Symbol y, decimal[] finiteDivisors, WorkloadProfile workload)
{
    var random = new Random(workload.Seed);
    var cases = new List<BenchmarkCase>(caseCount);

    for (int i = 0; i < caseCount; i++)
    {
        IExpression expression = GenerateSingleOccurrenceExpression(random, x, finiteDivisors, workload);
        decimal expectedX = random.Next(-20, 21);

        if (!NumericEvaluator.TryEvaluate(
            expression,
            new Dictionary<string, decimal> { [x.Name] = expectedX },
            out decimal yValue,
            out string? yError))
        {
            throw new InvalidOperationException(
                $"Failed to evaluate generated expression on case {i + 1}: {expression.ToDisplayString()}. Error: {yError}");
        }

        cases.Add(new BenchmarkCase(i + 1, new Equality(y, expression), expectedX, yValue));
    }

    return cases;
}

static void WarmUp(BenchmarkCase benchmarkCase, Symbol x, Symbol y, IBenchmarkStrategy strategy)
{
    RunCase(benchmarkCase, x, y, strategy);
}

static BenchmarkResult RunBenchmark(IReadOnlyList<BenchmarkCase> cases, Symbol x, Symbol y, IBenchmarkStrategy strategy)
{
    var stopwatch = Stopwatch.StartNew();
    CobraBenchmarkDiagnosticsAggregator? diagnosticsAggregator = strategy.RuntimeInfo is null
        ? null
        : new CobraBenchmarkDiagnosticsAggregator();

    for (int i = 0; i < cases.Count; i++)
    {
        var execution = RunCase(cases[i], x, y, strategy);
        if (execution.Diagnostics is not null)
        {
            diagnosticsAggregator?.Add(execution.Diagnostics);
        }
    }

    stopwatch.Stop();
    Console.WriteLine($"{strategy.Name} completed in {stopwatch.Elapsed.TotalMilliseconds:F2} ms.");
    return new BenchmarkResult(
        strategy.Name,
        stopwatch.Elapsed,
        diagnosticsAggregator?.ToSummary(strategy.RuntimeInfo!));
}

static BenchmarkSolveExecution RunCase(BenchmarkCase benchmarkCase, Symbol x, Symbol y, IBenchmarkStrategy strategy)
{
    var benchmarkAdditionalData = ImmutableDictionary<string, object>.Empty
        .Add(SolverOptionKeys.ArithmeticBenchmarkRuleProfile, true);
    var execution = strategy.Solve(
        benchmarkCase.Equation,
        new SolveContext(x, rules: null, maxIterations: 100, enableTracing: false, additionalData: benchmarkAdditionalData));

    var solveResult = execution.SolveResult;
    if (!solveResult.IsSuccess)
    {
        throw new InvalidOperationException(
            $"Solve failed on case {benchmarkCase.CaseIndex}. Equation: {benchmarkCase.Equation.ToDisplayString()}. Message: {solveResult.Message}");
    }

    if (solveResult.ResultExpression is not Equality solvedEquality)
    {
        throw new InvalidOperationException(
            $"Expected equality result on case {benchmarkCase.CaseIndex}. Got: {solveResult.ResultExpression?.ToDisplayString() ?? "null"}");
    }

    if (!TryGetSolvedExpressionForTarget(solvedEquality, x, out IExpression solvedExpression))
    {
        throw new InvalidOperationException(
            $"Solver did not isolate target x on case {benchmarkCase.CaseIndex}. Result: {solvedEquality.ToDisplayString()}");
    }

    IExpression substituted = SubstituteSymbol(solvedExpression, y, new Number(benchmarkCase.YValue)).Canonicalize();
    if (!NumericEvaluator.TryEvaluate(substituted, ImmutableDictionary<string, decimal>.Empty, out decimal recoveredX, out string? recoveredError))
    {
        throw new InvalidOperationException(
            $"Failed to evaluate solved expression on case {benchmarkCase.CaseIndex}. Expression: {substituted.ToDisplayString()}. Error: {recoveredError}");
    }

    if (decimal.Abs(benchmarkCase.ExpectedX - recoveredX) > SolveTolerance)
    {
        throw new InvalidOperationException(
            $"Recovered x did not match on case {benchmarkCase.CaseIndex}. Original x={benchmarkCase.ExpectedX}, recovered x={recoveredX}, solved={solvedEquality.ToDisplayString()}");
    }

    return execution;
}

static IBenchmarkStrategy CreateEGraphStrategy(Symbol x)
{
    return new EGraphSolveAdapter(CreateArithmeticBenchmarkRules(x));
}

static IBenchmarkStrategy CreateCobraStrategy(Symbol x)
{
    return new CobraSolveAdapter(CreateArithmeticBenchmarkRules(x));
}

static IExpression GenerateSingleOccurrenceExpression(Random random, Symbol x, decimal[] finiteDivisors, WorkloadProfile workload)
{
    IExpression expression = x;
    int steps = random.Next(workload.MinSteps, workload.MaxStepsExclusive);

    for (int i = 0; i < steps; i++)
    {
        int operation = random.Next(7);
        decimal constant = operation is 4 or 5 or 6
            ? NextFiniteDivisor(random, finiteDivisors)
            : NextNonZeroConstant(random, workload.ConstantMinInclusive, workload.ConstantMaxExclusive);
        IExpression constantExpression = new Number(constant);

        expression = ((IExpression)(operation switch
        {
            0 => new Add(expression, constantExpression),
            1 => new Add(constantExpression, expression),
            2 => new Subtract(expression, constantExpression),
            3 => new Subtract(constantExpression, expression),
            4 => new Multiply(expression, constantExpression),
            5 => new Multiply(constantExpression, expression),
            6 => new Divide(expression, constantExpression),
            _ => throw new InvalidOperationException("Unsupported random operation."),
        })).Canonicalize();
    }

    return expression;
}

static decimal NextNonZeroConstant(Random random, int minInclusive, int maxExclusive)
{
    decimal value;
    do
    {
        value = random.Next(minInclusive, maxExclusive);
    }
    while (value == 0m);

    return value;
}

static decimal NextFiniteDivisor(Random random, decimal[] finiteDivisors)
{
    return finiteDivisors[random.Next(finiteDivisors.Length)];
}

static IExpression SubstituteSymbol(IExpression expression, Symbol target, IExpression replacement)
{
    if (expression is Symbol symbol && symbol.InternalEquals(target))
    {
        return replacement;
    }

    if (expression is Operation operation)
    {
        var substitutedArguments = operation.Arguments
            .Select(argument => SubstituteSymbol(argument, target, replacement))
            .ToImmutableList();
        return operation.WithArguments(substitutedArguments);
    }

    return expression;
}

static bool TryGetSolvedExpressionForTarget(Equality equality, Symbol target, out IExpression solvedExpression)
{
    solvedExpression = null!;

    if (equality.LeftOperand is Symbol leftSymbol && leftSymbol.InternalEquals(target) && !equality.RightOperand.ContainsSymbol(target))
    {
        solvedExpression = equality.RightOperand;
        return true;
    }

    if (equality.RightOperand is Symbol rightSymbol && rightSymbol.InternalEquals(target) && !equality.LeftOperand.ContainsSymbol(target))
    {
        solvedExpression = equality.LeftOperand;
        return true;
    }

    return false;
}

static ImmutableList<Rule> CreateArithmeticBenchmarkRules(Symbol x)
{
    var a = new Wild("a");
    var b = new Wild("b");
    var c = new Wild("c");

    return ImmutableList.Create(
        new Rule(
            new Equality(a, b),
            new Equality(b, a))
        {
            Name = "SwapEquality"
        },
        new Rule(
            new Equality(new Add(a, b), c),
            new Equality(a, new Subtract(c, b)))
        {
            Name = "IsolateAddLeft"
        },
        new Rule(
            new Equality(new Add(a, b), c),
            new Equality(b, new Subtract(c, a)))
        {
            Name = "IsolateAddRight"
        },
        new Rule(
            new Equality(new Multiply(a, b), c),
            new Equality(a, new Divide(c, b)))
        {
            Name = "IsolateMulLeft"
        },
        new Rule(
            new Equality(new Multiply(a, b), c),
            new Equality(b, new Divide(c, a)))
        {
            Name = "IsolateMulRight"
        });
}

internal readonly record struct BenchmarkCase(int CaseIndex, Equality Equation, decimal ExpectedX, decimal YValue);

internal readonly record struct BenchmarkResult(string Name, TimeSpan Elapsed, CobraBenchmarkDiagnosticsSummary? CobraDiagnostics);

internal readonly record struct BenchmarkSolveExecution(SolveResult SolveResult, CobraDiagnosticsSnapshot? Diagnostics);

internal readonly record struct WorkloadProfile(
    string Name,
    int Seed,
    int MinSteps,
    int MaxStepsExclusive,
    int ConstantMinInclusive,
    int ConstantMaxExclusive);

internal interface IBenchmarkStrategy
{
    string Name { get; }
    CobraRuntimeInfo? RuntimeInfo { get; }
    BenchmarkSolveExecution Solve(IExpression? problem, SolveContext context);
}

internal sealed class EGraphSolveAdapter : IBenchmarkStrategy
{
    private readonly ImmutableList<Rule> _rules;
    private readonly EGraphSolverStrategy _solver = new();

    public string Name => "CPU e-graph";
    public CobraRuntimeInfo? RuntimeInfo => null;

    public EGraphSolveAdapter(ImmutableList<Rule> rules)
    {
        _rules = rules;
    }

    public BenchmarkSolveExecution Solve(IExpression? problem, SolveContext context)
    {
        var eGraphContext = new SolveContext(context.TargetVariable, _rules, context.MaxIterations, context.EnableTracing, context.AdditionalData, context.CancellationToken, context.SharedEGraph, context.MaxConcurrency);
        return new BenchmarkSolveExecution(_solver.Solve(problem, eGraphContext), null);
    }
}

internal sealed class CobraSolveAdapter : IBenchmarkStrategy
{
    private readonly ImmutableList<Rule> _rules;
    private readonly CobraSolverStrategy _solver = new();
    private readonly CobraRuntimeInfo _runtime = CobraRuntimeInfo.Detect();

    public string Name => "COBRA e-graph";
    public CobraRuntimeInfo? RuntimeInfo => _runtime;

    public CobraSolveAdapter(ImmutableList<Rule> rules)
    {
        _rules = rules;
    }

    public BenchmarkSolveExecution Solve(IExpression? problem, SolveContext context)
    {
        var cobraContext = new SolveContext(
            context.TargetVariable,
            _rules,
            context.MaxIterations,
            context.EnableTracing,
            (context.AdditionalData ?? ImmutableDictionary<string, object>.Empty).SetItem(SolverOptionKeys.CobraSkipCompatibilityForDirectHandledRules, true),
            context.CancellationToken,
            context.SharedEGraph,
            context.MaxConcurrency);
        var result = _solver.SolveWithDiagnostics(problem, cobraContext, out var diagnostics);
        return new BenchmarkSolveExecution(result, diagnostics.CreateSnapshot());
    }
}

internal sealed record CobraBenchmarkDiagnosticsSummary(
    string RuntimeKind,
    string RuntimeStatusMessage,
    int CasesWithFallbacks,
    int FullSyncCount,
    int IncrementalSyncCount,
    int FallbackPhaseCount,
    int ExtractionFallbackCount,
    int BindingMaterializationCount,
    int SkippedSimpleBindingMaterializationCount,
    int BindingExtractionCacheHitCount,
    int BindingExtractionCacheMissCount,
    int BindingDictionaryCacheHitCount,
    int BindingDictionaryCacheMissCount,
    ImmutableArray<CobraPhaseDiagnosticsSnapshot> Phases,
    ImmutableArray<CobraKernelTelemetrySummary> KernelTelemetry,
    ImmutableArray<CobraPhaseSourceSummary> PhaseSources,
    ImmutableArray<CobraSyncEventSummary> SyncEvents,
    ImmutableArray<CobraFallbackEventSummary> FallbackEvents);

internal sealed record CobraKernelTelemetrySummary(
    string KernelName,
    int CallCount,
    TimeSpan Elapsed);

internal sealed record CobraSyncEventSummary(
    CobraSyncDirection Direction,
    CobraSyncReason Reason,
    bool IsFullSync,
    int Count);

internal sealed record CobraFallbackEventSummary(
    CobraPhase Phase,
    CobraFallbackReason Reason,
    int Count);

internal sealed record CobraPhaseSourceSummary(
    CobraPhase Phase,
    string Source,
    bool IsGpuBacked,
    int Count);

internal sealed class CobraBenchmarkDiagnosticsAggregator
{
    private readonly Dictionary<CobraPhase, CobraPhaseAggregate> _phaseAggregates = new();
    private readonly Dictionary<string, CobraKernelTelemetryAggregate> _kernelTelemetryAggregates = new(StringComparer.Ordinal);
    private readonly Dictionary<(CobraPhase Phase, string Source, bool IsGpuBacked), int> _phaseSourceCounts = new();
    private readonly Dictionary<(CobraSyncDirection Direction, CobraSyncReason Reason, bool IsFullSync), int> _syncEventCounts = new();
    private readonly Dictionary<(CobraPhase Phase, CobraFallbackReason Reason), int> _fallbackEventCounts = new();

    public int CasesWithFallbacks { get; private set; }
    public int FullSyncCount { get; private set; }
    public int IncrementalSyncCount { get; private set; }
    public int FallbackPhaseCount { get; private set; }
    public int ExtractionFallbackCount { get; private set; }
    public int BindingMaterializationCount { get; private set; }
    public int SkippedSimpleBindingMaterializationCount { get; private set; }
    public int BindingExtractionCacheHitCount { get; private set; }
    public int BindingExtractionCacheMissCount { get; private set; }
    public int BindingDictionaryCacheHitCount { get; private set; }
    public int BindingDictionaryCacheMissCount { get; private set; }

    public CobraBenchmarkDiagnosticsAggregator()
    {
        foreach (CobraPhase phase in Enum.GetValues<CobraPhase>())
        {
            _phaseAggregates[phase] = new CobraPhaseAggregate(phase);
        }
    }

    public void Add(CobraDiagnosticsSnapshot snapshot)
    {
        if (snapshot.HasFallbacks)
        {
            CasesWithFallbacks++;
        }

        FullSyncCount += snapshot.FullSyncCount;
        IncrementalSyncCount += snapshot.IncrementalSyncCount;
        FallbackPhaseCount += snapshot.FallbackPhaseCount;
        ExtractionFallbackCount += snapshot.ExtractionFallbackCount;
        BindingMaterializationCount += snapshot.BindingMaterializationCount;
        SkippedSimpleBindingMaterializationCount += snapshot.SkippedSimpleBindingMaterializationCount;
        BindingExtractionCacheHitCount += snapshot.BindingExtractionCacheHitCount;
        BindingExtractionCacheMissCount += snapshot.BindingExtractionCacheMissCount;
        BindingDictionaryCacheHitCount += snapshot.BindingDictionaryCacheHitCount;
        BindingDictionaryCacheMissCount += snapshot.BindingDictionaryCacheMissCount;

        foreach (var phase in snapshot.Phases)
        {
            _phaseAggregates[phase.Phase].Add(phase);
        }

        foreach (var kernel in snapshot.KernelTelemetry)
        {
            if (!_kernelTelemetryAggregates.TryGetValue(kernel.KernelName, out var aggregate))
            {
                aggregate = new CobraKernelTelemetryAggregate(kernel.KernelName);
                _kernelTelemetryAggregates[kernel.KernelName] = aggregate;
            }

            aggregate.Add(kernel);
        }

        foreach (var phaseSourceEvent in snapshot.PhaseSourceEvents)
        {
            var key = (phaseSourceEvent.Phase, phaseSourceEvent.Source, phaseSourceEvent.IsGpuBacked);
            _phaseSourceCounts[key] = _phaseSourceCounts.TryGetValue(key, out int count) ? count + 1 : 1;
        }

        foreach (var syncEvent in snapshot.SyncEvents)
        {
            var key = (syncEvent.Direction, syncEvent.Reason, syncEvent.IsFullSync);
            _syncEventCounts[key] = _syncEventCounts.TryGetValue(key, out int count) ? count + 1 : 1;
        }

        foreach (var fallbackEvent in snapshot.FallbackEvents)
        {
            var key = (fallbackEvent.Phase, fallbackEvent.Reason);
            _fallbackEventCounts[key] = _fallbackEventCounts.TryGetValue(key, out int count) ? count + 1 : 1;
        }
    }

    public CobraBenchmarkDiagnosticsSummary ToSummary(CobraRuntimeInfo runtime)
    {
        var phases = _phaseAggregates.Values
            .Where(static phase => phase.ExecutionCount > 0 || phase.FallbackCount > 0 || phase.Elapsed > TimeSpan.Zero)
            .OrderBy(static phase => phase.Phase)
            .Select(static phase => phase.ToSnapshot())
            .ToImmutableArray();

        return new CobraBenchmarkDiagnosticsSummary(
            runtime.RuntimeKind,
            runtime.StatusMessage,
            CasesWithFallbacks,
            FullSyncCount,
            IncrementalSyncCount,
            FallbackPhaseCount,
            ExtractionFallbackCount,
            BindingMaterializationCount,
            SkippedSimpleBindingMaterializationCount,
            BindingExtractionCacheHitCount,
            BindingExtractionCacheMissCount,
            BindingDictionaryCacheHitCount,
            BindingDictionaryCacheMissCount,
            phases,
            _kernelTelemetryAggregates.Values
                .Where(static kernel => kernel.CallCount > 0 || kernel.Elapsed > TimeSpan.Zero)
                .OrderByDescending(static kernel => kernel.Elapsed)
                .ThenBy(static kernel => kernel.KernelName, StringComparer.Ordinal)
                .Select(static kernel => kernel.ToSummary())
                .ToImmutableArray(),
            _phaseSourceCounts
                .OrderBy(static entry => entry.Key.Phase)
                .ThenBy(static entry => entry.Key.Source)
                .ThenByDescending(static entry => entry.Key.IsGpuBacked)
                .Select(static entry => new CobraPhaseSourceSummary(entry.Key.Phase, entry.Key.Source, entry.Key.IsGpuBacked, entry.Value))
                .ToImmutableArray(),
            _syncEventCounts
                .OrderBy(static entry => entry.Key.Direction)
                .ThenBy(static entry => entry.Key.Reason)
                .ThenBy(static entry => entry.Key.IsFullSync)
                .Select(static entry => new CobraSyncEventSummary(entry.Key.Direction, entry.Key.Reason, entry.Key.IsFullSync, entry.Value))
                .ToImmutableArray(),
            _fallbackEventCounts
                .OrderBy(static entry => entry.Key.Phase)
                .ThenBy(static entry => entry.Key.Reason)
                .Select(static entry => new CobraFallbackEventSummary(entry.Key.Phase, entry.Key.Reason, entry.Value))
                .ToImmutableArray());
    }
}

internal sealed class CobraPhaseAggregate
{
    public CobraPhase Phase { get; }
    public int ExecutionCount { get; private set; }
    public int FallbackCount { get; private set; }
    public TimeSpan Elapsed { get; private set; }

    public CobraPhaseAggregate(CobraPhase phase)
    {
        Phase = phase;
    }

    public void Add(CobraPhaseDiagnosticsSnapshot snapshot)
    {
        ExecutionCount += snapshot.ExecutionCount;
        FallbackCount += snapshot.FallbackCount;
        Elapsed += snapshot.Elapsed;
    }

    public CobraPhaseDiagnosticsSnapshot ToSnapshot()
    {
        return new CobraPhaseDiagnosticsSnapshot(Phase, ExecutionCount, FallbackCount, Elapsed);
    }
}

internal sealed class CobraKernelTelemetryAggregate
{
    public string KernelName { get; }
    public int CallCount { get; private set; }
    public TimeSpan Elapsed { get; private set; }

    public CobraKernelTelemetryAggregate(string kernelName)
    {
        KernelName = kernelName;
    }

    public void Add(CobraKernelTelemetrySnapshot snapshot)
    {
        CallCount += snapshot.CallCount;
        Elapsed += snapshot.Elapsed;
    }

    public CobraKernelTelemetrySummary ToSummary()
    {
        return new CobraKernelTelemetrySummary(KernelName, CallCount, Elapsed);
    }
}
