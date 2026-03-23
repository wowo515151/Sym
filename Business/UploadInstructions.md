# Upload Instructions

This repo is deployed to Azure through GitHub Actions.

## Normal release flow

1. Commit the desired changes on `main`.
2. Push to `origin/main`.
3. GitHub Actions runs `.github/workflows/azure-static-web-apps-purple-pebble-07343a21e.yml`.
4. That workflow publishes `src/SymBlazor` and uploads the static site to the `Sym` Azure Static Web App.

## Useful commands

```powershell
git status --short --branch
git add <files>
git commit -m "Your message"
git push origin main
gh run list --workflow "azure-static-web-apps-purple-pebble-07343a21e.yml" --limit 5
gh run watch <run-id>
```

## Azure notes

- Azure login help lives in `Business/AzureLoginInstructions.md`.
- The current Static Web App is `Sym` in resource group `AICodersResourceGroup`.
- The workflow path is preferred over manual Azure uploads because the GitHub secret and publish steps are already configured there.
