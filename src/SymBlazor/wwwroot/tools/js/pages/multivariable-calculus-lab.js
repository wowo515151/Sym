import { createScalarFunction, directionalDerivative, gradient, hessian, jacobian, sampleScalarField } from "../core/numeric-calculus.js";
import { byId, drawVectorField, formatNumber, renderMatrixTable, renderMessage, sampleRange, setHtml, lineChartSvg } from "../page-helpers.js";

function parseVector(text) {
  const values = text.split(",").map((value) => Number(value.trim()));
  if (values.some((value) => Number.isNaN(value))) {
    throw new Error("Direction vector must be a comma-separated list of numbers");
  }
  return values;
}

function run() {
  try {
    const scalarExpression = byId("scalarExpression").value;
    const vectorExpressions = byId("vectorExpressions").value.split("\n").map((line) => line.trim()).filter(Boolean);
    const point = { x: Number(byId("pointX").value), y: Number(byId("pointY").value) };
    const direction = parseVector(byId("directionVector").value);

    const scalar = createScalarFunction(scalarExpression, ["x", "y"]);
    const scalarValue = scalar.evaluate(point);
    const grad = gradient(scalar.evaluate, point, ["x", "y"]);
    const H = hessian(scalar.evaluate, point, ["x", "y"]);
    const dd = directionalDerivative(scalar.evaluate, point, ["x", "y"], direction);

    const vectorFns = vectorExpressions.map((expression) => createScalarFunction(expression, ["x", "y"]).evaluate);
    const J = vectorFns.length ? jacobian(vectorFns, point, ["x", "y"]) : [];

    const fieldSamples = sampleScalarField(scalar.evaluate, [-3, 3], [-3, 3], 8).flat().map((sample) => {
      const sampleGrad = gradient(scalar.evaluate, { x: sample.x, y: sample.y }, ["x", "y"]);
      const length = Math.hypot(sampleGrad[0], sampleGrad[1]) || 1;
      return { x: sample.x, y: sample.y, dx: sampleGrad[0] / length, dy: sampleGrad[1] / length };
    });
    drawVectorField(byId("gradientCanvas"), fieldSamples, point);

    const sectionCurve = sampleRange(-3, 3, 160, (x) => scalar.evaluate({ x, y: point.y }));
    setHtml("sectionChart", lineChartSvg([
      { label: `f(x, ${formatNumber(point.y)})`, color: "#4a90e2", points: sectionCurve }
    ], { label: "section curve" }));

    setHtml("calculusStats", `
      <div class="tool-stat"><strong>f(x, y)</strong><span>${formatNumber(scalarValue, 6)}</span></div>
      <div class="tool-stat"><strong>Gradient</strong><span>[${grad.map((value) => formatNumber(value, 5)).join(", ")}]</span></div>
      <div class="tool-stat"><strong>Directional Derivative</strong><span>${formatNumber(dd, 6)}</span></div>
      <div class="tool-stat"><strong>Point</strong><span>(${formatNumber(point.x)}, ${formatNumber(point.y)})</span></div>
    `);

    setHtml("jacobianOutput", J.length ? renderMatrixTable(J, 5) : `<p>No vector-valued outputs were provided.</p>`);
    setHtml("hessianOutput", renderMatrixTable(H, 5));
  } catch (error) {
    setHtml("calculusStats", renderMessage(error.message, "error"));
    setHtml("jacobianOutput", "");
    setHtml("hessianOutput", "");
    setHtml("sectionChart", "");
  }
}

document.addEventListener("DOMContentLoaded", () => {
  byId("runCalculus").addEventListener("click", run);
  run();
});
