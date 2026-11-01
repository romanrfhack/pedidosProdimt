# I10 — Productos compartidos y precios por cliente con historial

Autorizado el 1 de noviembre de 2026. Implementación y validación en curso; no desplegado. Base: rama I09 `codex/i09-customer-order-links`, código `c0566c1f48d3ac87c6f66956beff978cb8217a63`; se conserva acceso de clientes y asignación de fecha. Nueva rama `codex/i10-shared-products-prices`.

## Catálogo compartido

Un producto no pertenece a un cliente. Se mantiene Products y la relación existente CustomerFrequentProducts para cantidades sugeridas, orden visual, posición explícita, abreviatura, máquina y activo. Precio no se incorpora al nombre del producto ni a su unidad.

La pantalla Productos propone grupos cuyos nombres difieren sólo en el sufijo de un cliente conocido y tienen igual unidad. Son candidatos para revisión, no equivalencias automáticas. También permite selección manual, elegir registro destino y nombre/descripción compartidos, revisar nombres/descripciones originales, impacto y confirmar mismo producto/unidad con motivo. No se equiparan preparaciones diferentes o unidades por parecido.

La consolidación guarda una huella de productos/perfiles/precios, actualiza relaciones en una transacción, conserva máquinas/posiciones/cantidades, mueve la cabecera de precio sin cambiar sus revisiones y archiva duplicados con MergedIntoProductId. Colisiones de posiciones/productos para un mismo cliente o de dos historiales propios se bloquean sin sumar cantidades ni elegir un precio. Perfiles cuyo significado cambia incrementan versión y requieren aprobación; los ya canónicos sin cambio conservan versión/aprobación. No se tocan partidas históricas ni se crea demanda.

Los códigos externos antiguos siguen siendo alias en importación diaria y configuración por CSV. Un CSV de catálogo que intente sobrescribir/reactivar un producto ya consolidado devuelve un error explícito. La respuesta perdida se recupera con la misma operación durable; evidencias previas y auditoría se conservan.

## Precios e historial

Moneda inicial MXN por unidad confirmada del producto. En cada fila de configuración del cliente aparece precio vigente y acceso al editor/historial. Guardar precio no guarda ni descarta el borrador de productos. El editor registra precio, vigente desde y motivo; identidad/instante proceden del servidor autenticado. Un cambio futuro se muestra programado. No se cargan precios iniciales inventados.

| Tabla | Responsabilidad |
|---|---|
| CustomerProductPriceBooks | Relación única cliente-producto y versión optimista; identidad estable aunque el producto se consolide. |
| CustomerProductPriceRevisions | Revisiones anexadas: importe nullable, moneda, unidad/nombre observados, vigencia, instante de registro, actor/nombre, motivo, operación y hash. No se actualizan ni borran revisiones. |
| ProductConsolidationOperations | Recibo durable, hash, resultado y evidencia previa para reintento y trazabilidad de consolidación. |
| OrderLines (columnas nuevas) | Precio unitario aplicado, moneda, revisión y fecha de vigencia como snapshot nullable de la captura. |

La revisión aplicable es la última vigencia no posterior a la fecha de servicio y, en la misma fecha, la revisión de versión mayor. La siguiente vigencia determina el fin mostrado; una revisión sustituida en la misma fecha permanece identificada en historial. Hay índice por cabecera/vigencia/versión para obtener una cotización sin cargar todo el historial. Registro UTC; interfaz muestra Ciudad de México. Vigencia puede ser histórica o futura, pero nunca revaloriza automáticamente pedidos anteriores.

Sin precio y retiro explícito de precio son null; cero explícito significa gratuito. No existe fallback al precio de otro cliente. Cambiar de unidad deja sin precio aplicable hasta registrar uno para esa unidad; no se convierte una tarifa de kg a pieza/paquete.

Cada cambio requiere versión esperada y operación persistente. Repetir la misma referencia/contenido no duplica revisiones; otra carga con esa referencia o versión desactualizada falla. Guardar iguales precio, vigencia y motivo conserva el registro existente. El editor conserva el intento ante respuesta incierta y consulta historial/reintenta la misma referencia, incluso después de recargar.

## Partidas y límites financieros

Captura administrativa, portal cliente, importación diaria y productos nuevos en correcciones usan el precio vigente para su fecha de servicio. Los pedidos existentes retienen precio/revisión/unidad originales al cambiar tarifas, renombrar/consolidar productos o corregir cantidades de esa partida. Corregir un ID canónico equivalente conserva la identidad/importe histórico original; no duplica el mismo producto por alias. Partidas nuevas se cotizan para la fecha del pedido. Históricos sin precio siguen pendientes, sin rellenarse como cero.

Importe de partida = cantidad × precio, redondeado a dos decimales AwayFromZero; total de pedido es null si alguna partida está sin precio. Se muestran importes aplicados en detalle administrativo. No se crean cargos de cuenta, pagos, saldos, impuestos o facturas. La etapa de cobranza usará snapshots, no el precio actual del catálogo, y deberá definir cómo valorar históricos aún pendientes y qué entrega genera el cargo.

## Validación y entrega

Build Angular y compilación backend correctos. 105 E2E correctos, incluyendo once nuevos de precios/consolidación y medidas previas sin scroll en laptop. Primer bloque dirigido de 15 pruebas de integración correcto, suite ampliada de .NET en cierre; SQL Server real se validará en CI. Datos sólo ficticios. Migración aditiva, respaldo y paquete nativo I10 pendientes de cierre. No se ha operado el VPS ni consolidado datos reales desde este entorno.
