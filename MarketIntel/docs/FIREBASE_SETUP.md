# Configurar push remoto real (Firebase Cloud Messaging)

Con esto, el servidor puede enviar notificaciones al teléfono **aunque la app esté cerrada**.
Todo el código ya está escrito; solo necesitas tu proyecto de Firebase y pegar dos archivos.

Necesitas: una cuenta de Google (gratis). El plan **Spark (gratuito)** de Firebase basta para FCM.

---

## 1. Crear el proyecto de Firebase

1. Entra en <https://console.firebase.google.com> e inicia sesión.
2. **Add project** → nombre (p. ej. `MarketIntel`) → puedes desactivar Google Analytics → **Create project**.

## 2. Registrar la app Android (cliente que RECIBE los push)

1. En el proyecto, pulsa el icono **Android** (“Add app”).
2. **Android package name**: `com.companyname.marketintel.maui`
   *(es el `ApplicationId` del `.csproj`; si lo cambias, usa el nuevo).*
3. Registra la app y **descarga `google-services.json`**.
4. Copia ese archivo aquí:
   ```
   src/MarketIntel.Maui/Platforms/Android/google-services.json
   ```

## 3. Compilar la app con Firebase activado

Con el `google-services.json` en su sitio:

```bash
dotnet build src/MarketIntel.Maui -f net10.0-android -t:Run -p:UseFirebase=true -p:AdbTarget="-s emulator-5554"
```

`-p:UseFirebase=true` activa el paquete `Xamarin.Firebase.Messaging` y compila
`MarketIntelFirebaseMessagingService`, que:
- registra el token del dispositivo en el backend (`POST /api/devices`),
- muestra la notificación cuando llega un push en primer plano.

*(Si el build se queja de la versión del paquete, usa la última:
`dotnet add src/MarketIntel.Maui package Xamarin.Firebase.Messaging`.)*

## 4. Configurar el servidor que ENVÍA los push

1. En Firebase console: **⚙ Project settings → Service accounts → Generate new private key**.
   Se descarga un JSON (la credencial del servidor). Guárdalo **fuera del repo**, p. ej.
   `C:\secrets\marketintel-fcm.json`.
2. Toma el **Project ID** (aparece en Project settings, o dentro de ese JSON como `project_id`).
3. Dale esos dos valores al API con user-secrets (no se guardan en el repo):
   ```bash
   dotnet user-secrets --project src/MarketIntel.Api set "Fcm:ProjectId" "TU_PROJECT_ID"
   dotnet user-secrets --project src/MarketIntel.Api set "Fcm:ServiceAccountPath" "C:\secrets\marketintel-fcm.json"
   ```
   *(En producción/Docker se monta como secreto y se pasan por variables de entorno
   `Fcm__ProjectId` y `Fcm__ServiceAccountPath`.)*
4. Reinicia el API. Al arrancar detecta la config y usa `FcmNotificationService` (FCM HTTP v1)
   en lugar del logger.

## 5. Probar

1. Abre la app (con `UseFirebase=true`). Concede el permiso de notificaciones.
   El token se registra solo en el backend.
2. Comprueba que hay dispositivos y envía un push de prueba:
   ```bash
   curl -X POST http://localhost:5080/api/notify/test
   ```
   Debería llegar una notificación **real** al teléfono/emulador, aunque la app esté en segundo plano.

---

## iOS (requiere Mac)

Para iPhone, FCM entrega a través de **APNs**, que exige:
- Una **APNs Authentication Key** (.p8) desde el Apple Developer Portal, subida a Firebase.
- El target iOS con capability *Push Notifications* y entitlement `aps-environment`.
- Compilar/firmar en **macOS** (build host).

El emisor del servidor (`FcmNotificationService`) ya sirve para iOS sin cambios: FCM enruta a APNs.
Solo falta la parte de firma/credenciales de Apple, que necesita el Mac.

## Notas

- El plan gratuito de Firebase cubre FCM sin coste.
- El token del dispositivo se guarda hoy en memoria (`DeviceRegistry`); en producción va a la base
  de datos junto al usuario, para enviar push dirigidos (p. ej. cuando salta su regla de alerta).
- La notificación **en el dispositivo** (cuando la app está abierta y salta una alerta) ya funciona
  sin Firebase — esto añade la entrega **remota en background**.
