//Copyright Warren Harding 2025.
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using System.Collections.Immutable;
using System.Linq;
using System.Collections.Generic;
using System;

namespace Sym.Core
{
    public static class ExpressionHelpers
    {
        public static ImmutableList<IExpression> FlattenArguments<T>(ImmutableList<IExpression> arguments) where T : Operation
        {
            ImmutableList<IExpression>.Builder flattenedArgs = ImmutableList.CreateBuilder<IExpression>();
            Stack<IExpression> stack = new Stack<IExpression>();
            
            for (int i = arguments.Count - 1; i >= 0; i--)
            {
                stack.Push(arguments[i]);
            }

            while (stack.Count > 0)
            {
                IExpression current = stack.Pop();
                if (current is T nestedOp)
                {
                    for (int i = nestedOp.Arguments.Count - 1; i >= 0; i--)
                    {
                        stack.Push(nestedOp.Arguments[i]);
                    }
                }
                else
                {
                    flattenedArgs.Add(current);
                }
            }
            return flattenedArgs.ToImmutable();
        }

        public static ImmutableList<IExpression> SortArguments(ImmutableList<IExpression> arguments)
        {
            if (arguments.Count > 100)
            {
            }

            return arguments
                .OrderBy(arg => arg, new ExpressionComparer())
                .ToImmutableList();
        }

        public sealed class ExpressionEqualityComparer : System.Collections.Generic.IEqualityComparer<IExpression>
        {
            public static readonly ExpressionEqualityComparer Instance = new();
            public bool Equals(IExpression? x, IExpression? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x is null || y is null) return false;
                return x.InternalEquals(y);
            }
            public int GetHashCode(IExpression obj) => obj.InternalGetHashCode();
        }

