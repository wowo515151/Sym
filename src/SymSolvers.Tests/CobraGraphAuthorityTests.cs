// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using Sym.Operations;
using SymCobra.Core;
using SymCobra.Regions;

namespace SymSolvers.Tests;

[TestClass]
public class CobraGraphAuthorityTests
{
    [TestMethod]
    public void CobraGraphState_Union_MergesClassContentsForAuthoritativeReads()
    {
        var graphState = new CobraGraphState();
        int xClassId = graphState.AddExpression(new Symbol("x"));
        int yClassId = graphState.AddExpression(new Symbol("y"));

        graphState.Union(xClassId, yClassId);

        var mergedClass = graphState.GetClass(xClassId);
        var literals = mergedClass.NodeIds
            .Select(graphState.GetNode)
            .Select(node => node.Literal ?? node.Head)
            .ToHashSet(System.StringComparer.Ordinal);

        Assert.AreEqual(graphState.Find(xClassId), graphState.Find(yClassId));
        Assert.AreEqual(2, mergedClass.NodeIds.Count);
        Assert.IsTrue(literals.Contains("Sym:x"), "Expected the merged class to retain the original x node.");
        Assert.IsTrue(literals.Contains("Sym:y"), "Expected the merged class to retain the absorbed y node.");
    }

    [TestMethod]
    public void CobraGraphState_SyncLegacyGraphFromCobra_IsIncrementalAndIdempotent()
    {
        var legacyGraph = new EGraph();
        int legacyX = legacyGraph.Add(new Symbol("x"));

        var graphState = new CobraGraphState();
        graphState.SyncFromLegacyGraph(legacyGraph);

        int yClassId = graphState.AddExpression(new Symbol("y"));
        int sumClassId = graphState.AddExpression(new Add(new Symbol("x"), new Symbol("y")).Canonicalize());
        graphState.Union(legacyX, yClassId);

        graphState.SyncLegacyGraphFromCobra(legacyGraph);

        Assert.AreEqual(3, legacyGraph.NodeCount, "Expected only the new y and add nodes to be materialized.");

        int legacyY = legacyGraph.Find(legacyGraph.Add(new Symbol("y")));
        int legacySum = legacyGraph.Find(legacyGraph.Add(new Add(new Symbol("x"), new Symbol("y")).Canonicalize()));

        Assert.AreEqual(legacyGraph.Find(legacyX), legacyY, "Expected COBRA-side union authority to sync back to the legacy graph.");
        Assert.AreEqual(legacyGraph.Find(sumClassId), legacySum, "Expected the synced legacy graph to recognize the new add expression.");

        int nodeCountAfterFirstSync = legacyGraph.NodeCount;
        graphState.SyncLegacyGraphFromCobra(legacyGraph);

        Assert.AreEqual(nodeCountAfterFirstSync, legacyGraph.NodeCount, "Incremental sync should not rematerialize already-synced nodes.");
    }

    [TestMethod]
    public void CobraPlannerSnapshot_FromCobraGraphState_MatchesLegacyGraphSnapshot()
    {
        var legacyGraph = new EGraph();
        int x = legacyGraph.Add(new Symbol("x"));
        int y = legacyGraph.Add(new Symbol("y"));
        legacyGraph.Add(new Add(new Symbol("x"), new Number(5)).Canonicalize());
        legacyGraph.Union(x, y);
        legacyGraph.Rebuild();

        var graphState = new CobraGraphState();
        graphState.SyncFromLegacyGraph(legacyGraph);

        var legacySnapshot = CobraPlannerSnapshot.Create(legacyGraph);
        var cobraSnapshot = CobraPlannerSnapshot.Create(graphState);

        CollectionAssert.AreEqual(legacySnapshot.RootIds, cobraSnapshot.RootIds);
        CollectionAssert.AreEqual(legacySnapshot.NodeCounts, cobraSnapshot.NodeCounts);
        CollectionAssert.AreEqual(legacySnapshot.Generations, cobraSnapshot.Generations);
        CollectionAssert.AreEqual(legacySnapshot.ClassConstraintMasks, cobraSnapshot.ClassConstraintMasks);
        CollectionAssert.AreEqual(legacySnapshot.ClassHeadBucketMasks, cobraSnapshot.ClassHeadBucketMasks);
        CollectionAssert.AreEqual(legacySnapshot.ClassExactHeadMasks, cobraSnapshot.ClassExactHeadMasks);
        CollectionAssert.AreEqual(legacySnapshot.ClassChildEqualityMasks, cobraSnapshot.ClassChildEqualityMasks);
        CollectionAssert.AreEqual(legacySnapshot.ClassChildAtomBucketMasks, cobraSnapshot.ClassChildAtomBucketMasks);
        CollectionAssert.AreEqual(legacySnapshot.ClassChildConstraintMasks, cobraSnapshot.ClassChildConstraintMasks);
        CollectionAssert.AreEqual(legacySnapshot.ClassChildReferenceBloomMasks, cobraSnapshot.ClassChildReferenceBloomMasks);
    }

