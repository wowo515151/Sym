// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using Sym.Core.Rewriters;

namespace SymSolvers;

/// <summary>
/// Solves equations by matching the structure of a symbolic expression against a ground value.
/// Example: a * Sqrt(b) / c == 2 * Sqrt(15) / 5  => {a=2, b=15, c=5}
/// </summary>
public class StructuralMatchStrategy : ISolverStrategy
{
    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (context is null) return SolveResult.Failure(problem, "SolveContext cannot be null.");
        if (problem is null) return SolveResult.Failure(null, "Problem expression cannot be null.");

        var targets = new HashSet<string>(StringComparer.Ordinal);
        if (context.AdditionalData is not null && 
            context.AdditionalData.TryGetValue(SolverOptionKeys.TargetVariables, out var raw) && 
            raw is IEnumerable<string> list)
        {
            foreach (var t in list) if (t != null) targets.Add(t.Trim());
        }

        if (context.TargetVariable is not null) targets.Add(context.TargetVariable.Name);

        if (targets.Count == 0) return SolveResult.Failure(problem, "No target variables for structural matching.");

        bool debug = Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1";
        if (debug)
        {
            Console.WriteLine($"StructuralMatchStrategy (DEBUG): Targets=[{string.Join(",", targets)}]");
            Console.WriteLine($"Problem: {problem}");
        }

        var allEqualities = ExtractEqualities(problem);
        if (debug)
        {
            Console.WriteLine($"Equalities found: {allEqualities.Count}");
            foreach(var e in allEqualities) Console.WriteLine($" - {e}");
        }

        if (allEqualities.Count == 0) return SolveResult.Failure(problem, "No equalities found for structural matching.");

        bool anyMatched = false;
        var assignments = new Dictionary<string, IExpression>(StringComparer.Ordinal);

        foreach (var eq in allEqualities)
        {
            if (debug) Console.WriteLine($"StructuralMatchStrategy: Checking eq: {eq.ToDisplayString()}");
            // 1. Direct match
            var res = TryMatch(eq.LeftOperand, eq.RightOperand, targets, context.EnableTracing || debug, context.CancellationToken);
            if (res is null) res = TryMatch(eq.RightOperand, eq.LeftOperand, targets, context.EnableTracing || debug, context.CancellationToken);

            if (debug && res != null) Console.WriteLine($"StructuralMatchStrategy: Match found for {eq.ToDisplayString()}. Bindings: {string.Join(", ", res.Select(kv => kv.Key + "=" + kv.Value.ToDisplayString()))}");

            // 2. System match (if direct match fails and this equality contains targets)
            if (res is null && ContainsAny(eq, targets))
            {
                res = TryMatchSystem(eq, allEqualities, targets, context.EnableTracing || debug, context.CancellationToken);
                if (debug && res != null) Console.WriteLine($"StructuralMatchStrategy: System match found. Bindings: {string.Join(", ", res.Select(kv => kv.Key + "=" + kv.Value.ToDisplayString()))}");
            }

            if (res is not null && res.Count > 0)
            {
                bool meaningful = false;
                foreach (var kv in res)
                {
                    // A match is meaningful if the RHS is not a Symbol that is also a target,
                    // or if it's a Number, or a more complex expression that doesn't contain targets.
                    bool rhsIsTarget = kv.Value is Symbol sVal && targets.Contains(sVal.Name);
                    bool rhsContainsTargets = kv.Value.ContainsSymbol(s => targets.Contains(s.Name));
                    
                    if (!kv.Value.InternalEquals(new Symbol(kv.Key)) && (!rhsIsTarget || !rhsContainsTargets))
                    {
                        if (debug) Console.WriteLine($"StructuralMatchStrategy: Meaningful binding: {kv.Key} = {kv.Value.ToDisplayString()}");
                        assignments[kv.Key] = kv.Value;
                        meaningful = true;
                    }
                }
                if (meaningful) anyMatched = true;
            }
        }

        if (anyMatched)
        {
            var solutions = assignments.Select(kv => new Equality(new Symbol(kv.Key), kv.Value).Canonicalize()).ToList();
            
            IExpression resultExpr;
            if (problem is Vector v)
            {
                var newArgs = v.Arguments.ToList();
                foreach (var sol in solutions)
                {
                    if (!newArgs.Any(a => a.InternalEquals(sol))) newArgs.Add(sol);
                }
                resultExpr = new Vector(newArgs.ToImmutableList()).Canonicalize();
            }
            else
            {
                var newList = new List<IExpression> { problem };
                foreach (var sol in solutions)
                {
                    if (!newList.Any(a => a.InternalEquals(sol))) newList.Add(sol);
                }
                resultExpr = new Vector(newList.ToImmutableList()).Canonicalize();
            }
            
            return SolveResult.Success(resultExpr, "Structural match applied.");
        }