        public static bool SequencesInternalEquals(ImmutableList<IExpression> a, ImmutableList<IExpression> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!a[i].InternalEquals(b[i])) return false;
            }
            return true;
        }

        public static int GetSequenceHashCode(Type type, ImmutableList<IExpression> sequence)
        {
            HashCode hash = new HashCode();
            hash.Add(type);
            foreach (var item in sequence)
            {
                hash.Add(item.InternalGetHashCode());
            }
            return hash.ToHashCode();
        }

        public static HashSet<string> CollectSymbolNames(IExpression expr)
        {
            var symbols = new HashSet<string>(StringComparer.Ordinal);
            CollectSymbolNames(expr, symbols);
            return symbols;
        }

        private static void CollectSymbolNames(IExpression expr, HashSet<string> symbols)
        {
            if (expr is Symbol s)
            {
                symbols.Add(s.Name);
                return;
            }

            if (expr is Operation op)
            {
                foreach (var arg in op.Arguments)
                {
                    CollectSymbolNames(arg, symbols);
                }
            }
        }

        public static List<IExpression> CollectSymbols(IExpression expr, Symbol? preferred = null)
        {
            return CollectSymbols(new[] { expr }, preferred);
        }

        public static List<IExpression> CollectSymbols(System.Collections.Generic.IEnumerable<IExpression> exprs, Symbol? preferred)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var symbols = new List<IExpression>();

            if (preferred is not null)
            {
                if (set.Add(preferred.ToDisplayString()))
                {
                    symbols.Add(preferred);
                }
            }

            void Visit(IExpression e)
            {
                if (e is Symbol s)
                {
                    if (s.Name.Equals("true", StringComparison.OrdinalIgnoreCase) || 
                        s.Name.Equals("false", StringComparison.OrdinalIgnoreCase)) return;
                    if (set.Add(s.Name))
                    {
                        symbols.Add(s);
                    }
                }
                else if (e is Function f && f.Arguments.All(arg => arg is Number || !arg.ContainsSymbol(sym => !IsMathConstant(sym.Name))))
                {
                    if (set.Add(f.ToDisplayString()))
                    {
                        symbols.Add(f);
                    }
                }
                else if (e is Operation op)
                {
                    foreach (var arg in op.Arguments) Visit(arg);
                }
            }

            foreach (var part in exprs)
            {
                Visit(part);
            }

            return symbols;
        }

        public static bool IsMathConstant(string name)
        {
            return string.Equals(name, "pi", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "e", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "i", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "j", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "degree", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "deg", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsBetter(IExpression next, IExpression existing)
        {
            if (next.InternalEquals(existing)) return false;

            // 1. Exact integers are better than anything else
            if (next is Number n1 && n1.Value % 1 == 0)
            {
                if (existing is Number n2 && n2.Value % 1 == 0)
                {
                    return n1.ToDisplayString().Length < n2.ToDisplayString().Length;
                }
                return true;
            }
            if (existing is Number n2_ && n2_.Value % 1 == 0) return false;

            // 2. Simple expressions (no variables except math constants) are better than ones with variables
            bool nextIsSimple = !next.ContainsSymbol(s => !IsMathConstant(s.Name));
            bool existingIsSimple = !existing.ContainsSymbol(s => !IsMathConstant(s.Name));
            if (nextIsSimple && !existingIsSimple) return true;
            if (existingIsSimple && !nextIsSimple) return false;

            // 3. Prefer symbolic constants (like pi, e, or Pow(3, 0.5)) over non-terminating decimals
            if (nextIsSimple && existingIsSimple)
            {
                bool nextIsNum = next is Number;
                bool existingIsNum = existing is Number;

                if (nextIsNum && !existingIsNum)
                {
                    // existing is symbolic (e.g. Pow(3, 0.5) or a symbol like 'target'). 
                    // Only prefer number if it's a short terminating decimal.
                    var s = ((Number)next).Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (s.Length <= 6) return true;
                    return false;
                }
                if (!nextIsNum && existingIsNum)
                {
                    var s = ((Number)existing).Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (s.Length <= 6) return false;
                    return true;
                }
            }

            // 3b. Special case: if we are comparing a Number against a Symbol (which might be a target),
            // only prefer the Number if it's an integer or very simple.
            if (next is Number num && existing is Symbol)
            {
                var s = num.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (num.Value % 1 == 0 || s.Length <= 6) return true;
                return false;
            }

            // 4. Shorter expressions are generally better
            if (next.ToDisplayString().Length < existing.ToDisplayString().Length) return true;

            return false;
        }

        public static IExpression Transform(IExpression expr, Func<IExpression, IExpression> transformer)
        {
            if (expr is Operation op)
            {
                var newArgs = op.Arguments.Select(a => Transform(a, transformer)).ToImmutableList();
                var newOp = op.WithArguments(newArgs);
                return transformer(newOp);
            }
            return transformer(expr);
        }

        public static int CountNodes(IExpression expr)
        {
            if (expr is Operation op)
            {
                int count = 1;
                foreach (var arg in op.Arguments) count += CountNodes(arg);
                return count;
            }
            return 1;
        }

        public static Symbol? FindFirstSymbol(IExpression expression)
        {
            if (expression is Symbol s) return s;
            if (expression is Operation op)
            {
                foreach (var arg in op.Arguments)
                {
                    var found = FindFirstSymbol(arg);
                    if (found is not null) return found;
                }
            }
            return null;
        }

        public static bool TryEvaluateSimple(IExpression e, out decimal val)
        {
            // Lightweight evaluator restricted to purely numeric Add/Multiply/Power trees.
            if (e is Number n) { val = n.Value; return true; }
            if (e is Add add)
            {
                decimal sum = 0m;
                foreach (var a in add.Arguments)
                {
                    if (!TryEvaluateSimple(a, out var v)) { val = 0m; return false; }
                    try { sum += v; } catch { val = 0m; return false; }
                }
                val = sum; return true;
            }
            if (e is Multiply mul)
            {
                decimal prod = 1m;
                foreach (var a in mul.Arguments)
                {
                    if (!TryEvaluateSimple(a, out var v)) { val = 0m; return false; }
                    try { prod = checked(prod * v); } catch { val = 0m; return false; }
                }
                val = prod; return true;
            }
            if (e is Power pow)
            {
                if (!TryEvaluateSimple(pow.Base, out var b) || !TryEvaluateSimple(pow.Exponent, out var ex)) { val = 0m; return false; }
                // Handle integer exponents efficiently
                if ((double)ex == Math.Floor((double)ex) && ex >= -1000m && ex <= 1000m)
                {
                    long ie = (long)ex;
                    try
                    {
                        if (ie >= 0)
                        {
                            decimal r = 1m;
                            for (long i = 0; i < ie; i++) r = checked(r * b);
                            val = r; return true;
                        }
                        else
                        {
                            if (b == 0m) { val = 0m; return false; }
                            decimal r = 1m;
                            for (long i = 0; i < -ie; i++) r = checked(r * b);
                            val = 1m / r; return true;
                        }
                    }
                    catch { val = 0m; return false; }
                }
                // Fallback to double for non-integer exponents
                try
                {
                    double dbl = Math.Pow((double)b, (double)ex);
                    if (double.IsNaN(dbl) || double.IsInfinity(dbl)) { val = 0m; return false; }
                    // Try convert to decimal safely
                    if (dbl > (double)decimal.MaxValue || dbl < (double)decimal.MinValue) { val = 0m; return false; }
                    val = (decimal)dbl; return true;
                }
                catch { val = 0m; return false; }
            }
            val = 0m; return false;
        }

        public delegate bool ExpressionEvaluator(IExpression expr, out decimal value);

        public static bool TryExtractLinearStruct(IExpression expr, System.Collections.Generic.IReadOnlyList<IExpression> symbols, ref decimal[] coefficients, ref decimal constant, ExpressionEvaluator? evaluator = null)
        {
            switch (expr)
            {
                case Number n:
                    constant += n.Value;
                    return true;
                case Symbol s when symbols.Any(sym => sym.InternalEquals(s)):
                {
                    var idx = -1;
                    for(int k=0; k<symbols.Count; k++) { if(symbols[k].InternalEquals(s)) { idx = k; break; } }
                    if (idx < 0) return false;
                    coefficients[idx] += 1m;
                    return true;
                }
                case Symbol s:
                {
                    if (s.Name.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        constant += 1m;
                        return true;
                    }
                    if (s.Name.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        constant += 0m;
                        return true;
                    }
                    if (evaluator != null && evaluator(s, out var val))
                    {
                        constant += val;
                        return true;
                    }
                    return false;
                }
                case Function f when symbols.Any(sym => sym.InternalEquals(f)):
                {
                    var idx = -1;
                    for(int k=0; k<symbols.Count; k++) { if(symbols[k].InternalEquals(f)) { idx = k; break; } }
                    if (idx >= 0)
                    {
                        coefficients[idx] += 1m;
                        return true;
                    }
                    return false;
                }
                case Function f:
                {
                    if (evaluator != null && evaluator(f, out var val))
                    {
                        constant += val;
                        return true;
                    }
                    return false;
                }
                case Divide div:
                    if (div.Denominator is Number denNum && denNum.Value != 0m)
                    {
                        if (!TryExtractLinearStruct(div.Numerator, symbols, ref coefficients, ref constant, evaluator)) return false;
                        for (int i = 0; i < symbols.Count; i++) coefficients[i] /= denNum.Value;
                        constant /= denNum.Value;
                        return true;
                    }
                    return false;
                case Subtract sub:
                    if (!TryExtractLinearStruct(sub.LeftOperand, symbols, ref coefficients, ref constant, evaluator)) return false;
                    var negCoeffs = new decimal[symbols.Count];
                    decimal negConstant = 0m;
                    if (!TryExtractLinearStruct(sub.RightOperand, symbols, ref negCoeffs, ref negConstant, evaluator)) return false;
                    for (int i = 0; i < symbols.Count; i++) coefficients[i] -= negCoeffs[i];
                    constant -= negConstant;
                    return true;
                case Add add:
                    foreach (var arg in add.Arguments)
                    {
                        var tempCoeffs = new decimal[symbols.Count];
                        decimal tempConstant = 0m;
                        if (!TryExtractLinearStruct(arg, symbols, ref tempCoeffs, ref tempConstant, evaluator)) return false;
                        for (int i = 0; i < symbols.Count; i++) coefficients[i] += tempCoeffs[i];
                        constant += tempConstant;
                    }
                    return true;
                case Multiply mul:
                {
                    decimal numeric = 1m;
                    IExpression? target = null;
                    try
                    {
                        foreach (var arg in mul.Arguments)
                        {
                            if (arg is Number num)
                            {
                                numeric *= num.Value;
                            }
                            else if (evaluator != null && evaluator(arg, out var val))
                            {
                                numeric *= val;
                            }
                            else if (target is null && symbols.Any(sym => sym.InternalEquals(arg)))
                            {
                                target = arg;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                    catch (OverflowException)
                    {
                        return false; 
                    }

                    if (target is null) 
                    {
                        constant += numeric;
                        return true;
                    }
                    var idx = -1;
                    for(int k=0; k<symbols.Count; k++) { if(symbols[k].InternalEquals(target)) { idx = k; break; } }
                    if (idx < 0) return false;
                    coefficients[idx] += numeric;
                    return true;
                }
                default:
                    if (evaluator != null && evaluator(expr, out var defVal))
                    {
                        constant += defVal;
                        return true;
                    }
                    return false;
            }
        }

        private sealed class ExpressionComparer : System.Collections.Generic.IComparer<IExpression>
        {
            public int Compare(IExpression? x, IExpression? y)
            {
                return CompareRecursive(x, y, 0);
            }

            private int CompareRecursive(IExpression? x, IExpression? y, int depth)
            {
                if (depth > 100) return 0; // Prevent stack overflow

                if (ReferenceEquals(x, y)) return 0;
                if (x is null) return -1;
                if (y is null) return 1;

                // Precedence: Number < Symbol < Wild < other Atom types < Operation types
                int typePrecedenceX = GetTypePrecedence(x);
                int typePrecedenceY = GetTypePrecedence(y);

                if (typePrecedenceX != typePrecedenceY)
                {
                    return typePrecedenceX.CompareTo(typePrecedenceY);
                }

                // If same type precedence, compare based on specific type rules
                if (x is Number numX && y is Number numY)
                {
                    return numX.Value.CompareTo(numY.Value);
                }
                else if (x is Symbol symX && y is Symbol symY)
                {
                    int nameComparison = string.CompareOrdinal(symX.Name, symY.Name);
                    if (nameComparison != 0) return nameComparison;
                    return symX.Shape.ToDisplayString().CompareTo(symY.Shape.ToDisplayString());
                }
                else if (x is Wild wildX && y is Wild wildY)
                {
                    int nameComparison = string.CompareOrdinal(wildX.Name, wildY.Name);
                    if (nameComparison != 0) return nameComparison;
                    return wildX.Constraint.CompareTo(wildY.Constraint);
                }
                else if (x is Atom && y is Atom) // Other Atoms by type name
                {
                    return string.CompareOrdinal(x.GetType().Name, y.GetType().Name);
                }
                else if (x is Operation opX && y is Operation opY)
                {
                    int typeNameComparison = string.CompareOrdinal(opX.GetType().Name, opY.GetType().Name);
                    if (typeNameComparison != 0) return typeNameComparison;

                    if (opX is Function fnX && opY is Function fnY)
                    {
                        int nameComparison = string.CompareOrdinal(fnX.Name, fnY.Name);
                        if (nameComparison != 0) return nameComparison;
                    }

                    int countComparison = opX.Arguments.Count.CompareTo(opY.Arguments.Count);
                    if (countComparison != 0) return countComparison;

                    for (int i = 0; i < opX.Arguments.Count; i++)
                    {
                        int argComparison = CompareRecursive(opX.Arguments[i], opY.Arguments[i], depth + 1);
                        if (argComparison != 0) return argComparison;
                    }
                    return 0;
                }

                // Fallback for types not explicitly handled above, usually by display string or type name
                return string.CompareOrdinal(x.ToDisplayString(), y.ToDisplayString());
            }

            private static int GetTypePrecedence(IExpression expr)
            {
                if (expr is Number) return 0;
                if (expr is Symbol) return 1;
                if (expr is Wild) return 2;
                if (expr is Atom) return 3; // Any other atom
                if (expr is Operation) return 4;
                return 5; // Default for unexpected types
            }
        }
    }
}
