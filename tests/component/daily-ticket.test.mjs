import assert from 'node:assert/strict';
import test from 'node:test';
import { buildDailyTicket, isDailyTicketOrder } from '../../apps/prodimt-pedidos-web/src/app/daily-ticket.ts';

const line = (id, quantity, unitPrice, lineAmount) => ({ id, quantity, unitPrice, lineAmount });
const order = (sequenceNumber, lines, extra = {}) => ({
  customerId: 'client-a', customerName: 'Cliente de prueba', orderDate: '2026-11-02',
  status: 'Submitted', sequenceNumber, lines, ...extra
});

test('ticket incluye solo renglones positivos de pedidos vigentes del mismo cliente y fecha', () => {
  const selected = order(1, [line('tortilla', 7, 23, 161), line('masa-sin-pedido', 0, null, null)]);
  const orders = [
    order(2, [line('masa', 1.25, 18, 22.50)], { status: 'Accepted' }),
    order(3, [line('rechazado', 7, 23, 161)], { status: 'Rejected' }),
    order(4, [line('pendiente', 7, 23, 161)], { status: 'PendingAdminReview' }),
    order(5, [line('otro-cliente', 7, 23, 161)], { customerId: 'client-b' }),
    order(6, [line('otra-fecha', 7, 23, 161)], { orderDate: '2026-11-03' }),
    selected
  ];
  const ticket = buildDailyTicket(selected, orders);
  assert.deepEqual(ticket.lines.map(x => x.id), ['tortilla', 'masa']);
  assert.equal(ticket.consumption, 183.50);
  assert.equal(ticket.hasUnpricedLines, false);
});

test('precio cero es válido y un precio pendiente conserva el consumo indeterminado', () => {
  const free = order(1, [line('gratis', 1, 0, 0)]);
  assert.equal(buildDailyTicket(free, [free]).consumption, 0);
  const missingPrice = order(2, [line('pendiente', 1, null, null)]);
  const ticket = buildDailyTicket(free, [free, missingPrice]);
  assert.equal(ticket.consumption, null);
  assert.equal(ticket.hasUnpricedLines, true);
  assert.equal(isDailyTicketOrder(order(1, [], { status: 'NoOrder' })), false);
  assert.equal(isDailyTicketOrder(order(1, [], { customerId: null })), false);
});
