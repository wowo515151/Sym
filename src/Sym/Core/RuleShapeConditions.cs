// Copyright Warren Harding 2026
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Sym.Core;

public static class RuleShapeConditions
{
    private static readonly ConditionalWeakTable<Func<ImmutableDictionary<string, IExpression>, bool>, ShapeConditionSpec> Specs = new();

    public static Func<ImmutableDictionary<string, IExpression>, bool> ElementWiseCompatible(string leftBindingName, string rightBindingName)
    {
        var spec = new ShapeConditionSpec(ShapeConditionKind.ElementWiseCompatible, leftBindingName, rightBindingName);
        Func<ImmutableDictionary<string, IExpression>, bool> condition = bindings =>
        {
            if (!TryGetBindingShape(bindings, leftBindingName, out var leftShape) ||
                !TryGetBindingShape(bindings, rightBindingName, out var rightShape))
            {
                return false;
            }

            return leftShape.AreDimensionsCompatibleForElementWise(rightShape);
        };

        Specs.Add(condition, spec);
        return condition;
    }

    public static Func<ImmutableDictionary<string, IExpression>, bool> MatMulCompatible(string leftBindingName, string rightBindingName)
    {
        var spec = new ShapeConditionSpec(ShapeConditionKind.MatMulCompatible, leftBindingName, rightBindingName);
        Func<ImmutableDictionary<string, IExpression>, bool> condition = bindings =>
        {
            if (!TryGetBindingShape(bindings, leftBindingName, out var leftShape) ||
                !TryGetBindingShape(bindings, rightBindingName, out var rightShape))
            {
                return false;
            }

            return AreMatMulCompatible(leftShape, rightShape);
        };

        Specs.Add(condition, spec);
        return condition;
    }

    public static Func<ImmutableDictionary<string, IExpression>, bool> SameShape(string leftBindingName, string rightBindingName)
    {
        var spec = new ShapeConditionSpec(ShapeConditionKind.SameShape, leftBindingName, rightBindingName);
        Func<ImmutableDictionary<string, IExpression>, bool> condition = bindings =>
        {
            if (!TryGetBindingShape(bindings, leftBindingName, out var leftShape) ||
                !TryGetBindingShape(bindings, rightBindingName, out var rightShape))
            {
                return false;
            }

            return leftShape.Equals(rightShape);
        };

        Specs.Add(condition, spec);
        return condition;
    }

    public static bool TryEvaluate(
        Func<ImmutableDictionary<string, IExpression>, bool>? condition,
        Func<string, Shape?> shapeResolver,
        out bool handled,
        out bool result)
    {
        handled = false;
        result = false;
        if (condition is null || !Specs.TryGetValue(condition, out var spec))
        {
            return false;
        }

        handled = true;
        if (shapeResolver(spec.LeftBindingName) is not Shape leftShape ||
            shapeResolver(spec.RightBindingName) is not Shape rightShape)
        {
            return true;
        }

        result = spec.Kind switch
        {
            ShapeConditionKind.ElementWiseCompatible => leftShape.AreDimensionsCompatibleForElementWise(rightShape),
            ShapeConditionKind.MatMulCompatible => AreMatMulCompatible(leftShape, rightShape),
            ShapeConditionKind.SameShape => leftShape.Equals(rightShape),
            _ => false
        };
        return true;
    }

    private static bool TryGetBindingShape(
        ImmutableDictionary<string, IExpression> bindings,
        string bindingName,
        out Shape shape)
    {
        shape = Shape.Error;
        if (!bindings.TryGetValue(bindingName, out var expression))
        {
            return false;
        }

        shape = expression.Shape;
        return shape.IsValid && !shape.IsWildcardShape;
    }

    private enum ShapeConditionKind
    {
        ElementWiseCompatible,
        MatMulCompatible,
        SameShape
    }

    private static bool AreMatMulCompatible(Shape leftShape, Shape rightShape)
    {
        if (!leftShape.IsValid || leftShape.IsWildcardShape ||
            !rightShape.IsValid || rightShape.IsWildcardShape ||
            leftShape.IsScalar || rightShape.IsScalar)
        {
            return false;
        }

        var dimsA = leftShape.Dimensions;
        var dimsB = rightShape.Dimensions;

        if (dimsA.Length >= 2 && dimsB.Length >= 2)
        {
            if (dimsA[^1] != dimsB[^2])
            {
                return false;
            }

            var batchA = new Shape(dimsA.Take(dimsA.Length - 2).ToImmutableArray());
            var batchB = new Shape(dimsB.Take(dimsB.Length - 2).ToImmutableArray());
            return batchA.CombineForElementWise(batchB).IsValid;
        }

        if (dimsA.Length >= 2 && dimsB.Length == 1)
        {
            return dimsA[^1] == dimsB[0];
        }

        if (dimsA.Length == 1 && dimsB.Length >= 2)
        {
            return dimsA[0] == dimsB[0];
        }

        if (dimsA.Length == 1 && dimsB.Length == 1)
        {
            return dimsA[0] == dimsB[0];
        }

        return false;
    }

    private sealed record ShapeConditionSpec(
        ShapeConditionKind Kind,
        string LeftBindingName,
        string RightBindingName);
}
