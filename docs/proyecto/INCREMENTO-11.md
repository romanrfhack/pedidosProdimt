# I11 — Productos compartidos y precios por cliente con historial

Autorizado el 1 de noviembre de 2026. Implementado, validado e instalado según reporte del usuario del 1 de noviembre; aceptación funcional pendiente. Rama `codex/i10-shared-products-prices`, PR #3 en borrador sobre I09. Se integra la base `9998e30ac8e912ae54d56274d8217229e5672944` con I09 instalado y modal de tokens I10. El primer checkpoint de productos/precios se llamó I10; este alcance queda renumerado como I11 para preservar la numeración y el trabajo concurrente del modal. No requiere aplicar el ZIP de modal I10 antes de I11.

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

## Instalación y protección de datos

Migración `20261101154618_AddSharedProductsAndCustomerPrices`: once→doce versiones, tres tablas y columnas nullable en Products/OrderLines. No modifica datos existentes ni inventa precios. El usuario SQL de aplicación puede consultar/agregar revisiones, pero recibe DENY UPDATE/DELETE sobre el historial y los recibos; las cabeceras permiten UPDATE para su versión y equivalencia de producto, con DELETE denegado.

El actualizador nativo admite backend I09 con frontend I09 o modal I10 `token-modal-89b51ddc3308`, y las bases I07/I08 conocidas. Comprueba paquete/archivos, proceso, configuración Production, canales y permisos antes de escribir. Solicita SQL admin oculto sólo con --apply; crea snapshot privado, prepara archivos de forma atómica y detiene únicamente la API de PRODIMT. Ejecuta COPY_ONLY/CHECKSUM y RESTORE VERIFYONLY antes del SQL incremental. Verifica esquema, conteos existentes, ausencia de precios/consolidaciones creados por instalación y permisos anexables. Conserva api.env y JSON productivos. Activa enlaces, comprueba endpoints protegidos y archivos HTTPS; si falla recupera los enlaces previos y salud, nunca restaura SQL automáticamente. Repetir el mismo paquete verifica already_current sin nuevas migraciones o reinicio.

## Validación y entrega

Código `148a52dd7428cd1f948441f6709407a22be5944b`; [CI 41329868536](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41329868536), cuatro trabajos correctos: 323 .NET, 46 SQL Server, 114 E2E, 269 Python y smoke Production. Dieciocho casos de aplicación y nueve SQL nuevos para historial/vigencia, precio desconocido/cero/retiro, snapshots/correcciones, consolidación/alias/perfiles, concurrencia entre hosts y rollback. Once E2E nuevos; se preservan 103 de la base con modal I10. Diecinueve pruebas Python nuevas del paquete y respaldo/recuperación; los reparadores históricos mantienen su guarda de once migraciones y sus fixtures propios. Esquema y permisos exactos del instalador ejecutados contra SQL Server, también bajo usuario db_datawriter con DENY efectivo. Modelo/migración coherentes, publicación linux-x64 y build Angular correctos. Sólo datos ficticios en pruebas. graphify update . no disponible (127), sin grafo previo.

ZIP corregido `PRODIMT_Productos_Precios_I11.zip`, 5,743,517 bytes, SHA256 `671c743b7da1b98a05dd032aa16e3e75aabd4f0de43a8b0e9c86c02c6f834c1d`. Payload SHA256 `8a43682026c7da6d3dc4a3cf0ef0e47475bf1bfe7d7883748005053e32f6d436`, 61 archivos y avisos de terceros. Descarga verificada/guardada; LEEME y SHA256SUMS incluidos. Nuevo backend/frontend `prices-148a52dd7428`. [Evidencia y pasos](../validation/2026-11-01-commercial-catalog.md).

No se ha operado el VPS ni consolidado productos o registrado tarifas reales. La aceptación de negocio, instalación, revisión de equivalencias y precios reales corresponden al siguiente paso con el usuario.


## Actualizador revisión 2 tras el preflight reportado

El intento de lectura del usuario se detuvo sin cambios por una omisión del instalador: faltaba registrar sus consultas en la lista exacta del inspector. Revisión 20261101-commercial-catalog-install2, fuente 152cfc95ab2d68cf8f32c9867b4305bb1a329628, corrige sólo el actualizador y conserva la política de lectura. Autoriza únicamente los tres SELECT requeridos y deniega consultas ajenas; informa paso/código fijo sin revelar diagnósticos o valores privados. Cuatro regresiones nuevas de composición real, incluidas las dos que reproducen el bloqueo anterior, elevan herramientas a 273 correctas. La aplicación y el payload continúan en 148a52d y SHA256 8a43682026c7da6d3dc4a3cf0ef0e47475bf1bfe7d7883748005053e32f6d436, respectivamente; no se recompilan, migran o cambian permisos por esta corrección.

Descarga independiente PRODIMT_Instalar_I11_v2.py, 32,663 bytes, SHA256 b1185a8ebb01fc139410c55f4d2d8e8a23bd709d24991d76c5fb9a7cffd659e0. Tiene los mismos bytes que el instalador del ZIP actualizado. Basta copiar el nuevo script y comprobar lectura antes de --apply, reutilizando el payload existente en el VPS. ZIP anterior 0f97c7bb... queda como antecedente; usar el instalador revisión 2. Instalación y aceptación operativa siguen pendientes del usuario. Ver evidencia del incidente.

Validación de la corrección: [CI final 41340426602](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41340426602) con cuatro trabajos correctos: 273 Python, 323 .NET, 46 SQL Server, 114 E2E y smoke Production. La sincronización de una prueba espera el valor inicial antes de reemplazarlo; no altera frontend o binarios. Entregas verificadas y guardadas; instalación remota pendiente.


## Despliegue confirmado

El usuario reportó instalador revisión 2 updated/completed, doce migraciones, esquema/conteos/permisos/salud/frontend verificados y configuración conservada. Backend/frontend prices-148a52dd7428, fuente de aplicación148a52dd7428cd1f948441f6709407a22be5944b. Respaldo SQL verificado y snapshot registrados en la evidencia. Contraseñas sin cambio, PRODIMT reiniciado, sin restauración automática ni consolidación/tarifas precargadas. No se accedió al VPS desde este entorno. La instalación está confirmada; revisión de equivalencias, precios reales y aceptación operativa siguen pendientes del usuario.
