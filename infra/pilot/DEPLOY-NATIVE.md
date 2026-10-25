# Actualización nativa I03-B

El VPS elegido ya tiene PRODIMT activo mediante systemd, frontend/backend en releases, SQL Server nativo y certificado con renovación webroot. Actualizar esa instalación conserva su base y evita compilar o crear otro SQL en un host compartido.

**Estado comprobado el 25 de octubre de 2026:** el usuario completó la reanudación `20261025-resume5`: nueve migraciones, releases I04 y salud/proceso/frontend HTTPS verificados. La secuencia de actualización de esta guía queda como referencia; no repetirla sobre la instalación ya actualizada. Continuar con login, revisión de catálogos y carga privada mediante vista previa. Evidencia y respaldos en `../../docs/validation/2026-10-25-native-resume.md`.

## Paquete preparado

API .NET 10 framework-dependent linux-x64 y Angular Production compilados fuera del VPS desde `b630072ce67a691c3757d0590cc7f7d45f423d52`, el mismo código validado en CI I04. El frontend usa la API del mismo origen y sus credenciales demo están vacías. Incluye SQL idempotente con nueve migraciones, cinco anteriores y cuatro de octubre. Los binarios no incluyen datos reales, archivos privados del servidor ni credenciales operativas.

La comprobación de API `/health` con configuración Production y conexión ficticia acredita arranque, no conectividad con la base del usuario. Las veinte pruebas SQL de CI usan una base desechable y no prueban su esquema actual.

## Secuencia

1. Ejecutar la inspección nativa de sólo lectura con el servicio actual encendido. Lee configuración efectiva y muestra sólo metadatos permitidos, flags de compatibilidad y resultados SQL de consulta.
2. Revisar destino de conexión, migraciones aplicadas, posibles duplicados de la clave nueva y permisos de respaldo/verificación y DDL. La cuenta cotidiana puede requerir otra identidad para la actualización; no elevarla permanentemente.
3. Preparar respaldo `COPY_ONLY` con checksum y verificarlo antes de aplicar migraciones. Guardar versión de configuración y destinos actuales de los releases en un directorio privado. El éxito de VERIFYONLY no sustituye un ensayo de restauración.
4. Preparar nuevos releases y sus permisos sin cambiar `current`. Programar una ventana breve, detener sólo PRODIMT, aplicar las migraciones que faltan y cambiar enlaces. Mantener secretos en la configuración privada existente. No ejecutar el bootstrap de instalación nueva, que puede crear usuarios/canales.
5. Reiniciar únicamente el servicio PRODIMT y comprobar salud, login, catálogos, pedidos e I04. Ante fallo, restaurar enlaces de binarios y servicio anterior; el rollback de código no deshace el esquema SQL.

La inspección del usuario confirmó configuración compatible, runtime 10.0.13, cinco migraciones y ausencia de pedidos/duplicados. La cuenta cotidiana carece de DDL y permisos de respaldo/verificación. La fase de escritura se prepara con `scripts/pilot/update-native.py` y el ZIP nativo exacto, sin usar bootstrap. Nginx y certificado actuales se conservan.

## Actualizador acotado

`update-native.py --package RUTA` consulta el plan por defecto. `--apply` prepara el respaldo y actualiza; necesita root y una terminal interactiva para introducir una identidad SQL administrativa existente. Sus credenciales sólo viven en memoria y entorno privado del cliente SQL; nunca se solicitan en el chat ni se añaden a la cuenta cotidiana.

El programa acepta el ZIP nativo original por su hash, verifica manifiesto y rutas, contrasta configuración efectiva y de reinicio y comprueba el estado observado de releases/base. Después de autenticar SQL valida permisos antes de detener únicamente PRODIMT. Captura los enlaces y configuración privada, verifica el respaldo antes de DDL y sólo activa el candidato tras completar las migraciones. La reversión de enlaces no restaura automáticamente la base ni deshace DDL parcial; el resultado indica si el esquema puede haber cambiado y dónde quedó el respaldo.

