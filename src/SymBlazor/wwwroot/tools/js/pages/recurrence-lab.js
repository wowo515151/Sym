import { generateSequence, describeClosedForm } from "../core/recurrence.js";
import { byId, formatNumber, lineChartSvg, renderChipList, renderMessage, setHtml } from "../page-helpers.js";

function parseNumberList(id) {
  const values = byId(id).value.split(",").map((value) => Number(value.trim())).filter((value) => !Number.isNaN(value));
  if (!values.length) {
    throw new Error("Enter at least one number");
  }
  return values;
}

function run() {
  try {
    const coefficients = parseNumberList("recurrenceCoefficients");
    const initialValues = parseNumberList("recurrenceInitial");
    const count = Number(byId("recurrenceCount").value);
    const sequence = generateSequence(coefficients, initialValues, count);
    const points = sequence.map((value, index) => ({ x: index, y: value }));

    setHtml("recurrenceChart", lineChartSvg([{ label: "Sequence", color: "#4a90e2", points }], { label: "recurrence sequence", minX: 0, maxX: Math.max(1, count - 1) }));
    setHtml("recurrenceStats", `
      <div class="tool-stat"><strong>Order</strong><span>${coefficients.length}</span></div>
      <div class="tool-stat"><strong>Terms shown</strong><span>${count}</span></div>
      <div class="tool-stat"><strong>Last term</strong><span>${formatNumber(sequence[sequence.length - 1], 6)}</span></div>
      <div class="tool-stat"><strong>Closed-form note</strong><span>${describeClosedForm(coefficients, initialValues)}</span></div>
    `);
    setHtml("recurrenceTerms", renderChipList(sequence.map((value, index) => `a(${index}) = ${formatNumber(value, 5)}`)));
  } catch (error) {
    setHtml("recurrenceStats", renderMessage(error.message, "error"));
    setHtml("recurrenceChart", "");
    setHtml("recurrenceTerms", "");
  }
}

document.addEventListener("DOMContentLoaded", () => {
  byId("runRecurrence").addEventListener("click", run);
  run();
});
