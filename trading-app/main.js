// Proceso principal de Electron. Crea la ventana de la app y expone, vía IPC,
// las operaciones pesadas (descarga de datos, entrenamiento, predicción y
// backtest) que corren en Node — nunca en el navegador — para evitar problemas
// de CORS y mantener la lógica en un solo sitio.

'use strict';

const { app, BrowserWindow, ipcMain, shell, Notification, Tray, Menu, dialog, nativeImage } = require('electron');
const path = require('path');
const fs = require('fs');

const data = require('./lib/data');
const model = require('./lib/model');
const ind = require('./lib/indicators');
const backtest = require('./lib/backtest');

const HORIZONS = [1, 5, 10]; // días vista a predecir

// --- Persistencia sencilla en la carpeta de datos del usuario -----------------
function storePath(name) {
  return path.join(app.getPath('userData'), name);
}
function readJson(name, fallback) {
  try {
    return JSON.parse(fs.readFileSync(storePath(name), 'utf8'));
  } catch {
    return fallback;
  }
}
function writeJson(name, value) {
  try {
    fs.writeFileSync(storePath(name), JSON.stringify(value, null, 2));
  } catch (e) {
    console.error('No se pudo guardar', name, e.message);
  }
  return value;
}

const DEFAULT_SETTINGS = { threshold: 0.6, scanIntervalMin: 0 };
function getSettings() { return { ...DEFAULT_SETTINGS, ...readJson('settings.json', {}) }; }
function getWatchlist() {
  const wl = readJson('watchlist.json', []);
  return Array.isArray(wl) ? wl : [];
}

function createWindow() {
  const win = new BrowserWindow({
    width: 1240,
    height: 860,
    minWidth: 960,
    minHeight: 640,
    backgroundColor: '#0e1117',
    title: 'Trading Desk',
    icon: path.join(__dirname, 'build', 'icon.png'),
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });
  win.setMenuBarVisibility(false);
  win.loadFile(path.join(__dirname, 'renderer', 'index.html'));

  win.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url);
    return { action: 'deny' };
  });

  // Si hay escaneo automático activo, cerrar la ventana la esconde en la bandeja
  // en vez de salir, para que las revisiones sigan corriendo en segundo plano.
  win.on('close', (e) => {
    if (!app.isQuitting && getSettings().scanIntervalMin > 0) {
      e.preventDefault();
      win.hide();
      if (!trayHintShown) {
        showNotification('Trading Desk sigue en segundo plano', 'Reviso tu watchlist según el intervalo elegido. Usa el icono de la bandeja para abrir o salir.');
        trayHintShown = true;
      }
    }
  });
  return win;
}

// --- Bandeja del sistema ------------------------------------------------------
let tray = null;
let trayHintShown = false;
function showWindow() {
  let win = BrowserWindow.getAllWindows()[0];
  if (!win || win.isDestroyed()) win = createWindow();
  win.show();
  win.focus();
}
function createTray() {
  try {
    const icon = nativeImage.createFromPath(path.join(__dirname, 'build', 'icon.png'));
    tray = new Tray(icon.isEmpty() ? nativeImage.createEmpty() : icon.resize({ width: 16, height: 16 }));
    tray.setToolTip('Trading Desk');
    tray.setContextMenu(Menu.buildFromTemplate([
      { label: 'Abrir', click: showWindow },
      { label: 'Revisar watchlist ahora', click: () => doScanAndNotify() },
      { type: 'separator' },
      { label: 'Salir', click: () => { app.isQuitting = true; app.quit(); } },
    ]));
    tray.on('click', showWindow);
  } catch (e) {
    console.error('No se pudo crear la bandeja:', e.message);
  }
}

// Descarga el histórico y prepara features una sola vez (compartidas entre
// horizontes, porque las características no dependen del horizonte).
async function loadSeries(provider, symbol) {
  const result = await data.fetchHistory({ provider, symbol });
  const series = data.toSeries(result);
  if (series.closes.length < 120) {
    throw new Error(`Solo se obtuvieron ${series.closes.length} días de "${symbol}". Se necesitan al menos ~120 para analizar. Prueba otra fuente o símbolo.`);
  }
  return { result, series };
}

