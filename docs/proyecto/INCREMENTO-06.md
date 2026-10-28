# I06 — Catálogo de clientes y estado del día

Autorizado el 28 de octubre de 2026. El usuario confirmó la carga inicial de clientes y pidió que la búsqueda sea el acceso principal, el alta se abra en un modal y las tarjetas reflejen la situación del día.

## Comportamiento

- Catálogos → Clientes abre búsqueda, filtros, botón Nuevo cliente y tarjetas. El formulario no se monta ni descarga hasta abrir Nuevo cliente o Editar; modal nativo centrado con foco, Escape/cancelación, restauración de foco y adaptación móvil.
- Ámbar: por confirmar. Verde: existe un pedido Submitted, Accepted o PendingAdminReview del día; los pendientes conservan su etiqueta de revisión. Gris: última confirmación efectiva NoOrder o descanso semanal programado sin pedido. Sin conexión y sin consulta anterior: estado desconocido, sin inventar pendientes o confirmaciones.
- Orden principal: por confirmar, con pedido, sin pedido/descanso e inactivos al final; nombre dentro del grupo. Búsqueda por nombre, teléfono, ruta o código, con filtros de estado.
- El día proviene de IDateTimeProvider.Today (Ciudad de México), no del reloj del navegador ni de la fecha UTC. Se consulta al abrir y cada 30 segundos, al volver a la pestaña y mediante actualización manual, sin modificar el formulario/perfil abierto. Un fallo conserva el resultado previo con fecha y advertencia visible.
- Un pedido vigente prevalece sobre NoOrder anterior y descanso semanal. Rejected, Cancelled, Draft y Superseded no se consideran un pedido vigente. Un NoOrder antiguo anterior a un rechazo posterior no se presenta como confirmación actual.

## Descansos y persistencia

- Selección semanal por cliente, ISO lunes=1 a domingo=7. RestDaysMask entero 0–127 en Customers, valor inicial 0: sin descanso configurado; no se deducen días de los datos históricos.
- RestDays opcional en el contrato administrativo: omitirlo conserva la programación; [] la limpia. Validar valores/repeticiones antes de modificar la entidad. Se registra en la auditoría de creación/edición, con programación anterior y nueva.
- Migración aditiva 20261028142346_AddCustomerRestDays. Conserva clientes, pedidos, credenciales y configuración; no desactiva clientes ni crea registros NoOrder automáticamente.
- El CSV de clientes existente conserva sus 13 columnas y no borra descansos configurados. No se infieren pedidos ni aprobación de perfiles.
- GET protegido /api/admin/customers/day-status devuelve fecha y proyección por cliente: estado, cantidad de pedidos vigentes y necesidad de revisión. Solo lectura; no modifica demanda ni registra producción.

## Aceptación y despliegue

Verificar apertura/cancelación/edición/errores y doble envío del modal; búsqueda/filtros/orden/colores, falta de conexión y refresco sin pérdida de edición. Backend: fecha operativa, ausencia de efectos de lectura, precedencia de pedidos, autorización, validación y persistencia/compatibilidad CSV en SQL Server real.

Preparar actualización nativa desde I05 activo password-886bee7b2202: paquete fijado por hashes, configuración conservada, respaldo SQL verificado antes de migrar, historial nueve→diez y recuperación de releases. Todavía no instalado en el VPS; las pruebas técnicas y las capturas ficticias no equivalen a despliegue ni revisión de los clientes reales.

La actualización se prepara con scripts/pilot/build-catalog-package.py e install-catalog-update.py. El paquete sólo acepta migración incremental I04→I06, sin bootstrap; comprueba nueve o diez migraciones exactas y restricción RestDaysMask confiable. Respaldo SQL y snapshot privado anteriores a la activación; no modifica contraseñas ni Nginx. Si el SQL queda completo pero falla la activación, permite reintento desde I05 y esquema diez tras nuevo respaldo.
