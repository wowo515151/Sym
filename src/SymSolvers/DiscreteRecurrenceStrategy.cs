// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

/// <summary>
/// Solves linear homogeneous recurrence relations with constant coefficients.
/// Example: a(n) = 2*a(n-1) - 2*a(n-2) + a(n-3)
/// </summary>
public class DiscreteRecurrenceStrategy : ISolverStrategy, INamedSolverStrategy
{
    public string Name => "DiscreteRecurrenceStrategy";
    private readonly RulePackStrategy _rulePackStrategy;

    public DiscreteRecurrenceStrategy()
    {
        var packs = SymRules.RulePackLibrary.GetRulePacks();
        var pack = packs.FirstOrDefault(p => p.Name == "RecurrenceStrategy");
        if (pack != null) _rulePackStrategy = new RulePackStrategy(pack);
        else _rulePackStrategy = null!;
    }

    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (context is null) return SolveResult.Failure(problem, "SolveContext cannot be null.");
        if (problem is null) return SolveResult.Failure(null, "Problem expression cannot be null.");
        var currentProblem = problem;

        var relations = new List<Equality>();
        var conditions = new List<Equality>();
        var targets = new List<Function>();

        foreach (var eq in EnumerateEqualities(currentProblem))
        {
            if (IsRecurrenceRelation(eq, out var fnName, out var paramName))
            {
                relations.Add(eq);
            }
            else if (IsInitialCondition(eq, out _, out _))
            {
                conditions.Add(eq);
            }
        }

        if (relations.Count != 1) return SolveResult.Failure(problem, "Only single recurrence relation supported.");
        
        // Find target: a(K)
        var targetExpr = context.TargetVariable;
        if (targets.Count == 0 && targetExpr == null)
        {
            if (currentProblem is Vector v)
            {
                foreach (var arg in v.Arguments)
                {
                    if (arg is Function f && relations.Count > 0 && relations[0].LeftOperand is Function lf && f.Name == lf.Name)
                    {
                        targets.Add(f);
                    }
                }
                
                // Fallback: look for any function matching the name, even if relations check failed/weird
                if (targets.Count == 0 && relations.Count > 0 && relations[0].LeftOperand is Function lf2)
                {
                    foreach (var arg in v.Arguments)
                    {
                        if (arg is Function f && f.Name == lf2.Name)
                        {
                            targets.Add(f);
                        }
                    }
                }
            }
        }

        if (targets.Count == 0 && targetExpr == null) return SolveResult.Failure(currentProblem, "No target term found.");

        var relation = relations[0];
        if (!ExtractCoefficients(relation, out var name, out var coeffs, out var order, out var failureDetail))
        {
            return SolveResult.Failure(currentProblem, $"Could not extract constant coefficients. {failureDetail}");
        }

        var charPolyCoeffs = new Rational[order + 1];
        charPolyCoeffs[order] = Rational.One; 
        
        for (int i = 1; i <= order; i++)
        {
            if (coeffs.TryGetValue(i, out var c))
            {
                charPolyCoeffs[order - i] = Rational.Zero - c;
            }
            else
            {
                charPolyCoeffs[order - i] = Rational.Zero;
            }
        }

        var rSym = new Symbol("r");
        IExpression polyExpr = new Number(0);
        for (int i = 0; i <= order; i++)
        {
            context.ThrowIfCancellationRequested();
            if (charPolyCoeffs[i].IsZero) continue;
            var term = charPolyCoeffs[i].ToExpression();
            if (i > 0)
            {
                term = new Multiply(term, new Power(rSym, new Number(i)).Canonicalize()).Canonicalize();
            }
            polyExpr = new Add(polyExpr, term).Canonicalize();
        }

        if (!Polynomial.TryCreate(polyExpr, rSym, out var charPoly))
        {
             return SolveResult.Failure(currentProblem, "Failed to create characteristic polynomial.");
        }

        var roots = new List<IExpression>();
        var factorization = charPoly.FactorLinear(context.CancellationToken);
        
        foreach (var root in factorization.LinearRoots)
        {
            roots.Add(root.ToExpression());
        }

