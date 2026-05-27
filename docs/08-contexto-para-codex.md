# 08 — Contexto para Codex

## Proyecto

PRODIMT Pedidos.

## Propósito

Construir una aplicación web mobile first para capturar pedidos de clientes y reemplazar gradualmente el proceso manual basado en WhatsApp, llamadas y captura en Excel.

## Estado actual

- Solo existe documentación inicial.
- No se ha generado código todavía.
- El Excel `05 EMBARQUES Mayo-04.xlsm` fue analizado como referencia operativa.
- La prioridad es crear primero una versión útil para captura de pedidos.

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

## Flujo principal a implementar primero

1. Cliente entra desde celular.
2. Sistema identifica al cliente.
3. Sistema muestra sugerencia de pedido.
4. Cliente repite o edita cantidades.
5. Cliente confirma.
6. Administración ve el pedido.
7. Administración ve quién falta de pedir.

## No implementar primero

- Dashboard ejecutivo complejo.
- Inteligencia artificial avanzada.
- Optimización de rutas.
- Integración completa de WhatsApp.
- Reemplazo total del Excel.
- Facturación.
- App nativa iOS/Android.

## Siguiente tarea sugerida para Codex

Crear la estructura inicial del repositorio:

```text
/
  README.md
  docs/
  src/
    Prodimt.Pedidos.Api/
    Prodimt.Pedidos.Application/
    Prodimt.Pedidos.Domain/
    Prodimt.Pedidos.Infrastructure/
  tests/
    Prodimt.Pedidos.UnitTests/
    Prodimt.Pedidos.IntegrationTests/
  apps/
    prodimt-pedidos-web/
```

Luego implementar el modelo base:

- Customer
- Product
- CustomerProductPreference
- Order
- OrderLine
- AuditLog

## Criterio para futuras sesiones

Al iniciar una nueva sesión, leer siempre:

- `README.md`
- `docs/08-contexto-para-codex.md`
- `docs/02-alcance-y-etapas.md`
- `docs/03-requerimientos-funcionales.md`
- `docs/07-backlog-mvp.md`

Al terminar una sesión, dejar documentado:

- Qué se creó.
- Qué falta.
- Qué decisiones se tomaron.
- Qué comandos se usaron.
- Qué pruebas pasan.
