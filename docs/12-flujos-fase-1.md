# 12 — Flujos Fase 1

## Flujo A — Cliente confirma pedido sugerido

1. Cliente entra a la app desde celular.
2. Sistema identifica al cliente.
3. Sistema carga productos frecuentes.
4. Sistema muestra sugerencia basada en historial.
5. Cliente confirma o edita cantidades.
6. Sistema valida hora límite.
7. Sistema valida si ya existe pedido del día.
8. Si no hay condición especial, guarda pedido.
9. Administración puede verlo en el panel diario.

## Flujo B — Cliente indica no pedido

1. Cliente entra a la app.
2. Selecciona "No pedir hoy".
3. Sistema registra estado `NoOrder`.
4. Cliente sale de pendientes.
5. Administración puede verlo como cliente que no pidió.

## Flujo C — Pedido tardío

1. Cliente entra después de la hora límite.
2. Sistema permite capturar el pedido para no perder la información.
3. Sistema marca el pedido como tardío.
4. Sistema lo envía a revisión administrativa.
5. Administración decide:
   - aceptar,
   - rechazar,
   - aceptar con cambios,
   - aceptar con cambio de hora/condición de entrega.
6. La decisión queda auditada.

## Flujo D — Segundo pedido o cambio del día

1. Cliente ya tiene pedido registrado para la fecha.
2. Cliente intenta enviar otro pedido o modificar el anterior.
3. Sistema no reemplaza automáticamente el pedido confirmado.
4. Sistema crea solicitud pendiente de revisión.
5. Administración decide si acepta, rechaza, fusiona o ajusta entrega.
6. La decisión queda auditada.

## Flujo E — Captura interna por llamada

1. Administración ve clientes pendientes.
2. Administración llama al cliente.
3. Si el cliente pide, administración captura el pedido en su nombre.
4. Si el cliente no pide, administración marca `NoOrder`.
5. El registro queda con canal interno y usuario que capturó.

## Flujo F — Cambio interno de máquina

1. Un pedido tiene máquina asignada por defecto.
2. Administración detecta situación especial.
3. Administración cambia la máquina asignada.
4. El cliente no ve el cambio.
5. El cambio queda auditado.

## Flujo G — Pedido de mostrador

1. Un usuario interno registra venta o pedido de mostrador.
2. El sistema guarda el pedido con canal `InternalCounter` o equivalente.
3. No se asocia a cliente externo salvo que operación lo requiera.
4. No afecta sugerencias ni estadísticas de clientes externos.