        if (factorization.Residual.Degree == 2)
        {
            var aVal = factorization.Residual.Coefficients[2];
            var bVal = factorization.Residual.Coefficients[1];
            var cVal = factorization.Residual.Coefficients[0];
            
            var disc = bVal * bVal - Rational.FromInteger(4) * aVal * cVal;
            
            var discExpr = disc.ToExpression();
            var sqrtDisc = new Power(discExpr, new Number(0.5m)).Canonicalize();
            
            var negB = (Rational.Zero - bVal).ToExpression();
            var twoA = (Rational.FromInteger(2) * aVal).ToExpression();
            
            var r1 = new Divide(new Add(negB, sqrtDisc).Canonicalize(), twoA).Canonicalize();
            var r2 = new Divide(new Subtract(negB, sqrtDisc).Canonicalize(), twoA).Canonicalize();
            
            roots.Add(r1);
            roots.Add(r2);
        }
        else if (factorization.Residual.Degree > 0)
        {
             return SolveResult.Failure(currentProblem, "Cannot solve higher order characteristic polynomial.");
        }

        var distinctRoots = roots.Select(r => r.ToDisplayString()).Distinct(StringComparer.Ordinal).ToList();
        var groupedRoots = roots.GroupBy(r => r.ToDisplayString()).Select(g => new { Root = g.First(), Count = g.Count() }).ToList();

        if (conditions.Count < order)
        {
             return SolveResult.Failure(currentProblem, $"Not enough initial conditions. Need {order}, found {conditions.Count}.");
        }

        var nSym = new Symbol("n");
        var generalSolutionTerms = new List<IExpression>();
        var unknowns = new List<Symbol>();
        
        int uIdx = 0;
        foreach (var grp in groupedRoots)
        {
            for (int j = 0; j < grp.Count; j++)
            {
                var kSym = new Symbol($"k_{uIdx++}");
                unknowns.Add(kSym);
                
                IExpression term = kSym;
                if (j > 0) term = new Multiply(term, new Power(nSym, new Number(j)).Canonicalize()).Canonicalize();
                term = new Multiply(term, new Power(grp.Root, nSym).Canonicalize()).Canonicalize();
                
                generalSolutionTerms.Add(term);
            }
        }
        
        var generalSolution = new Add(generalSolutionTerms.ToImmutableList()).Canonicalize();
        
        var equations = new List<IExpression>();
        var rhss = new List<IExpression>();
        
        var sortedConditions = conditions.OrderBy(c => 
        {
            if (c.LeftOperand is Function f && f.Arguments[0] is Number n) return (int)n.Value;
            return 0;
        }).Take(order).ToList();

        foreach (var cond in sortedConditions)
        {
            if (cond.LeftOperand is Function f && f.Arguments[0] is Number nVal)
            {
                var n = (int)nVal.Value;
                var val = cond.RightOperand;
                rhss.Add(val);
                
                var eqExpr = Substitute(generalSolution, nSym, new Number(n)).Canonicalize();
                equations.Add(eqExpr);
            }
        }

        // Build Matrix M
        var matrixRows = equations.Count;
        var matrixCols = unknowns.Count;
        
        if (matrixRows != matrixCols) return SolveResult.Failure(currentProblem, "System for coefficients is not square.");
        
        var matrixElems = new List<IExpression>();
        
        for (int r = 0; r < matrixRows; r++)
        {
            var expr = equations[r];
            for (int c = 0; c < matrixCols; c++)
            {
                context.ThrowIfCancellationRequested();
                var subMap = new Dictionary<string, IExpression>();
                foreach (var u in unknowns) subMap[u.Name] = new Number(0);
                subMap[unknowns[c].Name] = new Number(1);
                
                var coeff = SubstitutionStrategy.SubstituteInternal(expr, subMap).Canonicalize();
                
                matrixElems.Add(coeff);
            }
        }
        
        var matM = new Matrix(System.Collections.Immutable.ImmutableArray.Create(matrixRows, matrixCols), matrixElems.ToImmutableList());
        
        // Solve M * k = rhss
        // Use Cramer's rule for symbolic robustness
        
        var assignments = new Dictionary<string, IExpression>();
        
        // Calculate Determinant D
        var linAlg = new LinearAlgebraStrategy();
        var dExpr = new Function("determinant", System.Collections.Immutable.ImmutableList.Create<IExpression>(matM));
        var dRes = linAlg.Solve(dExpr, context);
        IExpression D = dRes.IsSuccess && dRes.ResultExpression != null ? dRes.ResultExpression : dExpr;
        
