// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using Sym.Atoms;
using Sym.Core.EGraph;
using Sym.Operations;

namespace SymEGraph.Tests;

[TestClass]
public class ExtractionTimeoutTests
{
    [TestMethod]
    public void ExtractBestEffort_ReturnsBestSoFar_WhenSoftTimeoutFires()
    {
        var graph = new EGraph();
        var rootId = graph.Add(new Add(new Symbol("x"), new Number(0)));
        graph.Add(new Symbol("x"));
        graph.Rebuild();

        using var softCts = new CancellationTokenSource();
        softCts.Cancel();

        var result = EGraphExtract.ExtractBestEffort(
            graph,
            rootId,
            costFunction: _ => 1,
            softCt: softCts.Token,
            hardCt: CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("(x + 0)", result.ToDisplayString());
    }

    [TestMethod]
    public void ExtractBestEffort_Throws_WhenHardCancellationFires()
    {
        var graph = new EGraph();
        var rootId = graph.Add(new Add(new Symbol("x"), new Number(0)));
        graph.Add(new Symbol("x"));
        graph.Rebuild();

        using var hardCts = new CancellationTokenSource();
        hardCts.Cancel();

        Assert.ThrowsException<OperationCanceledException>(() =>
            EGraphExtract.ExtractBestEffort(
                graph,
                rootId,
                costFunction: _ => 1,
                softCt: CancellationToken.None,
                hardCt: hardCts.Token));
    }
}
