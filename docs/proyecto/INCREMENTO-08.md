# I08 — Configuración del cliente y calendario semanal

Autorizado el 31 de octubre de 2026. Implementado, validado en CI y desplegado por el usuario; revisión funcional en navegador pendiente. Código be3995fdce6948ff00f44edd6025a1da1899887c; [CI de cierre](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41217210669) con cuatro jobs correctos: 79 E2E, 206 Python, 267 .NET, 31 SQL Server y smoke Production.

## Experiencia

La ruta `/admin/clientes/{id}/configuracion` aprovecha el ancho de pantalla con márgenes. En escritorio presenta productos compactos a la izquierda, calendario/máquinas a la derecha, cabecera resumida y acceso por tokens desplegable. Datos y entrega conservan su modal. Se mantienen producto, unidad, cantidad sugerida, orden visual, posición, abreviatura, máquina, activo y aprobación explícita del perfil.

La vista habitual con tres productos se verifica sin desplazamiento de página en 1366x768. En grandes monitores mantiene márgenes y controles legibles. En tablet/teléfono se apila y permite desplazamiento vertical, sin desbordamiento horizontal. Con más productos o contenido extraordinario se conserva acceso mediante desplazamiento propio de listas; no se corta la página ni se ocultan errores. No es una promesa de alojar cantidades ilimitadas de filas en cualquier altura.

## Días de trabajo

Siete casillas, lunes a domingo. Marcada significa que trabaja/se espera pedido; sin marcar significa descanso. Se reutiliza `restDays` de I06 (lunes=1, domingo=7); días de trabajo son su complemento. `[]` conserva la expectativa diaria existente. No se infieren descansos de pedidos pasados ni del Excel.

El mismo control se usa en el modal de cliente y en la configuración enfocada. Guardar días es independiente del guardado de productos/máquinas. Preserva el borrador y la versión del perfil. Consulta datos actuales antes de usar el PUT heredado y pide recargar si detecta otros descansos guardados; no añade concurrencia atómica a datos generales. Un pedido vigente prevalece sobre descanso, que no crea NoOrder ni cancela o desactiva al cliente.

Pendientes combina la respuesta existente de clientes sin pedido con sus calendarios guardados. Respeta el día de semana de la fecha de servicio elegida, independientemente de la zona del navegador; no usa el estado de hoy para una fecha pasada o futura. Oculta descansos por defecto y permite mostrarlos al final para capturar una excepción, con etiqueta explícita. El filtro queda bloqueado mientras hay una captura abierta, para conservar su intento. Si falla la consulta del calendario se informa el error y se permite consultar otra vez. No se modifica el contrato del endpoint de pendientes ni se generan pedidos/NoOrder automáticamente.

## Entrega

No modifica backend, migraciones, modelos o contratos de API. Paquete `PRODIMT_Configuracion_Cliente_I08.zip`, construido fuera del VPS, contiene frontend y un instalador con hash fijado. Target inicial: backend/frontend nativos I07 `capture-46fed604732e`; sólo cambia el enlace del frontend a `client-config-{source12}`. Conserva la reparación del canal y once migraciones.

El instalador usa sólo Python estándar/curl/systemctl de lectura, sin credenciales SQL o reinicio. Verifica proceso/backend, permisos de directorios, frontend previo y API; crea snapshot root-only, extrae archivos verificados, activa y comprueba index/JS/CSS por HTTPS. Una falla después de activar revierte el enlace; interrupciones de terminal también intentan restaurarlo. No restaura ni consulta SQL directamente. Repetir sobre el mismo frontend verifica sin nueva activación.

Antes de recargar, resolver o guardar cualquier captura/borrador abierto; el guardado real de I07 continúa pendiente de confirmación del usuario. Instalación no equivale a aceptación del calendario en producción. Evidencia: [validación I08](../validation/2026-10-31-client-configuration.md).

## Despliegue confirmado — 31 de octubre de 2026

El usuario verificó SHA exterior e internos y ejecutó el instalador revisión 20261031-client-configuration-install1. JSON: updated/completed, changes_made=true, health_verified=true y frontend_verified=true. Fuente be3995fdce6948ff00f44edd6025a1da1899887c; frontend activo /var/www/prodimt-pedidos/frontend/releases/client-config-be3995fdce69. Backend, contraseñas y servicio sin cambios; no se ejecutaron migraciones. Snapshot privado: /var/backups/prodimt-pedidos/client-configuration-20261031T162118Z-cfccc3ef23b6.

Evidencia reportada por el usuario; no se operó SSH desde este entorno. Falta revisar distribución responsive, persistencia de días reales de trabajo y efecto de descanso en Pendientes para la fecha seleccionada. No repetir la instalación para esa revisión ni confundir salud técnica con aceptación funcional o guardado real del pedido I07.
