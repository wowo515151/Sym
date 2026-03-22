using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using FunctionDefinition = SymSolvers.FunctionDefinitionHelper.FunctionDefinition;

namespace SymSolvers;

// Conservative substitution strategy: replaces Symbol atoms with provided expressions from the context.
public class SubstitutionStrategy : ISolverStrategy
{
    private static readonly ImmutableHashSet<string> EmptyStack =
        ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (context is null) return SolveResult.Failure(problem, "SolveContext cannot be null.");
        if (problem is null) return SolveResult.Failure(null, "Problem expression cannot be null.");

        if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
            Console.WriteLine($"DEBUG: SubstitutionStrategy starting on: {problem.ToDisplayString()}");

        var substitutions = ImmutableDictionary<string, IExpression>.Empty;
        var functionDefinitions = new Dictionary<string, FunctionDefinition>(StringComparer.Ordinal);

        // 1. Load substitutions and function definitions from context if present.
        var baseDict = context.AdditionalData ?? ImmutableDictionary<string, object>.Empty;
        if (baseDict.TryGetValue(SolverOptionKeys.Substitutions, out var subsObj) &&
            subsObj is ImmutableDictionary<string, IExpression> contextSubs)
        {
            substitutions = contextSubs;
        }

        if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
            Console.WriteLine($"DEBUG: SubstitutionStrategy: loaded {substitutions.Count} context subs.");

        FunctionDefinitionHelper.MergeContextDefinitions(
            functionDefinitions,
            context,
            name => !FunctionDefinitionHelper.IsReservedFunctionName(name),
            allowRightSide: false,
            requireBodyUsesParameter: false,
            requireKeyMatch: false);

        // 2. Heuristic: extract internal equalities as substitutions from systems or wrapped systems.
        if (true) // Use ExtractEqualities for all types now that it is robust
        {
            if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                Console.WriteLine("DEBUG: SubstitutionStrategy: extracting from system.");

            var contextTargets = new HashSet<string>(StringComparer.Ordinal);
            if (context.TargetVariable is not null) contextTargets.Add(context.TargetVariable.Name);
            if (baseDict.TryGetValue(SolverOptionKeys.TargetVariables, out var contextTargetsObj) && contextTargetsObj is IEnumerable<string> ctList)
            {
                foreach (var t in ctList) if (t != null) contextTargets.Add(t.Trim());
            }

            var extracted = new Dictionary<string, IExpression>(StringComparer.Ordinal);
            foreach (var eq in ExpressionClassification.ExtractEqualities(problem))
            {
                if (FunctionDefinitionHelper.TryExtractDefinition(eq,
                    name => !FunctionDefinitionHelper.IsReservedFunctionName(name),
                    allowRightSide: false,
                    requireBodyUsesParameter: false,
                    out var def))
                {
                    functionDefinitions[def.Name] = def;
                }

                if (SymSolvers.ExpressionPropertyValidator.TryGetIsolatedSolutionSymbol(eq, out var symbol, out var value))
                {
                    // Avoid using our special 'ans_' tracking symbols as substitutions.
                    if (!symbol.Name.StartsWith("ans_", StringComparison.Ordinal))
                    {
                        // Check if value contains the symbol (recursive definition)
                        if (!value.ContainsSymbol(symbol))
                        {
                            bool symbolIsTarget = contextTargets.Contains(symbol.Name);
                            
                            // Target variables should NOT be substituted away, to ensure they remain in the system 
                            // for final extraction. Intermediate variables should be substituted to reduce the system.
                            bool shouldSubstitute = !symbolIsTarget;

                            if (shouldSubstitute)
                            {
                                if (extracted.TryGetValue(symbol.Name, out var existing))
                                {
                                    if (ExpressionHelpers.IsBetter(value, existing)) extracted[symbol.Name] = value;
                                }
                                else
                                {
                                    extracted[symbol.Name] = value;
                                }
                            }
                        }
                    }
                }
                else
                {
                    // HEURISTIC: handle Function LHS like S(n) = expr
                    if (eq.LeftOperand is Function fn && fn.Arguments.All(a => a is Symbol))
                    {
                        if (!eq.RightOperand.ContainsSymbol(s => s.Name == fn.Name))
                        {
                            var key = eq.LeftOperand.ToDisplayString();
                            if (extracted.TryGetValue(key, out var existing))
                            {
                                if (ExpressionHelpers.IsBetter(eq.RightOperand, existing)) extracted[key] = eq.RightOperand;
                            }
                            else
                            {
                                extracted[key] = eq.RightOperand;
                            }
                        }
                    }
                }
            }

            if (extracted.Count > 0)
            {
                if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1" || context.EnableTracing)
                    Console.WriteLine($"DEBUG: SubstitutionStrategy: extracted {extracted.Count} subs: {string.Join(", ", extracted.Select(k => k.Key + "=" + k.Value.ToDisplayString()))}");
                var builder = substitutions.ToBuilder();
                foreach (var kvp in extracted)
                {
                    if (!builder.ContainsKey(kvp.Key))
                    {
                        builder[kvp.Key] = kvp.Value;
                    }
                    else if (ExpressionHelpers.IsBetter(kvp.Value, builder[kvp.Key]))
                    {
                        builder[kvp.Key] = kvp.Value;
                    }
                }
                substitutions = builder.ToImmutable();
            }
        }
        if (substitutions.Count == 0 && functionDefinitions.Count == 0)
        {
            if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1" || context.EnableTracing)
                Console.WriteLine("DEBUG: SubstitutionStrategy: no subs found.");
            return SolveResult.Success(problem, "No substitutions provided or found.");
        }

