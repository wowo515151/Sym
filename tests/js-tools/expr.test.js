import test from "node:test";
import assert from "node:assert/strict";
import { compileExpression, extractVariables, parseExpression, validateExpressionInput } from "../../src/SymBlazor/wwwroot/tools/js/core/expr.js";

test("expression parser evaluates arithmetic with precedence", () => {
  const compiled = compileExpression("2 + 3 * x^2");
  assert.equal(compiled.evaluate({ x: 4 }), 50);
});

test("expression parser handles supported functions", () => {
  const compiled = compileExpression("sin(pi / 2) + log(e)");
  assert.ok(Math.abs(compiled.evaluate({}) - 2) < 1e-10);
});

test("expression parser extracts variables", () => {
  const ast = parseExpression("x + y * z");
  assert.deepEqual(extractVariables(ast), ["x", "y", "z"]);
});

test("expression validation rejects unsupported functions", () => {
  assert.throws(() => validateExpressionInput("foo(x)"), /not supported/);
});
