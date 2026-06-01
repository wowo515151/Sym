const FUNCTION_NAMES = new Set(["sin", "cos", "tan", "exp", "log", "sqrt", "abs"]);

export function tokenizeExpression(input) {
  const tokens = [];
  let i = 0;
  while (i < input.length) {
    const ch = input[i];
    if (/\s/.test(ch)) {
      i += 1;
      continue;
    }
    if (/[0-9.]/.test(ch)) {
      let j = i + 1;
      while (j < input.length && /[0-9.]/.test(input[j])) {
        j += 1;
      }
      tokens.push({ type: "number", value: Number.parseFloat(input.slice(i, j)) });
      i = j;
      continue;
    }
    if (/[A-Za-z_]/.test(ch)) {
      let j = i + 1;
      while (j < input.length && /[A-Za-z0-9_]/.test(input[j])) {
        j += 1;
      }
      const name = input.slice(i, j);
      tokens.push({ type: "identifier", value: name });
      i = j;
      continue;
    }
    if ("+-*/^(),".includes(ch)) {
      tokens.push({ type: ch, value: ch });
      i += 1;
      continue;
    }
    throw new Error(`Unexpected character '${ch}' at position ${i}`);
  }
  return tokens;
}

export function parseExpression(input) {
  const tokens = tokenizeExpression(input);
  let position = 0;

  function peek() {
    return tokens[position] ?? null;
  }

  function consume(type) {
    const token = peek();
    if (!token || token.type !== type) {
      throw new Error(`Expected token '${type}'`);
    }
    position += 1;
    return token;
  }

  function parsePrimary() {
    const token = peek();
    if (!token) {
      throw new Error("Unexpected end of expression");
    }
    if (token.type === "number") {
      position += 1;
      return { type: "const", value: token.value };
    }
    if (token.type === "identifier") {
      position += 1;
      if (peek()?.type === "(") {
        consume("(");
        const args = [];
        if (peek()?.type !== ")") {
          do {
            args.push(parseAddSub());
            if (peek()?.type !== ",") {
              break;
            }
            consume(",");
          } while (true);
        }
        consume(")");
        return { type: "call", name: token.value, args };
      }
      if (token.value === "pi") {
        return { type: "const", value: Math.PI };
      }
      if (token.value === "e") {
        return { type: "const", value: Math.E };
      }
      return { type: "var", name: token.value };
    }
    if (token.type === "(") {
      consume("(");
      const node = parseAddSub();
      consume(")");
      return node;
    }
    if (token.type === "-") {
      consume("-");
      return { type: "unary", op: "-", arg: parsePrimary() };
    }
    throw new Error(`Unexpected token '${token.type}'`);
  }

  function parsePow() {
    let node = parsePrimary();
    while (peek()?.type === "^") {
      consume("^");
      node = { type: "binary", op: "^", left: node, right: parsePrimary() };
    }
    return node;
  }

  function parseMulDiv() {
    let node = parsePow();
    while (peek() && (peek().type === "*" || peek().type === "/")) {
      const op = consume(peek().type).type;
      node = { type: "binary", op, left: node, right: parsePow() };
    }
    return node;
  }

  function parseAddSub() {
    let node = parseMulDiv();
    while (peek() && (peek().type === "+" || peek().type === "-")) {
      const op = consume(peek().type).type;
      node = { type: "binary", op, left: node, right: parseMulDiv() };
    }
    return node;
  }

  const ast = parseAddSub();
  if (position !== tokens.length) {
    throw new Error("Unexpected trailing tokens");
  }
  return ast;
}

export function evaluateExpression(ast, scope = {}) {
  switch (ast.type) {
    case "const":
      return ast.value;
    case "var":
      if (!(ast.name in scope)) {
        throw new Error(`Missing variable '${ast.name}'`);
      }
      return scope[ast.name];
    case "unary":
      return -evaluateExpression(ast.arg, scope);
    case "binary": {
      const left = evaluateExpression(ast.left, scope);
      const right = evaluateExpression(ast.right, scope);
      switch (ast.op) {
        case "+":
          return left + right;
        case "-":
          return left - right;
        case "*":
          return left * right;
        case "/":
          return left / right;
        case "^":
          return left ** right;
        default:
          throw new Error(`Unknown binary operator '${ast.op}'`);
      }
    }
    case "call": {
      const args = ast.args.map((arg) => evaluateExpression(arg, scope));
      switch (ast.name) {
        case "sin":
          return Math.sin(args[0]);
        case "cos":
          return Math.cos(args[0]);
        case "tan":
          return Math.tan(args[0]);
        case "exp":
          return Math.exp(args[0]);
        case "log":
          return Math.log(args[0]);
        case "sqrt":
          return Math.sqrt(args[0]);
        case "abs":
          return Math.abs(args[0]);
        default:
          throw new Error(`Unknown function '${ast.name}'`);
      }
    }
    default:
      throw new Error(`Unknown AST node type '${ast.type}'`);
  }
}

export function compileExpression(input) {
  const ast = typeof input === "string" ? parseExpression(input) : input;
  return {
    ast,
    evaluate(scope = {}) {
      return evaluateExpression(ast, scope);
    }
  };
}

export function extractVariables(ast) {
  const names = new Set();
  walkExpression(ast, (node) => {
    if (node.type === "var") {
      names.add(node.name);
    }
  });
  return [...names].sort();
}

export function walkExpression(ast, visitor) {
  visitor(ast);
  if (ast.type === "unary") {
    walkExpression(ast.arg, visitor);
  } else if (ast.type === "binary") {
    walkExpression(ast.left, visitor);
    walkExpression(ast.right, visitor);
  } else if (ast.type === "call") {
    ast.args.forEach((arg) => walkExpression(arg, visitor));
  }
}

export function validateExpressionInput(input, allowedVariables = null) {
  const ast = parseExpression(input);
  walkExpression(ast, (node) => {
    if (node.type === "call" && !FUNCTION_NAMES.has(node.name)) {
      throw new Error(`Function '${node.name}' is not supported`);
    }
  });
  if (allowedVariables) {
    const vars = extractVariables(ast);
    for (const variable of vars) {
      if (!allowedVariables.includes(variable)) {
        throw new Error(`Variable '${variable}' is not allowed here`);
      }
    }
  }
  return ast;
}
