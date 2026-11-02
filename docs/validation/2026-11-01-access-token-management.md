# I12 — Gestión de accesos activos, edición y reenvío

## Autorización y alcance

Usuario autoriza revocar todos los accesos anteriores y generar los nuevos a demanda. Implementación sobre I11 confirmado prices-148a52dd7428, doce migraciones. Listado utilizable, copia explícita, edición versionada conservando token/enlace, reenvío individual preparado y revocación. Se conserva I11 y sus precios/historial/pedidos. No hay envío automático.

Hash para autenticación y copia AES-GCM bajo clave dedicada persistente fuera de releases/SQL. AAD por cliente/ID, nonce aleatorio, sin JWT fallback. Secretos sólo en respuesta administrativa de creación/reveal con no-store; sin claves en el paquete/listado/auditoría/log. Prohibidos vencidos/revocados/inactivos/cliente ajeno. Conflicto de metadata durante login devuelve401 y no emite JWT. Edición conserva hash/cifrado/ID y requiere versión esperada; acortar vigencia invalida sesiones por el validador existente.

## Evidencia local antes de CI

- Release API compila sin errores; modelo EF y migración13 coherentes. SQL incremental con columnas, revocación/auditoría e historial en la misma transacción.
- Application330 y Domain5 correctas; 14 crypto correctas, con tamper y aislamiento por key/cliente/ID.
- Python288 correctas,15 nuevas: composición real de preflight/allowlist/metadata, integridad inmutable, backup antes de clave/migración, key privada/atómica/efectiva, rechazo a drift/missing y retención al rollback, catchup sólo hash-only con auditoría.
- Angular producción correcto; advertencia previa7.55kB en CSS configuración bajo máximo8kB, sin alterar presupuestos/dependencias.
- E2E: siete nuevos correctos; suite local120/121 afectada por hot reload durante320px y ese caso correcto al repetir con fuentes congeladas. Matriz320/390/768/1366/1920, foco/borrador/busy, copia después de recarga, edición/versiones y reenvío manual. CI completo pendiente.
- Revisión independiente de cifrado, autorización, concurrencia, migración e instalador sin bloqueantes. El defaultbit de EF requiere formas CAST/CONVERT de cero; guarda exacta corregida y caso SQL real añadido.
- Seis pruebas nuevas SQL (además de14crypto) pendientes del motor real en CI: revocación única/catchup, rollback ante auditoría, concurrencia edición/revocación, recuperación entre hosts y consultas exactas de esquema/cleanup del instalador.
- graphify update . no disponible (127), sin grafo previo.

## Distribución y operación

Instalador específico I11→I12 con backup COPY_ONLY/CHECKSUM/RESTORE VERIFYONLY, snapshot privado y clave estable en api.env. API/frontend se reinician sólo al aplicar. La clave se conserva aun si revierte aplicación; SQL no se restaura automáticamente. Repetir no revoca accesos nuevos protegidos. Cualquier legado hash-only creado durante un rollback I11 se revoca/audita con el mismo predicado idempotente en un reintento respaldado.

No se accedió por SSH ni se migró/revocó/creó acceso real o envió WhatsApp desde este entorno. Paquete, CI y aceptación real pendientes al registrar.

## Cierre y paquete final

