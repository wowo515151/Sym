import test from "node:test";
import assert from "node:assert/strict";
import { createScalarFunction, directionalDerivative, gradient, hessian, jacobian, nthDerivative1D, taylorApproximation } from "../../src/SymBlazor/wwwroot/tools/js/core/numeric-calculus.js";

test("gradient matches polynomial derivatives numerically", () => {
  const fn = createScalarFunction("x^2 + x*y + y^2").evaluate;
  const point = { x: 2, y: -1 };
  const grad = gradient(fn, point, ["x", "y"]);
  assert.ok(Math.abs(grad[0] - 3) < 1e-3);
  assert.ok(Math.abs(grad[1] - 0) < 1e-3);
});

test("jacobian matches simple vector function", () => {
  const f1 = createScalarFunction("x*y").evaluate;
  const f2 = createScalarFunction("x + y^2").evaluate;
  const J = jacobian([f1, f2], { x: 3, y: 2 }, ["x", "y"]);
  assert.ok(Math.abs(J[0][0] - 2) < 1e-3);
  assert.ok(Math.abs(J[0][1] - 3) < 1e-3);
  assert.ok(Math.abs(J[1][0] - 1) < 1e-3);
  assert.ok(Math.abs(J[1][1] - 4) < 1e-3);
});

test("hessian matches quadratic form", () => {
  const fn = createScalarFunction("x^2 + 3*x*y + 2*y^2").evaluate;
  const H = hessian(fn, { x: 1, y: 2 }, ["x", "y"]);
  assert.ok(Math.abs(H[0][0] - 2) < 1e-2);
  assert.ok(Math.abs(H[0][1] - 3) < 1e-2);
  assert.ok(Math.abs(H[1][0] - 3) < 1e-2);
  assert.ok(Math.abs(H[1][1] - 4) < 1e-2);
});

test("directional derivative uses normalized direction", () => {
  const fn = createScalarFunction("x^2 + y^2").evaluate;
  const dd = directionalDerivative(fn, { x: 1, y: 0 }, ["x", "y"], [10, 0]);
  assert.ok(Math.abs(dd - 2) < 1e-3);
});

test("directional derivative rejects mismatched dimensions", () => {
  const fn = createScalarFunction("x^2 + y^2").evaluate;
  assert.throws(() => directionalDerivative(fn, { x: 0, y: 0 }, ["x", "y"], [1]), /dimension/);
});

test("nth derivative and taylor approximation recover low-order polynomial", () => {
  const fn = createScalarFunction("1 + 2*x + 3*x^2").evaluate;
  assert.ok(Math.abs(nthDerivative1D(fn, 0, 2) - 6) < 1e-2);
  assert.ok(Math.abs(taylorApproximation(fn, 0, 2, 0.4) - fn({ x: 0.4 })) < 1e-2);
});
