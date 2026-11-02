# I15 — Catálogo de clientes con rejilla responsiva

Autorizado el 2 de noviembre de 2026. El usuario solicita aprovechar el ancho en PC y adaptar tamaño/número de tarjetas a la pantalla.

## Comportamiento

- Sólo el catálogo de clientes utiliza todo el ancho disponible con márgenes adaptables. La rejilla CSS usa `auto-fill` con ancho mínimo de 280px, limitado al 100% cuando la pantalla es más estrecha. Las tarjetas se distribuyen sin lógica de viewport en TypeScript.
- La cabecera organiza título/alta y buscador en una fila amplia, y combina filtros/fecha/refresco en otra. En móvil/tablet los controles se apilan; PC tiene navegación compacta. Resultado visible y acciones para limpiar búsqueda o filtros.
- Tarjetas de una misma fila se estiran por igual, con acciones al pie, nombre/ruta sin truncar, teléfono/entrega y etiquetas de acceso/cobro. Se conservan colores y etiquetas de estado, revisión pendiente, desconocidos e inactivos.
- Búsqueda por nombre/teléfono/ruta/código, filtro y orden de I06 conservados. Descanso/no pedido e inactivos siguen al final; cambiar tamaño no modifica filtros, datos, pedidos o programación.
- `auto-fill` conserva columnas vacías al filtrar: una sola coincidencia mantiene ancho legible en PC. Estado vacío ocupa toda la rejilla. Nombres/rutas largos ajustan líneas sin desbordar la página.
- Nuevo cliente/Editar conservan sus modales, foco, borradores, errores y guardado. Configurar abre el mismo cliente en la pantalla enfocada existente, sin aplicar la nueva rejilla a su perfil.

## Contratos y entrega

Sólo HTML/CSS del frontend y controles de filtro existentes; sin endpoints, cambios de datos, migraciones, credenciales o dependencias nuevas. Se preservan Productos I13, precios/perfiles y mensaje de acceso I14.

Actualización nativa sólo sobre I14 saludable: frontend `reminder-message-768230826a3e` y backend `access-tokens-65560ca3cf98`. Nuevo frontend `customer-catalog-{source12}`. Instalador fijado por hashes, lectura inicial, snapshot privado, activación atómica, verificación HTTPS de assets y reversión de frontend ante fallo. Repetir verifica `already_current`; no hay SQL directo, revocaciones, reinicio o cambio de contraseña/clave.

## Aceptación

Medir columnas, ancho, controles y ausencia de desbordamiento en 320/390/768/1024/1366/1920/2560px. Verificar nombres/rutas largos, estados, filtros/reacomodo y regreso al listado, modales y configuración. Pruebas con datos ficticios; la aceptación real y la instalación requieren revisión posterior del usuario. Evidencia en `../validation/2026-11-02-customer-catalog-layout.md`.

Código de aplicación/paquete `afb8f3ad9c33a8960d3874c14d462bb8dd0c646d`, [PR #7](https://github.com/romanrfhack/pedidosProdimt/pull/7), [CI 41466208422](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41466208422) con cuatro trabajos correctos: 335 Domain/Application, 66 SQL/criptografía, 143 E2E y 335 Python más smoke Production. Los siete anchos dan 1/1/2/3/4/6/8 columnas; capturas PC/móvil revisadas. ZIP `PRODIMT_Catalogo_Clientes_I15.zip`, 199173 bytes, SHA256 `9cf6850090db8bd21f79675b9cdd5cb5e6a7fde6ac9beb0cbde3bfafeae08905`, verificado y guardado. Instalación/aceptación remotas pendientes.

## Instalación confirmada

El usuario comparte el 2 de noviembre SHA256 correcto, preflight `planned/completed` y aplicación `updated/completed`, salud/frontend verificados. Fuente desplegada `afb8f3ad9c33a8960d3874c14d462bb8dd0c646d`, release `customer-catalog-afb8f3ad9c33`; snapshot registrado en evidencia. Sin migraciones/SQL directo, cambios de backend/contraseña, reinicio o secretos impresos según el reporte. No se operó VPS/SQL desde este entorno. Esto sustituye el pendiente de instalación anterior; queda la aceptación visual del catálogo al recargar y variar el ancho. No repetir instalación o importaciones por este resultado.
