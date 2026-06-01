function complex(re, im = 0) {
  return { re, im };
}

function cAdd(a, b) {
  return complex(a.re + b.re, a.im + b.im);
}

function cSub(a, b) {
  return complex(a.re - b.re, a.im - b.im);
}

function cMul(a, b) {
  return complex(a.re * b.re - a.im * b.im, a.re * b.im + a.im * b.re);
}

function cDiv(a, b) {
  const denom = b.re * b.re + b.im * b.im;
  return complex((a.re * b.re + a.im * b.im) / denom, (a.im * b.re - a.re * b.im) / denom);
}

function cAbs(a) {
  return Math.hypot(a.re, a.im);
}

export function normalizeCoefficients(coefficients) {
  const trimmed = [...coefficients];
  while (trimmed.length > 1 && Math.abs(trimmed[0]) < 1e-12) {
    trimmed.shift();
  }
  return trimmed;
}

export function evaluatePolynomial(coefficients, x) {
  return coefficients.reduce((acc, coefficient) => acc * x + coefficient, 0);
}

export function derivativeCoefficients(coefficients) {
  const degree = coefficients.length - 1;
  if (degree <= 0) {
    return [0];
  }
  return coefficients.slice(0, -1).map((coefficient, index) => coefficient * (degree - index));
}

export function durandKerner(coefficients, maxIterations = 200, tolerance = 1e-10) {
  const normalized = normalizeCoefficients(coefficients);
  const degree = normalized.length - 1;
  if (degree <= 0) {
    return [];
  }
  const leading = normalized[0];
  const monic = normalized.map((coefficient) => coefficient / leading);
  const roots = Array.from({ length: degree }, (_, index) => {
    const angle = (2 * Math.PI * index) / degree;
    return complex(Math.cos(angle), Math.sin(angle));
  });

  function evaluateComplex(z) {
    return monic.reduce((acc, coefficient) => cAdd(cMul(acc, z), complex(coefficient, 0)), complex(0, 0));
  }

  for (let iteration = 0; iteration < maxIterations; iteration += 1) {
    let converged = true;
    for (let i = 0; i < degree; i += 1) {
      let denom = complex(1, 0);
      for (let j = 0; j < degree; j += 1) {
        if (i !== j) {
          denom = cMul(denom, cSub(roots[i], roots[j]));
        }
      }
      const correction = cDiv(evaluateComplex(roots[i]), denom);
      roots[i] = cSub(roots[i], correction);
      if (cAbs(correction) > tolerance) {
        converged = false;
      }
    }
    if (converged) {
      break;
    }
  }
  return roots;
}

export function realRoots(coefficients, tolerance = 1e-7) {
  return durandKerner(coefficients)
    .filter((root) => Math.abs(root.im) < tolerance)
    .map((root) => root.re)
    .sort((a, b) => a - b);
}

export function polynomialFeatures(coefficients) {
  const normalized = normalizeCoefficients(coefficients);
  const derivative = derivativeCoefficients(normalized);
  const roots = durandKerner(normalized);
  const real = roots.filter((root) => Math.abs(root.im) < 1e-7).map((root) => root.re).sort((a, b) => a - b);
  const critical = realRoots(derivative);
  return {
    degree: normalized.length - 1,
    roots,
    realRoots: real,
    criticalPoints: critical,
    yIntercept: normalized[normalized.length - 1]
  };
}

export function coefficientsFromRoots(roots) {
  let coefficients = [1];
  for (const root of roots) {
    const next = Array(coefficients.length + 1).fill(0);
    for (let i = 0; i < coefficients.length; i += 1) {
      next[i] += coefficients[i];
      next[i + 1] += -coefficients[i] * root;
    }
    coefficients = next;
  }
  return coefficients;
}
