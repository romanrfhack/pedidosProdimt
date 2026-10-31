import { expect, test, type Page } from '@playwright/test';

const api = 'http://127.0.0.1:5088';
const ids = ['a', 'b', 'c', 'd'];
const names = ['Alfa demo', 'Beta demo', 'Gamma demo', 'Delta demo'];
const statuses = ['NoOrderConfirmed', 'HasOrder', 'AwaitingOrder', 'ScheduledRest'];
const customer = (id: string, name: string) => ({
  id, name, phoneNumber: '5550000000', isActive: true, externalCode: 'DEMO-' + id,
  preferredDeliveryTime: null, preferredDeliveryWindowStart: null, preferredDeliveryWindowEnd: null,
  deliveryNotes: null, routeCode: 'Ruta demo', routeStopOrder: null, hasStoreKey: null, paymentTermCode: null,
  internalDeliveryInstructions: null, restDays: [], orderingProfileVersion: 0, isOrderProfileApproved: false
});

async function openCatalog(page: Page) {
  await page.goto('/admin/login');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
  await page.goto('/admin/catalogos');
  await expect(page.getByLabel('Buscar cliente')).toBeVisible();
}

async function mockCards(page: Page) {
  await page.route('**/api/admin/customers', route => route.fulfill({ json: ids.map((id, i) => customer(id, names[i])) }));
  await page.route('**/api/admin/customers/day-status', route => route.fulfill({ json: {
    date: '2026-10-28', customers: ids.map((customerId, i) => ({ customerId, status: statuses[i], orderCount: i === 1 ? 1 : 0, requiresReview: i === 1 }))
  } }));
}

test.beforeEach(async ({ request }) => { await request.post(api + '/__test/reset'); });

test('el formulario se crea solo al abrir el modal y cancelar restaura foco sin escribir', async ({ page }) => {
  let writes = 0;
  page.on('request', request => { if (request.method() === 'POST' && request.url().endsWith('/api/admin/customers')) writes++; });
  await openCatalog(page);
  await expect(page.locator('dialog')).toHaveCount(0);
  await expect(page.getByLabel('Nombre', { exact: true })).toHaveCount(0);
  const add = page.getByRole('button', { name: 'Nuevo cliente' });
  await add.click();
  const dialog = page.getByRole('dialog', { name: 'Nuevo cliente' });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByLabel('Nombre', { exact: true })).toBeFocused();
  const box = (await dialog.boundingBox())!;
  expect(Math.abs(box.x + box.width / 2 - page.viewportSize()!.width / 2)).toBeLessThan(3);
  await dialog.getByLabel('Nombre', { exact: true }).fill('Borrador demo');
  await dialog.getByLabel('Lunes', { exact: true }).uncheck();
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(add).toBeFocused();
  expect(writes).toBe(0);
  await add.click();
  await expect(dialog.getByLabel('Nombre', { exact: true })).toHaveValue('');
  await expect(dialog.getByLabel('Lunes', { exact: true })).toBeChecked();
});

test('tarjetas muestran estado del servidor y ordenan descansos y no pedidos al final', async ({ page }) => {
  await mockCards(page);
  await openCatalog(page);
  const cards = page.getByTestId('customer-card');
  await expect(cards.first()).toHaveAttribute('data-day-state', 'AwaitingOrder');
  await expect(cards.locator('h3')).toHaveText(['Gamma demo', 'Beta demo', 'Alfa demo', 'Delta demo']);
  await expect(cards.nth(0)).toHaveClass(/customer-awaiting/);
  await expect(cards.nth(1)).toHaveClass(/customer-ordered/);
  await expect(cards.nth(1)).toContainText('Pedido pendiente de revisión');
  await expect(cards.nth(2)).toContainText('Hoy no pide');
  await expect(cards.nth(3)).toContainText('Descanso programado');
  await page.screenshot({ path: test.info().outputPath('customer-cards-desktop.png'), fullPage: true });
  await page.getByRole('button', { name: /^Sin pedido hoy/ }).click();
  await expect(cards.locator('h3')).toHaveText(['Alfa demo', 'Delta demo']);
  await page.getByRole('button', { name: /^Con pedido/ }).click();
  await expect(cards.locator('h3')).toHaveText(['Beta demo']);
  await page.getByRole('button', { name: /^Todos/ }).click();
  await page.getByLabel('Buscar cliente').fill('gamma');
  await expect(cards.locator('h3')).toHaveText(['Gamma demo']);
  await page.getByLabel('Buscar cliente').fill('DEMO-b');
  await expect(cards.locator('h3')).toHaveText(['Beta demo']);
  await page.getByLabel('Buscar cliente').fill('nadie');
  await expect(cards).toHaveCount(0);
  await expect(page.getByText('No hay clientes que coincidan', { exact: false })).toBeVisible();
});

