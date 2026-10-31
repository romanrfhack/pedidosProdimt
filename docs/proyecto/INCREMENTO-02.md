# I02 — Pedidos diarios y correcciones auditables

## Problema

Capturar el mismo archivo dos veces puede duplicar demanda. Una corrección de cantidades tampoco debe tratarse como un pedido adicional ni borrar el contenido anterior sin evidencia. El pedido necesita una fecha de servicio explícita, separada de la hora de captura.

## Alcance

- Fecha de servicio opcional en captura administrativa; si se omite se mantiene el comportamiento de hoy en CDMX. La captura del cliente sigue orientada a hoy.
- Importación diaria separada de la importación de catálogos. Recibe CSV depurado, muestra los pedidos propuestos y errores, y aplica un lote válido de manera transaccional.
- Origen y referencia externa de cada pedido; clave de lote y hash para reconocer reintentos. Reutilizar una clave con otro contenido devuelve conflicto, no sobrescribe.
- Corrección administrativa con motivo, versión esperada y clave de operación; reemplazo explícito del conjunto completo de líneas, con evidencia antes/después.
- Pruebas de integración en SQL Server con datos ficticios y sin acceso a la base de un negocio.
- Historial SQL ampliado a `nvarchar(max)` para conservar snapshots completos con varias partidas y notas; las solicitudes mantienen sus límites de tamaño.

No incluye interpretación de WhatsApp, cargas directas del XLSM, producción física, entregas, pagos ni tickets monetarios.

## Entrada CSV

Una fila por partida; las partidas del mismo pedido comparten `externalOrderKey`, `serviceDate`, `customerExternalCode` y `action`.

```csv
externalOrderKey,serviceDate,customerExternalCode,action,productExternalCode,quantity,unitCode,notes
demo-001,2026-10-26,C-DEMO-001,order,P-DEMO-KG,10,kg,
demo-001,2026-10-26,C-DEMO-001,order,P-DEMO-DOZEN,1,dozen,
demo-002,2026-10-26,C-DEMO-002,no_order,,,,Confirmó que no pedirá
```

Los códigos de ejemplo son ficticios y deben existir en el catálogo para aplicar. No se resuelven clientes por parecido de nombre ni productos por la cantidad. `unitCode` debe coincidir con la unidad del catálogo. `X` no es cantidad ni cancela el pedido de todo un cliente.

La plantilla vacía está en `../import-templates/daily-orders.csv`. Mantener referencias externas estables cuando se vuelve a cargar el mismo pedido. Un cambio comercial requiere la operación de corrección; no inventar otra referencia para eludir un conflicto.

## Contratos

- `POST /api/admin/daily-orders/validate` y `POST /api/admin/daily-orders/apply`: `{ content, fileName?, source, batchKey }`.
- `POST /api/admin/orders/{id}/corrections`: `{ operationKey, expectedVersion, reason, lines }`.
- Captura administrativa: `serviceDate` opcional y `GET /api/admin/orders/today?date=YYYY-MM-DD` para consulta.

Todos estos endpoints exigen administración. La clave del pedido externo pertenece al origen. La clave del lote identifica una solicitud completa. Una respuesta perdida se reintenta con las mismas claves y el mismo contenido; cambiar contenido exige nueva validación, sin convertir una corrección en una nueva venta.

La demo InMemory permite consultar la vista previa, pero aplicar lotes y correcciones exige SQL Server. Ese modo responde con indisponibilidad explícita; no simula transacciones o reintentos persistentes.

## Datos privados

La revisión del usuario se conserva fuera de git. Se reconocen las referencias de fórmula mediante sus valores y sus celdas fuente. Un valor vacío sigue siendo vacío; “No visible” sigue siendo desconocido. Las fechas de revisión arrastradas en Excel no se utilizan como fechas de servicio ni como timestamps de autorización.

La confirmación del usuario permite preparar los valores corroborados, pero no crea equivalencias de unidades, posiciones de interpretación, orden de visita ni instrucciones interiores que no fueron proporcionadas. Cargar clientes o productos no aprueba automáticamente un perfil para interpretar cantidades abreviadas.

## Criterios de aceptación

- El lote inválido no guarda parcialmente pedidos, auditorías ni claves de operación.
- El mismo pedido externo no se duplica al repetir el lote o presentarlo dentro de otro lote.
- Una clave con contenido diferente produce conflicto visible.
- Corregir sustituye la demanda de ese pedido una sola vez y conserva evidencia del estado anterior.
- Una versión obsoleta no sobreescribe cambios de otro administrador.
- Las validaciones de unidad y estado se ejecutan otra vez al aplicar.
- Fecha de servicio, origen, actor y hora de registro quedan trazables.
- El cliente no recibe datos de acceso al local, máquinas ni metadatos internos de importación.
- Pruebas SQL Server ejecutadas y resultados identificados en el estado del proyecto.

La salida de la etapa 1 sigue requiriendo una semana de ensayo operativo, además de estas comprobaciones técnicas.
