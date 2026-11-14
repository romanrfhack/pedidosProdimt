import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test, type Page } from '@playwright/test';

interface Scene {
  baseUrl: string; date: string; adminName: string; password: string;
  tripId: string; assignmentId: string; orderId: string;
  portal: { customerId: string; customerName: string; token: string; driverName: string };
  delivery: { customerId: string; customerName: string; driverName: string };
}
const scene: Scene = JSON.parse(readFileSync(resolve(process.env.PRODIMT_C2_DIRECTORY!, 'browser-private.json'), 'utf8'));
const outgoing = new WeakMap<Page, string[]>();

test.beforeEach(async ({ page }) => {
  await page.clock.setFixedTime(new Date('2026-11-04T18:00:00Z'));
  const external: string[] = []; outgoing.set(page, external);
  page.on('request', request => {
    const url = new URL(request.url());
    if (url.protocol.startsWith('http') && url.origin !== scene.baseUrl) external.push(url.origin);
  });
});
test.afterEach(async ({ page }) => { expect(outgoing.get(page)).toEqual([]); });

async function login(page: Page, user = scene.adminName) {
  await page.goto('/admin/login');
  await page.getByLabel('Usuario', { exact: true }).fill(user);
  await page.getByLabel('Contrasena', { exact: true }).fill(scene.password);
  const authenticated = page.waitForResponse(response => response.request().method() === 'POST'
    && new URL(response.url()).pathname === '/api/auth/admin/login');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  expect((await authenticated).status()).toBe(200);
  // A customer session already shows Salir; wait for the administrative landing.
  await expect(page.getByTestId('admin-login')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Salir', exact: true })).toBeVisible();
}
async function authorization(page: Page) {
  const token = await page.evaluate(() => {
    const session = JSON.parse(localStorage.getItem('prodimt.auth.session.v1') || '{}') as { accessToken?: string };
    return session.accessToken;
  });
  if (!token) throw new Error('Real authentication did not persist a session');
  return { authorization: `Bearer ${token}` };
}

test('captura real pendiente, replay único, revisión con fecha conservada y habilitación para reparto', async ({ page }) => {
  await page.goto(`/cliente#token=${encodeURIComponent(scene.portal.token)}`);
  await expect(page.getByTestId('customer-delivery-date')).toBeVisible();
  await page.getByLabel('Tu pedido', { exact: true }).fill('50');
  await page.getByLabel('Tu pedido', { exact: true }).press('Tab');
  await expect(page.getByTestId('customer-capture-breakdown')).toContainText('50');
  const received = page.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith('/capture'));
  await page.getByRole('button', { name: 'Enviar solicitud de pedido', exact: true }).click();
  const response = await received; expect(response.status()).toBe(200);
  const result = await response.json() as { order: { orderId: string; orderDate: string; status: string }; replayed: boolean };
  expect(result.order.orderDate).toBe(scene.date); expect(result.order.status).toBe('PendingAdminReview');
  expect(result.replayed).toBe(false);
  await expect(page.getByTestId('customer-current-order')).toContainText('50');
  const replay = await page.request.post(response.url(), { headers: await authorization(page), data: response.request().postDataJSON() });
  expect(replay.status()).toBe(200); const repeated = await replay.json() as typeof result;
  expect(repeated.replayed).toBe(true); expect(repeated.order.orderId).toBe(result.order.orderId);
  await login(page);
  const headers = await authorization(page);
  const before = await page.request.get(`/api/admin/delivery?date=${scene.date}`, { headers });
  expect(before.status()).toBe(200);
  expect((await before.json() as { orders: Array<{ orderId: string }> }).orders.some(x => x.orderId === result.order.orderId)).toBe(false);
  await page.goto(`/admin/pendientes?date=${scene.date}`);
  await expect(page.getByLabel('Fecha de servicio', { exact: true })).toHaveValue(scene.date);
  const row = page.locator('article.admin-row').filter({ hasText: scene.portal.customerName });
  await expect(row).toContainText(scene.date);
  const decision = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith(`/${result.order.orderId}/review`));
  await row.getByRole('button', { name: 'Aceptar', exact: true }).click();
  expect((await decision).status()).toBe(200); await expect(row).toHaveCount(0);
  const board = await page.request.get(`/api/admin/delivery?date=${scene.date}`, { headers });
  expect(board.status()).toBe(200);
  const matches = (await board.json() as { orders: Array<{ orderId: string }> }).orders.filter(x => x.orderId === result.order.orderId);
  expect(matches).toHaveLength(1);
  // The host independently verifies the order, receipt and history through SQL
  // after all browser tests, before database/container cleanup.
});

