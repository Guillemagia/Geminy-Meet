// Proceso principal de Electron. Crea la ventana de la app y expone, vía IPC,
// las operaciones pesadas (descarga de datos, entrenamiento, predicción y
// backtest) que corren en Node — nunca en el navegador — para evitar problemas
// de CORS y mantener la lógica en un solo sitio.

'use strict';

const { app, BrowserWindow, ipcMain, shell } = require('electron');
const path = require('path');

const data = require('./lib/data');
const model = require('./lib/model');
const ind = require('./lib/indicators');
const backtest = require('./lib/backtest');

function createWindow() {
  const win = new BrowserWindow({
    width: 1180,
    height: 820,
    minWidth: 900,
    minHeight: 640,
    backgroundColor: '#0e1117',
    title: 'Trading Desk',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });
  win.setMenuBarVisibility(false);
  win.loadFile(path.join(__dirname, 'renderer', 'index.html'));

  // Abre enlaces externos en el navegador del sistema, no dentro de la app.
  win.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url);
    return { action: 'deny' };
  });
}

// Analiza un símbolo: descarga histórico, entrena el modelo, predice el próximo
// movimiento y corre el backtest. Devuelve todo lo que la UI necesita.
async function analyze({ provider, symbol, threshold }) {
  const th = typeof threshold === 'number' ? threshold : 0.5;
  const result = await data.fetchHistory({ provider, symbol });
  const series = data.toSeries(result);
  const closes = series.closes;
  const volumes = series.volumes;

  if (closes.length < 120) {
    throw new Error(`Solo se obtuvieron ${closes.length} días de "${symbol}". Se necesitan al menos ~120 para analizar. Prueba otra fuente o símbolo.`);
  }

  // Entrenamiento con todo el histórico usable para la predicción "en vivo".
  const { X } = model.buildFeatures(closes, volumes);
  const y = model.buildLabels(closes);
  const rows = [], labs = [];
  for (let i = 0; i < closes.length; i++) {
    if (X[i] && y[i] !== null) { rows.push(X[i]); labs.push(y[i]); }
  }
  let prob = null, lastIdx = -1;
  if (rows.length > 30) {
    const trained = model.train(rows, labs);
    for (let i = closes.length - 1; i >= 0; i--) if (X[i]) { lastIdx = i; break; }
    if (lastIdx !== -1) prob = model.predictOne(trained, X[lastIdx]);
  }

  // Backtest walk-forward. minTrain se adapta a la cantidad de datos.
  const minTrain = Math.min(250, Math.floor(rows.length * 0.5));
  const bt = backtest.walkForward({ closes, volumes }, { threshold: th, minTrain, retrainEvery: 20 });

  // Indicadores para los gráficos.
  const sma20 = ind.sma(closes, 20);
  const sma50 = ind.sma(closes, 50);
  const rsi14 = ind.rsi(closes, 14);

  return {
    symbol: result.symbol,
    provider: result.provider,
    candles: result.candles,
    indicators: { sma20, sma50, rsi14 },
    prediction: {
      probUp: prob,
      asOfDate: lastIdx !== -1 ? series.dates[lastIdx] : null,
      lastClose: lastIdx !== -1 ? closes[lastIdx] : null,
    },
    backtest: bt,
  };
}

ipcMain.handle('analyze', async (_e, args) => {
  try {
    return { ok: true, data: await analyze(args) };
  } catch (err) {
    return { ok: false, error: err.message || String(err) };
  }
});

app.whenReady().then(() => {
  createWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