        return SolveResult.Failure(problem, "Structural match failed.");
    }

    private static bool ContainsAny(IExpression expr, HashSet<string> symbols)
    {
        if (expr is Symbol s) 
        {
            bool found = symbols.Contains(s.Name);
            if (found && Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                Console.WriteLine($"DEBUG: ContainsAny found target: {s.Name}");
            return found;
        }
        if (expr is Operation op) return op.Arguments.Any(a => ContainsAny(a, symbols));
        return false;
    }

    private static Dictionary<string, IExpression>? TryMatchSystem(Equality template, List<Equality> system, HashSet<string> targets, bool enableTracing, System.Threading.CancellationToken ct)
    {
        // Find other side of template that is NOT a target
        IExpression pattern;
        IExpression nameExpr;

        if (ContainsAny(template.RightOperand, targets) && !ContainsAny(template.LeftOperand, targets))
        {
            pattern = template.RightOperand;
            nameExpr = template.LeftOperand;
        }
        else if (ContainsAny(template.LeftOperand, targets) && !ContainsAny(template.RightOperand, targets))
        {
            pattern = template.LeftOperand;
            nameExpr = template.RightOperand;
        }
        else return null;

        foreach (var eq in system)
        {
            if (ReferenceEquals(eq, template)) continue;

            IExpression? ground = null;
            if (eq.LeftOperand.InternalEquals(nameExpr)) ground = eq.RightOperand;
            else if (eq.RightOperand.InternalEquals(nameExpr)) ground = eq.LeftOperand;

            if (ground != null)
            {
                var res = TryMatch(pattern, ground, targets, enableTracing, ct);
                if (res != null) return res;
            }
        }

        return null;
    }

    private static bool IsConstraintBundle(Add add)
    {
        return add.Arguments.All(a => a is Equality || a is Symbol);
    }

    private static List<Equality> ExtractEqualities(IExpression expr)
    {
        var list = new List<Equality>();
        if (expr is Equality eq) list.Add(eq);
        else if (expr is Vector v) foreach (var a in v.Arguments) list.AddRange(ExtractEqualities(a));
        else if (expr is Add add) foreach (var a in add.Arguments) list.AddRange(ExtractEqualities(a));
        return list;
    }

    private static Dictionary<string, IExpression>? TryMatch(IExpression patternTemplate, IExpression ground, HashSet<string> targetNames, bool enableTracing = false, System.Threading.CancellationToken ct = default)
    {
        // Convert symbols in targetNames to Wilds in the pattern
        var wildPattern = ToWild(patternTemplate, targetNames);
        // If there were no wilds produced, then there were no target symbols in the pattern; bail out.
        if (!ContainsWild(wildPattern)) return null; 
        
        // Avoid trivial identity matches like x == x
        if (wildPattern.InternalEquals(ground)) return null;

        if (enableTracing) Console.WriteLine($"TryMatch: Pattern={wildPattern}, Ground={ground}");

        var match = Sym.Core.Rewriters.Rewriter.TryMatch(ground, wildPattern);
        if (match.Success)
        {
            return match.Bindings.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        return null;
    }

    private static IExpression ToWild(IExpression expr, HashSet<string> targetNames)
    {
        if (expr is Symbol s && targetNames.Contains(s.Name))
        {
            return new Wild(s.Name);
        }

        if (expr is Operation op)
        {
            var newArgs = op.Arguments.Select(arg => ToWild(arg, targetNames)).ToImmutableList();
            return op.WithArguments(newArgs).Canonicalize();
        }

        return expr;
    }
    private static bool IsNonInjective(string name)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
        { 
            "mod", "gcd", "lcm", "abs", "sin", "cos", "tan", "sec", "csc", "cot", "floor", "ceil", "round", "pow", "sqr"
        };
        return set.Contains(name);
    }

    private static bool ContainsWild(IExpression expr)
    {
        if (expr is Wild) return true;
        if (expr is Operation op) return op.Arguments.Any(ContainsWild);
        return false;
    }
}