        // 3. Resolve substitution chains (fixed-point)
        if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1" || context.EnableTracing)
            Console.WriteLine($"DEBUG: SubstitutionStrategy: resolving chains for {substitutions.Count} subs.");
        var resolvedSubs = substitutions.ToDictionary(k => k.Key, v => v.Value);
        for (int i = 0; i < 3; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var changed = false;
            foreach (var key in resolvedSubs.Keys.ToList())
            {
                var current = resolvedSubs[key];
                var subsForThis = resolvedSubs.ToImmutableDictionary().Remove(key);
                var next = SubstituteExpression(current, subsForThis, functionDefinitions, EmptyStack.Add(key), context.EnableTracing, context.CancellationToken);
                if (!next.InternalEquals(current))
                {
                    // Growth protection
                    if (CountNodes(next) > CountNodes(current) * 1.5 && CountNodes(next) > 20) continue;

                    resolvedSubs[key] = next;
                    changed = true;
                }
            }
            if (!changed) break;
        }
        substitutions = resolvedSubs.ToImmutableDictionary();

        if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1" || context.EnableTracing)
            Console.WriteLine("DEBUG: SubstitutionStrategy: removing targets.");

        // If we have targets, remove them from the substitution map so they don't get replaced,
        // UNLESS the value is a simple constant (e.g., number, or a matrix/vector of constants).
        var targets = new HashSet<string>(StringComparer.Ordinal);
        if (context.TargetVariable is not null) targets.Add(context.TargetVariable.Name);
        if (baseDict.TryGetValue(SolverOptionKeys.TargetVariables, out var rawTargets) && rawTargets is IEnumerable<string> targetList)
        {
            foreach (var t in targetList) if (t != null) targets.Add(t.Trim());
        }

        static bool IsSymbolFreeConstant(IExpression expr)
        {
            // Wildcards must never leak into normal evaluation results.
            if (expr is Wild) return false;
            if (expr is Symbol s) return ExpressionHelpers.IsMathConstant(s.Name);
            if (expr is Atom) return true;
            if (expr is Operation op)
            {
                foreach (var a in op.Arguments)
                {
                    if (!IsSymbolFreeConstant(a)) return false;
                }
                return true;
            }
            return false;
        }