Tras un fallo en `database-migrations` con recuperación anterior saludable, primero usar `scripts/pilot/diagnose-native-update.py --package ZIP`. Este diagnóstico sólo consulta el servicio, respaldo, staging, historial/esquema y opciones originales de sesión SQL; no autentica una cuenta administrativa ni repite DDL. Los releases nuevos ya pueden existir y la base conservar parte del esquema: el actualizador normal no es un mecanismo de reanudación. No borrar staging ni restaurar la base para saltar sus guardas; preparar la recuperación según la evidencia del diagnóstico.

El protocolo SQL corregido en código usa `-I` y opciones de sesión explícitas para índices filtrados. La prueba anterior de migraciones por SqlClient no ejercitaba los valores predeterminados del ejecutable ODBC; el workflow añade el salto cinco→nueve/repetición mediante sqlcmd y el mismo transporte de script. Tras el diagnóstico y la validación administrativa del esquema se distribuyó `resume5`, cuya ejecución satisfactoria queda registrada en la evidencia enlazada.

La preparación puede normalizar a root:www-data los cinco directorios estructurales propios para publicar con seguridad; conserva metadatos previos y los releases existentes. Si el nuevo valor predeterminado de vigencia de tokens difiere, conserva el valor efectivo anterior mediante un ajuste no secreto en el JSON del release nuevo, sin cambiar api.env, la clave JWT ni su emisor/audiencia. Los cambios de permisos SQL se limitan a DML de OrderOperations y OrderChangeNotices cuando hacen falta. No se instalan paquetes ni se cambian otros servicios.

El payload sigue siendo el código funcional b630072 validado. Ejecutar el actualizador no equivale a confirmar que terminó: revisar su JSON final y comprobar después login, configuración y pedidos desde el navegador.

### Compatibilidad de EnvironmentFile

La revisión `20261025-env2` sustituye el lector reducido inicial por un lector con estados de systemd 255: valores entre comillas de varias líneas, continuaciones, segmentos de comillas consecutivos, líneas sin asignación ignoradas y último valor de una clave repetida. No evalúa shell, expande variables ni modifica `api.env`. Mantiene la comparación de configuración crítica entre el archivo, el proceso activo y el candidato antes de cualquier preparación de releases o parada del servicio.

El resultado incluye la revisión del actualizador y contadores/estados fijos de sintaxis, nunca nombres o valores del archivo. Si un intento anterior se detuvo en `read-only-preflight` con `changes_made: false` y `migrations_may_have_changed: false`, se puede sustituir el script y volver a ejecutar con el mismo ZIP. Otros fallos requieren revisar primero la fase y los indicadores de recuperación; no borrar releases ni repetir a ciegas. No pegar el archivo privado en el chat.

Desde la revisión `20261025-tty3` se corrige la apertura del canal de prompts: `getpass` requiere un stream escribible y gestiona su propia entrada de terminal sin eco. Abrir un TTY con `r+` buffered exigía seek y fallaba aun con SSH interactivo; se usa `w` y se mantienen el rechazo sin terminal y la advertencia como error. Cinco pruebas con PTY real verifican ocultación, errores y restauración de la terminal. La revisión vigente `20261025-resume5` conserva esa corrección. Un bloqueo en `admin-authentication` con ambos indicadores de cambios falsos permite reemplazar sólo el script y repetir; no requiere revertir aplicación o base.

## Referencias

