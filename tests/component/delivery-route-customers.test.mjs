import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChangeDetectorRef, createEnvironmentInjector, runInInjectionContext } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Subject } from 'rxjs';
import { DeliveryRouteCustomersComponent } from '../../apps/prodimt-pedidos-web/src/app/delivery-route-customers.component.ts';

const firstVersion = '2026-11-07T14:00:00-06:00';
const secondVersion = '2026-11-07T14:00:00.0000001-06:00';
const customers = [
  { customerId: 'a', customerName: 'Cliente A', routeCode: 'R1', routeStopOrder: 1, updatedAt: firstVersion, isActive: true },
  { customerId: 'b', customerName: 'Cliente B', routeCode: 'R2', routeStopOrder: 4, updatedAt: firstVersion, isActive: true }
];

function setup(t) {
  const gets = [], puts = [];
  const http = {
    get(url) { const response = new Subject(); gets.push({ url, response }); return response; },
    put(url, body) { const response = new Subject(); puts.push({ url, body, response }); return response; }
  };
  const injector = createEnvironmentInjector([
    { provide: HttpClient, useValue: http },
    { provide: ChangeDetectorRef, useValue: { detectChanges() {} } }
  ]);
  const component = runInInjectionContext(injector, () => new DeliveryRouteCustomersComponent());
  component.route = { code: 'R1', isActive: true };
  component.ngOnInit();
  gets[0].response.next(customers.map(row => ({ ...row })));
  t.after(() => injector.destroy());
  return { component, gets, puts };
}

test('mover un cliente reinicia su orden y la siguiente edición usa la nueva versión', t => {
  const { component, puts } = setup(t);
  component.selectedId = 'b';
  component.assignSelected();
  assert.match(puts[0].url, /\/customers\/b\/route$/);
  assert.deepEqual(puts[0].body, { routeCode: 'R1', routeStopOrder: null, expectedUpdatedAt: firstVersion });
  component.assignSelected();
  assert.equal(puts.length, 1);
  puts[0].response.next({ ...customers[1], routeCode: 'R1', routeStopOrder: null, updatedAt: secondVersion });
  const moved = component.members.find(row => row.customerId === 'b');
  assert.equal(component.selectedId, '');
  component.changeStop(moved, 2);
  component.saveStop(moved);
  assert.deepEqual(puts[1].body, { routeCode: 'R1', routeStopOrder: 2, expectedUpdatedAt: secondVersion });
});

test('refrescar tras 409 conserva el orden escrito y exige revisar la versión del cliente', t => {
  const { component, gets, puts } = setup(t);
  const member = component.members[0];
  component.changeStop(member, 3);
  component.saveStop(member);
  puts[0].response.error(new HttpErrorResponse({ status: 409 }));
  component.saveStop(member);
  assert.equal(puts.length, 1);
  component.refresh();
  gets[1].response.next([{ ...member, routeStopOrder: 2, updatedAt: secondVersion }, customers[1]]);
  const current = component.members[0];
  assert.equal(component.stop(current), 3);
  assert.equal(component.stopConflict(current), true);
  component.saveStop(current);
  assert.equal(puts.length, 1);
  component.discardStop(current);
  assert.equal(component.stop(current), 2);
  component.changeStop(current, 3);
  component.saveStop(current);
  assert.deepEqual(puts[1].body, { routeCode: 'R1', routeStopOrder: 3, expectedUpdatedAt: secondVersion });
});
