# I15 — Catálogo de clientes responsivo

Fecha: 2 de noviembre de 2026. Rama `codex/i15-responsive-customers` desde `59a03197fdff7b9425afc9b2f31b53e82bdd8dc3`, con instalación I14 confirmada por reporte del usuario.

## Cambio

Catálogo de clientes sin límite de 860px, márgenes adaptables y rejilla automática con mínimo legible de 280px. Tarjetas compactas con datos completos, acciones al pie y estados existentes; búsqueda, filtros, refresco y modales conservados. Título/alta y buscador comparten fila en PC; controles apilados en móvil. Contador de resultados, limpiar búsqueda y limpiar filtros. Todo acotado a `.customers-page`; configuración enfocada y catálogo de productos mantienen su comportamiento.

No cambian contratos, backend, datos, reglas del servidor, perfiles, descansos, tarifas, accesos o mensaje I14. Las únicas acciones añadidas limpian filtros locales. No se consultó la base real ni se operó el VPS.

## Validación completada

- Build Angular local correcto sin cambiar dependencias o presupuestos. Advertencias de estilos existentes/de catálogo, dentro de 8kB por archivo.
- Pruebas de distribución y funcionalidad ejecutadas localmente con Chromium 153 aislado en scratch, con configuración externa y sin modificar el lockfile. CI usará el navegador de Playwright del proyecto.
- Inspección visual local de capturas ficticias; se corrigió alineación de metadatos y superposición de cabecera por especificidad de CSS encapsulado. Se añadió comprobación de que cabecera y buscador no se superponen y que las tarjetas empiezan antes de 450px en 1366×768.
- Quince pruebas del instalador/paquete I15 correctas: pines, sólo frontend, base I14, rechazo de frontends anteriores, lectura sin cambios, rollback, backend cambiado, idempotencia y errores sin datos privados.
- 335 pruebas Python locales correctas. Nueve E2E de I15 correctos en el navegador local; los siete anchos dan 1/1/2/3/4/6/8 columnas, sin desbordamiento, manteniendo filtros al redimensionar. Cabecera sin superposición y tarjetas antes de 450px en 1366×768. Capturas PC/móvil revisadas.
- `graphify update .` no disponible (herramienta no instalada), sin grafo previo.
- [CI 41466208422](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41466208422) completo sobre `afb8f3ad9c33a8960d3874c14d462bb8dd0c646d`: cuatro trabajos correctos, 335 Domain/Application, 66 SQL/criptografía, 143 E2E y 335 Python más smoke Production. La suite de navegador usa el Chromium del lockfile; regresión de configuración, productos, precios, captura y accesos correcta.

## Paquete y persistencia

Aplicación/paquete `afb8f3ad9c33a8960d3874c14d462bb8dd0c646d`, [PR #7](https://github.com/romanrfhack/pedidosProdimt/pull/7). Los commits documentales posteriores no cambian la aplicación o el pin. `PRODIMT_Catalogo_Clientes_I15.zip`, 199173 bytes, SHA256 `9cf6850090db8bd21f79675b9cdd5cb5e6a7fde6ac9beb0cbde3bfafeae08905`; payload SHA256 `b6c7201039790358682e24ddb91c84b9bda0e998e6e3c721fcbe5b0e6e42f103`. Verificado y guardado.

Once archivos activos frontend/licencias. Se preparó staging con la clausura de imports del HTML/main/lazy chunks y avisos de terceros, excluyendo únicamente JS/CSS de builds previos sin referencias. CRC de ZIP exterior/interior, SHA256SUMS, manifiesto/pines, dependencias relativas y carga de `load_payload` del instalador exportado contra el payload final correctos. Mensaje I14 presente y leyendas retiradas ausentes del JS entregado. No se empaquetan backend, fuentes, mapas, SQL o credenciales.

## Instalación prevista

Frontend requerido `reminder-message-768230826a3e`, backend conservado `access-tokens-65560ca3cf98`. Nuevo frontend `customer-catalog-{source12}`. Preflight de lectura debe dar `planned/completed`; después aplicar y comprobar `updated/completed`, salud/frontend true, sin reinicio o SQL directo. Tras éxito: Ctrl+F5 → Catálogos → Clientes, probar anchos/búsqueda/filtros/modales y revisar configuración, Productos y Tokens I14.

Release esperado `customer-catalog-afb8f3ad9c33`. No se operó VPS/SQL, se instaló I15 o se confirmó aceptación funcional remota desde este entorno. La instalación I14 reportada por el usuario sigue siendo la base vigente hasta recibir la aplicación I15 correcta.

## Instalación confirmada — 2 de noviembre de 2026

El usuario proporciona SHA256 del ZIP correcto y JSON de `20261102-customer-catalog-install1`:

- Lectura: `planned/completed`, `changes_made=false`, `base_release_verified=true`, `health_verified=true`; `frontend_verified=false` corresponde a esa fase anterior a activar.
- Aplicación: `updated/completed`, `changes_made=true`, `base_release_verified=true`, `frontend_verified=true` y `health_verified=true`.
- `database_migrations_run`, `database_queries_run`, `backend_changed`, `service_restarted`, `password_changed` y `secret_values_printed` en false.
- `source_commit`: `afb8f3ad9c33a8960d3874c14d462bb8dd0c646d`.
- `new_frontend`: `/var/www/prodimt-pedidos/frontend/releases/customer-catalog-afb8f3ad9c33`.
- `private_snapshot`: `/var/backups/prodimt-pedidos/customer-catalog-20261102T150222Z-c8d59ed390ba`.

I15 queda instalado por evidencia suministrada y sustituye el estado de instalación prevista y frontend activo I14 anteriores. No es inspección SSH independiente o aceptación visual de la rejilla con datos reales. Próximo: Ctrl+F5 → Catálogos → Clientes, revisar columnas/tamaño/textos al maximizar/reducir ventana, búsqueda/filtros y modales; confirmar que configuración, Productos y acceso I14 se conservan. No repetir instaladores o importaciones. Sólo se actualizó documentación en esta sesión, sin operar VPS/SQL, cambiar el paquete o repetir pruebas de binarios.
