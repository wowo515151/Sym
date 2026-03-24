using System.Net;

namespace SymMCP.Tests;

[TestClass]
public class SymMcpHostTests
{
    [TestMethod]
    public async Task HealthEndpoint_ReturnsOk()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task McpEndpoint_RejectsMissingApiKey()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/mcp", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task McpEndpoint_AllowsConfiguredApiKeyToReachEndpoint()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-secret");

        var response = await client.PostAsync("/mcp", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.AreNotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
