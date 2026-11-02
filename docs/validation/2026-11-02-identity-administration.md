# I18 — Evidencia de validación

Fecha: 2 de noviembre de 2026. Base I17 `cdbfec3071bc2046f0398fbdad6b62bdca6de748`; rama `codex/i18-identity-administration`.

## Alcance comprobado localmente

Preparación nativa I18: `scripts/pilot/build-identity-package.py`, `install-identity-update.py`, `tests/pilot/test_identity_package.py` y `verify_identity_sqlcmd_migration.py`. Los helpers de inspección/despliegue I07/I11/I12 se reutilizan sin modificar sus archivos ni llamar a la generación de clave o revocación I12.

`python3 -m unittest discover -s tests/pilot -p test_identity_package.py -v`: **16 pruebas correctas**. Comprobaciones: pines/manifiesto del constructor e instalador exportado, rechazo de payload modificado, rechazo de release paralelo I16, respaldo fallido que impide migración, conservación de claves y ausencia de revocación, límites de transacción y separación DDL/seed.

La revisión detectó dos defectos corregidos antes de entrega: comparación Python encadenada incorrecta del número de BEGIN/COMMIT en el constructor y carrera entre leer la revisión de seguridad y detener la API al recuperar I12. Las regresiones exigen exactamente una transacción y orden **detener API → leer revisión/grafo → recuperar binario previo**. Si falla detener, cambia la seguridad o no puede leerse SQL, no se inicia I12 y se informa `recovery_manual_required`. Es necesario porque I12 interpreta toda cuenta interna activa como administración total.

Normalizador probado con los scripts EF reales incremental e idempotente de `20261102235000_AddSecurityAdministration`, generados por el trabajo backend. Inserta únicamente GO antes del primer bloque completo de seed de SecurityStates, sin cortar IF/BEGIN ni cerrar la transacción. Repetir normalización conserva bytes. El validador del instalador acepta el resultado. Esto comprueba estructura/transporte previsto, **todavía no una ejecución SQL Server**.

`git diff --check` correcto para los cambios presentes al realizar esta revisión. La importación/compilación Python de los nuevos scripts también es correcta.

## Aplicación y navegador

Validación local coordinada: **351 pruebas Application y 5 Domain correctas (356 en total)**; Domain contiene tres Fact y dos casos InlineData. Build backend y publicación linux-x64 correctos. EF `has-pending-model-changes` no detecta diferencias tras la migración catorce; el proyecto SQL Server compila con seis casos nuevos I18, cuya ejecución real queda pendiente de CI.

Angular build correcto. **12 nuevas pruebas E2E de seguridad y 38 regresiones de interfaz correctas**. Verificadas búsqueda, CRUD, varios roles/procedencia de permisos, errores/conflicto y guardas; medidas y capturas de listado/editor a390/768/1440/1920px con datos ficticios. Las capturas locales de revisión no se entregan como evidencia de datos reales. Los resultados no equivalen a aceptación del usuario.

La ejecución de la suite Python completa devolvió código0, pero el registro local quedó truncado antes del resumen: no se informa un conteo completo ni se sustituye su ejecución en CI. Las16 pruebas específicas I18 sí conservan resumen correcto. `graphify update .` no está disponible en este entorno (127); no hay grafo del repositorio que actualizar.

## SQL por transporte real

El workflow prepara baseline 0→I12 e incremental I12→I18 idempotentes y ejecuta:

```bash
python3 tests/pilot/verify_identity_sqlcmd_migration.py --baseline /ruta/baseline-i12.sql --script /ruta/upgrade-i18.sql
```

Sólo admite la conexión sintética exacta de CI y una base desechable aleatoria. Reutiliza `sql_run` y opciones ODBC del instalador nativo, transporta los bytes normalizados del mismo constructor del ZIP, inyecta un fallo de seed para comprobar rollback integral, aplica la actualización, verifica cuentas activas/inactivas y contraseñas preservadas, catorce migraciones, esquema/seeds y repetición sin restaurar un rol retirado. El setup desechable conserva la reparación exacta de batch I12 para evitar reproducir SQL207 al crear el baseline. Resultado de ejecución pendiente de CI al escribir este registro.

## Cierre de CI y paquete

Fuente de aplicación/paquete `c49df453de9d07f5be301fb109dbb813309326d9`; [PR #10](https://github.com/romanrfhack/pedidosProdimt/pull/10) en borrador. [CI 41477009188](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41477009188) completo: cuatro trabajos correctos, **356 Domain/Application, 72 SQL/criptografía, 163 E2E y 366 Python**, más smoke Production inicial y persistido. SQL ODBC validó la migración13→14, rollback integral del DDL/seed y repetición sin reotorgar roles.

El transporte ODBC registró16 llamadas nativas, `ddl_seed_rollback_verified=true`, `existing_credentials_preserved=true` y `repeat_does_not_restore_grants=true`. SHA256 de SQL exacto `95398db9ae5b24c97e9ca0c59bf85b71a2c8c4a9ebcec9aa3b07e5a81561a5ed`, idéntico a `migrations-i18.sql` dentro del ZIP. Esto sustituye el pendiente de ejecución SQL de las secciones anteriores.

Entrega final `PRODIMT_Usuarios_Roles_Permisos_I18.zip`, 7414079 bytes, SHA256 `06e40518871904a2f9e3c834f03ad441940326974baeb828269c2bc2552b44e4`; payload `617e227bfc1a66becd75db395c5d9a5cf4a0d94e814f00e4a4f7c2bad0c5f326`. Construida desde worktree limpio de la fuente fijada;73 archivos en payload, con frontend emitido a directorio nuevo y licencias incluidas. Verificados CRC exterior/interior, SHA256SUMS, manifiesto/hashes, pines e importación/instanciación del cargador final. ZIP guardado como entrega persistente. Las actualizaciones documentales posteriores conservan el código/binarios de esta fuente.

## Pendientes

Instalación I18 y aceptación de negocio. Sin acceso al VPS o SQL operativo desde este trabajo; I17 sigue siendo el último despliegue confirmado por el usuario. No se restauró SQL, modificó configuración real o enviaron mensajes.
