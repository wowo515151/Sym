import { describeTensorSignature, einsum } from "../core/tensor-index.js";
import { byId, renderChipList, renderMatrixTable, renderMessage, setHtml } from "../page-helpers.js";

function run() {
  try {
    const signature = byId("tensorSignature").value.trim();
    const tensors = JSON.parse(byId("tensorInputs").value);
    if (!Array.isArray(tensors)) {
      throw new Error("Tensor input JSON must be an array of tensors");
    }
    const description = describeTensorSignature(signature, tensors);
    const result = einsum(signature, tensors);

    setHtml("tensorStats", `
      <div class="tool-stat"><strong>Signature</strong><span>${description.inputs.join(", ")} -> ${description.output || "scalar"}</span></div>
      <div class="tool-stat"><strong>Input count</strong><span>${description.inputs.length}</span></div>
      <div class="tool-stat"><strong>Input shapes</strong><span>${description.shapes.map((shape) => `[${shape.join(", ")}]`).join(" ")}</span></div>
      <div class="tool-stat"><strong>Output rank</strong><span>${description.output.length}</span></div>
    `);
    setHtml("tensorLegend", renderChipList(description.inputs.map((indices, index) => `Tensor ${index + 1}: ${indices}`)));
    setHtml("tensorResult", renderMatrixTable(result, 5));
  } catch (error) {
    setHtml("tensorStats", renderMessage(error.message, "error"));
    setHtml("tensorLegend", "");
    setHtml("tensorResult", "");
  }
}

document.addEventListener("DOMContentLoaded", () => {
  byId("runTensor").addEventListener("click", run);
  run();
});
