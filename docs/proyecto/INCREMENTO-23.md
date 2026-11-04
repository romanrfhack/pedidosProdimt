> Contrato del incremento y evidencia de su fecha. La instalación/aceptación vigente se consulta en [ESTADO](ESTADO.md); los pendientes antiguos de este documento no sustituyen esa evidencia.

# I23 — Horarios y solicitudes pendientes sobre Reparto I22

Decisiones de negocio del 2 de noviembre e integración autorizada el 3 de noviembre de 2026. Integra la rama de horarios con Reparto I22 y sus correcciones. Base de despliegue: backend `delivery-0cfe3ef511d8`; frontend I22 del mismo nombre o RF1 `delivery-flow-426c360e1365`, 17 migraciones exactas. Preparado sin acceso SSH directo al VPS.

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

Migración `20261103030000_AddOrderingSchedules`, posterior a AddDeliveryOperations. Agrega `Customers.AutomaticConfirmationCutoff`, `Customers.OrderingScheduleVersion`, `RouteOrderSchedules` y dos CHECK de hora < 18:00. No modifica valores previos ni revoca tokens. La excepción se guarda por un endpoint específico, para que los editores/CSV antiguos no la borren.

Los cambios de horario se auditan. La captura audita hora efectiva, origen, fecha y condición de solicitud limitada. Reutiliza transacción, bloqueo de perfil, precios históricos e idempotencia existentes. El contrato público recibe campos adicionales, sin eliminar los anteriores.

Los endpoints legados `/submit` de cliente también respetan horario efectivo y bloquean fecha actual después de las 18:00 o martes. La captura interna con fecha explícita y las importaciones conservan su lógica administrativa previa.

## Paquete nativo

Acepta sólo backend `delivery-0cfe3ef511d8` con frontend I22 del mismo nombre o RF1 `delivery-flow-426c360e1365`, con las 17 identidades exactas. La revisión2 incluye las fuentes RF1 y fija permisos explícitos de fixtures, conservando umask077 y los controles productivos. El resultado contiene 18. Preserva el esquema, los permisos y las huellas completas de datos de reparto y seguridad. No basta cambiar el nombre admitido en el instalador anterior: los ensamblados y la web se integraron.

Lectura por defecto. `--apply` solicita credenciales SQL existentes en entrada oculta, ejecuta tres pruebas EF/HTTP y el SQL exacto en bases temporales con limpieza verificada antes de cambios operativos. Crea snapshot privado, detiene sólo PRODIMT y exige `BACKUP COPY_ONLY,CHECKSUM` con `RESTORE VERIFYONLY` antes de migrar. DDL, permisos mínimos e historial comparten transacción. Se preservan claves, tokens, seguridad y precios. Un fallo después del DDL conserva marcador y servicio detenido; el mismo paquete reanuda hacia adelante. No restaura SQL automáticamente.

La aplicación recibe SELECT/INSERT/UPDATE y DENY DELETE en RouteOrderSchedules. El paquete usa código de soporte histórico fijado y hashes por archivo. No contiene credenciales, claves reales ni configuraciones de producción.

## Validación

Preparación en `docs/validation/2026-11-03-schedules-on-delivery.md` y corrección SQL en `docs/validation/2026-11-03-schedules-sql-correction.md`. El reporte posterior del usuario confirma instalación v3, migraciones17→18, salud/frontend y validaciones aisladas correctas: ver `docs/validation/2026-11-03-schedules-installed.md`. La aceptación visual y operativa sigue pendiente; no afirmar envío de mensajes ni pruebas de navegador completadas sin evidencia correspondiente.
