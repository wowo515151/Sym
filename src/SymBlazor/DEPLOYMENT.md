# SymBlazor Deployment

The app is developed locally with a root base href in [src/SymBlazor/wwwroot/index.html](c:/Users/wowod/Desktop/Code2025/SymWork/src/SymBlazor/wwwroot/index.html).

## Azure Static Web Apps

For Azure hosting from GitHub, prefer Azure Static Web Apps over manual file upload.

Recommended Azure Static Web Apps GitHub settings:
- App location: `src/SymBlazor`
- API location: leave blank
- Output location: `wwwroot`
- Branch: `main`

Notes:
- This repo now includes `src/SymBlazor/staticwebapp.config.json` so client-side Blazor routes fall back to `index.html`.
- In Azure Static Web Apps, the Blazor app should normally be hosted at the site root `/` rather than under `/sym/`.
- With root hosting, `SymHelp.txt`, `SymUIHelp.html`, `icon-192.png`, CSS, examples, and framework assets are all published directly from the Blazor app output.
- The older `/sym/` publish profile remains useful for non-Azure folder-based deployments.

For SymAI.NET deployment, use the publish profile named `SymbolicComputationSym`.

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

## Adding SymAI.NET as a custom domain

This repo is currently deployed through Azure Static Web Apps via [`.github/workflows/azure-static-web-apps-purple-pebble-07343a21e.yml`](c:/Users/wowod/Desktop/Code2025/SymWork/.github/workflows/azure-static-web-apps-purple-pebble-07343a21e.yml), so attaching `SymAI.NET` is primarily an Azure Static Web Apps custom-domain and DNS task.

Recommended migration approach:

1. Keep the existing `SymAI.NET` Azure setup unchanged.
2. Add `www.symai.net` to the same Azure Static Web App as a custom domain.
3. Add the apex `symai.net` either:
   - through Azure DNS using an `ALIAS` record, or
   - temporarily through HostGator forwarding to `https://www.symai.net/` if you are not ready to move DNS hosting yet.
4. Leave the HTML and branding alone for now; update those later after both domains serve the same site correctly.

Suggested cutover path toward Azure independence:

- Short term: leave the domain registered at HostGator and keep DNS there while you prove out `www.symai.net`.
- Medium term: move DNS hosting for `symai.net` to Azure DNS while keeping HostGator only as the registrar.
- Long term: once Azure DNS is authoritative, map apex `symai.net` with an `ALIAS` and manage both SSL and DNS from Azure.

Practical notes:

- `www.symai.net` is the easiest record to add with an external DNS provider because it can use a `CNAME`.
- Apex/root `symai.net` is the tricky part. If your DNS host does not support `ALIAS`, `ANAME`, or `CNAME` flattening, Azure recommends either forwarding apex to `www` or using an `A` record to the Static Web App's `stableInboundIP`.
- Using an apex `A` record works, but Azure notes that it sends traffic to a single regional host and loses the normal global-distribution benefit of Static Web Apps.

Portal flow:

1. Open the Azure Static Web App that serves SymAI.NET.
2. Go to `Settings` -> `Custom domains`.
3. Add `www.symai.net` first.
4. Let Azure generate the validation details.
5. Create the required DNS record in the current DNS host for `symai.net`.
6. Wait for validation and HTTPS certificate issuance to complete.
7. Add apex `symai.net` after `www.symai.net` is working.

DNS patterns to expect:

- `www.symai.net`: `CNAME` to the generated Azure Static Web Apps hostname after Azure gives you the target/validation instructions.
- `symai.net`: one of:
  - `ALIAS` or `ANAME` to the Azure Static Web Apps hostname if your DNS host supports it.
  - Domain forwarding from `symai.net` to `https://www.symai.net/`.
  - `A` record to the Static Web App `stableInboundIP` only if needed as a fallback.

No application code change should be required just to make the second domain serve the same site. The app routing in [`src/SymBlazor/staticwebapp.config.json`](c:/Users/wowod/Desktop/Code2025/SymWork/src/SymBlazor/staticwebapp.config.json) is path-based, not host-based.

For a step-by-step runbook tailored to `SymAI.NET`, see [CUSTOM_DOMAIN_SYMAI_NET.md](c:/Users/wowod/Desktop/Code2025/SymWork/src/SymBlazor/CUSTOM_DOMAIN_SYMAI_NET.md).
