# I22 — Colisión de semillas tras validación SQL correcta

## Reporte del usuario

El 3 de noviembre el usuario verifica y copia el instalador v3 de SHA `1c5dec8f3bd590f109ce76a6fb356af33b6e5eca78209fd2d2a62f7528cf5806`. El preflight devuelve planned/completed, base y salud verificadas e historia exacta de 16 migraciones. --apply autentica al administrador SQL y obtiene:

- portable_validation: passed, 3/3, cleanupVerified=true.
- exact_sql_validation: passed, exactSql/strictMetadata/applicationPermissions/rollbackVerified/replayVerified/cleanupVerified=true.
- isolated_validation_verified=true y validation_databases_cleaned=true.
- Snapshot privado y BACKUP COPY_ONLY verificado, sin restauración automática.
- Error en database-backup-and-migration: sql_error_numbers=[2601], issue=sql_operation_failed.
- Recuperación final: previous_health_verified=true, recovery_pending=false, sin rollback_failed.

El código asigna database_migrations_run=true antes de intentar el SQL: no significa que se haya confirmado la migración17. La operación no llegó a activar releases, reiniciar la versión nueva ni verificar su frontend. Conserva contraseñas, tokens y clave.

## Recuperación y salud

rollback_service_stopped=true se escribe antes de comprobar la historia y llamar al rollback heredado. Después, si la historia conserva las identidades previas y no es una recuperación pendiente anterior, el código restaura enlaces y arranca/verifica la versión anterior. Los flags finales aportados corresponden a ese camino seguro; no afirman servicio detenido al concluir. Una consulta HTTPS independiente a /health/db durante este análisis devolvió status ok/database reachable.

No se restauró SQL automáticamente. El script utiliza una transacción y SET XACT_ABORT ON; la recuperación observó la historia previa antes de volver a arrancar. Se conserva el respaldo verificado como evidencia, no se propone restaurarlo.

## Causa acotada, aún no confirmada

Microsoft define 2601 como clave duplicada en un índice único: https://learn.microsoft.com/en-us/sql/relational-databases/replication/mssql-eng002601.

Las tablas nuevas no contienen datos previos; las inserciones de datos son el rol del sistema, tres permisos y sus grants. SecurityRoles.Name y SecurityPermissions.Key tienen índices únicos. El choque de PK de identidad/grants normalmente corresponde al código2627; se priorizan los dos índices anteriores como hipótesis, sin declararlos confirmados por el número solo.

I18 permite crear roles y permisos personalizados con GUID aleatorio y sin reservar los nombres o claves futuros. El preflight v3 valida semillas del sistema y grants del administrador, pero no detecta esas personalizaciones. El baseline aislado limpio no las contiene. Esto explica cómo las pruebas SQL pueden pasar y producción encontrar un duplicado; hace falta medir las cuatro identidades/nombres reales.

## Lectura siguiente

`PRODIMT_Diagnostico_Semillas_I22.py` fija el instalador v3 y el payload ya copiados, usa el bloqueo existente en lectura y ejecuta sólo el preflight más una consulta SELECT agregada exacta, con la identidad DML existente. Compara en SQL las cuatro semillas conocidas: ID esperado, nombre/clave, coincidencia por otro ID, mismo ID con otro nombre/clave, activo/sistema y cantidades de grants/asignaciones. No devuelve nombres de usuarios, GUIDs desconocidos, otras claves, contraseñas, errores crudos ni filas de pedidos.

No solicita administrador SQL ni tiene una vía de apply, rollback, stage, migración o cambio de servicio. Falta recibir ese JSON para elegir la corrección. No adoptar ni convertir automáticamente un rol coincidente: puede tener usuarios y permisos adicionales. Si sólo colisiona el nombre del rol, se evaluará crear el nuevo rol canónico bajo una etiqueta distinta conservando el existente íntegro. Si colisionan permisos, es otro caso de compatibilidad y requiere analizar su identidad y asignaciones.

## Validación del diagnóstico

Pasaron las 53 pruebas Python focalizadas: 47 anteriores y seis nuevas del diagnóstico de semillas. Las nuevas comprueban la composición del transporte DML `query_sql` con proceso SQL simulado, allowlist temporal y restauración ante fallos, coincidencias agregadas sin identidades ajenas, rechazo de campos/tipos no autorizados y verificación de archivos/bloqueo antes de ejecutar código. Las correlaciones RoleId/PermissionId y la consulta SELECT fueron revisadas independientemente. No se ejecutó esta lectura en el VPS desde el agente.

Archivo `PRODIMT_Diagnostico_Semillas_I22.py`, SHA256 `91176603ecda19fdb5aaae5d3141eefd3e006bd15cb991dc5720da57f740eb45`. El instalador v3, el payload y la migración no se modificaron. El bundle de continuación incluye sólo la herramienta y documentación/pruebas nuevas. `status=observed` de esta lectura no equivale a instalar I22.
