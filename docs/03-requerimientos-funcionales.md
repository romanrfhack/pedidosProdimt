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
- Hora deseada de entrega opcional.
- Ventana deseada de entrega opcional.
- Notas de entrega opcionales.
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
- Fecha de pedido.
- Fecha de entrega o producción.
- Estado.
- Líneas de pedido.
- Cantidad por producto/molde.
- Observaciones opcionales.
- Hora o ventana deseada de entrega copiada del perfil del cliente, editable por administración.

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
- Si fue enviado antes o después de la hora límite.

### FR-010 — Marcar no pedido

El cliente o un usuario interno debe poder registrar que el cliente **no pedirá hoy**.

Esta acción reemplaza el significado de `x/X` del Excel.

Criterios:

- Debe sacar al cliente de la lista de pendientes.
- Debe registrarse con fecha, hora, canal y usuario.
- No debe confundirse con una cantidad cero en una línea específica.
- Debe poder auditarse.

### FR-011 — Estado de clientes pendientes

El área administrativa debe ver qué clientes no han enviado pedido ni han indicado no pedido antes de la hora límite.

Esto reemplaza la lista mental/manual de llamadas.

### FR-012 — Captura interna en nombre del cliente

Un usuario interno debe poder capturar o editar el pedido por teléfono cuando el cliente no use la app.

El pedido debe quedar marcado con canal `CapturadoInternamente` o equivalente.

### FR-013 — Vista administrativa diaria

El sistema debe mostrar pedidos por fecha con filtros básicos:

- Cliente.
- Producto/molde.
- Estado.
- Canal de captura.
- Pendiente/confirmado/no pidió.
- Hora de captura.
- Pedido tardío.
- Requiere revisión administrativa.

### FR-014 — Exportación inicial

El sistema debe poder generar una salida operativa simple, idealmente en Excel o CSV, para transición.

La exportación debe permitir ordenar o agrupar por:

- Producto/molde.
- Cliente.
- Ruta/repartidor.
- Estado.
- Máquina asignada, solo para uso interno.

### FR-015 — Roles

Roles mínimos:

- Cliente.
- Administración.
- Producción.
- Embarques.
- Reparto.
- Consulta gerencial.

En MVP pueden implementarse primero Cliente y Administración, dejando Producción, Embarques y Reparto como vistas protegidas posteriores.

### FR-016 — Auditoría

El sistema debe registrar cambios importantes:

- Creación de pedido.
- Edición de pedido.
- Confirmación.
- Cancelación.
- Registro de no pedido.
- Captura interna.
- Cambio posterior a hora límite.
- Segundo pedido o cambio del día.
- Decisión administrativa.
- Cambio de hora o ventana de entrega.
- Cambio de máquina asignada.

### FR-017 — Hora límite

El sistema debe manejar una hora límite configurable, inicialmente 10:00 a.m.

Después de esa hora, el pedido no se rechaza automáticamente. Debe:

- Marcarse como tardío.
- Quedar con revisión administrativa pendiente.
- Permitir a administración aceptar, rechazar o aceptar con modificación de hora/condición de entrega.

### FR-018 — Segundo pedido o cambio del mismo día

Si un cliente ya tiene pedido confirmado o enviado para la fecha y quiere enviar otro, el sistema debe crear una solicitud pendiente de revisión administrativa.

La administración debe poder:

- Aceptar el pedido adicional.
- Rechazarlo.
- Integrarlo como cambio al pedido anterior.
- Aceptarlo con cambio de hora o condición de entrega.

### FR-019 — Catálogo de máquinas

El sistema debe permitir registrar máquinas de forma interna.

La máquina puede asignarse por defecto a clientes o pedidos, pero:

- El cliente no debe ver la máquina.
- La vista de producción por máquina queda fuera de la Fase 1.
- El dato se guarda desde el inicio para preparar fases posteriores.

### FR-020 — Asignación interna de máquina

El sistema debe permitir que un administrador cambie la máquina asignada a un pedido o línea de pedido en situaciones especiales.

El cambio debe quedar auditado.

### FR-021 — Canal de venta Mostrador

El sistema debe tratar `Mostrador` como canal de venta interno, no como cliente externo.

Los pedidos de mostrador deben poder registrarse internamente, pero no deben afectar estadísticas o preferencias de clientes externos.
