# SymBlazor Deployment

The app is developed locally with a root base href in [src/SymBlazor/wwwroot/index.html](c:/Users/wowod/Desktop/Code2025/SymWork/src/SymBlazor/wwwroot/index.html).

For SymbolicComputation.com deployment, use the publish profile named `SymbolicComputationSym`.

Publish target:
- Output folder: `artifacts/publish/SymBlazor-SymbolicComputation`
- Deployable site files: `artifacts/publish/SymBlazor-SymbolicComputation/wwwroot`
- Production base href after publish: `/sym/`
- Root-level help files staged for upload: `artifacts/publish/SymBlazor-SymbolicComputation/SymHelp.txt` and `artifacts/publish/SymBlazor-SymbolicComputation/SymUIHelp.html`

Important:
- Upload the contents of the published `wwwroot` folder into the site's `/sym` folder.
- Upload `SymHelp.txt` and `SymUIHelp.html` from the publish root into the site root `/` so they are available as `/SymHelp.txt` and `/SymUIHelp.html`.
- Do not upload the published `/sym` app files into the site root.
- The site root `index.html` is still not touched by this publish profile unless someone manually copies app files there.

CLI publish example:

```powershell
dotnet publish src/SymBlazor/SymBlazor.csproj /p:PublishProfile=SymbolicComputationSym
```