function shapeOf(value) {
  if (!Array.isArray(value)) {
    return [];
  }
  if (value.length === 0) {
    return [0];
  }
  const childShape = shapeOf(value[0]);
  for (let index = 1; index < value.length; index += 1) {
    const nextShape = shapeOf(value[index]);
    if (JSON.stringify(nextShape) !== JSON.stringify(childShape)) {
      throw new Error("Tensor inputs must have rectangular nested-array shapes");
    }
  }
  return [value.length, ...childShape];
}

function getAt(tensor, indices) {
  let current = tensor;
  for (const index of indices) {
    current = current[index];
  }
  return current;
}

function makeNestedArray(shape, fill = 0) {
  if (!shape.length) {
    return fill;
  }
  return Array.from({ length: shape[0] }, () => makeNestedArray(shape.slice(1), fill));
}

function setAt(tensor, indices, value) {
  if (!indices.length) {
    return value;
  }
  let current = tensor;
  for (let i = 0; i < indices.length - 1; i += 1) {
    current = current[indices[i]];
  }
  current[indices[indices.length - 1]] = value;
  return tensor;
}

function* iterateIndices(shape, prefix = []) {
  if (!shape.length) {
    yield prefix;
    return;
  }
  for (let i = 0; i < shape[0]; i += 1) {
    yield* iterateIndices(shape.slice(1), [...prefix, i]);
  }
}

export function parseEinsumSignature(signature) {
  const pieces = signature.split("->");
  if (pieces.length !== 2) {
    throw new Error("Einsum signature must look like 'ij,jk->ik'");
  }
  const inputPart = pieces[0].trim();
  const outputPart = pieces[1].trim();
  if (!inputPart) {
    throw new Error("Einsum signature must look like 'ij,jk->ik'");
  }
  const inputs = inputPart.split(",").map((part) => part.trim());
  if (inputs.some((part) => !/^[A-Za-z]+$/.test(part))) {
    throw new Error("Each input tensor signature must use letter indices");
  }
  if (outputPart && !/^[A-Za-z]+$/.test(outputPart)) {
    throw new Error("Output tensor signature must use letter indices");
  }
  return { inputs, output: outputPart };
}

export function einsum(signature, tensors) {
  const parsed = parseEinsumSignature(signature);
  if (parsed.inputs.length !== tensors.length) {
    throw new Error("Tensor count does not match the signature");
  }

  const dimensions = new Map();
  parsed.inputs.forEach((indices, tensorIndex) => {
    const shape = shapeOf(tensors[tensorIndex]);
    if (shape.length !== indices.length) {
      throw new Error("Tensor rank does not match the signature");
    }
    [...indices].forEach((symbol, axis) => {
      const size = shape[axis];
      if (dimensions.has(symbol) && dimensions.get(symbol) !== size) {
        throw new Error(`Dimension mismatch for index '${symbol}'`);
      }
      dimensions.set(symbol, size);
    });
  });

  [...parsed.output].forEach((symbol) => {
    if (!dimensions.has(symbol)) {
      throw new Error(`Output index '${symbol}' does not appear in the inputs`);
    }
  });

  const outputShape = [...parsed.output].map((symbol) => dimensions.get(symbol));
  let result = makeNestedArray(outputShape, 0);
  const allSymbols = [...new Set(parsed.inputs.join("").split(""))];
  const summedSymbols = allSymbols.filter((symbol) => !parsed.output.includes(symbol));
  const sumShape = summedSymbols.map((symbol) => dimensions.get(symbol));

  for (const outputIndices of iterateIndices(outputShape)) {
    const outputScope = {};
    [...parsed.output].forEach((symbol, index) => {
      outputScope[symbol] = outputIndices[index];
    });

    let total = 0;
    for (const summedIndices of iterateIndices(sumShape)) {
      const scope = { ...outputScope };
      summedSymbols.forEach((symbol, index) => {
        scope[symbol] = summedIndices[index];
      });

      let product = 1;
      parsed.inputs.forEach((indices, tensorIndex) => {
        const lookup = [...indices].map((symbol) => scope[symbol]);
        product *= getAt(tensors[tensorIndex], lookup);
      });
      total += product;
    }
    result = setAt(result, outputIndices, total);
  }

  return result;
}

export function describeTensorSignature(signature, tensors) {
  const parsed = parseEinsumSignature(signature);
  return {
    inputs: parsed.inputs,
    output: parsed.output,
    shapes: tensors.map((tensor) => shapeOf(tensor))
  };
}
