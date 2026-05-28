# 08 — Contexto para Codex

## Proyecto

PRODIMT Pedidos.

## Propósito

Construir una aplicación web mobile first para capturar pedidos de clientes y reemplazar gradualmente el proceso manual basado en WhatsApp, llamadas y captura en Excel.

## Estado actual

- Ya existe estructura técnica inicial de backend, frontend y pruebas.
- La API usa EF Core + SQL Server por defecto y conserva fallback en memoria configurable para desarrollo.
- Existe migracion inicial EF Core y seed de desarrollo.
- El primer flujo end-to-end funcional de Fase 1 ya esta integrado:
  - Cliente ve productos frecuentes desde API.
  - Cliente envia pedido real y marca "No pedir hoy" por API.
  - Cliente ve estado actual del pedido del dia.
  - Administracion ve pedidos del dia, pendientes de revision y puede aceptar o rechazar.
- El estado de implementación inicial está documentado en `docs/13-estado-implementacion-inicial.md`.
- La persistencia EF Core está documentada en `docs/14-persistencia-ef-core-sql-server.md`.
- La integracion frontend/API de Fase 1 está documentada en `docs/15-integracion-frontend-api-fase-1.md`.
- El Excel `05 EMBARQUES Mayo-04.xlsm` fue analizado como referencia operativa.
- La prioridad es crear primero una versión útil para captura de pedidos.
- Las decisiones operativas confirmadas están documentadas en `docs/10-decisiones-operativas-confirmadas.md`.

## Stack deseado

- Backend: .NET 10.
- Arquitectura backend: Clean Architecture.
- Base de datos: SQL Server.
- Frontend: Angular, objetivo Angular 21 salvo que al iniciar el repo exista una versión estable más conveniente.
- Estilo: mobile first.
- CSS: Tailwind opcional.
- Pruebas E2E: Playwright.
- API documentada con OpenAPI/Swagger.

## Principios de implementación

1. El sistema debe ser mobile first.
2. Excel no debe ser la base de datos principal.
3. WhatsApp debe ser canal de comunicación, no fuente de verdad.
4. SQL Server debe ser la fuente de verdad.
5. El cliente solo ve sus propios pedidos.
6. La app debe mostrar productos frecuentes primero.
7. La captura rápida es más importante que reportes avanzados.
8. Todo cambio de pedido debe ser auditable.
9. Las decisiones técnicas importantes deben documentarse como ADR.
10. Cada sesión de Codex debe actualizar este documento o un archivo de estado equivalente si cambia el alcance o avance.
11. La máquina asignada es dato interno y no debe exponerse al cliente.
12. Pedidos tardíos y segundos pedidos del día requieren decisión administrativa.
13. `Mostrador` es canal interno, no cliente externo.

## Flujo principal a implementar primero

1. Cliente entra desde celular.
2. Sistema identifica al cliente.
3. Sistema muestra sugerencia de pedido.
4. Cliente repite o edita cantidades.
5. Cliente confirma.
6. Si está dentro de horario y no existe pedido previo, el pedido queda enviado/aceptado según regla MVP.
7. Si está fuera de horario, queda pendiente de revisión administrativa.
8. Si ya existía pedido del día, queda pendiente de revisión administrativa.
9. Administración ve el pedido.
10. Administración ve quién falta de pedir.
11. Administración acepta, rechaza o ajusta pedidos sujetos a revisión.

## Flujo alterno: no pedido

1. Cliente entra desde celular o administración registra llamada.
2. Se marca "No pedir hoy".
3. El cliente sale de pendientes.
4. Se guarda registro auditable.

## No implementar primero

- Dashboard ejecutivo complejo.
- Inteligencia artificial avanzada.
- Optimización de rutas.
- Integración completa de WhatsApp.
- Reemplazo total del Excel.
- Facturación.
- App nativa iOS/Android.
- Vista completa de producción por máquina.
- Vista completa de embarques.
- Vista completa de repartidores.

## Siguiente tarea sugerida para Codex

Continuar con endurecimiento del flujo vertical:

- Aplicar migracion inicial en SQL Server local/dev y validar endpoints contra base real.
- Agregar autenticación piloto sin implementar roles completos todavía.
- Agregar auditoría persistente mínima para pedido creado, no pedido y decisión administrativa.
- Agregar detalle administrativo de lineas y ajuste real para `AcceptedWithChanges`.

## Criterio para futuras sesiones

Al iniciar una nueva sesión, leer siempre:

- `README.md`
- `docs/08-contexto-para-codex.md`
- `docs/10-decisiones-operativas-confirmadas.md`
- `docs/11-reglas-de-negocio-fase-1.md`
- `docs/12-flujos-fase-1.md`
- `docs/02-alcance-y-etapas.md`
- `docs/03-requerimientos-funcionales.md`
- `docs/07-backlog-mvp.md`
- `docs/13-estado-implementacion-inicial.md`
- `docs/14-persistencia-ef-core-sql-server.md`

Al terminar una sesión, dejar documentado:

- Qué se creó.
- Qué falta.
- Qué decisiones se tomaron.
- Qué comandos se usaron.
- Qué pruebas pasan.
