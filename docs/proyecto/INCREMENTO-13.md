# I13 — Productos por tipo y tarjetas responsivas

Autorizado el 1 de noviembre de 2026. El usuario reporta consolidación terminada y solicita reducir espacio desperdiciado y facilitar búsqueda/consulta de productos.

## Comportamiento

- Productos aprovecha el ancho con márgenes, hasta 1920px; el resto de pantallas conserva su distribución.
- Cinco grupos visuales con encabezado, contador y color: Por número, Harina, Sancochados, Vapor y Otros productos. Etiquetas visibles además del color.
- La preparación explícita del nombre tiene prioridad sobre su número. Por número ordena decimales y fracciones numéricamente; los inactivos quedan al final de cada grupo.
- Harina se reconoce por esa palabra; no asumir ingrediente de Burrera o Flauta. Los nombres y descripciones existentes se presentan sin reescribirlos. No hay categoría nueva en SQL.
- Búsqueda sin acentos por nombre, descripción, grupo y unidad; filtros combinables por tipo/estado, contadores y vacío con limpieza de filtros.
- Rejilla automática con una o varias columnas según espacio; texto largo se ajusta, botones accesibles y etiquetas de estado.
- Nuevo producto/Editar abren modal centrado bajo demanda. Unidad inicial por confirmar; se conservan kg, pieza, docena y paquete. Precio sigue configurándose por cliente.
- Modal con Escape/cierre/fondo y devolución de foco; no se cierra ni permite otro envío mientras guarda. Error mantiene borrador. El enlace desde configuración abre el editor del mismo id.
- Archivados por consolidación ocultos al inicio, consultables con su destino y sin acciones de editar/reactivar. Consolidación I11 sigue disponible al pie.

## Contratos y datos

Reutiliza GET/POST/PUT y activación/desactivación del catálogo. No modifica API, esquema, precios, revisiones, unidades, perfiles, posiciones de captura, pedidos ni accesos. Los 17 nombres del usuario se usan como referencia de clasificación; pruebas sólo con descripciones e identificadores ficticios. No se consultó la base ni se accedió al VPS desde este entorno.

## Instalación

Frontend exclusivo sobre I12 instalado y saludable: backend/frontend access-tokens-65560ca3cf98. I12 se completa con su instalador revisión 2 antes de I13; este paquete no migra ni instala I12. Preflight de lectura rechaza I11 u otro release. Así no se rompe el reintento pendiente de I12 ni se combina su frontend con un backend anterior.

Paquete con instalador, payload y hashes. No solicita credenciales SQL, consulta la base o reinicia el servicio. Verifica proceso/release, salud, protección API y frontend servido, crea snapshot privado, prepara un release nuevo y cambia el enlace de forma atómica. Comprueba todos los JS/CSS por HTTPS local y que backend no haya cambiado. Si falla tras activar, restaura sólo el enlace anterior. Repetición verifica already_current.

## Validación y aceptación

Build Angular, pruebas de grupos/búsqueda/estado/edición/archivo y medidas a 320, 390, 768, 1366 y 1920px; regresión E2E existente y pruebas Python del instalador/paquete. Evidencia final en docs/validation/2026-11-01-product-catalog.md.

Tras instalación correcta, Ctrl+F5 y Catálogos → Productos. Revisar grupos con datos reales, búsqueda y edición; precios siguen en configuración de cliente. Validación técnica no equivale a instalación o aceptación remota.
