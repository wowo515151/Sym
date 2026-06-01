import { compileExpression, extractVariables, validateExpressionInput } from "./expr.js";

function factorial(n) {
  let value = 1;
  for (let i = 2; i <= n; i += 1) {
    value *= i;
  }
  return value;
}

export function partialDerivative(fn, point, variable, step = 1e-4) {
  const plus = { ...point, [variable]: point[variable] + step };
  const minus = { ...point, [variable]: point[variable] - step };
  return (fn(plus) - fn(minus)) / (2 * step);
}

export function secondPartialDerivative(fn, point, variableA, variableB, step = 1e-4) {
  if (variableA === variableB) {
    const plus = { ...point, [variableA]: point[variableA] + step };
    const minus = { ...point, [variableA]: point[variableA] - step };
    return (fn(plus) - 2 * fn(point) + fn(minus)) / (step * step);
  }
  const pp = { ...point, [variableA]: point[variableA] + step, [variableB]: point[variableB] + step };
  const pm = { ...point, [variableA]: point[variableA] + step, [variableB]: point[variableB] - step };
  const mp = { ...point, [variableA]: point[variableA] - step, [variableB]: point[variableB] + step };
  const mm = { ...point, [variableA]: point[variableA] - step, [variableB]: point[variableB] - step };
  return (fn(pp) - fn(pm) - fn(mp) + fn(mm)) / (4 * step * step);
}

export function gradient(fn, point, variables, step = 1e-4) {
  return variables.map((variable) => partialDerivative(fn, point, variable, step));
}

export function jacobian(functions, point, variables, step = 1e-4) {
  return functions.map((fn) => gradient(fn, point, variables, step));
}

export function hessian(fn, point, variables, step = 1e-4) {
  return variables.map((rowVar) =>
    variables.map((colVar) => secondPartialDerivative(fn, point, rowVar, colVar, step))
  );
}

export function directionalDerivative(fn, point, variables, direction, step = 1e-4) {
  if (direction.length !== variables.length) {
    throw new Error("Direction vector dimension must match the variable count");
  }
  const grad = gradient(fn, point, variables, step);
  const length = Math.hypot(...direction) || 1;
  const unit = direction.map((value) => value / length);
  return grad.reduce((sum, value, index) => sum + value * unit[index], 0);
}

export function nthDerivative1D(fn, x0, order, step = 1e-3) {
  if (order === 0) {
    return fn({ x: x0 });
  }
  const previous = (value) => nthDerivative1D(fn, value, order - 1, step / 1.4);
  return (previous(x0 + step) - previous(x0 - step)) / (2 * step);
}

export function taylorApproximation(fn, x0, order, sampleX) {
  let total = 0;
  for (let n = 0; n <= order; n += 1) {
    const derivative = nthDerivative1D(fn, x0, n);
    total += (derivative / factorial(n)) * ((sampleX - x0) ** n);
  }
  return total;
}

export function sampleScalarField(fn, xRange, yRange, steps = 24) {
  const values = [];
  for (let yi = 0; yi <= steps; yi += 1) {
    const y = yRange[0] + ((yRange[1] - yRange[0]) * yi) / steps;
    const row = [];
    for (let xi = 0; xi <= steps; xi += 1) {
      const x = xRange[0] + ((xRange[1] - xRange[0]) * xi) / steps;
      row.push({ x, y, value: fn({ x, y }) });
    }
    values.push(row);
  }
  return values;
}

export function createScalarFunction(expression, allowedVariables = ["x", "y", "z"]) {
  const ast = validateExpressionInput(expression, allowedVariables);
  const compiled = compileExpression(ast);
  return {
    ast,
    variables: extractVariables(ast),
    evaluate: (scope) => compiled.evaluate(scope)
  };
}
