# Compatibilidad de EnvironmentFile — 25 de octubre de 2026

## Fallo observado

El usuario copió el actualizador y el ZIP nativo al VPS y ejecutó `--apply`. El JSON terminó con `status: failed`, `phase: read-only-preflight`, `issue: environment_file_format_unsupported`, `changes_made: false` y `migrations_may_have_changed: false`. No llegó a autenticación administrativa SQL, respaldo, migraciones, preparación de releases ni parada del servicio. La lista `structure_adoption` era un plan de normalización; no indica permisos aplicados.

## Corrección

El lector anterior recorría líneas físicas y rechazaba construcciones admitidas por systemd. La revisión `20261025-env2` recorre caracteres con estados de lectura basados en systemd 255: comillas y continuaciones multilínea, transición después de cerrar comillas, CR/LF, espacios internos y escapes. Ignora líneas sin asignación y nombres inválidos como el entorno final de systemd, y conserva el último valor repetido.

El lector no ejecuta shell ni expande referencias, comandos o sustituciones. Las credenciales permanecen en memoria y el archivo privado permanece intacto. El JSON sólo añade contadores, estados fijos y, ante rechazo, motivo fijo/número de línea. Se mantiene la comparación de configuración crítica del proceso activo, de reinicio y del candidato antes de cualquier escritura del despliegue. Se identifican las revisiones del script en el resultado para distinguir descargas anteriores.

El código funcional, las migraciones y el ZIP nativo original no cambian. El mensaje de error anterior no identifica qué construcción concreta contenía el archivo del servidor; esa causa específica no se afirma como comprobada ni se solicita su contenido.

## Validación y límites

- **76/76 pruebas Python locales correctas**, incluidas 46 del actualizador, sin omisiones. `git diff --check` correcto.
- 22 fixtures ficticios contrastados con `load_env_file` de la biblioteca real systemd 255 local, sin crear unidades transitorias ni leer archivos operativos. La prueba diferencial es opcional cuando esa biblioteca no está disponible; las regresiones de casos esperados siempre se ejecutan.
- Dos pruebas de integración usan el lector de configuración real: JWT multilínea, conexión SQL continuada y clave repetida pasan con configuración coincidente; un JWT de reinicio distinto bloquea antes de prompt, preparación, parada, migración o activación, con los indicadores de cambios falsos.
- Revisión independiente sin bloqueadores, con otros 12,000 casos ficticios aleatorios comparados contra systemd 255 sin diferencias. El conjunto de pruebas de ciclo mantiene los bloqueos antes de parada, la verificación del respaldo antes de DDL y la recuperación de releases.
- [CI de la corrección a8df45e](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40199146344): 76 Python correctas con oráculo real sin omitir, smoke Production y respaldo/persistencia correctos, 21 SQL reales y 203 .NET correctas. Angular compilación/auditorías correctas; E2E 52/53 porque el test de fecha elegida esperaba la etiqueta de otro día justo cuando su fecha fija pasó a ser hoy. Se fija el reloj sólo de ese test al 24 de octubre conservando la selección del 25 y todas sus aserciones. El producto muestra correctamente su etiqueta de hoy; no se cambia la aplicación ni el actualizador. La corrección de esa fixture debe confirmarse en la nueva ejecución remota.

La comprobación local no demuestra que la actualización haya terminado en el VPS. Hace falta el JSON de la nueva ejecución y la revisión de la aplicación después de obtener `status: updated`. No se ha restaurado ni modificado una base real desde este entorno.

Referencias primarias: [systemd.exec 255](https://www.freedesktop.org/software/systemd/man/255/systemd.exec.html#EnvironmentFile=) y [env-file.c v255](https://github.com/systemd/systemd/blob/v255/src/basic/env-file.c).