        if (targets.Count > 0)
        {
            var builder = substitutions.ToBuilder();
            bool removedAny = false;
            foreach (var key in substitutions.Keys)
            {
                bool isTarget = IsIdentifier(key) && targets.Contains(key);
                if (!isTarget && !IsIdentifier(key))
                {
                    // For composite keys, check if they contain any target variables.
                    try
                    {
                        var pat = Sym.CSharpIO.CSharpIO.ParseExpressionsStrict(key).FirstOrDefault();
                        if (pat != null && pat.ContainsSymbol(s => targets.Contains(s.Name)))
                        {
                            isTarget = true;
                        }
                    }
                    catch { }
                }

                if (isTarget)
                {
                    if (builder.Remove(key)) removedAny = true;
                }
            }
            if (removedAny) substitutions = builder.ToImmutable();
        }

        if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1" || context.EnableTracing)
            Console.WriteLine($"DEBUG: SubstitutionStrategy: applying {substitutions.Count} substitutions.");

        // Convert substitutions to rules for the rewriter
        var rules = substitutions.Select(kvp => {
            try {
                var pattern = Sym.CSharpIO.CSharpIO.ParseExpressionsStrict(kvp.Key).FirstOrDefault();
                if (pattern != null) return new Sym.Core.Rule(pattern.Canonicalize(), kvp.Value.Canonicalize()) { Name = $"Sub_{kvp.Key}" };
            } catch { }
            return new Sym.Core.Rule(new Symbol(kvp.Key), kvp.Value.Canonicalize()) { Name = $"Sub_{kvp.Key}" };
        }).ToImmutableList();

        var rewriterResult = Sym.Core.Rewriters.Rewriter.RewriteFully(problem, rules, 10, context.Assumptions, context.CancellationToken);
        var currentExpr = rewriterResult.RewrittenExpression;
        var changedAtLeastOnce = rewriterResult.Changed;

        if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1" || context.EnableTracing)
            Console.WriteLine($"DEBUG: SubstitutionStrategy: done. Final: {currentExpr.ToDisplayString()}");

