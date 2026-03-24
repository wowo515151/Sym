using SymMCP.Services;

namespace SymMCP.Tests;

[TestClass]
public class CSharpMathAnalysisServiceTests
{
    [TestMethod]
    public async Task AnalyzeAsync_ReturnsNoFindingsForSafeCode()
    {
        var service = new CSharpMathAnalysisService();

        var result = await service.AnalyzeAsync(new CSharpMathAnalysisRequest("""
using System;
class C
{
    int Add(int a, int b) => a + b;
}
"""));

        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(0, result.FindingsCount);
    }
}
