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

Último despliegue confirmado: I06, ambos releases `catalog-965dc65d82d2`, diez migraciones. I07 está listo para instalación por el usuario; no se ejecutó SSH ni se modificó el VPS en esta sesión. No se reimportan catálogos.

El instalador requiere I06 activo y configuración vigente; pide SQL administrador por TTY privado. Respalda con COPY_ONLY/CHECKSUM y VERIFYONLY antes de la migración aditiva `20261028202031_AddCustomerCaptureAliases`. Comprueba 11 migraciones, columna nullable, índice único filtrado, permisos DML, datos preservados, salud, autorización de endpoints y todos los JS/CSS. Destino nuevo: `capture-46fed604732e`. Conserva entorno/cuentas y no cambia Nginx. Ante fallo intenta reactivar I06 sin restaurar SQL automáticamente.

HN queda sin asociación automática: se configura explícitamente por cliente y producto. Los perfiles importados se revisan/confirman una vez para captura rápida; una unidad/posición faltante se resuelve, no se infiere. Captura manual continúa disponible para productos confirmados. Ni la vista previa ni la aprobación crean pedidos.

Tras instalación correcta: Ctrl+F5, revisar captura en `/admin/clientes-pendientes`, probar un pedido real, comprobar líneas/fecha/unidades, autorizaciones y demanda. Una corrección del total de 15 a 20 sigue el pedido existente con motivo y aviso; otra entrega requiere confirmación explícita. Validación técnica no equivale al ensayo de ruta ni al registro de producción física.
