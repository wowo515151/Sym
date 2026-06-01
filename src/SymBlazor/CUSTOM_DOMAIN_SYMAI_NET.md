# SymAI.NET Custom Domain Runbook

This runbook attaches `SymAI.NET` to the existing Azure Static Web App that already serves `SymAI.NET`.

Goal:

- `https://symai.net` and `https://www.symai.net` should serve the same site as `https://symai.net`.
- Existing Azure hosting for `SymAI.NET` stays in place.
- HostGator can remain the registrar for now.

## What this project is using

This repo deploys `src/SymBlazor` through Azure Static Web Apps:

- Workflow: [azure-static-web-apps-purple-pebble-07343a21e.yml](c:/Users/wowod/Desktop/Code2025/SymWork/.github/workflows/azure-static-web-apps-purple-pebble-07343a21e.yml)
- Static Web Apps config: [staticwebapp.config.json](c:/Users/wowod/Desktop/Code2025/SymWork/src/SymBlazor/staticwebapp.config.json)

That means the custom-domain work happens in Azure Static Web Apps and DNS, not in Blazor routing.

## Recommended order

1. Add `www.symai.net` to Azure Static Web Apps.
2. Make the DNS record for `www.symai.net` in HostGator.
3. Wait for Azure validation and HTTPS certificate provisioning.
4. Add apex `symai.net`.
5. Decide whether apex should be:
   - forwarded to `https://www.symai.net/`, or
   - hosted through Azure DNS with an `ALIAS`, or
   - hosted through an `A` record fallback.

## Fastest safe path

Use this if you want the domain live soon without moving everything at once.

### 1. Add `www.symai.net` in Azure

In the Azure portal:

1. Open your Static Web App for the current site.
2. Open `Settings` -> `Custom domains`.
3. Select `+ Add`.
4. Enter `www.symai.net`.
5. Let Azure show the validation/DNS target values.

### 2. Add the DNS record in HostGator

In HostGator DNS for `symai.net`, create the record Azure asks for.

In most cases for `www`, this will be a `CNAME`:

- Host: `www`
- Value/Target: the Azure Static Web Apps hostname Azure shows you

Save the record and wait for Azure validation to complete.

### 3. Verify HTTPS on `www.symai.net`

Check:

- `https://www.symai.net`
- `https://www.symai.net/sym/`
- any important pages such as `/library`

Do not proceed to the apex until `www` is green in Azure and serving over HTTPS.

## Apex `symai.net` options

### Option A: Best long-term

Move DNS hosting for `symai.net` to Azure DNS, while keeping registration at HostGator.

Why this is best:

- Azure DNS can use the Azure Static Web Apps apex-domain flow cleanly.
- You reduce dependence on HostGator hosting features.
- You keep domain registration separate from web hosting, which matches your stated goal.

High-level flow:

1. Create an Azure DNS zone for `symai.net`.
2. Recreate any needed DNS records there.
3. Change the authoritative nameservers for `symai.net` at HostGator to the Azure DNS nameservers.
4. Add apex `symai.net` in the Static Web App custom-domains blade.
5. Use the Azure-guided `TXT` validation and `ALIAS` mapping.

### Option B: Good temporary bridge

Keep DNS at HostGator and forward `symai.net` to `https://www.symai.net/`.

Why this is useful:

- Simple.
- Avoids apex DNS limitations at the registrar.
- Lets both addresses reach the same site while you prepare Azure DNS.

Watch for:

- Use a redirect/forward, ideally permanent once you are confident.
- Prefer forwarding only the apex, not `www`.

### Option C: Fallback only

Keep DNS at HostGator and use an apex `A` record if HostGator cannot do `ALIAS`/`ANAME` and forwarding is not acceptable.

Why this is less ideal:

- Azure documents that an `A` record points traffic at a single regional host.
- You lose part of the Static Web Apps global-routing advantage.

If you use this path:

1. In the Azure portal, open the Static Web App.
2. From `Overview`, inspect the JSON view and find `stableInboundIP`.
3. Add apex `symai.net` in `Custom domains`.
4. Use the Azure-generated `TXT` record for validation.
5. Add an `A` record at HostGator:
   - Host: `@`
   - Value: the `stableInboundIP`

## Recommended final state

The clean final arrangement is:

- Registrar: HostGator
- DNS hosting: Azure DNS
- Web hosting: Azure Static Web Apps
- Custom domains on the same Static Web App:
  - `symai.net`
  - `www.symai.net`
  - `symai.net`

## What does not need to change yet

You do not need to change:

- Blazor routes
- `staticwebapp.config.json`
- build pipeline
- HTML branding

Those can stay as-is until the new domain is serving traffic correctly.

## Future cleanup after cutover

After both `symai.net` and `www.symai.net` are working, you can do a second pass to update:

- visible branding text
- canonical URLs and social metadata
- internal absolute links that still point at `symai.net`
- any sitemap, robots, or verification files if you add them later

## Azure docs used for this runbook

- External apex-domain guidance for Azure Static Web Apps:
  https://learn.microsoft.com/en-us/azure/static-web-apps/apex-domain-external
- Azure Static Web Apps custom domain setup with Azure DNS:
  https://learn.microsoft.com/en-us/azure/static-web-apps/custom-domain-azure-dns
