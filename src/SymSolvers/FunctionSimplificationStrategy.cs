// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymCore;

namespace SymSolvers;

/// <summary>
/// Simplifies common function identities conservatively (odd/even trig, constant reductions).
/// </summary>
public class FunctionSimplificationStrategy : ISolverStrategy, INamedSolverStrategy
{
    public string Name => "FunctionSimplificationStrategy";
    private readonly List<RulePackStrategy> _rulePackStrategies = new();

    public FunctionSimplificationStrategy()
    {
        var packs = SymRules.RulePackLibrary.GetRulePacks();
        foreach (var pack in packs.OrderByDescending(p => p.Priority))
        {
            if (pack.EnabledByDefault) _rulePackStrategies.Add(new RulePackStrategy(pack));
        }
    }

    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (context is null) return SolveResult.Failure(problem, "SolveContext cannot be null.");
        if (problem is null) return SolveResult.Failure(null, "Problem expression cannot be null.");

        IExpression current = problem;
        
        // 1. Apply rules from all enabled packs
        foreach (var strategy in _rulePackStrategies)
        {
            var res = strategy.Solve(current, context);
            if (res.IsSuccess && res.ResultExpression != null)
            {
                current = res.ResultExpression;
            }
        }

        // 2. Apply Piecewise simplification and other non-rule logic
        var simplified = SimplifyFunctions(current, context);
        
        // 3. Ensure root itself is simplified (SimplifyFunctions recurses, but doesn't necessarily hit the final root with ApplySimplifications if it's already a complex op)
        simplified = ApplySimplifications(simplified, context);

        if (context.EnableTracing)
        {
             Console.WriteLine($"DEBUG: FunctionSimplificationStrategy: {problem} -> {simplified}");
        }

