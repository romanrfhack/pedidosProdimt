# 13 — Estado de implementacion inicial

Fecha: 2026-05-28

## Hecho

- Se creo `AGENTS.md` con reglas operativas y tecnicas para futuras sesiones.
- Se agrego solucion backend `src/Prodimt.Pedidos.sln` con proyectos:
  - `Prodimt.Pedidos.Domain`
  - `Prodimt.Pedidos.Application`
  - `Prodimt.Pedidos.Infrastructure`
  - `Prodimt.Pedidos.Api`
- Se agregaron entidades base de dominio para clientes, productos, maquinas, canales de venta, pedidos y lineas.
- Se agregaron enums iniciales para estado de pedido, razon de revision, decision administrativa y tipo de canal.
- Se implemento regla de horario limite 10:00 a.m., pedido tardio, segundo pedido del dia y `NoOrder`.
- Se preparo EF Core con `PedidosDbContext` y SQL Server en `Infrastructure`.
- Se agregaron repositorios en memoria como fallback configurable de desarrollo cuando no hay base de datos local.
- Se agregaron endpoints minimos de cliente y administracion.
- Se creo app Angular 21 en `apps/prodimt-pedidos-web`.
- Se agregaron pantallas iniciales:
  - Cliente: "Mi pedido de hoy".
  - Admin: "Pedidos de hoy".
  - Admin: "Pendientes de revision".
- Se agregaron pruebas unitarias xUnit para reglas criticas.
- Se agrego Playwright E2E para validar acciones principales y que la vista de cliente no muestre informacion de maquina.
- Se agregaron `.gitignore` y `.editorconfig`.
- Se reemplazo la configuracion default de repositorios en memoria por repositorios EF Core.
- Se agregaron configuraciones Fluent API separadas para las entidades principales.
- Se creo migracion inicial `InitialCreate`.
- Se agrego seed de desarrollo con clientes, productos, maquinas, canales, productos frecuentes y asignaciones internas.
- Se mantuvo fallback `InMemory` configurable solo para desarrollo sin SQL Server.
- Se agregaron pruebas de persistencia con SQLite in-memory.
- Se agrego servicio Angular `CustomerOrdersApiService`.
- Se completo el primer flujo end-to-end funcional de Fase 1:
  - Cliente consulta "Mi pedido de hoy" desde la API.
  - Cliente edita cantidades y envia pedido real por API.
  - Cliente registra "No pedir hoy" por API.
  - Cliente ve estado de pedido enviado, tardio, pendiente de revision o `NoOrder`.
  - Administracion consulta pedidos reales del dia desde API.
  - Administracion consulta pendientes de revision desde API.
  - Administracion acepta o rechaza pedidos pendientes desde la UI y refresca la lista.
- Se amplio el contrato de `GET /api/customer-orders/{customerId}/today` con resumen del pedido actual del dia sin exponer maquina.
- Se agregaron validaciones de envio para rechazar cantidades negativas y pedidos sin cantidades positivas.
- Se evita duplicar `NoOrder` cuando ya existe un registro `NoOrder` del cliente para el dia.
- Se amplio el resumen administrativo con `customerName` y `adminDecision`.
- Se agrego mock API local para Playwright en `tests/e2e/mock-api.js`.
- Se agrego Docker Compose para SQL Server local/dev en `infra/dev`.
- Se agregaron scripts de desarrollo para levantar SQL Server, aplicar migraciones, correr API contra SQL Server y ejecutar smoke test de Fase 1.
- Se agrego `/health/db` para verificar conectividad real de base.
- Se valido la migracion inicial y el seed contra SQL Server 2022 local en Docker.
- Se agrego auditoria persistente minima de Fase 1 en `OrderAuditLogs`.
- Se agrego endpoint administrativo `GET /api/admin/orders/{orderId}/audit`.
- Se agrego migracion `AddOrderAuditLogs`.
- Se alineo `dotnet-ef` local a `10.0.8` con tool manifest del repositorio.
- Se agrego script de reset/reseed local `scripts/dev/reset-database.sh --confirm`.

