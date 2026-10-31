# I07: canal administrativo faltante en la aceptación

## Causa confirmada

El usuario ejecutó el diagnóstico de sólo lectura: inspected/completed, servicio activo y releases I07 en backend/frontend. Tres eventos de capture se clasificaron como admin_sales_channel_missing, con InvalidOperationException y stack de AdminOrderCaptureService/EfSalesChannelRepository. No hubo códigos SQL ni tablas con denegación. El código lanza esa categoría cuando no encuentra SalesChannels.Type=AdminManualCapture.

El primer reporte contenía preview correcto con perfil aprobado y unidades confirmadas; la segunda imagen confirmó capture HTTP 500. El canal no se consulta en preview. SubmitCustomerOrderAsync lo busca antes de crear el pedido y dentro de la transacción del intento: esos fallos no alcanzan la inserción de pedido/recibo. No se afirma que no existan otros pedidos de ese cliente; conservar referencia y control de duplicados.

Las migraciones nativas preservan datos, incluidos canales vacíos; los tests anteriores de captura sembraban el canal. No es un problema de contraseña, parser, unidad, perfil aprobado o permisos SQL demostrado por este diagnóstico. No se publican imagen, clientes, identificadores, petición real ni autenticación.

## Reparación puntual

- Fuente: scripts/pilot/repair-native-admin-channel.py.
- Entregable: PRODIMT_Reparar_Canal_Admin_I07.py, revisión 20261030-admin-channel-repair1.
- SHA256: 39147bfb8d029c5865c102fec05e6d4da837400be78abeec12d2afd816c3fa6e.
- Código de herramienta: 63b955ad333c1d4e3cc2f3f4eb73187a1629cef7.
- Dependencia que ya está en el VPS: /root/prodimt-quick-capture-payload.zip, SHA256 06c1a7fd68ec997b7ba7efa92ddf65de6b35fae58dc800ed7ec2596a0741755b. Carga sólo el inspector del ZIP verificado, sin ejecutar el instalador.
- Exige proceso www-data y enlaces I07 activos, proveedor/servidor/base/login esperados, once migraciones, índice único de tipo y SELECT/INSERT de aplicación. Obtiene la conexión efectiva con el inspector ya usado en la instalación; no solicita credenciales SQL ni toca api.env.
- Sin --apply sólo consulta. Con --apply inserta únicamente el canal ausente Captura administrativa / AdminManualCapture / IsInternal=1 y su AuditLog en una transacción. AuditLogs.ActorType se almacena como entero: System=2.
- Comprueba conteos de pedidos, recibos, clientes, productos y perfiles, además del incremento exacto canal/auditoría. No actualiza registros existentes. Canal ya correcto conserva Id/nombre y no repite auditoría; canal incompatible o permisos faltantes detienen la herramienta.
- XACT_ABORT, timeout de bloqueos, opciones de sesión e índice único protegen la operación. Un fallo revierte canal y auditoría juntos. Si se pierde confirmación de transporte, informa changes_may_have_occurred; repetir esta reparación comprueba el canal existente, sin agregar otro.
- No hay bootstrap, migraciones, restauración de SQL, cambio de cuentas/contraseñas/perfiles, modificación de binarios o reinicio. La operación aditiva y auditada no necesita restaurar el respaldo previo ni detener captura. Se conservan los respaldos existentes.
- SQLCMDUSER/PASSWORD viajan sólo en entorno limpio, que se limpia al finalizar; SQL usa entrada estándar y -I/-x. Salidas contienen sólo booleanos, estado y códigos SQL numéricos. No se imprime configuración ni streams de error.

## Validación

197 pruebas Python locales correctas, 11 nuevas para modo lectura/aplicación, rechazo de base/esquema/permisos/canal incompatible, idempotencia, pin previo a ejecución y ausencia de secretos en argv/salida. Carga y parser ejecutados contra los bytes del payload original entregado; se comprobó la coincidencia de sus constantes con el target fijado.

