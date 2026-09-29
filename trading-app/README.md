# Trading Desk — app de escritorio personal

Aplicación de escritorio (Windows) para **estudiar mercados**: descarga precios
históricos de acciones, índices o criptomonedas, calcula indicadores técnicos,
entrena un modelo que estima la **probabilidad** del próximo movimiento y — lo más
importante — mide con un **backtest honesto** si esa señal habría acertado más que
el azar.

> ⚠️ **No es asesoramiento financiero.** Ningún modelo predice de forma fiable el
> mercado. Esta herramienta es para aprender y experimentar, no para decidir
> inversiones con dinero real. El backtest no incluye comisiones, impuestos ni
> deslizamiento (*slippage*).

## Qué hace

- **Fuentes de datos gratis y sin clave de API:**
  - `Stooq` — acciones e índices mundiales (ej. `AAPL`, `^SPX`, `SAN.MC`).
  - `Yahoo Finance` — acciones e índices (ej. `AAPL`, `^GSPC`).
  - `CoinGecko` — cripto por su id (ej. `bitcoin`, `ethereum`). *Gratis: últimos 365 días.*
  - `Binance` — cripto en pares USDT (ej. `BTCUSDT`).
- **Predicción**: regresión logística (JS puro) sobre 10 características técnicas
  (retornos a 1/2/5/10 días, RSI, MACD, distancia a medias móviles, volatilidad,
  volumen). Devuelve la probabilidad de que el próximo cierre suba.
- **Backtest walk-forward**: entrena solo con el pasado y predice el día siguiente,
  avanzando en el tiempo (sin hacer trampa con datos del futuro). Compara el acierto
  del modelo con una regla tonta y con "comprar y mantener".
- **Gráficos**: velas con medias móviles (SMA 20/50) y curva de capital.

Todo corre **en local**: los datos se descargan de las APIs públicas, pero no se
envía nada tuyo a ningún servidor.

## Cómo ejecutarla en tu ordenador

Necesitas [Node.js](https://nodejs.org) 18 o superior (mejor la versión LTS).

```bash
cd trading-app
npm install      # instala Electron (solo la primera vez)
npm start        # abre la app
```

## Cómo generar el instalador .exe (Windows)

Ejecuta esto **en tu PC de Windows** (la compilación del `.exe` es más fiable ahí
que en Linux/Mac):

```bash
cd trading-app
npm install
npm run dist
```

El instalador quedará en la carpeta `dist/` (un `.exe` tipo NSIS que instala
"Trading Desk" con su icono y acceso directo). Doble clic para instalar; luego se
abre como cualquier programa de Windows.

> Si solo quieres una carpeta portable sin instalador, usa `npm run pack` — deja la
> app lista en `dist/win-unpacked/`.

## Estructura del proyecto

```
trading-app/
├─ main.js            Proceso principal de Electron (ventana + IPC)
├─ preload.js         Puente seguro renderer ↔ Node
├─ lib/
│  ├─ data.js         Descarga y normaliza datos de las 4 fuentes
│  ├─ indicators.js   SMA, EMA, RSI, MACD, volatilidad, retornos
│  ├─ model.js        Regresión logística + ingeniería de características
│  └─ backtest.js     Validación walk-forward + curva de capital
└─ renderer/
   ├─ index.html      Interfaz
   ├─ styles.css      Estilos (tema oscuro)
   ├─ app.js          Lógica de la UI
   └─ chart.js        Gráficos en canvas (sin librerías)
```

## Ideas para más adelante

- Guardar una lista de seguimiento (*watchlist*) de tus símbolos favoritos.
- Alertas cuando la probabilidad supere un umbral.
- Más características al modelo (patrones de velas, datos macro).
- Predicción a varios días vista, no solo al día siguiente.

Dime cuál te interesa y seguimos.
