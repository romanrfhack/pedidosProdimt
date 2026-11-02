import { expect, test, type Page } from '@playwright/test';

const customerId = '11111111-1111-1111-1111-111111111111';
const states = ['AwaitingOrder', 'HasOrder', 'NoOrderConfirmed', 'ScheduledRest', 'Unavailable'];

async function catalogFixture(page: Page) {
  const rows = Array.from({ length: 18 }, (_, i) => ({
    id: i === 0 ? customerId : `layout-customer-${i}`,
    name: i === 1 ? 'ClienteNombreLargoSinEspaciosParaVerificarQueLaTarjetaSeAdapta' : `Cliente demo ${String(i + 1).padStart(2, '0')}`,
    phoneNumber: i === 2 ? null : '5550001234', isActive: i !== 17, externalCode: `DEMO-${i}`,
    preferredDeliveryTime: '09:30:00', preferredDeliveryWindowStart: null, preferredDeliveryWindowEnd: null,
    deliveryNotes: null, routeCode: i === 1 ? 'RutaLargaDePruebaSinEspaciosParaVerificarElAjusteDelTexto' : 'Ruta demo',
    routeStopOrder: i + 1, hasStoreKey: i % 3 === 0 ? true : null, paymentTermCode: i % 2 ? null : 'cash_on_delivery',
    internalDeliveryInstructions: null, restDays: [], orderingProfileVersion: 0, isOrderProfileApproved: false
  }));
  const writes: string[] = [];
  page.on('request', request => {
    if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method()) && request.url().includes('/api/admin/customers')) writes.push(request.url());
  });
  await page.route('**/api/admin/customers', route => route.fulfill({ json: rows }));
  await page.route('**/api/admin/customers/day-status', route => route.fulfill({ json: {
    date: '2026-11-02', customers: rows.map((row, i) => ({ customerId: row.id,
      status: row.isActive ? states[i % states.length] : 'Inactive', orderCount: i % states.length === 1 ? 1 : 0,
      requiresReview: i % states.length === 1 }))
  } }));
  await page.goto('/admin/login'); await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
  await page.goto('/admin/catalogos'); await expect(page.getByTestId('customer-card')).toHaveCount(rows.length);
  return { rows, writes };
}

test.beforeEach(async ({ request }) => { await request.post('http://127.0.0.1:5088/__test/reset'); });

for (const [width, height, columns] of [[320, 740, 1], [390, 844, 1], [768, 1024, 2], [1024, 768, 3], [1366, 768, 4], [1920, 1080, 6], [2560, 1080, 8]]) {
  test(`catálogo de clientes distribuye ${columns} tarjetas por fila a ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height }); const fixture = await catalogFixture(page);
    const grid = page.locator('.customers-page .customer-list');
    const metrics = await grid.evaluate(el => ({ columns: getComputedStyle(el).gridTemplateColumns.split(' ').length,
      width: el.getBoundingClientRect().width, overflow: document.documentElement.scrollWidth > innerWidth,
      cardsFit: Array.from(el.querySelectorAll('[data-testid="customer-card"]')).every(card => card.scrollWidth <= card.clientWidth + 1) }));
    expect(metrics.columns).toBe(columns); expect(metrics.overflow).toBe(false); expect(metrics.cardsFit).toBe(true);
    if (width >= 1366) expect(metrics.width).toBeGreaterThan(width * .9);
    if (width === 1366) expect((await page.getByTestId('customer-card').first().boundingBox())!.y).toBeLessThan(450);
    if (width >= 1366) {
      const heading = (await page.locator('.customer-catalog-heading').boundingBox())!;
      const toolbar = (await page.locator('.customer-toolbar').boundingBox())!;
      expect(heading.x + heading.width).toBeLessThanOrEqual(toolbar.x);
      expect(await page.getByRole('heading', { name: 'Clientes', exact: true }).isVisible()).toBe(true);
    }
    await expect(page.getByText('Mostrando 18 de 18 clientes', { exact: true })).toBeVisible();
    await page.screenshot({ path: info.outputPath(`customers-${width}.png`), fullPage: false });
    if (width === 390) await page.getByTestId('customer-card').first().screenshot({ path: info.outputPath('customer-card-mobile.png') });
    await page.getByRole('button', { name: 'Nuevo cliente', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Nuevo cliente', exact: true }); await expect(dialog).toBeVisible();
    const bounds = (await dialog.boundingBox())!;
    expect(bounds.x).toBeGreaterThanOrEqual(0); expect(bounds.x + bounds.width).toBeLessThanOrEqual(width + 1);
    expect(bounds.height).toBeLessThanOrEqual(height + 1);
    await page.keyboard.press('Escape'); await expect(dialog).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Nuevo cliente', exact: true })).toBeFocused();
    expect(fixture.writes).toEqual([]);
  });
}

test('buscar y filtrar reacomoda tarjetas y conserva filas de ancho legible al quedar un cliente', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 }); const fixture = await catalogFixture(page);
  const cards = page.getByTestId('customer-card'); const search = page.getByRole('searchbox', { name: 'Buscar cliente', exact: true });
  await search.fill('DEMO-0'); await expect(cards).toHaveCount(1);
  await expect(cards).toContainText('Cliente demo 01');
  expect((await cards.boundingBox())!.width).toBeLessThan(400);
  await page.getByRole('button', { name: 'Limpiar búsqueda de clientes', exact: true }).click();
  await expect(cards).toHaveCount(18);
  await page.getByRole('button', { name: /^Con pedido/ }).click();
  await expect(cards).toHaveCount(4);
  await search.fill('no-coincide'); await expect(cards).toHaveCount(0);
  await expect(page.getByText('Mostrando 0 de 18 clientes', { exact: true })).toBeVisible();
  await expect(page.getByText('No hay clientes que coincidan', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Limpiar filtros de clientes', exact: true }).click();
  await expect(search).toHaveValue(''); await expect(cards).toHaveCount(18);
  expect(fixture.writes).toEqual([]);
});

test('redimensionar conserva filtros y tarjetas, y Configurar abre el mismo cliente fuera de la rejilla', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 }); const fixture = await catalogFixture(page);
  await page.getByRole('searchbox', { name: 'Buscar cliente', exact: true }).fill('Cliente demo 01');
  await expect(page.getByTestId('customer-card')).toHaveCount(1);
  for (const width of [390, 768, 1366]) {
    await page.setViewportSize({ width, height: 900 });
    await expect(page.getByTestId('customer-card')).toHaveCount(1);
    await expect(page.getByRole('searchbox', { name: 'Buscar cliente', exact: true })).toHaveValue('Cliente demo 01');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
  await page.getByTestId('customer-card').getByRole('button', { name: 'Configurar', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/admin/clientes/${customerId}/configuracion$`));
  await expect(page.getByTestId('customer-config')).toBeVisible();
  await expect(page.locator('.customers-page')).toHaveCount(0);
  expect(fixture.writes).toEqual([]);
});
