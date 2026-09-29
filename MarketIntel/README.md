# MarketIntel — Plataforma de Inteligencia de Mercado

Plataforma que **estima probabilidades de escenarios** (alcista / bajista / lateral) por
múltiples horizontes de tiempo, en lugar de predecir un precio exacto. Varios factores
independientes se combinan en un **motor de decisión** transparente que produce una señal
0-100 con su explicación, escenarios y gestión de riesgo — y sabe decir **NO OPERAR**.

> ⚠️ Muestra probabilidades estadísticas con fines informativos. **No es asesoramiento de
> inversión** ni ejecuta órdenes.

## Arquitectura

```
MAUI App (iOS / Android)  ──HTTPS──▶  ASP.NET Core API (.NET 10, C#)
  Dashboard · Detalle                 Datos → Indicadores → Régimen → Señal → Riesgo → Scanner
                                              │
                                       IMarketDataProvider  ◀── hoy: Mock sintético
                                                                 mañana: Alpaca / Polygon / ...
```

Todo en **C#**. Los modelos de ML pesados (fase V2) se entrenan aparte y se exportan a
**ONNX** para inferencia dentro del backend, sin cambiar de lenguaje.

## Proyectos

| Proyecto | Rol |
|---|---|
| `src/MarketIntel.Shared` | Contratos (DTOs) y enums compartidos entre backend y app |
| `src/MarketIntel.Api` | El "cerebro": datos, indicadores y los motores de decisión |
| `src/MarketIntel.Maui` | App móvil (iOS/Android) que consume el API |

### Motores del backend (`MarketIntel.Api/Engines`)

- **IndicatorEngine** — EMA/SMA, RSI, MACD, ATR, VWAP, Bollinger, volumen relativo, ADX.
- **SignalEngine** — combina 6 factores (tendencia, momentum, VWAP, volumen, fuerza,
  contexto) en un score transparente 0-100, probabilidades por horizonte, escenarios y la
  lógica de **NO-TRADE**.
- **RiskEngine** — nivel de riesgo, stop/objetivo por ATR, R/R y tamaño de posición sugerido.
- **MarketRegimeEngine** — contexto de mercado desde SPY/QQQ/IWM/DIA/VIX + un sesgo global.
- **EnsembleEngine** (motor de predicción) — trata cada ángulo (técnico, momentum, volumen,
  mercado, volatilidad, IA) como un **modelo independiente** que emite una probabilidad alcista,
  y los combina con **pesos explícitos** en un score de consenso, mostrando cada opinión.
- **SupportResistanceEngine** — detecta zonas de soporte/resistencia (no líneas) por swings
  fractales agrupados; fuerza = número de reacciones del precio.
- **PricePatternEngine** — detecta patrones de vela (envolvente, martillo, estrella fugaz) y
  rupturas, y calcula su **tasa de acierto empírica** sobre el histórico del propio símbolo
  (mini-backtest), en vez de asumir el sesgo por el nombre del patrón.
- **EventRiskEngine** + **IEventCalendar** — calendario económico (CPI, FOMC, NFP…) y earnings
  (`MockEventCalendar`, sustituible por Finnhub/Trading Economics). Eventos importantes cercanos
  **bajan la confianza o fuerzan NO-TRADE** (no operar justo antes de earnings o de un dato macro).
- **MarketStructureEngine** — etiqueta los swings como HH/HL/LH/LL, deduce la tendencia, detecta
  rango/consolidación y rupturas de estructura (BOS).
- **MarketAnalysisService** — orquesta el pipeline y expone la superficie que usan los endpoints.

En el cliente MAUI, la **watchlist** y las **reglas de alerta** se guardan localmente
(`Preferences`, vía `WatchlistStore`). Las alertas se evalúan en cada refresco contra el scanner
y la watchlist; las notificaciones push en background (Firebase/APNs) son de la fase de escalado.

### Machine learning (V2, `MarketIntel.Api/Ml`)

- **FeatureExtractor** — convierte las velas en vectores de features causales (sin look-ahead).
- **LogisticRegressionModel** — clasificador entrenado en C# por descenso de gradiente, con
  estandarización de features. Implementa `IPredictor`.
