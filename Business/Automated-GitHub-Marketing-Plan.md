# Automated GitHub Marketing Plan: Sym MCP & CLI
**Target:** GitHub-based MCP Registries, Awesome Lists, and AI Toolchains
**Focus:** Freemium Model (Open Source CLI + Paid Hosted Service)

---

## 1. Strategy: The "Freemium" Trojan Horse
We will use the **Open Source SymCLI** as the primary entry point for developers and "Awesome" lists. By providing a high-quality, free tool for local use, we gain entry into restricted directories that forbid "purely commercial" listings.

### The Conversion Path:
1.  **Local Dev:** User finds Sym on an "Awesome MCP" list.
2.  **Usage:** User installs the free SymCLI for local agent testing.
3.  **Scaling:** User realizes they need a hosted, high-availability version for their production agents.
4.  **Upgrade:** User clicks the "Premium Hosted" link in the README/CLI output to use the Agentverse/Azure endpoint.

---

## 2. GitHub README Updates (Required for Compliance)
To stay compliant with GitHub's "Awesome" list policies (which often prefer non-commercial tools), we must structure the README as follows:

-   **Primary Focus:** Open Source Symbolic Computation.
-   **License:** Clearly state the Open Source license (e.g., MIT/Apache).
-   **"Hosted Version" Section:** A dedicated section at the bottom: *"Need a managed endpoint? We offer a high-performance hosted version of this MCP server on Azure with Agentverse integration for production workloads. [View Premium Plans](https://symboliccomputation.com/mcp)."*

---

## 3. Automated Metadata Files

### A. `smithery.yaml` (For Smithery.ai & Registry Automation)
This file allows Smithery and other crawers to "self-index" Sym.

```yaml
# smithery.yaml
name: sym-mcp
version: 1.0.0
description: High-performance symbolic math and tensor optimization engine.
author: SymbolicComputation.com
repository: https://github.com/wowod/SymWork
license: MIT
categories:
  - Mathematics
  - AI Tools
  - GPU Optimization
runtime: dotnet
entrypoint: src/SymCLI/bin/Release/net10.0/SymCLI.dll # Points to the free CLI
config:
  env:
    SYM_PREMIUM_ENDPOINT: https://symcontainerapp.proudcliff-b0966a7a.canadacentral.azurecontainerapps.io/mcp
    SYM_AGENTVERSE_ADDR: agent1qdd7zue9uh2pj5djudx4udc8m9e55ajtxxlczpps2azvjmj3xmtewgwhfmc
```

### B. `mcp.json` (Emerging Standard)
```json
{
  "mcpId": "sym-solver",
  "name": "Sym Symbolic Solver",
  "description": "Zero-hallucination math and tensor optimization.",
  "sourceUrl": "https://github.com/wowod/SymWork",
  "capabilities": {
    "tools": ["sym.solve", "sym.analyze.tensor"],
    "resources": []
  },
  "pricing": {
    "type": "freemium",
    "hosted": "https://symboliccomputation.com/mcp"
  }
}
```

---

## 4. Automation Workflow (The "Bot" Path)

1.  **PR Automation:** I can generate a Pull Request to the [smithery-ai/mcp-registry](https://github.com/smithery-ai/mcp-registry) and [awesome-mcp](https://github.com/punkpeye/awesome-mcp) repositories.
2.  **README Badges:** Add badges for "MCP Compatible" and "Azure Hosted" to attract automated crawlers.
3.  **Issue/Discussion Monitoring:** Use a script to monitor GitHub Discussions for keywords like "math hallucination" or "tensor fusion" and automatically suggest Sym as a solution.

---

## 5. Compliance & Policy Notes
-   **GitHub Policy:** It is perfectly acceptable to link to a paid service from a README, provided the repository itself contains functional open-source code.
-   **Directory Rules:** Most "Awesome" lists *require* the open-source version to be the primary focus. If we only listed the paid endpoint, we would be rejected as "SPAM". By listing the CLI, we are a "Utility".

---

## 6. Next Steps
- [ ] **Action:** I will draft the specific "Premium Services" note for your `README.md`.
- [ ] **Action:** I will create the `smithery.yaml` and `mcp.json` files in the root of the workspace.
- [ ] **Action:** (Human Step) Once the repo is public/updated, you can trigger the PRs I've prepared.
