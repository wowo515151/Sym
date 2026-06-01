import test from "node:test";
import assert from "node:assert/strict";
import { describeTensorSignature, einsum, parseEinsumSignature } from "../../src/SymBlazor/wwwroot/tools/js/core/tensor-index.js";

test("einsum parser splits inputs and output", () => {
  const parsed = parseEinsumSignature("ij,jk->ik");
  assert.deepEqual(parsed, { inputs: ["ij", "jk"], output: "ik" });
});

test("einsum performs matrix multiplication", () => {
  const left = [[1, 2], [3, 4]];
  const right = [[5, 6], [7, 8]];
  assert.deepEqual(einsum("ij,jk->ik", [left, right]), [[19, 22], [43, 50]]);
});

test("einsum performs dot product and signature description", () => {
  const result = einsum("i,i->", [[1, 2, 3], [4, 5, 6]]);
  assert.equal(result, 32);
  const description = describeTensorSignature("i,i->", [[1, 2], [3, 4]]);
  assert.deepEqual(description.shapes, [[2], [2]]);
});

test("einsum validates output indices and rectangular tensors", () => {
  assert.throws(() => einsum("ij,jk->iz", [[[1]], [[1]]]), /Output index/);
  assert.throws(() => einsum("ij->ij", [[[1, 2], [3]]]), /rectangular/);
});