Dos tests nuevos de SQL Server sobre bases desechables y datos ficticios ejecutan OPTIONS/REPAIR_SQL de la herramienta mediante importación Python, sin duplicar el script SQL:

1. Ausencia de canal → preview 200 y capture fallido sin pedido/recibo/auditoría de pedido. Reparación con SELECT/INSERT limitados → un canal/auditoría; repetición conserva configuración personalizada y no duplica. El mismo intento original guarda y se recupera por recibo: un pedido y dos líneas.
2. Rechazo de la auditoría → rollback del canal; al quitar el rechazo, la reparación funciona una vez y el siguiente intento no duplica.

La primera corrida de la regresión detectó el tipo entero de ActorType en AuditLogs. La herramienta se corrigió a 2 antes de entregar; no hubo ejecución en el VPS. [CI 41167167347](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41167167347) sobre el código indicado: cuatro jobs correctos, incluidos los 31 tests SQL Server y las dos regresiones nuevas; 267 .NET, 68 E2E y 197 Python/Production. No se afirma ejecución local de SQL Server. graphify update . no disponible (127).

## Ejecución y aceptación pendiente

Tras descargar y verificar el SHA del archivo, ejecutar desde WSL:

```bash
ssh root@194.238.26.70 'python3 - --apply' \
  < /mnt/c/Users/TUF/Downloads/PRODIMT_Reparar_Canal_Admin_I07.py
```

El usuario sólo autentica SSH; no hace falta copiar otro instalador ni introducir un usuario SQL administrativo. Se espera status repaired o already_configured, phase completed, channel_verified=true, existing_data_counts_preserved=true y application_save_verified=false.

La herramienta no envía pedidos de prueba. Después del JSON correcto, usar Reintentar guardado en la captura que permanece abierta. Conservar pestaña, contenido y operación, sin refrescar o crear otra entrega independiente. Confirmar pedido visible una sola vez y cantidades correctas. Si el resultado no es correcto, revisar el JSON antes de repetir acciones. La reparación aplicada ya está confirmada por el usuario; el guardado de negocio sigue pendiente.

## Reparación aplicada — 31 de octubre de 2026

El usuario verifica el SHA del archivo entregado y ejecuta por SSH/entrada estándar con --apply. Su JSON confirma revisión 20261030-admin-channel-repair1, repaired/completed, changes_made=true, channel_created=true, channel_verified=true, audit_inserted=true y existing_data_counts_preserved=true. No hubo migraciones, reinicio ni reproducción del pedido, y no se imprimieron secretos.

channel_missing=true refleja el preflight previo a la inserción. application_save_verified=false refleja que la herramienta no envía una captura; no es fallo de la reparación. changes_may_have_occurred=true es coherente con la inserción confirmada, sin evidencia de rollback. No es necesario repetir la herramienta.

Siguiente paso: Reintentar guardado en la pestaña existente con la misma operación/contenido; comprobar una sola entrega y las cantidades previstas. No declarar aceptado el guardado a partir de la reparación del canal. Sin acceso directo al VPS desde este entorno; evidencia proporcionada por el usuario. Sólo actualización documental, sin repetir suites ni cambiar artefactos.

## Diagnóstico previo archivado

Fuente scripts/pilot/diagnose-capture-save.py; entrega PRODIMT_Diagnostico_Captura_I07.py, revisión 20261030-capture-save-diagnosis1 y SHA e0f83643a4f2a8beff76f85009fbe57fa83dfcfcda4e145f1871fd2b3a2de0d0. Lee únicamente estado y journal; no SQL/API/reproducción. Sus doce casos y la suite de 186 Python pasaron local y en CI 41166419971, cuatro jobs correctos. El usuario confirmó su ejecución sin cambios y su clasificación de canal faltante. No sustituye a la reparación ni es necesario repetirlo ahora.
