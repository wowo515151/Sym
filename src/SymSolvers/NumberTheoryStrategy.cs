// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers.Numerics;
using SymCore;

namespace SymSolvers;

public sealed class NumberTheoryStrategy : ISolverStrategy, INamedSolverStrategy
{
    public string Name => "NumberTheoryStrategy";
    private readonly RulePackStrategy _rulePackStrategy;

    public NumberTheoryStrategy()
    {
        var packs = SymRules.RulePackLibrary.GetRulePacks();
        var pack = packs.FirstOrDefault(p => p.Name == "NumberTheoryStrategy");
        if (pack != null) _rulePackStrategy = new RulePackStrategy(pack);
        else _rulePackStrategy = null!;
    }

    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (context is null) return SolveResult.Failure(problem, "SolveContext cannot be null.");
        if (problem is null) return SolveResult.Failure(null, "Problem expression cannot be null.");

        IExpression current = problem;
        bool changed = false;

        // 1. Try to evaluate modular inverses and powers
        var simplified = SimplifyModular(current);
        if (!simplified.InternalEquals(current))
        {
            current = simplified;
            changed = true;
        }

        // 2. Try to solve mod(x, M) == R with x < B or x > B
        if (TrySolveModWithBound(current, out var boundResult))
        {
            current = boundResult;
            changed = true;
        }

        // Try solving as a system of constraints using PrimeValuationSystem
        if (current is Sym.Operations.Vector sysVec)
        {
            var pvsResult = PrimeValuationSystem.Solve(sysVec.Arguments, context);
            if (pvsResult != null) return SolveResult.Success(pvsResult, "Solved via PrimeValuationSystem");
        }
        else if (current is Function fAnd && fAnd.Name.Equals("And", StringComparison.OrdinalIgnoreCase))
        {
             var pvsResult = PrimeValuationSystem.Solve(fAnd.Arguments, context);
             if (pvsResult != null) return SolveResult.Success(pvsResult, "Solved via PrimeValuationSystem");
        }

