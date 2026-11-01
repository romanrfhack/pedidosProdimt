# I12 — Gestión de accesos activos, edición y reenvío

## Autorización y alcance

Usuario autoriza revocar todos los accesos anteriores y generar los nuevos a demanda. Implementación sobre I11 confirmado prices-148a52dd7428, doce migraciones. Listado utilizable, copia explícita, edición versionada conservando token/enlace, reenvío individual preparado y revocación. Se conserva I11 y sus precios/historial/pedidos. No hay envío automático.

Hash para autenticación y copia AES-GCM bajo clave dedicada persistente fuera de releases/SQL. AAD por cliente/ID, nonce aleatorio, sin JWT fallback. Secretos sólo en respuesta administrativa de creación/reveal con no-store; sin claves en el paquete/listado/auditoría/log. Prohibidos vencidos/revocados/inactivos/cliente ajeno. Conflicto de metadata durante login devuelve401 y no emite JWT. Edición conserva hash/cifrado/ID y requiere versión esperada; acortar vigencia invalida sesiones por el validador existente.

## Evidencia local

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
