'use strict';

const $ = (id) => document.getElementById(id);

// Ejemplos de símbolo según la fuente elegida, para orientar al usuario.
const HINTS = {
  stooq: 'Acciones/índices. Ejemplos: AAPL, MSFT, ^SPX (S&P 500), SAN.MC (Santander, BME).',
  yahoo: 'Acciones/índices. Ejemplos: AAPL, MSFT, ^GSPC (S&P 500), SAN.MC.',
  coingecko: 'Cripto por su id de CoinGecko. Ejemplos: bitcoin, ethereum, solana. (Gratis: últimos 365 días.)',
  binance: 'Cripto en pares USDT. Ejemplos: BTCUSDT, ETHUSDT, SOLUSDT.',
};
const DEFAULT_SYMBOL = { stooq: 'AAPL', yahoo: 'AAPL', coingecko: 'bitcoin', binance: 'BTCUSDT' };

function updateHint() {
  const p = $('provider').value;
  $('hint').textContent = HINTS[p];
  if (!$('symbol').dataset.touched) $('symbol').value = DEFAULT_SYMBOL[p];
}

$('provider').addEventListener('change', updateHint);
$('symbol').addEventListener('input', () => { $('symbol').dataset.touched = '1'; });
$('symbol').addEventListener('keydown', (e) => { if (e.key === 'Enter') run(); });
$('analyze').addEventListener('click', run);

async function run() {
  const provider = $('provider').value;
  const symbol = $('symbol').value.trim();
  if (!symbol) { setStatus('Escribe un símbolo primero.', 'err'); return; }

  $('analyze').disabled = true;
  setStatus('Descargando datos y analizando…', 'loading');
  $('results').classList.add('hidden');

  try {
    const res = await window.api.analyze({ provider, symbol });
    if (!res.ok) throw new Error(res.error);
    render(res.data);
    setStatus('');
    $('results').classList.remove('hidden');
  } catch (err) {
    setStatus('Error: ' + err.message, 'err');
  } finally {
    $('analyze').disabled = false;
  }
}

function setStatus(msg, cls) {
  const el = $('status');
  el.textContent = msg;
  el.className = 'status' + (cls ? ' ' + cls : '');
}

function render(d) {
  // --- Cabecera de resultado ---
  $('res-title').textContent = `${d.symbol.toUpperCase()} · ${d.provider}`;

  // --- Predicción ---
  const prob = d.prediction.probUp;
  drawGauge($('gauge'), prob);
  const pct = prob == null ? '—' : (prob * 100).toFixed(1) + '%';
  $('pred-prob').textContent = pct;
  const leansUp = prob != null && prob >= 0.5;
  $('pred-label').textContent = prob == null ? 'Sin datos suficientes'
    : leansUp ? '▲ Sesgo a subir' : '▼ Sesgo a bajar';
  $('pred-label').style.color = prob == null ? 'var(--muted)' : leansUp ? 'var(--up)' : 'var(--down)';
  $('pred-meta').innerHTML = prob == null ? '' :
    `Probabilidad estimada de que el próximo cierre sea mayor que el actual.<br>` +
    `Referencia: cierre del ${d.prediction.asOfDate} en ${fmt(d.prediction.lastClose)}.`;

  // --- Precio ---
  window.Charts.drawPrice($('price-chart'), d.candles, d.indicators);

  // --- Backtest ---
  const bt = d.backtest;
  const btBox = $('backtest-box');
  if (bt.error) {
    btBox.innerHTML = `<p class="pred-note">${bt.error}</p>`;
  } else {
    const acc = bt.accuracy * 100;
    const base = bt.baselineMajority * 100;
    const edge = bt.edge * 100;
    const strat = bt.equityStrategy;
    const hold = bt.equityHold;
    btBox.innerHTML = `
      <div class="metrics">
        <div class="metric"><div class="v">${acc.toFixed(1)}%</div><div class="k">Acierto del modelo (${bt.trades} días)</div></div>
        <div class="metric"><div class="v">${base.toFixed(1)}%</div><div class="k">Base tonta (clase mayoritaria)</div></div>
        <div class="metric"><div class="v ${edge >= 0 ? 'pos' : 'neg'}">${edge >= 0 ? '+' : ''}${edge.toFixed(1)} pts</div><div class="k">Ventaja real sobre la base</div></div>
        <div class="metric"><div class="v ${strat >= hold ? 'pos' : 'neg'}">${strat.toFixed(2)}x</div><div class="k">Estrategia vs ${hold.toFixed(2)}x comprar/mantener</div></div>
      </div>
      <div class="legend" style="margin-top:12px">
        <span><i class="dot" style="background:${window.Charts.COLORS.strategy}"></i> Estrategia del modelo</span>
        <span><i class="dot" style="background:${window.Charts.COLORS.hold}"></i> Comprar y mantener</span>
      </div>
      <canvas id="equity-chart"></canvas>
      <p class="pred-note">${interpretEdge(edge)}</p>
    `;
    window.Charts.drawEquity($('equity-chart'), bt.equityCurve);
  }
}

function interpretEdge(edge) {
  if (edge >= 3) return 'El modelo supera a la base tonta en este histórico. Ojo: buen resultado pasado no garantiza el futuro, y no incluye comisiones ni deslizamiento.';
  if (edge >= 0) return 'El modelo apenas iguala a adivinar la clase mayoritaria: la ventaja es marginal. Trátalo con mucha cautela.';
  return 'En este histórico el modelo NO supera a una regla tonta (predecir siempre lo más frecuente). Es la prueba honesta de que predecir la dirección diaria es muy difícil. No lo uses para decidir dinero real.';
}

// Medidor circular (anillo) con la probabilidad.
function drawGauge(canvas, prob) {
  const dpr = window.devicePixelRatio || 1;
  const size = 150;
  canvas.width = size * dpr; canvas.height = size * dpr;
  canvas.style.width = size + 'px'; canvas.style.height = size + 'px';
  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, size, size);
  const cx = size / 2, cy = size / 2, r = 60;
  // fondo
  ctx.lineWidth = 14;
  ctx.strokeStyle = '#2a3038';
  ctx.beginPath(); ctx.arc(cx, cy, r, 0, Math.PI * 2); ctx.stroke();
  if (prob == null) return;
  const up = prob >= 0.5;
  ctx.strokeStyle = up ? '#2ea043' : '#f85149';
  ctx.lineCap = 'round';
  const start = -Math.PI / 2;
  ctx.beginPath();
  ctx.arc(cx, cy, r, start, start + Math.PI * 2 * prob);
  ctx.stroke();
}

function fmt(v) {
  if (v == null) return '—';
  if (v >= 1000) return v.toLocaleString('es-ES', { maximumFractionDigits: 2 });
  if (v >= 1) return v.toFixed(2);
  return v.toPrecision(4);
}

// Redibuja los gráficos si cambia el tamaño de la ventana.
let resizeT;
window.addEventListener('resize', () => {
  clearTimeout(resizeT);
  resizeT = setTimeout(() => { if (!$('results').classList.contains('hidden') && window.__last) render(window.__last); }, 150);
});
const _render = render;
render = function (d) { window.__last = d; _render(d); };

updateHint();
