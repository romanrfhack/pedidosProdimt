# 15 — Integracion frontend/API Fase 1

Fecha: 2026-05-27

## Estado

El primer flujo funcional de Fase 1 ya esta conectado entre Angular y la API.

Cliente:

- `GET /api/customer-orders/{customerId}/today` carga cliente, productos frecuentes, cantidades sugeridas y resumen del pedido actual del dia.
- `POST /api/customer-orders/{customerId}/submit` envia pedido real.
- `POST /api/customer-orders/{customerId}/no-order` registra `NoOrder`.
- La UI muestra carga, errores, confirmacion, pedido tardio, revision administrativa y pedido adicional.
- La UI valida que exista al menos una cantidad positiva antes de enviar.
- La vista de cliente no muestra maquina.

Administracion:

- `GET /api/admin/orders/today` carga pedidos del dia.
- `GET /api/admin/orders/pending-review` carga pendientes de revision.
- `POST /api/admin/orders/{orderId}/review` acepta o rechaza desde la UI.
- Despues de una decision administrativa, la UI refresca los pendientes.

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

## Validaciones implementadas

- Se rechazan cantidades negativas.
- Se rechaza el envio si no hay ninguna linea con cantidad positiva.
- Las lineas con cantidad cero se omiten.
- Si ya existe `NoOrder` para el cliente y fecha, `no-order` devuelve el registro existente sin duplicarlo.
- Si ya existe pedido activo del dia, `no-order` responde conflicto claro.
- `review` acepta solo `Accepted`, `Rejected` y `AcceptedWithChanges`.
- `review` persiste `adminDecision`, nuevo `status` e `internalNotes`.
- La auditoria persistente queda pendiente con TODO en Application.

## Configuracion Angular

La API se configura en `apps/prodimt-pedidos-web/src/environments/environment.ts`:

```ts
export const environment = {
  apiBaseUrl: 'http://127.0.0.1:5088',
  demoCustomerId: '11111111-1111-1111-1111-111111111111'
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

- Autenticacion real.
- WhatsApp.
- Produccion por maquina.
- Embarques.
- Repartidores.
- Estadisticas avanzadas.
- Pagos o facturacion.

## Pendiente recomendado

- Validar el flujo contra SQL Server local con migracion aplicada.
- Agregar detalle de lineas en administracion.
- Implementar ajuste real de cantidades/horario para `AcceptedWithChanges`.
- Agregar auditoria persistente para pedido creado, no pedido y decision administrativa.
