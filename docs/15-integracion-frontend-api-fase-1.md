# 15 — Integracion frontend/API Fase 1

Fecha: 2026-05-28

## Estado

El primer flujo funcional de Fase 1 ya esta conectado entre Angular y la API.

Cliente:

- `POST /api/auth/customer-token` intercambia token demo por JWT de cliente.
- `GET /api/customer-orders/{customerId}/today` carga cliente, productos frecuentes, cantidades sugeridas y resumen del pedido actual del dia.
- `POST /api/customer-orders/{customerId}/submit` envia pedido real.
- `POST /api/customer-orders/{customerId}/no-order` registra `NoOrder`.
- La UI muestra carga, errores, confirmacion, pedido tardio, revision administrativa y pedido adicional.
- La UI valida que exista al menos una cantidad positiva antes de enviar.
- La vista de cliente no muestra maquina.
- La pantalla cliente puede iniciar con `?token=demo-customer-token` o token pegado.

Administracion:

- `POST /api/auth/admin/login` intercambia usuario/contrasena demo por JWT admin.
- `GET /api/admin/orders/today` carga pedidos del dia.
- `GET /api/admin/orders/pending-review` carga pendientes de revision.
- `GET /api/admin/orders/{orderId}` carga detalle con lineas, canal y maquina interna.
- `GET /api/admin/orders/{orderId}/audit` devuelve auditoria persistente del pedido para administracion.
- `POST /api/admin/orders/{orderId}/review` acepta, rechaza o acepta con cambios de entrega y cantidades existentes.
- `GET /api/admin/customers/pending-orders` carga clientes activos que no han respondido.
- `GET /api/admin/customers/{customerId}/order-template` carga productos frecuentes para captura administrativa.
- `POST /api/admin/customers/{customerId}/orders/submit` captura pedido por administracion.
- `POST /api/admin/customers/{customerId}/orders/no-order` registra `NoOrder` por administracion.
- Despues de una decision administrativa, la UI refresca los pendientes.
- Despues de captura administrativa o `NoOrder`, la UI refresca clientes pendientes.
- Rutas administrativas usan guard local y requieren sesion admin.
- `/admin/catalogos` permite mantener clientes, productos, maquinas y configuracion de cliente.
- La configuracion de cliente administra productos frecuentes, asignacion interna de maquina y tokens de acceso.
- La pantalla cliente no muestra navegacion de catalogos ni maquina asignada.

## Contratos relevantes

`CustomerOrderTodayResponse` incluye `currentOrder` opcional con:

- `orderId`
- `status`
- `sequenceNumber`
- `submittedAt`
- `isLate`
- `requiresAdminReview`
- `adminReviewReason`

Este resumen permite mostrar `Pedido enviado`, `Pedido pendiente de revision`, `No pedir hoy registrado` y advertencia de pedido adicional.

`AdminOrderSummaryResponse` incluye:

- `orderId`
- `customerId`
- `customerName`
- `orderDate`
- `submittedAt`
- `status`
- `sequenceNumber`
- `isLate`
- `requiresAdminReview`
- `adminReviewReason`
- `requestedDeliveryTime`
- `requestedDeliveryWindowStart`
- `requestedDeliveryWindowEnd`
- `deliveryNotes`
- `adminDecision`

`AdminOrderDetailResponse` agrega:

- `internalNotes`
- `salesChannelName`
- `salesChannelType`
- `lines` con producto, cantidad, notas y maquina asignada solo para vista administrativa.

`PendingCustomerOrderResponse` incluye cliente, telefono, hora o ventana preferida, notas de entrega y conteo de productos frecuentes.

`AdminOrderTemplateResponse` incluye cliente, preferencias de entrega y productos frecuentes con cantidad sugerida.

## Validaciones implementadas

- Se rechazan cantidades negativas.
- Se rechaza el envio si no hay ninguna linea con cantidad positiva.
- Las lineas con cantidad cero se omiten.
- Si ya existe `NoOrder` para el cliente y fecha, `no-order` devuelve el registro existente sin duplicarlo.
- Si ya existe pedido activo del dia, `no-order` responde conflicto claro.
- `review` acepta solo `Accepted`, `Rejected` y `AcceptedWithChanges`.
- `review` persiste `adminDecision`, nuevo `status` e `internalNotes`.
- `AcceptedWithChanges` aplica cambios reales de hora/notas de entrega y cantidades/notas de lineas existentes.
- Captura administrativa valida cantidades positivas, omite ceros y rechaza pedidos sin lineas positivas.
- `NoOrder` administrativo no duplica un `NoOrder` existente y responde conflicto si ya hay pedido activo.
- Los eventos principales quedan registrados en auditoria persistente.

## Configuracion Angular

La API se configura en `apps/prodimt-pedidos-web/src/environments/environment.ts`:

```ts
export const environment = {
  apiBaseUrl: 'http://127.0.0.1:5088',
  demoCustomerId: '11111111-1111-1111-1111-111111111111',
  demoCustomerToken: 'demo-customer-token',
  demoAdminUserName: 'admin',
  demoAdminPassword: 'prodimt-admin-demo'
};
```

Para probar contra API real:

```bash
dotnet run --project src/Prodimt.Pedidos.Api/Prodimt.Pedidos.Api.csproj --urls http://127.0.0.1:5088
cd apps/prodimt-pedidos-web
npm run start -- --host 127.0.0.1 --port 4200
```

Fallback de desarrollo sin SQL Server:

```bash
Persistence__Provider=InMemory dotnet run --project src/Prodimt.Pedidos.Api/Prodimt.Pedidos.Api.csproj --urls http://127.0.0.1:5088
```

## E2E

Playwright usa un mock API local controlado en `tests/e2e/mock-api.js`.

Motivo:

- Validar el comportamiento de la UI sin depender de SQL Server local.
- Mantener pruebas rapidas y deterministas para el flujo de Fase 1.
- Validar autenticacion cliente/admin sin depender de JWT reales.
- Validar navegacion de catalogos con mock API sin depender de SQL Server.

Comando:

```bash
cd tests/e2e
npm test
```

Playwright levanta:

- Mock API: `http://127.0.0.1:5088`
- Angular: `http://127.0.0.1:4210`

Si una API real ya esta usando `5088`, detenerla antes de ejecutar E2E.

## Fuera de alcance mantenido

No se implemento:

- Autenticacion definitiva con roles finos.
- WhatsApp.
- Produccion por maquina.
- Embarques.
- Repartidores.
- Estadisticas avanzadas.
- Pagos o facturacion.

## Pendiente recomendado

- Repetir la validacion SQL Server local cuando cambien migraciones, seed o endpoints. Ver `docs/16-validacion-sql-server-local.md`.
- Ampliar la edicion administrativa para agregar productos nuevos o cambiar maquina de una linea cuando se apruebe el alcance.
