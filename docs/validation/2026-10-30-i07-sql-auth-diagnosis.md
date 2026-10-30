# Diagnóstico SQL I07 de sólo lectura

El reporte del usuario muestra sql_operation_failed en sql-admin-authentication, sin cambios, con diez migraciones y permisos de aplicación correctos. sql_error_numbers vacío sólo indica ausencia de encabezados Msg reconocidos por el instalador. La causa remota aún no está determinada.

## Herramienta

- Fuente: scripts/pilot/diagnose-capture-auth.py.
- Entregable: PRODIMT_Diagnostico_SQL_I07.py, 10,977 bytes.
- SHA256: 476c229e0df43ec31f6872cd524fdff8e811f88ff3bd3ab7fbe0828436aab16f.
- Verifica el instalador I07 exacto publicado (SHA256 42b1e7d869a41d3d42851925585035089e5d58450993ebb16b89bb9d44c5fb1b), que a su vez verifica el payload/inspector original.
- Ejecuta únicamente preflight y authenticate_admin originales: no invoca apply, migraciones, respaldo, activación ni reversión. Una lista de procesos y SELECT explícitos bloquea escrituras por esa vía. El inspector verificado por hash conserva su consulta de metadatos con permisos de aplicación.
- Credenciales por getpass en TTY privado; SQLCMDUSER/PASSWORD sólo en entorno limpio, no en argumentos. Se limpian referencias al concluir.
- Clasifica login rechazado, cuenta deshabilitada/bloqueada/expirada, acceso a BD, TLS, driver/opciones, conexión/timeout, permisos o consulta. Devuelve etapa, códigos/categorías y permisos 0/1; nunca devuelve fragmentos de los errores originales.
- No modifica el instalador ni el paquete I07. No altera contraseñas ni config/Nginx/servicios.

## Validación

173 pruebas Python locales correctas: 162 previas y 11 del diagnóstico. Pruebas de secretos sintéticos en stdout/stderr, categorías ODBC sin Msg, pin antes de ejecutar, rechazo de DDL/backup/lectura no permitida, entorno privado, timeout, metadata permitida, limpieza y autenticación/verificación del esquema mediante el método original. Se añade a CI una comprobación de error de login con ODBC real y un usuario ficticio inexistente en la instancia desechable. CI pendiente en este corte.

## Ejecución

Copiar la herramienta a /root y ejecutar desde una terminal interactiva:

```bash
ssh -t root@194.238.26.70 'python3 /root/PRODIMT_Diagnostico_SQL_I07.py'
```

Usa por defecto /root/PRODIMT_Instalar_I07.py y /root/prodimt-quick-capture-payload.zip. Solicita las credenciales SQL administrativas existentes y devuelve JSON sin secretos. Revisar sql_reads y su stage/category, y administrator_permissions cuando haya conexión. status inspected confirma únicamente comprobaciones de lectura; no declara I07 instalado. Si identifica sql_login_rejected, comprobar login/contraseña/estado de cuenta según la evidencia, sin reset automático.

Pendiente ejecutar con el usuario y revisar el JSON. El último despliegue confirmado sigue siendo I06.
