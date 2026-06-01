import { polynomialFeatures, evaluatePolynomial } from "../core/polynomial.js";
import { byId, formatNumber, lineChartSvg, renderChipList, renderMessage, setHtml, sampleRange } from "../page-helpers.js";

function parseCoefficients() {
  const coefficients = byId("coefficients").value.split(",").map((value) => Number(value.trim())).filter((value) => !Number.isNaN(value));
  if (!coefficients.length) {
    throw new Error("Enter at least one polynomial coefficient");
  }
  return coefficients;
}

function run() {
  try {
    const coefficients = parseCoefficients();
    const features = polynomialFeatures(coefficients);
    const points = sampleRange(-6, 6, 220, (x) => evaluatePolynomial(coefficients, x));

    setHtml("polynomialChart", lineChartSvg([
      { label: "Polynomial", color: "#89d1b6", points }
    ], { label: "polynomial curve" }));

    setHtml("polynomialStats", `
      <div class="tool-stat"><strong>Degree</strong><span>${features.degree}</span></div>
      <div class="tool-stat"><strong>y-intercept</strong><span>${formatNumber(features.yIntercept, 6)}</span></div>
      <div class="tool-stat"><strong>Real roots</strong><span>${features.realRoots.length}</span></div>
      <div class="tool-stat"><strong>Critical points</strong><span>${features.criticalPoints.length}</span></div>
    `);

    setHtml("polynomialRoots", renderChipList(features.roots.map((root) =>
      Math.abs(root.im) < 1e-7
        ? `x = ${formatNumber(root.re, 5)}`
        : `${formatNumber(root.re, 5)} ${root.im >= 0 ? "+" : "-"} ${formatNumber(Math.abs(root.im), 5)}i`
    )));

    setHtml("criticalPoints", features.criticalPoints.length
      ? renderChipList(features.criticalPoints.map((value) => `x = ${formatNumber(value, 5)}`))
      : `<p>No real critical points were detected.</p>`);
  } catch (error) {
    setHtml("polynomialStats", renderMessage(error.message, "error"));
    setHtml("polynomialChart", "");
    setHtml("polynomialRoots", "");
    setHtml("criticalPoints", "");
  }
}

document.addEventListener("DOMContentLoaded", () => {
  byId("runPolynomial").addEventListener("click", run);
  run();
});
