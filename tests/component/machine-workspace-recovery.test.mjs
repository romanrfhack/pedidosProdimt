import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChangeDetectorRef, DestroyRef, Injector, runInInjectionContext } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { MachineWorkspaceComponent } from '../../apps/prodimt-pedidos-web/src/app/machine-workspace.component.ts';
import { MachineWorkspaceApiService } from '../../apps/prodimt-pedidos-web/src/app/machine-workspace-api.service.ts';
import { EffectivePermissionsService } from '../../apps/prodimt-pedidos-web/src/app/effective-permissions.service.ts';
import { AuthService } from '../../apps/prodimt-pedidos-web/src/app/auth.service.ts';
import { AdminOperationsComponent } from '../../apps/prodimt-pedidos-web/src/app/admin-operations.component.ts';

const userId = 'machine-user-one';
const prefix = 'prodimt.machine-workspace.pending.v1.' + userId;
const firstId = '10000000-0000-4000-8000-000000000001';
const secondId = '10000000-0000-4000-8000-000000000002';
const body = operationId => ({ operationId, productId: 'product-one', quantity: 2, date: '2026-11-21' });
const command = operationId => ({ action: 'lots', body: body(operationId), machineId: 'machine-one', userId });

class MemoryStorage {
  values = new Map();
  beforeSet = null;
  get length() { return this.values.size; }
  key(index) { return [...this.values.keys()][index] ?? null; }
  getItem(key) { return this.values.get(key) ?? null; }
  setItem(key, value) {
    const interleave = this.beforeSet;
    this.beforeSet = null;
    interleave?.();
    this.values.set(key, value);
  }
  removeItem(key) { this.values.delete(key); }
}

function setup(storage, write = () => of({ id: 'receipt' })) {
  globalThis.localStorage = storage;
  globalThis.window = { setInterval: () => 1, clearInterval() {}, addEventListener() {}, removeEventListener() {}, confirm: () => true };
  globalThis.document = { hidden: false, addEventListener() {}, removeEventListener() {} };
  const actor = { id: userId };
  const callbacks = [];
  const api = { machines: () => of([]), write,
    workspace: () => of({ date: '2026-11-21', orders: [] }),
    team: () => of({ date: '2026-11-21', today: '2026-11-21', slots: [], workers: [], destinations: [] }) };
  const injector = Injector.create({ providers: [
    { provide: MachineWorkspaceApiService, useValue: api },
    { provide: EffectivePermissionsService, useValue: { userId: () => actor.id, has: () => true, refresh: async () => true } },
    { provide: AuthService, useValue: { isAdmin: () => true } },
    { provide: ChangeDetectorRef, useValue: { detectChanges() {} } },
    { provide: DestroyRef, useValue: { destroyed: false, onDestroy: callback => callbacks.push(callback) } }
  ] });
  const view = runInInjectionContext(injector, () => new MachineWorkspaceComponent());
  view.editor = { nativeElement: { close() {} } };
  return { view, actor, destroy: () => callbacks.forEach(fn => fn()) };
}

const ready = async context => {
  await Promise.resolve();
  await Promise.resolve();
  context.view.machineId = 'machine-one';
  context.view.board = { date: '2026-11-21', orders: [] };
};

test('dos pestañas que capturan simultáneamente conservan ambos recibos tras perder respuestas', async () => {
  const storage = new MemoryStorage(), calls = [];
  const loseResponse = value => {
    calls.push(structuredClone(value));
    return throwError(() => new HttpErrorResponse({ status: 0 }));
  };
  const first = setup(storage, loseResponse), second = setup(storage, loseResponse);
  await ready(first); await ready(second);
  let secondAttempt;
  storage.beforeSet = () => { secondAttempt = second.view.submit({ action: 'lots', body: body(secondId) }); };
  await first.view.submit({ action: 'lots', body: body(firstId) });
  await secondAttempt;
  assert.equal(calls.length, 2);
  assert.equal(storage.length, 2);
  assert.deepEqual([...storage.values.keys()].sort(), [prefix + '.' + firstId, prefix + '.' + secondId]);
  first.destroy(); second.destroy();

  const recoveredCalls = [];
  const recovered = setup(storage, value => { recoveredCalls.push(structuredClone(value)); return of({ id: 'replayed', replayed: true }); });
  await ready(recovered);
  assert.equal(recovered.view.pendingCount, 2);
  assert.equal(recoveredCalls.length, 0, 'la recuperación espera acción explícita');
  await recovered.view.send(recovered.view.pending, false);
  assert.equal(storage.length, 1);
  assert.equal(recovered.view.pending.body.operationId, secondId);
  await recovered.view.send(recovered.view.pending, false);
  assert.equal(storage.length, 0);
  assert.equal(recovered.view.pending, null);
  assert.deepEqual(recoveredCalls.map(item => item.body), [body(firstId), body(secondId)]);
  recovered.destroy();
});

test('una referencia histórica por usuario sigue siendo recuperable y conserva su UUID', async () => {
  const storage = new MemoryStorage();
  storage.setItem(prefix, JSON.stringify(command(firstId)));
  const calls = [];
  const context = setup(storage, value => { calls.push(structuredClone(value)); return of({ id: 'replayed' }); });
  await ready(context);
  assert.deepEqual(context.view.pending, command(firstId));
  await context.view.send(context.view.pending, false);
  assert.deepEqual(calls, [command(firstId)]);
  assert.equal(storage.length, 0);
  context.destroy();
});

test('confirmar una operación no borra la referencia concurrente de otra pestaña', () => {
  const storage = new MemoryStorage();
  storage.setItem(prefix + '.' + firstId, JSON.stringify(command(firstId)));
  storage.setItem(prefix + '.' + secondId, JSON.stringify(command(secondId)));
  const context = setup(storage);
  context.view.clearPending(command(secondId));
  assert.equal(storage.getItem(prefix + '.' + secondId), null);
  assert.equal(storage.getItem(prefix + '.' + firstId), JSON.stringify(command(firstId)));
  assert.equal(context.view.pending.body.operationId, firstId);
  context.destroy();
});

test('una referencia con clave alterada detiene nuevas capturas y conserva la evidencia', () => {
  const storage = new MemoryStorage();
  storage.setItem(prefix + '.' + secondId, JSON.stringify(command(firstId)));
  const context = setup(storage);
  assert.match(context.view.storageError, /recuperar/);
  assert.equal(context.view.canWrite, false);
  assert.equal(storage.length, 1);
  context.destroy();
});

test('los avisos de terminado agrupan máquinas, ordenan por última liberación y toleran API histórica', () => {
  const getter = Object.getOwnPropertyDescriptor(AdminOperationsComponent.prototype, 'releaseGroups').get;
  const labels = (_number, name) => name;
  assert.deepEqual(getter.call({ production: {}, machineLabel: labels }), []);
  const release = (id, machineId, quantity, createdAt) => ({ id, machineId, machineNumber: 1, machineName: machineId, quantity, createdAt });
  const groups = getter.call({ machineLabel: labels, production: { releases: [
    release('a', 'M1', 2.5, '2026-11-21T12:00:00Z'),
    release('b', 'M2', 4, '2026-11-21T13:00:00Z'),
    release('c', 'M1', 1.5, '2026-11-21T14:00:00Z')
  ] } });
  assert.deepEqual(groups.map(group => [group.machineId, group.quantity, group.releases.map(entry => entry.id)]),
    [['M1', 4, ['c', 'a']], ['M2', 4, ['b']]]);
});
