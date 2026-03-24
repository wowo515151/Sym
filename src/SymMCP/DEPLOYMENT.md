# SymMCP Deployment

This project is packaged as a Linux container for `Azure Container Apps`.

## Local Docker Build

Build from the repository root:

```powershell
docker build -t symmcp-local -f src/SymMCP/Dockerfile .
```

Run locally:

```powershell
docker run --rm -p 8080:8080 `
  -e SymMcp__ApiKeys__0__KeyId=local-primary `
  -e SymMcp__ApiKeys__0__CustomerId=local `
  -e SymMcp__ApiKeys__0__DisplayName="Local Primary" `
  -e SymMcp__ApiKeys__0__Secret=change-me `
  symmcp-local
```

Smoke test:

```powershell
Invoke-WebRequest http://localhost:8080/health
```

The MCP endpoint is available at:

- `/mcp`

Protected requests must include:

- `X-API-Key`

## Azure Container Apps Notes

Recommended first deployment shape:

- `0.5 vCPU`
- `1.0 GiB` memory
- `min replicas = 0`
- `max replicas = 2`
- external ingress on port `8080`

Required environment variables or secrets:

- `SymMcp__ApiKeys__0__KeyId`
- `SymMcp__ApiKeys__0__CustomerId`
- `SymMcp__ApiKeys__0__DisplayName`
- `SymMcp__ApiKeys__0__Secret`

Optional runtime variables:

- `SymMcp__McpPath`
- `SymMcp__DefaultSolveTimeoutSeconds`
- `SymMcp__MaxSolveTimeoutSeconds`
- `SymMcp__EnableDetailedErrors`

## Example Azure Flow

Build and push:

```powershell
docker build -t symmcpregistry.azurecr.io/symmcp:latest -f src/SymMCP/Dockerfile .
docker push symmcpregistry.azurecr.io/symmcp:latest
```

Create the app:

```powershell
$AzCli = "C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd"

& $AzCli containerapp create `
  --name "symmcp" `
  --resource-group "AICodersResourceGroup" `
  --environment "symmcp-env" `
  --image "symmcpregistry.azurecr.io/symmcp:latest" `
  --target-port 8080 `
  --ingress external `
  --min-replicas 0 `
  --max-replicas 2 `
  --cpu 0.5 `
  --memory 1.0Gi
```

Configure secrets and env vars:

```powershell
& $AzCli containerapp secret set `
  --name "symmcp" `
  --resource-group "AICodersResourceGroup" `
  --secrets fetch-primary-key="<strong-random-secret>"

& $AzCli containerapp update `
  --name "symmcp" `
  --resource-group "AICodersResourceGroup" `
  --set-env-vars `
    ASPNETCORE_URLS=http://0.0.0.0:8080 `
    SymMcp__McpPath=/mcp `
    SymMcp__DefaultSolveTimeoutSeconds=60 `
    SymMcp__MaxSolveTimeoutSeconds=180 `
    SymMcp__ApiKeys__0__KeyId=fetch-primary `
    SymMcp__ApiKeys__0__CustomerId=fetch-agentverse `
    SymMcp__ApiKeys__0__DisplayName="Fetch Agentverse Primary" `
    SymMcp__ApiKeys__0__Secret=secretref:fetch-primary-key
```

## Verification

After deployment:

- check `/health`
- verify `/mcp` rejects requests without `X-API-Key`
- verify your MCP client can initialize and call `sym.solve`

More Azure notes live in [SymMCP-Azure-ContainerApps-Spec.md](/c:/Users/wowod/Desktop/Code2025/SymWork/Business/SymMCP-Azure-ContainerApps-Spec.md).
