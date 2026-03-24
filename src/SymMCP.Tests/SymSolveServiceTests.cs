using Microsoft.Extensions.Options;
using SymMCP.Services;

namespace SymMCP.Tests;

[TestClass]
public class SymSolveServiceTests
{
    [TestMethod]
    public async Task SolveAsync_SolvesSimpleEquation()
    {
        var service = CreateService();

        var result = await service.SolveAsync(new SymSolveRequest("""
<Options>
  Target: x
  RulePacks: EquationSolving, Algebraic
</Options>
x + 5 = 10
"""));

        Assert.IsTrue(result.Ok, result.Result);
        StringAssert.Contains(result.Result, "x = 5");
        Assert.IsGreaterThanOrEqualTo(0L, result.ElapsedMs);
    }

    private static SymSolveService CreateService()
    {
        var options = Options.Create(new SymMcpOptions
        {
            DefaultSolveTimeoutSeconds = 30,
            MaxSolveTimeoutSeconds = 30,
            ApiKeys = new List<ConfiguredApiKey>
            {
                new() { KeyId = "test", CustomerId = "test", Secret = "secret" }
            }
        });
        return new SymSolveService(options);
    }
}
