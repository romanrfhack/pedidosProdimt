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

## CI y paquete final

Aplicación/paquete `cdbfec3071bc2046f0398fbdad6b62bdca6de748`, [PR #8](https://github.com/romanrfhack/pedidosProdimt/pull/8). [CI 41472464468](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41472464468) terminado con `success`, cuatro trabajos correctos: 335 Domain/Application (5+330), 66 SQL/criptografía, 151 E2E (4.3 minutos) y 350 Python más smoke Production inicial/persistido. Se verificaron los resultados en logs de los trabajos; no se cambiaron dependencias o presupuestos.

Fuente local limpia y árbol idéntico al commit remoto antes de compilar/empaquetar. ZIP `PRODIMT_Clientes_Pendientes_I17.zip`, 200296 bytes, SHA256 `6d3ff6d47282852dde32c340d283b53e860e0566efa5530d04e2abf357684f5a`. Payload `8b88052dcff7a0c5668e09f8a8d2106741c1bf56fa03b4dc3ec8f70fc03a1cc7`. Verificados CRC de ambos ZIP, SHA256SUMS, pines de fuente/payload y hashes por archivo; contiene sólo once archivos de la clausura activa del build/licencias, sin mapas, fuente, backend o SQL. Todas las referencias de index/imports resuelven dentro del paquete. El módulo del instalador exportado carga exactamente el payload verificado.

Comprobado en JS compilado el mensaje «Haz tu pedido aquí:», ausencia de las dos leyendas retiradas y conservación del catálogo I15; las secuencias hex Unicode del minificador se decodifican para esta comprobación textual. La lógica de la clase de Pendientes permanece idéntica byte por byte. Entrega descargable guardada después de verificarla. Requiere `customer-catalog-afb8f3ad9c33`/backend I12; activa `pending-customers-cdbfec3071bc`.

## Instalación confirmada por el usuario

El 2 de noviembre el usuario aporta verificación SHA256 correcta del ZIP y salida de `20261102-pending-customers-install1`:

- Lectura: `planned/completed`, `changes_made=false`, `base_release_verified=true`, `health_verified=true`. `frontend_verified=false` es esperado antes de aplicar.
- Aplicación: `updated/completed`, `changes_made=true`, `base_release_verified=true`, `health_verified=true`, `frontend_verified=true`.
- Fuente: `cdbfec3071bc2046f0398fbdad6b62bdca6de748`.
- Frontend: `/var/www/prodimt-pedidos/frontend/releases/pending-customers-cdbfec3071bc`.
- Snapshot privado: `/var/backups/prodimt-pedidos/pending-customers-20261102T154208Z-06bc1e751d24`.
- En ambas fases: `database_migrations_run=false`, `database_queries_run=false`, `backend_changed=false`, `service_restarted=false`, `password_changed=false`, `secret_values_printed=false`.

Confirma el despliegue por evidencia proporcionada y sustituye el pendiente de instalación anterior. No se operó VPS/SQL desde este entorno ni se realizó inspección SSH independiente. Esta sesión sólo actualiza documentación; no cambia aplicación/paquete ni repite pruebas de binarios ya validados.

Queda revisión visual/funcional: Ctrl+F5 → Pendientes, variar ancho, comprobar columnas/textos/acciones y abrir/cancelar captura conservando el borrador sin registrar pedidos de prueba reales. No repetir instalación o importaciones por este resultado.
