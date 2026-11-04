import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChangeDetectorRef, DestroyRef, Injector, runInInjectionContext } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ProductionCommandStore } from '../../apps/prodimt-pedidos-web/src/app/production-pending-command.ts';
const { AdminOperationsComponent } = await import(process.env.PRODUCTION_COMPONENT_SOURCE ?? '../../apps/prodimt-pedidos-web/src/app/admin-operations.component.ts');
import { AdminOperationsApiService } from '../../apps/prodimt-pedidos-web/src/app/admin-operations-api.service.ts';
import { AdminCatalogApiService } from '../../apps/prodimt-pedidos-web/src/app/admin-catalog-api.service.ts';
import { EffectivePermissionsService } from '../../apps/prodimt-pedidos-web/src/app/effective-permissions.service.ts';
import { AuthService } from '../../apps/prodimt-pedidos-web/src/app/auth.service.ts';

class MemoryStorage {
  values = new Map(); blocked = false; failWrites = false; failRemove = false;
  get length() { if (this.blocked) throw new Error('disabled'); return this.values.size; }
  key(index) { return [...this.values.keys()][index] ?? null; }
  getItem(key) { if (this.blocked) throw new Error('disabled'); return this.values.get(key) ?? null; }
  setItem(key, value) { if (this.blocked || this.failWrites) throw new Error('quota'); this.values.set(key, value); }
  removeItem(key) { if (this.failRemove) throw new Error('disabled'); this.values.delete(key); }
}
const date = '2026-11-02', userId = 'admin-one';
const batch = (id = '10000000-0000-4000-8000-000000000001', user = userId) => ({ version: 1, userId: user, date, kind: 'batch', request: {
  operationId: id, date, machineId: 'machine-one', productId: 'product-one', quantity: 7, notes: null
} });

function component(storage, overrides = {}) {
  globalThis.localStorage = storage;
  globalThis.window = { setInterval: () => 1, clearInterval() {}, addEventListener() {}, removeEventListener() {}, confirm: () => true };
  globalThis.document = { hidden: false, addEventListener() {}, removeEventListener() {} };
  const callbacks = [], actor = { id: userId, active: true }, calls = [], receipts = new Map();
  const plan = { date, orders: [], machineDemand: [], pendingReview: [], warnings: [] };
  const dashboard = { date, orders: [], machines: [], allocations: [], batches: [] };
  const api = { getPlan: () => of(plan), getChanges: () => of({ date, changes: [] }), getProduction: () => of(dashboard),
    registerProduction: request => { calls.push(structuredClone(request)); receipts.set(request.operationId, request); return of({ id: request.operationId, orderVersion: null, replayed: calls.length > 1 }); }, ...overrides };
  const injector = Injector.create({ providers: [
    { provide: AdminOperationsApiService, useValue: api },
    { provide: AdminCatalogApiService, useValue: { getProducts: () => of([]) } },
    { provide: EffectivePermissionsService, useValue: { userId: () => actor.id, has: () => actor.active, refresh: async () => true } },
    { provide: AuthService, useValue: { isAdmin: () => actor.active } },
    { provide: ChangeDetectorRef, useValue: { detectChanges() {} } },
    { provide: DestroyRef, useValue: { destroyed: false, onDestroy: callback => callbacks.push(callback) } },
    { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: new Map([['date', date]]) } } }
  ] });
  const view = runInInjectionContext(injector, () => new AdminOperationsComponent());
  view.productionDialog = { nativeElement: { showModal() {}, close() {} } };
  return { view, actor, api, calls, receipts, destroy: () => callbacks.forEach(fn => fn()) };
}
function prepareBatch(view) {
  view.openEditor('batch'); Object.assign(view.editor, { machineId: 'machine-one', productId: 'product-one', quantity: 7 });
}

test('F01 persists exact body per account and rejects a different pending operation without overwriting', () => {
  const storage = new MemoryStorage(), store = new ProductionCommandStore(() => storage), command = batch();
  store.remember(command); assert.deepEqual(new ProductionCommandStore(() => storage).pending(userId), [command]);
  assert.deepEqual(store.pending('another-admin'), []);
  assert.throws(() => store.remember(batch('10000000-0000-4000-8000-000000000002')), /otra operación/);
  assert.throws(() => store.remember({ ...command, request: { ...command.request, quantity: 9 } }), /recuperar/);
  assert.deepEqual(store.pending(userId), [command]);
  assert.equal(JSON.stringify([...storage.values.values()]).includes('accessToken'), false);
});

test('F01 corrupt records fail closed and remain available for diagnosis', () => {
  const storage = new MemoryStorage(), store = new ProductionCommandStore(() => storage);
  storage.setItem(`prodimt.production.pending.v1.${userId}.broken`, '{');
  assert.throws(() => store.pending(userId)); assert.equal(storage.length, 1);
  assert.throws(() => store.remember(batch())); assert.equal(storage.length, 1);
});

