import { formatTerm, match, parseTerm, unify } from "../core/term-rewrite.js";
import { escapeHtml, renderChipList, renderMessage, setHtml, byId } from "../page-helpers.js";

function renderSubstitutions(substitutions) {
  const entries = Object.entries(substitutions ?? {});
  if (!entries.length) {
    return `<p>No substitutions were needed.</p>`;
  }
  return renderChipList(entries.map(([name, value]) => `${name} = ${formatTerm(value)}`));
}

function run() {
  try {
    const pattern = parseTerm(byId("unifyPattern").value);
    const target = parseTerm(byId("unifyTarget").value);
    const unification = unify(pattern, target);
    const matching = match(pattern, target);

    setHtml("unifyStats", `
      <div class="tool-stat"><strong>Pattern</strong><span>${escapeHtml(formatTerm(pattern))}</span></div>
      <div class="tool-stat"><strong>Target</strong><span>${escapeHtml(formatTerm(target))}</span></div>
      <div class="tool-stat"><strong>Unification</strong><span class="${unification ? "tool-good" : "tool-bad"}">${unification ? "Succeeded" : "Failed"}</span></div>
      <div class="tool-stat"><strong>Pattern match</strong><span class="${matching ? "tool-good" : "tool-bad"}">${matching ? "Succeeded" : "Failed"}</span></div>
    `);
    setHtml("unifyResult", unification ? renderSubstitutions(unification) : `<p>These terms do not unify under the current simple first-order rules.</p>`);
  } catch (error) {
    setHtml("unifyStats", renderMessage(error.message, "error"));
    setHtml("unifyResult", "");
  }
}

document.addEventListener("DOMContentLoaded", () => {
  byId("runUnification").addEventListener("click", run);
  run();
});
