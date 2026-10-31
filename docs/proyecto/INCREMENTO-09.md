# I09 — Enlace del próximo pedido y recordatorio individual

Autorizado el 31 de octubre de 2026 después de diseñar el flujo con el usuario. La autorización incluye implementación; no implica que ya esté desplegado ni que se haya enviado un mensaje. Consultar `ESTADO.md` y la evidencia de validación para conocer el resultado efectivamente comprobado.

## Acceso y configuración

Se reutiliza Catálogos → Clientes → Configuración → Tokens de acceso y la autenticación existente del portal `/cliente`. Los nuevos recordatorios precargan «Recuerda hacer tu próximo pedido» como descripción editable. El mensaje completo añade nombre del cliente, fecha de entrega prevista, enlace y vencimiento con hora de Ciudad de México. Las descripciones antiguas no se reemplazan.

El teléfono pertenece al catálogo del cliente. Si falta o no tiene formato válido, se solicita y guarda antes de crear el enlace de recordatorio. Un fallo conserva el borrador y distingue guardar teléfono de crear token para que el reintento no simule una operación completada. No se duplica el teléfono en el token ni se usa como prueba de identidad.

Los enlaces nuevos de recordatorio exigen vencimiento futuro. El token plano sólo se devuelve al crearlo y se conserva únicamente su hash en base. Copiar enlace/mensaje y abrir WhatsApp están disponibles con ese valor recién creado; el listado histórico mantiene metadatos y revocación, sin recuperar el secreto desde su hash. Si se pierde el enlace debe crearse otro. Los tokens existentes y la entrada manual siguen disponibles.

Al abrir un enlace válido, el portal autentica al cliente y retira el secreto de la URL. No crea pedido, no marca NoOrder y no consume el acceso por una vista previa. Un enlace es una credencial: reenviarlo permite utilizarlo mientras siga activo. Se limita a su cliente; no otorga permisos administrativos. La sesión debe estar vinculada a su token de origen y no superar su vencimiento; las solicitudes protegidas comprueban además revocación, vigencia y cliente activo. Las sesiones cliente anteriores que no identifiquen el token deberán autenticarse de nuevo; no se revocan por ello sus tokens de acceso.

## Fecha de entrega del próximo pedido

El servidor calcula la fecha en la zona operativa `America/Mexico_City`, independientemente del reloj del navegador o de UTC del host:

| Momento local | Fecha de entrega |
|---|---|
| Desde 00:00 hasta antes de 18:00 | Mismo día |
| Desde 18:00 inclusive hasta antes de 00:00 | Día siguiente |
| Viernes 23:00 | Sábado |
| Sábado 01:00 | Sábado |

La fecha se muestra de forma destacada al abrir y antes de confirmar. El texto de WhatsApp indica fecha prevista si se confirma en ese momento y advierte la regla de las 18:00; el portal presenta la fecha efectiva. No se congela una fecha dentro del enlace ni se deduce del texto de la descripción o de la expiración.

El guardado recibe la fecha observada y la vuelve a comparar con la calculada al recibir la confirmación en el servidor. Ese instante se fija una vez y se usa tanto para fecha de servicio como para `SubmittedAt` y evaluación de tardío: una espera de base de datos no mueve sólo una parte del pedido al otro lado del corte. Si la confirmación ya llegó después del corte y la fecha observada cambió, no modifica pedidos: conserva texto, cantidades y notas, informa la nueva entrega y requiere una nueva confirmación. Un formulario abierto a las 17:55 no se traslada silenciosamente al día siguiente al confirmar a las 18:05. La expiración del acceso se valida por separado.

`OrderDate` ya representa fecha de servicio en los flujos de octubre; `SubmittedAt` conserva la hora real de recepción. La regla automática se utiliza en el nuevo flujo del cliente y como fecha predeterminada de captura administrativa. No sustituye una fecha explícita elegida por administración, no cambia importaciones, no reinterpreta pedidos históricos ni equivale a entrega física. Tampoco elimina el corte de las 10:00 y revisión administrativa de pedidos tardíos: asignación de fecha y evaluación de tardío son reglas diferentes.

