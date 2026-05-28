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
- Se agrego autenticacion piloto con JWT Bearer.
- Se agregaron entidades `AdminUser` y `CustomerAccessToken`.
- Se agrego migracion `AddPilotAuthentication`.
- Se agregaron endpoints `POST /api/auth/admin/login` y `POST /api/auth/customer-token`.
- Se protegio `GET/POST /api/customer-orders/{customerId}/...` con `CustomerAccess` y validacion de `customerId` propio.
- Se protegieron endpoints administrativos, incluyendo auditoria, con `AdminAccess`.
- Se agrego seed Development de admin demo y token demo de Gran Takito.
- Se actualizo Angular con login cliente por token, login admin, interceptor Bearer, guard admin y consulta minima de auditoria desde pendientes.
- Se actualizo Playwright para usar auth mockeada.
- Se actualizo el smoke real de Fase 1 para autenticacion.
- Se agrego detalle administrativo `GET /api/admin/orders/{orderId}` con lineas, canal y maquina interna.
- Se agrego lista administrativa de clientes pendientes `GET /api/admin/customers/pending-orders`.
- Se agrego plantilla administrativa de pedido `GET /api/admin/customers/{customerId}/order-template`.
- Se agrego captura administrativa `POST /api/admin/customers/{customerId}/orders/submit`.
- Se agrego `NoOrder` administrativo `POST /api/admin/customers/{customerId}/orders/no-order`.
- Se implemento `AcceptedWithChanges` real para hora/notas de entrega y cantidades/notas de lineas existentes.
- Se agregaron eventos de auditoria `AdminManualOrderCaptured`, `AdminNoOrderMarked` y `AdminOrderChanged`.
- Se propaga identidad admin del JWT a auditoria en flujos administrativos nuevos.
- Se actualizo Angular con detalle de pedido, clientes pendientes, captura administrativa y aceptacion con cambios.
- Se agrego CRUD interno minimo de catalogos administrativos:
  - clientes con horario/ventana/notas de entrega,
  - productos/moldes,
  - productos frecuentes por cliente,
  - maquinas,
  - asignacion cliente-maquina,
  - tokens de acceso de cliente,
  - usuarios administrativos basicos.
- Se agrego auditoria generica `AuditLogs` para cambios relevantes de catalogo sin mezclarla con `OrderAuditLogs`.
- Se agrego migracion `AddCatalogManagementSupport`.
- Se agrego UI Angular administrativa en `/admin/catalogos`.
- Se amplio Playwright para navegar catalogos, abrir configuracion de cliente y validar que cliente no vea catalogos.

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
- La autenticacion piloto usa JWT Bearer con claims explicitos `prodimt_actor_type`, `prodimt_customer_id`, `prodimt_user_id`, `prodimt_user_name` y `prodimt_display_name`.
- Los endpoints de cliente aceptan solo JWT de cliente; la captura en nombre de cliente se implementa por endpoints administrativos separados con `AdminAccess`.
- `/health` y `/health/db` quedan publicos en Fase 1. `/health/db` no expone datos sensibles.
- Angular guarda el JWT en `localStorage` solo para desarrollo piloto; debe revisarse antes de produccion.
- La maquina asignada se consulta en detalle administrativo, pero sigue excluida de DTOs y pantallas de cliente.
- En `AcceptedWithChanges` se ajustan lineas existentes; agregar productos nuevos y cambiar maquina quedan fuera de esta sesion.
- La gestion de tokens de cliente muestra el token plano solo en la respuesta de creacion; despues solo se listan metadatos.
- La UI de catalogos cubre operacion basica; usuarios admin basicos quedaron como API protegida, sin pantalla dedicada.

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
- `dotnet tool run dotnet-ef migrations add AddPilotAuthentication --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api --output-dir Persistence/Migrations --no-build`
- `bash scripts/dev/start-sqlserver.sh`
- `bash scripts/dev/update-database.sh`
- `bash scripts/dev/reset-database.sh --confirm`
- `bash scripts/dev/run-api-sqlserver.sh`
- `bash scripts/dev/smoke-fase1.sh`
- `git diff --check`
- `bash -n scripts/dev/smoke-fase1.sh scripts/dev/start-sqlserver.sh scripts/dev/update-database.sh scripts/dev/reset-database.sh scripts/dev/run-api-sqlserver.sh`
- `node --check scripts/dev/smoke-fase1.mjs`
- `dotnet build src/Prodimt.Pedidos.sln --no-restore`
- `dotnet test src/Prodimt.Pedidos.sln --no-restore`
- `npm run build` en `apps/prodimt-pedidos-web`
- `npm test` en `tests/e2e`
- `node --check tests/e2e/mock-api.js`
- `dotnet tool run dotnet-ef migrations add AddCatalogManagementSupport --project src/Prodimt.Pedidos.Infrastructure --startup-project src/Prodimt.Pedidos.Api --output-dir Persistence/Migrations --no-build`

## Resultado

- Backend build: exitoso.
- Pruebas unitarias/integracion backend: 50 pruebas exitosas.
- Angular build: exitoso.
- Playwright E2E: 7 pruebas exitosas.
- Playwright E2E con catalogos: 14 pruebas exitosas.
- API `/health`: responde `{"status":"ok"}`.
- API de cliente de ejemplo no expone maquina.
- Migracion inicial EF Core: creada.
- Flujo cliente/admin Fase 1 integrado desde Angular con API real para ejecucion normal.
- SQL Server real local: contenedor `prodimt-pedidos-sqlserver` levantado correctamente.
- Migraciones `InitialCreate`, `AddOrderAuditLogs`, `AddPilotAuthentication` y `AddCatalogManagementSupport` aplicadas correctamente en SQL Server.
- Seed de desarrollo validado: clientes, productos, maquinas, canales, productos frecuentes, asignaciones internas, admin demo y token demo de cliente.
- Smoke Fase 1 autenticado contra API real + SQL Server: exitoso, incluyendo catalogos internos y revocacion de token.
- Auditoria persistente: implementada para pedido enviado, `NoOrder`, pedido tardio, segundo pedido del dia y decision administrativa.
- Autenticacion piloto: implementada para cliente por token y admin por login demo en Development.
- Administracion operativa Fase 1: implementada para detalle con lineas, clientes pendientes, captura administrativa, `NoOrder` administrativo y `AcceptedWithChanges`.
- Catalogos internos Fase 1: implementados para preparar piloto con datos reales sin editar SQL directamente.

## Pendiente

- Endurecer autenticacion antes de produccion: secrets reales por entorno, expiracion/rotacion de tokens cliente, estrategia de almacenamiento frontend y roles finos.
- Agregar productos nuevos durante `AcceptedWithChanges`.
- Cambiar maquina desde administracion y auditarlo cuando entre en alcance.
- Definir importacion controlada desde Excel o carga masiva asistida para clientes reales.
