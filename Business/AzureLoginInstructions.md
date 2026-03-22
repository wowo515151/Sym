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

## What is still needed for Sym deployment

To deploy Sym to Azure through GitHub, the agent still needs one of these:

1. Permission to create or manage an Azure Static Web App.
2. Permission to read deployment secrets for an existing Azure Static Web App.
3. A confirmed alternative target, such as an existing App Service, plus publish/deploy credentials.

For the GitHub side, `gh auth login` must also remain valid so the repo secret can be created or updated.
