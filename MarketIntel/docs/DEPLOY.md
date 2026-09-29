# Desplegar el backend en un servidor

El backend (API) es **stateless** y va en Docker. La app móvil solo apunta a su URL.
El dueño del servidor (tu primo) pone aquí las claves de **Alpaca** y **Firebase**; la app no las lleva.

## 0. Requisitos del servidor

- Un servidor con **Docker** y **Docker Compose** (cualquier VM de cloud: DigitalOcean, Hetzner,
  AWS EC2, Azure, etc.), o Docker Desktop.
- Un **dominio** apuntando al servidor y **HTTPS** (recomendado para producción).
- Puerto 5080 abierto (o el que uses detrás del proxy).

## 1. Traer el código

```bash
git clone https://github.com/TU_USUARIO/MarketIntel.git
cd MarketIntel
```

## 2. Configurar los secretos (NO se suben al repo)

```bash
cp .env.example .env
```
Edita `.env` y rellena:
- `Alpaca__ApiKeyId` y `Alpaca__ApiSecretKey` — de la cuenta Alpaca del servidor (https://alpaca.markets).
- `Fcm__ProjectId` — el ID del proyecto Firebase (p. ej. `marketintel-a1746`).

Pon la **service-account de Firebase** (el JSON privado) en:
```
secrets/fcm.json
```
*(el `docker-compose.yml` la monta en el contenedor y ya apunta a ella).*

> Sin claves de Alpaca, el API arranca igual pero con **datos sintéticos**. En cuanto las pongas,
> pasa a **datos reales** automáticamente (lo verás en `/api/meta` → `synthetic: false`).

## 3. Levantar

```bash
docker compose up --build -d
```
Comprobar:
```bash
curl http://localhost:5080/api/health      # {"status":"ok",...}
curl http://localhost:5080/api/meta        # provider / synthetic
```

## 4. HTTPS (producción)

Pon un proxy inverso delante del API. Ejemplo con **Caddy** (HTTPS automático) — `Caddyfile`:
```
api.tudominio.com {
    reverse_proxy localhost:5080
}
```
Y en la app, quita el modo de desarrollo de texto plano: en
`src/MarketIntel.Maui/Platforms/Android/AndroidManifest.xml` elimina
`android:usesCleartextTraffic="true"` (con HTTPS ya no hace falta).

## 5. Apuntar la app al servidor

En `src/MarketIntel.Maui/Services/ApiConfig.cs`:
```csharp
private const string ProductionUrl = "https://api.tudominio.com";
```
Recompila la app (Android/iOS) y listo: ya habla con el backend del servidor.

## 6. Escalado (cuando haga falta)

- El API no guarda estado → puedes correr **varias réplicas** detrás del proxy.
- **Postgres/TimescaleDB** (ya en el compose) para históricos de precios/predicciones y para
  guardar watchlists, alertas y tokens de push por usuario.
- **Redis** (ya en el compose) para cachear señales calientes y compartir el estado de rate-limit
  entre réplicas.
- Estos dos servicios ya están declarados; falta cablear la capa de persistencia (EF Core) cuando
  se quiera pasar de en-memoria a base de datos.

## 7. Checklist de seguridad

- `.env` y `secrets/` **nunca** al repo (ya están en `.gitignore`).
- Cambia las contraseñas por defecto de Postgres (`changeme`).
- Mantén el repo **privado**.
- En producción, monta la service-account como secreto del orquestador (Docker/K8s), no como archivo suelto.