        var changed = !simplified.InternalEquals(problem);
        var trace = context.EnableTracing ? ImmutableList.Create(problem, simplified) : null;
        var message = changed ? "Function identities simplified." : "No changes performed.";
        return SolveResult.Success(simplified, message, trace);
    }

    private record SimplifyFunctionsWorkItem(IExpression Expr, bool ChildrenProcessed);

    private static IExpression SimplifyFunctions(IExpression root, SolveContext context)
    {
        var work = new Stack<SimplifyFunctionsWorkItem>();
        work.Push(new SimplifyFunctionsWorkItem(root, false));
        var results = new Stack<IExpression>();

        while (work.Count > 0)
        {
            var item = work.Pop();
            var expr = item.Expr;

            if (!item.ChildrenProcessed)
            {
                if (expr is Operation op)
                {
                    work.Push(new SimplifyFunctionsWorkItem(expr, true));
                    for (int i = op.Arguments.Count - 1; i >= 0; i--)
                    {
                        work.Push(new SimplifyFunctionsWorkItem(op.Arguments[i], false));
                    }
                }
                else
                {
                    results.Push(expr);
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
                        newArgs[i] = results.Pop();
                    }
                    var rebuilt = op.WithArguments(newArgs.ToImmutableList()).Canonicalize();
                    results.Push(ApplySimplifications(rebuilt, context));
                }
            }
        }
        return results.Pop();
    }

    private static IExpression ApplySimplifications(IExpression rebuilt, SolveContext context)
    {
        if (rebuilt is not Number && !rebuilt.ContainsSymbol(_ => true))
        {
            if (NumericEvaluator.TryEvaluate(rebuilt, ImmutableDictionary<string, decimal>.Empty, out var val, out _))
            {
                rebuilt = new Number(val);
            }
        }

        if (rebuilt is Multiply or Divide) return rebuilt.Canonicalize();

        if (rebuilt is Number nDec && nDec.Value % 1 != 0)
        {
             try
             {
                 var rat = Rational.FromDecimal(nDec.Value);
                 if (rat.Denominator < 10000) return rat.ToExpression();
             }
             catch { }
        }

        if (rebuilt is Equality eq)
        {
            IExpression lhs = eq.LeftOperand;
            IExpression rhs = eq.RightOperand;
            if (rhs is Multiply && lhs is not Multiply) (lhs, rhs) = (rhs, lhs);

            if (IsSquareRoot(lhs, out var lhsArg) && IsSquareRoot(rhs, out var rhsArg)) return new Equality(lhsArg, rhsArg).Canonicalize();

            if (lhs is Function fn1 && rhs is Function fn2 && fn1.Name.Equals(fn2.Name, StringComparison.OrdinalIgnoreCase) && fn1.Arguments.Count == 1 && fn2.Arguments.Count == 1)
            {
                var name = fn1.Name.ToLowerInvariant();
                if (name == "exp" || name == "log" || name == "ln") return new Equality(fn1.Arguments[0], fn2.Arguments[0]).Canonicalize();
            }
            
            if (lhs is Multiply mul)
            {
                if (IsSquareRoot(rhs, out var rhsInner))
                {
                    var insideTerms = new List<IExpression>();
                    bool possible = true;
                    foreach (var arg in mul.Arguments)
                    {
                        if (IsSquareRoot(arg, out var inner)) insideTerms.Add(inner);
                        else if (arg is Number n && n.Value > 0) insideTerms.Add(new Power(n, new Number(2)).Canonicalize());
                        else { possible = false; break; }
                    }
                    if (possible && insideTerms.Count > 0) return new Equality(new Multiply(insideTerms.ToImmutableList()).Canonicalize(), rhsInner).Canonicalize();
                }
                else if (rhs is Function fnR && fnR.Arguments.Count == 1 && fnR.Name.Equals("exp", StringComparison.OrdinalIgnoreCase))
                {
                    var insideSum = new List<IExpression>();
                    bool possible = true;
                    foreach (var arg in mul.Arguments)
                    {
                        if (arg is Function f && f.Name.Equals("exp", StringComparison.OrdinalIgnoreCase) && f.Arguments.Count == 1) insideSum.Add(f.Arguments[0]);
                        else if (arg is Number n && n.Value > 0) insideSum.Add(new Function("log", ImmutableList.Create((IExpression)n)).Canonicalize());
                        else { possible = false; break; }
                    }
                    if (possible && insideSum.Count > 0) return new Equality(new Add(insideSum.ToImmutableList()).Canonicalize(), fnR.Arguments[0]).Canonicalize();
                }
            }
        }

        if (rebuilt is Function fn)
        {
            var name = fn.Name.ToLowerInvariant();
            var arg = fn.Arguments.Count == 1 ? fn.Arguments[0] : null;

            if (name == "min" || name == "max")
            {
                var args = fn.Arguments;
                if (args.Count == 1 && args[0] is Vector v) args = v.Arguments;
                if (args.Count >= 1 && args.All(a => a is Number))
                {
                    var nums = args.Cast<Number>().Select(n => n.Value);
                    return new Number(name == "min" ? nums.Min() : nums.Max());
                }
            }

            if (name == "sort" && fn.Arguments.Count == 1 && fn.Arguments[0] is Vector vSort && vSort.Arguments.All(a => a is Number))
            {
                return new Vector(vSort.Arguments.Cast<Number>().OrderBy(n => n.Value).Cast<IExpression>().ToImmutableList()).Canonicalize();
            }

            if (name == "median" && fn.Arguments.Count == 1 && fn.Arguments[0] is Vector vMed && vMed.Arguments.All(a => a is Number))
            {
                var sorted = vMed.Arguments.Cast<Number>().OrderBy(n => n.Value).ToList();
                int count = sorted.Count;
                if (count == 0) return rebuilt;
                if (count % 2 != 0) return sorted[count / 2];
                return new Number((sorted[count / 2 - 1].Value + sorted[count / 2].Value) / 2m);
            }

            if (name == "index" && fn.Arguments.Count >= 2 && fn.Arguments[0] is Vector vec && fn.Arguments[1] is Number idxNum)
            {
                int idx = (int)idxNum.Value;
                if (idx == 0 && vec.Arguments.Count > 0) return vec.Arguments[0].Canonicalize();
                if (idx >= 1 && idx <= vec.Arguments.Count) return vec.Arguments[idx - 1].Canonicalize();
            }

            if (name == "tocommonfraction" && arg is Number nFrac)
            {
                try { var rat = Rational.FromDecimal(nFrac.Value); if (rat.Denominator < 10000) return rat.ToExpression(); } catch { }
                return nFrac;
            }

            if (arg is Number nDecF && nDecF.Value % 1 != 0)
            {
                 try { var rat = Rational.FromDecimal(nDecF.Value); if (rat.Denominator < 10000) return rat.ToExpression(); } catch { }
            }

            if (name == "expand" && fn.Arguments.Count == 1)
            {
                return RuleBasedExpansion.Expand(fn.Arguments[0], context);
            }

            if (name == "coefficient" && fn.Arguments.Count == 3 && fn.Arguments[1] is Symbol varSym && fn.Arguments[2] is Number powNum)
            {
                if (CoefficientUtility.TryGetCoefficient(fn.Arguments[0], varSym, (int)powNum.Value, out var coeff)) return coeff;
            }

            if (name == "length" && fn.Arguments.Count == 1 && fn.Arguments[0] is Symbol sLen)
            {
                var sName = sLen.Name;
                if (sName.StartsWith("\"") && sName.EndsWith("\"") && sName.Length >= 2) return new Number(sName.Length - 2);
            }

            if (name == "substring" && fn.Arguments.Count == 3 && fn.Arguments[0] is Symbol sSub && sSub.Name.StartsWith("\"") && sSub.Name.EndsWith("\"") && sSub.Name.Length >= 2 && fn.Arguments[1] is Number nStart && fn.Arguments[2] is Number nLen)
            {
                var str = sSub.Name.Substring(1, sSub.Name.Length - 2);
                int start = (int)nStart.Value - 1;
                if (start >= 0 && start < str.Length) return new Symbol("\"" + str.Substring(start, Math.Min((int)nLen.Value, str.Length - start)) + "\"");
            }

            if ((name == "ceiling" || name == "ceil") && arg is Number cNum) return new Number(Math.Ceiling(cNum.Value));
            if (name == "floor" && arg is Number fNum) return new Number(Math.Floor(fNum.Value));

            if ((name == "gcd" || name == "lcm") && fn.Arguments.Count >= 2 && fn.Arguments.All(a => a is Number))
            {
                var nums = fn.Arguments.Cast<Number>().Select(n => (long)Math.Abs(n.Value)).ToList();
                long res = nums[0];
                for (int i = 1; i < nums.Count; i++) {
                    if (name == "gcd") res = Gcd(res, nums[i]);
                    else res = (res == 0 || nums[i] == 0) ? 0 : Math.Abs(res * nums[i]) / Gcd(res, nums[i]);
                }
                return new Number((decimal)res);
            }

            if (name == "factorial" && arg is Number factNum) try { return new Number(NumericEvaluator.Evaluate(fn, ImmutableDictionary<string, decimal>.Empty)); } catch { }

            // Factorial/Combination patterns
            if (rebuilt is Multiply mulFact && mulFact.Arguments.Count == 2)
            {
                var numF = mulFact.Arguments.OfType<Number>().FirstOrDefault();
                var factExp = mulFact.Arguments.FirstOrDefault(a => IsFactorial(a, out _));
                if (numF != null && factExp != null && IsFactorial(factExp, out var nArgF) && nArgF is Number nValF && numF.Value > 0 && numF.Value < 1)
                {
                    decimal m = 1m / numF.Value;
                    if (Math.Abs(m - Math.Round(m)) < 1e-9m && nValF.Value > 0 && nValF.Value % (long)Math.Round(m) == 0)
                        return new Multiply(new Number(nValF.Value / (long)Math.Round(m)), new Function("factorial", new Number(nValF.Value - 1))).Canonicalize();
                }
            }

            if (TryGetNumeratorDenominator(rebuilt, out var numR, out var denR) && IsFactorial(numR, out var nExpr))
            {
                var denTerms = denR is Multiply m ? m.Arguments.ToList() : new List<IExpression> { denR };
                foreach (var argF in denTerms)
                {
                    if (IsFactorial(argF, out var kExpr))
                    {
                        var restTerms = new List<IExpression>(denTerms);
                        restTerms.Remove(argF);
                        foreach (var subArg in restTerms)
                        {
                            if (IsFactorial(subArg, out var subDiffExpr) && subDiffExpr.InternalEquals(new Subtract(nExpr, kExpr).Canonicalize()))
                            {
                                restTerms.Remove(subArg);
                                var otherExpr = restTerms.Count == 0 ? new Number(1) : (restTerms.Count == 1 ? restTerms[0] : new Multiply(restTerms.ToImmutableList()).Canonicalize());
                                var comb = new Function("Combination", nExpr, kExpr).Canonicalize();
                                return (otherExpr is Number nOne && nOne.Value == 1m) ? comb : new Divide(comb, otherExpr).Canonicalize();
                            }
                        }
                    }
                }
            }

            if (rebuilt is Divide divFact && IsFactorial(divFact.Numerator, out var a1) && IsFactorial(divFact.Denominator, out var a2))
            {
                if (a1 is Number n1 && a2 is Number n2 && n1.Value == n2.Value + 1) return n1;
                if (a1 is Add ad1 && ad1.Arguments.Any(a => a.InternalEquals(a2)) && ad1.Arguments.Any(a => a is Number na && na.Value == 1) && ad1.Arguments.Count == 2) return a1;
                if (a2 is Add ad2 && ad2.Arguments.Any(a => a.InternalEquals(a1)) && ad2.Arguments.Any(a => a is Number na && na.Value == 1) && ad2.Arguments.Count == 2) return new Divide(new Number(1), a2).Canonicalize();
            }

            if (fn.Arguments.Count > 0 && fn.Arguments.All(a => a is Number))
            {
                if (NumericEvaluator.TryEvaluate(fn, ImmutableDictionary<string, decimal>.Empty, out var val, out _)) return new Number(val);
            }

            if (name == "abs" && arg is Number absNum) return new Number(decimal.Abs(absNum.Value));
            if (name is "sign" or "sgn" && arg is Number signNum) return new Number((decimal)Math.Sign(signNum.Value));
            if (name == "mod" && fn.Arguments.Count == 2 && fn.Arguments[0] is Number modLeft && fn.Arguments[1] is Number modRight && modRight.Value != 0m) return new Number(modLeft.Value % modRight.Value);
        }

        if (rebuilt is Piecewise pw) return SimplifyPiecewise(pw, context);
        return rebuilt;
    }

    private static bool IsSquareRoot(IExpression expr, out IExpression arg)
    {
        arg = null!;
        if (expr is Function f && f.Name.Equals("sqrt", StringComparison.OrdinalIgnoreCase) && f.Arguments.Count == 1) { arg = f.Arguments[0]; return true; }
        if (expr is Power p && p.Exponent is Number en && en.Value == 0.5m) { arg = p.Base; return true; }
        return false;
    }

    private static bool IsFactorial(IExpression expr, out IExpression arg)
    {
        arg = null!;
        if (expr is Function f && f.Name.Equals("factorial", StringComparison.OrdinalIgnoreCase) && f.Arguments.Count == 1) { arg = f.Arguments[0]; return true; }
        return false;
    }

    private static bool TryGetNumeratorDenominator(IExpression expr, out IExpression numerator, out IExpression denominator)
    {
        numerator = expr; denominator = new Number(1);
        if (expr is Divide div) { numerator = div.Numerator; denominator = div.Denominator; return true; }
        if (expr is Multiply mul)
        {
            var numTerms = new List<IExpression>(); var denTerms = new List<IExpression>();
            foreach (var arg in mul.Arguments) { if (arg is Power p && p.Exponent is Number n && n.Value == -1m) denTerms.Add(p.Base); else numTerms.Add(arg); }
            if (denTerms.Count > 0)
            {
                numerator = numTerms.Count == 0 ? new Number(1) : (numTerms.Count == 1 ? numTerms[0] : new Multiply(numTerms.ToImmutableList()).Canonicalize());
                denominator = denTerms.Count == 0 ? new Number(1) : (denTerms.Count == 1 ? denTerms[0] : new Multiply(denTerms.ToImmutableList()).Canonicalize());
                return true;
            }
        }
        return false;
    }

    private static IExpression SimplifyPiecewise(Piecewise pw, SolveContext context)
    {
        var args = pw.Arguments; if (args.Count == 0) return pw;
        for (int i = 0; i < args.Count; i += 2)
        {
            if (i + 1 < args.Count)
            {
                var value = args[i]; var cond = args[i + 1];
                if (!LooksLikeCondition(cond) && LooksLikeCondition(value)) (value, cond) = (cond, value);
                if (TryEvaluateCondition(cond, context, out bool result)) { if (result) return value; } else return pw;
            }
            else return args[i];
        }
        return pw;
    }

    private static bool LooksLikeCondition(IExpression expr)
    {
        if (expr is Symbol s) return s.Name.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Name.Equals("false", StringComparison.OrdinalIgnoreCase);
        if (expr is Equality) return true;
        if (expr is Function fn)
        {
            var name = fn.Name.ToLowerInvariant();
            if (ExpressionClassification.IsInequalityExpression(fn) || name is "and" or "or" or "not" or "implies" or "iff" or "eq" or "equals") return true;
        }
        return false;
    }

    private static bool TryEvaluateCondition(IExpression cond, SolveContext context, out bool result)
    {
        result = false;
        if (cond is Symbol s && s.Name.Equals("true", StringComparison.OrdinalIgnoreCase)) { result = true; return true; }
        if (cond is Symbol s2 && s2.Name.Equals("false", StringComparison.OrdinalIgnoreCase)) { result = false; return true; }
        var assignments = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (context.AdditionalData != null && context.AdditionalData.TryGetValue(SolverOptionKeys.Substitutions, out var subsObj) && subsObj is ImmutableDictionary<string, IExpression> subs)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var (k, v) in subs)
                {
                    if (assignments.ContainsKey(k)) continue;
                    if (NumericEvaluator.TryEvaluate(v, assignments, out var val, out _)) { assignments[k] = val; changed = true; }
                }
            }
        }
        var numericAssignments = assignments.ToImmutableDictionary();
        if (cond is Equality eq && NumericEvaluator.TryEvaluate(eq.LeftOperand, numericAssignments, out var left, out _) && NumericEvaluator.TryEvaluate(eq.RightOperand, numericAssignments, out var right, out _)) { result = Math.Abs(left - right) <= 1e-9m; return true; }
        if (cond is Function f)
        {
            var name = f.Name.ToLowerInvariant();
            if (name == "and")
            {
                foreach (var arg in f.Arguments) if (!TryEvaluateCondition(arg, context, out bool res) || !res) { result = false; return res == false; }
                result = true; return true;
            }
            if (name == "or")
            {
                foreach (var arg in f.Arguments) if (TryEvaluateCondition(arg, context, out bool res) && res) { result = true; return true; }
                return false; 
            }
            if (ExpressionClassification.IsInequalityExpression(f) && f.Arguments.Count == 2 && NumericEvaluator.TryEvaluate(f.Arguments[0], numericAssignments, out var v1, out _) && NumericEvaluator.TryEvaluate(f.Arguments[1], numericAssignments, out var v2, out _))
            {
                result = name switch { "gt" => v1 > v2, "lt" => v1 < v2, "ge" => v1 >= v2, "le" => v1 <= v2, "ne" => Math.Abs(v1 - v2) > 1e-9m, _ => false };
                return true;
            }
            if (name is "eq" or "equals" && f.Arguments.Count == 2 && NumericEvaluator.TryEvaluate(f.Arguments[0], numericAssignments, out var ve1, out _) && NumericEvaluator.TryEvaluate(f.Arguments[1], numericAssignments, out var ve2, out _)) { result = Math.Abs(ve1 - ve2) <= 1e-9m; return true; }
            if (name == "not" && f.Arguments.Count == 1 && TryEvaluateCondition(f.Arguments[0], context, out var inner)) { result = !inner; return true; }
            if (name == "implies" && f.Arguments.Count == 2 && TryEvaluateCondition(f.Arguments[0], context, out var l) && TryEvaluateCondition(f.Arguments[1], context, out var r)) { result = !l || r; return true; }
            if (name == "iff" && f.Arguments.Count == 2 && TryEvaluateCondition(f.Arguments[0], context, out var ll) && TryEvaluateCondition(f.Arguments[1], context, out var rr)) { result = ll == rr; return true; }
        }
        return false;
    }

    private static long Gcd(long a, long b) { while (b != 0) { a %= b; (a, b) = (b, a); } return a; }
}
