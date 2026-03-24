using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SymMCP.Tests;

internal sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SymMcp:McpPath"] = "/mcp",
                ["SymMcp:DefaultSolveTimeoutSeconds"] = "30",
                ["SymMcp:MaxSolveTimeoutSeconds"] = "30",
                ["SymMcp:ApiKeys:0:KeyId"] = "test-primary",
                ["SymMcp:ApiKeys:0:CustomerId"] = "test-customer",
                ["SymMcp:ApiKeys:0:DisplayName"] = "Test Primary",
                ["SymMcp:ApiKeys:0:Secret"] = "test-secret"
            });
        });
    }
}
