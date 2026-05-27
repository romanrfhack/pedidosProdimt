# 10 — Decisiones operativas confirmadas

Fecha: 2026-05-27

Este documento registra decisiones de negocio ya confirmadas para evitar que se vuelvan a discutir en cada sesión de desarrollo.

## D-001 — Significado de `x/X`

En el Excel actual, `x` o `X` significa **no pidió**.

Implicación para el sistema:

- No debe guardarse como texto de pedido.
- No debe interpretarse simplemente como cantidad cero.
- Debe representarse como estado explícito de cliente sin pedido para la fecha.

## D-002 — Pedidos tardíos

La hora límite inicial es 10:00 a.m.

Si un cliente hace pedido después de la hora límite:

- El pedido debe marcarse como tardío.
- El pedido debe quedar pendiente de revisión administrativa.
- El administrador puede aceptarlo, rechazarlo o aceptarlo con cambio de hora/condición de entrega.

## D-003 — Más de un pedido por cliente en el mismo día

Si un cliente ya tenía pedido confirmado o enviado y solicita otro pedido el mismo día:

- El sistema no debe aceptarlo automáticamente.
- Debe quedar pendiente de revisión administrativa.
- El administrador decide si lo acepta, rechaza, fusiona con el pedido anterior o ajusta la entrega.

## D-004 — Hora deseada de entrega

Algunos clientes tienen un horario específico o deseado para recibir su pedido.

Implicación para el sistema:

- El catálogo de clientes debe tener hora o ventana deseada de entrega como campo opcional.
- El pedido debe copiar esa información al momento de capturarse para mantener histórico.
- Administración debe poder modificar la hora/condición de entrega al aceptar un pedido tardío o duplicado.

## D-005 — Mostrador

`Mostrador` es un canal de venta interno.

Implicación para el sistema:

- No debe tratarse como cliente externo.
- No debe afectar estadísticas de clientes.
- Puede existir como canal de captura/venta interna.

## D-006 — Columna E del Excel

La columna E de las hojas diarias representa el número de máquina que atenderá el pedido.

Implicación para el sistema:

- Se debe modelar máquina como dato interno.
- Normalmente cada máquina tiene clientes asignados.
- Un administrador puede cambiar la máquina en situaciones especiales.
- El cliente no debe saber qué máquina atenderá su pedido.

## D-007 — Enfoque de Fase 1

La Fase 1 se concentrará en obtener la información del pedido del cliente.

Quedan para fases posteriores:

- Vista de producción por máquina.
- Vista de embarques.
- Vista de repartidores.
- Estadísticas avanzadas para clientes.
- WhatsApp automático completo.
