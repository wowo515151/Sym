//Copyright Warren Harding 2025.
using System.Collections.Immutable;
using System;
using Sym.Core.Rewriters;
using SymCore;

namespace Sym.Core
{
    /// <summary>
    /// Represents a single rewrite rule, consisting of a pattern, a replacement, and an optional condition.
    /// </summary>
    public sealed class Rule
    {
        public string? Name { get; init; } = string.Empty;

        /// <summary>
        /// The pattern expression to match against. Can contain Wild objects.
        /// </summary>
        public IExpression Pattern { get; init; }
        /// <summary>
        /// The replacement expression. Can contain Wild objects that will be substituted with bound values from the pattern.
        /// </summary>
        public IExpression Replacement { get; init; }
        /// <summary>
        /// An optional predicate that must evaluate to true for the rule to apply.
        /// It operates on the collected bindings.
        /// </summary>
        public Func<ImmutableDictionary<string, IExpression>, bool>? Condition { get; init; }
        /// <summary>
        /// Optional predicate that can leverage solver assumptions to approve rule application.
        /// </summary>
        public Func<ImmutableDictionary<string, IExpression>, Assumptions, bool>? AssumptionCondition { get; init; }

        /// <summary>
        /// Optional transform function that computes the replacement expression dynamically.
        /// If this returns non-null, it overrides the fixed Replacement expression.
        /// </summary>
        public Func<ImmutableDictionary<string, IExpression>, IExpression?>? Transform { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Rule"/> class.
        /// </summary>
        /// <param name="pattern">The pattern expression.</param>
        /// <param name="replacement">The replacement expression.</param>
        /// <param name="condition">An optional condition function for applying the rule.</param>
        /// <param name="assumptionCondition">An optional assumption-aware condition function for applying the rule.</param>
        /// <param name="transform">An optional dynamic transform function.</param>
        public Rule(
            IExpression pattern,
            IExpression replacement,
            Func<ImmutableDictionary<string, IExpression>, bool>? condition = null,
            Func<ImmutableDictionary<string, IExpression>, Assumptions, bool>? assumptionCondition = null,
            Func<ImmutableDictionary<string, IExpression>, IExpression?>? transform = null)
        {
            if (Environment.GetEnvironmentVariable("SYM_DEBUG_RULES") == "1")
                Console.WriteLine($"DEBUG: Rule created with pattern={pattern.ToDisplayString()} (Type: {pattern.GetType().Name})");
            
            // Patterns should generally be canonical to ensure consistent matching,
            // UNLESS canonicalization changes the operator (head), in which case we keep
            // the original to match the specific structure intended.
            var canonicalPattern = pattern.Canonicalize();
            Pattern = (pattern.Head != canonicalPattern.Head) ? pattern : canonicalPattern;

            // Replacements are NOT canonicalized here. This preserves the rule author's
            // intent, especially for commutativity rules (a+b -> b+a) which would 
            // otherwise be neutralized into identities (a+b -> a+b).
            Replacement = replacement;

            Condition = condition;
            AssumptionCondition = assumptionCondition;
            Transform = transform;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not Rule other) return false;
            if (ReferenceEquals(this, other)) return true;
            
            return Pattern.InternalEquals(other.Pattern) && 
                   Replacement.InternalEquals(other.Replacement) &&
                   Equals(Condition, other.Condition) &&
                   Equals(AssumptionCondition, other.AssumptionCondition) &&
                   Equals(Transform, other.Transform);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Pattern.InternalGetHashCode(), Replacement.InternalGetHashCode(), Condition, AssumptionCondition, Transform);
        }

        /// <summary>
        /// Attempts to apply this rule to a given expression.
        /// </summary>
        /// <param name="expression">The expression to apply the rule to.</param>
        /// <returns>A new expression if the rule was applied successfully, otherwise the original expression.</returns>
        public IExpression Apply(IExpression expression, Assumptions? assumptions = null, CancellationToken ct = default)
        {
            MatchResult match = Rewriter.TryMatch(expression, Pattern);
            if (match.Success)
            {
                var hasAssumptions = assumptions is not null;
                if ((Condition is null || Condition(match.Bindings)) &&
                    (AssumptionCondition is null || (hasAssumptions && AssumptionCondition(match.Bindings, assumptions!))))
                {
                    if (Transform != null)
                    {
                        return Transform(match.Bindings) ?? expression;
                    }
                    return Rewriter.Substitute(Replacement, match.Bindings, ct);
                }
            }
            return expression;
        }
    }
}
