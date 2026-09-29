// Gráficos en canvas puro, sin librerías. Dos tipos: precio (con velas + medias
// móviles) y curva de capital del backtest.

'use strict';

const COLORS = {
  up: '#2ea043',
  down: '#f85149',
  sma20: '#4c8dff',
  sma50: '#d29922',
  grid: '#2a3038',
  text: '#8b949e',
  strategy: '#4c8dff',
  hold: '#8b949e',
};

// Prepara el canvas para pantallas de alta densidad y devuelve el contexto y
// las dimensiones lógicas.
function setup(canvas, cssHeight) {
  const dpr = window.devicePixelRatio || 1;
  const cssWidth = canvas.clientWidth || canvas.parentElement.clientWidth;
  canvas.style.height = cssHeight + 'px';
  canvas.width = Math.round(cssWidth * dpr);
  canvas.height = Math.round(cssHeight * dpr);
  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, cssWidth, cssHeight);
  return { ctx, w: cssWidth, h: cssHeight };
}

function niceExtent(min, max) {
  const pad = (max - min) * 0.06 || 1;
  return [min - pad, max + pad];
}

// Dibuja precio con velas y medias móviles. `candles` es [{open,high,low,close}],
// indicators = { sma20, sma50 } arrays alineados con candles (pueden traer null).
function drawPrice(canvas, candles, indicators) {
  const padL = 56, padR = 12, padT = 12, padB = 22;
  const { ctx, w, h } = setup(canvas, 320);
  const n = candles.length;
  if (!n) return;

  // Mostramos como mucho los últimos ~260 días para que se vea bien.
  const start = Math.max(0, n - 260);
  const view = candles.slice(start);
  const s20 = indicators.sma20.slice(start);
  const s50 = indicators.sma50.slice(start);

  let lo = Infinity, hi = -Infinity;
  for (const c of view) { lo = Math.min(lo, c.low); hi = Math.max(hi, c.high); }
  [lo, hi] = niceExtent(lo, hi);

  const plotW = w - padL - padR;
  const plotH = h - padT - padB;
  const x = (i) => padL + (plotW * i) / (view.length - 1 || 1);
  const yRaw = (v) => padT + plotH * (1 - (v - lo) / (hi - lo));

  // Rejilla + etiquetas de precio.
  ctx.strokeStyle = COLORS.grid;
  ctx.fillStyle = COLORS.text;
  ctx.font = '11px sans-serif';
  ctx.lineWidth = 1;
  for (let g = 0; g <= 4; g++) {
    const val = lo + ((hi - lo) * g) / 4;
    const yy = yRaw(val);
    ctx.beginPath(); ctx.moveTo(padL, yy); ctx.lineTo(w - padR, yy); ctx.stroke();
    ctx.fillText(fmtPrice(val), 6, yy + 3);
  }

  // Velas.
  const cw = Math.max(1, (plotW / view.length) * 0.6);
  for (let i = 0; i < view.length; i++) {
    const c = view[i];
    const up = c.close >= c.open;
    ctx.strokeStyle = up ? COLORS.up : COLORS.down;
    ctx.fillStyle = up ? COLORS.up : COLORS.down;
    const cx = x(i);
    // mecha
    ctx.beginPath(); ctx.moveTo(cx, yRaw(c.high)); ctx.lineTo(cx, yRaw(c.low)); ctx.stroke();
    // cuerpo
    const yo = yRaw(c.open), yc = yRaw(c.close);
    const top = Math.min(yo, yc);
    const bh = Math.max(1, Math.abs(yc - yo));
    ctx.fillRect(cx - cw / 2, top, cw, bh);
  }

  drawLine(ctx, s20, x, yRaw, COLORS.sma20);
  drawLine(ctx, s50, x, yRaw, COLORS.sma50);
}

function drawLine(ctx, arr, x, y, color) {
  ctx.strokeStyle = color;
  ctx.lineWidth = 1.6;
  ctx.beginPath();
  let started = false;
  for (let i = 0; i < arr.length; i++) {
    if (arr[i] == null) { started = false; continue; }
    const px = x(i), py = y(arr[i]);
    if (!started) { ctx.moveTo(px, py); started = true; }
    else ctx.lineTo(px, py);
  }
  ctx.stroke();
}

// Curva de capital: estrategia del modelo vs. comprar y mantener.
function drawEquity(canvas, curve) {
  const padL = 48, padR = 12, padT = 12, padB = 20;
  const { ctx, w, h } = setup(canvas, 200);
  if (!curve || !curve.length) return;

  let lo = Infinity, hi = -Infinity;
  for (const p of curve) {
    lo = Math.min(lo, p.strategy, p.hold);
    hi = Math.max(hi, p.strategy, p.hold);
  }
  [lo, hi] = niceExtent(lo, hi);

  const plotW = w - padL - padR;
  const plotH = h - padT - padB;
  const x = (i) => padL + (plotW * i) / (curve.length - 1 || 1);
  const y = (v) => padT + plotH * (1 - (v - lo) / (hi - lo));

  ctx.strokeStyle = COLORS.grid;
  ctx.fillStyle = COLORS.text;
  ctx.font = '11px sans-serif';
  for (let g = 0; g <= 3; g++) {
    const val = lo + ((hi - lo) * g) / 3;
    const yy = y(val);
    ctx.beginPath(); ctx.moveTo(padL, yy); ctx.lineTo(w - padR, yy); ctx.stroke();
    ctx.fillText(val.toFixed(2) + 'x', 6, yy + 3);
  }

  const seriesLine = (key, color) => {
    ctx.strokeStyle = color; ctx.lineWidth = 1.8; ctx.beginPath();
    curve.forEach((p, i) => { const px = x(i), py = y(p[key]); i ? ctx.lineTo(px, py) : ctx.moveTo(px, py); });
    ctx.stroke();
  };
  seriesLine('hold', COLORS.hold);
  seriesLine('strategy', COLORS.strategy);
}

function fmtPrice(v) {
  if (v >= 1000) return v.toLocaleString('es-ES', { maximumFractionDigits: 0 });
  if (v >= 1) return v.toFixed(2);
  return v.toPrecision(3);
}

window.Charts = { drawPrice, drawEquity, COLORS };
