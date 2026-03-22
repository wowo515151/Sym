// Copyright Warren Harding 2026
using System;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using Sym.Calculus;

namespace SymSolvers;

public record struct ProblemFeatures(
    int Complexity, 
    bool HasTrig, 
    bool HasCalculus, 
    bool HasInequality,
    bool IsSystem
);

public static class ProblemFeatureExtractor
{
    public static ProblemFeatures Analyze(IExpression expr)
    {
        var features = new ProblemFeatures();
        Visit(expr, ref features);
        return features;
    }

    private static void Visit(IExpression expr, ref ProblemFeatures f)
    {
        f.Complexity++;
        
        if (expr is Vector v && v.Arguments.Any(a => a is Equality))
        {
             f.IsSystem = true;
        }

        if (expr is Function fn)
        {
            var name = fn.Name.ToLowerInvariant();
            if (name == "sin" || name == "cos" || name == "tan" || name == "sec" || name == "csc" || name == "cot") f.HasTrig = true;
            if (name == "lt" || name == "gt" || name == "le" || name == "ge" || name == "interval") f.HasInequality = true;
        }

        if (expr is Derivative || expr is Integral || expr is DefiniteIntegral)
        {
            f.HasCalculus = true;
        }
        
        if (expr is Operation op)
        {
            foreach(var arg in op.Arguments) Visit(arg, ref f);
        }
    }
}