for (const kind of ['batch', 'allocation']) test(`F01 ${kind}: commit with lost response, destroy/recreate and replay uses same UUID and creates one record`, () => {
  const storage = new MemoryStorage(), requests = [], receipts = new Map(); let loseResponse = true;
  const send = request => {
    requests.push(structuredClone(request));
    const replayed = receipts.has(request.operationId); receipts.set(request.operationId, structuredClone(request));
    if (loseResponse) { loseResponse = false; return throwError(() => new HttpErrorResponse({ status: 0 })); }
    return of({ id: request.operationId, orderVersion: 2, replayed });
  };
  const api = kind === 'batch' ? { registerProduction: send } : { allocateProduction: send };
  const first = component(storage, api); let command;
  if (kind === 'batch') prepareBatch(first.view);
  else {
    const line = { orderLineId: 'line-one', productId: 'product-one', unitCode: 'kg', quantity: 50, pendingAllocationQuantity: 50, assignedMachineId: 'machine-one' };
    const order = { orderId: 'order-one', orderVersion: 3 };
    first.view.production.batches = [{ id: 'batch-one', machineId: 'machine-one', productId: 'product-one', unitCode: 'kg', availableQuantity: 25 }];
    first.view.openEditor('allocation', order, line); first.view.editor.quantity = 25;
  }
  first.view.saveEditor(); command = structuredClone(first.view.editor.submitted);
  assert.equal(requests.length, 1); assert.deepEqual(first.view.recovery, [command]);
  assert.equal(first.view.editor.locked, true); first.view.keepPendingAndClose(); assert.equal(first.view.editor, null);
  first.actor.active = false; assert.equal(first.view.canLeave(), true); first.destroy();
  const restored = component(storage, api);
  assert.deepEqual(restored.view.recovery, [command]); assert.equal(requests.length, 1, 'no automatic resend on construction');
  restored.view.retryPending(restored.view.recovery[0]);
  assert.deepEqual(requests[1], requests[0]); assert.equal(receipts.size, 1); assert.equal(restored.view.recovery.length, 0); assert.equal(storage.length, 0);
  restored.destroy();
});

test('F01 does not issue HTTP when durable storage cannot be written', () => {
  const storage = new MemoryStorage(); storage.failWrites = true;
  const context = component(storage); prepareBatch(context.view); context.view.saveEditor();
  assert.equal(context.calls.length, 0); assert.match(context.view.editor.error, /No se envió/); assert.equal(storage.length, 0); context.destroy();
});

test('F01 blocks new capture when reading storage fails rather than treating it as empty', () => {
  const storage = new MemoryStorage(); storage.blocked = true;
  const context = component(storage); assert.ok(context.view.storageError); assert.equal(context.view.canWrite(), false);
  context.view.openEditor('batch'); assert.equal(context.view.editor, null); assert.equal(context.calls.length, 0); context.destroy();
});

test('F01 a different account cannot replay another account command and logout does not delete it', () => {
  const storage = new MemoryStorage(), store = new ProductionCommandStore(() => storage); store.remember(batch());
  const context = component(storage); context.actor.id = 'another-admin'; context.actor.active = false;
  assert.equal(context.view.canLeave(), true); context.view.retryPending(batch()); assert.equal(context.calls.length, 0);
  assert.deepEqual(store.pending(userId), [batch()]); assert.deepEqual(store.pending('another-admin'), []); context.destroy();
});

test('F01 an initial validation rejection releases its request while a generic conflict preserves it', () => {
  const storage = new MemoryStorage(), reject = () => throwError(() => new HttpErrorResponse({ status: 409, error: { error: 'El pedido cambió.' } }));
  const first = component(storage, { registerProduction: () => throwError(() => new HttpErrorResponse({ status: 400 })) }); prepareBatch(first.view); first.view.saveEditor();
  assert.equal(first.view.editor.conflict, false); assert.equal(first.view.recovery.length, 0); assert.equal(storage.length, 0); first.destroy();
  new ProductionCommandStore(() => storage).remember(batch());
  const retry = component(storage, { registerProduction: reject }); retry.view.retryPending(retry.view.recovery[0]);
  assert.equal(retry.view.recovery.length, 1); assert.equal(storage.length, 1); assert.match(retry.view.recoveryError, /pedido cambió/); retry.destroy();
});

test('F01 confirmed result with failed cleanup keeps recovery and never opens a second command', () => {
  const storage = new MemoryStorage(); storage.failRemove = true;
  const context = component(storage); prepareBatch(context.view); context.view.saveEditor();
  assert.equal(context.calls.length, 1); assert.equal(context.view.recovery.length, 1); assert.equal(context.view.canWrite(), false);
  storage.failRemove = false; context.view.saveEditor();
  assert.equal(context.receipts.size, 1); assert.equal(context.view.recovery.length, 0); assert.equal(storage.length, 0); context.destroy();
});

