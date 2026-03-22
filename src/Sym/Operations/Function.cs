// Copyright Warren Harding 2026
using Sym.Core;
using Sym.Atoms;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    public sealed class Function : Operation
    {
        public override string Head => $"Func:{Name}";
        public string Name { get; init; }

        public Function(string name, IEnumerable<IExpression> arguments) : this(name, arguments.ToImmutableList())
        {
        }

        public Function(string name, params IExpression[] arguments) : this(name, arguments.ToImmutableList())
        {
        }

        public Function(string name, ImmutableList<IExpression> arguments) : base(arguments)
        {
            Name = name;
        }

        public override Shape Shape
        {
            get
            {
                Shape resultShape = Shape.Scalar;
                foreach (IExpression arg in Arguments)
                {
                    if (!arg.Shape.IsValid)
                    {
                        return Shape.Error;
                    }

                    resultShape = resultShape.CombineForElementWise(arg.Shape);
                    if (!resultShape.IsValid)
                    {
                        return Shape.Error; // Incompatible shapes found during aggregation
                    }
                }
                return resultShape;
            }
        }

        public override IExpression Canonicalize()
        {
            ImmutableList<IExpression> canonicalArgs = Arguments.Select(arg => arg.Canonicalize()).ToImmutableList();
            var lowerName = Name.ToLowerInvariant();

            // Attempt to numerically evaluate known functions if arguments are numbers.
            IExpression? evaluatedResult = null;
            if (canonicalArgs.All(arg => arg is Number))
            {
                try
                {
                    // Handle single-argument functions
                    if (canonicalArgs.Count == 1)
                    {
                        double val = (double)((Number)canonicalArgs[0]).Value;
                        double result;
                        bool evaluated = true;
                        switch (lowerName)
                        {
                            case "asin": result = Math.Asin(val); break;
                            case "acos": result = Math.Acos(val); break;
                            case "atan": result = Math.Atan(val); break;
                            case "sin":
                            case "cos":
                            case "tan":
                                evaluated = false;
                                result = 0;
                                break;
                            case "exp": result = Math.Exp(val); break;
                            case "log": result = Math.Log(val); break; // Natural log
                            case "log10": result = Math.Log10(val); break;
                            case "log2": result = Math.Log2(val); break;
                            case "sqrt":
                            {
                                if (val < 0) { evaluated = false; result = 0; break; }
                                result = Math.Sqrt(val);
                                if (Math.Abs(result - Math.Round(result)) > 1e-10)
                                {
                                    evaluated = false;
                                }
                                break;
                            }
                            case "ceiling":
                            case "ceil":
                                result = Math.Ceiling(val);
                                break;
                            case "floor":
                                result = Math.Floor(val);
                                break;
                            case "abs":
                                result = Math.Abs(val);
                                break;
                            default:
                                evaluated = false;
                                result = 0; // dummy
                                break;
                        }
                        if (evaluated)
                        {
                            evaluatedResult = new Number(SymCore.NumericConvert.SafeToDecimal(result));
                        }
                    }
                    // Handle two-argument functions (e.g., Log(base, value))
                    else if (canonicalArgs.Count == 2 && lowerName == "log")
                    {
                        double baseVal = (double)((Number)canonicalArgs[0]).Value;
                        double val = (double)((Number)canonicalArgs[1]).Value;
                        double result = Math.Log(val, baseVal);
                        evaluatedResult = new Number(SymCore.NumericConvert.SafeToDecimal(result));
                    }
                }
                catch (Exception)
                {
                    // On any math or conversion error, evaluatedResult remains null,
                    // and we fall through to symbolic representation.
                }
            }

            if (evaluatedResult == null)
            {
                if (lowerName == "sum")
                {
                    if (canonicalArgs.Count == 1 && canonicalArgs[0] is Vector vec)
                    {
                        if (vec.Arguments.All(a => a is Number))
                        {
                            evaluatedResult = new Number(vec.Arguments.Cast<Number>().Sum(n => n.Value));
                        }
                        else if (vec.Arguments.Count == 0)
                        {
                            evaluatedResult = new Number(0m);
                        }
                    }
                    else if (canonicalArgs.All(a => a is Number))
                    {
                        evaluatedResult = new Number(canonicalArgs.Cast<Number>().Sum(n => n.Value));
                    }
                }
                else if (lowerName == "min" && canonicalArgs.Count > 0 && canonicalArgs.All(a => a is Number))
                {
                    evaluatedResult = new Number(canonicalArgs.Cast<Number>().Min(n => n.Value));
                }
                else if (lowerName == "max" && canonicalArgs.Count > 0 && canonicalArgs.All(a => a is Number))
                {
                    evaluatedResult = new Number(canonicalArgs.Cast<Number>().Max(n => n.Value));
                }
                else if (lowerName == "length" && canonicalArgs.Count == 1 && canonicalArgs[0] is Vector vec)
                {
                    evaluatedResult = new Number(vec.Arguments.Count);
                }
            }

            if (evaluatedResult != null)
            {
                return evaluatedResult;
            }

            if (lowerName == "sqrt" && canonicalArgs.Count == 1)
            {
                return new Power(canonicalArgs[0], new Number(0.5m)).Canonicalize();
            }

            if (IsLogicFunctionName(lowerName))
            {
                return CanonicalizeLogic(lowerName, canonicalArgs);
            }

            // If not evaluated, reconstruct the function if its arguments have changed during canonicalization.
            bool argumentsChanged = false;
            if (Arguments.Count != canonicalArgs.Count)
            {
                argumentsChanged = true;
            }
            else
            {
                for (int i = 0; i < Arguments.Count; i++)
                {
                    if (!ReferenceEquals(Arguments[i], canonicalArgs[i]))
                    {
                        argumentsChanged = true;
                        break;
                    }
                }
            }

            if (argumentsChanged)
            {
                return new Function(Name, canonicalArgs);
            }

            return this;
        }

        public override string ToDisplayString()
        {
            return $"{Name}({string.Join(", ", Arguments.Select(arg => arg.ToDisplayString()))})";
        }

        public override bool InternalEquals(IExpression other)
        {
            if (other is not Function otherFunc)
            {
                return false;
            }

            bool nameEquals;
            var lowerThis = Name.ToLowerInvariant();
            var lowerOther = otherFunc.Name.ToLowerInvariant();
            if (lowerThis == "sin" || lowerThis == "cos" || lowerThis == "tan" || lowerThis == "log" || lowerThis == "exp" || lowerThis == "sqrt" || lowerThis == "abs" || lowerThis == "valuation" || lowerThis == "factorial" || lowerThis == "combination" || lowerThis == "binomial" || lowerThis == "permutation")
            {
                nameEquals = string.Equals(lowerThis, lowerOther, StringComparison.Ordinal);
            }
            else
            {
                nameEquals = string.Equals(Name, otherFunc.Name, StringComparison.Ordinal);
            }

            if (!nameEquals)
            {
                return false;
            }
            if (Arguments.Count != otherFunc.Arguments.Count)
            {
                return false;
            }
            for (int i = 0; i < Arguments.Count; i++)
            {
                if (!Arguments[i].InternalEquals(otherFunc.Arguments[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public override int InternalGetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(GetType());
            
            var lowerName = Name.ToLowerInvariant();
            if (lowerName == "sin" || lowerName == "cos" || lowerName == "tan" || lowerName == "log" || lowerName == "exp" || lowerName == "sqrt" || lowerName == "abs" || lowerName == "valuation" || lowerName == "factorial" || lowerName == "combination" || lowerName == "binomial" || lowerName == "permutation")
            {
                hash.Add(lowerName);
            }
            else
            {
                hash.Add(Name);
            }

            foreach (IExpression arg in Arguments)
            {
                hash.Add(arg.InternalGetHashCode());
            }
            return hash.ToHashCode();
        }

        public override Operation WithArguments(ImmutableList<IExpression> newArgs)
        {
            return new Function(Name, newArgs);
        }

        private static bool IsLogicFunctionName(string lowerName)
        {
            return lowerName == "and" || lowerName == "or" || lowerName == "not" ||
                lowerName == "implies" || lowerName == "iff";
        }

        private static IExpression CanonicalizeLogic(string lowerName, ImmutableList<IExpression> canonicalArgs)
        {
            return lowerName switch
            {
                "and" => CanonicalizeCommutativeLogic(lowerName, canonicalArgs),
                "or" => CanonicalizeCommutativeLogic(lowerName, canonicalArgs),
                "not" => CanonicalizeNot(canonicalArgs),
                "implies" => new Function(lowerName, canonicalArgs),
                "iff" => CanonicalizeIff(canonicalArgs),
                _ => new Function(lowerName, canonicalArgs)
            };
        }

        private static IExpression CanonicalizeCommutativeLogic(string lowerName, ImmutableList<IExpression> canonicalArgs)
        {
            var flattened = new List<IExpression>();
            foreach (var arg in canonicalArgs)
            {
                if (arg is Function nested && nested.Name.Equals(lowerName, StringComparison.OrdinalIgnoreCase))
                {
                    flattened.AddRange(nested.Arguments);
                }
                else
                {
                    flattened.Add(arg);
                }
            }

            var positives = new HashSet<IExpression>(ExpressionHelpers.ExpressionEqualityComparer.Instance);
            var negatives = new HashSet<IExpression>(ExpressionHelpers.ExpressionEqualityComparer.Instance);
            bool isAnd = lowerName == "and";

            foreach (var arg in flattened)
            {
                if (TryGetBooleanConstant(arg, out bool value))
                {
                    if (isAnd)
                    {
                        if (!value) return new Symbol("false");
                        continue;
                    }
                    if (value) return new Symbol("true");
                    continue;
                }

                if (TryGetNegatedArgument(arg, out var inner))
                {
                    if (positives.Contains(inner))
                    {
                        return new Symbol(isAnd ? "false" : "true");
                    }
                    negatives.Add(inner);
                    continue;
                }

                if (negatives.Contains(arg))
                {
                    return new Symbol(isAnd ? "false" : "true");
                }
                positives.Add(arg);
            }

            var resultArgs = new List<IExpression>(positives.Count + negatives.Count);
            resultArgs.AddRange(positives);
            foreach (var neg in negatives)
            {
                resultArgs.Add(new Function("not", neg));
            }

            if (resultArgs.Count == 0)
            {
                return new Symbol(isAnd ? "true" : "false");
            }

            var sortedArgs = ExpressionHelpers.SortArguments(resultArgs.ToImmutableList());
            if (sortedArgs.Count == 1)
            {
                return sortedArgs[0];
            }

            return new Function(lowerName, sortedArgs);
        }

        private static IExpression CanonicalizeNot(ImmutableList<IExpression> canonicalArgs)
        {
            if (canonicalArgs.Count != 1)
            {
                return new Function("not", canonicalArgs);
            }

            var arg = canonicalArgs[0];
            if (TryGetBooleanConstant(arg, out bool value))
            {
                return new Symbol(value ? "false" : "true");
            }

            if (TryGetNegatedArgument(arg, out var inner))
            {
                return inner;
            }

            if (arg is Function innerFunc)
            {
                var innerName = innerFunc.Name.ToLowerInvariant();
                if (innerName == "and" || innerName == "or")
                {
                    var negatedArgs = innerFunc.Arguments
                        .Select(a => new Function("not", a).Canonicalize())
                        .ToImmutableList();
                    var flipped = innerName == "and" ? "or" : "and";
                    return new Function(flipped, negatedArgs).Canonicalize();
                }
            }

            return new Function("not", canonicalArgs);
        }

        private static IExpression CanonicalizeIff(ImmutableList<IExpression> canonicalArgs)
        {
            if (canonicalArgs.Count <= 1)
            {
                return new Function("iff", canonicalArgs);
            }

            var sorted = ExpressionHelpers.SortArguments(canonicalArgs);
            return new Function("iff", sorted);
        }

        private static bool TryGetBooleanConstant(IExpression expr, out bool value)
        {
            if (expr is Symbol symbol)
            {
                if (symbol.Name.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    value = true;
                    return true;
                }
                if (symbol.Name.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    value = false;
                    return true;
                }
            }
            value = false;
            return false;
        }

        private static bool TryGetNegatedArgument(IExpression expr, out IExpression inner)
        {
            if (expr is Function func &&
                func.Name.Equals("not", StringComparison.OrdinalIgnoreCase) &&
                func.Arguments.Count == 1)
            {
                inner = func.Arguments[0];
                return true;
            }

            inner = null!;
            return false;
        }
    }
}

