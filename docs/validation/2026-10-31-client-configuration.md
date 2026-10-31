# I08 — Validación de configuración responsive

## Alcance comprobado localmente

- Build Angular de producción correcto, sin cambios de dependencias o presupuestos. Estilo de configuración de 7.83 kB emite advertencia por 4 kB, dentro del límite de error de 8 kB.
- 79 E2E correctos, incluidos ocho nuevos de calendario/medidas y tres de Pendientes por fecha. También continúan perfil/concurrencia/unidades, alta/edición de cliente y captura/reintento de pedidos existentes.
- Tres productos visibles en configuración habitual a 1366x768 sin scroll de página. Anchos 1920, 768 y 390 sin desbordamiento horizontal. Capturas locales con datos ficticios revisadas; las imágenes del usuario no se publican.
- Casillas trabajan/descansan invertidas correctamente; guardar/recargar, siete días o ninguno, fallo de red y otro calendario guardado. Guardar días preserva cantidades sin guardar del perfil y toma datos de contacto/entrega actuales.
- 206 Python correctos, nueve nuevos: hashes/rutas, empaquetado, activación/idempotencia, cambio externo de backend, rechazo de enlace inesperado y restauración ante fallo o interrupción justo al activar.
- No se modifican API, esquema, migraciones o lógica de demanda. La semántica de descansos procede de I06. Pendientes cruza calendarios persistidos con la fecha de servicio; tres casos nuevos comprueban lunes/domingo en navegador UTC+14, captura excepcional sin escrituras y recuperación de fallo de calendario. El PUT de datos generales conserva su contrato de concurrencia.

## Instalación preparada

Fuentes: `scripts/pilot/build-client-configuration-package.py` e `install-client-configuration-update.py`. Paquete de frontend únicamente, con sourceCommit y SHA del payload fijados dentro del instalador. Inicialmente requiere `capture-46fed604732e` en ambos enlaces; al terminar sólo frontend pasa a `client-config-{source12}`. Backend, proceso, entorno, contraseñas y SQL no se modifican.

Snapshot privado bajo `/var/backups/prodimt-pedidos/client-configuration-*`; conserva enlace anterior e identidad/hash del backend. No contiene cuentas o cadenas de conexión. HTTP verifica `/health/db`, protección administrativa y bytes de index/JS/CSS mediante HTTPS local con certificado del dominio. Fallos de verificación revierten frontend; no se restaura SQL.

## Cierre técnico y paquete

Código be3995fdce6948ff00f44edd6025a1da1899887c, [CI 41217210669](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41217210669) con cuatro jobs correctos: 79 E2E, 206 Python, 267 .NET, 31 SQL Server y smoke Production. No cambió código del backend.

- PRODIMT_Configuracion_Cliente_I08.zip: 170004 bytes, SHA256 5d950c83bcb59ff8b14dda9fb556dfdfe3205b79bd6d9f27dc68e966da2702ab.
- Payload SHA256: 3c35c329cf3434839336ffbb43ce2ee2e163cfc28b17d3e12792d4d4c4f328e3.
- Diez archivos frontend, incluidos avisos de terceros generados por el mismo build; sin mapas de fuente ni dirección de API de desarrollo.
- Se verificaron SHA256SUMS y se ejecutó load_payload del instalador incluido, comprobando contenido/manifiesto/pines, licencia y presencia del filtro de descansos. El ZIP definitivo se guardó; no se ejecutó apply sobre el VPS.
- Para reproducir: checkout limpio de ese código, npm ci/build de producción, copiar dist/prodimt-pedidos-web/3rdpartylicenses.txt a su browser/ y ejecutar build-client-configuration-package.py con --frontend de browser, --source del código y --output absoluto. El ZIP generado incorpora ambos pines; el entregado queda sin cambios.
- Estado esperado al aplicar: updated/completed, frontend_verified y health_verified true; nuevo frontend /var/www/prodimt-pedidos/frontend/releases/client-config-be3995fdce69. Backend continúa capture-46fed604732e. No necesita usuario SQL, compilación en VPS, migraciones o reinicio.

Tras extraer el ZIP y verificar SHA256SUMS.txt:

```bash
scp PRODIMT_Instalar_I08.py prodimt-client-configuration-payload.zip root@194.238.26.70:/root/
ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I08.py --package /root/prodimt-client-configuration-payload.zip --apply'
```

Pendiente recibir JSON del VPS y confirmar configuración en navegador, persistencia del calendario y efecto de descanso en estado diario sin crear NoOrder. Antes de recargar, resolver cualquier captura incierta o borrador abierto. No se ha ejecutado SSH desde este entorno ni se declara I08 instalado. La incidencia del canal I07 fue reparada por el usuario, pero no existe aún confirmación de guardado del pedido real. `graphify update .` no disponible (127).
