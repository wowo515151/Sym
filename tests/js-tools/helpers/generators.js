export function seededRandom(seed = 123456789) {
  let state = seed >>> 0;
  return () => {
    state = (1664525 * state + 1013904223) >>> 0;
    return state / 0x100000000;
  };
}

export function randomInteger(random, min, max) {
  return Math.floor(random() * (max - min + 1)) + min;
}

export function randomPolynomialRoots(seed = 7, count = 4) {
  const random = seededRandom(seed);
  return Array.from({ length: count }, () => randomInteger(random, -5, 5) || 1);
}

export function randomRecurrence(seed = 13, order = 2) {
  const random = seededRandom(seed);
  const coefficients = Array.from({ length: order }, () => randomInteger(random, -3, 3));
  const initialValues = Array.from({ length: order }, () => randomInteger(random, -4, 6));
  return { coefficients, initialValues };
}
