# C1 R5: preservar el perfil de permisos I23 confirmado

4 de noviembre de 2026. Base recuperada exclusivamente del bundle R4 local:
`4080152f4fd3ba25f94f3ec3e03bd929feda9b3d`, rama `codex/consolidacion-c1`.
Se usa un clon aislado; las copias R4 y el trabajo local anterior permanecen íntegros.
No se publica a GitHub. **C1 sigue sin instalación confirmada.**

## Evidencia R4 y causa

ZIP R4 validado: SHA-256
`a8abd7a36ec0af5d81d7e89f51e70d446034549692dfcf696ccc81dab9f4df22`.
Se verificaron SHA256SUMS, bundle completo/cabeza y los 557 archivos del manifiesto
del payload `db3fbd7e02343d589c763ae35357e9ce96eed69310d8f10a3a6001bdbaa87aba`.

Primer navegador R4: 240/240, sin fallos, omisiones ni inestables; reporte
`db0b8fcd5d01e2ef89dd9a86ed33200f801116adcc97a941ffa83f421b3affc0`.
El wrapper repitió correctamente ese SHA en `20261104T182408Z-51649`:
240/240; reporte leído y verificado de ese intento:
`46de425f49dc2eaf1b09a6a136dbce9ba7ff2f2e90b73bc3be4b597d178a7241`.
Hubo un cierre SSH inicial por demora en introducir la contraseña; después se
transfirieron los tres archivos al100%. El preflight final del usuario devolvió
`failed/read-only-preflight/audit_permissions_unexpected`, revisión
`20261104-consolidation-c1-install1`, `changes_made=false`,
`database_migrations_run=false`, `service_restarted=false`.

Diagnóstico de lectura confirmado por el usuario con la identidad efectiva:

```json
{
  "source_commit": "4080152f4fd3ba25f94f3ec3e03bd929feda9b3d",
  "mode": "read-only",
  "audit_permissions_expected": {"SELECT": 1, "INSERT": 1, "UPDATE": 0, "DELETE": 0},
  "audit_permissions_actual": {"SELECT": 1, "INSERT": 1, "UPDATE": 1, "DELETE": 1},
  "migrations_before": 18,
  "current_releases": {
    "backend": "ordering-schedules-i23-aa802e210d15",
    "frontend": "ordering-schedules-i23-aa802e210d15"
  },
  "changes_made": false
}
```

R4 exigía1100 en `validate-consolidation-c1-sql.py` y lo reutilizaba en el
instalador; su fixture añadía `DENY UPDATE,DELETE` a AuditLogs. I23 delegaba en
`commercial.PERMISSION_QUERY`/`permission_mismatches`, que exigían INSERT para
AuditLogs y comprobaban identidad/BD/no propietario/no sysadmin. No garantizaba
esos DENY. La fixture histórica de Horarios usa db_datareader/db_datawriter.
ExpandAuditMetadata sólo amplía MetadataJson y añade la migración19: no cambia
permisos. El endurecimiento homogéneo F24 pertenece a una fase posterior del plan.
Cambiar permisos productivos para que coincidieran con la fixture sería incorrecto.

## Contrato y recuperación corregidos

Se acepta exclusivamente I23 exacto con las 18 migraciones/release ya fijados y
AuditLogs1111. Cada bit debe ser `int`, no bool/string/float; faltantes, extras,
NULL y los otros15 vectores se rechazan. Los controles de servidor, BD, login,
identidad SQL efectiva y privilegios, esquema, historia y otros permisos permanecen.
Coincidir en1111 no permite una base distinta ni una cuenta administrativa.

El preflight captura una tupla inmutable de la observación anterior a cambios.
Antes de stop/DDL se publica de forma exclusiva, atómica y sincronizada en
`audit-permissions.json` dentro del snapshot privado existente: root/0600,
directorio0700, sin symlinks ni hard links adicionales. Incluye fuente, payload,
base I23, historia y releases. El marcador sincronizado incluye perfil y hash
de la referencia. Referencias ausentes, inválidas, alteradas o discrepantes
bloquean sin sobrescribir evidencia ni recapturar el estado actual.

Se compara la referencia y el perfil efectivo después de stage, antes de DDL
(también después del respaldo), después de migrar, en salud y recuperación.
1111→1100 sigue siendo deriva aunque reduzca privilegios. Con checkpoint de
migración verificada se conserva la comprobación de permisos, pero no se repite
DDL ni se compara contra datos anteriores a la activación que ya pudo recibir
escrituras legítimas. Sin esa referencia no se reconstruye un baseline desde19.