test('editar descansos guarda los días y un refresco no pisa el formulario abierto', async ({ page }) => {
  let record = { ...customer('schedule-demo', 'Cliente semanal demo'), restDays: [1] };
  let status = 'AwaitingOrder';
  let dayReads = 0;
  let writes: unknown[] = [];
  await page.route('**/api/admin/customers', route => route.fulfill({ json: [record] }));
  await page.route('**/api/admin/customers/day-status', async route => {
    dayReads++;
    await route.fulfill({ json: { date: '2026-10-28', customers: [{ customerId: record.id, status, orderCount: 0, requiresReview: false }] } });
  });
  await page.route('**/api/admin/customers/schedule-demo', async route => {
    const body = route.request().postDataJSON();
    writes.push(body);
    record = { ...record, ...body };
    status = 'ScheduledRest';
    await route.fulfill({ json: record });
  });
  await openCatalog(page);
  await page.getByTestId('customer-card').getByRole('button', { name: 'Editar', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByLabel('Lunes', { exact: true })).not.toBeChecked();
  await dialog.getByLabel('Miércoles', { exact: true }).uncheck();
  await dialog.getByLabel('Nombre', { exact: true }).fill('Nombre editado demo');
  const before = dayReads;
  await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
  await expect.poll(() => dayReads).toBeGreaterThan(before);
  await expect(dialog.getByLabel('Nombre', { exact: true })).toHaveValue('Nombre editado demo');
  await expect(dialog.getByLabel('Miércoles', { exact: true })).not.toBeChecked();
  await dialog.getByRole('button', { name: 'Guardar cliente' }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByTestId('customer-card')).toHaveAttribute('data-day-state', 'ScheduledRest');
  expect(writes).toHaveLength(1);
  expect(writes[0]).toMatchObject({ restDays: [1, 3], name: 'Nombre editado demo' });
  await page.reload();
  await page.getByTestId('customer-card').getByRole('button', { name: 'Editar', exact: true }).click();
  await expect(dialog.getByLabel('Lunes', { exact: true })).not.toBeChecked();
  await expect(dialog.getByLabel('Miércoles', { exact: true })).not.toBeChecked();
});

test('un fallo de conexión no inventa pendientes o no pedidos y preserva el último estado al fallar un refresco', async ({ page }) => {
  let fail = true;
  await page.route('**/api/admin/customers', route => route.fulfill({ json: [customer('state-demo', 'Estado demo')] }));
  await page.route('**/api/admin/customers/day-status', route => fail
    ? route.fulfill({ status: 503, json: { error: 'Fallo de prueba' } })
    : route.fulfill({ json: { date: '2026-10-28', customers: [{ customerId: 'state-demo', status: 'HasOrder', orderCount: 1, requiresReview: false }] } }));
  await openCatalog(page);
  const card = page.getByTestId('customer-card');
  await expect(card).toHaveAttribute('data-day-state', 'Unavailable');
  await expect(card).toContainText('Estado sin actualizar');
  await expect(card).not.toHaveClass(/customer-off|customer-awaiting/);
  fail = false;
  await page.getByRole('button', { name: 'Reintentar', exact: true }).click();
  await expect(card).toHaveAttribute('data-day-state', 'HasOrder');
  fail = true;
  await page.getByRole('button', { name: 'Actualizar estados', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Se conserva la última consulta');
  await expect(card).toHaveAttribute('data-day-state', 'HasOrder');
});

test('modal móvil permite reintentar errores y evita doble envío y cierre durante el guardado', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  let release: () => void = () => {};
  const gate = new Promise<void>(resolve => { release = resolve; });
  let writes = 0;
  await page.route('**/api/admin/customers', async route => {
    if (route.request().method() !== 'POST') return route.continue();
    writes++;
    if (writes === 1) {
      await gate;
      await route.fulfill({ status: 400, json: { error: 'Nombre de prueba ya existente.' } });
    } else {
      await route.fulfill({ status: 201, json: customer('new-demo', 'Cliente modal demo') });
    }
  });
  await openCatalog(page);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.getByRole('button', { name: 'Nuevo cliente' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Nombre', { exact: true }).fill('Cliente modal demo');
  await dialog.getByLabel('Domingo', { exact: true }).check();
  const save = dialog.getByRole('button', { name: 'Crear cliente' });
  await page.screenshot({ path: test.info().outputPath('customer-modal-mobile.png'), fullPage: true });
  await save.click();
  await expect(dialog.getByRole('button', { name: 'Guardando…' })).toBeDisabled();
  await expect(dialog.getByRole('button', { name: 'Cancelar' })).toBeDisabled();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeVisible();
  expect(writes).toBe(1);
  release();
  await expect(dialog.getByRole('alert')).toContainText('Nombre de prueba ya existente.');
  await expect(dialog.getByLabel('Nombre', { exact: true })).toHaveValue('Cliente modal demo');
  await expect(dialog.getByLabel('Domingo', { exact: true })).toBeChecked();
  expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  await dialog.getByRole('button', { name: 'Crear cliente' }).click();
  await expect(dialog).toHaveCount(0);
  expect(writes).toBe(2);
});