        // Check for D=0?
        // Symbolic D might not be simplifiable to 0 easily. 
        // We'll proceed and let simplification handle division.
        
        for (int i = 0; i < matrixCols; i++)
        {
            context.ThrowIfCancellationRequested();
            var mI_elems = new List<IExpression>(matrixElems);
            // Replace column i with rhss
            for (int r = 0; r < matrixRows; r++)
            {
                mI_elems[r * matrixCols + i] = rhss[r];
            }
            
            var matMi = new Matrix(System.Collections.Immutable.ImmutableArray.Create(matrixRows, matrixCols), mI_elems.ToImmutableList());
            var diExpr = new Function("determinant", System.Collections.Immutable.ImmutableList.Create<IExpression>(matMi));
            var diRes = linAlg.Solve(diExpr, context);
            IExpression Di = diRes.IsSuccess && diRes.ResultExpression != null ? diRes.ResultExpression : diExpr;
            
            var ki = new Divide(Di, D).Canonicalize();
            assignments[unknowns[i].Name] = ki;
        }

        var specificSolution = SubstitutionStrategy.SubstituteInternal(generalSolution, assignments);
        
        // Final simplification pipeline
        var simp = new EGraphSolverStrategy();
        var simpRes = simp.Solve(specificSolution, context);
        if (simpRes.IsSuccess && simpRes.ResultExpression != null) specificSolution = simpRes.ResultExpression;
        
        // If result is still complex (contains Pow(..., 0.5)), try numeric approximation
        if (specificSolution.ToDisplayString().Length > 50) 
        {
             var approx = new NumericApproximationStrategy();
             var approxRes = approx.Solve(specificSolution, context);
             if (approxRes.IsSuccess && approxRes.ResultExpression is Number numRes)
             {
                 // Check if it's close to integer
                 if (Math.Abs(numRes.Value - Math.Round(numRes.Value)) < 1e-9m)
                 {
                     specificSolution = new Number(Math.Round(numRes.Value));
                 }
                 else
                 {
                     specificSolution = numRes;
                 }
             }
        }
        
        if (targets.Count > 0)
        {
            var tFn = targets[0];
            if (tFn.Arguments[0] is Number tN)
            {
                var finalVal = Substitute(specificSolution, nSym, tN).Canonicalize();
                
                var approx = new NumericApproximationStrategy();
                var approxRes = approx.Solve(finalVal, context);
                if (approxRes.IsSuccess && approxRes.ResultExpression is Number numRes)
                {
                    if (Math.Abs(numRes.Value - Math.Round(numRes.Value)) < 1e-9m)
                    {
                        finalVal = new Number(Math.Round(numRes.Value));
                    }
                    else
                    {
                        finalVal = numRes;
                    }
                }
                
                return SolveResult.Success(new Equality(tFn, finalVal).Canonicalize(), "Solved linear recurrence.");
            }
            else
            {
                return SolveResult.Success(new Equality(tFn, specificSolution).Canonicalize(), "Solved general recurrence.");
            }
        }
        