El rollback anterior a DDL exige esquema18 y permisos iguales a la referencia
durable. Ante deriva o lectura imposible conserva marcador/bloqueo y exige
recuperación manual; no reinicia afirmando salud. Después de DDL sólo se permite
recuperación hacia delante con exactamente el mismo paquete, sin restauración
automática/downgrade/instalación I23. `already_current` exige fuente/binarios
propios, esquema19, perfil1111 y salud; no inventa comparación histórica ni
crea recibos genéricos o solicita otra migración/reinicio.

La fixture SQL UUID reproduce1111 con los roles históricos, sin el DENY añadido.
Captura permisos bajo la identidad de aplicación antes de cualquier migración y
los compara después del rollback transaccional, upgrade, downgrade rechazado,
downgrade seguro y replay. Conserva Unicode extenso, las33 tablas y limpieza.
El instalador exige el resultado completo del gate, incluido perfil capturado,
en vez de aceptar sólo `status=passed`. Portátil7/7, COPY_ONLY/CHECKSUM,
RESTORE VERIFYONLY y el resto de guardas permanecen obligatorios.

**AuditLogs conserva UPDATE/DELETE históricos. No queda append-only. F24 permanece separado.**

## Validación por entorno

| Entorno / control | Resultado observado antes del commit R5 |
|---|---|
| Regresión nueva sobre código R4 | Reproduce `audit_permissions_unexpected` con1111 independiente de la constante |
| Python C1, instalador/paquete/SQL/resume | 74 casos correctos, cero fallos u omisiones; SQL y sistema simulados |
| Python completo | 607 casos:604 correctos, tres omisiones históricas root, cero fallos |
| Shell del wrapper | `bash -n` correcto; resume verificado con transferencias simuladas, nunca VPS |
| Fuentes/binarios | 471 blobs relevantes iguales a R4; manifiesto557/557 verificado; reutilización declarada, sin recompilar |
| SQL Server real local | No ejecutado: sin sqlcmd, SQL Server o Docker local aprobado disponible |
| Navegador R5 | Debe ejecutarse después de fijar el SHA limpio; evidencia externa del wrapper |
| SSH del agente | `socket: Operation not permitted`; conexión no iniciada, sin autenticar ni cambiar VPS |
| VPS R5, portátil7/7/SQL/respaldo/DDL/salud | No ejecutados; requieren el wrapper R5 y acceso local al destino |

Las omisiones Python corresponden a tres fixtures históricas de instalación I19
que requieren root; no se deshabilitan controles de propietario productivos.
Las regresiones recorren los vectores inválidos, pérdida SELECT/INSERT, tipos,
deriva en las fronteras, recuperación con escrituras nuevas, referencia perdida,
discrepancia marcador/snapshot, rollback y already_current. El fake de auditoría
modela1111 por valores independientes y modifica bits explícitos; la regresión
principal ejecuta el control real `app_permissions`.

Las fuentes de backend, frontend, validador .NET y migraciones no cambian.
Se reutilizan exclusivamente archivos comprobados del payload R4; el constructor
vuelve a comprobar configuración de fuente, igualdad de assemblies y entradas.
`evidencias-r5/trazabilidad-entradas.json` externo enumera hashes de entradas y
blobs, y declara `recompiled=false`. El constructor genera bundle, pines, hashes
y payload nuevos; no se editan pines a mano ni se mezcla con el wrapper R4.

## Cierre y continuación

El wrapper R5 debe pasar240/240 sobre el nuevo SHA limpio antes de transferir.
Si sólo se corta SCP/SSH después del pase, `--resume-from-transfer` exige resultados
dentro del mismo paquete, copia igual de SHA256SUMS, fuente y reporte completo
con hash/conteos exactos. No repite navegador sin motivo y mantiene todos los
controles remotos. Un gate R4 no puede continuar R5.

Los resultados posteriores al commit, SHA de fuente/ZIP/bundle/reportes y el
comando absoluto se guardan fuera del checkout en los reportes para ChatGPT.
Un bloqueo se registra como pendiente, nunca como desplegado. La instalación
requiere `updated/completed`, fuente R5,19 migraciones, esquema y preservación
comprobados, salud/backend/frontend exactos y `recovery_pending=false`.
La aceptación operativa humana sigue separada; no generar pedidos reales.
