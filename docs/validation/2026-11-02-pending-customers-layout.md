# Evidencia I17 — Clientes pendientes responsivos

Fecha: 2 de noviembre de 2026. Datos de pruebas ficticios. Base I15: `71461b78b07183af215742b399d21f0fa5443146`.

## Verificado localmente

- `npm run build`: correcto. Sin elevar presupuestos; tres avisos CSS preexistentes de catálogo/configuración, ningún error. Pendientes usa una hoja propia por debajo del presupuesto.
- `python3 -m unittest discover -s tests/pilot -q`: 350 correctas. Las 15 nuevas prueban pines/manifiesto, rutas, rechazar releases previos, preflight real aislado, bloqueo por backend/salud/HTML diferentes, idempotencia y reversión de frontend ante fallo/interrupción.
- E2E seleccionadas: `pending-customers-layout.spec.ts`, `pending-rest-days.spec.ts`, `order-capture.spec.ts`. Primera ejecución: 18/19 correctas; el selector global de horario ahora encuentra tarjeta y captura. Se acota al formulario; ejecución de ese caso corregido: 1/1 correcto. Total 19 casos correctos, sin cambios funcionales para resolverlo.
- Chromium local aislado 153, ejecutable/configuración externos al repositorio. CI debe repetir con la versión fijada por el lockfile.
- Siete anchos: 320/390/768/1024/1366/1920/2560px → 1/1/2/3/4/6/8 columnas. Nombres/notas largos, teléfono faltante y etiqueta de descanso sin desbordar; ancho de rejilla superior al 90% en PC; acciones alineadas y botones de mínimo 44px.
- Captura a toda la fila a 320/390/768/1366/2560px; misma instancia, foco, cantidad decimal y notas después de cada cambio. Cancelar restaura tarjeta y no envía escrituras de pedido. Descanso/fecha/errores, interpretación, perfil, guardado, reintento durable y conflictos conservados por pruebas existentes.
- Capturas PC/móvil y formulario móvil revisados visualmente. No contienen datos reales y no se publican como entregables.
- `graphify update .`: no disponible; comando ausente, sin grafo modificado.

## Pendiente

CI, commit remoto de aplicación, ZIP/hash final y guardado de entrega. Después preflight, instalación y revisión visual por el usuario. No se operó VPS, SQL o datos reales. No presentar las pruebas locales como instalación en producción.
