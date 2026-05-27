# 06 — Modelo de datos inicial

Este modelo es conceptual. Debe refinarse durante el diseño técnico.

## Entidades principales

### Customer

Representa al cliente.

Campos candidatos:

- CustomerId
- DisplayName
- LegalName opcional
- PrimaryPhone
- SecondaryPhone opcional
- RouteId opcional
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

### Order

Pedido del cliente para una fecha.

Campos candidatos:

- OrderId
- CustomerId
- OrderDate
- DeliveryDate opcional
- Status
- CaptureChannel
- SubmittedAt
- SubmittedByUserId opcional
- Notes
- IsLate
- CreatedAt
- UpdatedAt

Estados candidatos:

- Draft
- Suggested
- Submitted
- Confirmed
- Cancelled
- Processed

Canales candidatos:

- CustomerApp
- InternalCall
- WhatsAppConfirmation
- Import
- AdminEdit

### OrderLine

Detalle del pedido.

Campos candidatos:

- OrderLineId
- OrderId
- ProductId
- Quantity
- Unit opcional
- Notes
- SourceSuggestionLineId opcional
- WasChangedFromSuggestion

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

1. Un cliente puede tener máximo un pedido activo por fecha.
2. Un pedido puede tener muchas líneas.
3. Una línea de pedido debe tener producto y cantidad.
4. La cantidad debe ser mayor o igual a cero.
5. Cero debe significar cantidad cero; no debe mezclarse con la marca `x/X` del Excel sin definirla.
6. Todo cambio después de la hora límite debe marcarse como tardío o auditado.
7. Las sugerencias nunca deben enviarse como pedido confirmado sin acción del cliente o usuario interno.

## Limpieza requerida

Antes de importar masivamente:

- Normalizar nombres de clientes.
- Unificar moldes equivalentes.
- Confirmar significado de `x/X`.
- Confirmar uso de columna E del Excel.
- Definir si `Mostrador` es cliente, canal o categoría interna.