- **ModelService** — entrena un modelo por símbolo con **separación temporal train/test**
  (entrena en el pasado, mide precisión en el tramo más reciente = fuera de muestra) y cachea a diario.
- **BacktestEngine** — backtest formal de la estrategia del modelo: win rate, profit factor,
  drawdown, Sharpe y retorno total, sobre operaciones no solapadas fuera de muestra.
- **IPredictor** — abstracción del modelo. Un modelo pesado entrenado aparte (XGBoost/LSTM/
  Transformer) se exporta a **ONNX** y se enchufa como `OnnxPredictor` sin tocar el resto.

> Nota honesta: sobre los **datos sintéticos** (paseo aleatorio) no hay patrón que aprender, así
> que la precisión ronda el 50% y el backtest no es rentable. Es el comportamiento correcto y
> justo lo que el backtesting debe exponer; con datos reales el modelo puede (o no) hallar ventaja.

## Endpoints

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/health` | Estado del servicio |
| GET | `/api/meta` | Proveedor de datos + aviso legal |
| GET | `/api/market/regime` | Régimen de mercado e índices |
| GET | `/api/calendar/upcoming?hours=N` | Eventos próximos (macro + earnings) |
| GET | `/api/signals/{symbol}` | Señal completa de un símbolo |
| GET | `/api/scanner/top?take=N` | Mejores oportunidades |
| GET | `/api/watchlist?symbols=A,B,C` | Cotizaciones compactas (score/dirección) de varios símbolos |
| GET | `/api/backtest/{symbol}` | Backtest fuera de muestra de la estrategia del modelo IA |
| GET | `/api/options/{symbol}` | Cadena de opciones: expected move, mejores contratos, actividad inusual |
| GET | `/api/assistant/{symbol}?q=...` | Asistente que responde desde los datos del sistema |
| GET | `/api/accuracy` | Precisión histórica del sistema por horizonte y por símbolo |
| GET | `/api/calibration` | Calibración walk-forward: acierto real vs. predicho por grado |
| GET | `/api/registry` | Registro vivo de señales emitidas y su resultado |
| GET | `/api/news/{symbol}` | Noticias + análisis de sentimiento agregado |
| POST | `/api/devices` · `/api/notify/test` | Registro de token push / envío de prueba |
| GET | `/api/ready` | Sonda de readiness para orquestadores |
| GET | `/api/candles/{symbol}?tf=H1&count=200` | Velas OHLCV |

> **Build personal:** esta versión es de un solo usuario. No hay niveles de
> suscripción, ni API keys, ni límites de uso: **todas las funciones están
> desbloqueadas** (opciones, backtest y scanner sin restricciones). Los endpoints
> `/api/plan`, `/api/plans` y `/api/keys` se han eliminado.

`{symbol}` acepta **acciones** (`AAPL`), **ETF** (`SPY`, `QQQ`, `GLD`…) y
**cripto** en forma sin barra (`BTCUSD`, `ETHUSD`, `SOLUSD`). El backend enruta
la cripto al feed 24/7 de Alpaca automáticamente.

## Cómo ejecutar

### 1. Backend

```bash
dotnet run --project src/MarketIntel.Api --urls http://localhost:5080
```

Prueba: `http://localhost:5080/api/signals/NVDA`

### 2. App móvil

- **Android** (emulador): la app apunta a `http://10.0.2.2:5080` automáticamente.
  Requiere el Android SDK (ver más abajo).

  ```bash
  dotnet build src/MarketIntel.Maui -f net10.0-android -t:Run
  ```

- **iOS**: requiere un Mac como build host (limitación de Apple). El código C# es idéntico.

- **Windows** (verificación rápida de UI, apunta a `http://localhost:5080`):

  ```bash
  dotnet build src/MarketIntel.Maui -f net10.0-windows10.0.19041.0
  ```

### Android SDK (una sola vez)

El workload de MAUI instala los *bindings* de .NET para Android, pero no el SDK de Google.
Para instalarlo desde la línea de comandos:

```bash
dotnet build src/MarketIntel.Maui -f net10.0-android -t:InstallAndroidDependencies -p:AcceptAndroidSDKLicenses=True
```

## Conectar datos reales (Alpaca)

El `AlpacaMarketDataProvider` ya está implementado. El backend elige el proveedor **solo.**:
si detecta claves de Alpaca en la configuración usa datos reales; si no, usa el mock. No hay
que tocar código.

