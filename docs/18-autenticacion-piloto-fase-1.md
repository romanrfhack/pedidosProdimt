# 18 — Autenticacion piloto Fase 1

Fecha: 2026-05-28

## Objetivo

Agregar una capa minima de autenticacion para que:

- Clientes piloto entren con un token/enlace seguro y solo operen su propio pedido.
- Administracion use login basico y acceda a endpoints internos.
- Auditoria administrativa quede protegida.

No es el sistema definitivo de usuarios, roles o seguridad de produccion.

## Cliente piloto

El cliente no usa contrasena en Fase 1.

Flujo:

1. El cliente recibe o pega un token de acceso.
2. `POST /api/auth/customer-token` valida el token hasheado en `CustomerAccessTokens`.
3. La API emite JWT de cliente.
4. El frontend usa `Authorization: Bearer <token>`.
5. Los endpoints de cliente validan que el `customerId` de la ruta coincida con el claim del JWT.

Endpoints protegidos con `CustomerAccess`:

- `GET /api/customer-orders/{customerId}/today`
- `POST /api/customer-orders/{customerId}/submit`
- `POST /api/customer-orders/{customerId}/no-order`

Un cliente no puede consultar auditoria, maquina ni pedidos de otros clientes.

## Administracion

Administracion usa login basico:

```http
POST /api/auth/admin/login
```

Request:

```json
{
  "userName": "admin",
  "password": "prodimt-admin-demo"
}
```

La contrasena demo es solo Development. Se guarda hasheada con `PasswordHasher` de ASP.NET Core.

Endpoints protegidos con `AdminAccess`:

- `GET /api/admin/orders/today`
- `GET /api/admin/orders/pending-review`
- `GET /api/admin/orders/{orderId}`
- `POST /api/admin/orders/{orderId}/review`
- `GET /api/admin/orders/{orderId}/audit`
- `GET /api/admin/customers/pending-orders`
- `GET /api/admin/customers/{customerId}/order-template`
- `POST /api/admin/customers/{customerId}/orders/submit`
- `POST /api/admin/customers/{customerId}/orders/no-order`
- `GET/POST/PATCH /api/admin/customers/{customerId}/access-tokens...`
- `GET/POST/PATCH /api/admin/users...`

## Endpoints de auth

Cliente:

```http
POST /api/auth/customer-token
```

Request:

```json
{
  "token": "demo-customer-token"
}
```

Response:

```json
{
  "accessToken": "...",
  "tokenType": "Bearer",
  "expiresAt": "2026-05-28T21:00:00-06:00",
  "customerId": "11111111-1111-1111-1111-111111111111",
  "customerName": "Gran Takito"
}
```

Admin:

```http
POST /api/auth/admin/login
```

Response:

```json
{
  "accessToken": "...",
  "tokenType": "Bearer",
  "expiresAt": "2026-05-28T21:00:00-06:00",
  "displayName": "Administrador Demo"
}
```

Credenciales invalidas, usuario inactivo o token invalido/inactivo/expirado devuelven `401`.

## Claims JWT

Claims propios:

- `prodimt_actor_type`: `Customer` o `Admin`.
- `prodimt_customer_id`: id del cliente para JWT de cliente.
- `prodimt_customer_name`: nombre del cliente para JWT de cliente.
- `prodimt_user_id`: id del admin para JWT admin.
- `prodimt_user_name`: usuario admin.
- `prodimt_display_name`: nombre visible admin.

Tambien se emiten `sub`, `nameidentifier` y `name` para interoperabilidad basica.

## Configuracion JWT

Configuracion local:

```json
{
  "Authentication": {
    "Jwt": {
      "Issuer": "Prodimt.Pedidos",
      "Audience": "Prodimt.Pedidos",
      "SigningKey": "development-only-prodimt-pedidos-jwt-signing-key-change-before-production-2026",
      "AccessTokenMinutes": 720
    }
  }
}
```

Variables soportadas:

```bash
Authentication__Jwt__SigningKey='local-development-key-change-me'
PRODIMT_JWT_SIGNING_KEY='local-development-key-change-me'
```

La clave versionada es demo solo Development. No usar en produccion.

## Seed demo

Solo en `Development`, cuando `DevelopmentSeed:Enabled=true`:

- Admin demo:
  - userName: `admin`
  - password: `prodimt-admin-demo`
  - displayName: `Administrador Demo`
- Cliente demo:
  - customer: `Gran Takito`
  - token: `demo-customer-token`

El token de cliente se guarda como hash SHA-256 Base64 en `CustomerAccessTokens`, no como texto plano.
Administracion puede crear y revocar tokens desde endpoints protegidos. El token plano solo se devuelve al crearlo; listados posteriores muestran metadatos sin el valor ni el hash.
Un cliente inactivo no puede autenticarse aunque tenga token activo.

Valores configurables:

```bash
PRODIMT_DEMO_ADMIN_USERNAME='admin'
PRODIMT_DEMO_ADMIN_PASSWORD='prodimt-admin-demo'
PRODIMT_DEMO_CUSTOMER_TOKEN='demo-customer-token'
```

## Frontend

Angular agrega:

- `AuthService`.
- Interceptor HTTP que agrega `Authorization: Bearer`.
- Login cliente por query string `?token=...` o token pegado.
- Login admin en `/admin/login`.
- Guard para `/admin/pedidos`, `/admin/pendientes` y `/admin/clientes-pendientes`.

Para esta fase el JWT se guarda en `localStorage`. Es una decision temporal de desarrollo piloto.

## Health

`GET /health` y `GET /health/db` quedan publicos en Fase 1.

Decision: `/health/db` puede quedar publico porque solo devuelve disponibilidad (`reachable`) o error generico `503`; no expone connection strings, nombres de usuario ni datos de negocio.

## Como probar localmente

Backend:

```bash
dotnet restore src/Prodimt.Pedidos.sln
dotnet build src/Prodimt.Pedidos.sln --no-restore
dotnet test src/Prodimt.Pedidos.sln --no-restore
```

SQL Server local:

```bash
bash scripts/dev/start-sqlserver.sh
bash scripts/dev/update-database.sh
bash scripts/dev/reset-database.sh --confirm
bash scripts/dev/run-api-sqlserver.sh
bash scripts/dev/smoke-fase1.sh
```

Frontend:

```bash
cd apps/prodimt-pedidos-web
npm run build
```

E2E:

```bash
cd tests/e2e
npm test
```

## Limitaciones

- Hay alta y activacion/desactivacion basica de usuarios admin por API protegida; no hay pantalla dedicada ni modulo completo.
- No hay recuperacion de contrasena.
- No hay 2FA.
- No hay roles finos.
- No hay refresh tokens.
- No hay rotacion automatica de tokens cliente.
- No hay endpoint de rotacion de token; se crea uno nuevo y se revoca el anterior.
- No hay envio real de enlaces por WhatsApp.
- La captura administrativa en nombre de cliente ya existe como flujo piloto protegido por `AdminAccess`.
- La identidad admin se copia a auditoria en los flujos administrativos nuevos.

## Endurecer antes de produccion

- Mover secretos a variables o secret manager.
- Exigir clave JWT fuerte por entorno.
- Definir expiracion y rotacion operativa de tokens cliente.
- Revisar almacenamiento frontend del token.
- Agregar roles/permisos finos para administracion.
- Propagar identidad autenticada a auditoria.
- Completar administracion interna de usuarios con cambio de contrasena controlado, roles finos y auditoria visible.
- Agregar pruebas de expiracion, inactividad y revocacion.
