# 06 — Modelo de datos inicial

Este modelo es conceptual. Debe refinarse durante el diseño técnico.

## Entidades principales

### Customer

Representa al cliente externo.

Campos candidatos:

- CustomerId
- DisplayName
- LegalName opcional
- PrimaryPhone
- SecondaryPhone opcional
- RouteId opcional
- PreferredDeliveryTime opcional
- PreferredDeliveryWindowStart opcional
- PreferredDeliveryWindowEnd opcional
- DeliveryNotes opcional
- IsActive
- CreatedAt
- UpdatedAt

### Product

Representa producto, molde o categoría vendible.

Campos candidatos:

- ProductId
- Name
- CanonicalCode
- ProductType
- IsActive

Ejemplos:

- #9.5
- #10
- #10.5
- Flauta
- Vapor
- Sancochado
- Grueso

### CustomerProductPreference

Relación entre cliente y producto que normalmente pide.

Campos candidatos:

- CustomerId
- ProductId
- DefaultQuantity opcional
- PreferredWeekdays opcional
- DisplayOrder
- IsFrequent
- DefaultMachineId opcional, interno

### Machine

Representa una máquina de producción o atención interna.

Campos candidatos:

- MachineId
- MachineNumber
- DisplayName
- IsActive

Notas:

- La máquina no debe mostrarse al cliente.
- Puede usarse después para vistas de producción.
- En Fase 1 basta con modelarla y permitir asignación básica.

### CustomerMachineAssignment

Asignación interna por defecto entre cliente, producto y máquina.

Campos candidatos:

- CustomerMachineAssignmentId
- CustomerId
- ProductId opcional
- Weekday opcional
- MachineId
- IsDefault
- IsActive

Esta entidad puede omitirse temporalmente si en MVP se decide guardar `DefaultMachineId` directamente en `CustomerProductPreference`.

### Order

Pedido del cliente para una fecha.

Campos candidatos:

- OrderId
- CustomerId nullable para ventas internas de mostrador
- OrderDate
- DeliveryDate opcional
- RequestedDeliveryTime opcional
- RequestedDeliveryWindowStart opcional
- RequestedDeliveryWindowEnd opcional
- Status
- CaptureChannel
- SalesChannel
- SubmittedAt
- SubmittedByUserId opcional
- Notes
- IsLate
- RequiresAdminReview
- AdminReviewReason opcional
- ReviewedByUserId opcional
- ReviewedAt opcional
- AdminDecision opcional
- RejectionReason opcional
- SequenceNumber
- CreatedAt
- UpdatedAt

Estados candidatos:

- Draft
- Submitted
- PendingAdminReview
- Accepted
- Rejected
- Cancelled
- NoOrder
- Superseded

Razones de revisión administrativa candidatas:

- LateSubmission
- AdditionalOrderSameDay
- PostConfirmationEdit
- ManualAdminReview

Decisiones administrativas candidatas:

- Pending
- Accepted
- Rejected
- AcceptedWithDeliveryTimeChange
- AcceptedWithChanges

Canales de captura candidatos:

- CustomerApp
- InternalCall
- WhatsAppConfirmation
- Import
- AdminEdit

Canales de venta candidatos:

- ExternalCustomer
- InternalCounter, equivalente a Mostrador

### OrderLine

Detalle del pedido.

Campos candidatos:

- OrderLineId
- OrderId
- ProductId
- Quantity
- Unit opcional
- Notes
- AssignedMachineId opcional, interno
- SourceSuggestionLineId opcional
- WasChangedFromSuggestion

### NoOrderRecord

Puede modelarse como una entidad separada o como un `Order` con estado `NoOrder` y sin líneas.

Recomendación inicial: modelarlo como `Order.Status = NoOrder` para que el cliente salga de pendientes y todo quede en la misma línea de tiempo.

Campos candidatos si se separa:

- NoOrderRecordId
- CustomerId
- OrderDate
- CaptureChannel
- SubmittedAt
- SubmittedByUserId opcional
- Notes opcional

### OrderSuggestion

Sugerencia calculada para un cliente y fecha.

Campos candidatos:

- CustomerId
- SuggestedForDate
- BasedOnOrderIds
- CreatedAt

Puede calcularse bajo demanda al inicio, sin persistirse.

### AuditLog

Registro de cambios.

Campos candidatos:

- AuditLogId
- EntityName
- EntityId
- Action
- OldValue
- NewValue
- ActorUserId
- OccurredAt

### User

Usuario interno o cliente.

Campos candidatos:

- UserId
- CustomerId nullable
- Name
- Phone
- Email nullable
- Role
- IsActive

### Route

Ruta opcional para reparto.

Campos candidatos:

- RouteId
- Name
- DefaultDriverUserId opcional
- IsActive

## Reglas de negocio iniciales

1. Un cliente puede tener máximo un pedido activo aceptado o enviado por fecha sin revisión adicional.
2. Si un cliente intenta crear otro pedido el mismo día, debe crearse una solicitud pendiente de revisión administrativa.
3. Un pedido tardío después de las 10:00 a.m. debe marcarse como `IsLate = true` y `RequiresAdminReview = true`.
4. Un pedido puede tener muchas líneas.
5. Una línea de pedido debe tener producto y cantidad.
6. La cantidad debe ser mayor o igual a cero.
7. Cero significa cantidad cero en un producto; `x/X` del Excel significa no pidió y debe mapearse a estado `NoOrder`.
8. Todo cambio después de la hora límite debe marcarse como tardío o auditado.
9. Las sugerencias nunca deben enviarse como pedido confirmado sin acción del cliente o usuario interno.
10. La máquina asignada es interna y no debe exponerse en endpoints o vistas de cliente.
11. `Mostrador` debe manejarse como canal de venta interno, no como cliente externo.
12. La hora deseada de entrega vive en el perfil de cliente, pero debe copiarse al pedido para conservar el contexto histórico.

## Limpieza requerida

Antes de importar masivamente:

- Normalizar nombres de clientes.
- Unificar moldes equivalentes.
- Mapear `x/X` a no pedido.
- Mapear columna E a máquina asignada.
- Separar `Mostrador` como canal interno.
- Definir catálogo inicial de máquinas.