// Entrena para un horizonte y devuelve la probabilidad para el último día.
function predictHorizon(X, closes, horizon, lastIdx) {
  const y = model.buildLabels(closes, horizon);
  const rows = [], labs = [];
  for (let i = 0; i < closes.length; i++) {
    if (X[i] && y[i] !== null) { rows.push(X[i]); labs.push(y[i]); }
  }
  let prob = null;
  if (rows.length > 30 && lastIdx !== -1) {
    const trained = model.train(rows, labs);
    prob = model.predictOne(trained, X[lastIdx]);
  }
  return { prob, usableCount: rows.length };
}

// Análisis completo (para la vista principal): predicción a 1/5/10 días + backtest.
async function analyze({ provider, symbol }) {
  const { result, series } = await loadSeries(provider, symbol);
  const closes = series.closes;
  const { X } = model.buildFeatures(series.candles);

  let lastIdx = -1;
  for (let i = closes.length - 1; i >= 0; i--) if (X[i]) { lastIdx = i; break; }

  const horizons = [];
  let primaryBacktest = null;
  for (const h of HORIZONS) {
    const { prob, usableCount } = predictHorizon(X, closes, h, lastIdx);
    const minTrain = Math.min(250, Math.floor(usableCount * 0.5));
    const bt = backtest.walkForward(series, { threshold: 0.5, minTrain, retrainEvery: 20, horizon: h });
    horizons.push({
      horizon: h,
      probUp: prob,
      accuracy: bt.error ? null : bt.accuracy,
      edge: bt.error ? null : bt.edge,
      trades: bt.error ? null : bt.trades,
      error: bt.error || null,
    });
    if (h === 1) primaryBacktest = bt;
  }

  return {
    symbol: result.symbol,
    provider: result.provider,
    candles: result.candles,
    indicators: {
      sma20: ind.sma(closes, 20),
      sma50: ind.sma(closes, 50),
      rsi14: ind.rsi(closes, 14),
    },
    horizons,
    prediction: {
      probUp: horizons[0].probUp,
      asOfDate: lastIdx !== -1 ? series.dates[lastIdx] : null,
      lastClose: lastIdx !== -1 ? closes[lastIdx] : null,
    },
    backtest: primaryBacktest,
  };
}

// Análisis rápido (para escanear la watchlist): solo la probabilidad a 1 día,
// sin backtest, para que el escaneo sea ágil.
async function analyzeQuick({ provider, symbol }) {
  const { series } = await loadSeries(provider, symbol);
  const closes = series.closes;
  const { X } = model.buildFeatures(series.candles);
  let lastIdx = -1;
  for (let i = closes.length - 1; i >= 0; i--) if (X[i]) { lastIdx = i; break; }
  const { prob } = predictHorizon(X, closes, 1, lastIdx);
  return { probUp: prob, lastClose: lastIdx !== -1 ? closes[lastIdx] : null, asOfDate: lastIdx !== -1 ? series.dates[lastIdx] : null };
}

// Escaneo de la watchlist (sin notificar; quien llama decide qué hacer).
async function runScan() {
  const wl = getWatchlist();
  const threshold = getSettings().threshold;
  const results = [];
  for (const item of wl) {
    try {
      const q = await analyzeQuick(item);
      const p = q.probUp;
      const alert = p != null && (p >= threshold || p <= 1 - threshold);
      results.push({ ...item, probUp: p, lastClose: q.lastClose, asOfDate: q.asOfDate, alert, error: null });
    } catch (err) {
      results.push({ ...item, probUp: null, alert: false, error: err.message });
    }
  }
  return { threshold, results };
}

