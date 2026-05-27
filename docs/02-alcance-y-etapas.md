# 02 — Alcance y etapas

## Alcance general

Construir una aplicación web mobile first para capturar pedidos de clientes, reducir llamadas y eliminar la recaptura manual desde WhatsApp hacia Excel.

## Alcance de la primera etapa

La primera etapa debe enfocarse en capturar pedidos de clientes y permitir revisión administrativa básica.

Incluye:

1. Catálogo mínimo de clientes.
2. Catálogo mínimo de productos/moldes.
3. Preferencias de pedido por cliente.
4. Hora o ventana deseada de entrega por cliente, opcional.
5. Captura de pedido del día desde celular.
6. Opción explícita de **no pedir hoy**.
7. Sugerencia basada en pedidos anteriores.
8. Detección de pedido tardío después de las 10:00 a.m.
9. Detección de segundo pedido o cambio el mismo día.
10. Panel administrativo para revisar pedidos capturados.
11. Panel administrativo para aceptar, rechazar o ajustar pedidos tardíos o duplicados.
12. Exportación o vista simple para operación interna.
13. Registro de auditoría de decisiones administrativas.

## Fuera de alcance de la primera etapa

No se debe intentar resolver desde el inicio:

- Vista completa de producción por máquina.
- Vista completa de embarques.
- Vista completa de repartidores.
- Optimización avanzada de producción.
- Ruteo automático.
- Predicción avanzada de demanda.
- Integración completa con facturación.
- Sustitución total del Excel histórico.
- Automatización completa de WhatsApp con plantillas interactivas.
- Dashboard ejecutivo complejo.
- Estadísticas avanzadas para clientes.

La Fase 1 puede guardar campos que faciliten módulos futuros, como máquina asignada o hora deseada de entrega, pero no debe construir todavía los módulos completos de producción, embarques o repartidores.

## Etapas propuestas

### Etapa 0 — Preparación

Objetivo: dejar lista la base de trabajo.

Entregables:

- Documentación inicial.
- Repositorio con estructura base.
- Decisiones técnicas.
- Modelo de datos inicial.
- Importación o carga manual inicial de clientes y productos.
- Carga inicial opcional de preferencias cliente-producto-máquina.

### Etapa 1 — MVP de captura

Objetivo: que el cliente pueda enviar su pedido y que PRODIMT pueda verlo y revisarlo.

Entregables:

- Login simple para cliente.
- Pantalla "Mi pedido de hoy".
- Productos frecuentes del cliente.
- Cantidad por producto/molde.
- Repetir pedido sugerido.
- Marcar "no pedir hoy".
- Guardar pedido.
- Vista administrativa de pedidos por fecha.
- Estado de clientes pendientes.
- Bandeja de pedidos tardíos pendientes de revisión.
- Bandeja de segundos pedidos o cambios pendientes de revisión.
- Decisión administrativa: aceptar, rechazar o aceptar con cambio de hora de entrega.

### Etapa 2 — Operación interna

Objetivo: que áreas internas usen la información sin esperar recaptura.

Entregables:

- Vista de producción por máquina.
- Vista de producción por molde/producto.
- Vista de embarques.
- Vista de reparto por cliente/ruta/repartidor.
- Vista de pedidos pendientes o tardíos.
- Exportación a Excel si aún se requiere.
- Roles internos por departamento.

### Etapa 3 — WhatsApp automatizado

Objetivo: usar WhatsApp para reducir fricción, no como base de datos.

Entregables:

- Mensaje diario con último pedido sugerido.
- Botón o liga para confirmar repetición.
- Liga para editar en app.
- Registro de confirmaciones.
- Webhook de respuestas cuando aplique.

### Etapa 4 — Valor agregado para clientes

Objetivo: aumentar adopción.

Entregables:

- Histórico visual de pedidos.
- Comparativos por semana.
- Tendencia de crecimiento.
- Sugerencias por fechas especiales.
- Avisos de pedido recurrente.
- Recomendaciones personalizadas.

## Criterio de éxito del MVP

El MVP se considera exitoso cuando:

- Un cliente puede confirmar o capturar su pedido desde celular sin ayuda.
- Un cliente puede indicar claramente que no pedirá hoy.
- Administración puede ver quién pidió, quién no pidió y quién falta.
- Los pedidos tardíos quedan identificados y pendientes de decisión.
- Los segundos pedidos del día quedan identificados y pendientes de decisión.
- La información puede consultarse sin leer mensajes de WhatsApp.
- El proceso puede convivir con llamadas manuales solo para clientes rezagados.
