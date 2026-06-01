import { createScalarFunction, nthDerivative1D, taylorApproximation } from "./numeric-calculus.js";

export function simpsonIntegrate(fn, a, b, intervals = 400) {
  const n = intervals % 2 === 0 ? intervals : intervals + 1;
  const h = (b - a) / n;
  let total = fn(a) + fn(b);
  for (let i = 1; i < n; i += 1) {
    const x = a + i * h;
    total += fn(x) * (i % 2 === 0 ? 2 : 4);
  }
  return (total * h) / 3;
}

export function fourierCoefficients(fn, harmonics, interval = Math.PI) {
  const scale = 1 / interval;
  const a0 = scale * simpsonIntegrate((x) => fn({ x }), -interval, interval);
  const cosine = [];
  const sine = [];
  for (let n = 1; n <= harmonics; n += 1) {
    cosine.push(scale * simpsonIntegrate((x) => fn({ x }) * Math.cos((n * Math.PI * x) / interval), -interval, interval));
    sine.push(scale * simpsonIntegrate((x) => fn({ x }) * Math.sin((n * Math.PI * x) / interval), -interval, interval));
  }
  return { a0, cosine, sine, interval };
}

export function evaluateFourierSeries(coeffs, x) {
  let total = coeffs.a0 / 2;
  for (let n = 1; n <= coeffs.cosine.length; n += 1) {
    total += coeffs.cosine[n - 1] * Math.cos((n * Math.PI * x) / coeffs.interval);
    total += coeffs.sine[n - 1] * Math.sin((n * Math.PI * x) / coeffs.interval);
  }
  return total;
}

export function createSeriesFunction(expression) {
  return createScalarFunction(expression, ["x"]);
}

export function taylorCoefficients(fn, x0, order) {
  const coefficients = [];
  for (let n = 0; n <= order; n += 1) {
    coefficients.push(nthDerivative1D(fn, x0, n) / factorial(n));
  }
  return coefficients;
}

export function evaluateTaylorSeries(fn, x0, order, x) {
  return taylorApproximation(fn, x0, order, x);
}

function factorial(n) {
  let value = 1;
  for (let i = 2; i <= n; i += 1) {
    value *= i;
  }
  return value;
}
