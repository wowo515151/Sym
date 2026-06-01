export function tokenizeTerm(input) {
  const tokens = [];
  let i = 0;
  while (i < input.length) {
    const ch = input[i];
    if (/\s/.test(ch)) {
      i += 1;
      continue;
    }
    if (/[A-Za-z0-9_?]/.test(ch)) {
      let j = i + 1;
      while (j < input.length && /[A-Za-z0-9_?]/.test(input[j])) {
        j += 1;
      }
      tokens.push({ type: "identifier", value: input.slice(i, j) });
      i = j;
      continue;
    }
    if ("(),".includes(ch)) {
      tokens.push({ type: ch, value: ch });
      i += 1;
      continue;
    }
    throw new Error(`Unexpected character '${ch}' in term`);
  }
  return tokens;
}

export function parseTerm(input) {
  const tokens = tokenizeTerm(input);
  let position = 0;
  function peek() {
    return tokens[position] ?? null;
  }
  function consume(type) {
    const token = peek();
    if (!token || token.type !== type) {
      throw new Error(`Expected '${type}'`);
    }
    position += 1;
    return token;
  }
  function parseNode() {
    const token = consume("identifier");
    const node = isPatternVariable(token.value)
      ? { type: "var", name: token.value }
      : { type: "term", name: token.value, args: [] };
    if (node.type === "term" && peek()?.type === "(") {
      consume("(");
      if (peek()?.type !== ")") {
        do {
          node.args.push(parseNode());
          if (peek()?.type !== ",") {
            break;
          }
          consume(",");
        } while (true);
      }
      consume(")");
    }
    return node;
  }
  const term = parseNode();
  if (position !== tokens.length) {
    throw new Error("Unexpected trailing term tokens");
  }
  return term;
}

export function isPatternVariable(name) {
  return name.startsWith("?");
}

export function formatTerm(term) {
  if (term.type === "var") {
    return term.name;
  }
  if (!term.args.length) {
    return term.name;
  }
  return `${term.name}(${term.args.map(formatTerm).join(", ")})`;
}

export function cloneTerm(term) {
  return JSON.parse(JSON.stringify(term));
}

function applySubstitutionsToTerm(term, substitutions) {
  if (term.type === "var" && substitutions[term.name]) {
    return applySubstitutionsToTerm(substitutions[term.name], substitutions);
  }
  if (term.type === "term") {
    return {
      type: "term",
      name: term.name,
      args: term.args.map((arg) => applySubstitutionsToTerm(arg, substitutions))
    };
  }
  return cloneTerm(term);
}

function occursIn(variableName, term, substitutions) {
  const resolved = applySubstitutionsToTerm(term, substitutions);
  if (resolved.type === "var") {
    return resolved.name === variableName;
  }
  return resolved.args.some((arg) => occursIn(variableName, arg, substitutions));
}

function bindVariable(variable, term, substitutions) {
  const resolvedTerm = applySubstitutionsToTerm(term, substitutions);
  if (resolvedTerm.type === "var" && resolvedTerm.name === variable.name) {
    return substitutions;
  }
  if (occursIn(variable.name, resolvedTerm, substitutions)) {
    return null;
  }
  return { ...substitutions, [variable.name]: cloneTerm(resolvedTerm) };
}

export function unify(pattern, target, substitutions = {}) {
  const left = applySubstitutionsToTerm(pattern, substitutions);
  const right = applySubstitutionsToTerm(target, substitutions);
  if (left.type === "var") {
    return bindVariable(left, right, substitutions);
  }
  if (right.type === "var") {
    return bindVariable(right, left, substitutions);
  }
  if (left.name !== right.name || left.args.length !== right.args.length) {
    return null;
  }
  let current = { ...substitutions };
  for (let index = 0; index < left.args.length; index += 1) {
    const next = unify(left.args[index], right.args[index], current);
    if (!next) {
      return null;
    }
    current = next;
  }
  return current;
}

