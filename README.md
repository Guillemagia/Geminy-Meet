# Geminy Meet — Backend real

Este backend reemplaza la versión "todo en el navegador" del prototipo anterior. Ahora:

- Los usuarios se registran e inician sesión de verdad (contraseñas con hash `scrypt`, nunca en texto plano).
- Los créditos y las ganancias viven en una base de datos en el servidor (`geminy.db`), no en `localStorage`.
- El chat, las fotos/videos y el cobro por minuto de llamada pasan por el servidor, que es quien decide si hay saldo o no — el navegador ya no puede "inventarse" créditos.
- La videollamada usa WebRTC real (cámara/micrófono de verdad); el servidor solo transporta la señalización, el video nunca pasa por él.
- Nota técnica: este entorno de desarrollo no tenía acceso a internet para instalar paquetes de npm, así que el servidor está escrito solo con módulos nativos de Node (`http`, `crypto`, `node:sqlite`) en vez de Express/ws. Funciona igual de bien, pero si más adelante quieres migrarlo a Express es totalmente compatible.

## Nuevo: feed en cuadrícula, código de invitación, y ya no hace falta código de sala

- **La app ahora abre directo en "Descubrir"** — ya no hay que escribir ni compartir ningún código para empezar a usarla. El campo de código de sala pasó a ser una opción avanzada casi escondida (botón "Código de sala" arriba a la derecha del feed), solo por si alguna vez lo necesitas para pruebas.
- **Feed rediseñado como cuadrícula** (2-3 columnas según el ancho de pantalla) en vez del carrusel de una tarjeta a la vez — cada tarjeta es la foto de perfil completa, con el nivel arriba a la izquierda, corazón de seguir arriba a la derecha, nombre/edad/idioma abajo con degradado, y un botón de llamada circular cuyo aro cambia de color según el estado (verde=en línea, rojo=en llamada, gris=desconectada).
- **Pestañas Todas / En línea / Siguiendo** arriba del feed para filtrar rápido.
- **Tocar una tarjeta** abre una vista rápida (mismo modal que ya existía) con biografía, calificaciones, galería, y botones de Chat / Llamar / Seguir — es el mismo modal tanto si lo abres desde el feed como si tocas el nombre de la otra persona dentro de una sala.
- **Código de invitación para atraer clientes**: cada anfitriona recibe un código único al registrarse (visible en "Mi perfil" con botón de copiar). Si un miembro nuevo se registra usando ese código, queda vinculado a ella y la app lo manda directo a chatear con ella en cuanto termina de crear su cuenta — en vez de caer en el feed general. Un código inválido o vacío simplemente se ignora, no rompe el registro.

## Nuevo en la ronda anterior: feed de descubrimiento, presencia en vivo y "seguir"

- **Pantalla "Descubrir personas"** — en vez de solo pedir un código de sala, ahora hay un feed de tarjetas deslizables (`#discover` en `public/index.html`). Los miembros ven anfitrionas y las anfitrionas ven miembros — cada quien ve el lado opuesto de su propio rol.
- **Punto de estado en cada tarjeta**: 🟢 verde = conectada ahora mismo, 🔴 rojo = está en una videollamada, ⚪ gris = desconectada. Se actualiza **en tiempo real** sin recargar la página — probado con dos conexiones simultáneas, cambia de "offline" a "online" al instante.
- **Sala privada fija por pareja**: cuando tocas "Chat" o "Llamar" en la tarjeta de alguien, el servidor calcula un código de sala único y estable entre ustedes dos (`dmRoomCode()` en `server.js`, un hash de los dos IDs de usuario) — siempre es el mismo cuarto, sin tener que compartir códigos a mano. "Llamar" además dispara la videollamada automáticamente en cuanto la otra persona está presente.
- **Filtro por nivel** (Nueva/Plata/Oro/Diamante) arriba del feed, para que los miembros filtren anfitrionas por categoría.
- **Seguir**: botón "+" en cada tarjeta (tabla `follows` en la base de datos). Por ahora solo guarda la relación — no manda notificaciones todavía; eso sería el siguiente paso si lo quieres.

### Cómo funciona la presencia por dentro

El servidor mantiene en memoria quién está conectado (`allConns`, `userConns`) y en qué estado (`userPresence`: `online` | `in-call`). El navegador manda `{type:'presence', status:'in-call'}` en cuanto una videollamada conecta de verdad (cuando llega el video remoto), y `{type:'presence', status:'online'}` al colgar. Cualquier cambio se difunde por WebSocket a todos los conectados (`broadcastPresence()`), así que el feed se actualiza solo mientras lo tienes abierto.