    [TestMethod]
    public void CobraShapeAnalysis_MatchesLegacyShapeAnalysis_ForSimpleGraph()
    {
        var legacyGraph = new EGraph();
        int vectorClass = legacyGraph.Add(new Vector(new Number(1), new Number(2)));
        int addClass = legacyGraph.Add(new Add(new Number(1), new Number(2)).Canonicalize());
        legacyGraph.Rebuild();
        ShapeAnalysis.Analyze(legacyGraph);

        var graphState = new CobraGraphState();
        graphState.SyncFromLegacyGraph(legacyGraph);
        CobraShapeAnalysis.Analyze(graphState);

        Assert.IsTrue(legacyGraph.GetClass(vectorClass).Data is Shape legacyVectorShape && legacyVectorShape.IsVector);
        Assert.IsTrue(graphState.GetClass(vectorClass).Metadata.TryGetValue("shape", out object? cobraVectorShapeObject) &&
                      cobraVectorShapeObject is Shape cobraVectorShape &&
                      cobraVectorShape.IsVector);

        Assert.IsTrue(legacyGraph.GetClass(addClass).Data is Shape legacyAddShape && legacyAddShape.IsScalar);
        Assert.IsTrue(graphState.GetClass(addClass).Metadata.TryGetValue("shape", out object? cobraAddShapeObject) &&
                      cobraAddShapeObject is Shape cobraAddShape &&
                      cobraAddShape.IsScalar);
    }

    [TestMethod]
    public void CobraPlannerSnapshot_FromCobraGraphState_IgnoresEmptyRootClasses()
    {
        var graphState = new CobraGraphState();
        int populated = graphState.AddExpression(new Symbol("x"));
        int empty = graphState.AddClass();

        var snapshot = CobraPlannerSnapshot.Create(graphState);

        CollectionAssert.AreEqual(new[] { graphState.Find(populated) }, snapshot.RootIds);
        Assert.IsFalse(snapshot.RootIds.Contains(empty));
    }

    [TestMethod]
    public void CobraUnionEngine_WarmsGpuParentsFromCobraGraphStateInsteadOfLegacyGraph()
    {
        var legacyGraph = new EGraph();
        int a = legacyGraph.Add(new Symbol("a"));
        int b = legacyGraph.Add(new Symbol("b"));
        int c = legacyGraph.Add(new Symbol("c"));
        legacyGraph.Rebuild();

        var graphState = new CobraGraphState();
        graphState.SyncFromLegacyGraph(legacyGraph);
        graphState.Union(a, b);

        Assert.IsTrue(CobraUnionEngine.TryWarmGpuParentsFromCobra(graphState));

        var warmedParents = SymCobra.Runtime.CobraCudaNative.GetParentsSnapshot(graphState.ClassCount);

        CollectionAssert.AreEqual(graphState.GetParentSnapshot(), warmedParents);
        Assert.AreNotEqual(legacyGraph.Find(b), warmedParents[b], "Expected the warmed GPU cache to follow COBRA's authoritative parent snapshot, not the stale legacy graph.");
        Assert.AreEqual(graphState.Find(c), warmedParents[c], "Unrelated roots should remain unchanged in the warmed GPU cache.");
    }
}
