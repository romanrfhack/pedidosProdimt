# 04 — Requerimientos plus

Estas funcionalidades aumentan adopción, eficiencia o valor percibido, pero no deben bloquear el MVP.

## PLUS-001 — WhatsApp de confirmación rápida

El sistema puede enviar un mensaje con el último pedido sugerido:

> Hola, este fue tu último pedido para este día: ...
> ¿Quieres repetirlo?

Opciones:

- Confirmar repetición.
- Abrir app para editar.
- Avisar que no hará pedido.

## PLUS-002 — WhatsApp con webhooks

Cuando el cliente responda por WhatsApp, el sistema puede recibir la respuesta y actualizar el estado del pedido.

Esto requiere integración con WhatsApp Business Platform, plantillas y webhooks.

## PLUS-003 — Estadísticas para el cliente

Mostrar al cliente información útil:

- Histórico de pedidos.
- Promedio semanal.
- Comparativo contra semanas anteriores.
- Productos más pedidos.
- Tendencia de crecimiento.

## PLUS-004 — Fechas especiales

El sistema puede mostrar avisos como:

- Día festivo.
- 10 de mayo.
- Puentes.
- Eventos locales.
- Temporadas con mayor demanda.

La intención es ayudar al cliente a anticipar ventas.

## PLUS-005 — Proyección simple

El sistema puede sugerir cantidades con base en:

- Últimos 3 pedidos del mismo día.
- Variación contra semana anterior.
- Eventos especiales.
- Estacionalidad simple.

## PLUS-006 — Vista de producción por área

Crear vistas internas especializadas:

- Producción por molde.
- Producción por máquina.
- Producción por línea.
- Faltantes o cambios tardíos.

## PLUS-007 — Vista de reparto

Crear vista para logística:

- Pedidos por ruta.
- Pedidos por repartidor.
- Clientes pendientes.
- Cambios de último momento.

## PLUS-008 — Notificaciones internas

Alertas para administración:

- Cliente importante no pidió.
- Pedido atípico.
- Pedido tardío.
- Cantidad mucho mayor o menor que lo normal.

## PLUS-009 — Modo PWA

Permitir instalar la aplicación en el celular como aplicación web progresiva.

Esto puede reducir fricción sin crear una app nativa.

## PLUS-010 — Importador avanzado desde Excel

Crear un importador que lea versiones futuras del Excel para migrar historia.

No debe ser dependencia permanente del flujo.
