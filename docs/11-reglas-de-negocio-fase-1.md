# 11 — Reglas de negocio Fase 1

## BR-001 — Cliente pendiente

Un cliente está pendiente para una fecha cuando:

- Está activo.
- Se espera que pueda pedir ese día.
- No tiene pedido enviado/aceptado.
- No tiene registro de no pedido.

## BR-002 — No pedido

Cuando un cliente indica que no pedirá:

- Se registra un pedido o evento con estado `NoOrder`.
- El cliente sale de pendientes.
- La acción se audita.

## BR-003 — Pedido dentro de horario

Si el cliente envía pedido antes de la hora límite y no tiene pedido previo activo para la fecha:

- El pedido puede quedar como `Submitted` o `Accepted` según regla operativa del MVP.
- No requiere revisión administrativa automática.

Recomendación inicial: usar `Submitted` para indicar que el cliente lo envió y permitir que administración lo procese.

## BR-004 — Pedido tardío

Si el cliente envía pedido después de la hora límite:

- `IsLate = true`.
- `RequiresAdminReview = true`.
- `AdminReviewReason = LateSubmission`.
- Estado sugerido: `PendingAdminReview`.

El administrador puede:

- Aceptar.
- Rechazar.
- Aceptar con cambios.
- Aceptar con cambio de hora o condición de entrega.

## BR-005 — Segundo pedido del día

Si un cliente ya tiene pedido activo y envía otro pedido o cambio para la misma fecha:

- El sistema no debe reemplazar el pedido anterior automáticamente.
- Debe crear una solicitud o pedido pendiente de revisión.
- `AdminReviewReason = AdditionalOrderSameDay` o `PostConfirmationEdit`.

## BR-006 — Hora deseada de entrega

El perfil del cliente puede tener hora o ventana deseada de entrega.

Al crear un pedido:

- La hora o ventana deseada se copia al pedido.
- Administración puede ajustarla si acepta un pedido tardío, duplicado o especial.

## BR-007 — Máquina asignada

La máquina asignada es dato interno.

Reglas:

- Puede venir de la preferencia cliente-producto.
- Puede cambiarla un administrador.
- No debe mostrarse al cliente.
- Todo cambio debe auditarse.

## BR-008 — Mostrador

Los pedidos de mostrador se registran como canal interno.

Reglas:

- No se consideran pedidos de cliente externo.
- No afectan sugerencias personalizadas de clientes.
- Pueden aparecer en vistas internas y exportaciones.

## BR-009 — Sugerencia de pedido

La sugerencia no confirma pedido por sí sola.

Reglas:

- Debe requerir acción del cliente o usuario interno.
- Debe basarse primero en últimos pedidos del mismo día de la semana.
- Debe mostrar productos frecuentes antes que productos no frecuentes.

## BR-010 — Auditoría mínima

Deben auditarse:

- Pedido creado.
- Pedido confirmado.
- Pedido editado.
- No pedido.
- Pedido tardío.
- Segundo pedido del día.
- Decisión administrativa.
- Cambio de entrega.
- Cambio de máquina.
