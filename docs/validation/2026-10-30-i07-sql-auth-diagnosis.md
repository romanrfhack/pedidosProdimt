# Diagnóstico SQL I07 de sólo lectura

El reporte del usuario muestra sql_operation_failed en sql-admin-authentication, sin cambios, con diez migraciones y permisos de aplicación correctos. sql_error_numbers vacío sólo indica ausencia de encabezados Msg reconocidos por el instalador. La causa remota aún no está determinada.

## Herramienta

- Fuente: scripts/pilot/diagnose-capture-auth.py.
- Entregable: PRODIMT_Diagnostico_SQL_I07.py, revisión 2, 11,005 bytes.
- SHA256 revisión 2: 8e7f3b99e874adb6b97ee1958311c01f4b25ddd89ba49481280528c2043a03fc.
- Verifica el instalador I07 exacto publicado (SHA256 42b1e7d869a41d3d42851925585035089e5d58450993ebb16b89bb9d44c5fb1b), que a su vez verifica el payload/inspector original.
- Ejecuta únicamente preflight y authenticate_admin originales: no invoca apply, migraciones, respaldo, activación ni reversión. Una lista de procesos y SELECT explícitos bloquea escrituras por esa vía. El inspector verificado por hash conserva su consulta de metadatos con permisos de aplicación.
- Credenciales por getpass en TTY privado; SQLCMDUSER/PASSWORD sólo en entorno limpio, no en argumentos. Se limpian referencias al concluir.
- Clasifica login rechazado, cuenta deshabilitada/bloqueada/expirada, acceso a BD, TLS, driver/opciones, conexión/timeout, permisos o consulta. Devuelve etapa, códigos/categorías y permisos 0/1; nunca devuelve fragmentos de los errores originales.
- No modifica el instalador ni el paquete I07. No altera contraseñas ni config/Nginx/servicios.

## Validación

173 pruebas Python locales correctas: 162 previas y 11 del diagnóstico. Pruebas de secretos sintéticos en stdout/stderr, categorías ODBC sin Msg, pin antes de ejecutar, rechazo de DDL/backup/lectura no permitida, entorno privado, timeout, metadata permitida, limpieza y autenticación/verificación del esquema mediante el método original. Se añade a CI una comprobación de error de login con ODBC real y un usuario ficticio inexistente en la instancia desechable. [CI 41105959819](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41105959819) con cuatro jobs correctos para `cd3ba9a034a5331a1437b95118b22ced382d8ccd`: 267 .NET, 29 SQL Server, 68 E2E, 173 Python y smoke Production. ODBC real confirmó la clasificación de un login ficticio inexistente, sin exponer credenciales ni streams. El diagnóstico mantiene exactamente el mismo hash publicado.

## Ejecución

Copiar la herramienta a /root y ejecutar desde una terminal interactiva:

```bash
ssh -t root@194.238.26.70 'python3 /root/PRODIMT_Diagnostico_SQL_I07.py'
```

Usa por defecto /root/PRODIMT_Instalar_I07.py y /root/prodimt-quick-capture-payload.zip. Solicita las credenciales SQL administrativas existentes y devuelve JSON sin secretos. Revisar sql_reads y su stage/category, y administrator_permissions cuando haya conexión. status inspected confirma únicamente comprobaciones de lectura; no declara I07 instalado. Si identifica sql_login_rejected, comprobar login/contraseña/estado de cuenta según la evidencia, sin reset automático.

Pendiente ejecutar con el usuario y revisar el JSON. El último despliegue confirmado sigue siendo I06.

## Corrección de revisión 1

La ejecución real de v1 se detuvo antes de SQL: el adaptador no preservaba subprocess.DEVNULL, requerido por command(). Defecto reproducido localmente con el instalador entregado. Se corrige ese valor y se marca revisión 2. La nueva prueba ejecuta ambos métodos originales, incluyendo los helpers reales de proceso/HTTP y el inspector real, con archivos y transporte ficticios. Cubre éxito y rechazo administrativo. Falla sobre v1 y pasa sobre v2. 174 Python locales correctas; CI de la corrección pendiente.

La revisión 1 tiene SHA256 476c229e0df43ec31f6872cd524fdff8e811f88ff3bd3ab7fbe0828436aab16f. La corrección sólo cambia revisión y preserva DEVNULL. Puede repararse el script ya copiado al VPS, sin descargar otra vez, con este comando que comprueba ambos hashes. Se probó el parseo de shell, la reparación, su repetición sin cambios y el rechazo sin escritura de bytes desconocidos.

```bash
ssh -t root@194.238.26.70 'python3 -c "from pathlib import Path
import hashlib
p = Path(\"/root/PRODIMT_Diagnostico_SQL_I07.py\")
raw = p.read_bytes()
old = \"476c229e0df43ec31f6872cd524fdff8e811f88ff3bd3ab7fbe0828436aab16f\"
new = \"8e7f3b99e874adb6b97ee1958311c01f4b25ddd89ba49481280528c2043a03fc\"
current = hashlib.sha256(raw).hexdigest()
if current not in (old, new):
    raise SystemExit(\"Hash no reconocido; no se modifico el archivo\")
fixed = raw.replace(b\"20261030-sql-auth-diagnosis1\", b\"20261030-sql-auth-diagnosis2\", 1)
fixed = fixed.replace(b\"TimeoutExpired=subprocess.TimeoutExpired)\", b\"TimeoutExpired=subprocess.TimeoutExpired, DEVNULL=subprocess.DEVNULL)\", 1)
if hashlib.sha256(fixed).hexdigest() != new:
    raise SystemExit(\"La correccion no coincide; no se modifico el archivo\")
if current == old:
    p.write_bytes(fixed)
print(\"Diagnostico revision 2 verificado\")" && python3 /root/PRODIMT_Diagnostico_SQL_I07.py'
```

Este comando modifica exclusivamente el diagnóstico verificado y después lo ejecuta en modo lectura. No instala I07. El problema SQL original sigue pendiente de la salida del usuario.
