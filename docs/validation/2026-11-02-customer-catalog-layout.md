# I15 — Catálogo de clientes responsivo

Fecha: 2 de noviembre de 2026. Rama `codex/i15-responsive-customers` desde `59a03197fdff7b9425afc9b2f31b53e82bdd8dc3`, con instalación I14 confirmada por reporte del usuario.

## Cambio

Catálogo de clientes sin límite de 860px, márgenes adaptables y rejilla automática con mínimo legible de 280px. Tarjetas compactas con datos completos, acciones al pie y estados existentes; búsqueda, filtros, refresco y modales conservados. Título/alta y buscador comparten fila en PC; controles apilados en móvil. Contador de resultados, limpiar búsqueda y limpiar filtros. Todo acotado a `.customers-page`; configuración enfocada y catálogo de productos mantienen su comportamiento.

No cambian contratos, backend, datos, reglas del servidor, perfiles, descansos, tarifas, accesos o mensaje I14. Las únicas acciones añadidas limpian filtros locales. No se consultó la base real ni se operó el VPS.

## Validación en curso

- Build Angular local correcto sin cambiar dependencias o presupuestos. Advertencias de estilos existentes/de catálogo, dentro de 8kB por archivo.
- Pruebas de distribución y funcionalidad ejecutadas localmente con Chromium 153 aislado en scratch, con configuración externa y sin modificar el lockfile. CI usará el navegador de Playwright del proyecto.
- Inspección visual local de capturas ficticias; se corrigió alineación de metadatos y superposición de cabecera por especificidad de CSS encapsulado. Se añadió comprobación de que cabecera y buscador no se superponen y que las tarjetas empiezan antes de 450px en 1366×768.
- Quince pruebas del instalador/paquete I15 correctas: pines, sólo frontend, base I14, rechazo de frontends anteriores, lectura sin cambios, rollback, backend cambiado, idempotencia y errores sin datos privados.
- 335 pruebas Python locales correctas. Nueve E2E de I15 correctos en el navegador local; los siete anchos dan 1/1/2/3/4/6/8 columnas, sin desbordamiento, manteniendo filtros al redimensionar. Cabecera sin superposición y tarjetas antes de 450px en 1366×768. Capturas PC/móvil revisadas.
- `graphify update .` no disponible (herramienta no instalada), sin grafo previo.
- Suite final E2E, CI, integridad y entrega del ZIP pendientes en este checkpoint. No afirmar instalación o aceptación remota.

## Instalación prevista

Frontend requerido `reminder-message-768230826a3e`, backend conservado `access-tokens-65560ca3cf98`. Nuevo frontend `customer-catalog-{source12}`. Preflight de lectura debe dar `planned/completed`; después aplicar y comprobar `updated/completed`, salud/frontend true, sin reinicio o SQL directo. Tras éxito: Ctrl+F5 → Catálogos → Clientes, probar anchos/búsqueda/filtros/modales y revisar configuración, Productos y Tokens I14.
