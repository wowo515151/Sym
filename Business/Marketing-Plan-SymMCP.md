# Marketing Plan: Sym MCP Hosted Service
**Date:** Tuesday, March 24, 2026
**Product:** Sym MCP Solver (Azure Container Apps + Agentverse Wrapper)
**Demo:** [SymbolicComputation.com](https://symboliccomputation.com)

---

## 1. Executive Summary
Sym is a high-performance symbolic computation engine designed to bridge the gap between Large Language Models (LLMs) and precise mathematical reasoning. By hosting Sym as a Model Context Protocol (MCP) endpoint with an Agentverse wrapper, we provide AI agents with a "System 2" thinking brain that handles complex math, logic, and tensor optimizations with 100% precision.

## 2. Target Audience
- **AI Agent Developers:** Those building autonomous agents that need to solve math or logic problems without hallucinations.
- **GPU/Machine Learning Researchers:** Developers looking to optimize tensor expressions (fusion, factoring, scale folding) before execution.
- **Enterprise Integrators:** Companies using MCP-compatible tools (like Claude Desktop or custom agents) that require private, reliable math solvers.
- **Fetch.ai Ecosystem Users:** Holders of FET looking for high-utility agent services.

## 3. Unique Selling Points (USPs)
- **Zero-Hallucination Math:** Unlike LLMs, Sym uses formal rules to solve equations, ensuring every result is mathematically sound.
- **Advanced Tensor Support:** Sym includes a specialized tensor rule-set (e.g., `MatMul` fusion, `Relu` commutation, `Scale Folding`) that can automatically optimize computation graphs for GPU performance.
- **MCP Native:** Built on the official Model Context Protocol, making it instantly compatible with any MCP-enabled client (Claude, LangChain, etc.).
- **Pay-per-Solve:** The Agentverse wrapper allows for seamless FET payments, enabling a "MaaS" (Math-as-a-Service) model without complex subscription overhead.
- **Scalable Azure Backend:** Hosted on Azure Container Apps with scale-to-zero capabilities, ensuring low latency and high availability.

## 4. Sym as a Complement to AI
Sym is the "System 2" (Slow, Logical) counterpart to the LLM's "System 1" (Fast, Intuitive) thinking.
- **LLM Role:** Translates natural language intent into a formal `ProblemScript`.
- **Sym Role:** Executes the `ProblemScript` using rigorous symbolic logic, returning a verifiable answer.
- **Benefit:** This "Neuro-symbolic" approach combines the language flexibility of AI with the absolute correctness of symbolic math.

## 5. Marketing Channels: Directories & Marketplaces

### A. MCP-Specific Directories
| Directory | URL | Labor Intensity | Listing Method |
| :--- | :--- | :--- | :--- |
| **Glama** | [glama.ai/mcp](https://glama.ai/mcp) | **Low** | Self-listing via web UI (Requires account). |
| **mcp.so** | [mcp.so](https://mcp.so) | **Low** | Community-driven self-listing. |
| **Smithery** | [smithery.ai](https://smithery.ai) | **Medium** | Registry submission; often requires metadata file. |
| **Official Registry** | [modelcontextprotocol.io](https://modelcontextprotocol.io) | **High** | Requires following official SDK standards and registry submission flow. |

### B. AI Agent Marketplaces
| Marketplace | URL | Labor Intensity | Listing Method |
| :--- | :--- | :--- | :--- |
| **Agentverse Explorer** | [agentverse.ai](https://agentverse.ai) | **Low** | Automated sync from Agentverse Hosted Cloud (Current setup). |
| **LangChain Hub** | [smith.langchain.com/hub](https://smith.langchain.com/hub) | **Medium** | Submission of tool/prompt templates. |
| **GPT Store** | [openai.com](https://chatgpt.com/gpts) | **Medium** | Requires creating a "GPT" that calls the Sym MCP Actions. |

### C. General API Marketplaces
| Marketplace | URL | Labor Intensity | Listing Method |
| :--- | :--- | :--- | :--- |
| **RapidAPI** | [rapidapi.com](https://rapidapi.com) | **High** | Full API documentation, testing, and manual verification required. |
| **APILayer** | [apilayer.com](https://apilayer.com) | **High** | Partnership-based; requires rigorous vetting. |

## 6. Action Plan & Task Breakdown

### Phase 1: Direct Listings (Self-Service)
- [ ] **Agentverse Explorer:** Verify metadata (description, tags) for the `SymMcp_Wrapper` agent.
- [ ] **Glama.ai:** Create an account and submit the endpoint URL.
- [ ] **mcp.so:** Submit the Sym server details to the community list.

### Phase 2: Content & SEO
- [ ] **SymbolicComputation.com Update:** Add a prominent "MCP Service" section with the Agentverse address and FET price tiers.
- [ ] **Case Study:** Write a blog post titled *"Optimizing GPU Tensor Graphs with Sym: A Guide for AI Engineers."*

### Phase 3: High-Labor Listings
- [ ] **RapidAPI Integration:** Wrap the MCP endpoint in a standard REST-like structure for the RapidAPI gateway to reach non-MCP users.
- [ ] **LangChain Tool:** Create a official LangChain integration/wrapper for Sym.

---

## 7. Effort Notes
- **Self-Listable (Low Effort):** Glama, mcp.so, Agentverse Explorer. I can handle these with minimal human input if provided with the final description text.
- **Human-Intensive (High Effort):** RapidAPI, Official MCP Registry, and any partnership-based marketplace. These require manual verification of identity, documentation quality, and sometimes legal/financial setup for payouts outside of FET.
