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
- Se agregaron repositorios en memoria para que la API inicial funcione sin base de datos local.
- Se agregaron endpoints minimos de cliente y administracion.
- Se creo app Angular 21 en `apps/prodimt-pedidos-web`.
- Se agregaron pantallas iniciales:
  - Cliente: "Mi pedido de hoy".
  - Admin: "Pedidos de hoy".
  - Admin: "Pendientes de revision".
- Se agregaron pruebas unitarias xUnit para reglas criticas.
- Se agrego Playwright E2E para validar acciones principales y que la vista de cliente no muestre informacion de maquina.
- Se agregaron `.gitignore` y `.editorconfig`.

## Decisiones tomadas

- Se uso .NET 10 porque el SDK `10.0.108` esta disponible.
- Se uso Angular 21 porque el CLI disponible corresponde a Angular CLI `21.1.2`.
- El CLI global `ng` queda colgado en este WSL; la app Angular se scaffoldo manualmente con dependencias Angular 21 y `@angular/build`.
- Tailwind no se uso; el frontend usa CSS estandar mobile first para evitar configuracion innecesaria en esta fase.
- La API usa repositorios en memoria para el arranque inicial, pero `Infrastructure` ya contiene `DbContext` y configuracion para SQL Server.
- No se creo migracion inicial todavia; debe generarse cuando se confirme el SQL Server local/dev.

## Validacion ejecutada

- `dotnet restore src/Prodimt.Pedidos.sln`
- `dotnet build src/Prodimt.Pedidos.sln --no-restore`
- `dotnet test src/Prodimt.Pedidos.sln --no-build`
- `npm install` en `apps/prodimt-pedidos-web`
- `npm run build` en `apps/prodimt-pedidos-web`
- `npm install --save-dev @playwright/test` en `tests/e2e`
- `npm test` en `tests/e2e`
- Verificacion local de API para `/health` y `/api/customer-orders/{customerId}/today`

## Resultado

- Backend build: exitoso.
- Pruebas unitarias backend: 6 pruebas exitosas.
- Angular build: exitoso.
- Playwright E2E: 2 pruebas exitosas.
- API `/health`: responde `{"status":"ok"}`.
- API de cliente de ejemplo no expone maquina.

## Pendiente

- Crear migracion EF Core inicial cuando se confirme la base SQL Server local/dev.
- Sustituir repositorios en memoria por repositorios EF Core reales.
- Agregar autenticacion piloto.
- Agregar auditoria persistente.
- Implementar CRUD interno de catalogos.
- Integrar frontend con API real.
