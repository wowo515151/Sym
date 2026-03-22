// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Immutable;
using Sym.CSharpIO;
using SymSolvers.StableForms;

namespace SymSolvers.Tests.StableForms;

[TestClass]
public sealed class StableLossSynthesisGuardsTests
{
    [TestMethod]
        [Timeout(10000)]
    public void Guards_Are_Extracted_For_Log_And_Divide()
    {
        var expr = CSharpIO.ParseExpressions("log(x) + 1/(y)")[0];
        var strat = new StableLossSynthesisStrategy();
        var ctx = new Sym.Core.SolveContext(additionalData: ImmutableDictionary<string, object>.Empty);

        var result = strat.Solve(expr, ctx);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var text = result.Message.ToLowerInvariant();
        StringAssert.Contains(text, "guards");
    }
}
