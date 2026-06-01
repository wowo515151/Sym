export function byId(id) {
  const node = document.getElementById(id);
  if (!node) {
    throw new Error(`Missing node '${id}'`);
  }
  return node;
}

export function formatNumber(value, digits = 4) {
  if (typeof value !== "number" || Number.isNaN(value)) {
    return "NaN";
  }
  if (!Number.isFinite(value)) {
    return value > 0 ? "Infinity" : "-Infinity";
  }
  return Number(value.toFixed(digits)).toString();
}

export function setHtml(id, html) {
  byId(id).innerHTML = html;
}

export function lineChartSvg(series, options = {}) {
  const width = options.width ?? 720;
  const height = options.height ?? 320;
  const pad = 36;
  const allPoints = series.flatMap((item) => item.points);
  const xs = allPoints.map((point) => point.x);
  const ys = allPoints.map((point) => point.y);
  const minX = options.minX ?? Math.min(...xs);
  const maxX = options.maxX ?? Math.max(...xs);
  const minY = options.minY ?? Math.min(...ys);
  const maxY = options.maxY ?? Math.max(...ys);
  const xScale = (x) => pad + ((x - minX) / Math.max(1e-9, maxX - minX)) * (width - pad * 2);
  const yScale = (y) => height - pad - ((y - minY) / Math.max(1e-9, maxY - minY)) * (height - pad * 2);

  const axisX = minY <= 0 && maxY >= 0 ? yScale(0) : height - pad;
  const axisY = minX <= 0 && maxX >= 0 ? xScale(0) : pad;

  const polylines = series.map((item) => {
    const points = item.points.map((point) => `${xScale(point.x)},${yScale(point.y)}`).join(" ");
    return `<polyline fill="none" stroke="${item.color}" stroke-width="2.5" points="${points}" />`;
  }).join("");

  const legends = series.map((item, index) =>
    `<g transform="translate(${pad + index * 180},${pad * 0.6})"><rect width="16" height="4" y="-6" fill="${item.color}"></rect><text x="22" y="0" fill="#edf5ff" font-size="12">${escapeHtml(item.label)}</text></g>`
  ).join("");

  return `
    <svg viewBox="0 0 ${width} ${height}" width="100%" height="${height}" role="img" aria-label="${escapeHtml(options.label ?? "chart")}">
      <rect width="${width}" height="${height}" rx="16" fill="rgba(255,255,255,0.02)"></rect>
      <line x1="${pad}" y1="${axisX}" x2="${width - pad}" y2="${axisX}" stroke="rgba(122,164,224,0.26)"></line>
      <line x1="${axisY}" y1="${pad}" x2="${axisY}" y2="${height - pad}" stroke="rgba(122,164,224,0.26)"></line>
      ${polylines}
      ${legends}
    </svg>
  `;
}

export function renderMatrixTable(matrix, digits = 4) {
  if (!Array.isArray(matrix)) {
    return `<div class="tool-code">${escapeHtml(String(matrix))}</div>`;
  }
  if (!Array.isArray(matrix[0])) {
    return `<table class="tool-table"><tbody><tr>${matrix.map((value) => `<td>${escapeHtml(formatNumber(value, digits))}</td>`).join("")}</tr></tbody></table>`;
  }
  return `<table class="tool-table"><tbody>${matrix.map((row) => `<tr>${row.map((value) => `<td>${escapeHtml(formatNumber(value, digits))}</td>`).join("")}</tr>`).join("")}</tbody></table>`;
}

export function renderChipList(items) {
  return `<div class="tool-chip-row">${items.map((item) => `<span class="tool-chip">${escapeHtml(item)}</span>`).join("")}</div>`;
}

export function renderMessage(message, tone = "info") {
  const toneClass = tone === "error" ? " error" : "";
  return `<div class="tool-message${toneClass}">${escapeHtml(message)}</div>`;
}

export function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

export function sampleRange(start, end, count, fn) {
  return Array.from({ length: count }, (_, index) => {
    const x = start + ((end - start) * index) / (count - 1);
    return { x, y: fn(x) };
  });
}

export function drawVectorField(canvas, samples, point = null) {
  const ctx = canvas.getContext("2d");
  const width = canvas.width;
  const height = canvas.height;
  ctx.clearRect(0, 0, width, height);
  ctx.fillStyle = "#091829";
  ctx.fillRect(0, 0, width, height);
  ctx.strokeStyle = "rgba(122,164,224,0.12)";
  for (let i = 0; i <= 10; i += 1) {
    const x = (i / 10) * width;
    const y = (i / 10) * height;
    ctx.beginPath();
    ctx.moveTo(x, 0);
    ctx.lineTo(x, height);
    ctx.stroke();
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.lineTo(width, y);
    ctx.stroke();
  }
  samples.forEach((sample) => {
    const sx = ((sample.x + 3) / 6) * width;
    const sy = height - ((sample.y + 3) / 6) * height;
    ctx.strokeStyle = "#89d1b6";
    ctx.beginPath();
    ctx.moveTo(sx, sy);
    ctx.lineTo(sx + sample.dx * 10, sy - sample.dy * 10);
    ctx.stroke();
  });
  if (point) {
    const px = ((point.x + 3) / 6) * width;
    const py = height - ((point.y + 3) / 6) * height;
    ctx.fillStyle = "#f3cb7a";
    ctx.beginPath();
    ctx.arc(px, py, 5, 0, Math.PI * 2);
    ctx.fill();
  }
}
