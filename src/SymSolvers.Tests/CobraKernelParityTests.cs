using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using SymCobra.Runtime;

namespace SymSolvers.Tests;

[TestClass]
public class CobraKernelParityTests
{
    [TestMethod]
    public void BatchMseScores_ParityWithCpu()
    {
        if (CobraCudaNative.GetDeviceCount() == 0)
        {
            Assert.Inconclusive("No CUDA devices found, skipping parity test.");
            return;
        }

        int samples = 1000;
        int candidates = 10;
        double[] targets = new double[samples];
        double[] predictions = new double[samples * candidates];
        var random = new Random(42);

        for (int i = 0; i < samples; i++)
        {
            targets[i] = random.NextDouble();
        }

        for (int c = 0; c < candidates; c++)
        {
            for (int i = 0; i < samples; i++)
            {
                predictions[c * samples + i] = random.NextDouble();
            }
        }

        // CPU computation
        double[] expected = new double[candidates];
        for (int c = 0; c < candidates; c++)
        {
            double sum = 0;
            int baseOffset = c * samples;
            for (int i = 0; i < samples; i++)
            {
                double diff = predictions[baseOffset + i] - targets[i];
                sum += diff * diff;
            }
            expected[c] = sum / Math.Max(1, samples);
        }

        // GPU computation
        bool success = CobraCudaNative.TryBatchMseScores(predictions, targets, samples, candidates, out double[] gpuResults);

        Assert.IsTrue(success, "TryBatchMseScores failed on GPU");
        Assert.AreEqual(expected.Length, gpuResults.Length);

        for (int c = 0; c < candidates; c++)
        {
            Assert.AreEqual(expected[c], gpuResults[c], 1e-6, $"Mismatch at candidate {c}");
        }
    }

    [TestMethod]
    public void BatchedUnion_ParityWithCpu()
    {
        if (CobraCudaNative.GetDeviceCount() == 0)
        {
            Assert.Inconclusive("No CUDA devices found, skipping parity test.");
            return;
        }

        int classCount = 100;
        int[] parents = Enumerable.Range(0, classCount).ToArray();
        
        Assert.IsTrue(CobraCudaNative.TryWarmParents(parents));

        int[] lefts = { 5, 10, 20, 5 };
        int[] rights = { 10, 20, 30, 40 };

        // CPU computation
        int Find(int[] p, int i)
        {
            int root = i;
            while (root != p[root]) root = p[root];
            int curr = i;
            while (curr != root)
            {
                int next = p[curr];
                p[curr] = root;
                curr = next;
            }
            return root;
        }

        void Union(int[] p, int i, int j)
        {
            int rootI = Find(p, i);
            int rootJ = Find(p, j);
            if (rootI != rootJ)
            {
                if (rootI < rootJ) p[rootJ] = rootI;
                else p[rootI] = rootJ;
            }
        }

        int[] expectedParents = (int[])parents.Clone();
        for (int i = 0; i < lefts.Length; i++)
        {
            Union(expectedParents, lefts[i], rights[i]);
        }
        
        for (int i = 0; i < classCount; i++)
        {
            Find(expectedParents, i);
        }

        // GPU computation
        Assert.IsTrue(CobraCudaNative.TryUnionBatchGpuCached(lefts, rights, out bool changed));
        Assert.IsTrue(changed);

        int[] gpuParents = CobraCudaNative.GetParentsSnapshot(classCount);
        
        for (int i = 0; i < classCount; i++)
        {
            Assert.AreEqual(Find(expectedParents, i), Find(gpuParents, i), $"Mismatch at index {i}. Expected: {Find(expectedParents, i)}, GPU: {Find(gpuParents, i)}");
        }
    }