Código `65560ca3cf98f3a1cc44e7f3ef50d4276e1f3df8`, [PR #4](https://github.com/romanrfhack/pedidosProdimt/pull/4), rama codex/i12-access-token-management sobre I11. [CI final 41350276236](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41350276236) con cuatro trabajos correctos: 335 Application/Domain, 66 del proyecto SQL Server/criptografía, 121 E2E y 288 Python más smoke Production. Capturas PC/tableta/móvil e integridad del ZIP revisadas. Paquete PRODIMT_Gestion_Tokens_I12.zip, 7,304,892 bytes, SHA256 `ee52910b3d1d0be45241a4d7accb47c86f31ce6b45bbafc3b7c7369a97b676df`. Instalación y revocación remotas aún pendientes; I11 sigue siendo el último despliegue confirmado. Al aplicar I12 se revocan los accesos anteriores autorizados y se crean nuevos a demanda. No repetir I09/I10/I11 ni reparaciones/importaciones para esta actualización.

Payload SHA256 `6ba05d78c0a4dfe33cebb18413c879ce0822bf4a683f9a9b3b6222a68a148f26`, 68 archivos. ZIP de origen limpio con fechas reproducibles, backend framework-dependent .NET10 y AssemblyInformationalVersion igual al commit, frontend de producción con avisos de terceros y SQL incremental verificado. Sin host/runtime incluido, secretos, configuración productiva, mapas o PDB. Se cargaron load_payload y make_installer del script exportado con el payload exportado real; verificaciones correctas sin preflight/SSH o generación de claves.

Los 66 casos del proyecto SQL incluyen los 14 de criptografía y seis SQL nuevos con motor real. CI final comprueba esquema y predicado exactos del instalador, revocación auditada de todos los anteriores, repetición sin revocar nuevos protegidos, rollback ante auditoría fallida, CAS edición/revocación y recuperación entre hosts. Los 121 E2E incluyen siete nuevos y regresión completa de precios/productos/captura.

Los primeros dos CI sólo requirieron corregir infraestructura de pruebas: comparación Guid textual por identidad y ejecución literal de SQL con JSON en un helper. La sincronización del formulario de contraseña conserva la igualdad estricta del request y verificó10 repeticiones locales correctas. El código productivo del primer commit se conserva; las revisiones siguientes sólo ajustan pruebas. Capturas iniciales/historial activo/enlace creado revisadas en PC y móvil; botones ajustan sin desbordamiento, cierre visible y desplazamiento interno.

Comando remoto, desde los dos archivos ya copiados: `ssh -t root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I12.py --package /root/prodimt-access-token-management-payload.zip --apply'`. SQL admin se captura por TTY oculta. Resultado esperado updated/completed, migrations_after=13, legacy_tokens_verified/encryption_key_effective_verified/health_verified/frontend_verified true y número de legacy_tokens_revoked. Conservar snapshot privado y clave con el respaldo SQL. Tras aplicar, Ctrl+F5 y crear accesos nuevos; comprobar copia tras recargar, edición/reenvío del mismo enlace y revocación. No declarar operación de pedidos o envío WhatsApp a partir de la salud del instalador.

## 2026-11-01 — I12: preflight bloqueado por identidad de api.env

El usuario verificó el ZIP I12 y ejecutó la revisión `20261101-access-token-management-install1`. El instalador terminó `failed/read-only-preflight`, paso `running-service`, con `environment_file_identity_unexpected`. Todos los indicadores de cambios, migraciones, clave y reinicio permanecen en false. No se aplicó I12 ni la revocación autorizada; I11 sigue siendo el último despliegue confirmado. `health_verified=false` sólo indica que no se alcanzó esa comprobación.

La guarda de `/etc/prodimt-pedidos/api.env` exige archivo regular, root, un enlace, modo exacto `0600` y ruta canónica. La cadena anterior admitía `0640` para root:www-data; es una incompatibilidad posible, no una causa remota demostrada. Revisión independiente del instalador exportado confirma el orden: falla antes de leer configuración, consultar SQL o generar la clave. Siguiente paso: consultar únicamente lstat/modo y booleans de identidad/ruta, sin contenido. No cambiar permisos, relajar la guarda o repetir --apply hasta identificar la condición real. Paquete original conservado; sin SSH desde este entorno.

## 2026-11-01 — I12: diagnóstico confirma permiso heredado 0640

Salida suministrada por el usuario: regular_file, root_owned, www_data_group, single_link y canonical_path todos true; mode=0640. Se aísla el rechazo exacto de la guarda I12: modo0600 requerido frente al0640 admitido previamente. No hay evidencia de alteración de configuración ni fallo de SQL; el intento no llegó a leer api.env, crear clave, migrar o revocar.

Se indica chmod0600 sobre el archivo conocido y reejecutar el instalador/payload originales con --apply y TTY para la credencial SQL existente. Systemd carga EnvironmentFile para el servicio nativo antes del proceso www-data. Sin cambios de contenido, propietario, contraseñas, unidad o payload; ningún reinicio adicional fuera del flujo respaldado del instalador. No se relaja la guarda privada de I12. No se accedió al VPS ni se confirma aplicación de esta instrucción; I11 sigue desplegado según la última evidencia. Registro documental, diff comprobado; no cambios de código ni nuevas suites necesarias.

## 2026-11-01 — I12: SQL207 después de respaldo y clave

Usuario aplicó modo0600 y ejecutó --apply: preflight completo sin discrepancias; respaldo SQL y snapshot de clave verificados, clave creada sin rotación. Fallo SQL207 en database-backup-key-and-migration. Rollback de aplicación con previous_health_verified y encryption_key_retained_after_rollback true; SQL no restaurado y sin cambio de contraseña. Snapshot reportado access-token-management-update-20261102T023700Z-c1d1890da942; respaldo SQL conservado bajo prodimt-i12 con el mismo sufijo. Flags de intento de migración no confirman commit o revocación; próximo preflight sólo lectura determinará esquema12/13 compatible y bloqueará drift parcial.

SQL exportado exacto SHA256 7788b05db38a2cd78b2fbda67bcfd4924e9af99aab3f5b6b9ee7915cce340efe: un batch con tres ALTER ADD y UPDATE de columnas recién agregadas, causante de compilación207 sobre I11. EF MigrateAsync separa comandos y las pruebas previas no ejercían ese batch de sqlcmd. Se prepara instalador revisión2 con GO antes del cuarto IF; transacción y COMMIT único se conservan, sin regenerar clave ni recompilar aplicación o payload. Regresión real del script original/corregido añadida a CI para documentar el defecto y la atomicidad. Sin SSH desde este entorno.

Corrección local: instalador revisión `20261101-access-token-management-install2`, 17 pruebas específicas y 290 Python completas correctas. Exportación independiente `PRODIMT_Instalar_I12_v2.py`, 38,208 bytes, SHA256 `a289e0615dfa7480db31cca55d721a187889390648df5e80ed4991e00b627a12`; pines del payload original `6ba05d78c0a4dfe33cebb18413c879ce0822bf4a683f9a9b3b6222a68a148f26` y aplicación `65560ca3cf98f3a1cc44e7f3ef50d4276e1f3df8` conservados. Se cargan load_payload/make_installer exportados contra los 68 archivos originales; sólo se añaden cuatro bytes GO/separación al SQL en memoria, sin alterar el ZIP. SQL corregido SHA256 `0fc3fc28b218b9716a93e5211960b85ee04836864270eb7abcba6ebb73193bce`. Regresión ODBC real/CI y aplicación remota pendientes. graphify update . no disponible (127), sin grafo previo.

## 2026-11-01 — Cierre de corrección SQL207: instalador v2

Corrección validada: instalador revisión `20261101-access-token-management-install2`, fuente `b8588ab0afb6be059bb8f29091d1586c35185069`. [CI 41379682207](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41379682207) completo con cuatro trabajos correctos: 335 Application/Domain, 66 SQL/criptografía, 121 E2E y 290 Python más smoke Production. Regresión ODBC real ejecuta el SQL exacto del ZIP: reproduce207 sin cambiar I11, verifica rollback de DDL/revocación/historial ante auditoría fallida y éxito12→13 con repetición que conserva accesos protegidos nuevos. No modifica aplicación ni payload.

Entrega independiente verificada y guardada: PRODIMT_Instalar_I12_v2.py, 38,208 bytes, SHA256 `a289e0615dfa7480db31cca55d721a187889390648df5e80ed4991e00b627a12`. Reutiliza el payload original ya subido, SHA256 `6ba05d78c0a4dfe33cebb18413c879ce0822bf4a683f9a9b3b6222a68a148f26`, y binarios fuente65560ca. Ejecutar v2 primero sólo lectura y continuar --apply únicamente si termina correctamente; ambos recorridos conservan y verifican la clave existente. El preflight determina el esquema remoto12/13 y rechaza estados parciales. No repetir v1, regenerar clave, aplicar DDL manual o restaurar SQL por este incidente. No se operó el VPS desde este entorno; instalación I12, revocación efectiva y aceptación siguen pendientes del reporte del usuario.