        return SolveResult.Success(specificSolution, "Solved general recurrence.");
    }

    private IEnumerable<Equality> EnumerateEqualities(IExpression expr)
    {
        if (expr is Equality e) yield return e;
        if (expr is Vector v) 
        {
            foreach(var arg in v.Arguments) 
            {
                foreach(var eq in EnumerateEqualities(arg)) yield return eq;
            }
        }
        if (expr is Add a) 
        {
            foreach(var arg in a.Arguments) 
            {
                foreach(var eq in EnumerateEqualities(arg)) yield return eq;
            }
        }
    }

    private bool IsRecurrenceRelation(Equality eq, out string fnName, out string paramName)
    {
        fnName = null!; paramName = null!;
        if (eq.LeftOperand is Function f && f.Arguments.Count == 1 && f.Arguments[0] is Symbol s)
        {
            fnName = f.Name;
            paramName = s.Name;
            return ContainsRecurrenceCall(eq.RightOperand, fnName);
        }
        return false;
    }

    private bool IsInitialCondition(Equality eq, out string fnName, out int n)
    {
        fnName = null!; n = 0;
        if (eq.LeftOperand is Function f && f.Arguments.Count == 1 && f.Arguments[0] is Number num)
        {
            fnName = f.Name;
            n = (int)num.Value;
            return true;
        }
        return false;
    }

    private bool ContainsRecurrenceCall(IExpression expr, string fnName)
    {
        if (expr is Function f && f.Name == fnName) return true;
        if (expr is Operation op) return op.Arguments.Any(a => ContainsRecurrenceCall(a, fnName));
        return false;
    }

    private bool ExtractCoefficients(Equality eq, out string fnName, out Dictionary<int, Rational> coeffs, out int order, out string failureDetail)
    {
        order = 0;
        failureDetail = "";
        fnName = ((Function)eq.LeftOperand).Name;
        var param = ((Symbol)((Function)eq.LeftOperand).Arguments[0]).Name; 
        
        var zeroEq = new Subtract(eq.LeftOperand, eq.RightOperand).Canonicalize();
        
        // Expand to ensure we have a flat sum of terms.
        // Use rule-based expansion to keep behavior consistent with the rest of the rule pipeline.
        zeroEq = RuleBasedExpansion.Expand(zeroEq, new SolveContext());

        IEnumerable<IExpression> terms = zeroEq is Add add ? add.Arguments : new[] { zeroEq };
        
        coeffs = new Dictionary<int, Rational>();
        int maxN = int.MinValue;
        var termOffsets = new List<(int offset, Rational coeff)>();

        foreach (var term in terms)
        {
            if (TryGetFunctionTerm(term, fnName, param, out var offset, out var coeff))
            {
                if (offset > maxN) maxN = offset;
                termOffsets.Add((offset, coeff));
            }
            else
            {
                if (term is Number n && n.Value == 0) continue;
                failureDetail = $"Failed term: {term.ToDisplayString()}";
                return false; 
            }
        }
        
        Rational leadCoeff = Rational.Zero;
        
        foreach (var t in termOffsets)
        {
            int diff = maxN - t.offset;
            if (diff == 0) leadCoeff += t.coeff;
        }
        
        if (leadCoeff.IsZero) 
        {
            failureDetail = "Leading coefficient is zero.";
            return false; 
        }
        
        foreach (var t in termOffsets)
        {
            int diff = maxN - t.offset;
            if (diff > order) order = diff;
            
            var c = t.coeff / leadCoeff;
            
            if (diff > 0)
            {
                if (!coeffs.ContainsKey(diff)) coeffs[diff] = Rational.Zero;
                coeffs[diff] = coeffs[diff] - c;
            }
        }
        
        return true;
    }

    private bool TryGetFunctionTerm(IExpression term, string fnName, string paramName, out int offset, out Rational coeff)
    {
        offset = 0; coeff = Rational.One;
        
        IExpression core = term;
        if (term is Multiply mul)
        {
            var nums = mul.Arguments.OfType<Number>().ToList();
            if (nums.Count > 0)
            {
                decimal val = 1;
                foreach (var n in nums) val *= n.Value;
                coeff = Rational.FromDecimal(val);
            }
            
            var fn = mul.Arguments.OfType<Function>().FirstOrDefault(f => f.Name == fnName);
            if (fn == null) return false;
            core = fn;
        }
        else if (term is Function f && f.Name == fnName)
        {
            core = f;
        }
        else
        {
            return false;
        }
        
        if (core is Function func)
        {
            var arg = func.Arguments[0];
            if (arg is Symbol s && s.Name == paramName)
            {
                offset = 0;
                return true;
            }
            if (arg is Add add)
            {
                var sym = add.Arguments.OfType<Symbol>().FirstOrDefault(x => x.Name == paramName);
                var num = add.Arguments.OfType<Number>().FirstOrDefault();
                if (sym != null && num != null)
                {
                    offset = (int)num.Value;
                    return true;
                }
            }
            if (arg is Subtract sub)
            {
                if (sub.LeftOperand is Symbol sl && sl.Name == paramName && sub.RightOperand is Number nr)
                {
                    offset = -(int)nr.Value;
                    return true;
                }
            }
        }
        
        return false;
    }

    private IExpression Substitute(IExpression expr, Symbol target, IExpression replacement)
    {
        if (expr is Symbol s && s.Name == target.Name) return replacement;
        if (expr is Operation op)
        {
            return op.WithArguments(op.Arguments.Select(a => Substitute(a, target, replacement)).ToImmutableList()).Canonicalize();
        }
        return expr;
    }
}
