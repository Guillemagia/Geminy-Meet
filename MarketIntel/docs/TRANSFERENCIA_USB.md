# Transferencia por USB y puesta en marcha en otra PC

Esta carpeta (`D:\MarketIntel`) quedó **limpia y portable** (~1 MB): solo código fuente,
sin `bin/`, `obj/` ni `.vs/` (se regeneran solos). Se puede copiar entera al USB.

---

## 1. Copiar al USB

Copiá **toda la carpeta `MarketIntel`** al pendrive (arrastrar y soltar).
Incluye `.git` (historial) y `src\MarketIntel.Maui\Platforms\Android\google-services.json`
(está *gitignored* pero es un archivo normal, sí se copia al USB — es necesario para el push).

> No hay ningún secreto embebido en la carpeta. Las claves (Alpaca / service-account de
> Firebase / user-secrets) **NO** están acá y se reconfiguran en la PC de destino (ver §4).

---

## 2. Toolchain a instalar en la PC de destino

La app **compila**, pero cada máquina necesita su propia cadena de herramientas:

| Herramienta | Versión / detalle | Para qué |
|---|---|---|
| **.NET SDK** | **10.0.400** (`winget install Microsoft.DotNet.SDK.Preview` o el instalador oficial) | compilar API + app |
| **MAUI workload** | `dotnet workload install maui-android` | app Android |
| **JDK 17** | Microsoft OpenJDK 17 (`winget install Microsoft.OpenJDK.17`) | build Android |
| **Android SDK** | platform-tools, android-36, build-tools 36, emulator, system-image android-35 google_apis x86_64 | emulador + deploy |
| (opcional) **Docker Desktop** | — | correr el API en contenedor (evita el bloqueo SAC) |

Notas:
- El SDK .NET puede **no** quedar en el PATH; usar ruta completa
  `C:\Program Files\dotnet\dotnet.exe` o refrescar PATH.
- Crear un AVD para el emulador (en la máquina anterior se llamaba `mi_pixel`).

---

## 3. Compilar y correr

**API** (puerto 5080):
```
dotnet run --project src/MarketIntel.Api --urls http://localhost:5080
```

**App Android** (emulador encendido primero):
```
dotnet build src/MarketIntel.Maui -f net10.0-android -t:Run
```
Con push remoto Firebase:
```
dotnet build src/MarketIntel.Maui -f net10.0-android -t:Run -p:UseFirebase=true
```

- Android apunta a `http://10.0.2.2:5080`; el resto a `http://localhost:5080`
  (`src/MarketIntel.Maui/Services/ApiConfig.cs`).
- Para apuntar a un servidor real, poné la URL pública en `ProductionUrl` en ese mismo archivo.

---

## 4. Secretos a reconfigurar (NO viajan en el USB)

**API — user-secrets** (UserSecretsId `marketintel-api-secrets`, solo se cargan en Development):
```
dotnet user-secrets --project src/MarketIntel.Api set "Alpaca:ApiKeyId"        "<clave>"
dotnet user-secrets --project src/MarketIntel.Api set "Alpaca:ApiSecretKey"    "<secreto>"
dotnet user-secrets --project src/MarketIntel.Api set "Fcm:ProjectId"          "marketintel-a1746"
dotnet user-secrets --project src/MarketIntel.Api set "Fcm:ServiceAccountPath" "C:\ruta\service-account.json"
```
- **Alpaca**: crear cuenta gratis en https://alpaca.markets. Sin claves, el backend cae al mock.
- **Firebase service-account**: es el JSON `*firebase-adminsdk*.json` del proyecto
  `marketintel-a1746`. Es un secreto: **copialo aparte** (no por el USB junto al código si el
  pendrive es compartido) y ponelo fuera del repo. Sin él, el push remoto queda deshabilitado
  (el resto de la app funciona igual).

**Con Docker** (alternativa): copiar `.env.example` → `.env`, rellenar valores, y poner la
service-account en `./secrets/fcm.json`. Luego `docker compose up`.

Ver también `docs/FIREBASE_SETUP.md`.

---

## 5. Aviso: Smart App Control (SAC)

En la máquina anterior, **Windows Smart App Control bloqueó la ejecución de los DLL
recién compilados** del API (`0x800711C7`). Si la PC de destino también tiene SAC en modo
*Enforced*, el `dotnet run` del API fallará igual. Salidas:
- Desactivar SAC en *Seguridad de Windows → Control de aplicaciones y explorador*
  (decisión del usuario; es casi irreversible), **o**
- Correr el API en **Docker** (Linux, SAC no aplica), **o**
- Desplegar el API en el servidor.

El emulador/app Android **no** se ve afectado por SAC.
