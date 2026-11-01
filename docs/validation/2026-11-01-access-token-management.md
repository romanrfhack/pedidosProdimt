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
