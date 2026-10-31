# Diagnóstico tras fallo de migración nativa — 25 de octubre de 2026

## Hechos recibidos del usuario

El intento `20261025-tty3` autenticó la cuenta SQL, preparó snapshots/releases, detuvo PRODIMT y terminó el respaldo COPY_ONLY/CHECKSUM y VERIFYONLY. Falló al ejecutar migraciones con `sql_operation_failed`. El esquema puede haber cambiado; no se restauró automáticamente SQL. La recuperación informó ambos enlaces anteriores restaurados, arranque y salud anterior verificados y cero errores.

La versión I04 no se activó. El staging y respaldo se conservan. Falta consultar el historial y metadatos actuales antes de elegir una reanudación. Los mensajes libres del cliente SQL se descartaron en el programa original; el JSON no permite identificar el número de error SQL.

## Hipótesis fundamentada y corrección preparada

ODBC sqlcmd inicia QUOTED_IDENTIFIER desactivado salvo `-I`. La primera migración nueva crea índices filtrados que requieren esa opción activada. Las pruebas SQL anteriores aplicaban el script usando SqlClient, con opciones distintas, y la prueba real de sqlcmd sólo hacía SELECT. Es una diferencia concreta de protocolo, pero no acredita por sí sola el mensaje original del servidor.

Se añade `-I` y un prologue que fija ANSI_NULLS, ANSI_PADDING, ANSI_WARNINGS, ARITHABORT, CONCAT_NULL_YIELDS_NULL y QUOTED_IDENTIFIER activados, NUMERIC_ROUNDABORT desactivado, además de XACT_ABORT y lock timeout. Los errores futuros sólo conservan exit code y números de cabeceras SQL, sin mensajes, hosts, valores o credenciales. Esta corrección no habilita saltarse guardas ni borrar releases existentes.

## Diagnóstico de sólo lectura

`scripts/pilot/diagnose-native-update.py` acepta el ZIP original pinneado por SHA256; sólo después carga el inspector confiable en memoria. No usa el actualizador mutable, pide credenciales, escribe archivos, detiene servicios, ejecuta DDL ni restaura bases. Reutiliza la conexión cotidiana del proceso activo exclusivamente en memoria y entorno limpio.

Muestra opciones de sesión del cliente original sin `-I`, compatibilidad/versión, historial conocido de migraciones y metadatos de columnas/índices, salud, presencia de staging y metadatos del respaldo identificado. Las propiedades SESSIONPROPERTY se convierten de sql_variant a int antes de JSON. Un valor null de catálogo puede indicar falta de visibilidad; no demuestra ausencia. Los prefijos de cinco a nueve migraciones no autorizan reintento automático. Los errores inesperados se convierten a códigos fijos y conservan metadatos parciales ya obtenidos.

## Validación

- Doce pruebas del diagnóstico correctas, incluyendo prefijos parciales/desconocidos, SQL fijo de sólo consulta, aislamiento de entorno/credenciales, ZIP exacto, rechazo de sentencias arbitrarias y redacción de errores. Revisión independiente sin bloqueadores.
- Suite local Python: 94/94 correctas; cinco pruebas de terminal reales y oráculo systemd ejecutados, sin omisiones. Diff correcto.
- Nueva prueba real por ODBC sqlcmd en CI: base aislada con GUID, contraste sin `-I` y error de índice filtrado, aplicación real cinco→nueve con scripts generados y el mismo stdin/GO/prologue, repetición, preservación de datos ficticios y limpieza de la base desechable. La ejecución remota del nuevo workflow debe verificarse antes de considerar probado ese cierre.

Referencias primarias: [opción -I de sqlcmd](https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility) y [opciones requeridas para índices filtrados](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-index-transact-sql).

Ninguna prueba CI usa la base ni los datos reales del VPS. El próximo resultado necesario es el JSON del diagnóstico de lectura; el nuevo despliegue sigue pendiente.
