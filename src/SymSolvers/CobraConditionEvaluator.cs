// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using Sym.Operations;
using SymCobra.Core;
using SymCobra.Regions;

namespace SymSolvers;

public static class CobraConditionEvaluator
{
    public static bool TryEvaluate(
        CobraGraphState graphState,
        int classId,
        IReadOnlyDictionary<string, decimal> assignments,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>? symbolAttributes,
        CancellationToken ct,
        CobraDiagnostics diagnostics,
        out bool result)
    {
        result = false;
        var eClass = graphState.GetClass(classId);
        
        foreach (int nodeId in eClass.NodeIds)
        {
            var node = graphState.GetNode(nodeId);
            string? head = DecodeHeadCode(node.HeadCode);
            
            if (head != null && IsConditionHead(head))
            {
                var childrenExprs = new List<IExpression>();
                bool allExtractable = true;
                foreach (int cid in node.CanonicalChildIds)
                {
                    try 
                    { 
                        childrenExprs.Add(CobraExtractor.ExtractBest(graphState, cid, ct, diagnostics)); 
                    }
                    catch { allExtractable = false; break; }
                }
                
                if (!allExtractable) continue;

                var conditionExpr = ENode.CreateExpression(head, childrenExprs.ToImmutableList());
                if (NumericEvaluator.TryEvaluateCondition(conditionExpr, assignments, out result, out _, symbolAttributes: symbolAttributes))
                {
                    return true;
                }
            }
        }
        
        return false;
    }

    private static string? DecodeHeadCode(int headCode)
    {
        return headCode switch
        {
            1 => "Add",
            2 => "Mul",
            3 => "MatMul",
            4 => "Transpose",
            5 => "Relu",
            6 => "Equality",
            7 => "Vector",
            10 => "gt",
            11 => "lt",
            12 => "ge",
            13 => "le",
            14 => "and",
            15 => "or",
            16 => "not",
            17 => "eq",
            18 => "ne",
            _ => null
        };
    }

    private static bool IsConditionHead(string head)
    {
        return head is "gt" or "lt" or "ge" or "le" or "and" or "or" or "not" or "eq" or "ne";
    }
}