        var message = changedAtLeastOnce ? "Substitution applied." : "No changes performed.";
        return SolveResult.Success(currentExpr, message);
    }

    private static bool TryGetDefiningSymbolToPreserve(
        Equality eq,
        ImmutableDictionary<string, IExpression> substitutions,
        IReadOnlyDictionary<string, FunctionDefinition> functionDefinitions,
        System.Threading.CancellationToken ct,
        out string symbolName)
    {
        symbolName = string.Empty;

        if (!SymSolvers.ExpressionPropertyValidator.TryGetIsolatedSolutionSymbol(eq, out var s, out var val))
        {
            return false;
        }

        if (!substitutions.TryGetValue(s.Name, out var subVal))
        {
            return false;
        }

        if (subVal.InternalEquals(val))
        {
            symbolName = s.Name;
            return true;
        }

        var normalizedVal = SubstituteExpression(val, substitutions.Remove(s.Name), functionDefinitions, EmptyStack.Add(s.Name), false, ct);
        if (subVal.InternalEquals(normalizedVal))
        {
            symbolName = s.Name;
            return true;
        }

        return false;
    }

    private static ImmutableDictionary<string, IExpression> RemoveCompositeDefinitionSubstitutions(
        Equality eq,
        ImmutableDictionary<string, IExpression> substitutions,
        IReadOnlyDictionary<string, FunctionDefinition> functionDefinitions,
        System.Threading.CancellationToken ct)
    {
        var updated = substitutions;
        updated = RemoveCompositeDefinition(eq.LeftOperand, eq.RightOperand, updated, functionDefinitions, ct);
        updated = RemoveCompositeDefinition(eq.RightOperand, eq.LeftOperand, updated, functionDefinitions, ct);
        return updated;
    }

    private static ImmutableDictionary<string, IExpression> RemoveCompositeDefinition(
        IExpression composite,
        IExpression simple,
        ImmutableDictionary<string, IExpression> substitutions,
        IReadOnlyDictionary<string, FunctionDefinition> functionDefinitions,
        System.Threading.CancellationToken ct)
    {
        var key = composite.ToDisplayString();
        if (IsIdentifier(key))
        {
            return substitutions;
        }

        if (!substitutions.TryGetValue(key, out var subVal))
        {
            return substitutions;
        }

        var normalizedSimple = SubstituteExpression(simple, substitutions.Remove(key), functionDefinitions, EmptyStack.Add(key), false, ct);
        if (subVal.InternalEquals(simple) || subVal.InternalEquals(normalizedSimple))
        {
            return substitutions.Remove(key);
        }

        return substitutions;
    }

    private static bool IsConstraintBundle(Add add)
    {
        return add.Arguments.All(a => a is Equality || a is Symbol);
    }

    private interface IWorkItem {}
    private record Process(IExpression Expr, ImmutableDictionary<string, IExpression> Subs, ImmutableHashSet<string> FnStack, ImmutableHashSet<string> SymStack) : IWorkItem;
    private record RebuildEqualityIsolated(Symbol Lhs) : IWorkItem;
    private record RebuildEquality() : IWorkItem;
    private record RebuildFunction(Function Original, ImmutableDictionary<string, IExpression> Subs, ImmutableHashSet<string> FnStack, ImmutableHashSet<string> SymStack) : IWorkItem;
    private record RebuildOperation(Operation Original) : IWorkItem;
    private record FinalizeFunction() : IWorkItem;

    public static IExpression SubstituteInternal(IExpression expr, IReadOnlyDictionary<string, IExpression> subs)
    {
        return SubstituteExpression(expr, subs.ToImmutableDictionary(), null, null, false, default);
    }

    private static IExpression SubstituteExpression(
        IExpression expr,
        ImmutableDictionary<string, IExpression> substitutions,
        IReadOnlyDictionary<string, FunctionDefinition>? functionDefinitions,
        ImmutableHashSet<string>? functionStack,
        bool enableTracing,
        System.Threading.CancellationToken ct)
    {
        // 1. Standard symbol substitution
        var current = SubstituteSymbols(expr, substitutions, functionDefinitions, functionStack, ct);

        // 2. Composite pattern substitution (e.g. x*y -> val, x+y -> val)
        // Only run this if we have composite keys in substitutions map.
        // We detect "composite keys" by checking if any key is not a simple identifier.
        // Actually, the substitutions dict is string->expr. We need to parse keys if they are expressions.
        // But typically the key IS the name.
        // If the user provided "x*y" as a key, it would be in the map.
        
        // Scan for keys that look like expressions
        var composites = new List<(IExpression Pattern, IExpression Replacement)>();
        foreach (var kvp in substitutions)
        {
            if (!IsIdentifier(kvp.Key))
            {
                // Try to parse the key as an expression pattern
                try
                {
                    // Use a relaxed parser for keys since they might be simple
                    var pat = Sym.CSharpIO.CSharpIO.ParseExpressionsStrict(kvp.Key).FirstOrDefault();
                    if (pat != null && !(pat is Symbol)) // Symbols are handled by SubstituteSymbols
                    {
                        composites.Add((pat.Canonicalize(), kvp.Value));
                    }
                }
                catch { }
            }
        }

        if (composites.Count > 0)
        {
            current = SubstitutePatterns(current, composites, ct);
        }

        return current;
    }

    private static bool IsIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!char.IsLetter(s[0]) && s[0] != '_') return false;
        for (int i = 1; i < s.Length; i++)
        {
            if (!char.IsLetterOrDigit(s[i]) && s[i] != '_') return false;
        }
        return true;
    }

    private record SubstitutePatternsWorkItem(IExpression Expr, bool ChildrenProcessed);

    private static IExpression SubstitutePatterns(IExpression root, List<(IExpression Pattern, IExpression Replacement)> patterns, System.Threading.CancellationToken ct)
    {
        var work = new Stack<SubstitutePatternsWorkItem>();
        work.Push(new SubstitutePatternsWorkItem(root, false));
        
        var resultStack = new Stack<IExpression>();

        while (work.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var item = work.Pop();
            var expr = item.Expr;

            // 1. Check direct match
            bool matched = false;
            foreach (var (pat, rep) in patterns)
            {
                if (expr.InternalEquals(pat))
                {
                    resultStack.Push(rep);
                    matched = true;
                    break;
                }
            }
            if (matched) continue;

            if (!item.ChildrenProcessed)
            {
                if (expr is Operation op)
                {
                    work.Push(new SubstitutePatternsWorkItem(expr, true));
                    for (int i = op.Arguments.Count - 1; i >= 0; i--)
                    {
                        work.Push(new SubstitutePatternsWorkItem(op.Arguments[i], false));
                    }
                }
                else
                {
                    resultStack.Push(expr);
                }
            }
            else
            {
                if (expr is Operation op)
                {
                    int count = op.Arguments.Count;
                    var newArgs = new IExpression[count];
                    for (int i = count - 1; i >= 0; i--)
                    {
                        newArgs[i] = resultStack.Pop();
                    }
                    
                    var next = op.WithArguments(newArgs.ToImmutableList()).Canonicalize();
                    
                    bool postMatched = false;
                    foreach (var (pat, rep) in patterns)
                    {
                        if (next.InternalEquals(pat))
                        {
                            resultStack.Push(rep);
                            postMatched = true;
                            break;
                        }
                    }
                    
                    if (!postMatched)
                    {
                        if (next is Multiply mul && patterns.Any(p => p.Pattern is Multiply))
                        {
                            next = TryMatchSubset(mul, patterns) ?? next;
                        }
                        else if (next is Add add && patterns.Any(p => p.Pattern is Add))
                        {
                            next = TryMatchSubset(add, patterns) ?? next;
                        }
                        resultStack.Push(next);
                    }
                }
            }
        }
        
        return resultStack.Pop();
    }

    private static IExpression? TryMatchSubset(Operation op, List<(IExpression Pattern, IExpression Replacement)> patterns)
    {
        // Simple greedy match: if pattern arguments are a subset of op arguments, replace them.
        var opArgs = op.Arguments.ToList();
        
        foreach (var (pat, rep) in patterns)
        {
            if (pat is Operation patOp && patOp.GetType() == op.GetType())
            {
                // check if patOp.Arguments is subset of opArgs
                var matchIndices = new List<int>();
                var tempArgs = new List<IExpression>(opArgs);
                bool match = true;

                foreach (var pArg in patOp.Arguments)
                {
                    int idx = tempArgs.FindIndex(a => a.InternalEquals(pArg));
                    if (idx < 0)
                    {
                        match = false;
                        break;
                    }
                    tempArgs.RemoveAt(idx); // Consume
                }

                if (match)
                {
                    // Found a match!
                    // Remaining args + replacement
                    tempArgs.Add(rep);
                    var newOp = op.WithArguments(tempArgs.ToImmutableList()).Canonicalize();
                    return newOp; // One match at a time for now
                }
            }
        }
        return null;
    }

    private static IExpression SubstituteSymbols(
        IExpression expr,
        ImmutableDictionary<string, IExpression> substitutions,
        IReadOnlyDictionary<string, FunctionDefinition>? functionDefinitions,
        ImmutableHashSet<string>? stack,
        System.Threading.CancellationToken ct)
    {
        if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
            Console.WriteLine($"DEBUG: SubstituteSymbols on: {expr.ToDisplayString()}");

        var results = new Stack<IExpression>();
        var work = new Stack<(IWorkItem Item, int Depth)>();
        work.Push((new Process(expr, substitutions, stack ?? EmptyStack, stack ?? EmptyStack), 0));

        while (work.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (item, depth) = work.Pop();
            
            if (depth > 100)
            {
                // Fallback for extreme depth
                if (item is Process process) results.Push(process.Expr);
                else if (item is RebuildEquality) results.Push(new Number(0m)); // Should not happen
                continue;
            }

            if (item is Process p)
            {
                var e = p.Expr;
                if (e is Symbol s && !s.Name.StartsWith("ans_", StringComparison.Ordinal) && p.Subs.TryGetValue(s.Name, out var replacement))
                {
                    if (p.SymStack.Contains(s.Name))
                    {
                        results.Push(s); // Avoid infinite recursion
                    }
                    else
                    {
                        work.Push((new Process(replacement, p.Subs, p.FnStack, p.SymStack.Add(s.Name)), depth + 1));
                    }
                    continue;
                }

                if (e is Equality eq)
                {
                    bool isFunctionDef = eq.LeftOperand is Function fnL && functionDefinitions != null && functionDefinitions.ContainsKey(fnL.Name);
                    
                    if (SymSolvers.ExpressionPropertyValidator.TryGetIsolatedSolutionSymbol(eq, out var isolatedSymbol, out var isolatedValue) && 
                        p.Subs.TryGetValue(isolatedSymbol.Name, out var subValue))
                    {
                        if (subValue.InternalEquals(isolatedValue))
                        {
                            // This is likely the defining equation for this symbol. 
                            // Preserve the LHS to keep the equality isolated.
                            work.Push((new RebuildEqualityIsolated(isolatedSymbol), depth + 1));
                            work.Push((new Process(isolatedValue, p.Subs, p.FnStack, p.SymStack.Add(isolatedSymbol.Name)), depth + 1));
                        }
                        else
                        {
                            // This is another constraint using the same symbol.
                            // Allow substitution into the LHS to eliminate the symbol.
                            work.Push((new RebuildEquality(), depth + 1));
                            work.Push((new Process(eq.RightOperand, p.Subs, p.FnStack, p.SymStack), depth + 1));
                            work.Push((new Process(eq.LeftOperand, p.Subs, p.FnStack, p.SymStack), depth + 1));
                        }
                    }
                    else if (isFunctionDef)
                    {
                        // This is a function definition f(x) == body.
                        // We must preserve the LHS f(x) to keep the definition in the system,
                        // and only substitute into the body.
                        var fnName = eq.LeftOperand is Function fnLeft ? fnLeft.Name : string.Empty;
                        var nextFnStack = string.IsNullOrWhiteSpace(fnName) ? p.FnStack : p.FnStack.Add(fnName);
                        work.Push((new RebuildEquality(), depth + 1));
                        work.Push((new Process(eq.RightOperand, p.Subs, p.FnStack, p.SymStack), depth + 1));
                        work.Push((new Process(eq.LeftOperand, ImmutableDictionary<string, IExpression>.Empty, nextFnStack, p.SymStack), depth + 1));
                    }
                    else
                    {
                        work.Push((new RebuildEquality(), depth + 1));
                        // Push in reverse order of processing so they are popped in correct order
                        work.Push((new Process(eq.RightOperand, p.Subs, p.FnStack, p.SymStack), depth + 1));
                        work.Push((new Process(eq.LeftOperand, p.Subs, p.FnStack, p.SymStack), depth + 1));
                    }
                }
                else if (e is Function fn)
                {
                    bool isQuantifier = (fn.Name.Equals("forall", StringComparison.OrdinalIgnoreCase) || 
                                         fn.Name.Equals("exists", StringComparison.OrdinalIgnoreCase)) && 
                                        fn.Arguments.Count == 3;

                    if (isQuantifier && fn.Arguments[0] is Symbol boundVar)
                    {
                        var subsWithoutBoundVar = p.Subs.Remove(boundVar.Name);
                        work.Push((new RebuildFunction(fn, p.Subs, p.FnStack, p.SymStack), depth + 1));
                        
                        // Predicate (arg 2)
                        work.Push((new Process(fn.Arguments[2], subsWithoutBoundVar, p.FnStack, p.SymStack), depth + 1));
                        // Domain (arg 1)
                        work.Push((new Process(fn.Arguments[1], p.Subs, p.FnStack, p.SymStack), depth + 1));
                        // Bound Variable (arg 0)
                        work.Push((new Process(fn.Arguments[0], ImmutableDictionary<string, IExpression>.Empty, p.FnStack, p.SymStack), depth + 1));
                    }
                    else
                    {
                        work.Push((new RebuildFunction(fn, p.Subs, p.FnStack, p.SymStack), depth + 1));
                        for (int i = fn.Arguments.Count - 1; i >= 0; i--)
                        {
                            work.Push((new Process(fn.Arguments[i], p.Subs, p.FnStack, p.SymStack), depth + 1));
                        }
                    }
                }
                else if (e is Operation op)
                {
                    work.Push((new RebuildOperation(op), depth + 1));
                    for (int i = op.Arguments.Count - 1; i >= 0; i--)
                    {
                        work.Push((new Process(op.Arguments[i], p.Subs, p.FnStack, p.SymStack), depth + 1));
                    }
                }
                else
                {
                    results.Push(e);
                }
            }
            else if (item is RebuildEqualityIsolated rei)
            {
                var rhs = results.Pop();
                results.Push(new Equality(rei.Lhs, rhs));
            }
            else if (item is RebuildEquality)
            {
                // results has [..., Left, Right]. Top is Right.
                var right = results.Pop();
                var left = results.Pop();
                results.Push(new Equality(left, right));
            }
            else if (item is RebuildOperation ro)
            {
                int count = ro.Original.Arguments.Count;
                var args = new IExpression[count];
                // results has [..., Arg0, Arg1, ..., ArgN]. Top is ArgN.
                bool anyChanged = false;
                for (int i = count - 1; i >= 0; i--)
                {
                    args[i] = results.Pop();
                    if (!ReferenceEquals(args[i], ro.Original.Arguments[i])) anyChanged = true;
                }
                results.Push(anyChanged ? ro.Original.WithArguments(args.ToImmutableList()) : ro.Original);
            }
            else if (item is RebuildFunction rf)
            {
                int count = rf.Original.Arguments.Count;
                var args = new IExpression[count];
                bool anyChanged = false;
                for (int i = count - 1; i >= 0; i--)
                {
                    args[i] = results.Pop();
                    if (!ReferenceEquals(args[i], rf.Original.Arguments[i])) anyChanged = true;
                }
                var updated = anyChanged ? rf.Original.WithArguments(args.ToImmutableList()) : rf.Original;
                
                if (updated is Function updatedFn && functionDefinitions != null && functionDefinitions.TryGetValue(updatedFn.Name, out var def) && updatedFn.Arguments.Count == def.Parameters.Length)
                {
                    if (rf.FnStack.Contains(updatedFn.Name))
                    {
                        results.Push(updatedFn);
                    }
                    else
                    {
                        var withParams = rf.Subs;
                        for (int i = 0; i < def.Parameters.Length; i++) withParams = withParams.SetItem(def.Parameters[i].Name, updatedFn.Arguments[i]);
                        work.Push((new FinalizeFunction(), depth + 1));
                        work.Push((new Process(def.Body, withParams, rf.FnStack.Add(updatedFn.Name), rf.SymStack), depth + 1));
                    }
                }
                else
                {
                    results.Push(updated);
                }
            }
            else if (item is FinalizeFunction)
            {
                var body = results.Pop();
                results.Push(body.Canonicalize());
            }
        }
        return results.Pop().Canonicalize();
    }

    private static int CountNodes(IExpression expr)
    {
        if (expr is Operation op)
        {
            int count = 1;
            foreach (var arg in op.Arguments) count += CountNodes(arg);
            return count;
        }
        return 1;
    }
}
