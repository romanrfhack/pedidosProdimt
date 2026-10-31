# I04 — Cambios visibles y configuración por cliente

## Problema y comportamiento esperado

Un cliente pide 15 kg y después solicita 5 kg más. Administración corrige el mismo pedido a un total de 20 kg, anota el motivo y conserva el historial. Además del total actualizado, la máquina necesita un aviso persistente con el cambio de 15 a 20 y diferencia de +5. Una reducción debe conservar el aviso de diferencia negativa; el sistema no puede deshacer producto ya fabricado.

La cantidad solicitada, el aviso recibido y la cantidad físicamente producida son hechos distintos. Este incremento registra los dos primeros. No promete que el reconocimiento de un aviso garantice producción o entrega.

## Alcance autorizado

- Mantener la corrección I02 con motivo, versión esperada e idempotencia.
- Guardar los avisos de impacto junto con la corrección, para que un rollback no deje alertas ficticias ni un reintento las duplique.
- Avisos por revisión y máquina afectada, incluyendo líneas eliminadas, nuevas y sin asignación. Cantidades agrupadas por producto y unidad; nunca sumar kilos con piezas.
- Cuando un pedido pendiente entra en demanda al aceptarse, notificar su incorporación. Rechazar uno que todavía no aportaba demanda no equivale a retirar producción.
- Mostrar avisos pendientes, antes/después/diferencia, motivo, actor y fecha; reconocerlos explícitamente sin modificar el pedido ni registrar producción.
- Refrescar las pantallas de pedidos y demanda con la fecha seleccionada, sin sobrescribir una edición abierta. Avisar de pérdida de conexión y conservar resultados anteriores.
- Pantalla enfocada de configuración del cliente, reutilizando los catálogos: agregar/quitar productos, orden visual, posiciones de interpretación, unidad visible y máquina aplicable; aprobación de perfil explícita.
- Protección de edición concurrente de perfiles mediante versión y lectura consistente del perfil.

## Captura desde varios puntos

Se inicia con captura administrativa asistida: conservar WhatsApp/llamada como origen del trabajo y elegir cliente, fecha de servicio, producto y cantidad explícitos. No exigir al cliente que conozca moldes o máquinas.

Si ya existe un pedido para ese cliente/fecha, abrirlo y corregirlo cuando el mensaje modifica la misma entrega. Crear otra entrega sólo por una decisión explícita. Un segundo envío por la app o importación continúa sujeto a revisión y no se suma automáticamente a la demanda autorizada.

La idempotencia técnica reconoce reintentos del mismo origen/referencia; no demuestra que un WhatsApp y un envío web diferentes correspondan al mismo pedido comercial. No deduplicar sólo por igualdad de cantidades o cliente/fecha, porque pueden existir entregas legítimas separadas. Una futura bandeja de mensajes deberá vincular cada evidencia a un pedido canónico o a una decisión explícita de nuevo pedido.

La interpretación de texto, respuestas automáticas, API de WhatsApp, push fuera del navegador y enlaces de acceso simplificados se evalúan después. La interfaz de cliente por token ya existente no sustituye esa validación de experiencia de uso.

## Datos revisados

El archivo I03 devuelto conserva las 36 relaciones y 23 clientes. Se validaron 30 unidades `kg` y 36 posiciones enteras, consecutivas por cliente. Siguen seis unidades vacías, 16 máquinas vacías y un valor de máquina cero sin significado confirmado. Las 19 asignaciones positivas anteriores se conservan. Falta orden de visita.

La etiqueta «Adicional» de la evidencia era imprecisa: reproduce una columna original sin encabezado, que contiene números o texto según la sección. No se usa para generar cantidades, posiciones o asignaciones. El usuario considera posible que algunos números sean ampliaciones de pedido, pero aún debe confirmarlo.

Los datos privados se normalizan fuera de git; no se aprueban perfiles ni se fusionan variantes por parecido de nombre. La hoja original queda intacta.

## Instalación

El destino ya fue elegido. Preparar `pedidos.prodimt.site` en el VPS mediante inventario de sólo lectura, instalación aislada y HTTPS. La ejecución corresponde al servidor real; los checks de CI no acreditan despliegue. No alterar el resto de aplicaciones del host.
