// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using Sym.Core.EGraph;
using SymCobra.Core;
using SymCobra.Runtime;

namespace SymCobra.Regions;

public static class CobraExtractionPlanner
{
    public static CobraExtractionPlan Build(EGraph graph)
    {
        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(graph);
        return Build(cobraGraphState);
    }

    public static CobraExtractionPlan Build(CobraGraphState graphState)
    {
        return Build(graphState, CobraPlannerSnapshot.Create(graphState));
    }

    public static CobraExtractionPlan Build(CobraGraphState graphState, CobraPlannerSnapshot snapshot)
    {
        var rootIds = snapshot.RootIds;
        if (rootIds.Length == 0)
        {
            return new CobraExtractionPlan([], new Dictionary<int, IReadOnlyList<int>>(), CobraExtractionSource.CpuHeuristic);
        }

        int[] classScores;
        CobraExtractionSource source;
        bool usedCudaClassScoring = false;
        int[] rootIdArray = rootIds;
        int[] rootNodeCounts = new int[rootIdArray.Length];
        int[] rootGenerations = new int[rootIdArray.Length];
        for (int i = 0; i < rootIdArray.Length; i++)
        {
            int classId = rootIdArray[i];
            rootNodeCounts[i] = snapshot.NodeCounts[classId];
            rootGenerations[i] = snapshot.Generations[classId];
        }

        if (CobraCudaNative.TryScoreExtractClassesById(rootIdArray, snapshot.NodeCounts, snapshot.Generations, out classScores) ||
            CobraCudaNative.TryScoreExtractClasses(
                rootNodeCounts,
                rootGenerations,
                out classScores))
        {
            source = CobraExtractionSource.Cuda;
            usedCudaClassScoring = true;
        }
        else
        {
            classScores = new int[rootIdArray.Length];
            for (int i = 0; i < rootIdArray.Length; i++)
            {
                int classId = rootIdArray[i];
                classScores[i] = (snapshot.NodeCounts[classId] * 32) + (snapshot.Generations[classId] * 4);
            }

            source = CobraExtractionSource.CpuHeuristic;
        }

        int[] orderedClassIndexes = CreateIndexes(rootIdArray.Length);
        Array.Sort(orderedClassIndexes, (left, right) =>
        {
            int scoreComparison = classScores[left].CompareTo(classScores[right]);
            return scoreComparison != 0 ? scoreComparison : rootIdArray[left].CompareTo(rootIdArray[right]);
        });
        var orderedClassIds = new int[rootIdArray.Length];
        for (int i = 0; i < orderedClassIndexes.Length; i++)
        {
            orderedClassIds[i] = rootIdArray[orderedClassIndexes[i]];
        }

        int[] allNodeScores = BuildNodeScores(graphState, snapshot, usedCudaClassScoring, ref source);
        var orderedNodesByClass = new Dictionary<int, IReadOnlyList<int>>(orderedClassIds.Length);
        foreach (int classId in orderedClassIds)
        {
            var nodeIds = graphState.GetClass(classId).NodeIds;
            if (nodeIds.Count == 0)
            {
                orderedNodesByClass[classId] = [];
                continue;
            }

            orderedNodesByClass[classId] = OrderNodeIds(nodeIds, graphState, allNodeScores);
        }

        return new CobraExtractionPlan(orderedClassIds, orderedNodesByClass, source);
    }

    private static int[] CreateIndexes(int length)
    {
        int[] indexes = new int[length];
        for (int i = 0; i < length; i++)
        {
            indexes[i] = i;
        }

        return indexes;
    }

    private static int[] BuildNodeScores(
        CobraGraphState graphState,
        CobraPlannerSnapshot snapshot,
        bool usedCudaClassScoring,
        ref CobraExtractionSource source)
    {
        if (graphState.NodeCount == 0)
        {
            return [];
        }

        var headCodes = new int[graphState.NodeCount];
        var arities = new int[graphState.NodeCount];
        var classIds = new int[graphState.NodeCount];
        for (int nodeId = 0; nodeId < graphState.NodeCount; nodeId++)
        {
            var node = graphState.GetNode(nodeId);
            headCodes[nodeId] = node.HeadCode;
            arities[nodeId] = node.Arity;
            classIds[nodeId] = graphState.Find(node.ClassId);
        }

        if (CobraCudaNative.TryScoreExtractNodesFullyCached(graphState.NodeCount, out var cudaScores) ||
            (usedCudaClassScoring &&
             CobraCudaNative.TryScoreExtractNodesWithCachedClassMetrics(
                 headCodes,
                 arities,
                 classIds,
                 snapshot.NodeCounts,
                 snapshot.Generations,
                 out cudaScores)) ||
            CobraCudaNative.TryScoreExtractNodes(headCodes, arities, classIds, snapshot.NodeCounts, snapshot.Generations, out cudaScores))
        {
            source = CobraExtractionSource.Cuda;
            return cudaScores;
        }

        var cpuScores = new int[graphState.NodeCount];
        for (int nodeId = 0; nodeId < graphState.NodeCount; nodeId++)
        {
            cpuScores[nodeId] = EstimateCpuNodeScore(graphState.GetNode(nodeId));
        }

        return cpuScores;
    }

    private static int[] OrderNodeIds(IReadOnlyList<int> nodeIds, CobraGraphState graphState, int[] scores)
    {
        int[] ordered = new int[nodeIds.Count];
        for (int i = 0; i < nodeIds.Count; i++)
        {
            ordered[i] = nodeIds[i];
        }

        Array.Sort(ordered, (left, right) =>
        {
            int scoreComparison = scores[left].CompareTo(scores[right]);
            if (scoreComparison != 0)
            {
                return scoreComparison;
            }

            var leftNode = graphState.GetNode(left);
            var rightNode = graphState.GetNode(right);
            int headComparison = StringComparer.Ordinal.Compare(leftNode.Head, rightNode.Head);
            if (headComparison != 0)
            {
                return headComparison;
            }

            return leftNode.Arity.CompareTo(rightNode.Arity);
        });

        return ordered;
    }

    private static int EstimateCpuNodeScore(CobraNodeRecord node)
    {
        int baseScore = node.Head switch
        {
            var head when head.StartsWith("Num:", StringComparison.Ordinal) => 1,
            var head when head.StartsWith("Sym:", StringComparison.Ordinal) => 10,
            "Transpose" => 60,
            "Relu" => 65,
            "MatMul" => 85,
            "FusedMatMulAdd" => 95,
            "FusedMatMulAddRelu" => 100,
            _ => 75
        };

        return baseScore + (node.Arity * 12);
    }
}
