# I07: vista previa correcta y guardado sin confirmar

## Evidencia disponible

Durante la aceptación en navegador, el usuario aporta una captura con desglose correcto de dos productos y el mensaje «No pudimos confirmar el guardado». La advertencia de resultado incierto mantiene el botón Reintentar guardado. Después comparte una respuesta JSON de `/api/admin/customers/{id}/orders/preview`: perfil aprobado, versión vigente, productos activos y unidades kg confirmadas, con las dos cantidades resueltas. No se publica el contenido real ni las cabeceras, identificadores o imagen del cliente.

La respuesta de preview confirma exclusivamente interpretación y lectura de configuración. No demuestra que se haya enviado, rechazado o confirmado la transacción de captura. En el siguiente reporte, Network muestra HTTP 500 en `/orders/capture` y HTTP 200 en preview, perfil, plantilla y consulta de pedidos. El cuerpo de capture coincide con la fecha, versión y huella previamente mostradas. No se dispone del cuerpo de respuesta ni de la excepción del servicio; la causa sigue sin identificar y no se afirma si quedó guardado el pedido.

## Revisión de código

Se revisaron AdminOrderCaptureService, OrderCaptureEndpoints, EfOrderOperationStore, CatalogTransaction, AdminOrderService, repositorios de pedidos/auditoría y el manejo de sendAttempt en Angular sobre el código I07 instalado.

- Preview no crea pedidos. Save verifica nuevamente perfil, huella y pedido vigente.
- El intento conserva referencia y contenido. Pedido, auditorías y recibo comparten transacción; un recibo existente se recupera antes de volver a validar el perfil.
- El cliente distingue rechazos 400/401/403/404/409 y PersistenceNotSupported de errores sin confirmación. Network confirma ahora un 500 real. El frontend conserva el intento incierto, sin convertir esa respuesta en autorización para otra entrega.
- El recibo se inserta una vez tras la acción, sin UPDATE. La hipótesis de que faltaría UPDATE de OrderOperations no se sustenta en este flujo.
- La ausencia de hora de entrega es opcional y no identifica la causa del fallo.

No se encontró una causa demostrada a partir de la evidencia disponible. No se han cambiado binarios, migraciones, permisos, credenciales ni datos de producción; tampoco se han reproducido escrituras contra el VPS. El código contiene una excepción de configuración ante ausencia del canal administrativo, pero no existe evidencia de que ésa sea la causa en el VPS; no ejecutar bootstrap para comprobarla.

## Diagnóstico del 500

Herramienta nueva y separada del diagnóstico de autenticación de preinstalación:

- Fuente: `scripts/pilot/diagnose-capture-save.py`.
- Entregable: `PRODIMT_Diagnostico_Captura_I07.py`, revisión `20261030-capture-save-diagnosis1`.
- SHA256: `e0f83643a4f2a8beff76f85009fbe57fa83dfcfcda4e145f1871fd2b3a2de0d0`.
- Únicos procesos: `systemctl show` de ActiveState y `journalctl` de este servicio, últimas seis horas y hasta 4000 registros. `--hours` permite 1–24. Timeout 25 segundos por lectura.
- No carga el instalador previo, no lee env/configuración, no solicita credenciales SQL, no consulta base ni API, no modifica servicios/releases y no reenvía operaciones. Se puede ejecutar por stdin sin copiar un script al VPS.
- Agrupa registros multilínea o líneas separadas de ConsoleLogger antes de asociar excepciones a capture. Devuelve fechas UTC, tipos conocidos, códigos SQL numéricos, categorías fijas y nombres de métodos/tablas permitidos. Nunca imprime texto de mensajes, SQL, parámetros, rutas con clientes, stack libre, cookies, JWT o credenciales.
- Errores ajenos al stack de captura se informan separados; no se atribuyen automáticamente a este pedido. Journal vacío o sin marcador no equivale a causa identificada ni guardado verificado.

Validación: 12 pruebas nuevas de agrupación, SQL, privacidad con secretos ficticios, separación de eventos ajenos, entrada inválida, lecturas limitadas, timeout y CLI. 186 pruebas Python locales correctas. Ejecución real local de CLI sin systemd termina con `read_command_failed`, JSON sin streams; no se confunde ese entorno con el VPS. `graphify update .` no disponible, salida 127; no se regeneró el grafo. La aplicación instalada no cambia.

Ejecutar desde WSL después de descargar y verificar el SHA:

```bash
ssh root@194.238.26.70 'python3 -' \
  < /mnt/c/Users/TUF/Downloads/PRODIMT_Diagnostico_Captura_I07.py
```

Pendiente obtener el JSON del VPS. Las categorías de registros constituyen indicios para una corrección acotada, no una autorización para restaurar SQL o cambiar permisos.

## Evidencia adicional si hace falta

Conservar la pestaña y el intento incierto. En DevTools → Network/Red, filtrar capture y seleccionar POST con terminación `/orders/capture`, distinto de `/preview`. Obtener únicamente Status Code y contenido de Response; si está vacío, indicar vacío. No compartir cURL, Authorization, cookies ni un HAR completo. Redactar datos reales si aparecieran en la respuesta.

Si la petición original ya no aparece, abrir Network antes de pulsar Reintentar guardado en la misma captura. Ese botón reutiliza la misma operación y contenido; no iniciar una captura nueva ni refrescar la pestaña. La falta de respuesta no autoriza a crear una entrega independiente.

Con el estado HTTP y la respuesta se decidirá si basta corregir una validación/cliente, revisar de forma acotada registros del servicio o reproducir un fallo del servidor con datos ficticios. No volver a ejecutar el instalador I07 ni su diagnóstico SQL de preinstalación I06 contra los releases I07 activos. Conservar la base y respaldos existentes.
