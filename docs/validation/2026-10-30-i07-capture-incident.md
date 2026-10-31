# I07: vista previa correcta y guardado sin confirmar

## Evidencia disponible

Durante la aceptación en navegador, el usuario aporta una captura con desglose correcto de dos productos y el mensaje «No pudimos confirmar el guardado». La advertencia de resultado incierto mantiene el botón Reintentar guardado. Después comparte una respuesta JSON de `/api/admin/customers/{id}/orders/preview`: perfil aprobado, versión vigente, productos activos y unidades kg confirmadas, con las dos cantidades resueltas. No se publica el contenido real ni las cabeceras, identificadores o imagen del cliente.

La respuesta de preview confirma exclusivamente interpretación y lectura de configuración. No demuestra que se haya enviado, rechazado o confirmado la transacción de captura. No se dispone aún del estado HTTP ni de la respuesta de `/orders/capture`; la causa no está identificada y no se afirma si quedó guardado el pedido.

## Revisión de código

Se revisaron AdminOrderCaptureService, OrderCaptureEndpoints, EfOrderOperationStore, CatalogTransaction, AdminOrderService, repositorios de pedidos/auditoría y el manejo de sendAttempt en Angular sobre el código I07 instalado.

- Preview no crea pedidos. Save verifica nuevamente perfil, huella y pedido vigente.
- El intento conserva referencia y contenido. Pedido, auditorías y recibo comparten transacción; un recibo existente se recupera antes de volver a validar el perfil.
- El cliente distingue rechazos 400/401/403/404/409 y PersistenceNotSupported de errores sin confirmación. La captura mostrada es compatible con una respuesta 5xx, un fallo de transporte o un fallo de lectura de respuesta; la imagen no permite elegir entre ellos.
- El recibo se inserta una vez tras la acción, sin UPDATE. La hipótesis de que faltaría UPDATE de OrderOperations no se sustenta en este flujo.
- La ausencia de hora de entrega es opcional y no identifica la causa del fallo.

No se encontró una causa demostrada a partir de la evidencia disponible. No se han cambiado binarios, migraciones, permisos, credenciales ni datos de producción; tampoco se han reproducido escrituras contra el VPS. No se repiten suites por esta actualización documental.

## Siguiente evidencia

Conservar la pestaña y el intento incierto. En DevTools → Network/Red, filtrar capture y seleccionar POST con terminación `/orders/capture`, distinto de `/preview`. Obtener únicamente Status Code y contenido de Response; si está vacío, indicar vacío. No compartir cURL, Authorization, cookies ni un HAR completo. Redactar datos reales si aparecieran en la respuesta.

Si la petición original ya no aparece, abrir Network antes de pulsar Reintentar guardado en la misma captura. Ese botón reutiliza la misma operación y contenido; no iniciar una captura nueva ni refrescar la pestaña. La falta de respuesta no autoriza a crear una entrega independiente.

Con el estado HTTP y la respuesta se decidirá si basta corregir una validación/cliente, revisar de forma acotada registros del servicio o reproducir un fallo del servidor con datos ficticios. No volver a ejecutar el instalador I07 ni su diagnóstico SQL de preinstalación I06 contra los releases I07 activos. Conservar la base y respaldos existentes.
