// Indicadores técnicos, escritos a mano con JS puro (sin dependencias).
// Todas las funciones reciben un array de números (normalmente precios de cierre)
// y devuelven un array del mismo largo, con `null` en las posiciones donde
// todavía no hay suficientes datos para calcular el valor.

'use strict';

// Media móvil simple (SMA) de periodo `p`.
function sma(values, p) {
  const out = new Array(values.length).fill(null);
  let sum = 0;
  for (let i = 0; i < values.length; i++) {
    sum += values[i];
    if (i >= p) sum -= values[i - p];
    if (i >= p - 1) out[i] = sum / p;
  }
  return out;
}

// Media móvil exponencial (EMA) de periodo `p`.
function ema(values, p) {
  const out = new Array(values.length).fill(null);
  const k = 2 / (p + 1);
  let prev = null;
  for (let i = 0; i < values.length; i++) {
    const v = values[i];
    if (prev === null) {
      // Arrancamos la EMA con una SMA de los primeros `p` valores.
      if (i >= p - 1) {
        let sum = 0;
        for (let j = i - p + 1; j <= i; j++) sum += values[j];
        prev = sum / p;
        out[i] = prev;
      }
    } else {
      prev = v * k + prev * (1 - k);
      out[i] = prev;
    }
  }
  return out;
}

// RSI (Relative Strength Index) de periodo `p` (por defecto 14).
// Usa el suavizado de Wilder. Devuelve valores entre 0 y 100.
function rsi(values, p = 14) {
  const out = new Array(values.length).fill(null);
  if (values.length <= p) return out;
  let gain = 0;
  let loss = 0;
  for (let i = 1; i <= p; i++) {
    const ch = values[i] - values[i - 1];
    if (ch >= 0) gain += ch;
    else loss -= ch;
  }
  let avgGain = gain / p;
  let avgLoss = loss / p;
  out[p] = avgLoss === 0 ? 100 : 100 - 100 / (1 + avgGain / avgLoss);
  for (let i = p + 1; i < values.length; i++) {
    const ch = values[i] - values[i - 1];
    const g = ch >= 0 ? ch : 0;
    const l = ch < 0 ? -ch : 0;
    avgGain = (avgGain * (p - 1) + g) / p;
    avgLoss = (avgLoss * (p - 1) + l) / p;
    out[i] = avgLoss === 0 ? 100 : 100 - 100 / (1 + avgGain / avgLoss);
  }
  return out;
}

// MACD: devuelve { macd, signal, hist } (tres arrays).
function macd(values, fast = 12, slow = 26, signalP = 9) {
  const emaFast = ema(values, fast);
  const emaSlow = ema(values, slow);
  const macdLine = values.map((_, i) =>
    emaFast[i] === null || emaSlow[i] === null ? null : emaFast[i] - emaSlow[i]
  );
  // La señal es una EMA de la línea MACD, calculada solo donde hay valores.
  const firstValid = macdLine.findIndex((v) => v !== null);
  const signal = new Array(values.length).fill(null);
  const hist = new Array(values.length).fill(null);
  if (firstValid !== -1) {
    const compact = macdLine.slice(firstValid).map((v) => (v === null ? 0 : v));
    const sig = ema(compact, signalP);
    for (let i = 0; i < sig.length; i++) {
      const idx = firstValid + i;
      if (sig[i] !== null) {
        signal[idx] = sig[i];
        hist[idx] = macdLine[idx] - sig[i];
      }
    }
  }
  return { macd: macdLine, signal, hist };
}

// Desviación estándar de los últimos `p` valores en cada punto.
function rollingStd(values, p) {
  const out = new Array(values.length).fill(null);
  for (let i = p - 1; i < values.length; i++) {
    let mean = 0;
    for (let j = i - p + 1; j <= i; j++) mean += values[j];
    mean /= p;
    let variance = 0;
    for (let j = i - p + 1; j <= i; j++) variance += (values[j] - mean) ** 2;
    out[i] = Math.sqrt(variance / p);
  }
  return out;
}

// Retornos diarios (porcentaje en decimal). El primer valor es null.
function returns(values) {
  const out = new Array(values.length).fill(null);
  for (let i = 1; i < values.length; i++) {
    out[i] = values[i - 1] === 0 ? 0 : values[i] / values[i - 1] - 1;
  }
  return out;
}

module.exports = { sma, ema, rsi, macd, rollingStd, returns };
