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

## Cierre SQL y entrega

Fuente de instalador/pruebas `87f8a2764c816219050f50002f3bed2763b5efb0`, CI [41492453572](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41492453572). SQL real correcto: 72 pruebas; verificador ODBC 34 llamadas y todas las banderas `original_hidden_default_mismatch_reproduced`, `read_only_structure_without_metadata_grant_verified`, `malformed_schema_rejected`, `administrator_exact_defaults_enforced` en true. El SQL14 sigue teniendo SHA256 `95398db9ae5b24c97e9ca0c59bf85b71a2c8c4a9ebcec9aa3b07e5a81561a5ed`, idéntico al payload original. Python 374 y smoke Production correctos; 356 Domain/Application correctas. Navegador 163/163 correcto; los cuatro trabajos CI terminaron en success.

La integración de las pruebas corrigió un mock global de espacio en disco para restaurarlo al salir del caso y la sintaxis del DDL dinámico del fixture desechable. En una sola ejecución,24 pruebas I18 más la de inventario pasan sin contaminar estado entre módulos. Ninguno de esos ajustes modifica el instalador entregado o los datos de aplicación.

Archivo final guardado: `PRODIMT_Instalar_I18_v2.py`, 37653 bytes, SHA256 `2bc080cb67f51ff0b1b0884e7cf3dbbb86131fe0cb5e2c4a7a9fae07cb92fdcd`. El cargador exportado verifica el payload original y la consulta de metadatos ocultos queda pendiente de administrador sin tocar archivos/servicio/SQL. El archivo conserva source/payload pins originales y el ZIP no se reemplazó. Instalar I18 y confirmar el estado real del VPS requiere primero la nueva lectura; un drift real permanecerá bloqueado.


CI final completo y correcto sobre la fuente fijada:356 Domain/Application,72 SQL/criptografía,163 E2E,374 Python y smoke Production inicial/persistido. Los fallos intermedios del fixture quedaron corregidos antes de entregar; no se omitieron pruebas. La lectura del usuario con revisión2 y la instalación remota siguen pendientes.
