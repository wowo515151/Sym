import test from "node:test";
import assert from "node:assert/strict";
import { confluenceSummary, formatTerm, normalForms, parseRule, parseTerm, reachableTerms, rewriteWithStrategy, terminationSummary, unify } from "../../src/SymBlazor/wwwroot/tools/js/core/term-rewrite.js";

test("term parser and formatter round-trip", () => {
  const term = parseTerm("f(g(a), ?x)");
  assert.equal(formatTerm(term), "f(g(a), ?x)");
});

test("unification binds pattern variables", () => {
  const result = unify(parseTerm("f(?x, a)"), parseTerm("f(b, a)"));
  assert.equal(formatTerm(result["?x"]), "b");
});

test("unification is symmetric across variables and uses occurs check", () => {
  const result = unify(parseTerm("?x"), parseTerm("f(a)"));
  assert.equal(formatTerm(result["?x"]), "f(a)");
  assert.equal(unify(parseTerm("?x"), parseTerm("f(?x)")), null);
});

test("rewrite graph reaches expected normal form", () => {
  const start = parseTerm("add(zero, x)");
  const rules = [parseRule("add(zero, ?x) -> ?x")];
  const forms = normalForms(start, rules, 2);
  assert.deepEqual(forms, ["x"]);
});

test("rewrite strategies produce traces and summaries", () => {
  const start = parseTerm("add(zero, add(zero, x))");
  const rules = [parseRule("add(zero, ?x) -> ?x")];
  const trace = rewriteWithStrategy(start, rules, "innermost", 4);
  assert.ok(trace.length >= 2);
  const graph = reachableTerms(start, rules, 3);
  assert.ok(graph.nodes.length >= 2);
  assert.equal(confluenceSummary(start, rules, 3).appearsConfluentOnSample, true);
  assert.equal(terminationSummary(start, rules, 3).likelyTerminatesOnSample, true);
});
