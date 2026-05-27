# 07 — Backlog MVP

## Épica 1 — Base del repositorio

- Crear solución .NET con Clean Architecture.
- Crear proyecto Angular mobile first.
- Configurar SQL Server local/dev.
- Configurar variables de entorno.
- Crear guía de ejecución local.
- Crear pruebas base.
- Configurar OpenAPI/Swagger.

## Épica 2 — Catálogos

- Crear entidad Customer.
- Agregar hora o ventana deseada de entrega a Customer.
- Crear entidad Product.
- Crear entidad Machine para uso interno.
- Crear relación CustomerProductPreference.
- Agregar asignación interna de máquina por preferencia o cliente-producto.
- Crear endpoints CRUD internos para catálogos.
- Cargar catálogo inicial manual o mediante seed.
- Tratar `Mostrador` como canal interno, no como cliente.

## Épica 3 — Pedido del cliente

- Crear pantalla "Mi pedido de hoy".
- Mostrar productos frecuentes.
- Mostrar sugerencia.
- Permitir editar cantidades.
- Permitir agregar producto no frecuente.
- Confirmar pedido.
- Permitir marcar "No pedir hoy".
- Mostrar estado de pedido enviado.
- Ocultar cualquier dato de máquina en la vista del cliente.

## Épica 4 — Administración

- Ver pedidos por fecha.
- Filtrar por cliente.
- Ver clientes pendientes.
- Ver clientes que marcaron no pedido.
- Capturar pedido en nombre de cliente.
- Editar pedido antes de cierre.
- Marcar pedido tardío.
- Revisar pedidos tardíos.
- Revisar segundos pedidos o cambios del día.
- Aceptar pedido.
- Rechazar pedido.
- Aceptar con modificación de hora o condición de entrega.
- Cambiar máquina asignada de forma interna cuando sea necesario.

## Épica 5 — Sugerencias

- Obtener último pedido del mismo día de la semana.
- Obtener últimos 3 pedidos del mismo día.
- Calcular sugerencia simple.
- Mostrar diferencia contra sugerencia.

## Épica 6 — Estados y reglas de revisión

- Configurar hora límite inicial: 10:00 a.m.
- Detectar pedido tardío.
- Detectar segundo pedido del mismo día.
- Crear estado `PendingAdminReview` o equivalente.
- Registrar razón de revisión administrativa.
- Registrar decisión administrativa.
- Registrar rechazo con motivo.

## Épica 7 — Exportación operativa

- Exportar pedidos del día a CSV o Excel.
- Agrupar por producto/molde.
- Agrupar por cliente.
- Incluir máquina asignada solo en exportación interna.
- Preparar transición con operación.

## Épica 8 — Seguridad y auditoría

- Roles mínimos.
- Autorización por cliente.
- Auditoría de cambios.
- Proteger endpoints internos.
- Validar que endpoints de cliente no expongan máquina, otros clientes o datos internos.

## Definición de terminado para MVP

- Cliente piloto puede enviar pedido desde celular.
- Cliente piloto puede marcar que no pedirá hoy.
- Administración puede ver pedido sin WhatsApp.
- Administración puede ver pendientes antes de llamadas.
- Sistema identifica pedidos tardíos.
- Sistema identifica segundos pedidos o cambios del mismo día.
- Administración puede aceptar o rechazar pedidos sujetos a revisión.
- Sistema evita duplicar pedido activo del mismo cliente y fecha sin revisión administrativa.
- Pedido queda guardado en SQL Server.
- Hay pruebas E2E del flujo principal.
- La documentación se actualiza con lo construido y lo pendiente.
