# I08 — Validación de configuración responsive

## Alcance comprobado localmente

- Build Angular de producción correcto, sin cambios de dependencias o presupuestos. Estilo de configuración de 7.83 kB emite advertencia por 4 kB, dentro del límite de error de 8 kB.
- 76 E2E correctos, incluidos ocho nuevos de calendario y medidas. También continúan perfil/concurrencia/unidades, alta/edición de cliente y captura/reintento de pedidos existentes.
- Tres productos visibles en configuración habitual a 1366x768 sin scroll de página. Anchos 1920, 768 y 390 sin desbordamiento horizontal. Capturas locales con datos ficticios revisadas; las imágenes del usuario no se publican.
- Casillas trabajan/descansan invertidas correctamente; guardar/recargar, siete días o ninguno, fallo de red y otro calendario guardado. Guardar días preserva cantidades sin guardar del perfil y toma datos de contacto/entrega actuales.
- 206 Python correctos, nueve nuevos: hashes/rutas, empaquetado, activación/idempotencia, cambio externo de backend, rechazo de enlace inesperado y restauración ante fallo o interrupción justo al activar.
- No se modifican API, esquema, migraciones o lógica de demanda. La semántica de descansos procede de I06, no de mocks nuevos. El PUT de datos generales conserva su contrato de concurrencia.

## Instalación preparada

Fuentes: `scripts/pilot/build-client-configuration-package.py` e `install-client-configuration-update.py`. Paquete de frontend únicamente, con sourceCommit y SHA del payload fijados dentro del instalador. Inicialmente requiere `capture-46fed604732e` en ambos enlaces; al terminar sólo frontend pasa a `client-config-{source12}`. Backend, proceso, entorno, contraseñas y SQL no se modifican.

Snapshot privado bajo `/var/backups/prodimt-pedidos/client-configuration-*`; conserva enlace anterior e identidad/hash del backend. No contiene cuentas o cadenas de conexión. HTTP verifica `/health/db`, protección administrativa y bytes de index/JS/CSS mediante HTTPS local con certificado del dominio. Fallos de verificación revierten frontend; no se restaura SQL.

Pendiente fijar commit/ZIP final, completar CI y recibir JSON del VPS. No se ha ejecutado SSH desde este entorno ni se declara I08 instalado. La incidencia del canal I07 fue reparada por el usuario, pero no existe aún confirmación de guardado del pedido real. `graphify update .` no disponible (127).
