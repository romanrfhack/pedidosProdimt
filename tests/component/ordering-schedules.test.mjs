import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createEnvironmentInjector, runInInjectionContext, ChangeDetectorRef } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Subject } from 'rxjs';
import { AdminOrderingSchedulesComponent } from '../../apps/prodimt-pedidos-web/src/app/admin-ordering-schedules.component.ts';

const snapshot = () => ({
  routes: [{ routeCode: 'R1', cutoff: '10:00:00', version: 1 }],
  customers: [
    { customerId: 'a', customerName: 'Cliente A', routeCode: 'R1', cutoff: '09:00:00', version: 1 },
    { customerId: 'b', customerName: 'Cliente B', routeCode: 'R1', cutoff: '11:00:00', version: 1 }
  ]
});
function setup(t) {
  const gets = [], puts = [];
  let checks = 0;
  const http = {
    get(url) { const response = new Subject(); gets.push({ url, response }); return response; },
    put(url, body) { const response = new Subject(); puts.push({ url, body, response }); return response; }
  };
  const injector = createEnvironmentInjector([
    { provide: HttpClient, useValue: http },
    { provide: ChangeDetectorRef, useValue: { detectChanges: () => checks++ } }
  ]);
  const component = runInInjectionContext(injector, () => new AdminOrderingSchedulesComponent());
  t.after(() => { component.ngOnDestroy?.(); injector.destroy(); });
  gets[0].response.next(snapshot());
  return { component, gets, puts, checks: () => checks };
}
function savedA() {
  const value = snapshot();
  value.customers[0].cutoff = '08:00:00';
  value.customers[0].version = 2;
  return value;
}

test('guardar A conserva borrador B y búsqueda; mantiene bloqueo hasta recibir las versiones', t => {
  const { component: c, gets, puts } = setup(t);
  c.data.customers[0].cutoff = '08:00';
  c.data.customers[1].cutoff = '07:30';
  c.search = 'Cliente B';
  c.saveCustomer(c.data.customers[0]);
  assert.deepEqual(puts[0].body, { cutoff: '08:00:00', expectedVersion: 1 });
  puts[0].response.next(null);
  assert.equal(c.saving, true);
  assert.equal(c.loading, true);
  c.saveCustomer(c.data.customers[1]);
  assert.equal(puts.length, 1);
  gets[1].response.next(savedA());
  assert.equal(c.saving, false);
  assert.equal(c.data.customers[0].version, 2);
  assert.equal(c.data.customers[1].cutoff, '07:30');
  assert.equal(c.data.customers[1].version, 1);
  assert.equal(c.changed(c.data.customers[0]), false);
  assert.equal(c.changed(c.data.customers[1]), true);
  assert.equal(c.visibleCustomers[0].customerId, 'b');
  c.saveCustomer(c.data.customers[1]);
  assert.deepEqual(puts[1].body, { cutoff: '07:30:00', expectedVersion: 1 });
});

test('un refresco conserva borradores y actualiza sólo tarjetas sin cambios', t => {
  const { component: c, gets } = setup(t);
  c.data.customers[1].cutoff = '08:15';
  c.refresh();
  const incoming = snapshot();
  incoming.customers[0].cutoff = '12:00:00';
  incoming.customers[0].version = 2;
  gets[1].response.next(incoming);
  assert.equal(c.data.customers[0].cutoff, '12:00:00');
  assert.equal(c.data.customers[0].version, 2);
  assert.equal(c.data.customers[1].cutoff, '08:15');
});

test('409 conserva borrador, muestra versión actual y exige elección antes de volver a guardar', t => {
  const { component: c, gets, puts } = setup(t);
  c.data.customers[0].cutoff = '08:00';
  c.saveCustomer(c.data.customers[0]);
  puts[0].response.error(new HttpErrorResponse({ status: 409, error: { error: 'El horario cambió.' } }));
  assert.equal(c.saving, true);
  const incoming = snapshot();
  incoming.customers[0].cutoff = '12:00:00';
  incoming.customers[0].version = 2;
  gets[1].response.next(incoming);
  const draft = c.data.customers[0];
  assert.equal(draft.cutoff, '08:00');
  assert.equal(draft.version, 1);
  assert.equal(c.hasConflict(draft), true);
  assert.match(c.conflictMessage(draft), /12:00/);
  c.saveCustomer(draft);
  assert.equal(puts.length, 1);
  c.keepDraft(draft);
  assert.equal(draft.cutoff, '08:00');
  assert.equal(draft.version, 2);
  c.saveCustomer(draft);
  assert.deepEqual(puts[1].body, { cutoff: '08:00:00', expectedVersion: 2 });
});

test('usar versión actual descarta sólo el borrador elegido', t => {
  const { component: c, gets } = setup(t);
  c.data.customers[0].cutoff = '08:00';
  c.data.customers[1].cutoff = '07:00';
  c.refresh();
  const incoming = snapshot();
  incoming.customers[0].cutoff = '12:00:00';
  incoming.customers[0].version = 2;
  gets[1].response.next(incoming);
  c.useCurrent(c.data.customers[0]);
  assert.equal(c.data.customers[0].cutoff, '12:00:00');
  assert.equal(c.data.customers[0].version, 2);
  assert.equal(c.hasConflict(c.data.customers[0]), false);
  assert.equal(c.changed(c.data.customers[0]), false);
  assert.equal(c.data.customers[1].cutoff, '07:00');
});