        // Handle specific functions
        if (current is Function fn)
        {
            // ... (rest of the handle specific functions)
            if (fn.Name.Equals("SmallestPrimeDivisor", StringComparison.OrdinalIgnoreCase) && fn.Arguments.Count == 1)
            {
                var arg = fn.Arguments[0];
                long spd = FindSmallestPrimeDivisor(arg, context.CancellationToken);
                if (spd > 0)
                {
                    return SolveResult.Success(new Number(spd), "Computed SmallestPrimeDivisor");
                }
            }
            else if (fn.Name.Equals("IsPrime", StringComparison.OrdinalIgnoreCase) && fn.Arguments.Count == 1)
            {
                if (TryEvaluateToLong(fn.Arguments[0], out var n))
                {
                    return SolveResult.Success(IsPrime(n) ? new Symbol("True") : new Symbol("False"), "Computed IsPrime");
                }
            }
            else if (fn.Name.Equals("IsPrimitiveRoot", StringComparison.OrdinalIgnoreCase) && fn.Arguments.Count == 2)
            {
                if (TryEvaluateToLong(fn.Arguments[0], out var a) && TryEvaluateToLong(fn.Arguments[1], out var p))
                {
                    return SolveResult.Success(IsPrimitiveRoot(a, p, context.CancellationToken) ? new Symbol("True") : new Symbol("False"), "Computed IsPrimitiveRoot");
                }
            }
            else if (fn.Name.Equals("PrimitiveRoots", StringComparison.OrdinalIgnoreCase) && fn.Arguments.Count == 1)
            {
                if (TryEvaluateToLong(fn.Arguments[0], out var p))
                {
                    var roots = GetPrimitiveRoots(p, context.CancellationToken);
                    return SolveResult.Success(new Sym.Operations.Vector(roots.Select(r => new Number(r)).Cast<IExpression>().ToImmutableList()), "Computed PrimitiveRoots");
                }
            }
            else if (fn.Name.Equals("EulerTotient", StringComparison.OrdinalIgnoreCase) || fn.Name.Equals("phi", StringComparison.OrdinalIgnoreCase) || fn.Name.Equals("φ", StringComparison.OrdinalIgnoreCase))
            {
                if (TryEvaluateToLong(fn.Arguments[0], out var n))
                {
                    if (n == 1) return SolveResult.Success(new Number(1), "Computed EulerTotient(1)");
                    var factors = PrimeFactors(n, context.CancellationToken);
                    var distinctFactors = factors.Distinct();
                    long result = n;
                    foreach (var p in distinctFactors)
                    {
                        result = result / p * (p - 1);
                    }
                    return SolveResult.Success(new Number(result), "Computed EulerTotient");
                }
            }
            else if (fn.Name.Equals("PrimeFactors", StringComparison.OrdinalIgnoreCase) && fn.Arguments.Count == 1)
            {
                if (TryEvaluateToLong(fn.Arguments[0], out var n))
                {
                    var factors = PrimeFactors(n, context.CancellationToken);
                    var vec = new Sym.Operations.Vector(factors.Select(f => new Number(f)).Cast<IExpression>().ToImmutableList());
                    return SolveResult.Success(vec, "Computed PrimeFactors");
                }
            }
            else if (fn.Name.Equals("IntegerDivisors", StringComparison.OrdinalIgnoreCase) && fn.Arguments.Count == 1)
            {
                if (TryEvaluateToLong(fn.Arguments[0], out var n))
                {
                    var posDivs = GetPosDivisors(n);
                    var allDivs = posDivs.Concat(posDivs.Select(d => -d)).Distinct().OrderBy(d => d).ToList();
                    var vec = new Sym.Operations.Vector(allDivs.Select(d => new Number(d)).Cast<IExpression>().ToImmutableList());
                    return SolveResult.Success(vec, "Computed IntegerDivisors");
                }
            }
            else if (fn.Name.Equals("Divisors", StringComparison.OrdinalIgnoreCase) && fn.Arguments.Count == 1)
            {
                if (TryEvaluateToLong(fn.Arguments[0], out var n))
                {
                    var posDivs = GetPosDivisors(n).OrderBy(d => d).ToList();
                    var vec = new Sym.Operations.Vector(posDivs.Select(d => new Number(d)).Cast<IExpression>().ToImmutableList());
                    return SolveResult.Success(vec, "Computed Divisors");
                }
            }
            else if (fn.Name.Equals("SumDivisors", StringComparison.OrdinalIgnoreCase) && fn.Arguments.Count == 1)
            {
                if (TryEvaluateToLong(fn.Arguments[0], out var n))
                {
                    var factors = PrimeFactors(n, context.CancellationToken);
                    var groups = factors.GroupBy(x => x);
                    BigInteger sum = 1;
                    foreach (var g in groups)
                    {
                        long p = g.Key;
                        int count = g.Count();
                        // (p^(count+1) - 1) / (p - 1)
                        BigInteger num = BigInteger.Pow(p, count + 1) - 1;
                        BigInteger den = p - 1;
                        sum *= (num / den);
                    }
                    return SolveResult.Success(new Number((decimal)sum), "Computed SumDivisors");
                }
            }
            else if (fn.Name.Equals("ModularInverse", StringComparison.OrdinalIgnoreCase) && fn.Arguments.Count == 2)
            {
                if (TryEvaluateToLong(fn.Arguments[0], out var a) && TryEvaluateToLong(fn.Arguments[1], out var m))
                {
                    try 
                    {
                        long inv = ModularInverse(a, m);
                        return SolveResult.Success(new Number(inv), "Computed ModularInverse");
                    }
                    catch
                    {
                        // No inverse exists
                    }
                }
            }
            else if (fn.Name.Equals("LinearCongruence", StringComparison.OrdinalIgnoreCase) && fn.Arguments.Count == 3)
            {
                // ax = b (mod m)
                if (TryEvaluateToLong(fn.Arguments[0], out var a) && 
                    TryEvaluateToLong(fn.Arguments[1], out var b) && 
                    TryEvaluateToLong(fn.Arguments[2], out var m))
                {
                    // Solution: x = (b * a^-1) mod m, but needs gcd check
                    // a*x + m*y = b
                    long g = ExtendedGCD(a, m, out var x0, out var y0);
                    if (b % g == 0)
                    {
                        // x0 is solution for a*x0 + m*y0 = g
                        // We want a*x = b (mod m)
                        // Multiply x0 by (b/g)
                        // Particular solution: x = x0 * (b/g)
                        // Modulo is m/g
                        
                        long m_div_g = Math.Abs(m / g);
                        // Ensure x0 is positive
                        long sol = (x0 % m_div_g + m_div_g) % m_div_g;
                        sol = (sol * (b / g)) % m_div_g;
                        
                        // There are g solutions modulo m: sol, sol + m/g, sol + 2m/g ...
                        // Return the smallest positive one? Or a Vector?
                        // Let's return the smallest non-negative solution for now.
                        return SolveResult.Success(new Number(sol), "Computed LinearCongruence (smallest positive)");
                    }
                }
            }
        }

