# 02 — Alcance y etapas

## Alcance general

Construir una aplicación web mobile first para capturar pedidos de clientes, reducir llamadas y eliminar la recaptura manual desde WhatsApp hacia Excel.

## Alcance de la primera etapa

La primera etapa debe enfocarse en:

1. Catálogo mínimo de clientes.
2. Catálogo mínimo de productos/moldes.
3. Preferencias de pedido por cliente.
4. Captura de pedido del día desde celular.
5. Sugerencia basada en pedidos anteriores.
6. Panel administrativo para revisar pedidos capturados.
7. Exportación o vista simple para operación interna.

## Fuera de alcance de la primera etapa

No se debe intentar resolver desde el inicio:

- Optimización avanzada de producción.
- Ruteo automático.
- Predicción avanzada de demanda.
- Integración completa con facturación.
- Sustitución total del Excel histórico.
- Automatización completa de WhatsApp con plantillas interactivas.
- Dashboard ejecutivo complejo.

## Etapas propuestas

### Etapa 0 — Preparación

Objetivo: dejar lista la base de trabajo.

Entregables:

- Documentación inicial.
- Repositorio con estructura base.
- Decisiones técnicas.
- Modelo de datos inicial.
- Importación o carga manual inicial de clientes y productos.

### Etapa 1 — MVP de captura

Objetivo: que el cliente pueda enviar su pedido y que PRODIMT pueda verlo.

Entregables:

- Login simple para cliente.
- Pantalla "Mi pedido de hoy".
- Productos frecuentes del cliente.
- Cantidad por producto/molde.
- Repetir pedido sugerido.
- Guardar pedido.
- Vista administrativa de pedidos por fecha.
- Estado de clientes pendientes.

### Etapa 2 — Operación interna

Objetivo: que áreas internas usen la información sin esperar recaptura.

Entregables:

- Vista de producción por molde/producto.
- Vista de reparto por cliente/ruta/repartidor.
- Vista de pedidos pendientes o tardíos.
- Exportación a Excel si aún se requiere.
- Roles internos.

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
- Administración puede ver quién pidió y quién falta.
- La información puede consultarse sin leer mensajes de WhatsApp.
- El proceso puede convivir con llamadas manuales solo para clientes rezagados.
