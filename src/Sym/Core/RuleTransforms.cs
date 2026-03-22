// Copyright Warren Harding 2026
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Sym.Atoms;
using SymCore;

namespace Sym.Core;

public static class RuleTransforms
{
    private static readonly ConditionalWeakTable<Func<ImmutableDictionary<string, IExpression>, IExpression?>, TransformSpec> Specs = new();

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantAdd(string leftBindingName, string rightBindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantAdd, leftBindingName, rightBindingName);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingNumber(bindings, leftBindingName, out decimal left) ||
                !TryGetBindingNumber(bindings, rightBindingName, out decimal right))
            {
                return null;
            }

            return new Number(left + right);
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantMultiply(string leftBindingName, string rightBindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantMultiply, leftBindingName, rightBindingName);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingNumber(bindings, leftBindingName, out decimal left) ||
                !TryGetBindingNumber(bindings, rightBindingName, out decimal right))
            {
                return null;
            }

            return new Number(left * right);
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantDivide(string leftBindingName, string rightBindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantDivide, leftBindingName, rightBindingName);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingNumber(bindings, leftBindingName, out decimal left) ||
                !TryGetBindingNumber(bindings, rightBindingName, out decimal right) ||
                right == 0m)
            {
                return null;
            }

            return new Number(left / right);
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantPower(string leftBindingName, string rightBindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantPower, leftBindingName, rightBindingName);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingNumber(bindings, leftBindingName, out decimal left) ||
                !TryGetBindingNumber(bindings, rightBindingName, out decimal right))
            {
                return null;
            }

            return TryCreateNumber(Math.Pow((double)left, (double)right), out Number? number) ? number : null;
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantGreaterThan(string leftBindingName, string rightBindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantGreaterThan, leftBindingName, rightBindingName);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingNumber(bindings, leftBindingName, out decimal left) ||
                !TryGetBindingNumber(bindings, rightBindingName, out decimal right))
            {
                return null;
            }

            return new Symbol(left > right ? "true" : "false");
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantLessThan(string leftBindingName, string rightBindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantLessThan, leftBindingName, rightBindingName);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingNumber(bindings, leftBindingName, out decimal left) ||
                !TryGetBindingNumber(bindings, rightBindingName, out decimal right))
            {
                return null;
            }

            return new Symbol(left < right ? "true" : "false");
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantGreaterThanOrEqual(string leftBindingName, string rightBindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantGreaterThanOrEqual, leftBindingName, rightBindingName);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingNumber(bindings, leftBindingName, out decimal left) ||
                !TryGetBindingNumber(bindings, rightBindingName, out decimal right))
            {
                return null;
            }

            return new Symbol(left >= right ? "true" : "false");
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantLessThanOrEqual(string leftBindingName, string rightBindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantLessThanOrEqual, leftBindingName, rightBindingName);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingNumber(bindings, leftBindingName, out decimal left) ||
                !TryGetBindingNumber(bindings, rightBindingName, out decimal right))
            {
                return null;
            }

            return new Symbol(left <= right ? "true" : "false");
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantAnd(string leftBindingName, string rightBindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantAnd, leftBindingName, rightBindingName);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingBoolean(bindings, leftBindingName, out bool left) ||
                !TryGetBindingBoolean(bindings, rightBindingName, out bool right))
            {
                return null;
            }

            return new Symbol(left && right ? "true" : "false");
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantOr(string leftBindingName, string rightBindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantOr, leftBindingName, rightBindingName);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingBoolean(bindings, leftBindingName, out bool left) ||
                !TryGetBindingBoolean(bindings, rightBindingName, out bool right))
            {
                return null;
            }

            return new Symbol(left || right ? "true" : "false");
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantNot(string bindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantNot, bindingName, null);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingBoolean(bindings, bindingName, out bool value))
            {
                return null;
            }

            return new Symbol(!value ? "true" : "false");
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantSquareRoot(string bindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantSquareRoot, bindingName, null);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingNumber(bindings, bindingName, out decimal value) || value < 0m)
            {
                return null;
            }

            return TryCreateNumber(Math.Sqrt((double)value), out Number? number) ? number : null;
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantAbsoluteValue(string bindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantAbsoluteValue, bindingName, null);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            return TryGetBindingNumber(bindings, bindingName, out decimal value)
                ? new Number(Math.Abs(value))
                : null;
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantExp(string bindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantExp, bindingName, null);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingNumber(bindings, bindingName, out decimal value))
            {
                return null;
            }

            return TryCreateNumber(Math.Exp((double)value), out Number? number) ? number : null;
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantLog(string bindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantLog, bindingName, null);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingNumber(bindings, bindingName, out decimal value) || value <= 0m)
            {
                return null;
            }

            return TryCreateNumber(Math.Log((double)value), out Number? number) ? number : null;
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantSin(string bindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantSin, bindingName, null);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingNumber(bindings, bindingName, out decimal value))
            {
                return null;
            }

            return TryCreateNumber(Math.Sin((double)value), out Number? number) ? number : null;
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static Func<ImmutableDictionary<string, IExpression>, IExpression?> ConstantCos(string bindingName)
    {
        var spec = new TransformSpec(TransformKind.ConstantCos, bindingName, null);
        Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = bindings =>
        {
            if (!TryGetBindingNumber(bindings, bindingName, out decimal value))
            {
                return null;
            }

            return TryCreateNumber(Math.Cos((double)value), out Number? number) ? number : null;
        };

        Specs.Add(transform, spec);
        return transform;
    }

    public static bool TryEvaluate(
        Func<ImmutableDictionary<string, IExpression>, IExpression?>? transform,
        Func<string, IExpression?> bindingResolver,
        out bool handled,
        out IExpression? result)
    {
        handled = false;
        result = null;
        if (transform is null || !Specs.TryGetValue(transform, out var spec))
        {
            return false;
        }

        handled = true;
        if (TryEvaluateNumericTransform(spec, bindingResolver, out result))
        {
            return true;
        }

        if (TryEvaluateUnaryNumericTransform(spec, bindingResolver, out result))
        {
            return true;
        }

        TryEvaluateBooleanTransform(spec, bindingResolver, out result);
        return true;
    }

    private static bool TryEvaluateNumericTransform(
        TransformSpec spec,
        Func<string, IExpression?> bindingResolver,
        out IExpression? result)
    {
        result = null;
        if (!TryGetLiteralNumber(bindingResolver(spec.LeftBindingName), out decimal left) ||
            spec.RightBindingName is null ||
            !TryGetLiteralNumber(bindingResolver(spec.RightBindingName), out decimal right))
        {
            return false;
        }

        result = spec.Kind switch
        {
            TransformKind.ConstantAdd => new Number(left + right),
            TransformKind.ConstantMultiply => new Number(left * right),
            TransformKind.ConstantDivide when right != 0m => new Number(left / right),
            TransformKind.ConstantPower => TryCreatePowerNumber(left, right, out result) ? result : null,
            TransformKind.ConstantGreaterThan => new Symbol(left > right ? "true" : "false"),
            TransformKind.ConstantLessThan => new Symbol(left < right ? "true" : "false"),
            TransformKind.ConstantGreaterThanOrEqual => new Symbol(left >= right ? "true" : "false"),
            TransformKind.ConstantLessThanOrEqual => new Symbol(left <= right ? "true" : "false"),
            _ => null
        };
        return result is not null;
    }

    private static bool TryEvaluateUnaryNumericTransform(
        TransformSpec spec,
        Func<string, IExpression?> bindingResolver,
        out IExpression? result)
    {
        result = null;
        if (!TryGetLiteralNumber(bindingResolver(spec.LeftBindingName), out decimal value) ||
            spec.RightBindingName is not null)
        {
            return false;
        }

        return spec.Kind switch
        {
            TransformKind.ConstantSquareRoot when value >= 0m => TryCreateNumber(Math.Sqrt((double)value), out result),
            TransformKind.ConstantAbsoluteValue => TryReturnNumber(new Number(Math.Abs(value)), out result),
            TransformKind.ConstantExp => TryCreateNumber(Math.Exp((double)value), out result),
            TransformKind.ConstantLog when value > 0m => TryCreateNumber(Math.Log((double)value), out result),
            TransformKind.ConstantSin => TryCreateNumber(Math.Sin((double)value), out result),
            TransformKind.ConstantCos => TryCreateNumber(Math.Cos((double)value), out result),
            _ => false
        };
    }

    private static bool TryEvaluateBooleanTransform(
        TransformSpec spec,
        Func<string, IExpression?> bindingResolver,
        out IExpression? result)
    {
        result = null;

        if (!TryGetLiteralBoolean(bindingResolver(spec.LeftBindingName), out bool left))
        {
            return false;
        }

        if (spec.Kind == TransformKind.ConstantNot)
        {
            result = new Symbol(!left ? "true" : "false");
            return true;
        }

        if (spec.RightBindingName is null ||
            !TryGetLiteralBoolean(bindingResolver(spec.RightBindingName), out bool right))
        {
            return false;
        }

        result = spec.Kind switch
        {
            TransformKind.ConstantAnd => new Symbol(left && right ? "true" : "false"),
            TransformKind.ConstantOr => new Symbol(left || right ? "true" : "false"),
            _ => null
        };
        return result is not null;
    }

    private static bool TryGetBindingNumber(
        ImmutableDictionary<string, IExpression> bindings,
        string bindingName,
        out decimal value)
    {
        value = 0m;
        return bindings.TryGetValue(bindingName, out IExpression? expression) &&
               TryGetLiteralNumber(expression, out value);
    }

    private static bool TryGetLiteralNumber(IExpression? expression, out decimal value)
    {
        value = 0m;
        return expression is Number number && (value = number.Value) == number.Value;
    }

    private static bool TryGetBindingBoolean(
        ImmutableDictionary<string, IExpression> bindings,
        string bindingName,
        out bool value)
    {
        value = false;
        return bindings.TryGetValue(bindingName, out IExpression? expression) &&
               TryGetLiteralBoolean(expression, out value);
    }

    private static bool TryGetLiteralBoolean(IExpression? expression, out bool value)
    {
        if (expression is Symbol symbol)
        {
            if (symbol.Name.Equals("true", System.StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            if (symbol.Name.Equals("false", System.StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }
        }

        value = false;
        return false;
    }

    private static bool TryCreateNumber(double value, out Number? number)
    {
        number = null;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return false;
        }

        if (value >= (double)decimal.MaxValue || value <= (double)decimal.MinValue)
        {
            return false;
        }

        number = new Number(NumericConvert.SafeToDecimal(value));
        return true;
    }

    private static bool TryCreateNumber(double value, out IExpression? result)
    {
        result = null;
        if (!TryCreateNumber(value, out Number? number))
        {
            return false;
        }

        result = number;
        return true;
    }

    private static bool TryCreatePowerNumber(decimal left, decimal right, out IExpression? result)
    {
        return TryCreateNumber(Math.Pow((double)left, (double)right), out result);
    }

    private static bool TryReturnNumber(Number number, out IExpression? result)
    {
        result = number;
        return true;
    }

    private enum TransformKind
    {
        ConstantAdd,
        ConstantMultiply,
        ConstantDivide,
        ConstantPower,
        ConstantGreaterThan,
        ConstantLessThan,
        ConstantGreaterThanOrEqual,
        ConstantLessThanOrEqual,
        ConstantAnd,
        ConstantOr,
        ConstantNot,
        ConstantSquareRoot,
        ConstantAbsoluteValue,
        ConstantExp,
        ConstantLog,
        ConstantSin,
        ConstantCos
    }

    private sealed record TransformSpec(
        TransformKind Kind,
        string LeftBindingName,
        string? RightBindingName);
}
