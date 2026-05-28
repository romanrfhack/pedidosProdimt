# PRODIMT Pedidos — Base documental inicial

Fecha: 2026-05-27  
Versión documental: 0.2

Este paquete contiene la documentación inicial para construir la aplicación de pedidos de PRODIMT con enfoque **mobile first**.

## Objetivo del proyecto

Sustituir gradualmente el flujo manual actual de pedidos por WhatsApp, llamadas, mensajes en grupo y captura en Excel por un sistema donde cada cliente pueda confirmar, editar o marcar que no pedirá desde el celular, y donde PRODIMT pueda consultar los pedidos sin depender de transcribir cientos de mensajes.

## Definiciones operativas confirmadas

- `x/X` en el Excel significa **no pidió**.
- La hora límite inicial es **10:00 a.m.**.
- Un pedido después de la hora límite se marca como **tardío** y queda sujeto a decisión administrativa.
- Si un cliente intenta hacer más de un pedido en el mismo día, el nuevo pedido o cambio debe quedar sujeto a decisión administrativa.
- `Mostrador` no es cliente externo; es un **canal de venta interno**.
- La columna E de las hojas diarias representa el **número de máquina** que atenderá el pedido.
- El cliente no debe ver la máquina asignada.
- El catálogo de cliente debe permitir registrar una **hora o ventana deseada de entrega** como dato opcional.
- La Fase 1 se concentra en capturar pedidos de clientes; las vistas de producción, embarques y repartidores quedan para fases posteriores.

## Documentos incluidos

- `docs/00-contexto-y-objetivo.md`: contexto operativo y objetivo del sistema.
- `docs/01-analisis-excel-actual.md`: hallazgos principales del archivo Excel actual.
- `docs/02-alcance-y-etapas.md`: alcance inicial, fuera de alcance y fases.
- `docs/03-requerimientos-funcionales.md`: requerimientos funcionales base.
- `docs/04-requerimientos-plus.md`: funcionalidades de valor agregado.
- `docs/05-arquitectura-propuesta.md`: arquitectura general propuesta.
- `docs/06-modelo-datos-inicial.md`: modelo conceptual inicial.
- `docs/07-backlog-mvp.md`: backlog inicial para la primera versión útil.
- `docs/08-contexto-para-codex.md`: instrucciones de continuidad para Codex.
- `docs/09-preguntas-abiertas.md`: dudas abiertas y decisiones ya resueltas.
- `docs/10-decisiones-operativas-confirmadas.md`: decisiones de negocio confirmadas por PRODIMT.
- `docs/11-reglas-de-negocio-fase-1.md`: reglas de negocio base para captura.
- `docs/12-flujos-fase-1.md`: flujos funcionales de la primera fase.
- `docs/adrs/`: decisiones arquitectónicas.
- `docs/reference/`: archivos de apoyo extraídos o derivados del Excel.

## Estado actual

- Ya existe estructura técnica inicial de backend, frontend y pruebas.
- Esta documentación define el alcance base para iniciar el repositorio.
- El Excel actual se usó como referencia para entender captura, moldes, clientes, máquinas y vistas derivadas.
- La primera meta de desarrollo debe ser capturar pedidos correctamente, no reemplazar todos los reportes de producción desde el día uno.

## Comandos reales

### Backend

```bash
dotnet restore src/Prodimt.Pedidos.sln
dotnet build src/Prodimt.Pedidos.sln
dotnet run --project src/Prodimt.Pedidos.Api/Prodimt.Pedidos.Api.csproj --urls http://127.0.0.1:5088
```

La API usa EF Core + SQL Server por defecto. La cadena local de ejemplo esta en `src/Prodimt.Pedidos.Api/appsettings.Development.json`:

```text
Server=localhost,1433;Database=ProdimtPedidos;User Id=sa;Password=CHANGE_ME_LOCAL_ONLY;TrustServerCertificate=True
```

`CHANGE_ME_LOCAL_ONLY` debe reemplazarse solo en configuracion local o variables de entorno. No agregar credenciales reales al repositorio.

Endpoints iniciales:

- `GET http://127.0.0.1:5088/health`
- `GET http://127.0.0.1:5088/api/customer-orders/11111111-1111-1111-1111-111111111111/today`
- `POST http://127.0.0.1:5088/api/customer-orders/11111111-1111-1111-1111-111111111111/submit`
- `POST http://127.0.0.1:5088/api/customer-orders/11111111-1111-1111-1111-111111111111/no-order`
- `GET http://127.0.0.1:5088/api/admin/orders/today`
- `GET http://127.0.0.1:5088/api/admin/orders/pending-review`
- `POST http://127.0.0.1:5088/api/admin/orders/{orderId}/review`

Si no hay SQL Server local disponible y solo se quiere levantar la API demo sin persistencia real:

```bash
Persistence__Provider=InMemory dotnet run --project src/Prodimt.Pedidos.Api/Prodimt.Pedidos.Api.csproj --urls http://127.0.0.1:5088
```

### EF Core

La migracion inicial ya existe en `src/Prodimt.Pedidos.Infrastructure/Persistence/Migrations`.

Aplicar migraciones a SQL Server local:

```bash
dotnet ef database update --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api
```

Crear una nueva migracion:

```bash
dotnet ef migrations add InitialCreate --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api
```

Los datos semilla de desarrollo se aplican al iniciar la API en `Development` cuando `DevelopmentSeed:Enabled` es `true`. Incluyen clientes demo, productos, maquinas, canales, productos frecuentes y asignaciones internas de maquina.

### Frontend

```bash
cd apps/prodimt-pedidos-web
npm install
npm run start -- --host 127.0.0.1 --port 4200
```

El frontend lee la API desde `apps/prodimt-pedidos-web/src/environments/environment.ts`:

```ts
export const environment = {
  apiBaseUrl: 'http://127.0.0.1:5088',
  demoCustomerId: '11111111-1111-1111-1111-111111111111'
};
```

Para desarrollo local normal, levantar primero la API en `http://127.0.0.1:5088` y despues Angular en `http://127.0.0.1:4200`.
Los envios reales (`submit`, `no-order`, revision admin) no simulan exito si la API falla.

### Pruebas

```bash
dotnet test src/Prodimt.Pedidos.sln
cd apps/prodimt-pedidos-web
npm run build
cd ../../tests/e2e
npm install
npm test
```

Playwright usa un mock API local controlado en `http://127.0.0.1:5088` y levanta Angular en `http://127.0.0.1:4210` para no depender de SQL Server durante E2E basico. Si ya hay una API real ocupando `5088`, detenerla antes de correr `cd tests/e2e && npm test`.

## Estado de implementación

Ver `docs/13-estado-implementacion-inicial.md`.
Ver tambien `docs/14-persistencia-ef-core-sql-server.md`.
Ver tambien `docs/15-integracion-frontend-api-fase-1.md`.

## Regla de continuidad para Codex

Antes de crear o modificar código, Codex debe leer:

1. `docs/08-contexto-para-codex.md`
2. `docs/10-decisiones-operativas-confirmadas.md`
3. `docs/11-reglas-de-negocio-fase-1.md`
4. `docs/12-flujos-fase-1.md`
5. `docs/02-alcance-y-etapas.md`
6. `docs/03-requerimientos-funcionales.md`
7. `docs/06-modelo-datos-inicial.md`
8. `docs/07-backlog-mvp.md`
