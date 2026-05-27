# 03 — Requerimientos funcionales

## Prioridad MVP

### FR-001 — Autenticación de cliente

El cliente debe poder entrar a la aplicación desde celular.

Criterio inicial aceptable:

- Acceso por número telefónico y código.
- Alternativa temporal: liga segura por cliente para piloto controlado.

### FR-002 — Perfil de cliente

El sistema debe guardar datos básicos del cliente:

- Nombre comercial.
- Teléfono principal.
- Contactos adicionales opcionales.
- Ruta o zona opcional.
- Estado activo/inactivo.

### FR-003 — Catálogo de productos/moldes

El sistema debe tener un catálogo de productos/moldes basado inicialmente en el Excel.

Ejemplos:

- #9.5
- #10
- #10.5
- #11
- #11.5
- #12
- #13
- #14
- #15
- #16
- Flauta
- Vapor
- Sancochado
- Grueso
- Especialidades

Los nombres deben normalizarse. El sistema no debe depender de textos duplicados como `# 10 1/ 2`, `#10.5` o `#10½`.

### FR-004 — Preferencias de cliente por producto

El sistema debe guardar qué productos/moldes suele pedir cada cliente.

Esto evita mostrar al cliente una lista completa de todos los moldes.

### FR-005 — Pedido del día

El cliente debe poder crear o confirmar su pedido para una fecha específica.

El pedido debe tener:

- Cliente.
- Fecha de entrega o producción.
- Estado.
- Líneas de pedido.
- Cantidad por producto/molde.
- Observaciones opcionales.

### FR-006 — Sugerencia de pedido

Al entrar, el cliente debe ver una sugerencia basada en su comportamiento histórico.

Primera regla del MVP:

- Mostrar el último pedido del mismo día de la semana.
- Mostrar también los últimos 3 pedidos del mismo día de la semana cuando existan.

Ejemplo: si hoy es lunes, mostrar los últimos 3 lunes del cliente.

### FR-007 — Repetir pedido

El cliente debe poder repetir el pedido sugerido con una acción simple.

Después puede editar cantidades antes de enviar.

### FR-008 — Editar cantidades

El cliente debe poder modificar cantidades por producto/molde frecuente.

Debe poder:

- Subir cantidad.
- Bajar cantidad.
- Dejar cantidad en cero.
- Agregar nota.
- Agregar producto no frecuente desde una opción secundaria.

### FR-009 — Enviar pedido

El cliente debe confirmar el pedido.

Al confirmar, el sistema debe guardar:

- Fecha y hora de confirmación.
- Usuario o canal de captura.
- Detalle del pedido.
- Cambios contra la sugerencia, si aplica.

### FR-010 — Estado de clientes pendientes

El área administrativa debe ver qué clientes no han enviado pedido antes de la hora límite.

Esto reemplaza la lista mental/manual de llamadas.

### FR-011 — Captura interna en nombre del cliente

Un usuario interno debe poder capturar o editar el pedido por teléfono cuando el cliente no use la app.

El pedido debe quedar marcado con canal `CapturadoInternamente`.

### FR-012 — Vista administrativa diaria

El sistema debe mostrar pedidos por fecha con filtros básicos:

- Cliente.
- Producto/molde.
- Estado.
- Canal de captura.
- Pendiente/confirmado.
- Hora de captura.

### FR-013 — Exportación inicial

El sistema debe poder generar una salida operativa simple, idealmente en Excel o CSV, para transición.

La exportación debe permitir ordenar o agrupar por:

- Producto/molde.
- Cliente.
- Ruta/repartidor.
- Estado.

### FR-014 — Roles

Roles mínimos:

- Cliente.
- Administración.
- Producción.
- Reparto.
- Consulta gerencial.

En MVP pueden implementarse primero Cliente y Administración, dejando Producción/Reparto como vistas protegidas posteriores.

### FR-015 — Auditoría

El sistema debe registrar cambios importantes:

- Creación de pedido.
- Edición de pedido.
- Confirmación.
- Cancelación.
- Captura interna.
- Cambio posterior a hora límite.

### FR-016 — Hora límite

El sistema debe manejar una hora límite configurable, inicialmente 10:00 a.m.

Después de esa hora, el pedido puede:

- Bloquearse.
- Aceptarse como tardío.
- Requerir autorización interna.

La regla exacta debe definirse con operación.