        if (changed) return SolveResult.Success(current, "Number theory simplifications applied.");
        return SolveResult.Failure(problem, "No number theory actions.");
    }

    private bool IsPrimitiveRoot(long a, long p, System.Threading.CancellationToken ct)
    {
        if (a <= 0 || a >= p) a = (a % p + p) % p;
        if (a == 0) return false;
        if (FindSmallestPrimeDivisor(new Number(p), ct) != p) return false; // Basic check, though usually p is prime.
        
        long phi = p - 1;
        var factors = PrimeFactors(phi, ct).Distinct();
        
        foreach (var fact in factors)
        {
            if (ModPow(a, phi / fact, p) == 1) return false;
        }
        return true;
    }

    private List<long> GetPrimitiveRoots(long p, System.Threading.CancellationToken ct)
    {
        var roots = new List<long>();
        if (!IsPrime(p)) return roots; // Or handle composite cases where roots exist? Primitive roots exist for 2, 4, p^k, 2p^k.
        // For now assume p is prime as per problem 3 context.
        
        long phi = p - 1;
        var factors = PrimeFactors(phi, ct).Distinct().ToList();

        for (long a = 1; a < p; a++)
        {
            ct.ThrowIfCancellationRequested();
            bool ok = true;
            foreach (var fact in factors)
            {
                if (ModPow(a, phi / fact, p) == 1) 
                {
                    ok = false;
                    break;
                }
            }
            if (ok) roots.Add(a);
        }
        return roots;
    }

    private static bool TryEvaluateToLong(IExpression expr, out long result)
    {
        result = 0;
        if (expr is Number num && num.Value % 1 == 0)
        {
            try
            {
                result = (long)num.Value;
                return true;
            }
            catch { return false; }
        }
        return false;
    }

    private long FindSmallestPrimeDivisor(IExpression expr, System.Threading.CancellationToken ct)
    {
        // Try small primes up to a limit
        long limit = 10000; // Check small primes first
        
        for (long p = 2; p < limit; p++)
        {
            if (p > 2 && p % 2 == 0) continue; // Optimization: only check odds after 2, ideally check primes only but checking odds is faster than generating primes
            if (!IsPrime(p)) continue;

            ct.ThrowIfCancellationRequested();
            
            try
            {
                long rem = EvalMod(expr, p);
                if (rem == 0) return p;
            }
            catch
            {
                // Evaluation failed (e.g. symbolic)
                return 0;
            }
        }
        
        // If expr evaluates to a number, we can check further
        if (TryEvaluateToLong(expr, out var n))
        {
            if (n < 0) n = -n;
            if (n <= 1) return n;
            return SmallestPrimeDivisor(n, ct);
        }

        return 0;
    }

    private long EvalMod(IExpression expr, long mod)
    {
        if (expr is Number n)
        {
            return (long)(n.Value % mod);
        }
        if (expr is Add add)
        {
            long sum = 0;
            foreach (var arg in add.Arguments)
            {
                sum = (sum + EvalMod(arg, mod)) % mod;
            }
            return sum;
        }
        if (expr is Multiply mul)
        {
            long prod = 1;
            foreach (var arg in mul.Arguments)
            {
                prod = (prod * EvalMod(arg, mod)) % mod;
            }
            return prod;
        }
        if (expr is Power pow)
        {
            long b = EvalMod(pow.Base, mod);
            if (pow.Exponent is Number expNum && expNum.Value >= 0 && expNum.Value % 1 == 0)
            {
                long e = (long)expNum.Value;
                return ModPow(b, e, mod);
            }
        }
        throw new InvalidOperationException("Cannot evaluate mod for expression");
    }

    private long SmallestPrimeDivisor(long n, System.Threading.CancellationToken ct)
    {
        if (n <= 1) return n;
        if (n % 2 == 0) return 2;
        if (n % 3 == 0) return 3;
        long limit = (long)Math.Sqrt(n);
        for (long i = 5; i <= limit; i += 6)
        {
            ct.ThrowIfCancellationRequested();
            if (n % i == 0) return i;
            if (n % (i + 2) == 0) return i + 2;
        }
        return n;
    }

    private List<long> PrimeFactors(long n, System.Threading.CancellationToken ct)
    {
        var factors = new List<long>();
        if (n <= 1) return factors;
        while (n % 2 == 0) { factors.Add(2); n /= 2; }
        while (n % 3 == 0) { factors.Add(3); n /= 3; }
        long i = 5;
        while (i * i <= n)
        {
            ct.ThrowIfCancellationRequested();
            while (n % i == 0) { factors.Add(i); n /= i; }
            while (n % (i + 2) == 0) { factors.Add(i + 2); n /= (i + 2); }
            i += 6;
        }
        if (n > 1) factors.Add(n);
        return factors;
    }

    private static IEnumerable<long> GetPosDivisors(long n)
    {
        if (n == 0) yield break;
        if (n < 0) n = -n;
        for (long i = 1; i * i <= n; i++)
        {
            if (n % i == 0)
            {
                yield return i;
                if (i * i != n) yield return n / i;
            }
        }
    }

    private bool IsPrime(long n)
    {
        if (n <= 1) return false;
        if (n <= 3) return true;
        if (n % 2 == 0 || n % 3 == 0) return false;
        long limit = (long)Math.Sqrt(n);
        for (long i = 5; i <= limit; i += 6)
        {
            if (n % i == 0 || n % (i + 2) == 0) return false;
        }
        return true;
    }

    private static long ModPow(long @base, long exp, long mod)
    {
        long res = 1;
        @base %= mod;
        while (exp > 0)
        {
            if (exp % 2 == 1) res = (res * @base) % mod;
            @base = (@base * @base) % mod;
            exp /= 2;
        }
        return res;
    }

    private static long ExtendedGCD(long a, long b, out long x, out long y)
    {
        if (a == 0)
        {
            x = 0;
            y = 1;
            return b;
        }
        long x1, y1;
        long gcd = ExtendedGCD(b % a, a, out x1, out y1);
        x = y1 - (b / a) * x1;
        y = x1;
        return gcd;
    }

    private static long ModularInverse(long a, long m)
    {
        long x, y;
        long g = ExtendedGCD(a, m, out x, out y);
        if (g != 1) throw new ArithmeticException("Modular inverse does not exist");
        return (x % m + m) % m;
    }

    private static bool TryApplyCRT(IExpression problem, out IExpression result)
    {
        result = null!;
        var congruences = new List<(long rem, long mod, string varName, IExpression original)>();
        var others = new List<IExpression>();

        void Collect(IExpression e)
        {
            if (e is Equality eq)
            {
                if (TryExtractCongruence(eq, out var rem, out var mod, out var varName))
                {
                    congruences.Add((rem, mod, varName, e));
                }
                else
                {
                    others.Add(e);
                }
            }
            else if (e is Function fn && (fn.Name.ToLowerInvariant() == "and" || fn.Name.ToLowerInvariant() == "or"))
            {
                if (fn.Name.ToLowerInvariant() == "and")
                {
                    foreach (var a in fn.Arguments) Collect(a);
                }
                else
                {
                    others.Add(e);
                }
            }
            else if (e is Sym.Operations.Vector v)
            {
                foreach (var arg in v.Arguments) Collect(arg);
            }
            else
            {
                others.Add(e);
            }
        }

        Collect(problem);

        var groups = congruences.GroupBy(c => c.varName).ToList();
        bool anyChanged = false;
        var finalConstraints = new List<IExpression>(others);

        foreach (var g in groups)
        {
            var varConqs = g.ToList();
            if (varConqs.Count < 2)
            {
                finalConstraints.AddRange(varConqs.Select(c => c.original));
                continue;
            }

            try
            {
                long resultRem = varConqs[0].rem;
                long resultMod = varConqs[0].mod;

                for (int i = 1; i < varConqs.Count; i++)
                {
                    (resultRem, resultMod) = SolveTwoCongruences(resultRem, resultMod, varConqs[i].rem, varConqs[i].mod);
                }

                finalConstraints.Add(new Equality(new Function("mod", new Symbol(g.Key), new Number(resultMod)), new Number(resultRem)).Canonicalize());
                anyChanged = true;
            }
            catch (Exception ex)
            {
                Logging.LogError("NumberTheoryStrategySolveCongruences", ex.Message, ex.StackTrace);
                return false;
            }
        }

        if (!anyChanged) return false;

        result = BuildResult(problem, finalConstraints);
        return true;
    }

    private static bool TryExtractCongruence(Equality eq, out long rem, out long mod, out string varName)
    {
        rem = 0; mod = 0; varName = null!;

        IExpression? leftMod = null;
        IExpression? rightMod = null;
        long mValue = 0;

        if (eq.LeftOperand is Function f1 && f1.Name.Equals("mod", StringComparison.OrdinalIgnoreCase) && f1.Arguments.Count == 2 && f1.Arguments[1] is Number m1)
        {
            leftMod = f1.Arguments[0];
            mValue = (long)m1.Value;

            if (eq.RightOperand is Function f2 && f2.Name.Equals("mod", StringComparison.OrdinalIgnoreCase) && f2.Arguments.Count == 2 && f2.Arguments[1] is Number m2 && (long)m2.Value == mValue)
            {
                rightMod = f2.Arguments[0];
            }
            else if (eq.RightOperand is Number rNum)
            {
                rightMod = rNum;
            }
        }

        if (leftMod != null && rightMod != null && mValue > 0)
        {
            var diff = new Subtract(leftMod, rightMod).Canonicalize();
            if (TryExtractLinearForMod(diff, out var a, out var b, out var name))
            {
                long aLong = (long)a;
                long bLong = (long)b;
                long targetR = (-bLong) % mValue;
                if (targetR < 0) targetR += mValue;

                long g = Gcd(Math.Abs(aLong), mValue);
                if (targetR % g != 0) return false;

                long aRed = aLong / g;
                long mRed = mValue / g;
                long rRed = targetR / g;

                try
                {
                    long inv = ModularInverse(aRed, mRed);
                    long x0 = (rRed * inv) % mRed;
                    if (x0 < 0) x0 += mRed;

                    rem = x0;
                    mod = mRed;
                    varName = name;
                    return true;
                }
                catch { return false; }
            }
        }

        return false;
    }

    private static bool TryExtractLinearForMod(IExpression expr, out decimal a, out decimal b, out string varName)
    {
        a = 0; b = 0; varName = null!;
        var symbols = ExpressionHelpers.CollectSymbols(expr);
        var variables = symbols.Where(s => s is Symbol sym && !ExpressionHelpers.IsMathConstant(sym.Name)).ToList();
        
        if (variables.Count != 1 || variables[0] is not Symbol target) return false;
        
        varName = target.Name;
        if (LinearExtraction.TryExtractLinear(expr, new[] { target }, out var coeffs, out var constant))
        {
            a = coeffs[0];
            b = constant;
            return true;
        }
        return false;
    }

    private static IExpression BuildResult(IExpression original, List<IExpression> constraints)
    {
        if (original is Sym.Operations.Vector) return new Sym.Operations.Vector(constraints.ToImmutableList()).Canonicalize();
        if (original is Function fn && fn.Name.ToLowerInvariant() == "and") return new Function("And", constraints.ToImmutableList()).Canonicalize();
        return constraints.Count == 1 ? constraints[0] : new Sym.Operations.Vector(constraints.ToImmutableList()).Canonicalize();
    }

    private static bool TrySolveModWithBound(IExpression current, out IExpression result)
    {
        result = null!;
        var varToCongruences = new Dictionary<string, List<(long mod, long rem)>>();
        var varToBounds = new Dictionary<string, (decimal? lower, decimal? upper)>();

        void Collect(IExpression e)
        {
            if (e is Equality eq)
            {
                if (TryExtractCongruence(eq, out var rem, out var mod, out var varName))
                {
                    if (!varToCongruences.TryGetValue(varName, out var list)) { list = new List<(long mod, long rem)>(); varToCongruences[varName] = list; }
                    list.Add((mod, rem));
                }
            }
            else if (e is Function fn)
            {
                var name = fn.Name.ToLowerInvariant();
                if (name == "lt" || name == "le")
                {
                    if (fn.Arguments[0] is Symbol s && fn.Arguments[1] is Number n)
                    {
                        var bounds = varToBounds.GetValueOrDefault(s.Name, (null, null));
                        varToBounds[s.Name] = (bounds.lower, n.Value);
                    }
                }
                else if (name == "gt" || name == "ge")
                {
                    if (fn.Arguments[0] is Symbol s && fn.Arguments[1] is Number n)
                    {
                        var bounds = varToBounds.GetValueOrDefault(s.Name, (null, null));
                        varToBounds[s.Name] = (n.Value, bounds.upper);
                    }
                }
                else if (name == "and" || name == "or")
                {
                    foreach (var a in fn.Arguments) Collect(a);
                }
            }
            else if (e is Sym.Operations.Vector v)
            {
                foreach (var arg in v.Arguments) Collect(arg);
            }
        }

        Collect(current);

        foreach (var kv in varToCongruences)
        {
            var varName = kv.Key;
            var conqs = kv.Value;
            
            try
            {
                long resultRem = conqs[0].rem;
                long resultMod = conqs[0].mod;
                for (int i = 1; i < conqs.Count; i++)
                {
                    (resultRem, resultMod) = SolveTwoCongruences(resultRem, resultMod, conqs[i].rem, conqs[i].mod);
                }

                if (varToBounds.TryGetValue(varName, out var bounds))
                {
                    if (bounds.upper.HasValue && bounds.lower.HasValue)
                    {
                        var solutions = new List<IExpression>();
                        long kMin = (long)Math.Ceiling((double)(bounds.lower.Value - resultRem) / resultMod);
                        long kMax = (long)Math.Floor((double)(bounds.upper.Value - resultRem) / resultMod);
                        // Limit search
                        if (kMax - kMin > 10000) continue; 

                        for (long k = kMin; k <= kMax; k++)
                        {
                            long x = k * resultMod + resultRem;
                            solutions.Add(new Number(x));
                        }
                        if (solutions.Count > 0)
                        {
                            result = new Equality(new Symbol(varName), solutions.Count == 1 ? solutions[0] : new Sym.Operations.Vector(solutions.ToImmutableList())).Canonicalize();
                            return true;
                        }
                    }
                    else if (bounds.upper.HasValue)
                    {
                        long k = (long)Math.Floor((double)(bounds.upper.Value - resultRem) / resultMod);
                        long x = k * resultMod + resultRem;
                        
                        if (!bounds.lower.HasValue || x >= bounds.lower.Value)
                        {
                            result = new Equality(new Symbol(varName), new Number(x)).Canonicalize();
                            return true;
                        }
                    }
                    else if (bounds.lower.HasValue)
                    {
                        long k = (long)Math.Ceiling((double)(bounds.lower.Value - resultRem) / resultMod);
                        long x = k * resultMod + resultRem;
                        result = new Equality(new Symbol(varName), new Number(x)).Canonicalize();
                        return true;
                    }
                }
            }
            catch { }
        }

        return false;
    }

    private static IExpression SimplifyModular(IExpression expr)
    {
        if (expr is Equality eq)
        {
            if (TryExtractCongruence(eq, out var rem, out var mod, out var varName))
            {
                return new Equality(new Function("mod", new Symbol(varName), new Number(mod)), new Number(rem)).Canonicalize();
            }
        }

        if (expr is Function f && f.Name.Equals("ModInverse", StringComparison.OrdinalIgnoreCase) && f.Arguments.Count == 2)
        {
            if (NumericEvaluator.TryEvaluate(f.Arguments[0], ImmutableDictionary<string, decimal>.Empty, out var a, out _) &&
                NumericEvaluator.TryEvaluate(f.Arguments[1], ImmutableDictionary<string, decimal>.Empty, out var m, out _))
            {
                try
                {
                    return new Number(ModularInverse((long)a, (long)m));
                }
                catch { }
            }
        }

        if (expr is Function f2 && f2.Name.Equals("mod", StringComparison.OrdinalIgnoreCase) && f2.Arguments.Count == 2)
        {
            if (NumericEvaluator.TryEvaluate(f2.Arguments[1], ImmutableDictionary<string, decimal>.Empty, out var modVal, out _))
            {
                long mod = (long)modVal;
                if (mod > 0)
                {
                    try
                    {
                        long res = EvalModular(f2.Arguments[0], mod);
                        return new Number(res);
                    }
                    catch
                    {
                        // Fallback to numeric evaluator if recursive evaluation fails, BUT only if it wasn't a structure we support.
                        // If we fall back to numeric for fractions, we get decimals which ruins modular arithmetic.
                        // So we strictly avoid fallback if the expression contained Division.
                        bool hasDivision = f2.Arguments[0] is Divide || f2.Arguments[0].ContainsSymbol(_ => false); // Only checks for structure really
                        // Actually easier: EvalModular handles Number/Add/Mul/Div/Power. If it failed, it's likely due to symbols or unsupported ops.
                        // We should only fallback if we are sure it's safe.
                        
                        // Check if we can evaluate numerically to an integer
                        if (NumericEvaluator.TryEvaluate(f2.Arguments[0], ImmutableDictionary<string, decimal>.Empty, out var val, out _))
                        {
                            if (Math.Abs(val - Math.Round(val)) < 1e-9m)
                            {
                                var res = (long)Math.Round(val) % mod;
                                if (res < 0) res += mod;
                                return new Number(res);
                            }
                        }
                    }
                }
            }

            if (f2.Arguments[0] is Power p && p.Exponent is Number n && p.Base is Number a && f2.Arguments[1] is Number m)
            {
                return new Number(ModPow((long)a.Value, (long)n.Value, (long)m.Value));
            }
        }

        if (expr is Operation op)
        {
            var newArgs = op.Arguments.Select(SimplifyModular).ToImmutableList();
            return op.WithArguments(newArgs).Canonicalize();
        }

        return expr;
    }

    private static long EvalModular(IExpression expr, long m)
    {
        if (expr is Number n)
        {
            var val = n.Value;
            if (Math.Abs(val - Math.Round(val)) < 1e-9m)
            {
                var lVal = (long)Math.Round(val);
                var res = lVal % m;
                if (res < 0) res += m;
                return res;
            }
            throw new InvalidOperationException("Non-integer number in modular expression.");
        }
        if (expr is Add add)
        {
            long sum = 0;
            foreach (var arg in add.Arguments)
            {
                sum = (sum + EvalModular(arg, m)) % m;
            }
            return sum;
        }
        if (expr is Multiply mul)
        {
            long prod = 1;
            foreach (var arg in mul.Arguments)
            {
                prod = (prod * EvalModular(arg, m)) % m;
            }
            return prod;
        }
        if (expr is Divide div)
        {
            long num = EvalModular(div.Numerator, m);
            long den = EvalModular(div.Denominator, m);
            return (num * ModularInverse(den, m)) % m;
        }
        if (expr is Power pow)
        {
            long b = EvalModular(pow.Base, m);
            if (NumericEvaluator.TryEvaluate(pow.Exponent, ImmutableDictionary<string, decimal>.Empty, out var eVal, out _))
            {
                long e = (long)eVal;
                if (e < 0)
                {
                    long inv = ModularInverse(b, m);
                    return ModPow(inv, Math.Abs(e), m);
                }
                return ModPow(b, e, m);
            }
        }
        throw new InvalidOperationException($"Cannot evaluate modular expression: {expr.GetType().Name}");
    }


    private static (long rem, long mod) SolveTwoCongruences(long a1, long m1, long a2, long m2)
    {
        long g = Gcd(m1, m2);
        if ((a2 - a1) % g != 0) throw new InvalidOperationException("Congruences are inconsistent.");

        long newMod = (m1 * m2) / g;
        long inv = ModularInverse(m1 / g, m2 / g);
        long k = ((a2 - a1) / g * inv) % (m2 / g);
        if (k < 0) k += (m2 / g);

        long newRem = (m1 * k + a1) % newMod;
        return (newRem, newMod);
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            a %= b;
            (a, b) = (b, a);
        }
        return a;
    }
}