## Decisiones tomadas

- Se uso .NET 10 porque el SDK `10.0.108` esta disponible.
- Se uso Angular 21 porque el CLI disponible corresponde a Angular CLI `21.1.2`.
- El CLI global `ng` queda colgado en este WSL; la app Angular se scaffoldo manualmente con dependencias Angular 21 y `@angular/build`.
- Tailwind no se uso; el frontend usa CSS estandar mobile first para evitar configuracion innecesaria en esta fase.
- La API usa EF Core + SQL Server por defecto; los repositorios en memoria quedan solo como fallback configurable.
- El seed se aplica solo en ambiente `Development`.
- Las pruebas de persistencia usan SQLite in-memory para no depender de SQL Server local.
- El frontend no simula exito en POST; muestra error si la API no responde o devuelve error.
- Playwright usa mock API local controlado para E2E basico y documenta esa decision.
- SQL Server local/dev puede ejecutarse con Docker Compose, pero la configuracion sigue permitiendo usar una instancia SQL Server instalada localmente mediante `ConnectionStrings__Pedidos`.
- La auditoria se escribe desde casos de uso de Application; no se expone al cliente y queda disponible solo en endpoint administrativo.

## Validacion ejecutada

- `dotnet restore src/Prodimt.Pedidos.sln`
- `dotnet build src/Prodimt.Pedidos.sln --no-restore`
- `dotnet test src/Prodimt.Pedidos.sln --no-build`
- `npm install` en `apps/prodimt-pedidos-web`
- `npm run build` en `apps/prodimt-pedidos-web`
- `npm install --save-dev @playwright/test` en `tests/e2e`
- `npm test` en `tests/e2e`
- `dotnet tool restore`
- `dotnet tool run dotnet-ef --version`
- `dotnet tool run dotnet-ef migrations add AddOrderAuditLogs --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api --output-dir Persistence/Migrations`
- `bash scripts/dev/reset-database.sh --confirm`
- `bash scripts/dev/start-sqlserver.sh`
- `bash scripts/dev/update-database.sh`
- `bash scripts/dev/run-api-sqlserver.sh`
- `bash scripts/dev/smoke-fase1.sh`
- Verificacion local de API con fallback `InMemory` para `/health` y `/api/customer-orders/{customerId}/today`
- `dotnet tool run dotnet-ef migrations add InitialCreate --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api --output-dir Persistence/Migrations`
- `dotnet build src/Prodimt.Pedidos.sln --no-restore`
- `dotnet test src/Prodimt.Pedidos.sln --no-restore`
- `npm run build` en `apps/prodimt-pedidos-web`
- `npm test` en `tests/e2e`

## Resultado

- Backend build: exitoso.
- Pruebas unitarias backend: 20 pruebas exitosas.
- Angular build: exitoso.
- Playwright E2E: 6 pruebas exitosas.
- API `/health`: responde `{"status":"ok"}`.
- API de cliente de ejemplo no expone maquina.
- Migracion inicial EF Core: creada.
- Flujo cliente/admin Fase 1 integrado desde Angular con API real para ejecucion normal.
- SQL Server real local: contenedor `prodimt-pedidos-sqlserver` levantado correctamente.
- Migracion inicial aplicada correctamente en SQL Server.
- Seed de desarrollo validado: 3 clientes, 4 productos, 3 maquinas, 3 canales, 4 productos frecuentes y 3 asignaciones internas.
- Smoke Fase 1 contra API real + SQL Server: exitoso.
- Auditoria persistente: implementada para pedido enviado, `NoOrder`, pedido tardio, segundo pedido del dia y decision administrativa.

## Pendiente

- Agregar autenticacion piloto.
- Implementar CRUD interno de catalogos.
- Agregar ajuste administrativo real de lineas para `AcceptedWithChanges`.
- Agregar vistas de detalle de lineas para administracion.
