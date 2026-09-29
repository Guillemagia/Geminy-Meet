// Modelo de predicción: regresión logística entrenada con descenso de gradiente,
// escrita en JS puro (sin librerías de ML). Predice la PROBABILIDAD de que el
// próximo periodo cierre por encima del actual — nunca una certeza.
//
// Importante: esto NO adivina el futuro. Aprende relaciones estadísticas de los
// datos históricos que muchas veces NO se mantienen. Trátalo como una señal más,
// con su probabilidad y su tasa de acierto medida honestamente en el backtest.

'use strict';

const ind = require('./indicators');

// A partir de las series de precios de cierre (y volumen opcional), construye
// la matriz de características por cada día. Devuelve { X, names } donde X[i] es
// el vector de features del día i (o null si aún no hay datos suficientes).
function buildFeatures(closes, volumes) {
  const n = closes.length;
  const ret = ind.returns(closes);
  const rsi14 = ind.rsi(closes, 14);
  const { hist } = ind.macd(closes);
  const sma20 = ind.sma(closes, 20);
  const sma50 = ind.sma(closes, 50);
  const vol10 = ind.rollingStd(ret.map((r) => (r === null ? 0 : r)), 10);
  const volSma20 = volumes ? ind.sma(volumes, 20) : null;

  const names = [
    'ret_1', 'ret_2', 'ret_5', 'ret_10',
    'rsi_14', 'macd_hist', 'dist_sma20', 'dist_sma50',
    'volatility_10', 'volume_ratio',
  ];

  const X = new Array(n).fill(null);
  for (let i = 0; i < n; i++) {
    // Necesitamos al menos 50 días de historia (por la SMA50) y todos los indicadores.
    if (
      i < 50 ||
      ret[i] === null || ret[i - 1] === null || rsi14[i] === null ||
      hist[i] === null || sma20[i] === null || sma50[i] === null ||
      vol10[i] === null || closes[i - 10] === undefined
    ) {
      continue;
    }
    const ret2 = closes[i - 2] ? closes[i] / closes[i - 2] - 1 : 0;
    const ret5 = closes[i - 5] ? closes[i] / closes[i - 5] - 1 : 0;
    const ret10 = closes[i - 10] ? closes[i] / closes[i - 10] - 1 : 0;
    let volumeRatio = 0;
    if (volumes && volSma20 && volSma20[i]) {
      volumeRatio = volumes[i] / volSma20[i] - 1;
    }
    X[i] = [
      ret[i],
      ret2,
      ret5,
      ret10,
      (rsi14[i] - 50) / 50, // centrado alrededor de 0
      hist[i] / closes[i], // normalizado por precio
      (closes[i] - sma20[i]) / sma20[i],
      (closes[i] - sma50[i]) / sma50[i],
      vol10[i],
      volumeRatio,
    ];
  }
  return { X, names };
}

// Etiqueta: 1 si el cierre dentro de `horizon` días es mayor que el de hoy, 0 si
// no. Los últimos `horizon` días no tienen etiqueta (no sabemos el futuro).
function buildLabels(closes, horizon = 1) {
  const y = new Array(closes.length).fill(null);
  for (let i = 0; i < closes.length - horizon; i++) {
    y[i] = closes[i + horizon] > closes[i] ? 1 : 0;
  }
  return y;
}

function sigmoid(z) {
  if (z >= 0) return 1 / (1 + Math.exp(-z));
  const e = Math.exp(z);
  return e / (1 + e);
}

// Estandariza cada columna (z-score) usando media y desviación de la muestra dada.
function computeScaler(rows) {
  const d = rows[0].length;
  const mean = new Array(d).fill(0);
  const std = new Array(d).fill(0);
  for (const r of rows) for (let j = 0; j < d; j++) mean[j] += r[j];
  for (let j = 0; j < d; j++) mean[j] /= rows.length;
  for (const r of rows) for (let j = 0; j < d; j++) std[j] += (r[j] - mean[j]) ** 2;
  for (let j = 0; j < d; j++) std[j] = Math.sqrt(std[j] / rows.length) || 1;
  return { mean, std };
}

function applyScaler(row, scaler) {
  return row.map((v, j) => (v - scaler.mean[j]) / scaler.std[j]);
}

// Entrena regresión logística por descenso de gradiente con regularización L2.
// rows: matriz de features (ya filtrada, sin nulls). labels: array 0/1.
function train(rows, labels, opts = {}) {
  const iterations = opts.iterations || 400;
  const lr = opts.lr || 0.1;
  const l2 = opts.l2 || 0.01;
  const scaler = computeScaler(rows);
  const scaled = rows.map((r) => applyScaler(r, scaler));
  const d = scaled[0].length;
  const w = new Array(d).fill(0);
  let b = 0;
  const m = scaled.length;

  for (let it = 0; it < iterations; it++) {
    const gw = new Array(d).fill(0);
    let gb = 0;
    for (let i = 0; i < m; i++) {
      let z = b;
      for (let j = 0; j < d; j++) z += w[j] * scaled[i][j];
      const err = sigmoid(z) - labels[i];
      for (let j = 0; j < d; j++) gw[j] += err * scaled[i][j];
      gb += err;
    }
    for (let j = 0; j < d; j++) {
      w[j] -= lr * (gw[j] / m + l2 * w[j]);
    }
    b -= lr * (gb / m);
  }
  return { w, b, scaler };
}

// Devuelve la probabilidad (0..1) de subida para un vector de features crudo.
function predictOne(modelObj, rawRow) {
  const s = applyScaler(rawRow, modelObj.scaler);
  let z = modelObj.b;
  for (let j = 0; j < s.length; j++) z += modelObj.w[j] * s[j];
  return sigmoid(z);
}

module.exports = {
  buildFeatures,
  buildLabels,
  train,
  predictOne,
  computeScaler,
  applyScaler,
  sigmoid,
};
