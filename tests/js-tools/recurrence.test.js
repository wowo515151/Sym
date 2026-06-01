import test from "node:test";
import assert from "node:assert/strict";
import { characteristicRoots, describeClosedForm, generateSequence, syntheticRecurrence } from "../../src/SymBlazor/wwwroot/tools/js/core/recurrence.js";
import { randomRecurrence } from "./helpers/generators.js";

test("recurrence generator handles Fibonacci-like sequence", () => {
  const seq = generateSequence([1, 1], [0, 1], 8);
  assert.deepEqual(seq, [0, 1, 1, 2, 3, 5, 8, 13]);
});

test("characteristic roots and closed form describe first-order recurrence", () => {
  assert.deepEqual(characteristicRoots([2]), [2]);
  assert.match(describeClosedForm([2], [3]), /3/);
});

test("synthetic recurrence data is internally consistent", () => {
  const sample = randomRecurrence(55, 2);
  const info = syntheticRecurrence(sample.coefficients, sample.initialValues, 8);
  assert.equal(info.sequence.length, 8);
});

test("recurrence generator validates order and count", () => {
  assert.throws(() => generateSequence([], [1], 3), /coefficient/);
  assert.throws(() => generateSequence([1], [1], 0), /positive integer/);
});
