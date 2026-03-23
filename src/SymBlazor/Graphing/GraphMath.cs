using Sym.Atoms;
using Sym.Core;
using Sym.CSharpIO;
using Sym.Operations;
using System.Globalization;

namespace SymBlazor.Graphing;

public static class GraphMath
{
    public sealed record SurfaceSample(
        IReadOnlyList<double> X,
        IReadOnlyList<double> Y,
        IReadOnlyList<IReadOnlyList<double?>> Z);

    public sealed record ExpressionTreeNode(string Label, IReadOnlyList<ExpressionTreeNode> Children);

    public static IExpression ParseSingleExpression(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new InvalidOperationException("Enter an expression.");
        }

        var expressions = CSharpIO.ParseExpressionsStrict(input);
        if (expressions.Count != 1)
        {
            throw new InvalidOperationException("Enter exactly one expression.");
        }

        return expressions[0];
    }

    public static IReadOnlyList<double> CreateLinearRange(double start, double end, int count)
    {
        if (count < 2)
        {
            count = 2;
        }

        var values = new double[count];
        var step = (end - start) / (count - 1);
        for (var i = 0; i < count; i++)
        {
            values[i] = start + (step * i);
        }

        return values;
    }

    public static IReadOnlyList<double?> SampleFunction(string expressionText, string variableName, double start, double end, int count)
    {
        var expression = ParseSingleExpression(expressionText);
        var samples = CreateLinearRange(start, end, count);
        var values = new double?[samples.Count];

        for (var i = 0; i < samples.Count; i++)
        {
            values[i] = TryEvaluate(expression, new Dictionary<string, double>(StringComparer.Ordinal)
            {
                [variableName] = samples[i]
            }, out var value)
                ? value
                : null;
        }

        return values;
    }

    public static (IReadOnlyList<double?> X, IReadOnlyList<double?> Y) SampleParametric2D(
        string xExpressionText,
        string yExpressionText,
        string parameterName,
        double start,
        double end,
        int count)
    {
        var xExpression = ParseSingleExpression(xExpressionText);
        var yExpression = ParseSingleExpression(yExpressionText);
        var samples = CreateLinearRange(start, end, count);
        var xValues = new double?[samples.Count];
        var yValues = new double?[samples.Count];

        for (var i = 0; i < samples.Count; i++)
        {
            var variables = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                [parameterName] = samples[i]
            };

            xValues[i] = TryEvaluate(xExpression, variables, out var xValue) ? xValue : null;
            yValues[i] = TryEvaluate(yExpression, variables, out var yValue) ? yValue : null;
        }

        return (xValues, yValues);
    }

    public static (IReadOnlyList<double?> R, IReadOnlyList<double?> Theta) SamplePolar(
        string expressionText,
        string angleName,
        double start,
        double end,
        int count)
    {
        var expression = ParseSingleExpression(expressionText);
        var samples = CreateLinearRange(start, end, count);
        var radii = new double?[samples.Count];
        var theta = new double?[samples.Count];

        for (var i = 0; i < samples.Count; i++)
        {
            theta[i] = samples[i] * (180d / Math.PI);
            radii[i] = TryEvaluate(expression, new Dictionary<string, double>(StringComparer.Ordinal)
            {
                [angleName] = samples[i]
            }, out var value)
                ? value
                : null;
        }

        return (radii, theta);
    }

    public static SurfaceSample SampleSurface(
        string expressionText,
        string xName,
        string yName,
        double xStart,
        double xEnd,
        double yStart,
        double yEnd,
        int xCount,
        int yCount)
    {
        var expression = ParseSingleExpression(expressionText);
        var xValues = CreateLinearRange(xStart, xEnd, xCount);
        var yValues = CreateLinearRange(yStart, yEnd, yCount);
        var zRows = new List<IReadOnlyList<double?>>(yValues.Count);

        for (var row = 0; row < yValues.Count; row++)
        {
            var zRow = new double?[xValues.Count];
            for (var column = 0; column < xValues.Count; column++)
            {
                zRow[column] = TryEvaluate(expression, new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    [xName] = xValues[column],
                    [yName] = yValues[row]
                }, out var zValue)
                    ? zValue
                    : null;
            }

            zRows.Add(zRow);
        }

        return new SurfaceSample(xValues, yValues, zRows);
    }

    public static (IReadOnlyList<double?> X, IReadOnlyList<double?> Y, IReadOnlyList<double?> Z) SampleParametric3D(
        string xExpressionText,
        string yExpressionText,
        string zExpressionText,
        string parameterName,
        double start,
        double end,
        int count)
    {
        var xExpression = ParseSingleExpression(xExpressionText);
        var yExpression = ParseSingleExpression(yExpressionText);
        var zExpression = ParseSingleExpression(zExpressionText);
        var samples = CreateLinearRange(start, end, count);
        var xValues = new double?[samples.Count];
        var yValues = new double?[samples.Count];
        var zValues = new double?[samples.Count];

        for (var i = 0; i < samples.Count; i++)
        {
            var variables = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                [parameterName] = samples[i]
            };

            xValues[i] = TryEvaluate(xExpression, variables, out var xValue) ? xValue : null;
            yValues[i] = TryEvaluate(yExpression, variables, out var yValue) ? yValue : null;
            zValues[i] = TryEvaluate(zExpression, variables, out var zValue) ? zValue : null;
        }

        return (xValues, yValues, zValues);
    }

    public static (IReadOnlyList<double?> X, IReadOnlyList<double?> Y) SampleVectorField(
        string xComponentText,
        string yComponentText,
        string xName,
        string yName,
        double xStart,
        double xEnd,
        double yStart,
        double yEnd,
        int density,
        double scale)
    {
        var xComponent = ParseSingleExpression(xComponentText);
        var yComponent = ParseSingleExpression(yComponentText);
        var xs = CreateLinearRange(xStart, xEnd, density);
        var ys = CreateLinearRange(yStart, yEnd, density);
        var lineX = new List<double?>();
        var lineY = new List<double?>();
        var xStep = xs.Count > 1 ? Math.Abs(xs[1] - xs[0]) : 1d;
        var yStep = ys.Count > 1 ? Math.Abs(ys[1] - ys[0]) : 1d;
        var maxArrowLength = Math.Min(xStep, yStep) * 0.72d;

        foreach (var y in ys)
        {
            foreach (var x in xs)
            {
                var variables = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    [xName] = x,
                    [yName] = y
                };

                if (!TryEvaluate(xComponent, variables, out var u) || !TryEvaluate(yComponent, variables, out var v))
                {
                    continue;
                }

                var magnitude = Math.Sqrt((u * u) + (v * v));
                if (magnitude < 1e-9 || double.IsNaN(magnitude) || double.IsInfinity(magnitude))
                {
                    continue;
                }

                var dx = u * scale;
                var dy = v * scale;
                var drawnLength = Math.Sqrt((dx * dx) + (dy * dy));
                if (drawnLength > maxArrowLength)
                {
                    var clampRatio = maxArrowLength / drawnLength;
                    dx *= clampRatio;
                    dy *= clampRatio;
                    drawnLength = maxArrowLength;
                }

                var tipX = x + dx;
                var tipY = y + dy;

                lineX.Add(x);
                lineY.Add(y);
                lineX.Add(tipX);
                lineY.Add(tipY);
                lineX.Add(null);
                lineY.Add(null);

                var angle = Math.Atan2(dy, dx);
                var headLength = Math.Min(drawnLength * 0.42d, maxArrowLength * 0.42d);
                var leftAngle = angle + (5d * Math.PI / 6d);
                var rightAngle = angle - (5d * Math.PI / 6d);

                lineX.Add(tipX);
                lineY.Add(tipY);
                lineX.Add(tipX + (Math.Cos(leftAngle) * headLength));
                lineY.Add(tipY + (Math.Sin(leftAngle) * headLength));
                lineX.Add(null);
                lineY.Add(null);

                lineX.Add(tipX);
                lineY.Add(tipY);
                lineX.Add(tipX + (Math.Cos(rightAngle) * headLength));
                lineY.Add(tipY + (Math.Sin(rightAngle) * headLength));
                lineX.Add(null);
                lineY.Add(null);
            }
        }

        return (lineX, lineY);
    }

    public static ExpressionTreeNode BuildExpressionTree(string expressionText)
    {
        var expression = ParseSingleExpression(expressionText);
        return BuildExpressionTree(expression);
    }

    public static bool TryEvaluate(IExpression expression, IReadOnlyDictionary<string, double> variables, out double value)
    {
        try
        {
            var substituted = SubstituteVariables(expression, variables).Canonicalize();
            return TryEvaluateInternal(substituted, variables, out value);
        }
        catch
        {
            value = 0d;
            return false;
        }
    }

    private static ExpressionTreeNode BuildExpressionTree(IExpression expression)
    {
        if (expression is Operation operation)
        {
            return new ExpressionTreeNode(
                LabelFor(expression),
                operation.Arguments.Select(BuildExpressionTree).ToList());
        }

        return new ExpressionTreeNode(LabelFor(expression), Array.Empty<ExpressionTreeNode>());
    }

    private static string LabelFor(IExpression expression)
    {
        return expression switch
        {
            Number number => number.Value.ToString(CultureInfo.InvariantCulture),
            Symbol symbol => symbol.Name,
            Function function => function.Name,
            _ => expression.GetType().Name.Replace("Expression", string.Empty, StringComparison.Ordinal)
        };
    }

    private static IExpression SubstituteVariables(IExpression expression, IReadOnlyDictionary<string, double> variables)
    {
        return ExpressionHelpers.Transform(expression, current =>
        {
            if (current is Symbol symbol && variables.TryGetValue(symbol.Name, out var value))
            {
                return new Number(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
            }

            return current;
        });
    }

    private static bool TryEvaluateInternal(IExpression expression, IReadOnlyDictionary<string, double> variables, out double value)
    {
        switch (expression)
        {
            case Number number:
                value = (double)number.Value;
                return IsFinite(value);
            case Symbol symbol:
                return TryResolveSymbol(symbol, variables, out value);
            case Add add:
            {
                value = 0d;
                foreach (var argument in add.Arguments)
                {
                    if (!TryEvaluateInternal(argument, variables, out var term))
                    {
                        value = 0d;
                        return false;
                    }

                    value += term;
                }

                return IsFinite(value);
            }
            case Multiply multiply:
            {
                value = 1d;
                foreach (var argument in multiply.Arguments)
                {
                    if (!TryEvaluateInternal(argument, variables, out var factor))
                    {
                        value = 0d;
                        return false;
                    }

                    value *= factor;
                }

                return IsFinite(value);
            }
            case Subtract subtract:
                if (TryEvaluateInternal(subtract.LeftOperand, variables, out var left) &&
                    TryEvaluateInternal(subtract.RightOperand, variables, out var right))
                {
                    value = left - right;
                    return IsFinite(value);
                }
                break;
            case Divide divide:
                if (TryEvaluateInternal(divide.Numerator, variables, out var numerator) &&
                    TryEvaluateInternal(divide.Denominator, variables, out var denominator) &&
                    Math.Abs(denominator) > 1e-12d)
                {
                    value = numerator / denominator;
                    return IsFinite(value);
                }
                break;
            case Power power:
                if (TryEvaluateInternal(power.Base, variables, out var basis) &&
                    TryEvaluateInternal(power.Exponent, variables, out var exponent))
                {
                    value = Math.Pow(basis, exponent);
                    return IsFinite(value);
                }
                break;
            case Function function:
                return TryEvaluateFunction(function, variables, out value);
        }

        value = 0d;
        return false;
    }

    private static bool TryResolveSymbol(Symbol symbol, IReadOnlyDictionary<string, double> variables, out double value)
    {
        if (variables.TryGetValue(symbol.Name, out value))
        {
            return IsFinite(value);
        }

        if (symbol.Name.Equals("pi", StringComparison.OrdinalIgnoreCase))
        {
            value = Math.PI;
            return true;
        }

        if (symbol.Name.Equals("e", StringComparison.OrdinalIgnoreCase))
        {
            value = Math.E;
            return true;
        }

        if (symbol.Name.Equals("degree", StringComparison.OrdinalIgnoreCase) ||
            symbol.Name.Equals("deg", StringComparison.OrdinalIgnoreCase))
        {
            value = Math.PI / 180d;
            return true;
        }

        value = 0d;
        return false;
    }

    private static bool TryEvaluateFunction(Function function, IReadOnlyDictionary<string, double> variables, out double value)
    {
        var args = new double[function.Arguments.Count];
        for (var i = 0; i < function.Arguments.Count; i++)
        {
            if (!TryEvaluateInternal(function.Arguments[i], variables, out args[i]))
            {
                value = 0d;
                return false;
            }
        }

        try
        {
            value = function.Name.ToLowerInvariant() switch
            {
                "sin" when args.Length == 1 => Math.Sin(args[0]),
                "cos" when args.Length == 1 => Math.Cos(args[0]),
                "tan" when args.Length == 1 => Math.Tan(args[0]),
                "asin" when args.Length == 1 => Math.Asin(args[0]),
                "acos" when args.Length == 1 => Math.Acos(args[0]),
                "atan" when args.Length == 1 => Math.Atan(args[0]),
                "exp" when args.Length == 1 => Math.Exp(args[0]),
                "log" when args.Length == 1 => Math.Log(args[0]),
                "log" when args.Length == 2 => Math.Log(args[1], args[0]),
                "log10" when args.Length == 1 => Math.Log10(args[0]),
                "log2" when args.Length == 1 => Math.Log2(args[0]),
                "sqrt" when args.Length == 1 => Math.Sqrt(args[0]),
                "abs" when args.Length == 1 => Math.Abs(args[0]),
                "floor" when args.Length == 1 => Math.Floor(args[0]),
                "ceil" when args.Length == 1 => Math.Ceiling(args[0]),
                "ceiling" when args.Length == 1 => Math.Ceiling(args[0]),
                "min" when args.Length >= 1 => args.Min(),
                "max" when args.Length >= 1 => args.Max(),
                _ => double.NaN
            };
        }
        catch
        {
            value = 0d;
            return false;
        }

        return IsFinite(value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
