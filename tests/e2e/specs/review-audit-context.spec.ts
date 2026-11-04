import { expect, test, type Page } from '@playwright/test';

const date = '2026-11-02';
const orderA = '33333333-3333-3333-3333-333333333301';
const orderB = '33333333-3333-3333-3333-333333333302';
const rows = [orderA, orderB].map((id, index) => ({
  orderId: id, customerId: `customer-${index}`, customerName: `Cliente de prueba ${index + 1}`,
  orderDate: date, status: 'PendingAdminReview', sequenceNumber: 1,
  submittedAt: `${date}T15:00:00Z`, requestedDeliveryTime: null,
  requestedDeliveryWindowStart: null, requestedDeliveryWindowEnd: null,
  isLate: true, requiresAdminReview: true, adminReviewReason: 'LateSubmission', adminDecision: null
}));

const history = (orderId: string, summary: string) => [{
  id: `event-${orderId}`, orderId, eventType: 'OrderSubmitted', occurredAt: `${date}T15:00:00Z`,
  actorType: 'Customer', summary, metadataJson: null
}];

async function openReview(page: Page) {
  await page.route('**/api/admin/orders/pending-review?*', route => route.fulfill({ json: rows }));
  await page.goto('/admin/login');
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
  await page.goto(`/admin/pendientes?date=${date}`);
  await expect(page.getByRole('heading', { name: 'Pedidos por confirmar' })).toBeVisible();
}

test('F04: cambiar de pedido limpia la historia visible mientras carga la nueva', async ({ page }) => {
  let releaseB!: () => void;
  const waitForB = new Promise<void>(resolve => { releaseB = resolve; });
  await page.route(`**/api/admin/orders/${orderA}/audit`, route => route.fulfill({ json: history(orderA, 'Historia exclusiva A') }));
  await page.route(`**/api/admin/orders/${orderB}/audit`, async route => {
    await waitForB;
    await route.fulfill({ json: history(orderB, 'Historia exclusiva B') });
  });
  await openReview(page);
  const cards = page.getByTestId('admin-pending').locator('.admin-row');
  await cards.nth(0).getByRole('button', { name: 'Ver auditoria' }).click();
  await expect(cards.nth(0).getByText('Historia exclusiva A')).toBeVisible();
  try {
    await cards.nth(1).getByRole('button', { name: 'Ver auditoria' }).click();
    await expect(cards.nth(1).getByRole('status')).toHaveText('Cargando auditoría...');
    await expect(page.getByText('Historia exclusiva A')).toHaveCount(0);
  } finally {
    releaseB();
  }
  await expect(cards.nth(1).getByText('Historia exclusiva B')).toBeVisible();
  await expect(cards.nth(0).locator('.audit-list')).toHaveCount(0);
});

test('F04: un error de auditoría conserva los pedidos y permite reintentar', async ({ page }) => {
  let attempts = 0;
  await page.route(`**/api/admin/orders/${orderA}/audit`, route => {
    attempts++;
    return attempts === 1
      ? route.fulfill({ status: 503, json: { error: 'No pudimos leer la auditoría.' } })
      : route.fulfill({ json: history(orderA, 'Historia recuperada A') });
  });
  await openReview(page);
  const cards = page.getByTestId('admin-pending').locator('.admin-row');
  await cards.nth(0).getByRole('button', { name: 'Ver auditoria' }).click();
  await expect(cards.nth(0).getByRole('alert')).toContainText('No pudimos leer la auditoría.');
  await expect(cards).toHaveCount(2);
  await expect(cards.nth(1).getByRole('button', { name: 'Aceptar', exact: true })).toBeVisible();
  await cards.nth(0).getByRole('button', { name: 'Ver auditoria' }).click();
  await expect(cards.nth(0).getByText('Historia recuperada A')).toBeVisible();
  await expect(cards.nth(0).getByRole('alert')).toHaveCount(0);
});