test('un cambio concurrente después de PUT tampoco se rebasea silenciosamente', t => {
  const { component: c, gets, puts } = setup(t);
  c.data.customers[0].cutoff = '08:00';
  c.saveCustomer(c.data.customers[0]);
  puts[0].response.next(null);
  const incoming = savedA();
  incoming.customers[0].cutoff = '13:00:00';
  incoming.customers[0].version = 3;
  gets[1].response.next(incoming);
  assert.equal(c.hasConflict(c.data.customers[0]), true);
  assert.equal(c.data.customers[0].cutoff, '08:00');
  assert.equal(c.data.customers[0].version, 1);
});

test('GET fallido tras PUT conserva cambios y exige recuperar versiones antes de guardar', t => {
  const { component: c, gets, puts } = setup(t);
  c.data.customers[0].cutoff = '08:00';
  c.data.customers[1].cutoff = '07:30';
  c.saveCustomer(c.data.customers[0]);
  puts[0].response.next(null);
  gets[1].response.error(new HttpErrorResponse({ status: 503 }));
  assert.equal(c.needsRefresh, true);
  assert.equal(c.saving, false);
  assert.equal(c.data.customers[1].cutoff, '07:30');
  c.saveCustomer(c.data.customers[1]);
  assert.equal(puts.length, 1);
  c.refresh();
  gets[2].response.next(savedA());
  assert.equal(c.needsRefresh, false);
  assert.equal(c.data.customers[1].cutoff, '07:30');
  // The unresolved successful write must be reviewed explicitly when its refresh failed.
  assert.equal(c.hasConflict(c.data.customers[0]), true);
});

test('borrar excepción envía null y hereda el horario guardado, no un borrador de ruta', t => {
  const { component: c, gets, puts } = setup(t);
  c.data.routes[0].cutoff = '07:00';
  c.data.customers[0].cutoff = '';
  assert.equal(c.inherited(c.data.customers[0]), '10:00');
  c.saveCustomer(c.data.customers[0]);
  assert.deepEqual(puts[0].body, { cutoff: null, expectedVersion: 1 });
  puts[0].response.next(null);
  const incoming = snapshot();
  incoming.customers[0].cutoff = null;
  incoming.customers[0].version = 2;
  gets[1].response.next(incoming);
  assert.equal(c.data.routes[0].cutoff, '07:00');
  assert.equal(c.inherited(c.data.customers[0]), '10:00');
  assert.equal(c.data.customers[0].cutoff, null);
});

test('guardar ruta actualiza herencia efectiva conservando el borrador del cliente', t => {
  const { component: c, gets, puts } = setup(t);
  c.data.routes[0].cutoff = '08:00';
  c.data.customers[1].cutoff = '';
  c.saveRoute(c.data.routes[0]);
  assert.deepEqual(puts[0].body, { routeCode: 'R1', cutoff: '08:00:00', expectedVersion: 1 });
  puts[0].response.next(null);
  const incoming = snapshot();
  incoming.routes[0].cutoff = '08:00:00';
  incoming.routes[0].version = 2;
  gets[1].response.next(incoming);
  assert.equal(c.inherited(c.data.customers[1]), '08:00');
  assert.equal(c.data.customers[1].cutoff, '');
  assert.equal(c.changed(c.data.routes[0]), false);
});

test('un registro desaparecido conserva el borrador hasta descartarlo explícitamente', t => {
  const { component: c, gets, puts } = setup(t);
  c.data.customers[1].cutoff = '08:00';
  c.refresh();
  const incoming = snapshot(); incoming.customers.pop();
  gets[1].response.next(incoming);
  const orphan = c.data.customers[1];
  assert.equal(orphan.cutoff, '08:00');
  assert.equal(c.hasConflict(orphan), true);
  assert.equal(c.available(orphan), false);
  c.keepDraft(orphan);
  c.saveCustomer(orphan);
  assert.equal(puts.length, 0);
  c.useCurrent(orphan);
  assert.equal(c.data.customers.length, 1);
});

test('lecturas sustituidas y destrucción no publican respuestas obsoletas', t => {
  const { component: c, gets, checks } = setup(t);
  c.load();
  c.load();
  const newer = snapshot(); newer.routes[0].cutoff = '12:00:00'; newer.routes[0].version = 2;
  gets[2].response.next(newer);
  const before = checks();
  gets[1].response.next(snapshot());
  assert.equal(c.data.routes[0].cutoff, '12:00:00');
  assert.equal(checks(), before);
  c.refresh();
  c.ngOnDestroy();
  gets[3].response.next(snapshot());
  assert.equal(checks(), before);
  assert.equal(c.data.routes[0].cutoff, '12:00:00');
});
