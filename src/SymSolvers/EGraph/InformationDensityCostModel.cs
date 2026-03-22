using System;
using System.Collections.Generic;
using System.Linq;
using Sym.Core.EGraph;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;

namespace SymSolvers.EGraphSolver
{
    public class InformationDensityCostModel : ICostModel
    {
        public Func<ENode, long> GetCostFunction(SolveContext context, Sym.Core.EGraph.EGraph graph)
        {
            var costExprStr = context.GetString("Cost", string.Empty);
            IExpression? costExpr = null;
            if (!string.IsNullOrEmpty(costExprStr))
            {
                try { costExpr = Sym.CSharpIO.CSharpIO.ParseExpressionsStrict(costExprStr).FirstOrDefault(); } catch { }
            }

            // Default Information Density: (Rel * Cert) / Tokens
            // We want to MINIMIZE cost, so we maximize Density by minimizing 1/Density? 
            // Let's use: Cost = 1000 - (Rel * Cert * 100) + Tokens
            if (costExpr == null)
            {
                costExpr = Sym.CSharpIO.CSharpIO.ParseExpressionsStrict("(100 - (Rel * Cert * 100)) + Tokens").First();
            }

            return node =>
            {
                int classId = graph.AddNode(node);
                var eClass = graph.GetClass(classId);
                
                var assignments = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                
                // 1. Try node-specific metadata (if node is a symbol)
                bool hasNodeMetadata = false;
                if (node.Head.StartsWith("Sym:"))
                {
                    string symName = node.Head.Substring(4);
                    if (context.AdditionalData != null && context.AdditionalData.TryGetValue("Attributes", out var attrData) && 
                        attrData is Dictionary<string, Dictionary<string, double>> symbolAttributes &&
                        symbolAttributes.TryGetValue(symName, out var attrs))
                    {
                        foreach (var kvp in attrs) assignments[kvp.Key] = (decimal)kvp.Value;
                        hasNodeMetadata = true;
                    }
                }
                
                // 2. Fallback to class-level metadata (only for non-symbols or as a weak fallback)
                if (!hasNodeMetadata)
                {
                    foreach (var kvp in eClass.Metadata) assignments[kvp.Key] = (decimal)kvp.Value;
                }
                
                // Fallbacks for missing metadata
                if (!assignments.ContainsKey("Rel")) assignments["Rel"] = 0.1m; 
                if (!assignments.ContainsKey("Cert")) assignments["Cert"] = 0.1m; 
                if (!assignments.ContainsKey("Tokens")) assignments["Tokens"] = (decimal)node.ToString().Length;

                try
                {
                    if (NumericEvaluator.TryEvaluate(costExpr!, assignments, out var result, out var error))
                    {
                        long cost = (long)result;
                        
                        // Mandatory Atomic preference: Numbers must be cheap
                        if (node.Head.StartsWith("Num:")) return 1;
                        
                        // Penalize non-fact symbols to encourage expansion to facts
                        if (!hasNodeMetadata && node.Head.StartsWith("Sym:")) cost += 10000;
                        
                        // Penalize Graph complexity (Depth/Edges)
                        if (node.Head == "Edge") cost += 10;
                        if (node.Head == "Graph") cost += node.Children.Count * 5;
                        
                        if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                            Console.WriteLine($"DEBUG: Cost for {node}: {cost} (hasNodeMetadata={hasNodeMetadata})");
                            
                        return cost;
                    }
                    else if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                    {
                        Console.WriteLine($"DEBUG: NumericEvaluator failed for cost expression: {error}");
                    }
                }
                catch (Exception ex)
                {
                    if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                        Console.WriteLine($"DEBUG: Exception in cost function: {ex.Message}");
                }

                return 100L; // Default
            };
        }
    }
}
