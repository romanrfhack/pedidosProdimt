# Cobranza de reparto y recepción en Mostrador

## Alcance del 22 de noviembre de 2026

El usuario solicita registrar efectivo al entregar pedidos únicamente a clientes con condición `cash_on_delivery` («Diario contraentrega · requiere ticket»). Confirma que el ticket muestra el **pedido completo del día**, aunque la entrega física sea parcial, más el adeudo registrado de días anteriores. El importe sugerido se puede editar, incluido cero. Un abono cubre primero el ticket del día de servicio, después los adeudos anteriores y conserva cualquier excedente como saldo a favor.

Ejemplo: ticket del día $500 y adeudo anterior $300. Cobrar $400 deja $100 del día y $300 anteriores. Cobrar $600 liquida el día y deja $200 anteriores. Cobrar $800 liquida ambos. Cobrar $900 deja $100 a favor.

## Registros y consistencia

- Los cargos conservan cliente, pedido, artículo, unidad, cantidad solicitada, cantidad facturable, fecha de servicio y precio histórico. Cambiar una tarifa del catálogo no modifica estos importes.
- La primera entrega activa el seguimiento del cliente para ese día e incorpora todas sus partidas vigentes del día. Las correcciones posteriores de esos pedidos producen movimientos compensatorios. Corregir únicamente kilos entregados no cambia el pedido ni su precio.
- Un precio pendiente permanece desconocido. Se permite registrar el efectivo real recibido; el ticket y el saldo completo siguen pendientes de cálculo hasta resolver el precio. La resolución conserva el movimiento original y añade su valoración.
- Los pagos conservan el importe efectivamente recibido, la vuelta, ruta, chofer asignado y usuario que capturó el movimiento. Entrega, cargo, pago y recibo de reintento se confirman en una misma transacción.
- Los pagos guardan la política de aplicación y su distribución al registrarse. La consulta del saldo actual aplica esa política a los cargos vigentes, incluidas correcciones posteriores; el registro original conserva lo que se conocía al cobrar.
- Los movimientos de efectivo y su recepción conservan su historia. Los comandos usan referencias durables, validación de versión y autorización por alcance.

## Caja

Mostrador consulta las vueltas que salieron y los importes efectivamente cobrados. Registra lo que cuenta, con receptor, fecha/hora y chofer de la vuelta. Una diferencia requiere motivo y pertenece a la liquidación del chofer, sin alterar el abono del cliente.

Ejemplo: venta $100, cobro $80, recepción $75. El cliente debe $20 y la liquidación conserva un faltante de $5. El subtotal recibido por el usuario en la fecha de recepción es la cobranza de rutas que deberá incluir en su entrega de caja. No se presupone que existan otros ingresos ni un cierre de turno anterior a este módulo.

## Historial y activación

No se infiere si entregas anteriores a este módulo fueron pagadas. Los saldos mostrados corresponden a cargos y pagos registrados en este seguimiento. Los saldos iniciales anteriores necesitarán una carga explícita y validada, si se decide incorporarlos. Una lectura no crea cargos ni pagos.

Los permisos de cobranza son independientes de captura de pedidos y gestión de reparto. El permiso de recepción se asigna al rol solicitado de Mostrador conservando sus usuarios y permisos previos. Una coincidencia de nombre ambigua necesita selección explícita antes de ampliar acceso.

## Evidencia y despliegue

Trabajo en la rama local `codex/cobranza-rutas`, sobre `0d64f5bd2aef`. La instalación anterior confirmada sigue siendo `machine-workspace-0d64f5bd2aef`, con 21 migraciones. Este documento no acredita el despliegue de cobranza.

La migración aditiva 22 es `20261122201338_AddDeliveryCollections`. Crea cinco tablas y permisos específicos; conserva las cuentas existentes. La compilación .NET pasa sin advertencias. Pasan 547 pruebas de dominio/aplicación (SQLite y fixtures InMemory) y 108 pruebas con SQL Server 2022 Developer real, incluidas actualización 21→22, conservación del rol personalizado, rechazo de colisión de identidad, separación de permisos, entrega parcial con ticket completo, rollback, respuesta perdida y recepciones concurrentes. EF no detecta diferencias pendientes entre modelo y migración. La suite Python conserva 632 casos, con cuatro omisiones históricas y ningún fallo. Pasan 68 componentes, nueve casos de navegador de cobranza, 241 generales y ocho de Máquina, con API simulada y Chromium 143. [Evidencia y límites](../validation/2026-11-22-cobranza.md).

Quedan para un incremento posterior los reversos de cobros capturados por error, la corrección de recibos, la resolución posterior de faltantes o sobrantes y el cierre completo de caja. En este alcance los pagos y recibos permanecen inmutables; registrar una diferencia documenta su importe y motivo, sin acreditar que haya sido resuelta.
