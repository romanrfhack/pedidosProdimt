# I07 — Evidencia de captura rápida e instalación nativa

Fecha: 28 de octubre de 2026. Código y herramientas: `46fed604732eddca0c7ac199b58bb0b429545b89`.
[CI completo](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40765239145): cuatro jobs correctos, sin omisiones.

## Validación

- 267 .NET: cinco dominio y 262 aplicación. Parser por posiciones explícitas, alias por cliente, cero/decimales, rechazo de ambigüedad, unidades desconocidas y productos repetidos. Vista previa sin escritura, aprobación explícita y guardado con versión/huella observadas.
- Contratos antiguos CSV/PUT conservan alias cuando se omite el campo, permiten borrado explícito y rechazan duplicados. Importación conserva aprobación pendiente.
- SQLite comprueba atomicidad de pedido, auditoría y recibo ante fallo; límites de notas corresponden a SQL (entrega 500, internas 1000).
- 29 pruebas SQL Server reales: persistencia/replay entre hosts, unidades distintas por línea, entrega heredada, rechazo tras cambio de unidad, dos capturas concurrentes con una sola entrega y rollback de auditoría/recibo seguido de reintento.
- 68 E2E con API mock: ocho de I07, incluido blur/Enter, los tres ejemplos, aprobación única, entrada desconocida/manual, respuesta de preview atrasada, respuesta de guardado perdida y reintento idéntico, perfil obsoleto, alias duplicado y móvil a 390 px. Revisadas capturas ficticias de escritorio/móvil; formulario al ancho de tarjeta y desglose en líneas.
- Angular Production: 469.14 kB inicial, dentro del presupuesto de 500 kB; origen relativo y sin credenciales demo. Auditorías npm sin vulnerabilidades.
- 162 Python: 14 guardas nuevas de paquete/instalación I07. Integridad, rutas, permisos, respaldo verificado antes de DDL, conteos conservados, esquema/índice exactos, reintento de migración completa y reversión de aplicación.
- ODBC sqlcmd real: 10→11 repetido con sesión ANSI requerida, datos/conteos conservados, rollback de transacción fallida y rechazo de DDL parcial o índice deshabilitado. Conserva pruebas anteriores 5→9 y 9→10.
- Smoke Docker Production completo correcto en una base desechable con datos ficticios: mismo origen, autenticación, SQL restringido, respaldo y persistencia después de recrear aplicación.
- SDK local 10.0.403, publicación linux-x64 dependiente de framework 10.0.0. SourceRevisionId coincide con el commit anterior. Modelo EF sin cambios pendientes.
- `graphify update .` no disponible (127).

## Paquete

`PRODIMT_Captura_Rapida_I07.zip`: 5,696,903 bytes.

- SHA-256 exterior: `131b0161735197c15a3c7b5fd715648ea2b9258deec066bae0af041cca89acae`.
- Payload: `06c1a7fd68ec997b7ba7efa92ddf65de6b35fae58dc800ed7ec2596a0741755b`.
- 62 archivos de payload comprobados: hashes, tamaños, CRC, manifest de origen y pines del instalador. Sin Excel, datos reales, credenciales ni símbolos/mapas.
- Contenido exterior: `prodimt-quick-capture-update/PRODIMT_Instalar_I07.py`, `prodimt-quick-capture-payload.zip`, `LEEME.txt` y `SHA256SUMS.txt`.
- El plan del instalador empaquetado se ejecutó sin `--apply`: planned y cero cambios.

## Destino y límites

El paquete se preparó sobre I06, ambos releases `catalog-965dc65d82d2` y diez migraciones. El usuario confirmó después su instalación I07 el 30 de octubre: ambos releases `capture-46fed604732e`, once migraciones, salud/esquema y conteos verificados. No se reimportaron catálogos ni se ejecutó SSH desde este entorno.

El instalador requiere I06 activo y configuración vigente; pide SQL administrador por TTY privado. Respalda con COPY_ONLY/CHECKSUM y VERIFYONLY antes de la migración aditiva `20261028202031_AddCustomerCaptureAliases`. Comprueba 11 migraciones, columna nullable, índice único filtrado, permisos DML, datos preservados, salud, autorización de endpoints y todos los JS/CSS. Destino nuevo: `capture-46fed604732e`. Conserva entorno/cuentas y no cambia Nginx. Ante fallo intenta reactivar I06 sin restaurar SQL automáticamente.

HN queda sin asociación automática: se configura explícitamente por cliente y producto. Los perfiles importados se revisan/confirman una vez para captura rápida; una unidad/posición faltante se resuelve, no se infiere. Captura manual continúa disponible para productos confirmados. Ni la vista previa ni la aprobación crean pedidos.

Tras instalación correcta: Ctrl+F5, revisar captura en `/admin/clientes-pendientes`, probar un pedido real, comprobar líneas/fecha/unidades, autorizaciones y demanda. Una corrección del total de 15 a 20 sigue el pedido existente con motivo y aviso; otra entrega requiere confirmación explícita. Validación técnica no equivale al ensayo de ruta ni al registro de producción física.

## I07 desplegado — 30 de octubre de 2026

Confirmado por salida del instalador ejecutado por el usuario: revision 20261028-capture-install1, status updated, phase completed, historial 10→11, migrated_schema_verified, existing_data_counts_preserved y health_verified true. Código 46fed604732eddca0c7ac199b58bb0b429545b89. Backend/frontend activos en capture-46fed604732e. Contraseñas sin cambio; no se restauró SQL automáticamente.

- Snapshot privado: `/var/backups/prodimt-pedidos/quick-capture-update-20261031T024549Z-90dbaa60d04e`.
- Respaldo SQL verificado: `/var/opt/mssql/backup/prodimt-i07-20261031T024549Z-90dbaa60d04e/ProdimtPedidos-copy-only.bak`.

El instalador verificó esquema, permisos DML, proceso, /health/db, autorización de los endpoints y frontend/JS/CSS por HTTPS. Se trata de evidencia reportada por el usuario; no se accedió al VPS desde este entorno. El campo database_checks.migrations_count=10 pertenece al preflight; migrations_after=11 y migrated_schema_verified=true confirman el resultado final.

Siguiente paso: Ctrl+F5 y revisar Pendientes → Capturar pedido. Comprobar fecha, entrega habitual informativa, texto abreviado/desglose por posiciones y alternativa manual. Perfiles importados aún requieren confirmación explícita para guardar captura rápida; HN debe asociarse al producto correcto por cliente. Revisar primero la vista previa sin crear pedidos ficticios. Instalación no equivale a aceptación de negocio ni al ensayo de ruta. Conservar catálogos y respaldos; no repetir instalador, bootstrap o importaciones.
