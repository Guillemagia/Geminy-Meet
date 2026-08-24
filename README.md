# Geminy Meet — Backend real

Este backend reemplaza la versión "todo en el navegador" del prototipo anterior. Ahora:

- Los usuarios se registran e inician sesión de verdad (contraseñas con hash `scrypt`, nunca en texto plano).
- Los créditos y las ganancias viven en una base de datos en el servidor (`geminy.db`), no en `localStorage`.
- El chat, las fotos/videos y el cobro por minuto de llamada pasan por el servidor, que es quien decide si hay saldo o no — el navegador ya no puede "inventarse" créditos.
- La videollamada usa WebRTC real (cámara/micrófono de verdad); el servidor solo transporta la señalización, el video nunca pasa por él.
- Nota técnica: el servidor está escrito casi entero con módulos nativos de Node (`http`, `crypto`, `node:sqlite`) en vez de Express/ws — funciona igual de bien y si algún día quieres migrarlo a Express es totalmente compatible. La única dependencia de npm es `@anthropic-ai/sdk`, que es lo que mueve a las anfitrionas de IA. Antes de arrancar el servidor por primera vez, corre `npm install`.

## Nuevo: anfitrionas de inteligencia artificial (Claude)

Ahora la app puede tener **anfitrionas movidas por IA** que chatean solas, sin que haya nadie del otro lado. Están construidas sobre [Claude](https://www.anthropic.com) (modelo `claude-opus-5`) y viven en la misma tabla de usuarios que las anfitrionas reales, así que aparecen en el feed, tienen perfil, historial de chat y se puede "seguirlas" como a cualquier otra.

### Lo que decidí y por qué

Te lo pongo por delante porque son decisiones que afectan al negocio, no solo al código:

- **Siempre dicen que son una IA.** La tarjeta del feed lleva una insignia naranja **IA**, la cabecera del chat también, cada mensaje suyo va marcado, y al abrir la sala sale un aviso: *"Estás chateando con Aria, un personaje de inteligencia artificial. No es una persona real."* Además el propio modelo tiene la instrucción de decirlo si se lo preguntan. Cobrarle a alguien por hablar con un bot haciéndole creer que es una mujer real es fraude, y es motivo de expulsión inmediata de la App Store y de Google Play — así que no lo construí de esa forma. Con la etiqueta puesta es un producto perfectamente legítimo; muchas apps del sector lo hacen así.
- **Solo chatean, no hacen videollamadas.** No hay cámara detrás. Su tarjeta muestra un botón de 💬 en vez del de 📹, el botón de llamar desaparece dentro de su sala, y el servidor rechaza cobrar minutos de llamada en una sala de IA (antes se le hubieran cobrado 10 créditos por minuto a alguien hablando con nadie).
- **No acumulan "ganancias".** Si un miembro le manda un regalo a una IA, el 100% queda en la plataforma (tuyo), en vez de fingir que "ella" se lleva el 70%. El texto sigue siendo gratis, igual que con las anfitrionas reales.
- **Nunca piden dinero.** El modelo tiene prohibido pedir créditos, regalos, recargas, teléfono, redes sociales o datos bancarios. Tampoco promete quedar en persona, ni produce contenido sexual explícito, y si detecta que quien escribe podría ser menor de edad corta el tono coqueto de inmediato.

### Cómo activarlas

1. Crea una cuenta en https://console.anthropic.com y saca una clave de API.
2. En Render → tu servicio → Environment → agrega `ANTHROPIC_API_KEY` con esa clave.
3. Reinicia el servicio. En el arranque verás en los logs: `[ai] 3 anfitriona(s) de inteligencia artificial activas`.

**Sin esa variable no pasa nada malo**: las anfitrionas de IA simplemente no se crean ni aparecen en el feed, y el resto de la app funciona exactamente igual que antes.

Vienen tres por defecto (Aria, Sol y Nube), cada una con su personalidad, su avatar y sus frases de saludo. Se crean solas la primera vez que arranca el servidor con la clave puesta.

### Cambiarlas o agregar más (sin tocar código)

Dos rutas nuevas de administrador, con la misma clave `X-Admin-Key` que las demás:

```
GET  /api/admin/ai     -- lista las anfitrionas de IA con su personalidad
POST /api/admin/ai     -- crea una nueva, o actualiza una existente si mandas {"userId":"..."}
```

El cuerpo del POST acepta `name`, `age`, `bio`, `persona`, `avatarUrl`, `openers` (hasta 5 frases de saludo) y `preferredLanguage`. El campo `persona` es la descripción de personalidad que se le manda al modelo — ahí es donde defines si es tímida, bromista, directa, de qué le gusta hablar, etc.

### Costos y frenos de gasto

Cada respuesta es una llamada a la API de Anthropic y se paga por uso (unos centavos por cada varias decenas de mensajes, según el largo de la conversación). Para que no se dispare:

- Solo se manda el contexto de los últimos 24 mensajes de la sala, recortados.
- Las respuestas están topadas a 400 tokens (son mensajes de chat, no ensayos).
- Un mismo miembro puede pedir como máximo 25 respuestas cada 5 minutos.
- Una respuesta a la vez por sala: si manda tres mensajes seguidos, se contestan juntos en vez de disparar tres llamadas.

Puedes cambiar el modelo con la variable `AI_MODEL` si algún día quieres uno más barato.

### Lo que todavía no hacen

- No ven las fotos ni los videos que les mandes (te lo dicen ellas mismas). Se puede agregar — el modelo sí sabe ver imágenes — pero no lo activé para no subir el costo sin que lo decidas tú.
- No inician conversaciones por su cuenta: saludan cuando entras a su sala por primera vez, y de ahí en adelante contestan.
- No recuerdan nada fuera del historial de esa sala.

## Arreglos que hice de paso en esta ronda

Mientras conectaba la IA me topé con cosas que ya estaban rotas en el repositorio. Las arreglé porque sin ellas no se podía ni probar la app:

- **La app no pasaba de la pantalla de inicio de sesión.** `public/index.html` buscaba un elemento `#earningsLine` que el rediseño del feed había borrado, y el error tumbaba todo el arranque de sesión justo después de crear la cuenta. Devolví esa línea (ahora en la cabecera de "Descubrir", donde una anfitriona sí la ve) y blindé el código para que no vuelva a pasar.
- **El código de invitación reventaba el registro.** `server.js` llamaba a `db.getUserByReferralCode(...)` y leía `user.referral_code`, pero ni la función ni la columna existían en `db.js`. Registrarse con un código daba error 500. Agregué la columna, el índice único y la generación del código (6 caracteres, sin letras que se confundan como 0/O o 1/I).
- **Los avatares de los miembros daban 404.** Los ocho `male-0X.svg` estaban en la raíz del proyecto, pero el servidor los sirve desde `public/avatars/`. Los moví ahí.
- **El historial de chat se quedaba congelado.** `getRoomHistory` pedía los *primeros* 80 mensajes de la sala en vez de los últimos, así que en una conversación larga nunca se veía lo reciente. Ahora trae los últimos 80 en orden.
- **Un hash de contraseña con largo inesperado daba error 500** en vez de "contraseña incorrecta" (`crypto.timingSafeEqual` revienta si los buffers miden distinto).
- **`node_modules/` ya no se sube al repositorio.** Estaban commiteadas 985 archivos de Express y Socket.IO que el servidor nunca usa (no aparecían en `package.json`). Agregué un `.gitignore` — las dependencias se instalan con `npm install`, que es lo que ya hace Render al desplegar.

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
4. Instala las dependencias (solo la primera vez):
   ```
   npm install
   ```
5. Arranca el servidor:
   ```
   node server.js
   ```
   Deberías ver: `Geminy Meet backend corriendo en http://localhost:8080`
6. Abre `http://localhost:8080` en tu navegador. Crea una cuenta, entra a una sala.
7. Para probarlo con **dos personas de verdad**: abre esa misma dirección desde otro dispositivo conectado a tu misma red WiFi, mais reemplazando `localhost` por la IP de tu computadora (ej. `http://192.168.1.34:8080`). Puedes ver tu IP local con `ipconfig` en PowerShell (busca "Dirección IPv4"). Entra con el mismo código de sala en ambos.

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
| Anfitrionas de inteligencia artificial (chat) | ✅ Real (necesita tu propia clave de Anthropic) |
| Videollamada con una anfitriona de IA | ❌ No existe — no hay cámara detrás, la app no lo ofrece |
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
