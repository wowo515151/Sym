using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Sym.Atoms;
using Sym.Core.EGraph;
using Sym.Operations;
using SymCobra.Regions;

namespace SymSolvers.Tests;

[TestClass]
public class CobraRegionExpansionTests
{
    [TestMethod]
    public void Detect_ReluOnMatMul_FindsStructuredRegionForMatMulCore()
    {
        var graph = new EGraph();
        var matmul = new MatMul(new Symbol("A"), new Symbol("B"));
        var relu = new Relu(matmul);

        int reluId = graph.Add(relu);
        graph.Rebuild();

        var regions = CobraRegionDetector.Detect(graph);

        var structuredRegion = regions.FirstOrDefault(r => r.Family != CobraRegionFamily.Unknown);
        
        Assert.IsNotNull(structuredRegion, "Expected a structured region to be detected.");
        Assert.AreNotEqual(CobraRegionFamily.Unknown, structuredRegion.Family, "Relu(MatMul) should still produce a structured region classification.");
        
        int matmulId = graph.Find(graph.Add(matmul));
        Assert.IsTrue(structuredRegion.MemberClassIds.Contains(matmulId), "Expected MatMul class to be a member.");
        Assert.IsTrue(
            structuredRegion.Family is CobraRegionFamily.LeftFactorPack or CobraRegionFamily.SharedSink,
            $"Expected Relu(MatMul) to classify around the MatMul core, but saw {structuredRegion.Family}.");
    }

    [TestMethod]
    public void Detect_SharedRightFactor_ClassifiesRightFactorPack()
    {
        var graph = new EGraph();
        graph.Add(new Add(
            new MatMul(new Symbol("A"), new Symbol("B")),
            new MatMul(new Symbol("C"), new Symbol("B"))));
        graph.Rebuild();

        var regions = CobraRegionDetector.Detect(graph);

        Assert.IsTrue(regions.Any(r => r.Family == CobraRegionFamily.RightFactorPack),
            "Expected a structured region to identify the repeated right factor.");
    }

    [TestMethod]
    public void Detect_SharedFactorAcrossSeparateRoots_UsesParentOverlapExpansion()
    {
        var graph = new EGraph();
        int leftMatMulId = graph.Add(new MatMul(new Symbol("A"), new Symbol("B")));
        int rightMatMulId = graph.Add(new MatMul(new Symbol("A"), new Symbol("C")));
        int sharedFactorId = graph.Add(new Symbol("A"));
        graph.Rebuild();

        leftMatMulId = graph.Find(leftMatMulId);
        rightMatMulId = graph.Find(rightMatMulId);
        sharedFactorId = graph.Find(sharedFactorId);

        var regions = CobraRegionDetector.Detect(graph);

        Assert.IsTrue(regions.Any(region =>
                region.MemberClassIds.Contains(sharedFactorId) &&
                region.MemberClassIds.Contains(leftMatMulId) &&
                region.MemberClassIds.Contains(rightMatMulId)),
            "Expected parent-overlap expansion to build a region that includes the shared factor and both structured parents.");
    }

    [TestMethod]
    public void ConflictResolver_ExcludesSuppressedAndHotClassesFromBoundarySet()
    {
        var regions = new[]
        {
            new CobraRegion(0, CobraRegionFamily.SharedSink, new[] { 1 }, new[] { 1, 2 }, 10, 1, CobraScoreSource.CpuHeuristic, true, false, "accepted"),
            new CobraRegion(1, CobraRegionFamily.BilinearOverlap, new[] { 1, 3 }, new[] { 3, 4 }, 5, 6, CobraScoreSource.CpuHeuristic, false, false, "suppressed")
        };

        var plan = CobraConflictResolver.BuildPlan(regions);

        Assert.IsTrue(plan.HotClassIds.Count > 0, "Expected at least one accepted hot class.");
        Assert.IsTrue(plan.SuppressedClassIds.Count > 0, "Expected at least one suppressed class from the conflict.");
        Assert.IsFalse(plan.BoundaryClassIds.Overlaps(plan.HotClassIds), "Hot classes should not remain in the boundary set.");
        Assert.IsFalse(plan.BoundaryClassIds.Overlaps(plan.SuppressedClassIds), "Suppressed classes should not remain in the boundary set.");
        Assert.IsFalse(plan.ResidualClassIds.Overlaps(plan.SuppressedClassIds), "Suppressed classes should not remain in the residual set.");
    }

    [TestMethod]
    public void ConflictResolver_TracksRegionSelectionMetrics()
    {
        var regions = new[]
        {
            new CobraRegion(0, CobraRegionFamily.SharedSink, new[] { 1 }, new[] { 2 }, 10, 1, CobraScoreSource.CpuHeuristic, false, false, "accepted"),
            new CobraRegion(1, CobraRegionFamily.BilinearOverlap, new[] { 1, 3 }, new[] { 4 }, 5, 6, CobraScoreSource.CpuHeuristic, false, false, "suppressed")
        };

        var plan = CobraConflictResolver.BuildPlan(regions);

        CollectionAssert.AreEquivalent(new[] { 0 }, plan.SelectedRegionIds.ToArray());
        CollectionAssert.AreEquivalent(new[] { 1 }, plan.SuppressedRegionIds.ToArray());
        Assert.AreEqual(1, plan.PackCount);
        Assert.AreEqual(1.0, plan.ConflictDensity, 1e-9);
        Assert.AreEqual(0.5, plan.ReductionRatio, 1e-9);
    }

    [TestMethod]
    public void ConflictResolver_PrefersCompatiblePacksOverSingleConflictingCoverRegion()
    {
        var regions = new[]
        {
            new CobraRegion(0, CobraRegionFamily.SharedSink, new[] { 1, 2 }, System.Array.Empty<int>(), 10, 2, CobraScoreSource.CpuHeuristic, false, false, "wide"),
            new CobraRegion(1, CobraRegionFamily.LeftFactorPack, new[] { 1 }, System.Array.Empty<int>(), 8, 1, CobraScoreSource.CpuHeuristic, false, false, "left-pack"),
            new CobraRegion(2, CobraRegionFamily.RightFactorPack, new[] { 2 }, System.Array.Empty<int>(), 8, 1, CobraScoreSource.CpuHeuristic, false, false, "right-pack")
        };

        var plan = CobraConflictResolver.BuildPlan(regions);

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, plan.SelectedRegionIds.ToArray());
        CollectionAssert.AreEquivalent(new[] { 0 }, plan.SuppressedRegionIds.ToArray());
        Assert.AreEqual(2, plan.PackCount);
        Assert.AreEqual(1.0 / 3.0, plan.ReductionRatio, 1e-9);
    }
}