export function substitute(term, substitutions) {
  return applySubstitutionsToTerm(term, substitutions);
}

export function match(pattern, target) {
  return unify(pattern, target, {});
}

export function applyRuleOnce(term, rule) {
  const substitution = match(rule.left, term);
  if (substitution) {
    return [substitute(rule.right, substitution)];
  }
  if (term.type === "term") {
    const rewrites = [];
    term.args.forEach((arg, index) => {
      applyRuleOnce(arg, rule).forEach((rewrittenArg) => {
        const next = cloneTerm(term);
        next.args[index] = rewrittenArg;
        rewrites.push(next);
      });
    });
    return rewrites;
  }
  return [];
}

export function parseRule(input) {
  const [left, right] = input.split("->").map((part) => part.trim());
  if (!left || !right) {
    throw new Error("Rules must have the form left -> right");
  }
  return { left: parseTerm(left), right: parseTerm(right), source: input.trim() };
}

export function reachableTerms(startTerm, rules, depthLimit = 4) {
  const frontier = [{ term: startTerm, depth: 0 }];
  const seen = new Set([formatTerm(startTerm)]);
  const edges = [];
  while (frontier.length) {
    const current = frontier.shift();
    if (current.depth >= depthLimit) {
      continue;
    }
    for (const rule of rules) {
      for (const nextTerm of applyRuleOnce(current.term, rule)) {
        const source = formatTerm(current.term);
        const target = formatTerm(nextTerm);
        edges.push({ source, target, rule: rule.source });
        if (!seen.has(target)) {
          seen.add(target);
          frontier.push({ term: nextTerm, depth: current.depth + 1 });
        }
      }
    }
  }
  return { nodes: [...seen], edges };
}

export function normalForms(startTerm, rules, depthLimit = 5) {
  const graph = reachableTerms(startTerm, rules, depthLimit);
  const outgoing = new Map();
  graph.edges.forEach((edge) => {
    if (!outgoing.has(edge.source)) {
      outgoing.set(edge.source, []);
    }
    outgoing.get(edge.source).push(edge.target);
  });
  return graph.nodes.filter((node) => !outgoing.has(node));
}

export function rewriteWithStrategy(startTerm, rules, strategy = "outermost", maxSteps = 8) {
  let current = cloneTerm(startTerm);
  const trace = [formatTerm(current)];
  for (let step = 0; step < maxSteps; step += 1) {
    const next = strategy === "innermost"
      ? innermostRewrite(current, rules)
      : outermostRewrite(current, rules);
    if (!next) {
      break;
    }
    current = next;
    trace.push(formatTerm(current));
  }
  return trace;
}

function outermostRewrite(term, rules) {
  for (const rule of rules) {
    const rewrites = applyRuleOnce(term, rule);
    if (rewrites.length) {
      return rewrites[0];
    }
  }
  return null;
}

function innermostRewrite(term, rules) {
  if (term.type === "term") {
    for (let i = 0; i < term.args.length; i += 1) {
      const rewrittenChild = innermostRewrite(term.args[i], rules);
      if (rewrittenChild) {
        const next = cloneTerm(term);
        next.args[i] = rewrittenChild;
        return next;
      }
    }
  }
  return outermostRewrite(term, rules);
}

export function confluenceSummary(startTerm, rules, depthLimit = 4) {
  const forms = normalForms(startTerm, rules, depthLimit);
  return {
    normalForms: forms,
    appearsConfluentOnSample: forms.length <= 1
  };
}

export function terminationSummary(startTerm, rules, depthLimit = 6) {
  const graph = reachableTerms(startTerm, rules, depthLimit);
  const hasSelfLoop = graph.edges.some((edge) => edge.source === edge.target);
  return {
    exploredNodes: graph.nodes.length,
    exploredEdges: graph.edges.length,
    likelyTerminatesOnSample: !hasSelfLoop && graph.nodes.length < 60
  };
}
