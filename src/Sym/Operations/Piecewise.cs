using System;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;

namespace Sym.Operations
{
    /// <summary>
    /// Represents a piecewise expression with value/condition pairs.
    /// Arguments are stored as [value1, condition1, value2, condition2, ...].
    /// </summary>
    public sealed class Piecewise : Operation
    {
        public Piecewise(ImmutableList<IExpression> arguments) : base(arguments) { }

        public Piecewise(params IExpression[] arguments) : base(ImmutableList.Create(arguments)) { }

        public override Shape Shape
        {
            get
            {
                if (Arguments.Count == 0) return Shape.Error;
                var firstValue = Arguments[0];
                return firstValue.Shape;
            }
        }

        public override IExpression Canonicalize()
        {
            if (Arguments.Count == 0)
            {
                return this;
            }

            var builder = ImmutableList.CreateBuilder<IExpression>();
            for (int i = 0; i < Arguments.Count; i += 2)
            {
                if (i + 1 < Arguments.Count)
                {
                    var value = Arguments[i].Canonicalize();
                    var guard = Arguments[i + 1].Canonicalize();

                    // Handle boolean guards
                    if (guard is Symbol sGuard)
                    {
                        if (sGuard.Name.Equals("true", StringComparison.OrdinalIgnoreCase))
                        {
                            // This branch always applies. 
                            // If we have no branches yet, we can just return this value.
                            if (builder.Count == 0) return value;

                            // If we already have branches, this 'true' guard makes it the default case.
                            builder.Add(value);
                            return new Piecewise(builder.ToImmutable());
                        }
                        if (sGuard.Name.Equals("false", StringComparison.OrdinalIgnoreCase))
                        {
                            // Skip this branch
                            continue;
                        }
                    }

                    // Flatten nested piecewise in value by distributing guard.
                    if (value is Piecewise nested && (nested.Arguments.Count % 2 == 0 || nested.Arguments.Count > 1))
                    {
                        int nestedCount = nested.Arguments.Count;
                        bool hasDefault = nestedCount % 2 != 0;
                        int pairs = hasDefault ? nestedCount - 1 : nestedCount;

                        for (int j = 0; j < pairs; j += 2)
                        {
                            var innerValue = nested.Arguments[j];
                            var innerGuard = nested.Arguments[j + 1];
                            var combinedGuard = CombineGuard(guard, innerGuard);
                            builder.Add(innerValue);
                            builder.Add(combinedGuard);
                        }

                        if (hasDefault)
                        {
                            var innerValue = nested.Arguments[nestedCount - 1];
                            builder.Add(innerValue);
                            builder.Add(guard);
                        }
                    }
                    else
                    {
                        builder.Add(value);
                        builder.Add(guard);
                    }
                }
                else
                {
                    // Default case
                    var value = Arguments[i].Canonicalize();
                    if (value is Piecewise nested && (nested.Arguments.Count % 2 == 0 || nested.Arguments.Count > 1))
                    {
                        int nestedCount = nested.Arguments.Count;
                        bool hasDefault = nestedCount % 2 != 0;
                        int pairs = hasDefault ? nestedCount - 1 : nestedCount;

                        for (int j = 0; j < pairs; j += 2)
                        {
                            var innerValue = nested.Arguments[j];
                            var innerGuard = nested.Arguments[j + 1];
                            builder.Add(innerValue);
                            builder.Add(innerGuard);
                        }

                        if (hasDefault)
                        {
                            var innerValue = nested.Arguments[nestedCount - 1];
                            builder.Add(innerValue);
                        }
                    }
                    else
                    {
                        if (builder.Count == 0) return value;
                        builder.Add(value);
                    }
                }
            }

            var canonicalArgs = builder.ToImmutable();
            if (canonicalArgs.Count == 0) return new Number(0); // All branches were false and no default
            if (canonicalArgs.Count == 1) return canonicalArgs[0];

            return new Piecewise(canonicalArgs);
        }

        private static IExpression CombineGuard(IExpression outer, IExpression inner)
        {
            // and(outer, inner)
            var args = ImmutableList.Create(outer, inner);
            return new Function("and", args).Canonicalize();
        }

        public override string ToDisplayString()
        {
            var parts = Arguments.Chunk(2)
                .Select(pair => pair.Length == 2
                    ? $"{pair[0].ToDisplayString()} if {pair[1].ToDisplayString()}"
                    : pair[0].ToDisplayString());
            return $"Piecewise({string.Join(", ", parts)})";
        }

        public override bool InternalEquals(IExpression other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is not Piecewise pw) return false;
            return ExpressionHelpers.SequencesInternalEquals(this.Arguments, pw.Arguments);
        }

        public override int InternalGetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(GetType());
            foreach (var arg in Arguments)
            {
                hash.Add(arg.InternalGetHashCode());
            }
            return hash.ToHashCode();
        }

        public override Operation WithArguments(ImmutableList<IExpression> newArgs)
        {
            return new Piecewise(newArgs);
        }
    }
}
