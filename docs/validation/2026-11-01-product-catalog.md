# I13 — Validación de catálogo agrupado

Fecha operativa: 1 de noviembre de 2026, America/Mexico_City.

## Alcance comprobado

Catálogo de Productos con tarjetas por tipo/número, búsqueda, filtros y modal de alta/edición. No cambia backend, SQL ni contratos. Preserva accesos I12, consolidación, precios e historial. Se revisó el DTO/método GET y el listado suministrado; no hubo acceso a la base ni al VPS real.

Código de aplicación: a6b7cec04977821f42a2bbb150c00cba9220179e. Fuente final de paquete: 2345c7f4c27f89b841344c6a8dfb2b3bfb196285; sólo ajusta una prueba de preflight para ejecutar sin root en CI. No relaja el guard del instalador, que sigue exigiendo directorios de root en el servidor. Rama codex/i13-responsive-products, PR #5 en borrador sobre I12 7ad9bb0. main permanece 492bbe9.

## Comprobaciones locales

- Build Angular de producción correcto: inicial 493.06kB; editor de producto bajo demanda 6.90kB. Avisos de estilo: productos6.35kB y configuración existente7.55kB superan el umbral de aviso4kB; ambos están bajo el límite de error8kB. No se elevaron presupuestos.
- Regresión de interfaz: 132 casos correctos en la ejecución completa más repetición correcta del caso de activación. Ese caso se había cargado antes de corregir la expectativa POST a PATCH; la API sigue usando PATCH sin cambios. Doce casos nuevos de Productos, 133 en total. CI ejecuta la fuente final íntegra.
- Grupos de 17 productos ficticios: 9 Por número, 3 Harina, 2 Sancochados, 1 Vapor, 2 Otros. Orden de enteros, decimales y fracciones; ingredientes no inferidos de Burrera/Flauta.
- Búsqueda sin acentos, búsqueda por descripción/tipo/unidad, filtros combinados, vacío y limpieza; no escribe por clasificar/filtrar.
- Rejilla sin desbordamiento a 320/390/768/1366/1920px: 1/1/2/4/6 columnas en el grupo de nueve productos. Medidas de ancho, modal dentro del viewport, Escape y foco verificados. Revisión visual con datos ficticios de laptop y teléfono.
- Edición por id y enlace desde configuración, unidad null conservada, error mantiene borrador, guardado bloquea cierre/envío repetido; activar/desactivar actualiza estado/filtros. Archivo consultable sin editar/reactivar.
- 305 pruebas Python correctas (incluidas 15 nuevas de I13). Se ejercitan payload fijado, rutas/hash, carga del instalador final, preflight, cambios de base rechazados, salud/HTML, snapshot, activación, interrupción, recuperación, backend que cambia e idempotencia.
- git diff --check correcto. graphify no disponible; no se declara actualización del grafo.

## Paquete entregable

PRODIMT_Catalogo_Productos_I13.zip, 271205 bytes.

SHA256: 26c27c5e1972af27321ac47efab18692e2fc39ab6a8af5e1d9c13eec30b1cd2d.

Payload: db6301283505a3156cb2728f1c071dce3bdf560ac494bc006bac96240701bf4f.

CRC de ambos ZIP, tres checksums externos, manifest/pines, carga real del instalador generado y quince archivos frontend verificados. Incluye licencias y no incluye fuentes, mapas, credenciales, datos reales o backend. Archivo verificado y guardado.

Requiere I12 instalado y saludable (access-tokens-65560ca3cf98). Al preparar el paquete ese prerrequisito aún estaba pendiente de confirmar; el reporte de instalación siguiente acredita la comprobación de ese release base. Instalador sólo frontend, sin SQL/reinicio/claves/contraseñas. No se operó el VPS desde este entorno.

## CI

[CI 41381704462](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41381704462), fuente2345c7f. Cuatro trabajos completos correctos: 335 Application/Domain, 66 SQL/criptografía, 133 E2E, 305 Python y smoke Production. La ejecución previa a6b7cec detectó que una prueba de preflight asumía directorios temporales de root; corregido exclusivamente en la prueba para el runner sin privilegios. No hubo cambio de aplicación o permisos requeridos en producción.

## Instalación confirmada por reporte del usuario

El usuario comparte preflight planned/completed sin cambios y base_release_verified/health_verified true; posteriormente aplica y obtiene updated/completed con frontend_verified/health_verified true. Fuente2345c7f, frontend product-catalog-2345c7f4c27f y snapshot /var/backups/prodimt-pedidos/product-catalog-20261102T031948Z-1db3b0cdd020. SQL, backend, reinicio y contraseñas figuran sin cambios. La salida de sólo lectura con frontend_verified=false corresponde a la fase previa y queda superada por true en la aplicación.

Evidencia aportada por el usuario, no inspección SSH independiente; no permite deducir conteos de SQL o aceptación funcional de tokens I12. No se ejecutan más pruebas, migraciones o instaladores por este registro.

## Aceptación visual pendiente

Ctrl+F5, revisar grupos y búsqueda con catálogo real y probar los modales en laptop/tableta/teléfono. Instalación I13 confirmada; aceptación visual aún sin reporte. No registrar prueba como pedido real ni repetir importaciones/consolidaciones.