function showNotification(title, body) {
  if (Notification.isSupported()) new Notification({ title, body, silent: false }).show();
}
function notifyAlert(r, threshold) {
  const p = r.probUp;
  if (p == null) return;
  const up = p >= 0.5;
  showNotification(
    `${up ? '▲' : '▼'} ${r.symbol.toUpperCase()}: ${((up ? p : 1 - p) * 100).toFixed(0)}% prob. de ${up ? 'subir' : 'bajar'}`,
    `Watchlist · umbral ${Math.round(threshold * 100)}%`
  );
}

// --- Escaneo periódico en segundo plano --------------------------------------
let scanTimer = null;
let scanning = false;
async function doScanAndNotify() {
  if (scanning) return; // evita solapes si una ronda tarda más que el intervalo
  scanning = true;
  try {
    const { threshold, results } = await runScan();
    for (const r of results) if (r.alert) notifyAlert(r, threshold);
    const win = BrowserWindow.getAllWindows()[0];
    if (win && !win.isDestroyed()) win.webContents.send('scan-update', { threshold, results, auto: true, at: Date.now() });
  } catch (e) {
    console.error('Escaneo automático falló:', e.message);
  } finally {
    scanning = false;
  }
}
function rescheduleScan() {
  if (scanTimer) { clearInterval(scanTimer); scanTimer = null; }
  const m = getSettings().scanIntervalMin;
  if (m && m > 0) scanTimer = setInterval(doScanAndNotify, m * 60000);
}

// --- IPC ----------------------------------------------------------------------
ipcMain.handle('analyze', async (_e, args) => {
  try { return { ok: true, data: await analyze(args) }; }
  catch (err) { return { ok: false, error: err.message || String(err) }; }
});

ipcMain.handle('watchlist:get', async () => getWatchlist());
ipcMain.handle('watchlist:add', async (_e, item) => {
  const wl = getWatchlist();
  const exists = wl.some((w) => w.provider === item.provider && w.symbol.toLowerCase() === item.symbol.toLowerCase());
  if (!exists) wl.push({ provider: item.provider, symbol: item.symbol });
  return writeJson('watchlist.json', wl);
});
ipcMain.handle('watchlist:remove', async (_e, item) => {
  const wl = getWatchlist().filter((w) => !(w.provider === item.provider && w.symbol.toLowerCase() === item.symbol.toLowerCase()));
  return writeJson('watchlist.json', wl);
});

ipcMain.handle('settings:get', async () => getSettings());
ipcMain.handle('settings:set', async (_e, patch) => {
  const s = writeJson('settings.json', { ...getSettings(), ...patch });
  if ('scanIntervalMin' in patch) rescheduleScan(); // aplica el nuevo intervalo al momento
  return s;
});
ipcMain.handle('scan-now', async () => { await doScanAndNotify(); return true; });

// Escanea toda la watchlist y devuelve la probabilidad a 1 día de cada símbolo,
// marcando cuáles disparan alerta según el umbral guardado.
ipcMain.handle('scan-watchlist', async () => runScan());

ipcMain.handle('notify', async (_e, { title, body }) => {
  showNotification(title, body);
  return true;
});

// Guarda un CSV (el contenido lo arma el renderer) mediante un diálogo del sistema.
ipcMain.handle('save-csv', async (_e, { filename, content }) => {
  const win = BrowserWindow.getAllWindows()[0];
  const { canceled, filePath } = await dialog.showSaveDialog(win, {
    title: 'Guardar backtest en CSV',
    defaultPath: filename || 'backtest.csv',
    filters: [{ name: 'CSV', extensions: ['csv'] }],
  });
  if (canceled || !filePath) return { ok: false, canceled: true };
  try { fs.writeFileSync(filePath, content, 'utf8'); return { ok: true, path: filePath }; }
  catch (err) { return { ok: false, error: err.message }; }
});

app.whenReady().then(() => {
  createWindow();
  createTray();
  rescheduleScan();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  // Con escaneo automático la ventana se esconde (no se destruye), así que este
  // evento no salta y la app sigue viva en la bandeja. Sin escaneo, se sale.
  if (process.platform !== 'darwin') app.quit();
});

app.on('before-quit', () => { app.isQuitting = true; });
