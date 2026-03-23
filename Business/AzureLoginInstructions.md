# Azure Login Instructions

This repo uses a restricted Azure service principal login for agent-driven deployment work.

## Known working Azure CLI path

```powershell
$AzCli = "C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd"
```

If `az` is not on `PATH`, always call Azure CLI through `$AzCli`.

## Known config directory

Use a writable config directory inside the repo when possible:

```powershell
$env:AZURE_CONFIG_DIR = "C:\Users\wowod\Desktop\Code2025\SymWork\.azure"
```

The original restricted profile lives at:

```powershell
C:\Users\wowod\Documents\AzureAgent\.azure-restricted
```

That original folder may be readable but not writable from sandboxed agent sessions, so repo-local `.azure` is preferred for active work.

## Service principal login flow

The working login pattern is:

```powershell
$AzCli = "C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd"
$PassDir = "C:\Users\wowod\Desktop\Code2025\Pass\Agent2Azure"
$TenantId = "ed2300e5-3e13-4aad-b8a3-1b9add838433"
$env:AZURE_CONFIG_DIR = "C:\Users\wowod\Desktop\Code2025\SymWork\.azure"

$appId = (Get-Content (Join-Path $PassDir "id.txt") -Raw).Trim()
$secret = (Get-Content (Join-Path $PassDir "secret.txt") -Raw).Trim()

& $AzCli logout
& $AzCli login --service-principal --username $appId --password $secret --tenant $TenantId --output none

$secret = $null
```

## Verify the login

After login:

```powershell
& $AzCli account list --output table
```

Expected subscription:

- Name: `Pago pelo Uso`
- Subscription ID: `3b878f13-bba7-46ef-b0ce-a036dccc7432`
- Tenant ID: `ed2300e5-3e13-4aad-b8a3-1b9add838433`

## Verify resource-group access

```powershell
& $AzCli group show --name "AICodersResourceGroup" --output table
```

Known good response when authorization is working:

- Location: `westus`
- Name: `AICodersResourceGroup`

To inspect currently visible resources:

```powershell
& $AzCli resource list --resource-group "AICodersResourceGroup" --output table
```

Known previously visible resources:

- `smolmemory1` (`Microsoft.Storage/storageAccounts`)
- `ASP-AICodersResourceGroup-a5a1` (`Microsoft.Web/serverFarms`)
- `SmolAgent1` (`Microsoft.Web/sites`)

Known current Static Web App:

- Name: `Sym`
- Resource group: `AICodersResourceGroup`
- Default hostname: `purple-pebble-07343a21e.4.azurestaticapps.net`
- Repository: `https://github.com/Wowo51/Sym`
- Branch: `main`

To verify it directly:

```powershell
& $AzCli staticwebapp list --output table
```

## Known issue

Authorization has been flaky across logins. The same service principal login has sometimes returned:

```text
AuthorizationFailed
The client does not have authorization to perform action 'Microsoft.Resources/subscriptions/resourcegroups/read'
```

If that happens:

1. Run `logout`.
2. Log in again with the same service principal.
3. Re-run `group show`.

If repeated retries still fail, the service principal likely needs role assignment review in Azure IAM.

## GitHub to Azure deployment path

This repo already contains the workflow:

- `.github/workflows/azure-static-web-apps-purple-pebble-07343a21e.yml`

That workflow deploys pushes to `main` to the `Sym` Azure Static Web App using:

- .NET publish output from `src/SymBlazor`
- the GitHub secret `AZURE_STATIC_WEB_APPS_API_TOKEN`
- the Azure Static Web Apps deploy action

Recommended release flow:

1. Commit site changes on `main`.
2. Push to `origin/main`.
3. Watch the GitHub Actions run for the Azure Static Web Apps workflow.
4. Confirm the deployed site updates on the Azure Static Web App hostname or the bound custom domain.

Helpful GitHub commands:

```powershell
gh auth status
gh run list --workflow "azure-static-web-apps-purple-pebble-07343a21e.yml" --limit 5
gh run watch <run-id>
```

## What is still needed if GitHub deployment fails

To recover Sym deployment through GitHub, the agent still needs one of these if the current workflow path stops working:

1. Permission to create or manage an Azure Static Web App.
2. Permission to read deployment secrets for an existing Azure Static Web App.
3. A confirmed alternative target, such as an existing App Service, plus publish/deploy credentials.

For the GitHub side, `gh auth login` must also remain valid so the repo secret can be created or updated.
