# 07 — Backlog MVP

## Épica 1 — Base del repositorio

- Crear solución .NET con Clean Architecture.
- Crear proyecto Angular mobile first.
- Configurar SQL Server local/dev.
- Configurar variables de entorno.
- Crear guía de ejecución local.
- Crear pruebas base.

## Épica 2 — Catálogos

- Crear entidad Customer.
- Crear entidad Product.
- Crear relación CustomerProductPreference.
- Crear endpoints CRUD internos para catálogos.
- Cargar catálogo inicial manual o mediante seed.

## Épica 3 — Pedido del cliente

- Crear pantalla "Mi pedido de hoy".
- Mostrar productos frecuentes.
- Mostrar sugerencia.
- Permitir editar cantidades.
- Permitir agregar producto no frecuente.
- Confirmar pedido.
- Mostrar estado de pedido enviado.

## Épica 4 — Administración

- Ver pedidos por fecha.
- Filtrar por cliente.
- Ver clientes pendientes.
- Capturar pedido en nombre de cliente.
- Editar pedido antes de cierre.
- Marcar pedido tardío.

## Épica 5 — Sugerencias

- Obtener último pedido del mismo día de la semana.
- Obtener últimos 3 pedidos del mismo día.
- Calcular sugerencia simple.
- Mostrar diferencia contra sugerencia.

## Épica 6 — Exportación operativa

- Exportar pedidos del día a CSV o Excel.
- Agrupar por producto/molde.
- Agrupar por cliente.
- Preparar transición con operación.

## Épica 7 — Seguridad y auditoría

- Roles mínimos.
- Autorización por cliente.
- Auditoría de cambios.
- Proteger endpoints internos.

## Definición de terminado para MVP

- Cliente piloto puede enviar pedido desde celular.
- Administración puede ver pedido sin WhatsApp.
- Administración puede ver pendientes antes de llamadas.
- Sistema evita duplicar pedido del mismo cliente y fecha.
- Pedido queda guardado en SQL Server.
- Hay pruebas E2E del flujo principal.
