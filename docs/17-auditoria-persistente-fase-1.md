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
- Captura administrativa de pedido.
- `NoOrder` registrado por administracion.
- Decision administrativa `Accepted`.
- Decision administrativa `Rejected`.
- Decision administrativa `AcceptedWithChanges`.
- Cambios administrativos aplicados a entrega o lineas existentes.

## Que no se audita todavia

- Validaciones fallidas.
- Cambios de maquina.
- Cambios de maquina.
- Agregado de productos nuevos durante revision.

## Auditoria de catalogos

Los cambios de catalogos se guardan en la tabla generica `AuditLogs`, separada de `OrderAuditLogs` para no forzar eventos sin pedido a tener `OrderId`.

Eventos cubiertos:

- Creacion, actualizacion, activacion y desactivacion de clientes.
- Creacion, actualizacion, activacion y desactivacion de productos.
- Creacion, actualizacion, activacion y desactivacion de maquinas.
- Cambios de productos frecuentes por cliente.
- Cambios de asignacion cliente-maquina.
- Creacion y revocacion de tokens de cliente.
- Creacion, activacion y desactivacion de usuarios admin basicos.
- Aplicacion de carga masiva controlada por CSV.

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
- `AdminManualOrderCaptured`
- `AdminNoOrderMarked`
- `AdminOrderChanged`

Eventos de `AuditLogs` para importacion:

- `BulkImportApplied`
- `CustomerImportedCreated`
- `CustomerImportedUpdated`
- `ProductImportedCreated`
- `ProductImportedUpdated`
- `MachineImportedCreated`
- `MachineImportedUpdated`
- `CustomerFrequentProductsImported`
- `CustomerMachineAssignmentsImported`

## Actores soportados

- `Customer`
- `Admin`
- `System`

La autenticacion piloto distingue cliente y admin en JWT. Los flujos administrativos nuevos propagan `ActorId` y `ActorDisplayName` desde el JWT admin cuando estan disponibles.

## Registro desde casos de uso

La escritura ocurre en Application:

- `CustomerOrderService.SubmitAsync`
- `CustomerOrderService.MarkNoOrderAsync`
- `AdminOrderService.ReviewAsync`
- `AdminOrderService.SubmitCustomerOrderAsync`
- `AdminOrderService.MarkNoOrderAsync`

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
- Captura administrativa crea `AdminManualOrderCaptured`.
- `NoOrder` administrativo crea `AdminNoOrderMarked`.
- `AcceptedWithChanges` crea `AdminOrderChanged` cuando aplica cambios.
- La consulta devuelve eventos ordenados.
- DTOs de cliente no exponen maquina ni auditoria.
- Importacion aplicada crea `BulkImportApplied` en `AuditLogs`.

El smoke real `scripts/dev/smoke-fase1.sh` consulta auditoria contra API + SQL Server.

## Limitaciones

- `AcceptedWithChanges` solo ajusta lineas existentes; no agrega productos nuevos.
- No se cambia maquina desde la UI en esta sesion.
- La identidad autenticada se propaga en flujos administrativos nuevos; eventos historicos de cliente siguen sin identidad enriquecida.

## Pendiente

- Auditar cambios de maquina cuando exista flujo administrativo.
- Ampliar vista administrativa de auditoria si operacion requiere filtros o busqueda.
