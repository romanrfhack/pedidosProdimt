# Reanudación nativa I04 — 25 de octubre de 2026

## Evidencia observada

El diagnóstico del usuario confirma versión anterior en ambos enlaces y proceso, salud HTTP 200/base accesible, staging presente con probes coincidentes y respaldo regular de 4,872,704 bytes, uid/gid de SQL y modo 600. El historial conserva cinco migraciones exactas, cero desconocidas y cero pedidos. El cliente ODBC 17.10 abre QUOTED_IDENTIFIER y ARITHABORT desactivados; SQL tiene compatibilidad 160. La cuenta habitual carece de VIEW DEFINITION, por lo que sus valores null de catálogo no prueban ausencia de objetos.

El fallo original no conservó su mensaje SQL, pero el protocolo defectuoso fue reproducido y corregido mediante ODBC real en [CI 40203099261](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40203099261). La base real sigue pendiente de comprobación administrativa del esquema antes de una nueva aplicación.

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

Estos ensayos verifican el mecanismo, no su ejecución real en el VPS. La distribución incluye los resultados locales/remotos y se mantiene el despliegue pendiente hasta obtener el JSON `status: updated` con verificación de salud y revisar el acceso a la aplicación.
