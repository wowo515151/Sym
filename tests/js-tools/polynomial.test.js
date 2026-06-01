import test from "node:test";
import assert from "node:assert/strict";
import { coefficientsFromRoots, derivativeCoefficients, evaluatePolynomial, polynomialFeatures, realRoots } from "../../src/SymBlazor/wwwroot/tools/js/core/polynomial.js";
import { randomPolynomialRoots } from "./helpers/generators.js";

test("polynomial evaluation and derivative coefficients work", () => {
  assert.equal(evaluatePolynomial([1, -3, 2], 2), 0);
  assert.deepEqual(derivativeCoefficients([3, 0, -4]), [6, 0]);
});

test("real roots recover simple factorization", () => {
  const roots = realRoots([1, -6, 11, -6]);
  assert.deepEqual(roots.map((value) => Math.round(value)), [1, 2, 3]);
});

test("synthetic polynomials built from roots vanish at those roots", () => {
  const syntheticRoots = randomPolynomialRoots(101, 4);
  const coefficients = coefficientsFromRoots(syntheticRoots);
  syntheticRoots.forEach((root) => {
    assert.ok(Math.abs(evaluatePolynomial(coefficients, root)) < 1e-7);
  });
});

test("polynomial features include critical points and y intercept", () => {
  const features = polynomialFeatures([1, 0, -4]);
  assert.equal(features.yIntercept, -4);
  assert.ok(features.criticalPoints.some((value) => Math.abs(value) < 1e-6));
});
