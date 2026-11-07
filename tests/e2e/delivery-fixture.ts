import type { Page } from '@playwright/test';

export const deliveryDate = '2026-11-02';
export const driverId = '92000000-0000-0000-0000-000000000002';
export const deliveryTripId = '51000000-0000-0000-0000-000000000001';
export const secondDeliveryTripId = '51000000-0000-0000-0000-000000000002';
export const partialAssignmentId = '52000000-0000-0000-0000-000000000001';
const orderId = '53000000-0000-0000-0000-000000000001';
const compositeOrderId = '53000000-0000-0000-0000-000000000002';
const lineId = '54000000-0000-0000-0000-000000000001';

function orderLine(id: string, productName: string, requestedQuantity: number, reservedQuantity = 0) {
  return { orderLineId: id, productId: `product-${id}`, productName, unitCode: 'kg', requestedQuantity,
    deliveredQuantity: 0, reservedQuantity, availableQuantity: requestedQuantity - reservedQuantity };
}

/** Fictional operational data only. HTTP stubs exercise UI contracts, not server validation. */
export function deliveryFixture() {
  let permissions = ['delivery.read.own', 'delivery.operate.own'];
  const location = { mapsUrl: 'https://www.google.com/maps/search/?api=1&query=Demo', address: 'Referencia de ejemplo',
    requestedDeliveryTime: '09:30:00', requestedDeliveryWindowStart: null, requestedDeliveryWindowEnd: null,
    hasStoreKey: true, paymentTermCode: 'cash_on_delivery', deliveryNotes: 'Entregar en recepción.',
    internalDeliveryInstructions: 'Llave de demostración: verificar con responsable.', locationVersion: 0 };
  const orders: any[] = [
    { orderId, customerId: 'customer-delivery-one', customerName: 'Cliente Reparto Uno', sequenceNumber: 1,
      routeCode: 'A', routeStopOrder: 1, orderVersion: 1, ...location,
      lines: [orderLine(lineId, 'Tortilla demo', 50, 25)] },
    { orderId: compositeOrderId, customerId: 'customer-delivery-two', customerName: 'Cliente Compuesto', sequenceNumber: 2,
      routeCode: 'A', routeStopOrder: 2, orderVersion: 1, ...location, mapsUrl: null, hasStoreKey: null,
      lines: [orderLine('54000000-0000-0000-0000-000000000002', 'Producto A', 10, 10),
        orderLine('54000000-0000-0000-0000-000000000003', 'Producto B', 2, 2)] }
  ];
  function assignment(order: any, line: any, index: number, quantity: number, loaded = false) {
    return { id: index === 1 ? partialAssignmentId : `52000000-0000-0000-0000-${String(index).padStart(12, '0')}`,
      orderId: order.orderId, orderLineId: line.orderLineId, customerId: order.customerId, customerName: order.customerName,
      sequenceNumber: order.sequenceNumber, routeStopOrder: order.routeStopOrder, productName: line.productName, unitCode: 'kg',
      requestedQuantity: line.requestedQuantity, assignedQuantity: quantity, releasedQuantity: 0,
      countedQuantity: loaded ? quantity : null, loadedQuantity: loaded ? quantity : null, deliveredQuantity: 0,
      returnedQuantity: 0, transferredOutQuantity: 0, transferredInQuantity: 0, custodyQuantity: loaded ? quantity : 0,
      pendingQuantity: line.requestedQuantity, status: loaded ? 'Loaded' : 'Assigned', version: 1,
      currentOrderVersion: 1, hasOrderChanges: false, orderIsComplete: false,
      ...location, mapsUrl: order.mapsUrl, hasStoreKey: order.hasStoreKey };
  }
  const trip: any = { id: deliveryTripId, date: deliveryDate, routeCode: 'A', routeName: 'Ruta A de ejemplo', tripNumber: 1,
    driverUserId: driverId, driverName: 'Repartidor Demo', status: 'Open', version: 1,
    createdAt: `${deliveryDate}T13:00:00Z`, closedAt: null,
    assignments: [assignment(orders[0], orders[0].lines[0], 1, 25, true),
      assignment(orders[1], orders[1].lines[0], 2, 10), assignment(orders[1], orders[1].lines[1], 3, 2)],
    events: [{ id: 'event-initial', assignmentId: partialAssignmentId, type: 'Loaded', quantity: 25, previousQuantity: null,
      reason: 'Primera vuelta parcial', notes: null, actorName: 'Repartidor Demo', occurredAt: `${deliveryDate}T13:10:00Z` }] };
  const dashboard: any = { date: deliveryDate,
    routes: [{ code: 'A', name: 'Ruta A de ejemplo', defaultDriverUserId: driverId, isActive: true, version: 1 }],
    drivers: [{ id: driverId, displayName: 'Repartidor Demo', userName: 'repartidor.demo' }],
    trips: [trip, { ...trip, id: secondDeliveryTripId, tripNumber: 2, assignments: [], events: [] }], orders };
  const writes: Array<{ path: string; body: any }> = [];
  const receipts = new Map<string, any>();
  let loseDeliveryResponse = false;
  let conflict = false;

  function refresh() {
    const assignments: any[] = dashboard.trips.flatMap((item: any) => item.assignments);
    for (const item of assignments) {
      item.custodyQuantity = Math.max(0, (item.loadedQuantity ?? 0) + item.transferredInQuantity - item.transferredOutQuantity - item.deliveredQuantity - item.returnedQuantity);
    }
    for (const order of orders) {
      for (const line of order.lines) {
        const matches = assignments.filter(item => item.orderLineId === line.orderLineId);
        line.deliveredQuantity = matches.reduce((sum, item) => sum + item.deliveredQuantity, 0);
        line.reservedQuantity = matches.reduce((sum, item) => sum + Math.max(0, item.assignedQuantity - item.releasedQuantity - item.deliveredQuantity - item.returnedQuantity), 0);
        line.availableQuantity = Math.max(0, line.requestedQuantity - line.deliveredQuantity - line.reservedQuantity);
        for (const item of matches) item.pendingQuantity = Math.max(0, line.requestedQuantity - line.deliveredQuantity);
      }
      const orderIsComplete = order.lines.every((line: any) => line.deliveredQuantity >= line.requestedQuantity);
      for (const item of assignments.filter(value => value.orderId === order.orderId)) item.orderIsComplete = orderIsComplete;
    }
  }

  async function install(page: Page, isSystemAdministrator = false) {
    await page.route('**/api/admin/security/me', route => route.fulfill({ json: { userId: driverId, permissions, isSystemAdministrator } }));
    await page.route(/\/api\/(?:admin\/)?delivery(?:[/?]|$)/, async route => {
      const request = route.request(), url = new URL(request.url()), path = url.pathname;
      refresh();
      if (request.method() === 'GET') {
        const id = path.match(/\/trips\/([^/]+)$/)?.[1];
        const management = path.startsWith('/api/admin/');
        const trips = management ? dashboard.trips : dashboard.trips.filter((item: any) => item.driverUserId === driverId);
        const assignedOrders = new Set(trips.flatMap((item: any) => item.assignments.map((assignment: any) => assignment.orderId)));
        const value = management ? dashboard : { ...dashboard, routes: [], drivers: [], trips,
          orders: orders.filter(order => assignedOrders.has(order.orderId)) };
        return route.fulfill({ json: id ? trips.find((item: any) => item.id === id) : value });
      }
      const body = request.postDataJSON(); writes.push({ path, body });
      if (receipts.has(body.operationId)) return route.fulfill({ json: { ...receipts.get(body.operationId), replayed: true } });
      if (conflict) return route.fulfill({ status: 409, json: { error: 'El pedido cambió. Recarga y revisa la versión actual.' } });
      const parts = path.split('/'), action = parts.at(-1);
      let result: any = { id: `command-${writes.length}`, version: null, replayed: false };
      const targetAssignment = dashboard.trips.flatMap((item: any) => item.assignments).find((item: any) => item.id === parts.at(-2));
      if (targetAssignment) {
        if (action === 'count') { targetAssignment.countedQuantity = body.quantity; targetAssignment.status = 'Counted'; }
        if (action === 'load') { targetAssignment.loadedQuantity = body.quantity; targetAssignment.status = 'Loaded'; }
        if (action === 'deliver') { targetAssignment.deliveredQuantity += body.quantity; targetAssignment.status = targetAssignment.deliveredQuantity >= targetAssignment.requestedQuantity ? 'Delivered' : body.quantity === 0 ? 'NotDelivered' : 'Partial'; }
        if (action === 'return') targetAssignment.returnedQuantity += body.quantity;
        result.id = targetAssignment.id; result.version = ++targetAssignment.version;
        const owner = dashboard.trips.find((item: any) => item.assignments.includes(targetAssignment)); owner.version++;
        owner.events.push({ id: `event-${writes.length}`, assignmentId: targetAssignment.id,
          type: ({ count: 'Counted', load: 'Loaded', deliver: 'Delivered', return: 'Returned' } as any)[action!] ?? action,
          quantity: body.quantity, previousQuantity: null, reason: body.reason, notes: body.notes,
          actorName: 'Repartidor Demo', occurredAt: `${deliveryDate}T15:30:00Z` });
      } else if (action === 'assignments') {
        const destination = dashboard.trips.find((item: any) => item.id === parts.at(-2));
        const order = orders.find(item => item.orderId === body.orderId), line = order.lines.find((item: any) => item.orderLineId === body.orderLineId);
        const item = assignment(order, line, 10 + writes.length, body.quantity); destination.assignments.push(item); destination.version++;
        result.id = item.id; result.version = destination.version;
      } else if (action === 'trips') {
        const item = { ...trip, id: `51000000-0000-0000-0000-${String(dashboard.trips.length + 1).padStart(12, '0')}`,
          tripNumber: body.tripNumber, driverUserId: body.driverUserId || driverId, version: 1, assignments: [], events: [] };
        dashboard.trips.push(item); result.id = item.id; result.version = 1;
      } else if (action === 'close') {
        const item = dashboard.trips.find((item: any) => item.id === parts.at(-2)); item.status = 'Closed'; item.closedAt = `${deliveryDate}T18:00:00Z`; result.id = item.id; result.version = ++item.version;
      } else if (action === 'routes') {
        dashboard.routes.push({ code: body.code, name: body.name, defaultDriverUserId: body.defaultDriverUserId,
          isActive: true, version: 1 });
      } else if (action === 'location') {
        const customerId = parts.at(-2), order = orders.find(item => item.customerId === customerId);
        order.mapsUrl = body.mapsUrl; order.address = body.address; order.locationVersion++;
      }
      refresh(); receipts.set(body.operationId, result);
      if (action === 'deliver' && loseDeliveryResponse) { loseDeliveryResponse = false; return route.abort('failed'); }
      return route.fulfill({ json: result });
    });
  }
  return { dashboard, trip, orders, writes, install, refresh,
    setPermissions: (value: string[]) => { permissions = value; },
    loseNextDeliveryResponse: () => { loseDeliveryResponse = true; },
    setConflict: (value: boolean) => { conflict = value; } };
}
