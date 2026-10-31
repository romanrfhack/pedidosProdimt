# Actualizador nativo I04 — validación

El usuario ejecutó la inspección del kit nativo en el VPS: configuración Production compatible, runtime .NET/ASP.NET 10.0.13, base esperada con cinco migraciones, cero pedidos/grupos duplicados y cuenta cotidiana sin DDL/BACKUP/verificación. El actualizador se limita a ese despliegue y ZIP exactos.

## Comprobaciones locales

- 71 pruebas Python correctas, incluidas 41 del actualizador: guards, configuración y secretos, ZIP seguro, estados de parada/respaldo/migración/activación, fallos de cada etapa, interrupciones y rollback bajo el mismo lock.
- Health directo: proceso/release esperado, base accesible, endpoints I04 protegidos y HTML/JS/CSS exactos mediante HTTPS. La reversión sólo arranca después de confirmar ambos enlaces anteriores y luego comprueba su proceso/salud.
- Autenticación administrativa y capacidades antes de detener PRODIMT. Contraseñas y usuario SQL sólo en entorno mínimo del cliente, sin argumentos ni stdout, ni SQLCMDINI heredado. Archivo privado no se ejecuta como shell y se conserva.
- Respaldo COPY_ONLY/CHECKSUM y VERIFYONLY completados antes de DDL. Error o interrupción desde iniciar el script marca que el esquema puede haber cambiado; nunca se restaura la base automáticamente.
- Publicación de releases y normalización no recursiva sólo de cinco directorios estructurales propios; metadatos previos guardados. DML adicional limitado a dos objetos nuevos; la cuenta cotidiana mantiene la ausencia de DDL.
- Dos revisiones independientes cerradas sin bloqueadores; código de la aplicación sin cambios frente al payload b630072 ya probado. `git diff --check` correcto.

## SQL real en CI

`NativeUpgradeSqlTests` se compila sin advertencias y se ejecuta en el workflow contra una instancia desechable. Aplica las cinco migraciones anteriores, inserta cliente y administrador ficticios, crea usuario sin DDL/BACKUP, realiza COPY_ONLY/CHECKSUM y VERIFYONLY, aplica el SQL idempotente hasta nueve migraciones dos veces y confirma conservación de datos/hash/estados/SID, índices y DML de tablas nuevas. El backup contiene un identificador único y desaparece al eliminar el contenedor.

El workflow existente también comprueba las pruebas HTTP/SQL anteriores y el contrato de JSON de sqlcmd real. Los resultados remotos se enlazan en el PR y acompañan la distribución validada. Ninguna de esas pruebas utiliza la base del usuario.

## Ejecución pendiente en el VPS

El comando `--apply` vuelve a ejecutar todas las comprobaciones de lectura y valida el acceso administrativo antes de detener el servicio. El usuario introducirá su identidad administrativa existente mediante terminal oculta; no se añade ni recupera su contraseña automáticamente. Hasta recibir el JSON final de esa ejecución, no se afirma que exista respaldo, que el esquema esté actualizado ni que I04 esté desplegado. La verificación de un backup no sustituye un ensayo de restauración completo.