test('pedido 50, primera carga 25, entrega 20, pendiente 30, custodia 5 y disponible 25', async ({ page }) => {
  await login(page, scene.delivery.driverName);
  await expect(page.getByTestId('delivery-page')).toBeVisible();
  await page.getByLabel('Fecha de reparto', { exact: true }).fill(scene.date);
  const consult = page.getByRole('button', { name: 'Consultar reparto', exact: true });
  const refreshed = page.waitForResponse(response => {
    const url = new URL(response.url());
    return response.request().method() === 'GET' && url.pathname === '/api/delivery'
      && url.searchParams.get('date') === scene.date;
  });
  await consult.click();
  expect((await refreshed).status()).toBe(200);
  // The previous stops stay visible while the component applies the refreshed dashboard.
  await expect(consult).toBeEnabled();
  const customer = page.getByRole('button', { name: scene.delivery.customerName, exact: true });
  const row = page.getByTestId('delivery-customer').filter({ has: customer });
  const stop = row.getByTestId('delivery-stop');
  const advance = async (label: string) => {
    const stageButton = page.getByRole('button', { name: label, exact: true });
    await expect(stageButton).toBeEnabled();
    await stageButton.click();
    await row.getByRole('button', { name: `${label} para ${scene.delivery.customerName}`, exact: true }).click();
  };
  await advance('Registrar conteo');
  let dialog = page.getByRole('dialog', { name: 'Registrar conteo', exact: true });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByLabel('Cantidad (kg)', { exact: true })).toHaveValue('25');
  await dialog.getByRole('button', { name: 'Guardar conteo', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await advance('Confirmar carga'); dialog = page.getByRole('dialog');
  await expect(dialog.getByLabel('Cantidad (kg)', { exact: true })).toHaveValue('25');
  await dialog.getByRole('button', { name: 'Confirmar carga', exact: true }).click(); await expect(dialog).toHaveCount(0);
  await advance('Registrar entrega'); dialog = page.getByRole('dialog');
  await dialog.getByLabel('Cantidad (kg)', { exact: true }).fill('20');
  await dialog.getByLabel('Motivo de la diferencia', { exact: true }).fill('Recepción parcial sintética C2');
  await dialog.getByRole('combobox', { name: 'Producto cargado que no se entregó', exact: true }).selectOption('in-vehicle');
  await dialog.getByLabel('Observaciones', { exact: true }).fill('Cinco kg bajo custodia; datos sintéticos.');
  await dialog.getByRole('button', { name: 'Guardar entrega', exact: true }).click(); await expect(dialog).toHaveCount(0);
  await customer.click();
  await expect(stop.locator('.delivery-custody')).toContainText('5 kg');
  await expect(stop.locator('.delivery-quantities')).toContainText('30 kg');
  const detail = await page.request.get(`/api/delivery/trips/${scene.tripId}`, { headers: await authorization(page) });
  expect(detail.status()).toBe(200);
  const trip = await detail.json() as { assignments: Array<Record<string, unknown>>; events: Array<{ type: string }> };
  expect(trip.assignments).toHaveLength(1);
  expect(trip.assignments[0]).toMatchObject({ requestedQuantity: 50, countedQuantity: 25, loadedQuantity: 25,
    deliveredQuantity: 20, pendingQuantity: 30, custodyQuantity: 5 });
  for (const type of ['Counted', 'Loaded', 'Delivered']) expect(trip.events.filter(x => x.type === type)).toHaveLength(1);
  await page.goto('/admin/login'); // Re-enter through the existing SPA entry and real session.
  await expect(page.getByTestId('delivery-page')).toBeVisible();
  await page.getByRole('button', { name: 'Registrar entrega', exact: true }).click();
  await customer.click();
  await expect(stop.locator('.delivery-custody')).toContainText('5 kg');
});

test('autenticación real conserva aislamiento de cliente y chofer ajeno a la vuelta', async ({ page }) => {
  await login(page, scene.portal.driverName);
  await expect(page.getByTestId('delivery-page')).toBeVisible();
  const headers = await authorization(page);
  const forbiddenRead = await page.request.get(`/api/delivery/trips/${scene.tripId}`, { headers });
  expect(forbiddenRead.status()).toBe(403);
  const forbiddenWrite = await page.request.post(`/api/delivery/assignments/${scene.assignmentId}/count`, {
    headers, data: { operationId: crypto.randomUUID(), expectedVersion: 1, expectedOrderVersion: 2, quantity: 25 }
  });
  expect(forbiddenWrite.status()).toBe(403);
  const management = await page.request.get('/api/admin/ordering-schedules', { headers }); expect(management.status()).toBe(403);
  await page.goto(`/cliente#token=${encodeURIComponent(scene.portal.token)}`);
  await expect(page.getByTestId('customer-delivery-date')).toBeVisible();
  const alien = await page.request.get(`/api/customer-orders/${scene.delivery.customerId}/next`, { headers: await authorization(page) });
  expect(alien.status()).toBe(403);
});