test('F01 navigation guard protects drafts/in-flight commands and allows explicit logout', () => {
  const storage = new MemoryStorage(), context = component(storage); prepareBatch(context.view);
  globalThis.window.confirm = () => false; assert.equal(context.view.canLeave(), false);
  globalThis.window.confirm = () => true; assert.equal(context.view.canLeave(), true);
  context.view.saving = true; assert.equal(context.view.canLeave(), false);
  context.actor.active = false; assert.equal(context.view.canLeave(), true); context.destroy();
});


test('F01 rejects corrupted request values and extra fields before storing a replay', () => {
  const storage = new MemoryStorage(), store = new ProductionCommandStore(() => storage);
  for (const command of [
    { ...batch(), date: '2026-02-30', request: { ...batch().request, date: '2026-02-30' } },
    { ...batch(), request: { ...batch().request, quantity: 1.001 } },
    { ...batch(), request: { ...batch().request, machineId: '../other-route' } },
    { ...batch(), accessToken: 'unexpected-field' }
  ]) assert.throws(() => store.remember(command));
  assert.equal(storage.length, 0);
});

test('F01 observing a pending operation from another tab suspends an unsent editor', () => {
  const storage = new MemoryStorage(), context = component(storage); prepareBatch(context.view);
  new ProductionCommandStore(() => storage).remember(batch()); context.view.storageChanged({ key: `prodimt.production.pending.v1.${userId}.operation` });
  assert.equal(context.view.recovery.length, 1); assert.equal(context.view.canWrite(), false);
  context.view.saveEditor(); assert.equal(context.calls.length, 0); context.destroy();
});

for (const hint of [true, undefined, 0, 'false']) test(`F01 initial 409 with retrySameOperation=${String(hint)} keeps its identity`, () => {
  const storage = new MemoryStorage(), context = component(storage, { registerProduction: () => throwError(() => new HttpErrorResponse({ status: 409,
    error: { error: 'Concurrent operation', retrySameOperation: hint } })) });
  prepareBatch(context.view); context.view.saveEditor(); assert.equal(context.view.recovery.length, 1);
  assert.ok(context.view.editor.submitted); assert.equal(context.view.editor.conflict, false); context.destroy();
});

test('F01 only an explicit definitive business rejection releases an initial 409', () => {
  const storage = new MemoryStorage(), reject = () => throwError(() => new HttpErrorResponse({ status: 409, error: { error: 'Not enough stock', retrySameOperation: false } }));
  const first = component(storage, { registerProduction: reject }); prepareBatch(first.view); first.view.saveEditor();
  assert.equal(first.view.recovery.length, 0); assert.equal(first.view.editor.conflict, true); assert.equal(first.view.editor.submitted, null); first.destroy();
  new ProductionCommandStore(() => storage).remember(batch());
  const restored = component(storage, { registerProduction: reject }); restored.view.retryPending(restored.view.recovery[0]);
  assert.equal(restored.view.recovery.length, 1, 'a prior uncertain response always retains its reference'); restored.destroy();
});

test('F01 a concurrent 409 followed by replay cannot create a second logical batch', () => {
  const storage = new MemoryStorage(), receipts = new Map(), requests = [];
  const context = component(storage, { registerProduction: request => {
    requests.push(structuredClone(request));
    const replayed = receipts.has(request.operationId); receipts.set(request.operationId, request);
    return replayed ? of({ id: request.operationId, orderVersion: null, replayed })
      : throwError(() => new HttpErrorResponse({ status: 409, error: { error: 'Same request committed elsewhere', retrySameOperation: true } }));
  } });
  prepareBatch(context.view); context.view.saveEditor(); assert.equal(context.view.recovery.length, 1);
  context.view.saveEditor(); assert.equal(context.view.recovery.length, 0); assert.deepEqual(requests[1], requests[0]); assert.equal(receipts.size, 1); context.destroy();
});


test('F01 baseline regression: destroying a view after lost response must leave a recoverable command on the next view', () => {
  const storage = new MemoryStorage(), requests = [];
  const send = request => { requests.push(structuredClone(request)); return throwError(() => new HttpErrorResponse({ status: 0 })); };
  const first = component(storage, { registerProduction: send }); prepareBatch(first.view); first.view.saveEditor(); first.destroy();
  const restored = component(storage, { registerProduction: send });
  const pending = restored.view.recovery ?? [];
  assert.equal(pending.length, 1, 'a committed request with lost response must survive navigation/reload');
  assert.deepEqual(pending[0].request, requests[0]); assert.equal(requests.length, 1); restored.destroy();
});
