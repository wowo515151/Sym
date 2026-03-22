// Copyright Warren Harding 2026
using Sym.Atoms;
using Sym.Operations;

namespace Sym.Core
{
    public sealed class RandomExpressionGeneratorOptions
    {
        public int MaxDepth { get; init; } = 4;
        public int MinLiteralValue { get; init; } = -9;
        public int MaxLiteralValue { get; init; } = 9;
        public bool IncludeVariables { get; init; }
        public IReadOnlyList<string> VariableNames { get; init; } = Array.Empty<string>();
        public bool AllowDivision { get; init; } = true;
        public double LeafProbability { get; init; } = 0.35;
    }

    public sealed class RandomExpressionGenerator
    {
        private readonly Random _random;

        public RandomExpressionGenerator(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public IExpression GenerateNumericExpression(int maxDepth = 4)
        {
            return GenerateExpression(new RandomExpressionGeneratorOptions
            {
                MaxDepth = maxDepth,
                IncludeVariables = false,
                AllowDivision = true,
            });
        }

        public IExpression GenerateExpression(RandomExpressionGeneratorOptions? options = null)
        {
            var resolvedOptions = options ?? new RandomExpressionGeneratorOptions();
            ValidateOptions(resolvedOptions);
            return GenerateInternal(resolvedOptions, resolvedOptions.MaxDepth);
        }

        private IExpression GenerateInternal(RandomExpressionGeneratorOptions options, int remainingDepth)
        {
            if (remainingDepth <= 0 || _random.NextDouble() < options.LeafProbability)
            {
                return GenerateLeaf(options, forceNonZero: false);
            }

            int operationCount = options.AllowDivision ? 4 : 3;
            int operationIndex = _random.Next(operationCount);

            IExpression left = GenerateInternal(options, remainingDepth - 1);
            IExpression right = operationIndex == 3
                ? GenerateLeaf(options, forceNonZero: true)
                : GenerateInternal(options, remainingDepth - 1);

            return operationIndex switch
            {
                0 => new Add(left, right),
                1 => new Subtract(left, right),
                2 => new Multiply(left, right),
                3 => new Divide(left, right),
                _ => throw new InvalidOperationException("Unsupported operation index."),
            };
        }

        private IExpression GenerateLeaf(RandomExpressionGeneratorOptions options, bool forceNonZero)
        {
            bool canUseVariables =
                options.IncludeVariables &&
                !forceNonZero &&
                options.VariableNames.Count > 0;

            if (canUseVariables && _random.NextDouble() < 0.35)
            {
                string variableName = options.VariableNames[_random.Next(options.VariableNames.Count)];
                return new Symbol(variableName);
            }

            return new Number(NextLiteral(options, forceNonZero));
        }

        private decimal NextLiteral(RandomExpressionGeneratorOptions options, bool forceNonZero)
        {
            if (forceNonZero && options.MinLiteralValue == 0 && options.MaxLiteralValue == 0)
            {
                throw new InvalidOperationException("Cannot generate a non-zero literal from a zero-only range.");
            }

            int value;
            do
            {
                value = _random.Next(options.MinLiteralValue, options.MaxLiteralValue + 1);
            }
            while (forceNonZero && value == 0);

            return value;
        }

        private static void ValidateOptions(RandomExpressionGeneratorOptions options)
        {
            if (options.MaxDepth < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "MaxDepth must be non-negative.");
            }

            if (options.MinLiteralValue > options.MaxLiteralValue)
            {
                throw new ArgumentException("MinLiteralValue cannot be greater than MaxLiteralValue.", nameof(options));
            }

            if (options.LeafProbability is < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "LeafProbability must be between 0 and 1.");
            }
        }
    }
}