    [TestMethod]
    public void MatchPriorityV3_ParityWithCpu()
    {
        if (CobraCudaNative.GetDeviceCount() == 0)
        {
            Assert.Inconclusive("No CUDA devices found, skipping parity test.");
            return;
        }

        int count = 100;
        var random = new Random(42);

        int[] hotFlags = Enumerable.Range(0, count).Select(_ => random.Next(0, 2)).ToArray();
        int[] boundaryFlags = Enumerable.Range(0, count).Select(_ => random.Next(0, 2)).ToArray();
        int[] suppressedFlags = Enumerable.Range(0, count).Select(_ => random.Next(0, 2)).ToArray();
        int[] ruleArities = Enumerable.Range(0, count).Select(_ => random.Next(1, 5)).ToArray();
        int[] directFlags = Enumerable.Range(0, count).Select(_ => random.Next(0, 2)).ToArray();
        int[] nestedFlags = Enumerable.Range(0, count).Select(_ => random.Next(0, 2)).ToArray();

        int[] expectedScores = new int[count];
        for (int i = 0; i < count; i++)
        {
            expectedScores[i] =
                (hotFlags[i] != 0 ? 1000 : 0) +
                (boundaryFlags[i] != 0 ? 100 : 0) -
                (suppressedFlags[i] != 0 ? 1000 : 0) +
                (ruleArities[i] * 12) +
                (directFlags[i] != 0 ? 32 : 0) +
                (nestedFlags[i] != 0 ? 72 : 0);
        }

        bool success = CobraCudaNative.TryScoreMatchPriorityV3(
            hotFlags,
            boundaryFlags,
            suppressedFlags,
            ruleArities,
            directFlags,
            nestedFlags,
            out int[] gpuScores);

        Assert.IsTrue(success, "TryScoreMatchPriorityV3 failed on GPU");
        Assert.AreEqual(expectedScores.Length, gpuScores.Length);

        for (int i = 0; i < count; i++)
        {
            Assert.AreEqual(expectedScores[i], gpuScores[i], $"Mismatch at index {i}");
        }
    }
    [TestMethod]
    public void FrontierV3_ParityWithCpu()
    {
        if (CobraCudaNative.GetDeviceCount() == 0)
        {
            Assert.Inconclusive("No CUDA devices found, skipping parity test.");
            return;
        }

        int classCount = 100;
        var random = new Random(42);

        int[] classIds = Enumerable.Range(0, classCount).ToArray();
        int[] nodeCounts = Enumerable.Range(0, classCount).Select(_ => random.Next(1, 20)).ToArray();
        int[] generations = Enumerable.Range(0, classCount).Select(_ => random.Next(1, 10)).ToArray();
        int[] hotFlags = Enumerable.Range(0, classCount).Select(_ => random.Next(0, 2)).ToArray();
        int[] boundaryFlags = Enumerable.Range(0, classCount).Select(_ => random.Next(0, 2)).ToArray();
        int[] residualFlags = Enumerable.Range(0, classCount).Select(_ => random.Next(0, 2)).ToArray();
        int[] suppressedFlags = Enumerable.Range(0, classCount).Select(_ => random.Next(0, 2)).ToArray();
        int[] hotRegionCounts = Enumerable.Range(0, classCount).Select(_ => random.Next(0, 5)).ToArray();
        int[] boundaryRegionCounts = Enumerable.Range(0, classCount).Select(_ => random.Next(0, 3)).ToArray();

        int[] expectedScores = new int[classCount];
        for (int i = 0; i < classCount; i++)
        {
            expectedScores[i] = (hotFlags[i] * 1600) +
                                (hotRegionCounts[i] * 300) +
                                (generations[classIds[i]] * 8) +
                                (nodeCounts[classIds[i]] * 2) -
                                (boundaryFlags[i] * 180) -
                                (boundaryRegionCounts[i] * 40) -
                                (residualFlags[i] * 140) -
                                (suppressedFlags[i] * 1200);
        }

        bool success = CobraCudaNative.TryScoreFrontierV3ById(
            classIds,
            nodeCounts,
            generations,
            hotFlags,
            boundaryFlags,
            residualFlags,
            suppressedFlags,
            hotRegionCounts,
            boundaryRegionCounts,
            out int[] gpuScores);

        Assert.IsTrue(success, "TryScoreFrontierV3ById failed on GPU");
        Assert.AreEqual(expectedScores.Length, gpuScores.Length);

        for (int i = 0; i < classCount; i++)
        {
            Assert.AreEqual(expectedScores[i], gpuScores[i], $"Mismatch at index {i}");
        }
    }
}
