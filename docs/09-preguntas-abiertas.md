# 09 — Preguntas abiertas

Este documento separa decisiones ya resueltas de preguntas todavía abiertas.

## Decisiones ya resueltas

1. `x/X` en el Excel significa **no pidió**.
2. La hora límite inicial es **10:00 a.m.**.
3. Después de la hora límite, el pedido debe marcarse como tardío y quedar sujeto a decisión administrativa.
4. Si un cliente hace más de un pedido al día, el administrador debe decidir si lo acepta o no.
5. Algunos clientes tienen horario específico o deseado de entrega; debe registrarse en el catálogo de cliente como opcional.
6. `Mostrador` es canal de venta interno, no cliente externo.
7. La columna E del Excel es el número de máquina que atenderá el pedido.
8. La máquina asignada no debe mostrarse al cliente.
9. La Fase 1 se centrará en obtener la información del pedido del cliente.
10. Las vistas de producción, embarques y repartidores se definirán como módulos posteriores.

## Preguntas abiertas para cerrar MVP

### Operación

1. Cuando un pedido tardío se acepta, ¿debe quedar marcado permanentemente como tardío para reportes?
2. ¿Qué motivos de rechazo se usarán inicialmente?
3. ¿El cliente debe recibir aviso dentro de la app cuando un pedido tardío sea rechazado o aceptado con cambio?
4. ¿La administración puede modificar cantidades al aceptar un pedido tardío o duplicado?
5. ¿La hora límite aplica igual todos los días o puede cambiar por día de la semana?
6. ¿Qué pasa si el cliente marca "No pedir hoy" y luego intenta hacer pedido después?

### Entrega

7. ¿La hora deseada de entrega se guarda como una hora exacta o como ventana, por ejemplo 08:00-09:00?
8. ¿La hora deseada de entrega puede cambiar por día de la semana?
9. ¿Quién puede modificar la hora de entrega solicitada?
10. ¿Debe existir una hora prometida por PRODIMT diferente a la hora deseada por el cliente?

### Catálogo

11. ¿Cuál es la lista oficial de moldes/productos?
12. ¿Cómo deben normalizarse equivalencias como `# 10 1/ 2`, `#10.5` y `#10½`?
13. ¿Cuál es el catálogo oficial de máquinas?
14. ¿La máquina por defecto se asigna por cliente, por cliente-producto o por ruta?

### Clientes

15. ¿Cada cliente tiene un teléfono único?
16. ¿Hay clientes con varios encargados?
17. ¿Hay clientes que compran en más de una ruta?
18. ¿Todos los clientes deben poder ver estadísticas en etapas posteriores?

### WhatsApp

19. ¿PRODIMT ya tiene WhatsApp Business Platform o solo WhatsApp normal?
20. ¿Se cuenta con consentimiento de clientes para mensajes automáticos?
21. ¿El mensaje debe permitir confirmar sin abrir la app, o basta con liga a la app?

### Departamentos posteriores

22. ¿Cuál será la primera vista interna posterior a captura: producción por máquina, embarques o repartidores?
23. ¿Qué campos necesita cada departamento?
24. ¿Deben conservarse formatos parecidos al Excel para facilitar adopción?

### Piloto

25. ¿Con cuántos clientes se probará primero?
26. ¿Qué clientes son ideales para piloto?
27. ¿Cuánto tiempo convivirá el sistema con WhatsApp manual?
