// Capa de datos de mercado. Descarga precios históricos de varias fuentes
// gratuitas y sin clave de API, y los normaliza a un formato común:
//   { symbol, provider, candles: [{ date, open, high, low, close, volume }] }
//
// Fuentes soportadas:
//   - 'stooq'     acciones/índices mundiales (CSV).           ej. AAPL, ^SPX, SAN.MC
//   - 'yahoo'     acciones/índices (JSON).                    ej. AAPL, MSFT, ^GSPC
//   - 'coingecko' criptomonedas por id de CoinGecko.          ej. bitcoin, ethereum
//   - 'binance'   criptomonedas par USDT.                     ej. BTCUSDT, ETHUSDT
//
// Usa el `fetch` nativo de Node/Electron (Node 18+). Sin dependencias externas.

'use strict';

async function getJson(url) {
  const res = await fetch(url, { headers: { 'User-Agent': 'Mozilla/5.0 TradingDeskApp' } });
  if (!res.ok) throw new Error(`HTTP ${res.status} al pedir datos`);
  return res.json();
}

async function getText(url) {
  const res = await fetch(url, { headers: { 'User-Agent': 'Mozilla/5.0 TradingDeskApp' } });
  if (!res.ok) throw new Error(`HTTP ${res.status} al pedir datos`);
  return res.text();
}

// --- Stooq (acciones/índices) -------------------------------------------------
// Símbolos US: aapl.us, msft.us ; índices: ^spx ; España (BME): san.mc, etc.
function stooqSymbol(sym) {
  const s = sym.trim().toLowerCase();
  if (s.startsWith('^')) return s; // índice
  if (s.includes('.')) return s; // ya trae sufijo de mercado (p.ej. san.mc)
  return s + '.us'; // por defecto, mercado de EE. UU.
}

async function fetchStooq(symbol) {
  const url = `https://stooq.com/q/d/l/?s=${encodeURIComponent(stooqSymbol(symbol))}&i=d`;
  const csv = await getText(url);
  const lines = csv.trim().split('\n');
  if (lines.length < 2 || !/^Date/i.test(lines[0])) {
    throw new Error(`Stooq no devolvió datos para "${symbol}". ¿Símbolo correcto? (ej. AAPL, ^SPX, SAN.MC)`);
  }
  const candles = [];
  for (let i = 1; i < lines.length; i++) {
    const [date, open, high, low, close, volume] = lines[i].split(',');
    if (!close || close === 'null') continue;
    candles.push({
      date,
      open: +open, high: +high, low: +low, close: +close,
      volume: volume ? +volume : 0,
    });
  }
  return { symbol, provider: 'stooq', candles };
}

// --- Yahoo Finance (acciones/índices) ----------------------------------------
async function fetchYahoo(symbol, range = '5y') {
  const url = `https://query1.finance.yahoo.com/v8/finance/chart/${encodeURIComponent(symbol)}?range=${range}&interval=1d`;
  const data = await getJson(url);
  const result = data?.chart?.result?.[0];
  if (!result) throw new Error(`Yahoo no devolvió datos para "${symbol}".`);
  const ts = result.timestamp || [];
  const q = result.indicators.quote[0];
  const candles = [];
  for (let i = 0; i < ts.length; i++) {
    if (q.close[i] == null) continue;
    candles.push({
      date: new Date(ts[i] * 1000).toISOString().slice(0, 10),
      open: q.open[i], high: q.high[i], low: q.low[i], close: q.close[i],
      volume: q.volume[i] || 0,
    });
  }
  return { symbol, provider: 'yahoo', candles };
}

// --- CoinGecko (cripto) ------------------------------------------------------
// Devuelve cierres y volúmenes diarios. `symbol` es el id de CoinGecko.
async function fetchCoinGecko(symbol, days = 'max') {
  const id = symbol.trim().toLowerCase();
  const url = `https://api.coingecko.com/api/v3/coins/${encodeURIComponent(id)}/market_chart?vs_currency=usd&days=${days}&interval=daily`;
  const data = await getJson(url);
  if (!data.prices) throw new Error(`CoinGecko no devolvió datos para "${symbol}". Usa el id (ej. bitcoin, ethereum).`);
  const vols = data.total_volumes || [];
  const candles = data.prices.map((p, i) => {
    const close = p[1];
    return {
      date: new Date(p[0]).toISOString().slice(0, 10),
      open: close, high: close, low: close, close, // CoinGecko market_chart solo da cierre
      volume: vols[i] ? vols[i][1] : 0,
    };
  });
  return { symbol, provider: 'coingecko', candles };
}

// --- Binance (cripto) --------------------------------------------------------
async function fetchBinance(symbol, interval = '1d', limit = 1000) {
  const s = symbol.trim().toUpperCase();
  const url = `https://api.binance.com/api/v3/klines?symbol=${encodeURIComponent(s)}&interval=${interval}&limit=${limit}`;
  const data = await getJson(url);
  if (!Array.isArray(data)) throw new Error(`Binance no devolvió datos para "${symbol}" (ej. BTCUSDT).`);
  const candles = data.map((k) => ({
    date: new Date(k[0]).toISOString().slice(0, 10),
    open: +k[1], high: +k[2], low: +k[3], close: +k[4], volume: +k[5],
  }));
  return { symbol, provider: 'binance', candles };
}

// Punto de entrada unificado.
async function fetchHistory({ provider, symbol, range, days }) {
  switch (provider) {
    case 'stooq': return fetchStooq(symbol);
    case 'yahoo': return fetchYahoo(symbol, range || '5y');
    case 'coingecko': return fetchCoinGecko(symbol, days || 'max');
    case 'binance': return fetchBinance(symbol, '1d', 1000);
    default: throw new Error(`Fuente desconocida: ${provider}`);
  }
}

// Extrae arrays paralelos de un resultado de candles.
function toSeries(result) {
  return {
    dates: result.candles.map((c) => c.date),
    closes: result.candles.map((c) => c.close),
    volumes: result.candles.map((c) => c.volume),
    candles: result.candles,
  };
}

module.exports = {
  fetchHistory, fetchStooq, fetchYahoo, fetchCoinGecko, fetchBinance, toSeries,
};
