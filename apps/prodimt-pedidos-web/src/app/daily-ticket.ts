import type { AdminOrder, AdminOrderDetail } from './order-data.service';

export function isDailyTicketOrder(order: AdminOrder): boolean {
  return order.customerId !== null && (order.status === 'Submitted' || order.status === 'Accepted');
}

export function buildDailyTicket(selected: AdminOrderDetail, orders: AdminOrderDetail[]) {
  const lines = orders
    .filter(order => isDailyTicketOrder(order) && order.customerId === selected.customerId && order.orderDate === selected.orderDate)
    .sort((first, second) => first.sequenceNumber - second.sequenceNumber)
    .flatMap(order => order.lines.filter(line => line.quantity > 0));
  const hasUnpricedLines = lines.some(line => line.unitPrice == null || line.lineAmount == null);
  const consumption = lines.length && !hasUnpricedLines
    ? lines.reduce((cents, line) => cents + Math.round((line.lineAmount ?? 0) * 100), 0) / 100
    : null;
  return { customerName: selected.customerName, date: selected.orderDate, lines, consumption, hasUnpricedLines };
}
