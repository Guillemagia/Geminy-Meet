'use strict';

const $ = (id) => document.getElementById(id);

const HINTS = {
  stooq: 'Acciones/índices. Ejemplos: AAPL, MSFT, ^SPX (S&P 500), SAN.MC (Santander, BME).',
  yahoo: 'Acciones/índices. Ejemplos: AAPL, MSFT, ^GSPC (S&P 500), SAN.MC.',
  coingecko: 'Cripto por su id de CoinGecko. Ejemplos: bitcoin, ethereum, solana. (Gratis: últimos 365 días.)',
  binance: 'Cripto en pares USDT. Ejemplos: BTCUSDT, ETHUSDT, SOLUSDT.',
};
const DEFAULT_SYMBOL = { stooq: 'AAPL', yahoo: 'AAPL', coingecko: 'bitcoin', binance: 'BTCUSDT' };
const HORIZON_LABEL = { 1: '1 día', 5: '5 días', 10: '10 días' };

let threshold = 0.6; // se carga de ajustes

function updateHint() {
  const p = $('provider').value;
  $('hint').textContent = HINTS[p];
  if (!$('symbol').dataset.touched) $('symbol').value = DEFAULT_SYMBOL[p];
}

// --- Arranque ---
async function init() {
  updateHint();
  try {
    const s = await window.api.settingsGet();
    threshold = s.threshold ?? 0.6;
  } catch {}
  $('threshold').value = Math.round(threshold * 100);
  $('threshold-val').textContent = Math.round(threshold * 100) + '%';
  await refreshWatchlist();
}

$('provider').addEventListener('change', updateHint);
$('symbol').addEventListener('input', () => { $('symbol').dataset.touched = '1'; });
$('symbol').addEventListener('keydown', (e) => { if (e.key === 'Enter') run(); });
$('analyze').addEventListener('click', run);
$('save').addEventListener('click', saveCurrent);
$('scan').addEventListener('click', scanWatchlist);

$('threshold').addEventListener('input', () => {
  threshold = +$('threshold').value / 100;
  $('threshold-val').textContent = $('threshold').value + '%';
});
$('threshold').addEventListener('change', () => {
  window.api.settingsSet({ threshold });
});

// --- Análisis principal ---
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
    // Mostramos el panel ANTES de dibujar: los canvas necesitan que su
    // contenedor sea visible para tener un ancho > 0 al renderizar.
    $('results').classList.remove('hidden');
    render(res.data);
    setStatus('');
    checkAlert(res.data);
  } catch (err) {
    setStatus('Error: ' + err.message, 'err');
  } finally {
    $('analyze').disabled = false;
  }
}

// Dispara notificación nativa si la probabilidad a 1 día cruza el umbral.
function checkAlert(d) {
  const p = d.prediction.probUp;
  if (p == null) return;
  const sym = d.symbol.toUpperCase();
  if (p >= threshold) {
    window.api.notify(`▲ ${sym}: ${(p * 100).toFixed(0)}% prob. de subir`, `Señal alcista a 1 día (umbral ${Math.round(threshold * 100)}%).`);
  } else if (p <= 1 - threshold) {
    window.api.notify(`▼ ${sym}: ${((1 - p) * 100).toFixed(0)}% prob. de bajar`, `Señal bajista a 1 día (umbral ${Math.round(threshold * 100)}%).`);
  }
}

function setStatus(msg, cls) {
  const el = $('status');
  el.textContent = msg;
  el.className = 'status' + (cls ? ' ' + cls : '');
}

