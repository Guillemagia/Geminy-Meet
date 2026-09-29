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
- **Predicción a 1, 5 y 10 días**: regresión logística (JS puro) sobre 19
  características: momento (retornos a 1/2/5/10 días, RSI, MACD, distancia a medias
  móviles, volatilidad, volumen), **señales de vela** (tamaño de cuerpo, mechas,
  hueco de apertura, posición del cierre en el rango) y **tendencia larga +
  estacionalidad** (distancia a la SMA100, día de la semana). Devuelve la
  probabilidad de subida en cada horizonte, cada una con su tasa de acierto medida.
- **Backtest walk-forward**: entrena solo con el pasado y predice hacia delante,
  avanzando en el tiempo (sin hacer trampa con datos del futuro). Compara el acierto
  del modelo con una regla tonta y con "comprar y mantener".
- **Exportar a CSV**: guarda el backtest día a día (fecha, cierre, probabilidad,
  predicción, resultado real, curva de capital) para analizarlo en Excel.
- **Watchlist**: guarda tus símbolos favoritos (se conservan entre sesiones) y
  cárgalos con un clic. El botón "Revisar watchlist" analiza todos de golpe.
- **Alertas**: fija un umbral de probabilidad; cuando un símbolo lo supera (al
  analizarlo o al revisar la watchlist), salta una **notificación del sistema**.
- **Escaneo automático en segundo plano**: elige un intervalo (5/15/30/60 min) y la
  app revisa tu watchlist sola y te avisa. Con el escaneo activo, cerrar la ventana
  la envía a la **bandeja del sistema** (icono junto al reloj) y sigue vigilando;
  desde ahí puedes abrirla, revisar al instante o salir.
- **Gráficos**: velas con medias móviles (SMA 20/50) y curva de capital.
- **Icono propio** para la app y el instalador.

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

## Notas de uso

- La **watchlist** y el **umbral de alerta** se guardan en la carpeta de datos de la
  app (`%AppData%/Trading Desk` en Windows), así que persisten entre reinicios.
- Las **notificaciones** usan el sistema de avisos de Windows; la primera vez puede
  pedirte permiso para mostrar notificaciones.
- El icono se genera con `node build/make-icon.js` (ya está incluido; solo necesitas
  regenerarlo si quieres cambiar el diseño).

## Ideas para más adelante

- Señales macro externas (VIX, tipos de interés, índice del dólar) cruzando varias
  fuentes por fecha.
- Comparar varios modelos y elegir el mejor por horizonte automáticamente.
- Panel de histórico de alertas.

Dime cuál te interesa y seguimos.
