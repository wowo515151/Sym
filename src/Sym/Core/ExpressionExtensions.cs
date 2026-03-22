// Copyright Warren Harding 2026
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using System.Collections.Immutable;
using System.Linq;

namespace Sym.Core
{
    /// <summary>
    /// Provides extension methods for IExpression, useful for analysis operations.
    /// </summary>
    public static class ExpressionExtensions
    {
        /// <summary>
        /// Determines if the expression or any of its sub-expressions contain the specified symbol.
        /// </summary>
        /// <param name="expression">The expression to search within.</param>
        /// <param name="targetSymbol">The symbol to search for.</param>
        /// <returns>True if the symbol is found, false otherwise.</returns>
        public static bool ContainsSymbol(this IExpression expression, Symbol targetSymbol)
        {
            return expression.ContainsSymbol(s => s.InternalEquals(targetSymbol));
        }

        public static bool ContainsSymbol(this IExpression expression, System.Func<Symbol, bool> predicate)
        {
            return expression.ContainsSymbol(predicate, out _);
        }

        public static bool ContainsSymbol(this IExpression expression, System.Func<Symbol, bool> predicate, out Symbol? found)
        {
            found = null;
            if (expression is Symbol s && predicate(s))
            {
                found = s;
                return true;
            }

            if (expression is Operation operation)
            {
                foreach (IExpression arg in operation.Arguments)
                {
                    if (arg.ContainsSymbol(predicate, out found))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
