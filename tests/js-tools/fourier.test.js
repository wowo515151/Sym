import test from "node:test";
import assert from "node:assert/strict";
import { createSeriesFunction, evaluateFourierSeries, evaluateTaylorSeries, fourierCoefficients, taylorCoefficients } from "../../src/SymBlazor/wwwroot/tools/js/core/fourier.js";

test("Fourier coefficients recover sine signal", () => {
  const fn = createSeriesFunction("sin(x)").evaluate;
  const coeffs = fourierCoefficients(fn, 3);
  assert.ok(Math.abs(coeffs.sine[0] - 1) < 1e-2);
  assert.ok(Math.abs(coeffs.cosine[0]) < 1e-2);
  assert.ok(Math.abs(evaluateFourierSeries(coeffs, 0.7) - Math.sin(0.7)) < 2e-2);
});

test("Taylor coefficients approximate exponential near zero", () => {
  const fn = createSeriesFunction("exp(x)").evaluate;
  const coeffs = taylorCoefficients(fn, 0, 4);
  assert.ok(Math.abs(coeffs[0] - 1) < 1e-3);
  assert.ok(Math.abs(coeffs[1] - 1) < 1e-2);
  assert.ok(Math.abs(evaluateTaylorSeries(fn, 0, 4, 0.2) - Math.exp(0.2)) < 3e-2);
});

test("series parser rejects unsupported functions", () => {
  assert.throws(() => createSeriesFunction("foo(x)"), /supported/);
});
