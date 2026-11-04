import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChangeDetectorRef, createEnvironmentInjector, runInInjectionContext } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { Subject } from 'rxjs';
import { AdminPendingComponent } from '../../apps/prodimt-pedidos-web/src/app/admin-pending.component.ts';
import { OrderDataService } from '../../apps/prodimt-pedidos-web/src/app/order-data.service.ts';

const date = '2026-11-02';
const orderA = { id: 'order-a', orderDate: date };
const orderB = { id: 'order-b', orderDate: date };
const eventA = { id: 'event-a', summary: 'Historia de A' };
const eventB = { id: 'event-b', summary: 'Historia de B' };
const detail = order => ({
  ...order, version: 1, deliveryNotes: `Entrega ${order.id}`, internalNotes: null,
  lines: [{ id: `line-${order.id}`, productName: 'Tortilla de prueba', unitCode: 'kg', quantity: 10, notes: null }]
});

function fixture(t) {
  const audits = [];
  const lists = [];
  const otherReads = [];
  let renders = 0;
  const data = {
    loadPendingReviewOrders(serviceDate) {
      const response = new Subject();
      lists.push({ serviceDate, response });
      return response;
    },
    loadOrderAudit(id) {
      const response = new Subject();
      audits.push({ id, response });
      return response;
    },
    loadOrderDetail() {
      const response = new Subject();
      otherReads.push(response);
      return response;
    }
  };
  const injector = createEnvironmentInjector([
    { provide: OrderDataService, useValue: data },
    { provide: HttpClient, useValue: {} },
    { provide: ChangeDetectorRef, useValue: { detectChanges() { renders++; } } },
    { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: new Map([['date', date]]) } } }
  ], null);
  const component = runInInjectionContext(injector, () => new AdminPendingComponent());
  t.after(() => { if (!injector.destroyed) injector.destroy(); });
  lists[0].response.next([orderA, orderB]);
  lists[0].response.complete();
  return { component, injector, audits, lists, otherReads, renders: () => renders };
}

test('F04: seleccionar B cancela A y nunca presenta sus eventos bajo B', t => {
  const { component, audits } = fixture(t);
  component.loadAudit(orderA);
  component.loadAudit(orderB);
  assert.equal(component.selectedAuditOrderId, orderB.id);
  assert.equal(component.loadingAuditOrderId, orderB.id);
  assert.deepEqual(component.auditEvents, []);
  audits[1].response.next([eventB]);
  audits[0].response.next([eventA]);
  assert.deepEqual(component.auditEvents, [eventB]);
  assert.equal(audits[0].response.observed, false, 'se cancela la petición anterior');
  assert.equal(component.selectedAuditOrderId, orderB.id);
  assert.equal(component.loadingAuditOrderId, null);
});

test('F04: un error tardío de A no quita la carga ni muestra un error en B', t => {
  const { component, audits } = fixture(t);
  component.loadAudit(orderA);
  component.loadAudit(orderB);
  audits[0].response.error(new HttpErrorResponse({ status: 500, error: { error: 'Error de A' } }));
  assert.equal(component.loadingAuditOrderId, orderB.id);
  assert.equal(component.auditErrorMessage, null);
  assert.equal(component.errorMessage, null);
  audits[1].response.next([eventB]);
  assert.deepEqual(component.auditEvents, [eventB]);
});

test('F04: al elegir otra auditoría desaparecen los eventos anteriores mientras carga', t => {
  const { component, audits } = fixture(t);
  component.loadAudit(orderA);
  audits[0].response.next([eventA]);
  component.loadAudit(orderB);
  assert.deepEqual(component.auditEvents, []);
  assert.equal(component.loadingAuditOrderId, orderB.id);
  audits[1].response.error(new HttpErrorResponse({ status: 503, error: { error: 'Reintenta la auditoría.' } }));
  assert.equal(component.auditErrorMessage, 'Reintenta la auditoría.');
  assert.equal(component.errorMessage, null, 'el error de auditoría no oculta los pedidos');
  assert.deepEqual(component.orders, [orderA, orderB]);
  assert.equal(component.loadingAuditOrderId, null);
});

test('F04: cambiar fecha cancela auditoría y no repuebla el contexto anterior', t => {
  const { component, audits } = fixture(t);
  component.loadAudit(orderA);
  component.serviceDate = '2026-11-04';
  component.changeDate();
  audits[0].response.next([eventA]);
  assert.equal(audits[0].response.observed, false);
  assert.equal(component.selectedAuditOrderId, null);
  assert.equal(component.loadingAuditOrderId, null);
  assert.deepEqual(component.auditEvents, []);
  assert.deepEqual(component.orders, []);
});

test('F04: consultar de nuevo invalida la auditoría desde el inicio del GET', t => {
  const { component, audits, lists } = fixture(t);
  component.loadAudit(orderA);
  component.load();
  assert.equal(component.selectedAuditOrderId, null);
  audits[0].response.next([eventA]);
  assert.deepEqual(component.auditEvents, []);
  assert.equal(component.isLoading, true);
  lists[1].response.next([orderB]);
  assert.deepEqual(component.orders, [orderB]);
  assert.equal(component.selectedAuditOrderId, null);
  assert.equal(component.isLoading, false);
});

