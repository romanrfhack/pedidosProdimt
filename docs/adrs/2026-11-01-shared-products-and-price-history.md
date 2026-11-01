# Producto compartido, historial de precios y precio aplicado

Decisión del 1 de noviembre de 2026 para I10.

Products representa el producto físico vendible. La configuración del cliente continúa en CustomerFrequentProducts. La tarifa corresponde a cliente-producto; no se duplica el producto para variar el precio.

Se separa una cabecera de precio con Id estable y relación única de sus revisiones anexadas. Esta separación permite consolidar el vínculo de producto sin modificar revisiones anteriores. Fecha de vigencia y fecha de registro son distintas: una tarifa puede programarse o corregir la configuración de una fecha pasada sin reescribir lo que fue aplicado a pedidos anteriores. No se utilizan tablas temporales de SQL Server como único historial, porque el motivo, operación idempotente y vigencia de negocio forman parte del contrato de aplicación.

El precio aplicado vive en OrderLine junto con revisión/moneda/vigencia. El importe se calcula en decimal en el servidor, con redondeo por partida; un precio desconocido produce importe/total desconocido. Cantidad corregida conserva precio original, y nuevas partidas usan tarifa para la fecha de servicio. No se usa una tarifa vigente hoy para recalcular un estado de cuenta histórico.

La consolidación es explícita, con vista previa y huella; archiva identidades antiguas y conserva sus códigos como alias. Rechaza unidades distintas, configuraciones duplicadas de un cliente e historiales de precio en conflicto. No se borran pedidos ni revisiones. La transacción comparte cambios de perfiles, productos, precios, recibo y auditoría. Un precio no es demanda, ingreso cobrado o saldo de cuenta.

Alternativa descartada: una columna Price en Product sería global y perdería tarifas por cliente. Una columna mutable en CustomerFrequentProduct perdería historia al reemplazar el perfil. Referenciar sólo la tarifa actual desde pedidos alteraría importes al cambiar el catálogo. La cabecera/revisiones y snapshots añaden tablas, pero mantienen los contratos de captura y la trazabilidad requerida para cobranza futura.
