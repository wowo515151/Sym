// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Linq;
using Sym.Core.EGraph;
using Sym.Core;
using Sym.Atoms;

namespace SymSolvers.EGraphSolver
{
    public class TensorCostModel : ICostModel
    {
        public Func<ENode, long> GetCostFunction(SolveContext context, Sym.Core.EGraph.EGraph graph)
        {
            var isStaticClass = new HashSet<int>();
            
            var userStaticSymbols = new HashSet<string>(StringComparer.Ordinal);
            string staticList = context.GetString("StaticSymbols", "");
            if (!string.IsNullOrEmpty(staticList))
            {
                foreach (var s in staticList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    userStaticSymbols.Add(s);
                }
            }

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var id in graph.GetRootIds())
                {
                    if (isStaticClass.Contains(id)) continue;
                    
                    var eClass = graph.GetClass(id);
                    bool isStatic = false;
                    foreach (var node in eClass.Nodes)
                    {
                        if (node.Head.StartsWith("Num:", StringComparison.Ordinal)) { isStatic = true; break; }
                        if (node.Head.StartsWith("Sym:", StringComparison.Ordinal))
                        {
                            string name = node.Head.Substring(4);
                            if (userStaticSymbols.Contains(name) ||
                                name.StartsWith("W", StringComparison.Ordinal) || 
                                name.StartsWith("g", StringComparison.Ordinal) || 
                                name.Contains("weight", StringComparison.OrdinalIgnoreCase) || 
                                name.Contains("bias", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("gamma", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("beta", StringComparison.OrdinalIgnoreCase))
                            {
                                isStatic = true;
                                break;
                            }
                        }
                        if (node.Children.Count > 0 && node.Children.All(c => isStaticClass.Contains(graph.Find(c))))
                        {
                            isStatic = true;
                            break;
                        }
                    }

                    if (isStatic)
                    {
                        isStaticClass.Add(id);
                        changed = true;
                    }
                }
            }

            return node =>
            {
                int classId = graph.AddNode(node); 
                var eClass = graph.GetClass(classId);
                
                // If all inputs are static, this op can be constant-folded/precomputed.
                if (node.Children.Count > 0 && node.Children.All(c => isStaticClass.Contains(graph.Find(c))))
                {
                    return 1; // Minimal cost for precomputable ops
                }

                var shape = eClass.Data as Shape;
                
                long cost = 10; // Base op cost

                if (shape != null && !shape.IsWildcardShape && !shape.IsScalar)
                {
                    if (!shape.IsValid) return 1_000_000_000L; // Penalize Error shapes heavily

                    long numel = 1;
                    foreach(var d in shape.Dimensions) numel *= d;
                    
                    const int FlopWeight = 1;
                    const int MemWeight = 2;

                    if (node.Head == "MatMul")
                    {
                        // Estimate FLOPs: 2*M*N*K
                        if (node.Children.Count >= 1)
                        {
                            var leftClass = graph.GetClass(node.Children[0]);
                            if (leftClass.Data is Shape leftShape && leftShape.IsValid)
                            {
                                long m, k;
                                if (leftShape.IsMatrix) { m = leftShape.Dimensions[0]; k = leftShape.Dimensions[1]; }
                                else if (leftShape.IsVector) { m = 1; k = leftShape.Dimensions[0]; }
                                else { m = 1; k = 1; }

                                long n = numel / (m == 0 ? 1 : m);
                                cost += FlopWeight * (2 * m * n * k);
                            }
                            else
                            {
                                long dim = (long)Math.Sqrt(numel);
                                cost += FlopWeight * (2 * numel * dim);
                            }
                        }
                        cost += MemWeight * numel * 3;
                    }
                    else if (node.Head == "inverse")
                    {
                        long n = (long)Math.Sqrt(numel);
                        cost += FlopWeight * (n * n * n);
                        cost += MemWeight * numel * 2;
                    }
                    else if (node.Head == "FusedMatMulAdd" || node.Head == "FusedMatMulAddRelu")
                    {
                        if (node.Children.Count >= 1)
                        {
                            var leftClass = graph.GetClass(node.Children[0]);
                            if (leftClass.Data is Shape leftShape && leftShape.IsMatrix)
                            {
                                long m = leftShape.Dimensions[0];
                                long k = leftShape.Dimensions[1];
                                long n = numel / (m == 0 ? 1 : m);
                                cost += FlopWeight * (2 * m * n * k + numel);
                            }
                            else
                            {
                                long dim = (long)Math.Sqrt(numel);
                                cost += FlopWeight * (2 * numel * dim);
                            }
                        }
                        cost += MemWeight * numel * 4; 
                    }
                    else if (node.Head == "Conv2D")
                    {
                        cost += FlopWeight * numel * 9 * 64;
                        cost += MemWeight * numel * 2;
                    }
                    else if (node.Head == "FusedConv2DRelu")
                    {
                        cost += FlopWeight * numel * 9 * 64;
                        cost += MemWeight * numel * 2;
                    }
                    else if (node.Head == "Kronecker")
                    {
                        cost += FlopWeight * numel;
                        cost += MemWeight * numel;
                    }
                    else if (node.Head == "vec")
                    {
                        cost += MemWeight * numel;
                    }
                    else if (node.Head == "Softmax")
                    {
                        cost += FlopWeight * numel * 5;
                        cost += MemWeight * numel * 2;
                    }
                    else if (node.Head == "RMSNorm")
                    {
                        cost += FlopWeight * numel * 4;
                        cost += MemWeight * numel * 2;
                    }
                    else
                    {
                        // Elementwise (TensorAdd, TensorMul, Relu, etc)
                        cost += FlopWeight * numel;
                        cost += MemWeight * numel * 2; 
                    }
                }
                else
                {
                    // Fallback structural costs if shape is unknown or scalar
                    if (node.Head == "FusedMatMulAddRelu") return 28;
                    if (node.Head == "FusedMatMulAdd") return 25;
                    if (node.Head == "FusedConv2DRelu") return 50; 
                    if (node.Head == "MatMul") return 20;
                    if (node.Head == "inverse") return 25;
                    if (node.Head == "Conv2D") return 60;
                    if (node.Head == "Kronecker") return 100;
                    if (node.Head == "vec") return 5;
                    if (node.Head == "TensorAdd") return 10;
                    if (node.Head == "TensorMul") return 10;
                    if (node.Head == "Relu") return 5;
                    if (node.Head == "Transpose") return 5;
                    return 1;
                }

                return cost;
            };
        }
    }
}
