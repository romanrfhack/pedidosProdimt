# Reanudación nativa I04 — 25 de octubre de 2026

## Evidencia observada

El diagnóstico del usuario confirma versión anterior en ambos enlaces y proceso, salud HTTP 200/base accesible, staging presente con probes coincidentes y respaldo regular de 4,872,704 bytes, uid/gid de SQL y modo 600. El historial conserva cinco migraciones exactas, cero desconocidas y cero pedidos. El cliente ODBC 17.10 abre QUOTED_IDENTIFIER y ARITHABORT desactivados; SQL tiene compatibilidad 160. La cuenta habitual carece de VIEW DEFINITION, por lo que sus valores null de catálogo no prueban ausencia de objetos.

El fallo original no conservó su mensaje SQL, pero el protocolo defectuoso fue reproducido y corregido mediante ODBC real en [CI 40203099261](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40203099261). Esta era la evidencia previa a la reanudación; su ejecución satisfactoria posterior se registra al final.

## Contrato de la revisión resume5

- `--resume-from` acepta exclusivamente el snapshot del intento revisado. La ejecución normal conserva sus guardas de releases inexistentes.
- Verifica directorio privado y archivos root, modos exactos, archivos regulares/sin links y conjunto esperado; exige resultado previo fallido en migración con recuperación anterior saludable, hash del paquete, enlaces, rutas de respaldo y estados esperados. No reutiliza credenciales administrativas guardadas.
- Compara env y unidad estática. Normaliza ExecStart a argumentos, porque su PID/timestamps cambiaron al recuperar el proceso anterior; la comprobación actual antes de parar sigue detectando cambios durante la ejecución.
- Compara todos los archivos/directorios de staging con paquete/configuración efectiva: bytes, conjunto, propietario, modos y ausencia de symlinks/hardlinks. No crea ni sobreescribe releases en este modo; vuelve a comprobarlos antes de parar y activar.
- Antes de escribir exige identidad administrativa y cinco migraciones exactas, VIEW DEFINITION completo, 13 tablas previas, ausencia de las 23 columnas nuevas y de tablas/índices/FK nuevos, índice Orders previo no único/sin filtro y auditoría nullable nvarchar(2000). Una discrepancia bloquea; no ejecuta reparaciones DDL inferidas.
- Verifica el primer respaldo con VERIFYONLY/CHECKSUM y lo conserva. Después crea snapshot nuevo, detiene sólo PRODIMT, verifica otra vez el esquema y realiza un respaldo nuevo/independiente antes de DDL. Revalida esquema y staging antes de migrar con `-I` y las siete opciones de sesión explícitas.
- Sólo cambia enlaces tras verificar las nueve migraciones y permisos DML limitados de la aplicación. Conserva verificación de proceso, base, endpoints protegidos y HTML/JS/CSS HTTPS, así como recuperación de aplicación bajo el mismo lock. Nunca restaura automáticamente la base.

## Verificación

Las pruebas de filesystem/lifecycle cubren procedencia, cambios de configuración/staging, bloqueo antes de escribir/detener, preservación del respaldo anterior, creación de respaldo fresco, orden de migración/activación y recuperación. La prueba de CLI real reproduce el script tty3 completo fallando con error 1934 y confirma rollback al esquema de cinco migraciones; contrasta restos parciales, índice ausente, auditoría modificada, metadatos restringidos y el esquema final de nueve migraciones. Todos los datos son ficticios en una base desechable.

Comprobación local: **108/108 pruebas Python correctas**, incluidas catorce de reanudación, cinco con PTY real y oráculo systemd, sin omisiones. `git diff --check` correcto. Revisión independiente del flujo y esquema; la prueba ampliada de ODBC real se ejecuta en el job SQL del nuevo workflow. `graphify update .` intentado, herramienta no disponible (127).

La [corrida SQL real de 85bc5c9](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40205493674) confirmó rollback del script original completo a cinco migraciones, rechazo de restos DDL/metadatos restringidos e historial nueve, y posterior migración/repetición correctas. SQL, .NET y navegador pasaron; el job Python detectó dos fixtures de preflight que necesitaban simular root cuando el runner no es privilegiado. Se corrigen sólo esas fixtures, manteniendo la guarda productiva; las catorce pruebas de reanudación pasan también con geteuid global no privilegiado y las dos simulaciones internas acotadas. El script distribuido conserva los mismos bytes.

La [corrida final 40205783899](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40205783899) del commit `dd83eba0a81f27850e8af63c05957e3f4e6357f4` cerró los cuatro jobs correctamente: 108 Python, 21 SQL, 203 .NET, 53 E2E, protocolo ODBC real y smoke Production con persistencia/respaldo. Las fixtures corregidas pasan en el runner sin privilegios; el script distribuido conserva sus bytes.

## Ejecución real completada

El usuario ejecutó el script con SHA256 `cd3489971c335b86e16cc2c29415dc4ff83e27b69171bf4d53a3d96217b7cd48` y el ZIP original, usando el snapshot revisado. Compartió el JSON final `updater_revision: 20261025-resume5`, `status: updated` y `phase: completed` el 25 de octubre de 2026. No se obtuvo acceso SSH directo desde este entorno; la evidencia remota es esa ejecución del usuario.

- Confirmados origen de recuperación, staging completo, salud previa y esquema administrativo anterior; el respaldo anterior fue reverificado y conservado.
- Snapshot nuevo: `/var/backups/prodimt-pedidos/i04-20261025T160429Z-1f5a37e4cab5`.
- Respaldo nuevo COPY_ONLY/CHECKSUM verificado: `/var/opt/mssql/backup/prodimt-i04-20261025T160429Z-1f5a37e4cab5/ProdimtPedidos-copy-only.bak`.
- Migraciones 5→9, esquema migrado y permisos DML de las tablas nuevas verificados. Ambos releases activos: `20261024-i04-b630072`.
- `env_preserved: true`, `health_verified: true`, `database_automatically_restored: false`. La revisión de salud exige proceso candidato, base accesible, endpoints nuevos protegidos y HTML/JS/CSS coincidentes por HTTPS. `health_used_https_proxy: false` sólo describe la ruta de la consulta inicial a `/health/db`; no omite la comprobación HTTPS posterior.

El despliegue técnico queda completado. Faltan login desde el navegador del usuario, conciliación/vista previa de los catálogos privados y ensayo operativo. No se importó la ruta del Excel por ejecutar el actualizador y no se deben repetir actualización, bootstrap ni restauración de base para continuar esos pasos.
