import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChangeDetectorRef, DestroyRef, Injector, runInInjectionContext } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { CollectionApiService, parseCollectionAmount } from '../../apps/prodimt-pedidos-web/src/app/collection-api.service.ts';
import { DeliveryApiService } from '../../apps/prodimt-pedidos-web/src/app/delivery-api.service.ts';
import { DeliveryWorkspaceComponent } from '../../apps/prodimt-pedidos-web/src/app/delivery-workspace.component.ts';
import { CashRegisterApiService } from '../../apps/prodimt-pedidos-web/src/app/cash-register-api.service.ts';
import { CashRegisterComponent } from '../../apps/prodimt-pedidos-web/src/app/cash-register.component.ts';
import { AuthService } from '../../apps/prodimt-pedidos-web/src/app/auth.service.ts';
import { EffectivePermissionsService } from '../../apps/prodimt-pedidos-web/src/app/effective-permissions.service.ts';

class MemoryStorage {
  values = new Map();
  get length() { return this.values.size; }
  key(index) { return [...this.values.keys()][index] ?? null; }
  getItem(key) { return this.values.get(key) ?? null; }
  setItem(key, value) { this.values.set(key, value); }
  removeItem(key) { this.values.delete(key); }
}
const firstId = '10000000-0000-4000-8000-000000000001';
const secondId = '10000000-0000-4000-8000-000000000002';
const userId = 'driver-one';
const pending = (id = firstId) => ({ operationId: id, method: 'POST', path: '/api/delivery/assignments/assignment-one/deliver',
  body: { operationId: id, quantity: 25, collectedAmount: 400, collectionVersion: 'snapshot-one' },
  title: 'Registrar entrega', management: false, tripId: 'trip-one', summary: 'Cliente de prueba' });

function globals() {
  globalThis.localStorage = new MemoryStorage();
  globalThis.sessionStorage = new MemoryStorage();
  globalThis.window = { setInterval: () => 1, clearInterval() {}, addEventListener() {}, removeEventListener() {}, confirm: () => true };
  globalThis.document = { hidden: false, addEventListener() {}, removeEventListener() {} };
}
const preview = overrides => ({ paymentTermCode: 'cash_on_delivery', customerId: 'customer-one', customerName: 'Cliente de prueba',
  currencyCode: 'MXN', ticketLines: [{ orderId: 'order-one', orderLineId: 'line-one', productName: 'Tortilla',
    unitCode: 'kg', quantity: 50, unitPrice: 10, amount: 500 }], ticketTotal: 500, priorBalance: 300,
  collectedAmount: 0, suggestedAmount: 800, balance: 800, version: 'snapshot-one', pricePending: false, amountDue: 800,
  creditBalance: 0, ...overrides });

function deliverySetup(options = {}) {
  globals();
  const assignment = { id: 'assignment-one', orderId: 'order-one', orderLineId: 'line-one', customerId: 'customer-one',
    customerName: 'Cliente de prueba', productName: 'Tortilla', paymentTermCode: options.credit ? 'credit' : 'cash_on_delivery',
    unitCode: 'kg', countedQuantity: 25, loadedQuantity: 25, assignedQuantity: 25, releasedQuantity: 0,
    deliveredQuantity: 0, returnedQuantity: 0, transferredInQuantity: 0, transferredOutQuantity: 0, custodyQuantity: 25,
    requestedQuantity: 50, pendingQuantity: 50, version: 1, currentOrderVersion: 1, status: 'Loaded' };
  const trip = { id: 'trip-one', date: '2026-11-22', routeCode: 'A', routeName: 'Ruta A', tripNumber: 1,
    status: 'Open', closedAt: null, assignments: [assignment], events: [], plannedOrders: [] };
  const dashboard = { date: trip.date, trips: [trip], orders: [], routes: [], drivers: [] };
  const calls = [], reads = [], callbacks = [];
  const api = { getDashboard: () => of(dashboard), pending: () => null, remember() {}, clear() {},
    execute: command => { calls.push(structuredClone(command)); return options.reject ? throwError(() => options.reject) : of({ id: 'receipt' }); } };
  const collections = { preview: (...args) => {
    reads.push(args);
    return options.previewSource ?? of(preview(options.preview));
  } };
  const injector = Injector.create({ providers: [
    { provide: DeliveryApiService, useValue: api }, { provide: CollectionApiService, useValue: collections },
    { provide: AuthService, useValue: { isAdmin: () => true } },
    { provide: EffectivePermissionsService, useValue: { userId: () => userId, has: () => true } },
    { provide: ActivatedRoute, useValue: { snapshot: { data: {}, queryParamMap: new Map() } } },
    { provide: ChangeDetectorRef, useValue: { detectChanges() {} } },
    { provide: DestroyRef, useValue: { destroyed: false, onDestroy: callback => callbacks.push(callback) } }
  ] });
  const view = runInInjectionContext(injector, () => new DeliveryWorkspaceComponent());
  view.dialog = { nativeElement: { showModal() {}, close() {} } };
  return { view, assignment, api, calls, reads, destroy: () => callbacks.forEach(fn => fn()) };
}

