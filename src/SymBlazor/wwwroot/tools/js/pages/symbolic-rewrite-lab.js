import { confluenceSummary, normalForms, parseRule, parseTerm, reachableTerms, rewriteWithStrategy, terminationSummary } from "../core/term-rewrite.js";
import { byId, escapeHtml, renderChipList, renderMessage, setHtml } from "../page-helpers.js";

function connectedComponents(nodes, edges) {
  const adjacency = new Map(nodes.map((node) => [node, new Set()]));
  edges.forEach((edge) => {
    adjacency.get(edge.source)?.add(edge.target);
    adjacency.get(edge.target)?.add(edge.source);
  });
  const seen = new Set();
  const groups = [];
  for (const node of nodes) {
    if (seen.has(node)) {
      continue;
    }
    const stack = [node];
    const component = [];
    seen.add(node);
    while (stack.length) {
      const current = stack.pop();
      component.push(current);
      adjacency.get(current)?.forEach((next) => {
        if (!seen.has(next)) {
          seen.add(next);
          stack.push(next);
        }
      });
    }
    groups.push(component.sort());
  }
  return groups;
}

function run() {
  try {
    const start = parseTerm(byId("rewriteStart").value);
    const rules = byId("rewriteRules").value.split("\n").map((line) => line.trim()).filter(Boolean).map(parseRule);
    if (!rules.length) {
      throw new Error("Enter at least one rewrite rule");
    }
    const outerTrace = rewriteWithStrategy(start, rules, "outermost", 8);
    const innerTrace = rewriteWithStrategy(start, rules, "innermost", 8);
    const graph = reachableTerms(start, rules, 4);
    const confluent = confluenceSummary(start, rules, 4);
    const termination = terminationSummary(start, rules, 5);
    const components = connectedComponents(graph.nodes, graph.edges);

    setHtml("rewriteStats", `
      <div class="tool-stat"><strong>Reachable terms</strong><span>${graph.nodes.length}</span></div>
      <div class="tool-stat"><strong>Rewrite edges</strong><span>${graph.edges.length}</span></div>
      <div class="tool-stat"><strong>Sample confluence</strong><span class="${confluent.appearsConfluentOnSample ? "tool-good" : "tool-warn"}">${confluent.appearsConfluentOnSample ? "One normal form found" : "Multiple normal forms found"}</span></div>
      <div class="tool-stat"><strong>Sample termination</strong><span class="${termination.likelyTerminatesOnSample ? "tool-good" : "tool-warn"}">${termination.likelyTerminatesOnSample ? "Looks terminating on sample" : "Needs closer inspection"}</span></div>
    `);
    setHtml("outerTrace", renderChipList(outerTrace));
    setHtml("innerTrace", renderChipList(innerTrace));
    setHtml("normalForms", renderChipList(normalForms(start, rules, 4)));
    setHtml("rewriteEdges", `<div class="tool-section-list">${graph.edges.map((edge) =>
      `<div class="tool-code">${escapeHtml(edge.source)} --[${escapeHtml(edge.rule)}]--> ${escapeHtml(edge.target)}</div>`).join("")}</div>`);
    setHtml("egraphClusters", `<div class="tool-section-list">${components.map((group, index) =>
      `<div><strong>EClass ${index + 1}</strong>${renderChipList(group)}</div>`).join("")}</div>`);
  } catch (error) {
    setHtml("rewriteStats", renderMessage(error.message, "error"));
    setHtml("outerTrace", "");
    setHtml("innerTrace", "");
    setHtml("normalForms", "");
    setHtml("rewriteEdges", "");
    setHtml("egraphClusters", "");
  }
}

document.addEventListener("DOMContentLoaded", () => {
  byId("runRewrite").addEventListener("click", run);
  run();
});
