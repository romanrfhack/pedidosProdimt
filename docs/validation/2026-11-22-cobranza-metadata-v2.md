# Cobranza — revisión 2 del instalador por metadatos incompletos

## Resultado de destino recibido

El paquete de aplicación `ccc4193301043ec1935fb575d516319598fe94d0` pasó inventario y plan sobre API/frontend `machine-workspace-0d64f5bd2aef`, SQL `127.0.0.1,14330`, base `ProdimtPedidos`, historia 21. Al aplicar se detuvo en `stop-service` con `database_preservation_metadata_invalid`. El reporte del usuario acredita `database_migrations_run=false`, `previous_health_verified=true`, `recovery_pending=false`, entorno conservado y los mismos releases anteriores. Se prepararon archivos y snapshot privados, pero no se ejecutó la migración. La cobranza no está instalada.

## Causa y corrección

La consulta original calculaba SHA256 directamente sobre subconsultas escalares `FOR JSON`. Cuando `SecurityUserPermissions` está vacía, la subconsulta escalar devuelve NULL; `HASHBYTES` conserva ese NULL y el JSON exterior omite `user_permissions_hash`. El guard exige siete listas y tres hashes y recibe sólo nueve campos. Es un conjunto vacío legítimo, no una credencial SQL incorrecta.

La causa se reproduce en SQL Server 2022 Developer `16.0.4205.1`, incluida la ruta real `preserve_database → runtime.sql_json → sql_run → sqlcmd/ODBC → inspector.parse_sql_output`. El instalador CCC reproduce el rechazo con nueve campos; la misma base temporal acepta el instalador corregido con diez campos, siete listas y tres hashes de 64 caracteres. Esto demuestra el defecto y su solución sin inspeccionar ni publicar filas de producción.

V2 usa `COALESCE(subconsulta_JSON,N'[]')` antes de calcular cada uno de los tres hashes. Conserva los bytes Unicode de los conjuntos no vacíos y calcula una huella real y determinista del conjunto vacío. El JSON exterior conserva también nulos explícitos para que un fallo inesperado siga siendo visible y rechazado. No se relajan verificaciones ni se crean usuarios o permisos artificiales.

`validate_preservation_metadata` centraliza el contrato estricto de diez campos. Lo llaman la inspección y ambas modalidades de comparación, cerrando la brecha de las pruebas anteriores que podían comparar dos snapshots igualmente incompletos. El reporte identifica `installer_revision=collections-preservation-v2` y mantiene `source_commit=ccc4193301043ec1935fb575d516319598fe94d0` como fuente de aplicación.

## Evidencia

- Cuatro pruebas SQL Server reales correctas: actualización 21→22 con Mostrador nuevo/personalizado, tablas de permisos/asignaciones vacías y también usuarios vacíos. Se ejecuta el SELECT escalar envuelto y el guard público completo; las huellas vacías coinciden con SHA256 Unicode de `[]`.
- Reproducción nativa sqlcmd/ODBC de fallo CCC y pase v2 en la misma base temporal, sin valores privados en los diagnósticos.
- 34 pruebas Python de instalador/payload/WSL y nueve del reempaquetador correctas. Suite Python completa: 675 casos, cuatro omisiones históricas y cero fallos.
- Sin cambios en backend, frontend, migraciones SQL o helpers históricos de despliegue. Las validaciones funcionales del PR #12 conservan su alcance; la revisión correctiva tiene su propia validación de CI.

## Paquete conservado y reintento

`scripts/pilot/repackage-collections-installer.py` exige main limpio y una revisión descendiente de CCC que sólo modifique instalador, reempaquetador, pruebas o documentación. Rechaza reutilizar binarios si cambian aplicación, migraciones, soporte nativo, configuración o wrapper.

El ZIP original debe tener SHA256 `848a41ded5510a0d5d63abf36f620c5bf37c65b6f69fe43d324fab295519d175`. El payload permanece exactamente en `bbd4d3c91702ecc7249d896cf9ed1d12d687ef28e461d2caf09a55c49f7224e3`, con sus 92 archivos. Se conservan el wrapper y los pines de aplicación CCC; cambian únicamente instalador, instrucciones y checksums. El README registra la fuente de la corrección y el hash del ejecutor.

La entrega es `PRODIMT_Cobranza_ccc419330104_v2.zip`, con directorio interior `prodimt-collections-update`. Desde una extracción nueva se ejecuta `python3 PRODIMT_Desplegar_Cobranza_WSL.py --apply`. El wrapper copia el instalador corregido a la carpeta remota CCC y verifica de nuevo la base real; no cambia la identidad SQL ni fuerza otros releases. El estado final esperado es `updated/completed`, historia 22, salud/esquema/frontend verificados y `recovery_pending=false`. Hasta recibir ese resultado, siguen confirmados Máquina e historia 21.
