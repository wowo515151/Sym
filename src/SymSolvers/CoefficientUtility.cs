using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

public static class CoefficientUtility
{
    public static bool TryGetCoefficient(IExpression expr, Symbol variable, int power, out IExpression coefficient)
    {
        coefficient = new Number(0m);
        var canonical = expr.Canonicalize();
        
        // If it's not already expanded, try to expand it first to make coefficient extraction easier.
        var expanded = RuleBasedExpansion.Expand(canonical, new SolveContext());

        if (expanded is Add add)
        {
            decimal totalNumeric = 0m;
            var symbolicTerms = new List<IExpression>();

            foreach (var term in add.Arguments)
            {
                if (TryExtractTermCoefficient(term, variable, power, out var coeff))
                {
                    if (coeff is Number n) totalNumeric += n.Value;
                    else symbolicTerms.Add(coeff);
                }
            }

            if (symbolicTerms.Count == 0)
            {
                coefficient = new Number(totalNumeric);
                return true;
            }

            if (totalNumeric != 0m) symbolicTerms.Add(new Number(totalNumeric));
            coefficient = symbolicTerms.Count == 1 ? symbolicTerms[0] : new Add(symbolicTerms.ToImmutableList()).Canonicalize();
            return true;
        }

        return TryExtractTermCoefficient(expanded, variable, power, out coefficient);
    }

    private static bool TryExtractTermCoefficient(IExpression term, Symbol variable, int power, out IExpression coefficient)
    {
        coefficient = new Number(0m);
        
        if (power == 0)
        {
            if (!term.ContainsSymbol(variable))
            {
                coefficient = term;
                return true;
            }
            return false;
        }

        if (term is Symbol s && s.InternalEquals(variable))
        {
            if (power == 1)
            {
                coefficient = new Number(1m);
                return true;
            }
            return false;
        }

        if (term is Power p && p.Base is Symbol baseSym && baseSym.InternalEquals(variable) && p.Exponent is Number expNum)
        {
            if ((int)expNum.Value == power)
            {
                coefficient = new Number(1m);
                return true;
            }
            return false;
        }

        if (term is Multiply mul)
        {
            int foundPower = 0;
            var rest = new List<IExpression>();
            foreach (var arg in mul.Arguments)
            {
                if (arg is Symbol s2 && s2.InternalEquals(variable))
                {
                    foundPower += 1;
                }
                else if (arg is Power p2 && p2.Base is Symbol baseSym2 && baseSym2.InternalEquals(variable) && p2.Exponent is Number expNum2)
                {
                    foundPower += (int)expNum2.Value;
                }
                else
                {
                    rest.Add(arg);
                }
            }

            if (foundPower == power)
            {
                coefficient = rest.Count == 0 ? new Number(1m) : (rest.Count == 1 ? rest[0] : new Multiply(rest.ToImmutableList()).Canonicalize());
                return true;
            }
            return false;
        }

        return false;
    }
}