test('cobra ticket completo del día más deuda aunque entrega una carga parcial; el importe es editable', () => {
  const context = deliverySetup();
  context.view.open('deliver', context.assignment);
  assert.equal(context.view.editor.quantity, 25);
  assert.equal(context.view.editor.collectionAmount, '800');
  context.view.editor.collectionAmount = '400';
  context.view.collectionEdited();
  assert.equal(context.view.collectionBalanceAfter, 400);
  context.view.save();
  assert.equal(context.calls[0].body.quantity, 25);
  assert.equal(context.calls[0].body.collectedAmount, 400);
  assert.equal(context.calls[0].body.collectionVersion, 'snapshot-one');
  context.destroy();
});

test('No pagó registra cero explícito y conserva saldo en la vista previa', () => {
  const context = deliverySetup();
  context.view.open('deliver', context.assignment);
  context.view.noPayment();
  assert.equal(context.view.editor.collectionAmount, '0');
  assert.equal(context.view.collectionBalanceAfter, 800);
  context.view.save();
  assert.equal(context.calls[0].body.collectedAmount, 0);
  context.destroy();
});

test('precio pendiente exige monto manual y permite cobrar un adeudo sin convertir precio nulo en cero', () => {
  const context = deliverySetup({ preview: { pricePending: true, ticketTotal: null, amountDue: null, balance: null,
    suggestedAmount: null, ticketLines: [{ productName: 'Sin precio', quantity: 50, unitPrice: null, amount: null }] } });
  context.view.open('deliver', context.assignment);
  assert.equal(context.view.editor.collectionAmount, '');
  context.view.save();
  assert.equal(context.calls.length, 0);
  assert.match(context.view.editor.error, /importe cobrado/);
  context.view.editor.collectionAmount = '600';
  context.view.save();
  assert.equal(context.calls[0].body.collectedAmount, 600);
  context.destroy();
});

test('clientes de crédito conservan la entrega y no abren preview ni envían un cobro', () => {
  const context = deliverySetup({ credit: true });
  context.view.open('deliver', context.assignment);
  assert.equal(context.reads.length, 0);
  context.view.save();
  assert.equal(Object.hasOwn(context.calls[0].body, 'collectedAmount'), false);
  context.destroy();
});

test('reconsultar kilos conserva el importe manual y no usa respuestas de otra captura', () => {
  const response = new Subject();
  const context = deliverySetup({ previewSource: response });
  context.view.open('deliver', context.assignment);
  response.next(preview());
  context.view.editor.collectionAmount = '600';
  context.view.collectionEdited();
  context.view.refreshCollectionPreview();
  response.next(preview({ suggestedAmount: 750, version: 'new-version' }));
  assert.equal(context.view.editor.collectionAmount, '600');
  assert.equal(context.view.editor.collectionPreview.version, 'new-version');
  context.view.closeEditor();
  response.next(preview({ suggestedAmount: 99 }));
  assert.equal(context.view.editor, null);
  context.destroy();
});

