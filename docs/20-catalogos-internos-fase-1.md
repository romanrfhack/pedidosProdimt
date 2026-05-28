# 20 — Catalogos internos Fase 1

Fecha: 2026-05-28

## Objetivo

Permitir que administracion prepare un piloto con clientes reales sin editar SQL directamente ni depender solo del seed demo.

Todos los endpoints de esta seccion requieren JWT con `AdminAccess`. Un JWT de cliente recibe `403` y las pantallas de cliente no muestran catalogos ni maquina asignada.

## Catalogos disponibles

- Clientes.
- Productos o moldes.
- Productos frecuentes por cliente.
- Maquinas.
- Asignacion interna cliente-maquina.
- Tokens de acceso de cliente.
- Usuarios administrativos basicos por API.
- Carga masiva controlada por CSV para preparar datos de piloto.

## Endpoints principales

Clientes:

- `GET /api/admin/customers`
- `GET /api/admin/customers/{customerId}`
- `POST /api/admin/customers`
- `PUT /api/admin/customers/{customerId}`
- `PATCH /api/admin/customers/{customerId}/activate`
- `PATCH /api/admin/customers/{customerId}/deactivate`

Productos:

- `GET /api/admin/products`
- `GET /api/admin/products/{productId}`
- `POST /api/admin/products`
- `PUT /api/admin/products/{productId}`
- `PATCH /api/admin/products/{productId}/activate`
- `PATCH /api/admin/products/{productId}/deactivate`

Maquinas:

- `GET /api/admin/machines`
- `GET /api/admin/machines/{machineId}`
- `POST /api/admin/machines`
- `PUT /api/admin/machines/{machineId}`
- `PATCH /api/admin/machines/{machineId}/activate`
- `PATCH /api/admin/machines/{machineId}/deactivate`

Configuracion de cliente:

- `GET /api/admin/customers/{customerId}/frequent-products`
- `PUT /api/admin/customers/{customerId}/frequent-products`
- `GET /api/admin/customers/{customerId}/machine-assignments`
- `PUT /api/admin/customers/{customerId}/machine-assignments`
- `GET /api/admin/customers/{customerId}/access-tokens`
- `POST /api/admin/customers/{customerId}/access-tokens`
- `PATCH /api/admin/customers/{customerId}/access-tokens/{tokenId}/revoke`

Usuarios admin basicos:

- `GET /api/admin/users`
- `POST /api/admin/users`
- `PATCH /api/admin/users/{userId}/activate`
- `PATCH /api/admin/users/{userId}/deactivate`

## Que puede editar administracion

Clientes:

- nombre,
- codigo externo opcional para importacion,
- telefono,
- estado activo/inactivo,
- hora deseada de entrega,
- ventana deseada de entrega,
- notas de entrega.

Productos:

- nombre,
- codigo externo opcional para importacion,
- descripcion,
- estado activo/inactivo.

Maquinas:

- numero,
- codigo externo opcional para importacion,
- nombre,
- estado activo/inactivo.

Configuracion de cliente:

- productos frecuentes activos/inactivos,
- cantidad default,
- orden de visualizacion,
- maquinas asignadas,
- maquina default,
- notas internas de asignacion,
- tokens de acceso.

## Productos frecuentes

`PUT /api/admin/customers/{customerId}/frequent-products` reemplaza la configuracion completa enviada.

Reglas:

- No se permite repetir `productId` para el mismo cliente.
- No se permiten cantidades default negativas.
- `sortOrder` menor o igual a cero se normaliza por posicion.
- Quitar un producto frecuente no borra historico de pedidos.
- La vista cliente solo muestra productos frecuentes activos y productos activos.

## Maquinas

La maquina es dato interno.

Reglas:

- Solo puede existir una asignacion default por cliente.
- Una maquina inactiva no puede quedar como default.
- La asignacion default se usa para nuevas lineas de pedido.
- El cliente nunca recibe `machine`, `machineId`, `assignedMachineId` ni datos equivalentes.
- Las maquinas pueden aparecer en detalle administrativo y configuracion interna.

## Tokens de cliente

Administracion puede crear y revocar tokens.

Reglas:

- El token plano solo se muestra en la respuesta de creacion.
- La base guarda `TokenHash`, no texto plano.
- La expiracion es opcional.
- Un token revocado no permite login.
- Un cliente inactivo no puede autenticarse aunque su token siga activo.
- No se envia WhatsApp ni mensaje automatico.

## Auditoria

Los cambios de catalogo se registran en `AuditLogs`, separada de `OrderAuditLogs`.

Eventos cubiertos:

- `CustomerCreated`, `CustomerUpdated`, `CustomerActivated`, `CustomerDeactivated`.
- `ProductCreated`, `ProductUpdated`, `ProductActivated`, `ProductDeactivated`.
- `MachineCreated`, `MachineUpdated`, `MachineActivated`, `MachineDeactivated`.
- `CustomerFrequentProductsUpdated`.
- `CustomerMachineAssignmentsUpdated`.
- `CustomerAccessTokenCreated`, `CustomerAccessTokenRevoked`.
- `AdminUserCreated`, `AdminUserActivated`, `AdminUserDeactivated`.
- `BulkImportApplied`.
- `CustomerImportedCreated`, `CustomerImportedUpdated`.
- `ProductImportedCreated`, `ProductImportedUpdated`.
- `MachineImportedCreated`, `MachineImportedUpdated`.
- `CustomerFrequentProductsImported`.
- `CustomerMachineAssignmentsImported`.

## Carga masiva controlada

La carga masiva esta documentada en `docs/21-carga-masiva-controlada-fase-1.md`.

Endpoints protegidos con `AdminAccess`:

- `GET /api/admin/import/templates`
- `POST /api/admin/import/{importType}/validate`
- `POST /api/admin/import/{importType}/apply`

Tipos soportados:

- `customers`
- `products`
- `customer-frequent-products`
- `machines`
- `customer-machine-assignments`

`validate` no modifica base. `apply` vuelve a validar y solo guarda si no hay errores bloqueantes. Las plantillas viven en `docs/import-templates/`.

## UI Angular

La ruta administrativa es:

```text
/admin/catalogos
```

La ruta de carga masiva es:

```text
/admin/importacion
```

Incluye secciones para:

- clientes,
- productos,
- maquinas,
- configuracion de cliente.

La configuracion de cliente incluye productos frecuentes, maquinas asignadas y tokens de acceso. Usuarios admin basicos quedaron solo como API protegida en esta fase.

## Limitaciones actuales

- No hay importacion completa de Excel.
- No hay importacion directa ciega del `.xlsm`.
- No hay importacion de tokens planos por CSV.
- No hay CRUD de canales de venta.
- No hay roles avanzados.
- No hay recuperacion o cambio completo de contrasena.
- No hay rotacion automatica de tokens.
- No hay WhatsApp real ni envio de enlaces.
- No hay produccion por maquina completa.
- No hay embarques, repartidores, estadisticas avanzadas, pagos ni facturacion.

## Pendiente recomendado

- Probar carga masiva con una copia depurada de datos reales antes del piloto.
- Agregar pantalla de usuarios admin solo cuando haya reglas claras de operacion.
- Agregar rotacion de token si se necesita reemplazo controlado sin crear uno nuevo manualmente.
- Agregar cambio administrativo de maquina por pedido o linea cuando entre en alcance.