Pasos (una sola vez):

1. Crea una cuenta gratuita en <https://alpaca.markets> (una cuenta *paper* basta).
2. En el panel, genera una **API Key ID** y una **Secret Key**.
3. Guárdalas de forma segura con user-secrets (quedan **fuera del repo**):

   ```bash
   dotnet user-secrets --project src/MarketIntel.Api set "Alpaca:ApiKeyId" "TU_KEY_ID"
   dotnet user-secrets --project src/MarketIntel.Api set "Alpaca:ApiSecretKey" "TU_SECRET_KEY"
   ```

   (Alternativa sin user-secrets: variables de entorno `Alpaca__ApiKeyId` y `Alpaca__ApiSecretKey`.)
4. Reinicia el API. El endpoint `/api/meta` pasará a `synthetic:false` y el banner de la app
   mostrará **"Datos en vivo · Alpaca (iex)"**.

Notas del tier gratuito: usa el feed **IEX** (retardo de ~15 min) y el índice VIX se sustituye
por el ETF **VIXY**. Para reemplazar Alpaca por otro proveedor (Polygon, Finnhub…), implementa
`IMarketDataProvider` y regístralo en `Program.cs`.

## Escalado y despliegue (V4)

- **Uso personal (un solo usuario)**: se ha retirado todo el sistema de suscripciones/pago
  (niveles, `X-Api-Key`, rate limiting y los endpoints de planes/keys). **Todas las funciones
  están desbloqueadas**; no hay paywalls ni límites de uso.
- **Caché de salida** en los endpoints globales (rendimiento, no restricción).
- **Contenedorización**: `Dockerfile` (multi-stage) + `docker-compose.yml` (API stateless +
  TimescaleDB + Redis). `docker compose up --build`. El API es horizontalmente escalable.
- **Notificaciones**:
  - **En el dispositivo (funciona ya)**: al saltar una regla de alerta, la app muestra una
    **notificación nativa de Android** (`ILocalNotifier`, permiso `POST_NOTIFICATIONS`). Validado
    en el emulador.
  - **Push remoto en background**: el servidor incluye un emisor **FCM HTTP v1 real**
    (`FcmNotificationService` con Google service-account), activado por configuración
    (`Fcm:ProjectId` + `Fcm:ServiceAccountPath`); si no, usa el logger. El cliente Android trae
    el receptor FCM (`MarketIntelFirebaseMessagingService`, tras el flag `-p:UseFirebase=true`).
    **Guía completa paso a paso: [`docs/FIREBASE_SETUP.md`](docs/FIREBASE_SETUP.md)** (crear el
    proyecto, `google-services.json`, service-account). iOS añade APNs y requiere Mac.

## Clases de activo

El motor trabaja sobre velas OHLCV, así que es **agnóstico al activo**. Cubre:

- **Acciones** (`AAPL`, `NVDA`, `TSLA`…) — feed de acciones de Alpaca.
- **ETF** (`SPY`, `QQQ`, `GLD`, `TLT`, `SMH`…) — mismo feed que las acciones.
- **Cripto** (`BTCUSD`, `ETHUSD`, `SOLUSD`…) — feed de cripto 24/7 de Alpaca. Se usa la forma
  **sin barra** en toda la app y se convierte a `BTC/USD` solo al llamar a Alpaca (`AssetClass`).

`AssetClass` clasifica cada símbolo (acción/ETF/cripto), normaliza la cripto y define el universo
del scanner. Las ETF y la cripto no generan "earnings" (no hay riesgo de resultados empresariales).
**Futuros**: pendientes — requieren un proveedor de datos aparte (de pago).

## Hoja de ruta

- **V1** — datos, indicadores, señal probabilística transparente, riesgo, scanner,
  régimen de mercado, dashboard + detalle en la app.
- **V2 — IA** — proveedor de datos real, sentimiento de noticias (NLP), ensemble ML (ONNX),
  confianza calibrada.
- **V3 — Profesional** — opciones y actividad inusual, backtesting, paper trading, alertas.
- **Personal (actual)** — build de un solo usuario: sin suscripciones ni paywalls, todo
  desbloqueado; acciones + ETF + cripto; idiomas inglés y español.
