import { expect, test } from '@playwright/test';

const serviceDate = '2026-11-02';
const baseOrder = {
  customerId: 'ticket-test-client', customerName: 'Cliente de prueba', orderDate: serviceDate,
  status: 'Submitted', sequenceNumber: 1, submittedAt: serviceDate + 'T16:00:00Z',
  requiresAdminReview: false, isLate: false
};
const first = { ...baseOrder, orderId: 'first' };
const second = { ...baseOrder, orderId: 'second', status: 'Accepted', sequenceNumber: 2 };
const rejected = { ...baseOrder, orderId: 'rejected', status: 'Rejected', sequenceNumber: 3 };
const other = { ...baseOrder, orderId: 'other', customerId: 'other-client', customerName: 'Otro cliente' };
const product = (name: string, quantity: number, unitPrice: number, lineAmount: number) => ({
  orderLineId: name, productName: name, quantity, unitCode: 'kg', unitPrice, lineAmount
});

test('Mostrador puede abrir un ticket diario, capturar el adeudo e imprimir únicamente el comprobante', async ({ page, request }, info) => {
  await request.post('http://127.0.0.1:5088/__test/reset');
  await page.clock.install({ time: new Date('2026-11-02T16:00:00Z') });
  await page.route('**/api/admin/security/me', route => route.fulfill({
    json: { userId: 'ticket-test-user', permissions: ['orders.read', 'orders.capture'] }
  }));
  await page.route('**/api/admin/orders/today?*', route => route.fulfill({ json: [first, second, rejected, other] }));
  await page.route('**/api/admin/orders/first', route => route.fulfill({
    json: { ...first, lines: [product('Tortilla de maíz', 7, 23, 161), product('Producto sin pedido', 0, 5, 0)] }
  }));
  await page.route('**/api/admin/orders/second', route => route.fulfill({
    json: { ...second, lines: [product('Masa', 1.25, 18, 22.5)] }
  }));

  await page.goto('/admin/login');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByTestId('order-card').filter({ hasText: 'Rechazado' }).getByRole('link', { name: 'Ver ticket' })).toHaveCount(0);
  await page.getByTestId('order-card').filter({ hasText: 'Pedido #1' }).filter({ hasText: 'Cliente de prueba' })
    .getByRole('link', { name: 'Ver ticket' }).click();
  const ticket = page.getByRole('article', { name: 'Ticket diario' });
  await expect(ticket).toContainText('lunes');
  await expect(ticket.getByRole('row')).toHaveCount(3);
  await expect(ticket).toContainText('Tortilla de maíz');
  await expect(ticket).toContainText('Masa');
  await expect(ticket).not.toContainText('Producto sin pedido');
  await expect(ticket).not.toContainText('Otro cliente');
  await expect(ticket).toContainText('$183.50');
  await expect(ticket.locator('.ticket-total')).toContainText('Pendiente');
  const balance = page.getByLabel('Adeudo anterior verificado');
  const print = page.getByRole('button', { name: 'Imprimir ticket' });
  await balance.fill('error');
  await expect(print).toBeDisabled();
  await balance.fill('35,50');
  await expect(ticket.locator('.ticket-total')).toContainText('$219.00');
  await expect(ticket.getByRole('img', { name: 'Tortillas Taqueras' })).toBeVisible();
  await expect.poll(() => ticket.getByRole('img').evaluate(img => (img as HTMLImageElement).naturalWidth)).toBeGreaterThan(0);
  await page.evaluate(() => { window.print = () => { document.body.dataset['ticketPrint'] = 'called'; }; });
  await print.click();
  await expect(page.locator('body')).toHaveAttribute('data-ticket-print', 'called');
  await page.getByLabel('Ancho del rollo').selectOption('58');
  await page.emulateMedia({ media: 'print' });
  await expect(ticket).toBeVisible();
  await expect(print).toBeHidden();
  await expect(page.getByRole('navigation')).toBeHidden();
  expect((await ticket.boundingBox())?.width).toBeLessThanOrEqual(205);
  await page.pdf({ path: info.outputPath('ticket-58mm.pdf'), width: '58mm', height: '150mm' });
  await page.emulateMedia({ media: 'screen' });
  await page.getByLabel('Ancho del rollo').selectOption('80');
  expect((await ticket.boundingBox())?.width).toBeGreaterThan(205);
  await page.emulateMedia({ media: 'print' });
  await page.pdf({ path: info.outputPath('ticket-80mm.pdf'), width: '80mm', height: '150mm' });
});
