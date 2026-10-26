# Cuenta administrativa generada — 26 de octubre de 2026

El usuario confirma que su navegador ya usa `/api/auth/admin/login` y muestra 401 con sus credenciales. Solicita una cuenta nueva con contraseña generada, pruebas y entrega del acceso. El cambio de contraseña dentro de la aplicación se desarrollará después de confirmar acceso; no forma parte de esta herramienta local.

`scripts/pilot/create-native-admin.py`, revisión `20261026-admin-create1`, usa el módulo previo `PRODIMT_Restablecer_Admin.py` del VPS sólo si propietario/modos/archivo regular/hash exacto coinciden. Ese módulo conserva el protocolo Identity V3 ya contrastado con .NET, el destino SQL revisado, TTY oculta y transporte SQL/HTTPS. No se cambian sus bytes.

- Cuenta nueva `roman.prodimt`; contraseña de 32 caracteres URL-safe, generada con 24 bytes aleatorios en el VPS. No hay contraseña operativa en git, CI, artifact entregado o argumentos. La entrada manual se limita al usuario/contraseña SQL existentes.
- Referencia persistente en `/var/backups/prodimt-pedidos/admin-create-roman-prodimt/credentials.json`: directorio root/700, archivo root/600, sin symlinks/hardlinks, validación de campos/destino/Id/hash/contraseña. Se sincroniza antes de DML para poder recuperar un resultado perdido. Es un archivo privado de acceso; no se pega en el chat.
- Alta de usuario y auditoría System en una transacción. Bloqueo de rango por nombre y unicidad protegen la carrera. Repetir sólo acepta la misma operación, Id, hash, usuario activo y auditoría exacta; no rota credenciales ni duplica cuentas/avisos. Una cuenta anterior con ese nombre, una referencia ajena o una cuenta desactivada bloquean; no se editan ni reactivan.
- Tras confirmar la fila, prueba login y GET administrativo por HTTPS con certificado válido. Secretos por stdin/entorno privado; no en argv/JSON de diagnóstico. Muestra usuario/contraseña sólo en TTY tras ambas respuestas 200. Si falla después de DML, conserva referencia privada e indicadores de escritura; no intenta borrar/restaurar automáticamente.
- Lock compartido con mantenimiento nativo. No reinicia ni toca cuentas anteriores, configuración, permisos SQL, migraciones, catálogo o pedidos. Sin `--apply` sólo imprime plan.

129 pruebas Python locales correctas, incluidas nueve nuevas de la herramienta. El contrato sqlcmd real de CI ejercita alta/repetición, preservación, rechazo de adopción/reactivación y rollback de alta ante fallo de auditoría. Resultados remotos pendientes de registrar. Diff correcto; graphify ausente (127).

El intento SSH desde este entorno volvió a fallar por falta de ruta; la ejecución debe realizarla el usuario desde WSL. Preparar/probar con datos ficticios no crea la cuenta en el VPS. No declarar completado el acceso hasta revisar su JSON y confirmación de navegador.