test('F04: un listado viejo no sustituye la fecha nueva ni reinicia su auditoría', t => {
  const { component, audits, lists } = fixture(t);
  component.load();
  component.serviceDate = '2026-11-04';
  component.changeDate();
  component.load();
  const newOrder = { ...orderB, orderDate: '2026-11-04' };
  lists[2].response.next([newOrder]);
  component.loadAudit(newOrder);
  audits[0].response.next([eventB]);
  lists[1].response.next([orderA]);
  assert.equal(lists[1].response.observed, false);
  assert.deepEqual(component.orders, [newOrder]);
  assert.equal(component.selectedAuditOrderId, newOrder.id);
  assert.deepEqual(component.auditEvents, [eventB]);
});

test('F04: un fallo de listado obsoleto no limpia la carga del listado vigente', t => {
  const { component, lists } = fixture(t);
  component.load();
  component.load();
  lists[1].response.error(new HttpErrorResponse({ status: 500 }));
  assert.equal(component.isLoading, true);
  assert.equal(component.errorMessage, null);
  lists[2].response.next([orderB]);
  assert.equal(component.isLoading, false);
  assert.deepEqual(component.orders, [orderB]);
});

test('F04: destruir la vista cancela las lecturas y no solicita renders tardíos', t => {
  const { component, injector, audits, lists, otherReads, renders } = fixture(t);
  component.loadAudit(orderA);
  component.startAcceptedWithChanges(orderA);
  injector.destroy();
  const rendersBefore = renders();
  audits[0].response.next([eventA]);
  audits[0].response.error(new HttpErrorResponse({ status: 500 }));
  otherReads[0].next({ id: orderA.id, lines: [] });
  lists[0].response.next([orderA]);
  assert.equal(audits[0].response.observed, false);
  assert.equal(otherReads[0].observed, false);
  assert.equal(renders(), rendersBefore);
  assert.equal(component.selectedAuditOrderId, null);
  assert.deepEqual(component.auditEvents, []);
});

test('F04 ampliación: detalle tardío de A no sustituye el borrador editado de B', t => {
  const { component, otherReads } = fixture(t);
  component.startAcceptedWithChanges(orderA);
  component.startAcceptedWithChanges(orderB);
  otherReads[1].next(detail(orderB));
  component.changeLines[0].quantity = 25;
  component.deliveryNotes = 'Borrador de entrega B';
  otherReads[0].next(detail(orderA));
  assert.equal(component.changeDetail.id, orderB.id);
  assert.equal(component.changeLines[0].quantity, 25);
  assert.equal(component.deliveryNotes, 'Borrador de entrega B');
  assert.equal(otherReads[0].observed, false);
});

test('F04 ampliación: error tardío de detalle A no afecta la carga de B', t => {
  const { component, otherReads } = fixture(t);
  component.startAcceptedWithChanges(orderA);
  component.startAcceptedWithChanges(orderB);
  otherReads[0].error(new HttpErrorResponse({ status: 500, error: { error: 'Error detalle A' } }));
  assert.equal(component.loadingChangesOrderId, orderB.id);
  assert.equal(component.errorMessage, null);
  otherReads[1].next(detail(orderB));
  assert.equal(component.changeDetail.id, orderB.id);
  assert.equal(component.loadingChangesOrderId, null);
});

test('F04 ampliación: cancelar ajustes no permite que una lectura pendiente reabra el editor', t => {
  const { component, otherReads } = fixture(t);
  component.startAcceptedWithChanges(orderA);
  component.cancelChanges();
  otherReads[0].next(detail(orderA));
  assert.equal(component.changeDetail, null);
  assert.deepEqual(component.changeLines, []);
  assert.equal(component.loadingChangesOrderId, null);
  assert.equal(otherReads[0].observed, false);
});

test('F04 ampliación: cambiar fecha descarta una respuesta pendiente de detalle de la fecha anterior', t => {
  const { component, otherReads } = fixture(t);
  component.startAcceptedWithChanges(orderA);
  component.serviceDate = '2026-11-04';
  component.changeDate();
  otherReads[0].next(detail(orderA));
  assert.equal(component.changeDetail, null);
  assert.deepEqual(component.changeLines, []);
  assert.equal(component.loadingChangesOrderId, null);
});

test('F04 ampliación: consultar pendientes otra vez cancela la lectura de ajustes aún sin resolver', t => {
  const { component, otherReads, lists } = fixture(t);
  component.startAcceptedWithChanges(orderA);
  component.load();
  otherReads[0].next(detail(orderA));
  lists[1].response.next([orderA, orderB]);
  assert.equal(component.changeDetail, null);
  assert.equal(component.loadingChangesOrderId, null);
  assert.deepEqual(component.changeLines, []);
});
