# I22 — Horarios y solicitudes pendientes de confirmación

Fecha de decisiones: 2 de noviembre de 2026. Base recuperada: I21 `496eb7094c05e3269f941ca6f87af6a5a88e4c98`, que incorpora I19, I18 e I16. I22 preparado como continuación de esa fuente, sin despliegue remoto.

## Reglas confirmadas

- Hora límite de confirmación automática: cliente > ruta > 10:00 por defecto. Puede configurarse antes o después de la de su ruta, siempre antes de las 18:00. Borrar el valor particular devuelve la herencia; borrar el de ruta devuelve el valor general.
- A partir de la hora límite, inclusive, las capturas del cliente para ese mismo día pasan a `PendingAdminReview` / Pendiente de confirmación.
- Recepción para el mismo día hasta antes de las 18:00, hora de Ciudad de México. Desde las 18:00 se ofrece el siguiente día hábil y el cliente debe aceptar esa fecha antes de capturar. Martes es el único cierre semanal global confirmado. Lunes desde las 18:00 y cualquier hora de martes ofrecen miércoles.
- La fecha observada se valida otra vez al guardar: si cruzó el corte, se devuelve 409 `DeliveryDateChanged` y se conserva el borrador para aceptar la nueva fecha. El servidor evalúa el horario al recibir, antes de esperas en base.
- Se conserva la exclusión existente de `PendingAdminReview` de producción/asignaciones. La aceptación mueve a `Accepted` y habilita el flujo operativo una sola vez. Rechazar no crea otra fecha.

## Acceso con enlace vencido

El token almacenado conserva `CustomerId` y `ExpiresAt`. El vencimiento no elimina esa asociación. Se incorpora una ventana limitada para registrar solicitudes: hasta antes de las 18:00 del siguiente día hábil posterior a la fecha de vencimiento.

El enlace debe existir, estar activo y pertenecer a un cliente activo. Revocar bloquea inmediatamente, también sesiones abiertas. El JWT de sesión tiene duración máxima de configuración y queda acotado a la ventana; en cada petición se consulta de nuevo el origen. Después del vencimiento sólo se autorizan GET `/next`, GET `/additional-products`, POST `/preview` y POST `/capture` del cliente autorizado. Se bloquean endpoints legados, NoOrder y cualquier ruta administrativa. Estas capturas siempre quedan pendientes, incluso para la siguiente fecha. Los tokens antiguos fuera de esta ventana o revocados no recuperan acceso.

## Interfaz y operación

- `/admin/horarios`, permiso `customers.manage`: edición por ruta y excepciones por cliente, búsqueda, herencia visible y versiones para impedir guardados obsoletos.
- `/admin/pendientes`, permiso `orders.review`: Pedidos por confirmar. Muestra ruta, hora de recepción, fecha, motivo y detalle/ajustes. Reutiliza decisiones existentes y auditoría.
- Una decisión guardada permite preparar un mensaje con `/api/admin/orders/{id}/notification`, protegido por `orders.review`. Incluye teléfono normalizado sólo para el equipo autorizado. El botón abre WhatsApp para revisión manual; no envía mensajes ni acredita su envío. La aceptación con cambios informa las cantidades confirmadas. El rechazo ofrece revisar el siguiente día hábil; requiere respuesta y captura posterior, sin crear pedidos automáticamente.
- Cliente: pantalla amable para enlace no disponible, aviso de solicitud pendiente y aceptación explícita de siguiente fecha. Los errores de conexión conservan un mensaje de reintento.

## Persistencia y compatibilidad

Migración `20261103030000_AddOrderingSchedules`, ordenada después de I19. Agrega `Customers.AutomaticConfirmationCutoff`, `Customers.OrderingScheduleVersion`, `RouteOrderSchedules` y dos CHECK de hora < 18:00. No modifica valores previos ni revoca tokens. La excepción se guarda por un endpoint específico, para que los editores/CSV antiguos no la borren.

Los cambios de horario se auditan. La captura audita hora efectiva, origen, fecha y condición de solicitud limitada. Reutiliza transacción, bloqueo de perfil, precios históricos e idempotencia existentes. El contrato público recibe campos adicionales, sin eliminar los anteriores.

Los endpoints legados `/submit` de cliente también respetan horario efectivo y bloquean fecha actual después de las 18:00 o martes. La captura interna con fecha explícita y las importaciones conservan su lógica administrativa previa.

## Paquete nativo

Acepta sólo backend I19 `production-d3a11420b202` con frontend I19 o I21 `orders-catalog-867df56de3bb`. Verifica identidades exactas de las 16 migraciones anteriores. El resultado contiene 17.

Lectura por defecto. `--apply` solicita credenciales SQL existentes en entrada oculta, crea snapshot privado y `BACKUP COPY_ONLY,CHECKSUM` con `RESTORE VERIFYONLY`, luego detiene sólo PRODIMT para migrar. DDL, permisos mínimos e historial comparten transacción. Se preservan claves, tokens, seguridad y precios. Un fallo después del DDL conserva marcador y servicio detenido; el mismo paquete reanuda hacia adelante. No restaura SQL automáticamente.

La aplicación recibe SELECT/INSERT/UPDATE y DENY DELETE en RouteOrderSchedules. El paquete usa código de soporte histórico fijado y hashes por archivo. No contiene credenciales, claves reales ni configuraciones de producción.

## Validación

Ver `docs/validation/2026-11-02-ordering-schedules.md`. No afirmar despliegue, aceptación de negocio, envío de mensajes ni pruebas visuales completadas sin evidencia del entorno correspondiente.
