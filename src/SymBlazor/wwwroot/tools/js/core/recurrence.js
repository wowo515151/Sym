export function generateSequence(coefficients, initialValues, count) {
  const order = coefficients.length;
  if (order === 0) {
    throw new Error("At least one recurrence coefficient is required");
  }
  if (!Number.isInteger(count) || count < 1) {
    throw new Error("Sequence length must be a positive integer");
  }
  if (initialValues.length < order) {
    throw new Error("Initial values must cover the recurrence order");
  }
  const sequence = initialValues.slice(0, count);
  for (let index = order; index < count; index += 1) {
    let next = 0;
    for (let j = 0; j < order; j += 1) {
      next += coefficients[j] * sequence[index - j - 1];
    }
    sequence.push(next);
  }
  return sequence;
}

export function characteristicRoots(coefficients) {
  if (!coefficients.length) {
    throw new Error("At least one recurrence coefficient is required");
  }
  if (coefficients.length === 1) {
    return [coefficients[0]];
  }
  if (coefficients.length === 2) {
    const [c1, c2] = coefficients;
    const discriminant = c1 * c1 + 4 * c2;
    if (discriminant >= 0) {
      const sqrt = Math.sqrt(discriminant);
      return [(c1 + sqrt) / 2, (c1 - sqrt) / 2];
    }
    const real = c1 / 2;
    const imag = Math.sqrt(-discriminant) / 2;
    return [{ re: real, im: imag }, { re: real, im: -imag }];
  }
  return [];
}

export function describeClosedForm(coefficients, initialValues) {
  const roots = characteristicRoots(coefficients);
  if (coefficients.length === 1 && typeof roots[0] === "number") {
    return `a_n = ${initialValues[0]} * (${formatNumber(roots[0])})^n`;
  }
  if (coefficients.length === 2 && roots.length === 2 && typeof roots[0] === "number" && typeof roots[1] === "number") {
    const [r1, r2] = roots;
    if (Math.abs(r1 - r2) < 1e-9) {
      return "Repeated-root second-order recurrence";
    }
    const a0 = initialValues[0];
    const a1 = initialValues[1];
    const alpha = (a1 - a0 * r2) / (r1 - r2);
    const beta = a0 - alpha;
    return `a_n = ${formatNumber(alpha)} * (${formatNumber(r1)})^n + ${formatNumber(beta)} * (${formatNumber(r2)})^n`;
  }
  return "Sequence generated numerically from the recurrence";
}

function formatNumber(value) {
  return Number(value.toFixed(6)).toString();
}

export function syntheticRecurrence(coefficients, initialValues, count = 12) {
  return {
    coefficients,
    initialValues,
    sequence: generateSequence(coefficients, initialValues, count),
    description: describeClosedForm(coefficients, initialValues)
  };
}
