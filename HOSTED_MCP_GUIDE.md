# Sym MCP Hosted Service Guide

**Date:** 2026-03-24

## Service Overview

This service provides a hosted Agentverse wrapper for the Azure Sym MCP solver.
It runs on the Agentverse Hosted Cloud and provides high-availability symbolic math capabilities to AI agents.

- **Hosted Agent Name:** `SymMcp_Wrapper`
- **Hosted Agent Address:** `agent1qdd7zue9uh2pj5djudx4udc8m9e55ajtxxlczpps2azvjmj3xmtewgwhfmc`
- **Hosted Payment Wallet:** `fetch1e59g94xrlzf9yg43hympu073ux3nf70mqy8xvq`

## Services and Fees

The service operates on a pay-per-solve model using the FET token (Fetch.ai). 
*Note: 1 FET = 1,000,000,000,000,000,000 afet.*

1. `mcaas.sym.lightning`
   - **Fee:** 0.1 FET
   - **Timeout tier:** 1 second

2. `mcaas.sym.standard`
   - **Fee:** 1.0 FET
   - **Timeout tier:** 10 seconds

3. `mcaas.sym.deep`
   - **Fee:** 10.0 FET
   - **Timeout tier:** 100 seconds

## Payment Requirement

Payment is required *before* execution. The request must include a `payment_tx_hash`.

The payment transaction must:
- Be a successful on-chain Fetch mainnet transaction.
- Send at least the required tier amount.
- Use denomination `afet`.
- Send funds to the Hosted Payment Wallet: `fetch1e59g94xrlzf9yg43hympu073ux3nf70mqy8xvq`

If payment is missing or invalid, the service rejects the request before calling the Azure backend.

## Request Model

The service accepts the following message model:

```json
{
  "service": "string",
  "problemScript": "string",
  "user_id": "string",
  "payment_tx_hash": "string"
}
```

**Fields:**
- `service`: One of `mcaas.sym.lightning`, `mcaas.sym.standard`, `mcaas.sym.deep`.
- `problemScript`: The Sym problem text to solve.
- `user_id`: Buyer-defined request label.
- `payment_tx_hash`: Required on-chain payment transaction hash.

## Response Model

The service returns:

```json
{
  "service": "string",
  "response": "string",
  "manifest": "dict"
}
```

Typical `manifest` fields include:
- `status`
- `solve_ms`
- `tier`
- `limit_s`
- `price_fet`
- `hosted`
- `payment_required`
- `payment`

## Minimal Example

### Example Request

```json
{
  "service": "mcaas.sym.lightning",
  "problemScript": "2 + 2",
  "user_id": "demo_request",
  "payment_tx_hash": "<64-hex mainnet tx hash>"
}
```

### Example Successful Response Content

```json
{"ok":true,"result":"4","elapsedMs":29,"estimatedWorkUnits":2}
```

### Example Success Manifest

```json
{
  "status": "ok",
  "solve_ms": 845,
  "tier": "mcaas.sym.lightning",
  "limit_s": 1.0,
  "price_fet": 0.1,
  "hosted": true,
  "payment_required": true
}
```

### Example Unpaid Rejection

```json
{
  "status": "rejected",
  "error_class": "payment_missing"
}
```

## How to Use It

1. **Choose a tier** based on the expected complexity (cost and timeout).
2. **Send the required FET amount** to the hosted payment wallet.
3. **Wait** for the transaction to finalize on-chain.
4. **Send the agent request** with the `service`, `problemScript`, `user_id`, and the `payment_tx_hash`.
5. **Read** the returned response and manifest.

## Supported Use

This service is intended for Sym-backed symbolic math solving through the Azure MCP endpoint.

Examples:
- Arithmetic
- Algebraic simplification
- Equation solving
- Symbolic problem scripts supported by the backend

*Current deployment state: Running & Compiled.*