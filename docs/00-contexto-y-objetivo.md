# 00 — Contexto y objetivo

## Empresa

PRODIMT.

## Problema actual

El proceso actual depende de WhatsApp, llamadas telefónicas, mensajes en grupo y captura manual en Excel.

Flujo actual observado por negocio:

1. El cliente envía su pedido por WhatsApp.
2. Si no lo manda temprano, antes de las 10:00 a.m., alguien le llama para confirmar.
3. La persona que confirma escribe el pedido en un grupo de WhatsApp.
4. Otra persona lee los mensajes del grupo y los pasa al Excel.
5. Al manejar aproximadamente 300 clientes, existe riesgo alto de omitir mensajes, duplicar pedidos o capturar cantidades en el molde incorrecto.

## Objetivo del sistema

Crear una aplicación mobile first para que los clientes capturen, confirmen, editen o indiquen que no harán pedido de forma sencilla.

El resultado principal esperado es que PRODIMT tenga los pedidos capturados en una base de datos central, listos para ser consultados por distintas áreas.

## Principio rector

La aplicación debe reducir fricción para el cliente. El cliente no debe llenar una tabla grande parecida al Excel. Debe ver primero los productos y moldes que normalmente pide, con la opción de agregar otros cuando sea necesario.

## Meta de la primera versión útil

Que el pedido llegue al sistema sin llamada y sin recaptura manual.

La Fase 1 debe resolver únicamente la captura y revisión administrativa básica del pedido. Las vistas de producción por máquina, embarques, repartidores, reportes avanzados, estadísticas para cliente y WhatsApp automático deben construirse después de estabilizar la captura.

## Condiciones operativas confirmadas

- El cliente debe poder enviar pedido desde celular.
- Si no pedirá, debe poder marcarlo explícitamente.
- En el Excel, `x/X` equivale a **no pidió**.
- Los pedidos después de las 10:00 a.m. no se aceptan automáticamente; quedan como tardíos y requieren decisión administrativa.
- Si el cliente hace un segundo pedido o cambio el mismo día, también requiere decisión administrativa.
- Algunos clientes tienen una hora o ventana deseada de entrega. Este dato debe guardarse en el catálogo de cliente como opcional.
- La asignación de máquina es información interna; el cliente no debe verla.
