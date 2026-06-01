import { createSeriesFunction, evaluateFourierSeries, evaluateTaylorSeries, fourierCoefficients, taylorCoefficients } from "../core/fourier.js";
import { byId, formatNumber, lineChartSvg, renderChipList, renderMessage, sampleRange, setHtml } from "../page-helpers.js";

function run() {
  try {
    const expression = byId("seriesExpression").value;
    const harmonics = Number(byId("fourierHarmonics").value);
    const center = Number(byId("taylorCenter").value);
    const order = Number(byId("taylorOrder").value);
    if (!Number.isInteger(harmonics) || harmonics < 1) {
      throw new Error("Fourier harmonics must be a positive integer");
    }
    if (!Number.isInteger(order) || order < 1) {
      throw new Error("Taylor order must be a positive integer");
    }
    const fn = createSeriesFunction(expression).evaluate;
    const coeffs = fourierCoefficients(fn, harmonics);
    const taylor = taylorCoefficients(fn, center, order);

    const chartPoints = sampleRange(-Math.PI, Math.PI, 240, (x) => fn({ x }));
    const fourierPoints = sampleRange(-Math.PI, Math.PI, 240, (x) => evaluateFourierSeries(coeffs, x));
    const taylorPoints = sampleRange(center - 2, center + 2, 160, (x) => evaluateTaylorSeries(fn, center, order, x));

    setHtml("seriesChart", lineChartSvg([
      { label: "f(x)", color: "#89d1b6", points: chartPoints },
      { label: "Fourier partial sum", color: "#4a90e2", points: fourierPoints },
      { label: "Taylor near center", color: "#f3cb7a", points: taylorPoints }
    ], { label: "series and fourier chart", minX: -Math.PI, maxX: Math.PI }));

    setHtml("seriesStats", `
      <div class="tool-stat"><strong>Taylor center</strong><span>${formatNumber(center)}</span></div>
      <div class="tool-stat"><strong>Taylor order</strong><span>${order}</span></div>
      <div class="tool-stat"><strong>Fourier harmonics</strong><span>${harmonics}</span></div>
      <div class="tool-stat"><strong>a0 / 2</strong><span>${formatNumber(coeffs.a0 / 2, 6)}</span></div>
    `);

    setHtml("fourierCoeffs", renderChipList(coeffs.cosine.map((value, index) => `a${index + 1} = ${formatNumber(value, 5)}`).concat(
      coeffs.sine.map((value, index) => `b${index + 1} = ${formatNumber(value, 5)}`)
    )));
    setHtml("taylorCoeffs", renderChipList(taylor.map((value, index) => `c${index} = ${formatNumber(value, 5)}`)));
  } catch (error) {
    setHtml("seriesStats", renderMessage(error.message, "error"));
    setHtml("seriesChart", "");
    setHtml("fourierCoeffs", "");
    setHtml("taylorCoeffs", "");
  }
}

document.addEventListener("DOMContentLoaded", () => {
  byId("runSeries").addEventListener("click", run);
  run();
});