## Nuevo en la ronda anterior: niveles, precios variables, perfiles, calificaciones, traductor y más

- **Niveles de anfitriona** — sube sola según minutos acumulados en llamada: Nueva (10 cr/min) → Plata a los 500 min (12) → Oro a los 2,000 min (15) → Diamante a los 6,000 min (20). Constante `TIERS` en `db.js`.
- **Fotos y video con precio variable** — el texto siempre es gratis. Al enviar una foto/video, si eres anfitriona eliges en el momento: gratis, o el precio que quieras (hasta 1,000 créditos). Paga quien la recibe, gana quien la envía.
- **Mensajes de apertura automáticos** — cada anfitriona escribe hasta 5 frases en su perfil ("Mi perfil" → Frases de apertura). Si entra a una sala con un miembro y no escribe nada en 20 segundos, se envía una al azar a su nombre — pero **solo si ella está conectada de verdad en ese momento**, nunca simulando que está presente cuando no lo está.
- **Descuento por créditos agotados** — si un miembro tiene menos de 10 créditos durante 3+ días seguidos sin recargar, se le genera automáticamente una oferta de +30% en su próxima recarga (válida 48h). Constantes en `db.js` (`LOW_CREDIT_THRESHOLD`, `LOW_CREDIT_DAYS_MS`, `LAPSED_BONUS_PERCENT`).
- **Bono de primera compra** — la primera recarga de cualquier miembro trae +50% créditos gratis, automático.
- **20 regalos** — catálogo ampliado de 5 a 300 créditos, en `GIFTS` dentro de `server.js` (y espejado en `public/index.html` solo para mostrar precios).
- **Perfiles** — anfitrionas: foto de perfil, galería (hasta 6 fotos), edad, biografía. Miembros: perfil editable con un avatar sintético (ilustración abstracta, no persona real) asignado al azar al registrarse — ver nota abajo.
- **Calificación después de la llamada** — el miembro califica 1-5 estrellas en 5 categorías (Carisma, Ojos, Sensualidad, Piernas, Belleza natural), anónimo, la anfitriona solo ve el promedio.
- **Traductor bajo demanda** — botón "Traducir" en cada mensaje. Cada quien tiene su idioma preferido (detectado automáticamente del idioma del teléfono al registrarse, editable en el perfil), y el botón traduce el mensaje a ese idioma. Requiere una clave de [DeepL](https://www.deepl.com/pro-api) — ver abajo.

### Sobre los avatares automáticos de los miembros

Me pediste fotos de "hombres atractivos" tomadas al azar de internet para los miembros nuevos, y no lo construí así: usar la foto de una persona real sin su permiso como avatar de otra cuenta es un problema de derecho de imagen, sin importar la intención. En su lugar, cada miembro nuevo recibe uno de 8 avatares abstractos (`public/avatars/male-01.svg` a `male-08.svg`) — son geométricos, no fotorrealistas. Si quieres rostros sintéticos generados por IA de verdad, la manera correcta es contratar un servicio con licencia para eso (ej. [Generated Photos](https://generated.photos)) y reemplazar esos 8 archivos SVG por esas imágenes (mismo nombre de archivo, cambia solo la extensión y `MEMBER_AVATAR_COUNT` en `server.js` si agregas más de 8).

### Configurar el traductor (opcional)

1. Crea una cuenta gratuita en https://www.deepl.com/pro-api (el plan "Free" da 500,000 caracteres/mes sin costo).
2. Copia tu clave de API.
3. En Render → tu servicio → Environment → agrega `DEEPL_API_KEY` con esa clave.
4. Sin esta variable, el botón "Traducir" sigue apareciendo pero muestra un error explicando que falta configurarlo — el resto de la app funciona igual sin ella.

## Anfitrionas, regalos y reparto de ganancias (70/30)

- Al registrarse, cada persona elige si es **Miembro** (paga créditos) o **Anfitriona** (gana créditos).
- Las anfitrionas empiezan en estado **"pending"** (pendiente) — pueden usar la app normalmente, pero quedan marcadas para que tú las apruebes antes de que en el futuro puedas habilitarles el retiro real a su cuenta bancaria. Esto es estándar en apps de este tipo: evita fraude y cuentas de menores de edad.
- **Reparto de créditos:** cada vez que un miembro paga por un minuto de llamada, una foto/video o un regalo, el 70% queda para la anfitriona (en `earnings_balance`) y el 30% para ti (registrado en la tabla `platform_ledger`). El porcentaje es la constante `COMPANION_SHARE_PERCENT` en `server.js` — cámbialo ahí si quieres otro reparto.

### Panel de administrador (tú)

No hay interfaz visual todavía — son 3 rutas HTTP protegidas por una clave (`ADMIN_KEY`). En Render, ve a Environment y agrega la variable `ADMIN_KEY` con una clave larga y secreta tuya (si no la agregas, usa el valor por defecto inseguro `cambia-esta-clave` — cámbialo antes de tener anfitrionas reales).

```
GET  /api/admin/companions/pending      -- lista quién espera aprobación
POST /api/admin/companions/approve      -- body: {"userId":"..."} -- la aprueba
GET  /api/admin/revenue                 -- total que ha ganado la plataforma (tu 30%)
```

Todas requieren el header `X-Admin-Key: tu-clave`. Puedes probarlas desde el navegador con una extensión tipo "Postman", o dime cuando quieras y te armo una pantalla simple para esto en vez de tener que usarlas a mano.

## Cómo correrlo en tu computadora (Windows)

1. Instala Node.js (versión 22 o más nueva) desde https://nodejs.org — elige la versión LTS.
2. Copia la carpeta `geminy-backend` a tu equipo, por ejemplo dentro de `C:\Users\guill\OneDrive\Desktop\APP`.
3. Abre "Símbolo del sistema" (cmd) o PowerShell, y entra a la carpeta:
   ```
   cd C:\Users\guill\OneDrive\Desktop\APP\geminy-backend
   ```
4. Arranca el servidor:
   ```
   node server.js
   ```
   Deberías ver: `Geminy Meet backend corriendo en http://localhost:8080`
5. Abre `http://localhost:8080` en tu navegador. Crea una cuenta, entra a una sala.
6. Para probarlo con **dos personas de verdad**: abre esa misma dirección desde otro dispositivo conectado a tu misma red WiFi, mais reemplazando `localhost` por la IP de tu computadora (ej. `http://192.168.1.34:8080`). Puedes ver tu IP local con `ipconfig` en PowerShell (busca "Dirección IPv4"). Entra con el mismo código de sala en ambos.

## Qué ya es real vs. qué falta

| Función | Estado |
|---|---|
| Registro / login con contraseña | ✅ Real |
| Créditos y ganancias guardados en servidor | ✅ Real |
| Chat en tiempo real | ✅ Real |
| Envío de fotos/video con precio variable | ✅ Real (hasta 8MB por demo) |
| Videollamada (cámara/mic real) | ✅ Real, WebRTC con tu propia señalización |
| Cobro por minuto según nivel de la anfitriona | ✅ Real |
| Niveles automáticos, regalos (20), calificaciones, ofertas | ✅ Real |
| Perfiles (fotos, bio, edad, aperturas automáticas) | ✅ Real |
| Traductor bajo demanda | ✅ Real (necesita tu propia clave de DeepL) |
| Historial de chat guardado | ✅ Real |
| Compra de créditos | ⚠️ Simulada — no cobra dinero real todavía (falta conectar Apple In-App Purchase) |
| Retiro de créditos a cuenta bancaria | ❌ No implementado (endpoint de ejemplo en `/api/wallet/withdraw` que explica lo que falta) |
| Verificación de edad / identidad real | ❌ No implementado (solo el campo de edad autodeclarado en el perfil) |
| Bloquear / reportar usuarios | ❌ No implementado todavía |
| Servidor accesible desde fuera de tu WiFi (para que funcione como app real) | ❌ Falta desplegarlo en un hosting (Render, Railway, un VPS, etc.) |

## Siguiente paso: empaquetarlo para iPhone

Como me dijiste que solo tienes iPhone (no Android), hay dos caminos posibles y quiero que elijas con información clara:

**Opción A — App web instalable (PWA):** conviertes esta misma página en algo que se instala en la pantalla de inicio del iPhone desde Safari, con ícono propio y pantalla completa (sin barra de navegador). Es rápida de lograr, no necesita cuenta de desarrollador ni Mac, pero técnicamente sigue sin pasar por la App Store — no se puede "descargar" desde ahí.

**Opción B — App nativa real en la App Store:** para eso Apple exige compilarla con Xcode, lo cual **solo corre en una Mac** — necesitarías tener o pedir prestada una Mac, además de una cuenta de Apple Developer ($99 USD al año) para poder probarla en tu iPhone (vía TestFlight) o publicarla.

Además, para que la app funcione fuera de tu casa (no solo en tu WiFi), este backend tiene que vivir en un servidor real en internet, no en tu computadora apagada.

Dime qué tienes disponible y seguimos por ahí.
