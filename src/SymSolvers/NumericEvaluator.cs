// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers.Numerics;
using SymCore;

namespace SymSolvers;

public static class NumericEvaluator
{
    static NumericEvaluator()
    {
        Sym.Core.Rewriters.Rewriter.RegisterEvaluator(EvaluateFunction);
    }

    public static decimal EvaluateFunction(string name, decimal[] args)
    {
        var fn = new Function(name, args.Select(a => (IExpression)new Number(a)).ToImmutableList());
        return EvaluateFunction(fn, ImmutableDictionary<string, decimal>.Empty);
    }

    private static readonly System.Threading.ThreadLocal<IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>?> _currentSymbolAttributes = new();

    public static bool TryEvaluate(IExpression expr, IReadOnlyDictionary<string, decimal> assignments, out decimal result, out string? error, IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>? symbolAttributes = null)
    {
        var old = _currentSymbolAttributes.Value;
        _currentSymbolAttributes.Value = symbolAttributes ?? old;
        try
        {
            result = Evaluate(expr, assignments);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException || ex is TimeoutException) throw;

            if (!ex.Message.Contains("Missing assignment") && 
                !ex.Message.Contains("Unsupported function") && 
                !ex.Message.Contains("Unsupported expression type") &&
                !ex.Message.Contains("requires an interval or vector domain"))
            {
                Logging.LogError("NumericEvaluatorTryEvaluate", ex.Message, $"Expression: {expr.ToDisplayString()}\nStack Trace: {ex.StackTrace}");
            }
            result = 0;
            error = ex.Message;
            return false;
        }
        finally
        {
            _currentSymbolAttributes.Value = old;
        }
    }

    public static bool TryEvaluateCondition(
        IExpression condition,
        IReadOnlyDictionary<string, decimal> assignments,
        out bool result,
        out string? error,
        bool allowSymbolAssignments = false,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>? symbolAttributes = null)
    {
        var old = _currentSymbolAttributes.Value;
        _currentSymbolAttributes.Value = symbolAttributes ?? old;
        try
        {
            result = EvaluateCondition(condition, assignments, allowSymbolAssignments);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException || ex is TimeoutException) throw;

            if (!ex.Message.Contains("Missing assignment") && 
                !ex.Message.Contains("Unsupported function") && 
                !ex.Message.Contains("Unsupported expression type") &&
                !ex.Message.Contains("requires an interval or vector domain"))
            {
                Logging.LogError("NumericEvaluatorTryEvaluateCondition", ex.Message, $"Condition: {condition.ToDisplayString()}\nStack Trace: {ex.StackTrace}");
            }
            result = false;
            error = ex.Message;
            return false;
        }
        finally
        {
            _currentSymbolAttributes.Value = old;
        }
    }

    public static decimal SafeToDecimal(double d) => SymCore.NumericConvert.SafeToDecimal(d);

    public static decimal Evaluate(IExpression expr, IReadOnlyDictionary<string, decimal> assignments)
    {
        return EvaluateRecursive(expr, assignments);
    }

    private static double EvaluateAsDouble(IExpression expr, IReadOnlyDictionary<string, decimal> assignments)
    {
        switch (expr)
        {
            case Number n: return (double)n.Value;
            case Symbol s:
                if (assignments.TryGetValue(s.Name, out var v)) return (double)v;
                if (s.Name.Equals("pi", StringComparison.OrdinalIgnoreCase)) return Math.PI;
                if (s.Name == "e") return Math.E;
                return 0.0;
            case Add add: return add.Arguments.Sum(a => EvaluateAsDouble(a, assignments));
            case Subtract sub: return EvaluateAsDouble(sub.LeftOperand, assignments) - EvaluateAsDouble(sub.RightOperand, assignments);
            case Multiply mul: return mul.Arguments.Aggregate(1.0, (p, a) => p * EvaluateAsDouble(a, assignments));
            case Divide div: return EvaluateAsDouble(div.Numerator, assignments) / EvaluateAsDouble(div.Denominator, assignments);
            case Power pow: return Math.Pow(EvaluateAsDouble(pow.Base, assignments), EvaluateAsDouble(pow.Exponent, assignments));
            case Function fn:
                var name = fn.Name.ToLowerInvariant();
                if (name == "exp") return Math.Exp(EvaluateAsDouble(fn.Arguments[0], assignments));
                if (name == "pow") return Math.Pow(EvaluateAsDouble(fn.Arguments[0], assignments), EvaluateAsDouble(fn.Arguments[1], assignments));
                if (name == "sqrt") return Math.Sqrt(EvaluateAsDouble(fn.Arguments[0], assignments));
                return (double)EvaluateFunction(fn, assignments);
            default: return (double)EvaluateRecursive(expr, assignments);
        }
    }

    private static decimal EvaluateRecursive(IExpression expr, IReadOnlyDictionary<string, decimal> assignments)
    {
        switch (expr)
        {
            case Number n:
                return n.Value;
            case Symbol s:
                if (assignments.TryGetValue(s.Name, out var v)) return v;
                if (s.Name.Equals("pi", StringComparison.OrdinalIgnoreCase)) return SafeToDecimal(Math.PI);
                if (s.Name == "e") return SafeToDecimal(Math.E);
                if (s.Name.Equals("true", StringComparison.OrdinalIgnoreCase)) return 1m;
                if (s.Name.Equals("false", StringComparison.OrdinalIgnoreCase)) return 0m;

                // Special case for attribute symbols like Fact.Rel
                if (s.Name.Contains('.') && _currentSymbolAttributes.Value != null)
                {
                    var parts = s.Name.Split('.');
                    if (parts.Length == 2 && _currentSymbolAttributes.Value.TryGetValue(parts[0], out var attrs) && attrs.TryGetValue(parts[1], out var attrVal))
                    {
                        return (decimal)attrVal;
                    }
                }

                throw new InvalidOperationException($"Missing assignment for symbol '{s.Name}'.");
            case Add add:
            {
                decimal sum = 0m;
                try
                {
                    foreach (var arg in add.Arguments)
                    {
                        sum += Evaluate(arg, assignments);
                    }
                    return sum;
                }
                catch (Exception ex) when (ex is OverflowException || ex.Message.Contains("large or too small", StringComparison.OrdinalIgnoreCase))
                {
                    return SafeToDecimal(EvaluateAsDouble(add, assignments));
                }
            }
            case Subtract sub:
                try { return Evaluate(sub.LeftOperand, assignments) - Evaluate(sub.RightOperand, assignments); }
                catch (Exception ex) when (ex is OverflowException || ex.Message.Contains("large or too small", StringComparison.OrdinalIgnoreCase)) { 
                    return SafeToDecimal(EvaluateAsDouble(sub, assignments));
                }
            case Multiply mul:
            {
                decimal prod = 1m;
                try
                {
                    foreach (var arg in mul.Arguments)
                    {
                        var val = Evaluate(arg, assignments);
                        if ((val == 0m && (prod == decimal.MaxValue || prod == decimal.MinValue)) ||
                            (prod == 0m && (val == decimal.MaxValue || val == decimal.MinValue)))
                        {
                            throw new OverflowException("Indeterminate form 0 * Infinity encountered in multiplication.");
                        }
                        prod *= val;
                    }
                    return prod;
                }
                catch (Exception ex) when (ex is OverflowException || ex.Message.Contains("large or too small", StringComparison.OrdinalIgnoreCase))
                {
                    return SafeToDecimal(EvaluateAsDouble(mul, assignments));
                }
            }
            case Divide div:
            {
                var numerator = Evaluate(div.Numerator, assignments);
                var denominator = Evaluate(div.Denominator, assignments);
                if (denominator == 0m) throw new DivideByZeroException("Division by zero in evaluation.");

                if (numerator % 1m == 0m && denominator % 1m == 0m)
                {
                    var absN = Math.Abs(numerator);
                    var absD = Math.Abs(denominator);
                    if (absN <= long.MaxValue && absD <= long.MaxValue)
                    {
                        long ln = (long)absN;
                        long ld = (long)absD;

                        static long Gcd(long a, long b)
                        {
                            while (b != 0)
                            {
                                long t = a % b;
                                a = b;
                                b = t;
                            }
                            return Math.Abs(a);
                        }

                        if (ln != 0 && ld != 0)
                        {
                            var g = Gcd(ln, ld);
                            if (g > 1)
                            {
                                numerator /= g;
                                denominator /= g;
                            }
                        }
                    }
                }

                try { return numerator / denominator; }
                catch (Exception ex) when (ex is OverflowException || ex.Message.Contains("large or too small", StringComparison.OrdinalIgnoreCase)) { 
                    return SafeToDecimal(EvaluateAsDouble(div, assignments));
                }
            }
            case Power pow:
            {
                var @base = Evaluate(pow.Base, assignments);
                var exp = Evaluate(pow.Exponent, assignments);
                
                if (exp == (long)exp)
                {
                    long iExp = (long)exp;
                    if (iExp == 0) return 1m;
                    if (iExp == 1) return @base;
                    if (iExp == -1 && @base != 0m) return 1m / @base;
                    
                    if (iExp > 0 && iExp < 1000)
                    {
                        if (@base % 1m == 0m)
                        {
                            var b = new System.Numerics.BigInteger(@base);
                            var resBig = System.Numerics.BigInteger.Pow(b, (int)iExp);
                            if (resBig <= new System.Numerics.BigInteger(decimal.MaxValue) && resBig >= new System.Numerics.BigInteger(decimal.MinValue))
                            {
                                return (decimal)resBig;
                            }
                            // DO NOT cap to decimal.MaxValue here, let it throw or be handled by double fallback
                            throw new OverflowException("BigInteger power result exceeds decimal range.");
                        }

                        decimal res = 1m;
                        try
                        {
                            for (int i = 0; i < iExp; i++) res = checked(res * @base);
                            return res;
                        }
                        catch (OverflowException) { throw; }
                    }
                    if (iExp < 0 && iExp > -1000 && @base != 0m)
                    {
                        if (@base % 1m == 0m)
                        {
                            var b = new System.Numerics.BigInteger(@base);
                            var resBig = System.Numerics.BigInteger.Pow(b, (int)-iExp);
                            if (resBig == 0) throw new DivideByZeroException();
                            if (resBig <= new System.Numerics.BigInteger(decimal.MaxValue))
                            {
                                return 1m / (decimal)resBig;
                            }
                            return 0m; // Extremely large denominator -> 0
                        }

                        decimal res = 1m;
                        try
                        {
                            for (int i = 0; i < -iExp; i++) res = checked(res * @base);
                            return 1m / res;
                        }
                        catch (OverflowException) { throw; }
                    }
                }

                try
                {
                    double dExp = (double)exp;
                    if (Math.Abs(dExp - 1.0/3.0) < 1e-10 || Math.Abs(dExp + 1.0/3.0) < 1e-10)
                    {
                        double res = Math.Cbrt((double)@base);
                        if (dExp < 0) res = 1.0 / res;
                        return SafeToDecimal(res);
                    }

                    double resPow = Math.Pow((double)@base, dExp);
                    if (double.IsInfinity(resPow) || double.IsNaN(resPow))
                    {
                        throw new OverflowException("Power result is infinity or NaN and cannot be represented as decimal.");
                    }
                    if (Math.Abs(resPow - Math.Round(resPow)) < 1e-10)
                    {
                        // Integer-valued result, try to return as decimal safely
                        var rounded = Math.Round(resPow);
                        return SafeToDecimal(rounded);
                    }
                    return SafeToDecimal(resPow);
                }
                catch (Exception ex) when (ex is OverflowException || ex.Message.Contains("large or too small", StringComparison.OrdinalIgnoreCase))
                {
                    try { Console.WriteLine($"NUMERIC_OVERFLOW: Power encountered overflow/underflow evaluating {pow.ToDisplayString()} with base={@base} exp={exp} -> {ex.Message}"); } catch { }
                    double dRes = Math.Pow((double)@base, (double)exp);
                    if (double.IsInfinity(dRes) || double.IsNaN(dRes) || dRes > (double)decimal.MaxValue || dRes < (double)decimal.MinValue)
                    {
                         throw new OverflowException("Result of power is outside decimal range.", ex);
                    }
                    return (decimal)dRes;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Power evaluation failed: {ex.Message}");
                }
            }
            case Function fn:
                return EvaluateFunction(fn, assignments);
            case Piecewise pw:
            {
                if (pw.Arguments.Count == 0)
                    throw new InvalidOperationException("Invalid Piecewise structure.");

                for (int i = 0; i < pw.Arguments.Count; i += 2)
                {
                    if (i + 1 < pw.Arguments.Count)
                    {
                        // Piecewise arguments are stored as [value1, condition1, value2, condition2, ...].
                        // Evaluate the guard at i+1 and return the value at i when it matches.
                        if (EvaluateCondition(pw.Arguments[i + 1], assignments, allowSymbolAssignments: false))
                        {
                            return Evaluate(pw.Arguments[i], assignments);
                        }
                    }
                    else
                    {
                        return Evaluate(pw.Arguments[i], assignments);
                    }
                }
                throw new InvalidOperationException("No matching case in Piecewise evaluation.");
            }
            case Attr attr:
                return EvaluateAttr(attr);
            case DotProduct dp:
                if (dp.LeftOperand is Symbol dpTarget && dp.RightOperand is Symbol dpAttr)
                {
                    return EvaluateAttr(new Attr(dpTarget, dpAttr));
                }
                throw new InvalidOperationException("DotProduct not supported for numeric evaluation unless it is attribute access.");
            default:
                throw new InvalidOperationException($"Unsupported expression type '{expr.GetType().Name}' for numeric evaluation.");
        }
    }

    private static decimal EvaluateAttr(Attr attr)
    {
        var symbolAttributes = _currentSymbolAttributes.Value;
        if (symbolAttributes == null) throw new InvalidOperationException("Symbol attributes not provided for Attr evaluation.");
        
        if (attr.Target is not Symbol targetSym) throw new InvalidOperationException("Attr target must be a symbol for numeric evaluation.");
        if (attr.AttributeName is not Symbol attrNameSym) throw new InvalidOperationException("Attr attribute name must be a symbol.");

        string attrName = attrNameSym.Name;
        if (attrName.StartsWith('\"') && attrName.EndsWith('\"') && attrName.Length >= 2)
        {
            attrName = attrName[1..^1];
        }

        if (symbolAttributes.TryGetValue(targetSym.Name, out var attrs) && attrs.TryGetValue(attrName, out var val))
        {
            return (decimal)val;
        }

        throw new InvalidOperationException($"Attribute '{attrName}' not found for symbol '{targetSym.Name}'.");
    }

    private static int CompareNumeric(IExpression e1, IExpression e2, IReadOnlyDictionary<string, decimal> assignments)
    {
        try
        {
            decimal d1 = Evaluate(e1, assignments);
            decimal d2 = Evaluate(e2, assignments);

            if ((d1 == decimal.MaxValue || d1 == decimal.MinValue) &&
                (d2 == decimal.MaxValue || d2 == decimal.MinValue))
            {
                // Both capped at extremes, use double to break tie
                double db1 = EvaluateAsDouble(e1, assignments);
                double db2 = EvaluateAsDouble(e2, assignments);
                return db1.CompareTo(db2);
            }

            return d1.CompareTo(d2);
        }
        catch
        {
            // Fallback to double if decimal fails completely
            double db1 = EvaluateAsDouble(e1, assignments);
            double db2 = EvaluateAsDouble(e2, assignments);
            return db1.CompareTo(db2);
        }
    }

    private static bool EvaluateCondition(IExpression condition, IReadOnlyDictionary<string, decimal> assignments, bool allowSymbolAssignments)
    {
        // Piecewise conditions are frequently encoded as Equalities.
        // Handle those before Function-based conditions.
        if (condition is Equality eqCond)
        {
            return CompareNumeric(eqCond.LeftOperand, eqCond.RightOperand, assignments) == 0;
        }

        if (condition is Symbol s)
        {
            if (s.Name.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Name.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            if (allowSymbolAssignments && assignments.TryGetValue(s.Name, out var symVal))
            {
                return symVal != 0m;
            }
        }

        if (condition is Function fn)
        {
            var name = fn.Name.ToLowerInvariant();

            return name switch
            {
                "gt" => CompareNumeric(fn.Arguments[0], fn.Arguments[1], assignments) > 0,
                "lt" => CompareNumeric(fn.Arguments[0], fn.Arguments[1], assignments) < 0,
                "ge" => CompareNumeric(fn.Arguments[0], fn.Arguments[1], assignments) >= 0,
                "le" => CompareNumeric(fn.Arguments[0], fn.Arguments[1], assignments) <= 0,
                "eq" => CompareNumeric(fn.Arguments[0], fn.Arguments[1], assignments) == 0,
                "ne" => CompareNumeric(fn.Arguments[0], fn.Arguments[1], assignments) != 0,
                "and" => fn.Arguments.All(a => EvaluateCondition(a, assignments, allowSymbolAssignments)),
                "or" => fn.Arguments.Any(a => EvaluateCondition(a, assignments, allowSymbolAssignments)),
                "not" => !EvaluateCondition(fn.Arguments[0], assignments, allowSymbolAssignments),
                "implies" => !EvaluateCondition(fn.Arguments[0], assignments, allowSymbolAssignments) || EvaluateCondition(fn.Arguments[1], assignments, allowSymbolAssignments),
                "iff" => EvaluateCondition(fn.Arguments[0], assignments, allowSymbolAssignments) == EvaluateCondition(fn.Arguments[1], assignments, allowSymbolAssignments),
                "forall" => EvaluateQuantifier(fn, assignments, true),
                "exists" => EvaluateQuantifier(fn, assignments, false),
                "integer" or "isinteger" => Evaluate(fn.Arguments[0], assignments) % 1 == 0,
                "isrational" => IsRational(fn.Arguments[0], assignments),
                "isirrational" => !IsRational(fn.Arguments[0], assignments),
                "count" or "length" or "sum" or "product" => Evaluate(fn, assignments) != 0m,
                _ => throw new InvalidOperationException($"Unsupported condition function '{fn.Name}'.")
            };
        }

        throw new InvalidOperationException($"Unsupported condition type '{condition.GetType().Name}'.");
    }

    private static bool EvaluateQuantifier(Function fn, IReadOnlyDictionary<string, decimal> assignments, bool isForAll)
    {
        if (fn.Arguments.Count != 3) throw new InvalidOperationException($"{fn.Name} requires 3 arguments: variable, domain, predicate.");
        if (fn.Arguments[0] is not Symbol varSym) throw new InvalidOperationException($"{fn.Name} first argument must be a symbol.");

        var domain = fn.Arguments[1];
        var predicate = fn.Arguments[2];

        if (domain is Function intervalFn && intervalFn.Name.Equals("interval", StringComparison.OrdinalIgnoreCase))
        {
            var startVal = Evaluate(intervalFn.Arguments[0], assignments);
            var endVal = Evaluate(intervalFn.Arguments[1], assignments);
            var start = (long)Math.Ceiling(startVal);
            var end = (long)Math.Floor(endVal);

            if (end - start > 10000) throw new InvalidOperationException("Quantifier interval too large for numeric evaluation.");

            var loopAssignments = new Dictionary<string, decimal>(assignments, StringComparer.Ordinal);
            for (long i = start; i <= end; i++)
            {
                loopAssignments[varSym.Name] = (decimal)i;
                bool ok = EvaluateCondition(predicate, loopAssignments, allowSymbolAssignments: false);
                if (isForAll && !ok) return false;
                if (!isForAll && ok) return true;
            }
            return isForAll;
        }
        else if (domain is Vector vec)
        {
            var loopAssignments = new Dictionary<string, decimal>(assignments, StringComparer.Ordinal);
            foreach (var item in vec.Arguments)
            {
                loopAssignments[varSym.Name] = Evaluate(item, assignments);
                bool ok = EvaluateCondition(predicate, loopAssignments, allowSymbolAssignments: false);
                if (isForAll && !ok) return false;
                if (!isForAll && ok) return true;
            }
            return isForAll;
        }

        throw new InvalidOperationException($"{fn.Name} requires an interval or vector domain.");
    }

    private static decimal EvaluateFunction(Function fn, IReadOnlyDictionary<string, decimal> assignments)
    {
        var name = fn.Name.ToLowerInvariant();
        decimal arg(int i) => Evaluate(fn.Arguments[i], assignments);

        switch (name)
        {
            case "rel":
            case "cert":
            case "tokens":
                if (_currentSymbolAttributes.Value != null && fn.Arguments.Count == 1 && fn.Arguments[0] is Symbol s)
                {
                    if (_currentSymbolAttributes.Value.TryGetValue(s.Name, out var attrs) && attrs.TryGetValue(name, out var val))
                    {
                        return (decimal)val;
                    }
                }
                throw new InvalidOperationException($"Attribute '{name}' not found or invalid usage for '{fn.ToDisplayString()}'.");
            case "sin":
                return SafeToDecimal(Math.Sin((double)arg(0)));
            case "cos":
                return SafeToDecimal(Math.Cos((double)arg(0)));
            case "tan":
                return SafeToDecimal(Math.Tan((double)arg(0)));
            case "asin":
                return SafeToDecimal(Math.Asin((double)arg(0)));
            case "acos":
                return SafeToDecimal(Math.Acos((double)arg(0)));
            case "atan":
                return SafeToDecimal(Math.Atan((double)arg(0)));
            case "csc": 
            {
                var valValue = Math.Sin((double)arg(0));
                if (valValue == 0) throw new DivideByZeroException("csc(x) where sin(x) == 0");
                return SafeToDecimal(1.0 / valValue);
            }
            case "sec":
            {
                var valValue = Math.Cos((double)arg(0));
                if (valValue == 0) throw new DivideByZeroException("sec(x) where cos(x) == 0");
                return SafeToDecimal(1.0 / valValue);
            }
            case "cot":
            {
                var valValue = Math.Tan((double)arg(0));
                if (valValue == 0) throw new DivideByZeroException("cot(x) where tan(x) == 0");
                return SafeToDecimal(1.0 / valValue);
            }
            case "cscd":
            {
                var a = arg(0) % 360m;
                if (a < 0) a += 360m;
                if (a == 0m || a == 180m || a == 360m) throw new DivideByZeroException("cscd(x) undefined at 0/180/360 degrees.");
                var valValue = Math.Sin((double)arg(0) * Math.PI / 180.0);
                return SafeToDecimal(1.0 / valValue);
            }
            case "secd":
            {
                var a = arg(0) % 360m;
                if (a < 0) a += 360m;
                if (a == 90m || a == 270m) throw new DivideByZeroException("secd(x) undefined at 90/270 degrees.");
                var valValue = Math.Cos((double)arg(0) * Math.PI / 180.0);
                return SafeToDecimal(1.0 / valValue);
            }
            case "cotd":
            {
                var a = arg(0) % 360m;
                if (a < 0) a += 360m;
                if (a == 0m || a == 180m || a == 360m) throw new DivideByZeroException("cotd(x) undefined at 0/180/360 degrees.");
                if (a == 90m || a == 270m) return 0m;
                var valValue = Math.Tan((double)arg(0) * Math.PI / 180.0);
                return SafeToDecimal(1.0 / valValue);
            }
            case "sind":
            {
                var a = arg(0) % 360m;
                if (a < 0) a += 360m;
                if (a == 0m || a == 180m || a == 360m) return 0m;
                if (a == 90m) return 1m;
                if (a == 270m) return -1m;
                if (a == 30m || a == 150m) return 0.5m;
                if (a == 210m || a == 330m) return -0.5m;
                return SafeToDecimal(Math.Sin((double)arg(0) * Math.PI / 180.0));
            }
            case "cosd":
            {
                var a = arg(0) % 360m;
                if (a < 0) a += 360m;
                if (a == 0m || a == 360m) return 1m;
                if (a == 180m) return -1m;
                if (a == 90m || a == 270m) return 0m;
                if (a == 60m || a == 300m) return 0.5m;
                if (a == 120m || a == 240m) return -0.5m;
                return SafeToDecimal(Math.Cos((double)arg(0) * Math.PI / 180.0));
            }
            case "tand":
            {
                var a = arg(0) % 360m;
                if (a < 0) a += 360m;
                if (a == 0m || a == 180m || a == 360m) return 0m;
                if (a == 45m || a == 225m) return 1m;
                if (a == 135m || a == 315m) return -1m;
                if (a == 90m || a == 270m) throw new DivideByZeroException("Tangent undefined at 90/270 degrees.");
                return SafeToDecimal(Math.Tan((double)arg(0) * Math.PI / 180.0));
            }
            case "exp":
                return SafeToDecimal(Math.Exp((double)arg(0)));
            case "ln":
                return SafeToDecimal(Math.Log((double)arg(0)));
            case "sumproperdivisors":
            {
                long n = (long)arg(0);
                if (n <= 1) return 0m;
                long sum = 1;
                long limit = (long)Math.Sqrt(n);
                for (long i = 2; i <= limit; i++)
                {
                    if (n % i == 0)
                    {
                        sum += i;
                        if (i != n / i)
                        {
                            sum += n / i;
                        }
                    }
                }
                return (decimal)sum;
            }
            case "log10":
                return SafeToDecimal(Math.Log10((double)arg(0)));
            case "log":
            {
                if (fn.Arguments.Count == 2)
                {
                    double @base = (double)arg(0);
                    double x = (double)arg(1);
                    if (double.IsNaN(x) || double.IsInfinity(x) || x <= 0.0)
                        throw new InvalidOperationException("Log undefined for non-positive or non-finite argument.");
                    if (double.IsNaN(@base) || double.IsInfinity(@base) || @base <= 0.0 || Math.Abs(@base - 1.0) < 1e-15)
                        throw new InvalidOperationException("Log base must be positive, finite and not equal to 1.");
                    double resValue = Math.Log(x, @base);
                    if (Math.Abs(resValue - Math.Round(resValue)) < 1e-10) return SafeToDecimal(Math.Round(resValue));
                    return SafeToDecimal(resValue);
                }
                else
                {
                    double x = (double)arg(0);
                    if (double.IsNaN(x) || double.IsInfinity(x) || x <= 0.0)
                        throw new InvalidOperationException("Log undefined for non-positive or non-finite argument.");
                    double resValue = Math.Log(x);
                    if (Math.Abs(resValue - Math.Round(resValue)) < 1e-10) return SafeToDecimal(Math.Round(resValue));
                    return SafeToDecimal(resValue);
                }
            }
            case "length":
            case "norm":
            {
                if (fn.Arguments.Count == 1)
                {
                    if (TryEvaluateVector(fn.Arguments[0], assignments, out var vNorm))
                    {
                        double sumSqValue = 0;
                        foreach (var aValue in vNorm.Arguments)
                        {
                            double vValue = (double)Evaluate(aValue, assignments);
                            sumSqValue += vValue * vValue;
                        }
                        return SafeToDecimal(Math.Sqrt(sumSqValue));
                    }
                    if (fn.Arguments[0] is Vector vLen) return (decimal)vLen.Arguments.Count;
                }
                return (decimal)fn.Arguments.Count;
            }
            case "dotproduct":
            {
                if (fn.Arguments.Count == 2)
                {
                    if (TryEvaluateVector(fn.Arguments[0], assignments, out var v1) && 
                        TryEvaluateVector(fn.Arguments[1], assignments, out var v2))
                    {
                        decimal dotValue = 0;
                        int cValue = Math.Min(v1.Arguments.Count, v2.Arguments.Count);
                        for (int i = 0; i < cValue; i++) dotValue += Evaluate(v1.Arguments[i], assignments) * Evaluate(v2.Arguments[i], assignments);
                        return dotValue;
                    }
                }
                throw new InvalidOperationException("DotProduct requires two vector-valued arguments.");
            }
            case "count":
            {
                if (fn.Arguments.Count == 1)
                {
                    return EvaluateCondition(fn.Arguments[0], assignments, allowSymbolAssignments: false) ? 1m : 0m;
                }
                if (fn.Arguments.Count == 2)
                {
                    if (fn.Arguments[0] is not Symbol varSym2) throw new InvalidOperationException("Count variable must be a symbol.");
                    var condition = fn.Arguments[1];
                    
                    IExpression? nValExpr = null;
                    void FindDivisorConstraint(IExpression e)
                    {
                        if (e is Function f && f.Name.Equals("and", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (var a in f.Arguments) FindDivisorConstraint(a);
                        }
                        else if (e is Equality eq && eq.RightOperand is Number n0 && n0.Value == 0 &&
                                 eq.LeftOperand is Function fm && fm.Name.Equals("mod", StringComparison.OrdinalIgnoreCase) &&
                                 fm.Arguments[1].InternalEquals(varSym2))
                        {
                            nValExpr = fm.Arguments[0];
                        }
                        else if (e is Function fc && fc.Name.Equals("eq", StringComparison.OrdinalIgnoreCase) &&
                                 fc.Arguments[1] is Number n0_ && n0_.Value == 0 &&
                                 fc.Arguments[0] is Function fm_ && fm_.Name.Equals("mod", StringComparison.OrdinalIgnoreCase) &&
                                 fm_.Arguments[1].InternalEquals(varSym2))
                        {
                            nValExpr = fm_.Arguments[0];
                        }
                    }
                    FindDivisorConstraint(condition);

                    if (nValExpr != null && NumericEvaluator.TryEvaluate(nValExpr, assignments, out var nVal, out _))
                    {
                        int count2Value = 0;
                        var loopAssignments = new Dictionary<string, decimal>(assignments, StringComparer.Ordinal);
                        foreach (var d in GetDivisors((long)nVal))
                        {
                            loopAssignments[varSym2.Name] = (decimal)d;
                            if (EvaluateCondition(condition, loopAssignments, allowSymbolAssignments: false)) count2Value++;
                        }
                        return (decimal)count2Value;
                    }
                    
                    throw new InvalidOperationException("Count(variable, condition) requires an inferrable domain (e.g. mod(n, var) == 0).");
                }
                if (fn.Arguments.Count == 3)
                {
                    if (fn.Arguments[0] is not Symbol varSym3) throw new InvalidOperationException("Count variable must be a symbol.");
                    var domain = fn.Arguments[1];
                    var condition = fn.Arguments[2];

                    int count3Value = 0;
                    var loopAssignments = new Dictionary<string, decimal>(assignments, StringComparer.Ordinal);

                    if (domain is Function intervalFn && intervalFn.Name.Equals("interval", StringComparison.OrdinalIgnoreCase))
                    {
                        var startVal = Evaluate(intervalFn.Arguments[0], assignments);
                        var endVal = Evaluate(intervalFn.Arguments[1], assignments);
                        var start = (long)Math.Ceiling(startVal);
                        var end = (long)Math.Floor(endVal);
                        for (long i = start; i <= end; i++)
                        {
                            loopAssignments[varSym3.Name] = (decimal)i;
                            if (EvaluateCondition(condition, loopAssignments, allowSymbolAssignments: false)) count3Value++;
                        }
                    }
                    else if (domain is Vector vec)
                    {
                        foreach (var item in vec.Arguments)
                        {
                            loopAssignments[varSym3.Name] = Evaluate(item, assignments);
                            if (EvaluateCondition(condition, loopAssignments, allowSymbolAssignments: false)) count3Value++;
                        }
                    }
                    else if (domain is Function fDomain && fDomain.Name.Equals("divisors", StringComparison.OrdinalIgnoreCase))
                    {
                        var nVal = Evaluate(fDomain.Arguments[0], assignments);
                        foreach (var d in GetDivisors((long)nVal))
                        {
                            loopAssignments[varSym3.Name] = (decimal)d;
                            if (EvaluateCondition(condition, loopAssignments, allowSymbolAssignments: false)) count3Value++;
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("Count requires an interval, vector, or divisors domain.");
                    }
                    return (decimal)count3Value;
                }
                if (fn.Arguments.Count != 4) throw new InvalidOperationException("Count requires 1, 3 or 4 arguments.");
                if (fn.Arguments[1] is not Symbol varSymCount) throw new InvalidOperationException("Count variable must be a symbol.");
                
                var startVal4 = Evaluate(fn.Arguments[2], assignments);
                var endVal4 = Evaluate(fn.Arguments[3], assignments);
                
                var start4 = (long)Math.Ceiling(startVal4);
                var end4 = (long)Math.Floor(endVal4);
                
                int count4Value = 0;
                var loopAssignments4 = new Dictionary<string, decimal>(assignments, StringComparer.Ordinal);
                
                for (long i = start4; i <= end4; i++)
                {
                    loopAssignments4[varSymCount.Name] = (decimal)i;
                    if (EvaluateCondition(fn.Arguments[0], loopAssignments4, allowSymbolAssignments: false))
                    {
                        count4Value++;
                    }
                }
                return (decimal)count4Value;
            }
            case "sqrt":
                return SafeToDecimal(Math.Sqrt((double)arg(0)));
            case "distance":
            {
                if (fn.Arguments.Count == 2)
                {
                    if (TryEvaluateVector(fn.Arguments[0], assignments, out var v1) && 
                        TryEvaluateVector(fn.Arguments[1], assignments, out var v2))
                    {
                        double sumSq = 0;
                        int count = Math.Max(v1.Arguments.Count, v2.Arguments.Count);
                        for (int i = 0; i < count; i++)
                        {
                            double x1 = i < v1.Arguments.Count ? (double)Evaluate(v1.Arguments[i], assignments) : 0;
                            double x2 = i < v2.Arguments.Count ? (double)Evaluate(v2.Arguments[i], assignments) : 0;
                            double diff = x1 - x2;
                            sumSq += diff * diff;
                        }
                        return SafeToDecimal(Math.Sqrt(sumSq));
                    }
                }
                throw new InvalidOperationException("Distance requires two vector-valued arguments.");
            }
            case "pow":
                if (fn.Arguments.Count != 2) throw new InvalidOperationException("pow expects exactly 2 arguments.");
                return SafeToDecimal(Math.Pow((double)arg(0), (double)arg(1)));
            case "log1p":
                return SafeToDecimal(SpecialMath.Log1p((double)arg(0)));
            case "expm1":
                return SafeToDecimal(SpecialMath.Expm1((double)arg(0)));
            case "softplus":
            {
                var xValue = (double)arg(0);
                var maxValue = Math.Max(0, xValue);
                var termValue = SpecialMath.Log1p(Math.Exp(-Math.Abs(xValue)));
                return SafeToDecimal(maxValue + termValue);
            }
            case "logsumexp":
            {
                var valsValue = fn.Arguments.Select(a => (double)Evaluate(a, assignments)).ToArray();
                var maxValue = valsValue.Length == 0 ? double.NegativeInfinity : valsValue.Max();
                if (double.IsInfinity(maxValue)) return SafeToDecimal(maxValue);
                double sumValue = 0;
                foreach (var vValue in valsValue) sumValue += Math.Exp(vValue - maxValue);
                return SafeToDecimal(maxValue + Math.Log(sumValue));
            }
            case "abs":
                return Math.Abs(arg(0));
            case "round":
            {
                if (fn.Arguments.Count == 1) return Math.Round(arg(0));
                if (fn.Arguments.Count == 2) return Math.Round(arg(0), (int)arg(1));
                throw new InvalidOperationException("Round expects 1 or 2 arguments.");
            }
            case "floor":
                return Math.Floor(arg(0));
            case "ceiling":
            case "ceil":
                return Math.Ceiling(arg(0));
            case "sign":
            case "sgn":
            {
                var valueValue = arg(0);
                return valueValue > 0m ? 1m : valueValue < 0m ? -1m : 0m;
            }
            case "if":
            {
                if (fn.Arguments.Count != 3) throw new InvalidOperationException("'if' requires 3 arguments.");
                return EvaluateCondition(fn.Arguments[0], assignments, allowSymbolAssignments: false) ? arg(1) : arg(2);
            }
            case "mod":
            {
                if (fn.Arguments.Count != 2) throw new InvalidOperationException("Mod requires two arguments.");
                var leftValue = arg(0);
                var rightValue = arg(1);
                if (rightValue == 0m) throw new DivideByZeroException("Modulo by zero in evaluation.");
                var resValue = leftValue % rightValue;
                if (resValue < 0) resValue += rightValue;
                return resValue;
            }
            case "dayofweekname":
            {
                var valValue = (int)arg(0);
                // Assume 0=Sunday, 1=Monday, ..., 6=Saturday
                var namesValue = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
                var indexValue = valValue % 7;
                if (indexValue < 0) indexValue += 7;
                return (decimal)indexValue;
            }
            case "abspow":
            {
                if (fn.Arguments.Count != 2) throw new InvalidOperationException("absPow requires two arguments.");
                var baseValValue = Math.Abs(arg(0));
                var expValValue = arg(1);
                return SafeToDecimal(Math.Pow((double)baseValValue, (double)expValValue));
            }
            case "countzeros":
            {
                var valValue = arg(0);
                var sValue = Math.Abs(valValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return (decimal)sValue.Count(cValue => cValue == '0');
            }
            case "gcd":
            case "greatestcommondivisor":
            {
                if (fn.Arguments.Count < 2) throw new InvalidOperationException("Gcd requires at least two arguments.");
                long resValue = (long)arg(0);
                for (int i = 1; i < fn.Arguments.Count; i++) resValue = Gcd(resValue, (long)arg(i));
                return (decimal)resValue;
            }
            case "lcm":
            case "leastcommonmultiple":
            {
                if (fn.Arguments.Count < 2) throw new InvalidOperationException("Lcm requires at least two arguments.");
                long resValue = (long)arg(0);
                for (int i = 1; i < fn.Arguments.Count; i++)
                {
                    long nextValue = (long)arg(i);
                    long gValue = Gcd(resValue, nextValue);
                    if (gValue == 0) resValue = 0;
                    else resValue = Math.Abs(resValue * nextValue) / gValue;
                }
                return (decimal)resValue;
            }
            case "min":
            {
                if (fn.Arguments.Count == 0) throw new InvalidOperationException("Min requires at least one argument.");
                if (fn.Arguments.Count == 1 && fn.Arguments[0] is Vector vecValue)
                {
                    if (vecValue.Arguments.Count == 0) throw new InvalidOperationException("Min(Vector) requires at least one element.");
                    var minValValue = Evaluate(vecValue.Arguments[0], assignments);
                    for (int i = 1; i < vecValue.Arguments.Count; i++)
                    {
                        minValValue = Math.Min(minValValue, Evaluate(vecValue.Arguments[i], assignments));
                    }
                    return minValValue;
                }
                else
                {
                    var minValValue = arg(0);
                    for (int i = 1; i < fn.Arguments.Count; i++)
                    {
                        minValValue = Math.Min(minValValue, arg(i));
                    }
                    return minValValue;
                }
            }
            case "max":
            {
                if (fn.Arguments.Count == 0) throw new InvalidOperationException("Max requires at least one argument.");
                if (fn.Arguments.Count == 1 && fn.Arguments[0] is Vector vecValue)
                {
                    if (vecValue.Arguments.Count == 0) throw new InvalidOperationException("Max(Vector) requires at least one element.");
                    var maxValValue = Evaluate(vecValue.Arguments[0], assignments);
                    for (int i = 1; i < vecValue.Arguments.Count; i++)
                    {
                        maxValValue = Math.Max(maxValValue, Evaluate(vecValue.Arguments[i], assignments));
                    }
                    return maxValValue;
                }
                else
                {
                    var maxValValue = arg(0);
                    for (int i = 1; i < fn.Arguments.Count; i++)
                    {
                        maxValValue = Math.Max(maxValValue, arg(i));
                    }
                    return maxValValue;
                }
            }
            case "median":
            {
                var listValue = GetValues(fn, assignments);
                if (listValue.Count == 0) return 0m;
                listValue.Sort();
                int midValue = listValue.Count / 2;
                if (listValue.Count % 2 != 0) return listValue[midValue];
                return (listValue[midValue - 1] + listValue[midValue]) / 2m;
            }
            case "mean":
            case "average":
            {
                var listValue = GetValues(fn, assignments);
                if (listValue.Count == 0) return 0m;
                return listValue.Average();
            }
            case "sort":
            {
                var listValue = GetValues(fn, assignments);
                listValue.Sort();
                var elementsValue = listValue.Select(dValue => (IExpression)new Number(dValue)).ToImmutableList();
                throw new UnsupportedFunctionVectorResultException(new Vector(elementsValue));
            }
            case "largestprimefactor":
            {
                var val = arg(0);
                if (val > (decimal)long.MaxValue || val < (decimal)long.MinValue)
                {
                    var bi = new System.Numerics.BigInteger(Math.Abs(val));
                    return (decimal)GetPrimeFactors(bi).Max();
                }
                return (decimal)GetPrimeFactors((long)val).Max();
            }
            case "primefactors":
            {
                var val = arg(0);
                IEnumerable<System.Numerics.BigInteger> factors;
                if (val > (decimal)long.MaxValue || val < (decimal)long.MinValue)
                {
                    factors = GetPrimeFactors(new System.Numerics.BigInteger(Math.Abs(val)));
                }
                else
                {
                    factors = GetPrimeFactors((long)val).Select(f => new System.Numerics.BigInteger(f));
                }
                var elementsValue = factors.Select(fValue => (IExpression)new Number(SafeToDecimal((double)fValue))).ToImmutableList();
                throw new UnsupportedFunctionVectorResultException(new Vector(elementsValue));
            }
            case "divisors":
            {
                var val = arg(0);
                IEnumerable<System.Numerics.BigInteger> divisors;
                if (val > (decimal)long.MaxValue || val < (decimal)long.MinValue)
                {
                    divisors = GetDivisors(new System.Numerics.BigInteger(Math.Abs(val)));
                }
                else
                {
                    divisors = GetDivisors((long)val).Select(d => new System.Numerics.BigInteger(d));
                }
                var divisorsValue = divisors.OrderBy(d => d).Select(d => (IExpression)new Number(SafeToDecimal((double)d))).ToImmutableList();
                throw new UnsupportedFunctionVectorResultException(new Vector(divisorsValue));
            }
            case "determinant":
            {
                if (fn.Arguments.Count == 1 && fn.Arguments[0] is Matrix matValue)
                {
                    var dimsValue = matValue.MatrixDimensions;
                    if (dimsValue.Length == 2)
                    {
                        if (dimsValue[0] == 2 && dimsValue[1] == 2)
                        {
                            decimal aValue = Evaluate(matValue.Arguments[0], assignments);
                            decimal bValue = Evaluate(matValue.Arguments[1], assignments);
                            decimal cValue = Evaluate(matValue.Arguments[2], assignments);
                            decimal dValue = Evaluate(matValue.Arguments[3], assignments);
                            return aValue * dValue - bValue * cValue;
                        }
                        if (dimsValue[0] == 3 && dimsValue[1] == 3)
                        {
                            decimal aValue = Evaluate(matValue.Arguments[0], assignments);
                            decimal bValue = Evaluate(matValue.Arguments[1], assignments);
                            decimal cValue = Evaluate(matValue.Arguments[2], assignments);
                            decimal dValue = Evaluate(matValue.Arguments[3], assignments);
                            decimal eValue = Evaluate(matValue.Arguments[4], assignments);
                            decimal fValue = Evaluate(matValue.Arguments[5], assignments);
                            decimal gValue = Evaluate(matValue.Arguments[6], assignments);
                            decimal hValue = Evaluate(matValue.Arguments[7], assignments);
                            decimal iValue = Evaluate(matValue.Arguments[8], assignments);
                            return aValue * (eValue * iValue - fValue * hValue) - bValue * (dValue * iValue - fValue * gValue) + cValue * (dValue * hValue - eValue * gValue);
                        }
                    }
                }
                throw new InvalidOperationException("Determinant currently only supports 2x2 and 3x3 numeric matrices.");
            }
            case "binomial":
            case "combination":
            {
                if (fn.Arguments.Count != 2) throw new InvalidOperationException("Binomial/Combination requires two arguments.");
                return SafeToDecimal((double)Binomial(new System.Numerics.BigInteger(arg(0)), new System.Numerics.BigInteger(arg(1))));
            }
            case "permutation":
            case "permutations":
            {
                if (fn.Arguments.Count != 2) throw new InvalidOperationException("Permutations requires two arguments.");
                return SafeToDecimal((double)Permutations(new System.Numerics.BigInteger(arg(0)), new System.Numerics.BigInteger(arg(1))));
            }
            case "factorial":
            {
                if (fn.Arguments.Count != 1) throw new InvalidOperationException("Factorial requires one argument.");
                return SafeToDecimal((double)Factorial(new System.Numerics.BigInteger(arg(0))));
            }
            case "modinverse":
            {
                if (fn.Arguments.Count != 2) throw new InvalidOperationException("ModInverse requires two arguments.");
                return (decimal)ModInverse(new System.Numerics.BigInteger(arg(0)), new System.Numerics.BigInteger(arg(1)));
            }
            case "primesbetween":
            {
                if (fn.Arguments.Count != 2) throw new InvalidOperationException("PrimesBetween requires 2 arguments: start, end.");
                var startVal = (long)arg(0);
                var endVal = (long)arg(1);
                var primes = GetPrimesBetween(startVal, endVal).Select(p => (IExpression)new Number(p)).ToImmutableList();
                throw new UnsupportedFunctionVectorResultException(new Vector(primes));
            }
            case "valuation":
            {
                if (fn.Arguments.Count != 2) throw new InvalidOperationException("Valuation requires 2 arguments: prime, number.");
                
                decimal pDec;
                if (!NumericEvaluator.TryEvaluate(fn.Arguments[0], assignments, out pDec, out _)) return 0m;
                System.Numerics.BigInteger p = new System.Numerics.BigInteger(pDec);
                if (p <= 1) return 0m;

                // Attempt to evaluate the second argument, but catch overflow
                try
                {
                    decimal nDec;
                    if (!NumericEvaluator.TryEvaluate(fn.Arguments[1], assignments, out nDec, out _)) return 0m;
                    if (nDec == 0m) return 0m;
                    
                    System.Numerics.BigInteger n = new System.Numerics.BigInteger(Math.Abs(nDec));
                    
                    long count = 0;
                    while (n > 0 && n % p == 0)
                    {
                        count++;
                        n /= p;
                    }
                    return (decimal)count;
                }
                catch (Exception)
                {
                    return 0m;
                }
            }
            case "isprime":
                return IsPrime(new System.Numerics.BigInteger(arg(0))) ? 1m : 0m;
            case "anglebetweenvectors":
            {
                if (fn.Arguments.Count == 2)
                {
                    if (TryEvaluateVector(fn.Arguments[0], assignments, out var v1Value) && 
                        TryEvaluateVector(fn.Arguments[1], assignments, out var v2Value))
                    {
                        double dotValue = 0;
                        double n1Value = 0;
                        double n2Value = 0;
                        int countValue = Math.Min(v1Value.Arguments.Count, v2Value.Arguments.Count);
                        for (int iValue = 0; iValue < countValue; iValue++)
                        {
                            double x1Value = (double)Evaluate(v1Value.Arguments[iValue], assignments);
                            double x2Value = (double)Evaluate(v2Value.Arguments[iValue], assignments);
                            dotValue += x1Value * x2Value;
                            n1Value += x1Value * x1Value;
                            n2Value += x2Value * x2Value;
                        }
                        if (n1Value == 0 || n2Value == 0) return 0m;
                        double cosValueVal = dotValue / (Math.Sqrt(n1Value) * Math.Sqrt(n2Value));
                        if (cosValueVal > 1.0) cosValueVal = 1.0;
                        if (cosValueVal < -1.0) cosValueVal = -1.0;
                        return SafeToDecimal(Math.Acos(cosValueVal) * 180.0 / Math.PI);
                    }
                }
                throw new InvalidOperationException("AngleBetweenVectors requires two vector-valued arguments.");
            }
            case "countrealsolutions":
            {
                if (fn.Arguments.Count != 2) throw new InvalidOperationException("CountRealSolutions requires 2 arguments: variable, equation.");
                if (fn.Arguments[0] is not Symbol varSymRootsValue) throw new InvalidOperationException("CountRealSolutions first argument must be a symbol.");
                if (fn.Arguments[1] is not Equality eqRootsValue) throw new InvalidOperationException("CountRealSolutions second argument must be an equality.");

                var residualValue = new Subtract(eqRootsValue.LeftOperand, eqRootsValue.RightOperand).Canonicalize();
                var substitutedValue = SubstitutionStrategy.SubstituteInternal(residualValue, assignments.ToDictionary(kvValue => kvValue.Key, kvValue => (IExpression)new Number(kvValue.Value))).Canonicalize();
                
                if (Polynomial.TryCreate(substitutedValue, varSymRootsValue, out var polyRootsValue))
                {
                    if (polyRootsValue.Degree == 1) return 1m;
                    if (polyRootsValue.Degree == 2)
                    {
                        if (CoefficientUtility.TryGetCoefficient(substitutedValue, varSymRootsValue, 2, out var aExpValue) &&
                            CoefficientUtility.TryGetCoefficient(substitutedValue, varSymRootsValue, 1, out var bExpValue) &&
                            CoefficientUtility.TryGetCoefficient(substitutedValue, varSymRootsValue, 0, out var cExpValue))
                        {
                            if (NumericEvaluator.TryEvaluate(aExpValue, ImmutableDictionary<string, decimal>.Empty, out var aValVal, out _) &&
                                NumericEvaluator.TryEvaluate(bExpValue, ImmutableDictionary<string, decimal>.Empty, out var bValVal, out _) &&
                                NumericEvaluator.TryEvaluate(cExpValue, ImmutableDictionary<string, decimal>.Empty, out var cValVal, out _))
                            {
                                var discValue = bValVal * bValVal - 4 * aValVal * cValVal;
                                if (discValue > 0) return 2m;
                                if (discValue == 0) return 1m;
                                return 0m;
                            }
                        }
                    }
                    if (polyRootsValue.Degree == 4)
                    {
                        if (CoefficientUtility.TryGetCoefficient(substitutedValue, varSymRootsValue, 4, out var a4ExpValue) &&
                            CoefficientUtility.TryGetCoefficient(substitutedValue, varSymRootsValue, 3, out var a3ExpValue) &&
                            CoefficientUtility.TryGetCoefficient(substitutedValue, varSymRootsValue, 2, out var a2ExpValue) &&
                            CoefficientUtility.TryGetCoefficient(substitutedValue, varSymRootsValue, 1, out var a1ExpValue) &&
                            CoefficientUtility.TryGetCoefficient(substitutedValue, varSymRootsValue, 0, out var a0ExpValue))
                        {
                            if (NumericEvaluator.TryEvaluate(a4ExpValue, ImmutableDictionary<string, decimal>.Empty, out var a4ValVal, out _) &&
                                NumericEvaluator.TryEvaluate(a3ExpValue, ImmutableDictionary<string, decimal>.Empty, out var a3ValVal, out _) &&
                                NumericEvaluator.TryEvaluate(a2ExpValue, ImmutableDictionary<string, decimal>.Empty, out var a2ValVal, out _) &&
                                NumericEvaluator.TryEvaluate(a1ExpValue, ImmutableDictionary<string, decimal>.Empty, out var a1ValVal, out _) &&
                                NumericEvaluator.TryEvaluate(a0ExpValue, ImmutableDictionary<string, decimal>.Empty, out var a0ValVal, out _))
                            {
                                if (a3ValVal == 0 && a1ValVal == 0)
                                {
                                    var discValue = a2ValVal * a2ValVal - 4 * a4ValVal * a0ValVal;
                                    if (discValue < 0) return 0m;
                                    
                                    var u1Value = (-a2ValVal + SafeToDecimal(Math.Sqrt((double)discValue))) / (2 * a4ValVal);
                                    var u2Value = (-a2ValVal - SafeToDecimal(Math.Sqrt((double)discValue))) / (2 * a4ValVal);
                                    
                                    int countRootsValue = 0;
                                    void CheckUVal(decimal uValueVal)
                                    {
                                        if (uValueVal > 0) countRootsValue += 2;
                                        else if (uValueVal == 0) countRootsValue += 1;
                                    }
                                    
                                    if (discValue == 0) CheckUVal(u1Value);
                                    else { CheckUVal(u1Value); CheckUVal(u2Value); }
                                    return (decimal)countRootsValue;
                                }
                                else
                                {
                                    // General quartic root counting using Sturm sequences or simple numeric sampling of extrema
                                    // For a quick heuristic, let's use the derivative's roots
                                    // f'(x) = 4a4 x^3 + 3a3 x^2 + 2a2 x + a1
                                    // This is a cubic, harder to solve exactly here.
                                    // Fallback: use NumericApproximation if possible or just return 0 for now.
                                    // TODO: Implement Sturm chain for reliable counting.
                                }
                            }
                        }
                    }
                }
                return 0m; 
            }
            case "sum":
            {
                if (fn.Arguments.Count == 2)
                {
                    // Case: Sum(variable, condition) or Sum(condition, variable)
                    Symbol? varSym2 = fn.Arguments[0] as Symbol;
                    IExpression condition = fn.Arguments[1];
                    if (varSym2 == null && fn.Arguments[1] is Symbol vs)
                    {
                        varSym2 = vs;
                        condition = fn.Arguments[0];
                    }

                    if (varSym2 != null)
                    {
                        decimal sum = 0m;
                        // Try to infer domain from problem context or use a default range for small integer problems
                        for (long i = -100; i <= 100; i++)
                        {
                            var loopAssignments = new Dictionary<string, decimal>(assignments, StringComparer.Ordinal);
                            loopAssignments[varSym2.Name] = (decimal)i;
                            if (EvaluateCondition(condition, loopAssignments, allowSymbolAssignments: false))
                            {
                                sum += (decimal)i;
                            }
                        }
                        return sum;
                    }
                }
                if (fn.Arguments.Count == 3)
                {
                    // Standard: Sum(var, domain, body) OR body could be first: Sum(body, var, domain)
                    Symbol? varSym3 = fn.Arguments[0] as Symbol;
                    IExpression domain = fn.Arguments[1];
                    IExpression body = fn.Arguments[2];

                    if (varSym3 == null && fn.Arguments[1] is Symbol vs)
                    {
                        varSym3 = vs;
                        domain = fn.Arguments[2];
                        body = fn.Arguments[0];
                    }

                    if (varSym3 != null)
                    {
                        if (domain is Function intFn && intFn.Name.Equals("interval", StringComparison.OrdinalIgnoreCase))
                        {
                            return EvaluateFunction(new Function("Sum", body, varSym3, intFn.Arguments[0], intFn.Arguments[1]), assignments);
                        }
                        if (domain is Vector vec)
                        {
                            decimal sum = 0m;
                            var loopAssignments = new Dictionary<string, decimal>(assignments, StringComparer.Ordinal);
                            foreach (var item in vec.Arguments)
                            {
                                loopAssignments[varSym3.Name] = Evaluate(item, assignments);
                                sum += Evaluate(body, loopAssignments);
                            }
                            return sum;
                        }
                    }
                }
                if (fn.Arguments.Count != 4) throw new InvalidOperationException("Sum requires 2, 3 or 4 arguments.");
                
                // Robust argument identification for Sum(body, var, start, end) or Sum(var, body, start, end) etc.
                Symbol? varSymSumValue = fn.Arguments[1] as Symbol;
                IExpression bodyValue = fn.Arguments[0];
                if (varSymSumValue == null && fn.Arguments[0] is Symbol vs0)
                {
                    varSymSumValue = vs0;
                    bodyValue = fn.Arguments[1];
                }

                if (varSymSumValue == null) throw new InvalidOperationException("Sum variable must be a symbol.");
                
                var startValValValue = arg(2);
                
                if (fn.Arguments[3] is Symbol infValue && infValue.Name.Equals("infinity", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryEvaluateGeometricSeries(bodyValue, varSymSumValue, out var sumInfValue))
                    {
                        return sumInfValue;
                    }
                    throw new InvalidOperationException("Infinite sum only supported for recognized series (e.g. geometric).");
                }

                var endValValValue = arg(3);
                var startIntValue = (long)Math.Ceiling(startValValValue);
                var endIntValue = (long)Math.Floor(endValValValue);

                if (endIntValue - startIntValue > 10000) throw new InvalidOperationException("Sum interval too large for numeric evaluation.");

                decimal sumResultValue = 0m;
                var loopAssignmentsValue = new Dictionary<string, decimal>(assignments, StringComparer.Ordinal);
                for (long iValue = startIntValue; iValue <= endIntValue; iValue++)
                {
                    loopAssignmentsValue[varSymSumValue.Name] = (decimal)iValue;
                    sumResultValue += Evaluate(bodyValue, loopAssignmentsValue);
                }
                return sumResultValue;
            }
            case "product":
            {
                if (fn.Arguments.Count == 2)
                {
                    // Case: Product(variable, condition) or Product(condition, variable)
                    Symbol? varSym2 = fn.Arguments[0] as Symbol;
                    IExpression condition = fn.Arguments[1];
                    if (varSym2 == null && fn.Arguments[1] is Symbol vs)
                    {
                        varSym2 = vs;
                        condition = fn.Arguments[0];
                    }

                    if (varSym2 != null)
                    {
                        decimal prod = 1m;
                        for (long i = -100; i <= 100; i++)
                        {
                            var loopAssignments = new Dictionary<string, decimal>(assignments, StringComparer.Ordinal);
                            loopAssignments[varSym2.Name] = (decimal)i;
                            if (EvaluateCondition(condition, loopAssignments, allowSymbolAssignments: false))
                            {
                                prod *= (decimal)i;
                            }
                        }
                        return prod;
                    }
                }
                if (fn.Arguments.Count == 3)
                {
                    Symbol? varSym3 = fn.Arguments[0] as Symbol;
                    IExpression domain = fn.Arguments[1];
                    IExpression body = fn.Arguments[2];

                    if (varSym3 == null && fn.Arguments[1] is Symbol vs)
                    {
                        varSym3 = vs;
                        domain = fn.Arguments[2];
                        body = fn.Arguments[0];
                    }

                    if (varSym3 != null)
                    {
                        if (domain is Function intFn && intFn.Name.Equals("interval", StringComparison.OrdinalIgnoreCase))
                        {
                            return EvaluateFunction(new Function("Product", body, varSym3, intFn.Arguments[0], intFn.Arguments[1]), assignments);
                        }
                        if (domain is Vector vec)
                        {
                            decimal prod = 1m;
                            var loopAssignments = new Dictionary<string, decimal>(assignments, StringComparer.Ordinal);
                            foreach (var item in vec.Arguments)
                            {
                                loopAssignments[varSym3.Name] = Evaluate(item, assignments);
                                prod *= Evaluate(body, loopAssignments);
                            }
                            return prod;
                        }
                    }
                }
                if (fn.Arguments.Count != 4) throw new InvalidOperationException("Product requires 2, 3 or 4 arguments.");
                
                Symbol? varSymProdValue = fn.Arguments[1] as Symbol;
                IExpression bodyProdValue = fn.Arguments[0];
                if (varSymProdValue == null && fn.Arguments[0] is Symbol vs0)
                {
                    varSymProdValue = vs0;
                    bodyProdValue = fn.Arguments[1];
                }

                if (varSymProdValue == null) throw new InvalidOperationException("Product variable must be a symbol.");

                var startValValValue = arg(2);
                var endValValValue = arg(3);
                var startIntValue = (long)Math.Ceiling(startValValValue);
                var endIntValue = (long)Math.Floor(endValValValue);

                if (endIntValue - startIntValue > 500) throw new InvalidOperationException("Product interval too large for numeric evaluation.");

                decimal prodResultValue = 1m;
                var loopAssignmentsValue = new Dictionary<string, decimal>(assignments, StringComparer.Ordinal);
                for (long iValue = startIntValue; iValue <= endIntValue; iValue++)
                {
                    loopAssignmentsValue[varSymProdValue.Name] = (decimal)iValue;
                    prodResultValue *= Evaluate(bodyProdValue, loopAssignmentsValue);
                }
                return prodResultValue;
            }
            case "and":
            case "or":
            case "not":
            case "forall":
            case "exists":
                return EvaluateCondition(fn, assignments, allowSymbolAssignments: false) ? 1m : 0m;
            default:
                throw new InvalidOperationException($"Unsupported function '{fn.Name}' for numeric evaluation.");
        }
    }

    private static bool TryEvaluateGeometricSeries(IExpression body, Symbol varSym, out decimal result)
    {
        result = 0;
        IExpression? nTermValue = null;
        IExpression? xPowNValue = null;

        if (body is Multiply mulValue && mulValue.Arguments.Count == 2)
        {
            if (mulValue.Arguments[0].InternalEquals(varSym)) { nTermValue = mulValue.Arguments[0]; xPowNValue = mulValue.Arguments[1]; }
            else if (mulValue.Arguments[1].InternalEquals(varSym)) { nTermValue = mulValue.Arguments[1]; xPowNValue = mulValue.Arguments[0]; }
        }
        else if (body is Divide divValue && divValue.Numerator.InternalEquals(varSym))
        {
            nTermValue = divValue.Numerator;
            xPowNValue = new Power(divValue.Denominator, new Number(-1)).Canonicalize();
        }

        if (nTermValue != null && xPowNValue is Power pValue && pValue.Exponent.InternalEquals(varSym))
        {
            if (NumericEvaluator.TryEvaluate(pValue.Base, ImmutableDictionary<string, decimal>.Empty, out var xValueVal, out _))
            {
                if (Math.Abs(xValueVal) < 1)
                {
                    result = xValueVal / ((1 - xValueVal) * (1 - xValueVal));
                    return true;
                }
            }
        }
        
        if (body is Power p2Value && p2Value.Exponent.InternalEquals(varSym))
        {
            if (NumericEvaluator.TryEvaluate(p2Value.Base, ImmutableDictionary<string, decimal>.Empty, out var xValueVal, out _))
            {
                if (Math.Abs(xValueVal) < 1)
                {
                    result = 1m / (1 - xValueVal);
                    return true;
                }
            }
        }

        return false;
    }

    private static List<decimal> GetValues(Function fn, IReadOnlyDictionary<string, decimal> assignments)
    {
        var listValue = new List<decimal>();
        foreach (var aValue in fn.Arguments)
        {
            if (aValue is Vector vValue)
            {
                foreach (var vaValue in vValue.Arguments) listValue.Add(Evaluate(vaValue, assignments));
            }
            else
            {
                listValue.Add(Evaluate(aValue, assignments));
            }
        }
        return listValue;
    }

    public static bool TryEvaluateVector(IExpression expr, IReadOnlyDictionary<string, decimal> assignments, out Vector result)
    {
        result = null!;
        try
        {
            if (expr is Vector vectorExprValue) { result = vectorExprValue; return true; }
            if (expr is Function fvValue && fvValue.Name.Equals("vector", StringComparison.OrdinalIgnoreCase))
            {
                var vArgsValue = fvValue.Arguments.Select(aValue => (IExpression)new Number(Evaluate(aValue, assignments))).ToImmutableList();
                result = new Vector(vArgsValue);
                return true;
            }
            if (expr is Add addValue)
            {
                var vecsValue = new List<Vector>();
                foreach (var argValue in addValue.Arguments)
                {
                    if (TryEvaluateVector(argValue, assignments, out var vecValue)) vecsValue.Add(vecValue);
                    else return false;
                }
                if (vecsValue.Count == 0) return false;
                int dimValue = vecsValue.Max(vElemValue => vElemValue.Arguments.Count);
                var componentsValue = new IExpression[dimValue];
                for (int iValue = 0; iValue < dimValue; iValue++)
                {
                    decimal sumValueVal = 0;
                    foreach (var vItemValue in vecsValue) if (iValue < vItemValue.Arguments.Count) sumValueVal += Evaluate(vItemValue.Arguments[iValue], assignments);
                    componentsValue[iValue] = new Number(sumValueVal);
                }
                result = new Vector(componentsValue.ToImmutableList());
                return true;
            }
            if (expr is Subtract subValue)
            {
                if (TryEvaluateVector(subValue.LeftOperand, assignments, out var v1ValueVal) && 
                    TryEvaluateVector(subValue.RightOperand, assignments, out var v2ValueVal))
                {
                    int dimValueVal = Math.Max(v1ValueVal.Arguments.Count, v2ValueVal.Arguments.Count);
                    var componentsValueVal = new IExpression[dimValueVal];
                    for (int iValueVal = 0; iValueVal < dimValueVal; iValueVal++)
                    {
                        decimal val1ValueVal = iValueVal < v1ValueVal.Arguments.Count ? Evaluate(v1ValueVal.Arguments[iValueVal], assignments) : 0;
                        decimal val2ValueVal = iValueVal < v2ValueVal.Arguments.Count ? Evaluate(v2ValueVal.Arguments[iValueVal], assignments) : 0;
                        componentsValueVal[iValueVal] = new Number(val1ValueVal - val2ValueVal);
                    }
                    result = new Vector(componentsValueVal.ToImmutableList());
                    return true;
                }
            }
            if (expr is Multiply mulValueVal)
            {
                Vector? vecValueVal = null;
                decimal scalarValueVal = 1;
                foreach (var argValueVal in mulValueVal.Arguments)
                {
                    if (TryEvaluateVector(argValueVal, assignments, out var vArgValueVal))
                    {
                        if (vecValueVal != null) return false; 
                        vecValueVal = vArgValueVal;
                    }
                    else scalarValueVal *= Evaluate(argValueVal, assignments);
                }
                if (vecValueVal != null)
                {
                    var componentsValueValVal = vecValueVal.Arguments.Select(aValueValVal => (IExpression)new Number(Evaluate(aValueValVal, assignments) * scalarValueVal)).ToImmutableList();
                    result = new Vector(componentsValueValVal);
                    return true;
                }
            }
            return false;
        }
        catch { return false; }
    }

    private static IEnumerable<long> GetPrimeFactors(long n)
    {
        n = Math.Abs(n);
        if (n < 2) yield break;
        while (n % 2 == 0)
        {
            yield return 2;
            n /= 2;
        }
        for (long i = 3; i * i <= n; i += 2)
        {
            while (n % i == 0)
            {
                yield return i;
                n /= i;
            }
        }
        if (n > 2) yield return n;
    }

    private static IEnumerable<System.Numerics.BigInteger> GetPrimeFactors(System.Numerics.BigInteger n)
    {
        n = System.Numerics.BigInteger.Abs(n);
        if (n < 2) yield break;
        while (n % 2 == 0)
        {
            yield return 2;
            n /= 2;
        }
        for (System.Numerics.BigInteger i = 3; i * i <= n; i += 2)
        {
            while (n % i == 0)
            {
                yield return i;
                n /= i;
            }
        }
        if (n > 2) yield return n;
    }

    private static bool IsRational(IExpression expr, IReadOnlyDictionary<string, decimal> assignments)
    {
        if (expr is Number numValValue) 
        {
            double dValueVal = (double)numValValue.Value;
            if (Math.Abs(dValueVal - Math.PI) < 1e-12 || Math.Abs(dValueVal - Math.E) < 1e-12) return false;
            return true;
        }
        if (expr is Symbol sValueVal)
        {
            if (sValueVal.Name.Equals("pi", StringComparison.OrdinalIgnoreCase) || sValueVal.Name.Equals("e", StringComparison.OrdinalIgnoreCase)) return false;
            if (assignments.TryGetValue(sValueVal.Name, out var valValueVal))
            {
                double dValueValVal = (double)valValueVal;
                if (Math.Abs(dValueValVal - Math.PI) < 1e-12 || Math.Abs(dValueValVal - Math.E) < 1e-12) return false;
                return true;
            }
            return true; 
        }
        if (expr is Add addValueVal) return addValueVal.Arguments.All(aValueVal => IsRational(aValueVal, assignments));
        if (expr is Subtract subValueVal) return IsRational(subValueVal.LeftOperand, assignments) && IsRational(subValueVal.RightOperand, assignments);
        if (expr is Multiply mulValueValVal) return mulValueValVal.Arguments.All(aValueValVal => IsRational(aValueValVal, assignments));
        if (expr is Divide divValueVal) return IsRational(divValueVal.Numerator, assignments) && IsRational(divValueVal.Denominator, assignments);
        if (expr is Power pValueVal)
        {
            if (pValueVal.Exponent is Number nValueValVal)
            {
                if (nValueValVal.Value % 1 == 0) return IsRational(pValueVal.Base, assignments);
                try
                {
                    if (!NumericEvaluator.TryEvaluate(pValueVal.Base, assignments, out var baseValValueVal, out _)) return false;
                    if (nValueValVal.Value == 0.5m)
                    {
                        var sqrtValValueVal = Math.Sqrt((double)baseValValueVal);
                        return Math.Abs(sqrtValValueVal - Math.Round(sqrtValValueVal)) < 1e-10;
                    }
                    if (Math.Abs(nValueValVal.Value - 1m/3m) < 1e-10m)
                    {
                        var cbrtValValueVal = Math.Cbrt((double)baseValValueVal);
                        return Math.Abs(cbrtValValueVal - Math.Round(cbrtValValueVal)) < 1e-10;
                    }
                }
                catch { return false; }
            }
            return false; 
        }
        if (expr is Function fnValueVal)
        {
            var nameValueVal = fnValueVal.Name.ToLowerInvariant();
            if (nameValueVal == "sqrt") return IsRational(new Power(fnValueVal.Arguments[0], new Number(0.5m)), assignments);
            if (nameValueVal is "floor" or "ceil" or "ceiling" or "round" or "abs" or "sgn" or "sign" or "mod" or "gcd" or "lcm") 
                return fnValueVal.Arguments.All(aValueValValVal => IsRational(aValueValValVal, assignments));
        }

        return true; 
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            a %= b;
            (a, b) = (b, a);
        }
        return Math.Abs(a);
    }

    private static System.Numerics.BigInteger Gcd(System.Numerics.BigInteger a, System.Numerics.BigInteger b)
    {
        return System.Numerics.BigInteger.GreatestCommonDivisor(a, b);
    }

    private static long Permutations(long n, long k)
    {
        if (k < 0 || k > n) return 0;
        if (k == 0) return 1;
        long resValueVal = 1;
        for (long iValueVal = 0; iValueVal < k; iValueVal++)
        {
             try 
             {
                 checked { resValueVal *= (n - iValueVal); }
             }
             catch (OverflowException)
             {
                 throw new OverflowException("Permutations result too large.");
             }
        }
        return resValueVal;
    }

    private static System.Numerics.BigInteger Permutations(System.Numerics.BigInteger n, System.Numerics.BigInteger k)
    {
        if (k < 0 || k > n) return 0;
        if (k == 0) return 1;
        System.Numerics.BigInteger res = 1;
        for (System.Numerics.BigInteger i = 0; i < k; i++)
        {
            res *= (n - i);
        }
        return res;
    }

    private static long Binomial(long n, long k)
    {
        if (k < 0 || k > n) return 0;
        if (k == 0 || k == n) return 1;
        if (k > n / 2) k = n - k;
        long resValueVal = 1;
        for (long iValueVal = 1; iValueVal <= k; iValueVal++)
        {
            try
            {
                checked
                {
                    resValueVal = resValueVal * (n - iValueVal + 1) / iValueVal;
                }
            }
            catch (OverflowException)
            {
                 throw new OverflowException("Binomial result too large.");
            }
        }
        return resValueVal;
    }

    private static System.Numerics.BigInteger Binomial(System.Numerics.BigInteger n, System.Numerics.BigInteger k)
    {
        if (k < 0 || k > n) return 0;
        if (k == 0 || k == n) return 1;
        if (k > n / 2) k = n - k;
        System.Numerics.BigInteger res = 1;
        for (System.Numerics.BigInteger i = 1; i <= k; i++)
        {
            res = res * (n - i + 1) / i;
        }
        return res;
    }

    private static long Factorial(long n)
    {
        if (n < 0) return 0;
        if (n == 0) return 1;
        if (n > 20) throw new OverflowException("Factorial result too large for decimal (n > 20).");
        long resValueVal = 1;
        for (long iValueVal = 1; iValueVal <= n; iValueVal++) resValueVal *= iValueVal;
        return resValueVal;
    }

    private static System.Numerics.BigInteger Factorial(System.Numerics.BigInteger n)
    {
        if (n < 0) return 0;
        if (n == 0) return 1;
        System.Numerics.BigInteger res = 1;
        for (System.Numerics.BigInteger i = 1; i <= n; i++) res *= i;
        return res;
    }

    private static long ModInverse(long a, long m)
    {
        long m0ValueVal = m;
        long yValueVal = 0, xValueVal = 1;
        if (m == 1) return 0;
        while (a > 1)
        {
            if (m == 0) throw new DivideByZeroException("Modulo by zero in ModInverse.");
            long qValueVal = a / m;
            long tValueVal = m;
            m = a % m;
            a = tValueVal;
            tValueVal = yValueVal;
            yValueVal = xValueVal - qValueVal * yValueVal;
            xValueVal = tValueVal;
        }
        if (xValueVal < 0) xValueVal += m0ValueVal;
        return xValueVal;
    }

    private static long ModInverse(System.Numerics.BigInteger a, System.Numerics.BigInteger m)
    {
        System.Numerics.BigInteger m0 = m;
        System.Numerics.BigInteger y = 0, x = 1;
        if (m == 1) return 0;
        while (a > 1)
        {
            if (m == 0) throw new DivideByZeroException("Modulo by zero in ModInverse.");
            System.Numerics.BigInteger q = a / m;
            System.Numerics.BigInteger t = m;
            m = a % m;
            a = t;
            t = y;
            y = x - q * y;
            x = t;
        }
        if (x < 0) x += m0;
        return (long)x;
    }

    private static long GetValuation(long p, long n)
    {
        if (p <= 1 || n == 0) return 0;
        long count = 0;
        n = Math.Abs(n);
        while (n > 0 && n % p == 0)
        {
            count++;
            n /= p;
        }
        return count;
    }

    private static IEnumerable<long> GetPrimesBetween(long start, long end)
    {
        for (long i = start; i <= end; i++)
        {
            if (IsPrime(i)) yield return i;
        }
    }

    private static bool IsPrime(long n)
    {
        if (n <= 1) return false;
        if (n <= 3) return true;
        if (n % 2 == 0 || n % 3 == 0) return false;
        for (long iValueVal = 5; iValueVal * iValueVal <= n; iValueVal += 6)
        {
            if (n % iValueVal == 0 || n % (iValueVal + 2) == 0) return false;
        }
        return true;
    }

    private static bool IsPrime(System.Numerics.BigInteger n)
    {
        if (n <= 1) return false;
        if (n <= 3) return true;
        if (n % 2 == 0 || n % 3 == 0) return false;
        for (System.Numerics.BigInteger i = 5; i * i <= n; i += 6)
        {
            if (n % i == 0 || n % (i + 2) == 0) return false;
        }
        return true;
    }

    private static long SumDivisors(long n)
    {
        long sumValueVal = 0;
        foreach (var dValueVal in GetDivisors(n))
        {
            sumValueVal += dValueVal;
        }
        return sumValueVal;
    }

    private static IEnumerable<long> GetDivisors(long n)
    {
        if (n <= 0) yield break;
        for (long iValueVal = 1; iValueVal * iValueVal <= n; iValueVal++)
        {
            if (n % iValueVal == 0)
            {
                yield return iValueVal;
                if (iValueVal * iValueVal != n) yield return n / iValueVal;
            }
        }
    }

    private static IEnumerable<System.Numerics.BigInteger> GetDivisors(System.Numerics.BigInteger n)
    {
        if (n <= 0) yield break;
        for (System.Numerics.BigInteger i = 1; i * i <= n; i++)
        {
            if (n % i == 0)
            {
                yield return i;
                if (i * i != n) yield return n / i;
            }
        }
    }
}

public sealed class UnsupportedFunctionVectorResultException : Exception
{
    public IExpression Result { get; }
    public UnsupportedFunctionVectorResultException(IExpression result) : base("Function returned a vector result which is not supported by decimal evaluation.")
    {
        Result = result;
    }
}
