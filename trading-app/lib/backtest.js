// Backtesting honesto con validación "walk-forward": entrenamos solo con el
// pasado y predecimos el día siguiente, avanzando en el tiempo. Nunca usamos
// datos del futuro para entrenar (eso sería trampa y daría resultados irreales).
//
// Devuelve métricas medidas de verdad: acierto del modelo vs. una línea base
// tonta, y la curva de capital de una estrategia simple comparada con
// "comprar y mantener".

'use strict';

const model = require('./model');

// series: { closes:number[], volumes?:number[] }
// opts: { threshold:number (prob mínima para "apostar a subida"),
//         minTrain:number (días mínimos antes de empezar a evaluar),
//         retrainEvery:number (cada cuántos días reentrenar),
//         horizon:number (a cuántos días vista se predice; 1 = día siguiente) }
// La curva de capital solo se calcula para horizon=1 (estrategia diaria sin
// solapamiento de posiciones); para horizontes mayores se mide solo el acierto.
function walkForward(series, opts = {}) {
  const closes = series.closes;
  const volumes = series.volumes;
  const threshold = opts.threshold ?? 0.5;
  const minTrain = opts.minTrain ?? 250;
  const retrainEvery = opts.retrainEvery ?? 20;
  const horizon = opts.horizon ?? 1;

  const { X } = model.buildFeatures(closes, volumes);
  const y = model.buildLabels(closes, horizon);

  // Índices utilizables: tienen features y etiqueta.
  const usable = [];
  for (let i = 0; i < closes.length; i++) {
    if (X[i] && y[i] !== null) usable.push(i);
  }
  if (usable.length < minTrain + 30) {
    return { error: 'No hay suficientes datos históricos para un backtest fiable.' };
  }

  let correct = 0;
  let total = 0;
  let baselineCorrect = 0; // línea base: predecir siempre "sube"
  let upCount = 0;
  const predictions = []; // { index, prob, actual }
  let equityStrategy = 1; // capital de la estrategia (empieza en 1)
  let equityHold = 1; // comprar y mantener
  const equityCurve = []; // { i, strategy, hold }

  let trained = null;
  let sinceTrain = 1e9;
  const startEval = usable[minTrain];

  for (let k = minTrain; k < usable.length; k++) {
    const i = usable[k];

    // Reentrenar periódicamente usando SOLO datos anteriores a `i`.
    if (sinceTrain >= retrainEvery || !trained) {
      const rows = [];
      const labs = [];
      for (let t = 0; t < k; t++) {
        const idx = usable[t];
        rows.push(X[idx]);
        labs.push(y[idx]);
      }
      trained = model.train(rows, labs);
      sinceTrain = 0;
    }
    sinceTrain++;

    const prob = model.predictOne(trained, X[i]);
    const actual = y[i]; // ¿subió al día siguiente?
    const predUp = prob >= threshold;

    if (predUp === (actual === 1)) correct++;
    total++;
    if (actual === 1) { baselineCorrect++; upCount++; }

    // Estrategia (solo para horizon=1, para evitar posiciones solapadas): si el
    // modelo dice "sube", nos ponemos largos y capturamos el retorno del día
    // siguiente; si no, quedamos en liquidez.
    if (horizon === 1) {
      const nextRet = closes[i + 1] / closes[i] - 1;
      if (predUp) equityStrategy *= 1 + nextRet;
      equityHold *= 1 + nextRet;
      equityCurve.push({ i, strategy: equityStrategy, hold: equityHold });
    }

    predictions.push({ index: i, prob, actual });
  }

  const accuracy = total ? correct / total : 0;
  // La línea base "siempre sube" acierta tantas veces como días subieron.
  const baselineAcc = total ? baselineCorrect / total : 0;
  // También la base "clase mayoritaria" (lo que más se repite).
  const majorityAcc = Math.max(baselineAcc, 1 - baselineAcc);

  return {
    horizon,
    accuracy,
    baselineAlwaysUp: baselineAcc,
    baselineMajority: majorityAcc,
    edge: accuracy - majorityAcc, // ventaja real sobre adivinar la clase mayoritaria
    trades: total,
    equityStrategy,
    equityHold,
    equityCurve,
    predictions,
    startEvalIndex: startEval,
  };
}

module.exports = { walkForward };
