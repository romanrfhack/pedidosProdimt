# 13 — Estado de implementacion inicial

Fecha: 2026-05-27

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

## Validacion ejecutada

- `dotnet restore src/Prodimt.Pedidos.sln`
- `dotnet build src/Prodimt.Pedidos.sln --no-restore`
- `dotnet test src/Prodimt.Pedidos.sln --no-build`
- `npm install` en `apps/prodimt-pedidos-web`
- `npm run build` en `apps/prodimt-pedidos-web`
- `npm install --save-dev @playwright/test` en `tests/e2e`
- `npm test` en `tests/e2e`
- Verificacion local de API con fallback `InMemory` para `/health` y `/api/customer-orders/{customerId}/today`
- `dotnet ef migrations add InitialCreate --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api --output-dir Persistence/Migrations`
- `dotnet build src/Prodimt.Pedidos.sln --no-restore`
- `dotnet test src/Prodimt.Pedidos.sln --no-restore`
- `npm run build` en `apps/prodimt-pedidos-web`
- `npm test` en `tests/e2e`

## Resultado

- Backend build: exitoso.
- Pruebas unitarias backend: 19 pruebas exitosas.
- Angular build: exitoso.
- Playwright E2E: 6 pruebas exitosas.
- API `/health`: responde `{"status":"ok"}`.
- API de cliente de ejemplo no expone maquina.
- Migracion inicial EF Core: creada.
- Flujo cliente/admin Fase 1 integrado desde Angular con API real para ejecucion normal.

## Pendiente

- Aplicar migracion a SQL Server local/dev y validar endpoint completo contra SQL Server.
- Agregar autenticacion piloto.
- Agregar auditoria persistente.
- Implementar CRUD interno de catalogos.
- Agregar ajuste administrativo real de lineas para `AcceptedWithChanges`.
- Agregar vistas de detalle de lineas para administracion.
