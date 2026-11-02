# I17 — Clientes pendientes con tarjetas responsivas

Autorizado el 2 de noviembre de 2026. El usuario solicita aprovechar el espacio de PC de forma similar al catálogo de clientes I15 y adaptar tarjetas/columnas al ancho disponible.

## Comportamiento

- Pendientes utiliza el ancho de la pantalla con márgenes adaptables y navegación compacta en PC. Fecha, entrega automática y consulta se organizan en una fila amplia y se apilan en móvil.
- Rejilla CSS `auto-fill` con mínimo 280px limitado al 100% del contenedor. La cantidad de columnas depende del espacio disponible; no existe lógica de viewport ni reordenación visual que cambie el orden de teclado.
- Nombre y notas completos con ajuste de línea; teléfono faltante explícito, entrega habitual y número de productos. Acciones al pie y botones de mínimo 44px. El contador refleja clientes visibles, incluido descanso sólo cuando se muestra.
- Capturar pedido expande esa tarjeta a toda la fila. El formulario conserva su instancia, modo, cantidades, notas y foco al cambiar ancho. Cancelar devuelve la tarjeta al tamaño normal; los datos sólo se guardan con la acción existente.
- Conserva filtro/calendario de descanso, orden por descanso/nombre, fecha automática o explícita, corte de entrega, verificación de pedido existente, interpretación, perfil, guardado/reintento y NoOrder. No se añaden consultas ni escrituras por reacomodar tarjetas.

## Alcance y compatibilidad

Cambios de HTML/CSS; la lógica de `AdminPendingCustomersComponent` y la captura compartida permanecen intactas. Sin API, backend, migraciones, dependencias o cambios de datos. Preserva catálogo I15, productos/precios I13 y plantilla I14.

Rama `codex/i17-responsive-pending-customers` desde I15 `71461b78b07183af215742b399d21f0fa5443146`. I16 en su rama de propuesta sólo tenía documentación en el checkpoint revisado `74b7926ff2aba09e60fca54b800409b83d05241e`; no se incorpora ni reemplaza ese trabajo. Un futuro despliegue I16 debe integrar estos estilos si parte de una base previa.

## Actualización nativa

Paquete `PRODIMT_Clientes_Pendientes_I17.zip`, carpeta `prodimt-pending-customers-layout-update`, instalador `PRODIMT_Instalar_I17.py` y payload `prodimt-pending-customers-payload.zip`. Requiere frontend `customer-catalog-afb8f3ad9c33`, conserva backend `access-tokens-65560ca3cf98` y activa `pending-customers-cdbfec3071bc`. Rechaza cualquier otra base para no sustituir otro incremento instalado.

Pines de fuente/payload, hashes por archivo, lectura inicial, snapshot privado, activación atómica, verificación HTTPS de assets y reversión del enlace si falla. Repetir el mismo paquete verifica `already_current`. Sin SQL directo, migraciones, claves, contraseñas o reinicios.

## Verificación

Build Angular correcto. 350 pruebas Python correctas, incluidas 15 del paquete I17. En E2E se verificaron 320/390/768/1024/1366/1920/2560px: 1/1/2/3/4/6/8 columnas, contenido/botones dentro de tarjeta, botones de 44px, acciones alineadas y sin desbordamiento de página. Nombres/notas largos, teléfono faltante, descanso/contador, formulario ancho, borrador/foco y cancelación comprobados. Las 19 E2E de captura/descanso/diseño pasan en total; se acotó una aserción anterior al formulario por el horario repetido en la tarjeta. Capturas con datos ficticios revisadas.

Aplicación/paquete `cdbfec3071bc2046f0398fbdad6b62bdca6de748`, [PR #8](https://github.com/romanrfhack/pedidosProdimt/pull/8), [CI 41472464468](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41472464468) con cuatro trabajos correctos: 335 Domain/Application, 66 SQL/criptografía, 151 E2E y 350 Python más smoke Production.

Entrega verificada y guardada: ZIP 200296 bytes, SHA256 `6d3ff6d47282852dde32c340d283b53e860e0566efa5530d04e2abf357684f5a`; payload `8b88052dcff7a0c5668e09f8a8d2106741c1bf56fa03b4dc3ec8f70fc03a1cc7`. Once archivos activos/licencias, sin residuos de compilaciones previas; CRC, hashes, manifiesto/imports y cargador exportado correctos. Evidencia: `../validation/2026-11-02-pending-customers-layout.md`. No se accedió al VPS/SQL ni se instaló desde este entorno; la confirmación posterior del usuario se registra a continuación.

## Instalación confirmada

El usuario comparte el 2 de noviembre SHA256 correcto, preflight `planned/completed` sin cambios y aplicación `updated/completed` de `20261102-pending-customers-install1`, con base/salud verificadas y frontend verificado al aplicar. Fuente desplegada `cdbfec3071bc2046f0398fbdad6b62bdca6de748`, release `pending-customers-cdbfec3071bc`; snapshot privado registrado en evidencia. El reporte indica sin migraciones/SQL directo, cambios de backend/contraseña, reinicio o secretos impresos.

Esta evidencia sustituye el pendiente de instalación; no es inspección SSH independiente ni aceptación visual. Queda Ctrl+F5 → Pendientes, revisar columnas al variar ancho y abrir/cancelar captura para comprobar el borrador. No repetir instaladores/importaciones por este resultado. Sesión documental, sin cambios de aplicación/paquete ni nuevas pruebas de binarios.
