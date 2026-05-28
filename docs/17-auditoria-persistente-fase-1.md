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

La autenticacion piloto ya distingue cliente y admin en JWT, pero la auditoria de Fase 1 aun no propaga identidad real a `ActorId`. Ese enriquecimiento queda pendiente.

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

Queda protegido por `AdminAccess`. Un cliente autenticado no puede consultar auditoria.

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
- La identidad autenticada aun no se copia a `ActorId`/`ActorDisplayName` en cada evento.
- Existe una vista minima de auditoria en pendientes administrativos, pero no un detalle administrativo completo.

## Pendiente

- Propagar identidad autenticada real a los eventos de auditoria.
- Auditar cambios de maquina cuando exista flujo administrativo.
- Auditar modificaciones de lineas y horario de entrega.
- Ampliar vista administrativa de auditoria.