function render(d) {
  window.__last = d;
  $('res-title').textContent = `${d.symbol.toUpperCase()} · ${d.provider}`;

  // Predicción a 1 día (gauge)
  const prob = d.prediction.probUp;
  drawGauge($('gauge'), prob);
  $('pred-prob').textContent = prob == null ? '—' : (prob * 100).toFixed(1) + '%';
  const leansUp = prob != null && prob >= 0.5;
  $('pred-label').textContent = prob == null ? 'Sin datos suficientes' : leansUp ? '▲ Sesgo a subir (1 día)' : '▼ Sesgo a bajar (1 día)';
  $('pred-label').style.color = prob == null ? 'var(--muted)' : leansUp ? 'var(--up)' : 'var(--down)';
  $('pred-meta').innerHTML = prob == null ? '' :
    `Referencia: cierre del ${d.prediction.asOfDate} en ${fmt(d.prediction.lastClose)}.`;

  // Tabla de horizontes
  const rows = d.horizons.map((h) => {
    if (h.error) return `<tr><td>${HORIZON_LABEL[h.horizon]}</td><td colspan="3" class="muted">${h.error}</td></tr>`;
    const p = h.probUp == null ? '—' : (h.probUp * 100).toFixed(1) + '%';
    const acc = h.accuracy == null ? '—' : (h.accuracy * 100).toFixed(1) + '%';
    const edge = h.edge == null ? '—' : `${h.edge >= 0 ? '+' : ''}${(h.edge * 100).toFixed(1)} pts`;
    const edgeCls = h.edge == null ? '' : h.edge >= 0.01 ? 'pos' : h.edge < 0 ? 'neg' : '';
    const pCls = h.probUp == null ? '' : h.probUp >= 0.5 ? 'pos' : 'neg';
    return `<tr>
      <td>${HORIZON_LABEL[h.horizon]}</td>
      <td class="${pCls}">${p}</td>
      <td>${acc}</td>
      <td class="${edgeCls}">${edge}</td>
    </tr>`;
  }).join('');
  $('horizons-table').innerHTML = `
    <thead><tr><th>Horizonte</th><th>Prob. subida</th><th>Acierto</th><th>Ventaja</th></tr></thead>
    <tbody>${rows}</tbody>`;

  // Precio
  window.Charts.drawPrice($('price-chart'), d.candles, d.indicators);

  // Backtest (1 día)
  const bt = d.backtest;
  const btBox = $('backtest-box');
  if (!bt || bt.error) {
    btBox.innerHTML = `<p class="pred-note">${(bt && bt.error) || 'Sin backtest disponible.'}</p>`;
  } else {
    const acc = bt.accuracy * 100, base = bt.baselineMajority * 100, edge = bt.edge * 100;
    const strat = bt.equityStrategy, hold = bt.equityHold;
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
      <p class="pred-note">${interpretEdge(edge)}</p>`;
    window.Charts.drawEquity($('equity-chart'), bt.equityCurve);
  }
}

function interpretEdge(edge) {
  if (edge >= 3) return 'El modelo supera a la base tonta en este histórico. Ojo: buen resultado pasado no garantiza el futuro, y no incluye comisiones ni deslizamiento.';
  if (edge >= 0) return 'El modelo apenas iguala a adivinar la clase mayoritaria: la ventaja es marginal. Trátalo con mucha cautela.';
  return 'En este histórico el modelo NO supera a una regla tonta (predecir siempre lo más frecuente). Es la prueba honesta de que predecir la dirección es muy difícil. No lo uses para decidir dinero real.';
}

// --- Watchlist ---
async function refreshWatchlist() {
  const wl = await window.api.watchlistGet();
  const box = $('wl-chips');
  if (!wl.length) {
    box.innerHTML = '<span class="wl-empty">Aún no has guardado ningún símbolo. Analiza uno y pulsa ★ Guardar.</span>';
    return;
  }
  box.innerHTML = '';
  for (const item of wl) {
    const chip = document.createElement('span');
    chip.className = 'chip';
    chip.innerHTML = `<span class="chip-label">${item.symbol.toUpperCase()}</span><span class="chip-src">${item.provider}</span><span class="chip-x" title="Quitar">×</span>`;
    chip.querySelector('.chip-label').addEventListener('click', () => loadSymbol(item));
    chip.querySelector('.chip-src').addEventListener('click', () => loadSymbol(item));
    chip.querySelector('.chip-x').addEventListener('click', async (e) => {
      e.stopPropagation();
      await window.api.watchlistRemove(item);
      refreshWatchlist();
    });
    box.appendChild(chip);
  }
}

function loadSymbol(item) {
  $('provider').value = item.provider;
  $('symbol').value = item.symbol;
  $('symbol').dataset.touched = '1';
  updateHint();
  run();
}

async function saveCurrent() {
  const provider = $('provider').value;
  const symbol = $('symbol').value.trim();
  if (!symbol) { setStatus('Escribe un símbolo para guardarlo.', 'err'); return; }
  await window.api.watchlistAdd({ provider, symbol });
  refreshWatchlist();
}

async function scanWatchlist() {
  $('scan').disabled = true;
  const box = $('scan-results');
  box.classList.remove('hidden');
  box.innerHTML = '<span class="muted">Revisando watchlist…</span>';
  try {
    const { threshold: th, results } = await window.api.scanWatchlist();
    if (!results.length) { box.innerHTML = '<span class="muted">Watchlist vacía.</span>'; return; }
    const alerts = results.filter((r) => r.alert);
    box.innerHTML = '<div class="scan-title">Resultado del escaneo (prob. a 1 día):</div>' +
      results.map((r) => {
        if (r.error) return `<div class="scan-row"><b>${r.symbol.toUpperCase()}</b> <span class="muted">${r.error}</span></div>`;
        const p = r.probUp == null ? null : r.probUp;
        const pct = p == null ? '—' : (p * 100).toFixed(0) + '%';
        const dir = p == null ? '' : p >= 0.5 ? '▲' : '▼';
        const cls = p == null ? '' : p >= 0.5 ? 'pos' : 'neg';
        const flag = r.alert ? '<span class="alert-flag">⚠ alerta</span>' : '';
        return `<div class="scan-row ${r.alert ? 'is-alert' : ''}"><b>${r.symbol.toUpperCase()}</b> <span class="chip-src">${r.provider}</span> <span class="${cls}">${dir} ${pct}</span> ${flag}</div>`;
      }).join('');
    // Notificar las alertas
    for (const a of alerts) {
      const p = a.probUp;
      const up = p >= 0.5;
      window.api.notify(
        `${up ? '▲' : '▼'} ${a.symbol.toUpperCase()}: ${((up ? p : 1 - p) * 100).toFixed(0)}% prob. de ${up ? 'subir' : 'bajar'}`,
        `Watchlist · umbral ${Math.round(th * 100)}%`
      );
    }
  } catch (err) {
    box.innerHTML = `<span class="muted">Error: ${err.message}</span>`;
  } finally {
    $('scan').disabled = false;
  }
}

// --- Medidor circular ---
function drawGauge(canvas, prob) {
  const dpr = window.devicePixelRatio || 1;
  const size = 150;
  canvas.width = size * dpr; canvas.height = size * dpr;
  canvas.style.width = size + 'px'; canvas.style.height = size + 'px';
  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, size, size);
  const cx = size / 2, cy = size / 2, r = 60;
  ctx.lineWidth = 14;
  ctx.strokeStyle = '#2a3038';
  ctx.beginPath(); ctx.arc(cx, cy, r, 0, Math.PI * 2); ctx.stroke();
  if (prob == null) return;
  ctx.strokeStyle = prob >= 0.5 ? '#2ea043' : '#f85149';
  ctx.lineCap = 'round';
  const start = -Math.PI / 2;
  ctx.beginPath(); ctx.arc(cx, cy, r, start, start + Math.PI * 2 * prob); ctx.stroke();
}

function fmt(v) {
  if (v == null) return '—';
  if (v >= 1000) return v.toLocaleString('es-ES', { maximumFractionDigits: 2 });
  if (v >= 1) return v.toFixed(2);
  return v.toPrecision(4);
}

let resizeT;
window.addEventListener('resize', () => {
  clearTimeout(resizeT);
  resizeT = setTimeout(() => { if (!$('results').classList.contains('hidden') && window.__last) render(window.__last); }, 150);
});

init();