## Captura del cliente

Se reutiliza la interpretación determinista I07 mediante servicios y validaciones comunes, sin invocar endpoints administrativos desde el portal:

- **Rápida:** cantidades por posiciones explícitas y sufijos configurados, vista previa con desglose y confirmación separada. Guardar interpretación exige perfil aprobado y vigente.
- **Manual:** cantidades de los productos configurados con unidad confirmada, sin exigir aprobación para interpretar posiciones.
- Cambiar modalidad conserva el mismo borrador y notas de entrega. No se genera demanda por sugerencias, vista previa o apertura del enlace.
- Sólo productos/cantidades/unidades y datos públicos de entrega. No se exponen máquinas, llaves, instrucciones interiores, notas internas, auditoría interna ni configuración administrativa.
- Si existe pedido vigente para la fecha calculada, se muestra y se impide crear un duplicado o sobrescribirlo silenciosamente. Un cambio comercial conserva el flujo de corrección/revisión administrativa existente; no se salta a otra fecha automáticamente.
- La referencia del intento, contenido, pedido, auditoría y recibo se guardan de forma transaccional. Una respuesta perdida se reintenta con la misma referencia y contenido. Una clave reutilizada con contenido diferente produce conflicto. No se presenta InMemory como persistencia durable.

Los contratos nuevos son aditivos y requieren `CustomerAccess`, comprobando identidad del cliente de la ruta contra su JWT. Las respuestas tienen DTOs públicos específicos. Conservar los contratos administrativos e importaciones existentes; registrar los contratos exactos finales en la evidencia después de implementarlos.

## WhatsApp individual y fase futura

El botón **Abrir WhatsApp** prepara la conversación del número guardado y el texto. La persona operadora confirma el envío en WhatsApp. No registrar «enviado» ni «entregado» por abrir la conversación; no enviar mensajes desde el agente, no automatizar el navegador ni usar una API de mensajería.

No se implementan envío masivo, programación global/por vendedor, tareas en segundo plano, plantillas oficiales o webhooks. Para una fase futura, la selección comprobará pedido vigente, NoOrder y descanso para la fecha de entrega calculada. Después de las 18:00 esa fecha será mañana; revisar sólo «ya pidió hoy» podría seleccionar u omitir al cliente incorrectamente. No saltar descansos ni fabricar otra fecha sin una regla posterior autorizada.

## Aceptación técnica

- Enlace válido abre al cliente correcto y la entrada manual continúa funcionando.
- Teléfono faltante/incorrecto impide crear el recordatorio hasta guardar uno válido; se conserva el resto del borrador.
- Vigencia y revocación bloquean también solicitudes desde una sesión que ya estaba abierta.
- Fecha correcta en madrugada, 17:59, 18:00 y 23:00; cálculo independiente de la zona del host.
- Abrir antes del corte y guardar después produce conflicto sin escritura y permite revisar/reconfirmar sin perder cantidades.
- Captura rápida y manual equivalentes en cantidades/unidades; perfil obsoleto o unidad faltante bloquean sin inferencias.
- Pedido existente, doble clic, respuesta perdida y reintento no duplican demanda ni auditoría.
- Un cliente no opera otro cliente ni recibe propiedades internas en éxito, vista previa, conflicto o error.
- Mensaje incluye enlace y vencimiento reales; abrir WhatsApp no simula envío.
- Interfaz usable en teléfono, tablet y escritorio sin desbordamiento horizontal.

## Entrega nativa

Preparar actualización de backend/frontend para el I07 nativo y frontend I07/I08 compatibles, con integridad fijada, preflight, conservación de configuración/cuentas/catálogos y canal reparado, snapshot privado, verificación de salud y reversión de aplicación ante fallo. No ejecutar bootstrap, reimportación o restauración SQL automática. No se prevén cambios de esquema para este incremento; el constructor y el instalador deberán comprobar el historial vigente y rechazar un destino incompatible. Registrar sólo comprobaciones realmente ejecutadas; paquete y CI correctos no acreditan despliegue ni aceptación del cliente real.
