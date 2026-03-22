using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Immutable;
using Sym.CSharpIO;
using SymSolvers.StableForms;

namespace SymSolvers.Tests.StableForms;

[TestClass]
public sealed class StableLossSynthesisStrategyStressTests
{
    [TestMethod]
        [Timeout(10000)]

    public void Synthesis_Rewrites_ExpMinusOne_To_Expm1()
    {
        using var _ = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var expr = CSharpIO.ParseExpressions("(exp(x) - 1) / x")[0];
        var strat = new StableLossSynthesisStrategy();
        var ctx = new Sym.Core.SolveContext(additionalData: ImmutableDictionary<string, object>.Empty);

        var result = strat.Solve(expr, ctx);

        Assert.IsTrue(result.IsSuccess, result.Message);
        StringAssert.Contains(result.ResultExpression?.ToDisplayString() ?? string.Empty, "expm1");
    }
}
