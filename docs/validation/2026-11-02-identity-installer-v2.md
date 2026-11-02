# I18 — Revisión 2 del preflight SQL

Fecha: 2 de noviembre de 2026. Antecedente: el usuario verificó el SHA256 del ZIP original, subió instalador/payload y ejecutó sólo lectura. Resultado: `token_management_schema_unexpected` en `application-sql-read`,13 migraciones y grants/canales correctos. Sin cambios remotos según el JSON reportado. No se han observado los contadores concretos del esquema de tokens del VPS.

## Defecto y alcance de la corrección

El instalador original comparaba la definición textual de defaults desde el usuario limitado. SQL Server devuelve NULL en `sys.default_constraints.definition` cuando ese principal carece de permisos para ver definiciones, aun teniendo DML sobre la tabla. Fuente primaria: https://learn.microsoft.com/en-us/sql/relational-databases/security/metadata-visibility-configuration . Esto permite reproducir el falso negativo; no demuestra por sí solo que sea la única discrepancia real del VPS.

La revisión `20261102-identity-administration-install2` consulta nueve contadores/banderas seguros: estructura, tipos, longitud, nulabilidad, bindings y visibilidad/validez de defaults. Estructuras incompletas o defaults visibles incorrectos se rechazan. Sólo un default realmente oculto puede quedar pendiente; `token_schema_defaults_pending_admin=true` no significa verificación exacta completada. `token_schema_checks` y `token_schema_mismatches` nunca contienen definiciones, credenciales, hashes o tokens.

`authenticate_admin → verify_schema` mantiene la comparación exacta anterior con el administrador SQL. Se ejecuta antes de snapshots/staging/parada/backups/DDL. En la repetición de `already_current`, si hay defaults pendientes, `--apply` también autentica/verifica antes de retornar sin cambios. No se conceden privilegios de metadatos al usuario app, ni se modifica el esquema, backend, frontend, clave o acceso de clientes por esta corrección.

## Validación

24/24 pruebas Python específicas correctas: lectura oculta/mixta/visible, drift estructural, flags/formatos inesperados, defaults incorrectos, diagnósticos sin secretos, orden de verificación administrativa anterior a cualquier cambio y cargador exportado compatible con el payload fijado. `py_compile` y `git diff --check` correctos.

El verificador ODBC crea un usuario ficticio sin login y sólo DML en una base desechable; compara el fallo original con la nueva consulta y valida esquema parcial, default faltante, tipo incorrecto y defaults visibles/ocultos incorrectos. Las alteraciones de prueba se revierten y el administrador conserva la validación estricta. Su ejecución real en CI queda pendiente en este checkpoint.

## Entrega y estado

Se entrega sólo `PRODIMT_Instalar_I18_v2.py`; reutiliza `/root/prodimt-identity-payload.zip` ya copiado y verifica SHA256 `617e227bfc1a66becd75db395c5d9a5cf4a0d94e814f00e4a4f7c2bad0c5f326` y sourceCommit `c49df453de9d07f5be301fb109dbb813309326d9`. ZIP y payload originales no se regeneran. SHA256 de la revisión2 y resultados CI se registrarán tras validarlos.

Sin acceso independiente al VPS/SQL operativo. I18 no está instalado; la lectura con revisión2 deberá confirmar contadores/salud/base antes de aplicar. Un posible drift real seguirá bloqueando la instalación sin cambios.