- [Despliegue de migraciones EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).
- [RESTORE VERIFYONLY y permisos](https://learn.microsoft.com/en-us/sql/t-sql/statements/restore-statements-verifyonly-transact-sql).
- [EnvironmentFile en systemd 255](https://www.freedesktop.org/software/systemd/man/255/systemd.exec.html#EnvironmentFile=).
- [Estados del lector de systemd 255](https://github.com/systemd/systemd/blob/v255/src/basic/env-file.c).

## Ejecución desde WSL

Descargar `PRODIMT_Actualizar_I04.py` junto al ZIP nativo ya entregado. Copiar ambos al VPS y ejecutar con una terminal interactiva; no introducir las credenciales administrativas en el comando ni en el chat. El usuario y la contraseña SQL se piden ocultos en el servidor. El antecedente usó la cuenta sa; la contraseña SQL es distinta de la autenticación SSH.

```bash
scp /mnt/c/Users/TUF/Downloads/PRODIMT_Actualizar_I04.py \
    /mnt/c/Users/TUF/Downloads/PRODIMT_I04_Nativo_20261024.zip \
    root@194.238.26.70:/root/ &&
ssh -t root@194.238.26.70 \
    'python3 /root/PRODIMT_Actualizar_I04.py --package /root/PRODIMT_I04_Nativo_20261024.zip --apply'
```

No envolver el último SSH en un heredoc sin reenviar `/dev/tty`: la interacción privada SQL necesita la terminal. Revisar JSON final y ruta del backup; después entrar a la aplicación y validar login/configuración. El actualizador no requiere reiniciar Nginx, SQL ni otras aplicaciones.

## Reanudación del intento SQL revisado

Después del diagnóstico que confirmó cinco migraciones, cero pedidos y la versión anterior saludable, `20261025-resume5` permite reutilizar los releases preparados exclusivamente con el snapshot revisado. La comprobación no supone que metadatos null de la cuenta habitual signifiquen ausencia: una identidad SQL con VIEW DEFINITION debe confirmar el esquema previo exacto antes de preparar archivos o detener el servicio.

El programa valida bytes/permisos/conjunto completo de staging frente al ZIP/configuración conservada y contrasta snapshot, enlaces y unidad anterior. Conserva y verifica el primer respaldo, crea otro independiente tras la parada y revalida antes de migrar. No cambia staging ni aplica una restauración de SQL. Las opciones `-I` y SET explícitas se mantienen para los índices filtrados. Los errores e interrupciones conservan la recuperación de releases bajo el mismo lock; un resultado satisfactorio requiere nueve migraciones y salud/proceso/frontend verificados.

```bash
ssh -t root@194.238.26.70 \
  'python3 /root/PRODIMT_Actualizar_I04.py --package /root/PRODIMT_I04_Nativo_20261024.zip --resume-from /var/backups/prodimt-pedidos/i04-20261025T142158Z-11ceb110bb44 --apply'
```

Usar el script actualizado distribuido tras validar la revisión. Omitir `--apply` conserva el plan de lectura; omitir `--resume-from` mantiene el rechazo normal de staging ya existente. Esta reanudación no es una autorización genérica para otros fallos o historiales.

## Restablecer acceso de administrador web

La contraseña web pertenece a `dbo.AdminUsers`, distinta de la contraseña del login SQL. `scripts/pilot/reset-native-admin.py --user admin --apply` recupera una cuenta web existente y activa en el despliegue I04 revisado. Sin `--apply` sólo describe el plan y no pide secretos.

Ejecutar en una terminal SSH interactiva con root; solicita usuario/contraseña SQL existentes de manera oculta y la nueva contraseña web dos veces (16–128 caracteres). Usa el formato Identity V3 de la aplicación, conserva una copia privada del estado anterior con hash en `/var/backups/prodimt-pedidos/admin-reset-*/administrator-before.json` y cambia únicamente PasswordHash, con comparación de la versión observada y auditoría atómica. No pegar esa copia, hashes, contraseñas ni tokens en el chat. No crea/activa cuentas, cambia contraseñas SQL, hace bootstrap ni reinicia servicios.

La verificación usa HTTPS con certificado válido contra el mismo virtual host: login y GET administrativo autenticado. Un resultado completo exige `password_reset: true`, `login_verified: true`, `login_http_status: 200` y `admin_http_status: 200`. Si ocurre un error después de enviar SQL, revisar `password_reset` y `changes_may_have_occurred`; conservar el respaldo y no repetir por suposición. No se revierte la cuenta automáticamente. Evidencia en `../../docs/validation/2026-10-25-admin-recovery.md`.
