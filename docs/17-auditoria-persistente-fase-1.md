# 17 — Auditoria persistente Fase 1

Fecha: 2026-05-28

## Objetivo

Guardar una linea de tiempo minima y persistente de lo que paso con cada pedido en Fase 1.

## Que se audita

- Pedido enviado por cliente.
- Registro de `NoOrder`.
- Pedido tardio despues de las 10:00 a.m.
- Segundo pedido del mismo cliente en el dia.
- Pedido enviado a revision administrativa.
- Decision administrativa `Accepted`.
- Decision administrativa `Rejected`.
- Decision administrativa `AcceptedWithChanges`.

## Que no se audita todavia

- Validaciones fallidas.
- Cambios de maquina.
- Cambios detallados de lineas.
- Ajustes reales de cantidades u horarios para `AcceptedWithChanges`.
- Identidad real de usuario autenticado.

## Modelo

La tabla es `OrderAuditLogs`.

Campos principales:

- `Id`
- `OrderId`
- `CustomerId`
- `EventType`
- `OccurredAt`
- `ActorType`
- `ActorId`
- `ActorDisplayName`
- `OrderStatus`
- `AdminReviewReason`
- `AdminDecision`
- `Summary`
- `MetadataJson`
- `CreatedAt`

`MetadataJson` es opcional y debe usarse solo con datos controlados. No debe guardar secretos, connection strings ni datos sensibles innecesarios.

## Eventos soportados

- `OrderSubmitted`
- `NoOrderMarked`
- `OrderRequiresAdminReview`
- `OrderMarkedLate`
- `AdditionalOrderDetected`
- `AdminDecisionRecorded`

## Actores soportados

- `Customer`
- `Admin`
- `System`

Como aun no hay autenticacion real, `ActorId` queda nulo por ahora.

## Registro desde casos de uso

La escritura ocurre en Application:

- `CustomerOrderService.SubmitAsync`
- `CustomerOrderService.MarkNoOrderAsync`
- `AdminOrderService.ReviewAsync`

Los endpoints no contienen logica de auditoria.

## Consulta administrativa

Endpoint:

```http
GET /api/admin/orders/{orderId}/audit
```

Devuelve eventos ordenados por `OccurredAt`, `CreatedAt` e `Id`.

Este endpoint es administrativo. No hay endpoint de auditoria para cliente.

TODO: proteger este endpoint cuando se implemente autenticacion y autorizacion.

## Validacion

Pruebas backend cubren:

- Pedido normal crea `OrderSubmitted`.
- Pedido tardio crea eventos de tardio y revision.
- Segundo pedido crea evento `AdditionalOrderDetected`.
- `NoOrder` crea `NoOrderMarked`.
- Revision administrativa crea `AdminDecisionRecorded`.
- La consulta devuelve eventos ordenados.
- DTOs de cliente no exponen maquina ni auditoria.

El smoke real `scripts/dev/smoke-fase1.sh` consulta auditoria contra API + SQL Server.

## Limitaciones

- `AcceptedWithChanges` persiste decision y auditoria, pero aun no ajusta lineas ni horario.
- No hay identidad real de administrador hasta implementar autenticacion.
- No hay UI Angular para consultar auditoria; queda disponible por API administrativa.

## Pendiente

- Proteger endpoint admin con autenticacion y roles.
- Auditar cambios de maquina cuando exista flujo administrativo.
- Auditar modificaciones de lineas y horario de entrega.
- Agregar vista administrativa de auditoria.
