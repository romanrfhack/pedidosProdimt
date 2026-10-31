# I08 — Validación de configuración responsive

## Alcance comprobado localmente

- Build Angular de producción correcto, sin cambios de dependencias o presupuestos. Estilo de configuración de 7.83 kB emite advertencia por 4 kB, dentro del límite de error de 8 kB.
- La base pasó 76 E2E, incluidos ocho nuevos de calendario y medidas. El cierre añade tres de Pendientes por fecha y ejecuta la suite completa. También continúan perfil/concurrencia/unidades, alta/edición de cliente y captura/reintento de pedidos existentes.
- Tres productos visibles en configuración habitual a 1366x768 sin scroll de página. Anchos 1920, 768 y 390 sin desbordamiento horizontal. Capturas locales con datos ficticios revisadas; las imágenes del usuario no se publican.
- Casillas trabajan/descansan invertidas correctamente; guardar/recargar, siete días o ninguno, fallo de red y otro calendario guardado. Guardar días preserva cantidades sin guardar del perfil y toma datos de contacto/entrega actuales.
- 206 Python correctos, nueve nuevos: hashes/rutas, empaquetado, activación/idempotencia, cambio externo de backend, rechazo de enlace inesperado y restauración ante fallo o interrupción justo al activar.
- No se modifican API, esquema, migraciones o lógica de demanda. La semántica de descansos procede de I06. Pendientes cruza calendarios persistidos con la fecha de servicio; tres casos nuevos comprueban lunes/domingo en navegador UTC+14, captura excepcional sin escrituras y recuperación de fallo de calendario. El PUT de datos generales conserva su contrato de concurrencia.

## Instalación preparada

Fuentes: `scripts/pilot/build-client-configuration-package.py` e `install-client-configuration-update.py`. Paquete de frontend únicamente, con sourceCommit y SHA del payload fijados dentro del instalador. Inicialmente requiere `capture-46fed604732e` en ambos enlaces; al terminar sólo frontend pasa a `client-config-{source12}`. Backend, proceso, entorno, contraseñas y SQL no se modifican.

Snapshot privado bajo `/var/backups/prodimt-pedidos/client-configuration-*`; conserva enlace anterior e identidad/hash del backend. No contiene cuentas o cadenas de conexión. HTTP verifica `/health/db`, protección administrativa y bytes de index/JS/CSS mediante HTTPS local con certificado del dominio. Fallos de verificación revierten frontend; no se restaura SQL.

## Cierre técnico y paquete

La base 42f6eb4c225a6781475893151a28d3e9b19fae2a pasó [CI 41214749798](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41214749798): cuatro jobs correctos, 76 E2E, 206 Python, 267 .NET, 31 SQL Server y smoke Production. El cierre de Pendientes por fecha se valida y empaqueta sobre el siguiente commit; metadatos definitivos pendientes. No cambió código del backend.

Para reproducir: checkout limpio del código final, npm ci/build de producción, copiar dist/prodimt-pedidos-web/3rdpartylicenses.txt a su browser/ y ejecutar build-client-configuration-package.py con --frontend de browser, --source del código y --output absoluto. El ZIP incorpora ambos pines. Estado esperado al aplicar: updated/completed, frontend_verified y health_verified true; backend continúa capture-46fed604732e. No necesita usuario SQL, compilación en VPS, migraciones o reinicio.

Tras extraer el ZIP y verificar SHA256SUMS.txt:

```bash
scp PRODIMT_Instalar_I08.py prodimt-client-configuration-payload.zip root@194.238.26.70:/root/
ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I08.py --package /root/prodimt-client-configuration-payload.zip --apply'
```

Pendiente recibir JSON del VPS y confirmar configuración en navegador, persistencia del calendario y efecto de descanso en estado diario sin crear NoOrder. Antes de recargar, resolver cualquier captura incierta o borrador abierto. No se ha ejecutado SSH desde este entorno ni se declara I08 instalado. La incidencia del canal I07 fue reparada por el usuario, pero no existe aún confirmación de guardado del pedido real. `graphify update .` no disponible (127).