test('rechazo ambiguo de un reintento monetario conserva el cuerpo y su referencia', () => {
  const context = deliverySetup({ reject: new HttpErrorResponse({ status: 409, error: { retrySameOperation: true } }) });
  context.view.execute(pending(), false);
  assert.deepEqual(context.view.recovery, pending());
  context.destroy();
});

test('las referencias monetarias sobreviven al cierre de sesión de pestaña y se retiran individualmente', () => {
  globals();
  const injector = Injector.create({ providers: [{ provide: HttpClient, useValue: {} }] });
  const service = runInInjectionContext(injector, () => new DeliveryApiService());
  service.remember(userId, pending());
  sessionStorage.values.clear();
  assert.deepEqual(service.pending(userId), pending());
  localStorage.setItem('prodimt.delivery.collection.pending.v1.' + userId + '.' + secondId, JSON.stringify(pending(secondId)));
  service.remember(userId, pending());
  service.clear(userId, pending());
  assert.deepEqual(service.pending(userId), pending(secondId));
  assert.equal(service.pending('other-user'), null);
  injector.destroy();
});

test('el importe admite cero y centavos en coma, y rechaza decimales ocultos o importes ambiguos', () => {
  for (const [input, expected] of [['0', 0], ['400,50', 400.5], ['600.01', 600.01]]) assert.equal(parseCollectionAmount(input), expected);
  for (const input of ['', '-1', '1e3', '5.001', '1,000.00']) assert.equal(parseCollectionAmount(input), null);
});

test('Caja exige motivo para diferencia y registra el monto contado con la versión consultada', async () => {
  globals();
  const callbacks = [], writes = [];
  const trip = { tripId: 'trip-one', date: '2026-11-22', routeCode: 'A', routeName: 'Ruta A', tripNumber: 1,
    driverUserId: userId, driverName: 'Chofer', departedAt: '2026-11-22T14:00:00Z', status: 'Open',
    version: 'cash-version', expectedAmount: 500, accountedAmount: 0, receivedAmount: 0, pendingAmount: 500,
    difference: 0, receipts: [] };
  const dashboard = { date: trip.date, routeCode: null, trips: [trip], expectedAmount: 500, receivedAmount: 0,
    pendingAmount: 500, difference: 0, myReceivedAmount: 0, customers: [] };
  const api = { dashboard: () => of(dashboard), receive: request => { writes.push(structuredClone(request)); return of({ id: 'receipt' }); } };
  const injector = Injector.create({ providers: [
    { provide: CashRegisterApiService, useValue: api }, { provide: CollectionApiService, useValue: {} },
    { provide: AuthService, useValue: { isAdmin: () => true } },
    { provide: EffectivePermissionsService, useValue: { userId: () => userId, has: () => true } },
    { provide: ChangeDetectorRef, useValue: { detectChanges() {} } },
    { provide: DestroyRef, useValue: { destroyed: false, onDestroy: callback => callbacks.push(callback) } }
  ] });
  const view = runInInjectionContext(injector, () => new CashRegisterComponent());
  view.element = { nativeElement: { open: false, showModal() {}, close() {}, querySelector: () => null } };
  await new Promise(resolve => setImmediate(resolve));
  view.receive(trip);
  assert.equal(view.received, '500');
  view.received = '450';
  assert.equal(view.receiptDifference, 50);
  view.saveReceipt(trip);
  assert.equal(writes.length, 0);
  assert.match(view.dialogError, /motivo/);
  view.reason = 'Faltaron 50 pesos en el efectivo recibido';
  view.saveReceipt(trip);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(writes[0].declaredAmount, 500);
  assert.equal(writes[0].receivedAmount, 450);
  assert.equal(writes[0].expectedVersion, 'cash-version');
  assert.equal(view.pending, null);
  assert.equal(localStorage.length, 0);
  callbacks.forEach(fn => fn());
  injector.destroy();
});
