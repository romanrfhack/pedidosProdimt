# I09 — Reparación puntual del canal Customer

## Evidencia recibida

El usuario ejecutó el diagnóstico de sólo lectura con hashes correctos el 1 de noviembre de 2026: `inspected/completed`, base correcta, once migraciones e I07, `admin_channel=1` y los trece permisos consultados en 1. La única diferencia es `customer_channel=0`. Esa consulta cuenta sólo Customer con IsInternal=0: no distingue ausencia de tipo de una fila incompatible, por lo que la reparación vuelve a consultar ambas cantidades. No se ha demostrado un problema de permisos ni de esquema.

## Alcance de la herramienta

`repair-native-customer-channel.py`, revisión `20261101-customer-channel-repair1`, fija el payload I09 original SHA256 `90c0e6505212f5165df56107a70d19085bb920ebdbf63c9bfc083fb7e0723d5c`, su manifiesto/código y bytes del inspector. Verifica servicio/proceso/configuración Production, backend I07 y frontend I07/I08 conocidos. Usa sólo la identidad SQL limitada del proceso, sin credenciales de administrador.

Preflight consulta historial, índice único habilitado, permisos SELECT/INSERT de SalesChannels y AuditLogs, cantidad total del tipo y cantidad válida. Una fila existente requiere Type Customer exacto (BIN2 y DATALENGTH), IsInternal=0 y nombre no vacío. Una fila incompatible se rechaza sin modificación.

En --apply, sólo si el tipo está ausente, inserta Id nuevo/Name Cliente/Type Customer/IsInternal0 y una auditoría CustomerSalesChannelRepaired, ActorType System=2. Ambas filas comparten transacción y se revierten si falla cualquier guarda/auditoría. Comprueba preservación de conteos de pedidos, operaciones, clientes, productos y perfiles, así como incrementos exactos de canal/auditoría. Repetir sobre canal válido no agrega filas ni cambia su nombre. No ejecuta bootstrap, migraciones, cuentas, cambios de contraseñas, reinicio o pedidos.

El modo predeterminado es de lectura; --apply autoriza explícitamente la transacción puntual. El reporte no imprime credenciales o streams SQL; errores inciertos conservan changes_may_have_occurred para no afirmar que se revirtió sin evidencia.

## Validación y entrega

238 pruebas Python locales correctas, incluidos trece casos de la nueva herramienta: idempotencia, rechazo de fila incompatible, privacidad, transportes/SQL autorizados, identidad I07/I08 y payload fijado. Se cargó el inspector real del payload original sin ejecutar reparación o preflight real en este entorno.

Se agregaron cuatro casos SQL/HTTP: inserción con DML limitado, preservación e idempotencia y captura cliente después de reparar; rollback si falla auditoría; rechazo de Customer interno o nombre vacío sin sobrescritura. La ejecución SQL real en CI queda pendiente antes de entregar la herramienta.

Exportación prevista: PRODIMT_Reparar_Canal_Cliente_I09.py, 15,310 bytes, SHA256 a8142f74b6d4946063266cf1912bee59a84431ad5a6ac3c440cc4cfc86b154df. El ZIP I09 original y sus hashes no cambian. No se ha ejecutado SSH ni modificado el VPS desde esta sesión.

Una vez reparado/verificado el canal puede ejecutarse el instalador I09 original con el payload ya presente. Guardar borradores antes del reinicio de la instalación. La reparación por sí sola no instala I09 ni acredita captura real del cliente.
