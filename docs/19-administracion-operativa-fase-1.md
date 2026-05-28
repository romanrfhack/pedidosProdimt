# 19 — Administracion operativa Fase 1

Fecha: 2026-05-28

## Que resuelve

Administracion ya puede operar los pedidos capturados sin depender del Excel ni de WhatsApp:

- Ver detalle interno de pedido con lineas.
- Ver clientes activos que no han respondido para la fecha.
- Capturar pedido en nombre de cliente.
- Marcar `NoOrder` en nombre de cliente.
- Aceptar, rechazar o aceptar con cambios reales.
- Consultar auditoria administrativa.

## Endpoints administrativos nuevos

Todos requieren JWT con `AdminAccess`.

- `GET /api/admin/orders/{orderId}`
- `GET /api/admin/customers/pending-orders?date=YYYY-MM-DD`
- `GET /api/admin/customers/{customerId}/order-template`
- `POST /api/admin/customers/{customerId}/orders/submit`
- `POST /api/admin/customers/{customerId}/orders/no-order`

Tambien se amplio:

- `POST /api/admin/orders/{orderId}/review`

## Flujo de clientes pendientes

`GET /api/admin/customers/pending-orders` devuelve clientes activos que no tienen pedido ni `NoOrder` para la fecha.

Si no se envia `date`, se usa la fecha actual del proveedor de tiempo.

La respuesta incluye:

- cliente,
- telefono,
- hora o ventana preferida,
- notas de entrega,
- conteo de productos frecuentes.

No incluye maquina.

## Flujo de captura administrativa

Administracion obtiene plantilla con:

```http
GET /api/admin/customers/{customerId}/order-template
```

Luego captura:

```http
POST /api/admin/customers/{customerId}/orders/submit
```

Reglas:

- Usa canal `AdminManualCapture`.
- Omite lineas con cantidad cero.
- Rechaza cantidades negativas.
- Rechaza pedidos sin cantidades positivas.
- Asigna maquina interna por asignacion default si existe.
- Aplica regla normal de pedido tardio.
- Aplica regla normal de segundo pedido del dia.
- Audita con actor admin desde JWT cuando esta disponible.

## Flujo de NoOrder administrativo

```http
POST /api/admin/customers/{customerId}/orders/no-order
```

Reglas:

- Crea `NoOrder` si el cliente no habia respondido.
- Si ya existe `NoOrder`, devuelve el existente.
- Si ya existe pedido activo, responde conflicto.
- Audita `AdminNoOrderMarked`.
- Saca al cliente de pendientes.

## Detalle de pedido

`GET /api/admin/orders/{orderId}` devuelve:

- datos del pedido,
- estado y revision,
- hora o ventana de entrega,
- notas de entrega e internas,
- canal de venta,
- lineas con producto, cantidad y notas,
- maquina asignada interna cuando existe.

Este DTO es solo administrativo. Los DTOs de cliente siguen sin exponer maquina, lineas administrativas ni auditoria.

## AcceptedWithChanges

`POST /api/admin/orders/{orderId}/review` acepta:

- `Accepted`
- `Rejected`
- `AcceptedWithChanges`

Para `AcceptedWithChanges` se aplican cambios reales en:

- `requestedDeliveryTime`
- `requestedDeliveryWindowStart`
- `requestedDeliveryWindowEnd`
- `deliveryNotes`
- cantidad de lineas existentes
- notas de lineas existentes

Validaciones:

- No se permiten cantidades negativas.
- No se permite `quantity = 0` en ajustes de linea.
- No se agregan productos nuevos.
- No se cambia maquina.

## Auditoria generada

Eventos nuevos:

- `AdminManualOrderCaptured`
- `AdminNoOrderMarked`
- `AdminOrderChanged`

Eventos reutilizados:

- `OrderMarkedLate`
- `AdditionalOrderDetected`
- `OrderRequiresAdminReview`
- `AdminDecisionRecorded`

`AcceptedWithChanges` guarda resumen y metadata de cambios. La auditoria sigue disponible solo para administracion:

```http
GET /api/admin/orders/{orderId}/audit
```

## UI Angular

Administracion tiene:

- detalle en Pedidos de hoy,
- aceptacion con cambios en Pendientes de revision,
- vista Clientes pendientes,
- formulario simple de captura administrativa,
- accion `No pedir hoy` administrativa,
- auditoria minima por pedido.

Playwright sigue usando mock API y no depende de SQL Server.

## Limitaciones actuales

- No hay WhatsApp real.
- No hay produccion por maquina.
- No hay embarques, repartidores ni rutas avanzadas.
- No hay estadisticas avanzadas.
- No hay pagos ni facturacion.
- No se agregan productos nuevos durante revision.
- No se cambia maquina desde UI.
- No hay CRUD interno de catalogos.
- La autenticacion sigue siendo piloto.

## Que queda pendiente

- CRUD interno de clientes, productos, maquinas y canales.
- Cambio administrativo de maquina con auditoria.
- Agregar productos nuevos en ajustes administrativos.
- Endurecer autenticacion antes de produccion.
- Definir flujo operativo para fusionar pedidos adicionales.
