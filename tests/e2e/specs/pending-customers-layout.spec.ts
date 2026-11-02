import { expect, test, type Page } from '@playwright/test';

const api = 'http://127.0.0.1:5088';
const customer = '11111111-1111-1111-1111-111111111112';

async function openFixture(page: Page) {
  const rows = Array.from({ length: 18 }, (_, i) => ({
    customerId: i === 0 ? customer : `pending-layout-${i}`,
    customerName: i === 1 ? 'ClienteNombreMuyLargoSinEspaciosParaComprobarElAjusteDeLaTarjeta' : `Cliente demo ${String(i + 1).padStart(2, '0')}`,
    phoneNumber: i === 2 ? null : '5550001234', preferredDeliveryTime: i % 2 ? null : '13:30:00',
    preferredDeliveryWindowStart: null, preferredDeliveryWindowEnd: null,
    deliveryNotes: i === 3 ? 'NotaDeEntregaLargaSinEspaciosParaComprobarQueNoSeSaleDeLaTarjeta' : null,
    frequentProductsCount: i + 1
  }));
  const writes: string[] = [];
  page.on('request', r => {
    if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(r.method()) && /\/api\/admin\/(customers|orders)/.test(r.url())) writes.push(r.url());
  });
  await page.route('**/api/admin/customers/pending-orders?*', route => route.fulfill({ json: rows }));
  await page.route('**/api/admin/customers', route => route.fulfill({ json: rows.map((r, i) => ({ id: r.customerId, restDays: i === 17 ? [1] : [] })) }));
  await page.goto('/admin/login'); await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
  await page.goto('/admin/clientes-pendientes');
  await expect(page.getByRole('button', { name: 'Consultar clientes' })).toBeEnabled();
  await page.getByLabel('Fecha de servicio').fill('2026-11-02');
  await page.getByRole('button', { name: 'Consultar clientes' }).click();
  await expect(page.getByTestId('pending-customer-card')).toHaveCount(17);
  return { rows, writes };
}

test.beforeEach(async ({ request }) => { await request.post(api + '/__test/reset'); });

for (const [width, height, columns] of [[320, 740, 1], [390, 844, 1], [768, 1024, 2], [1024, 768, 3], [1366, 768, 4], [1920, 1080, 6], [2560, 1080, 8]]) {
  test(`pendientes distribuye ${columns} tarjetas por fila a ${width}px sin cortar acciones o datos`, async ({ page }, info) => {
    await page.setViewportSize({ width, height }); const fixture = await openFixture(page);
    await expect(page.getByTestId('pending-count')).toHaveText('17 clientes en la lista');
    await page.getByRole('checkbox', { name: /Mostrar también clientes en descanso/ }).check();
    await expect(page.getByTestId('pending-customer-card')).toHaveCount(18);
    await expect(page.getByTestId('pending-count')).toHaveText('18 clientes en la lista');
    const metrics = await page.locator('.pending-grid').evaluate(el => ({
      columns: getComputedStyle(el).gridTemplateColumns.split(' ').length, width: el.getBoundingClientRect().width,
      overflow: document.documentElement.scrollWidth > innerWidth,
      contentsFit: Array.from(el.querySelectorAll('article, button, dl, p, .badge')).every(item => item.scrollWidth <= item.clientWidth + 1),
      buttonsTouchSize: Array.from(el.querySelectorAll('button')).every(item => item.getBoundingClientRect().height >= 44),
      actionsAligned: (() => {
        const cards = Array.from(el.querySelectorAll('article'));
        const firstRow = cards.filter(card => Math.abs(card.getBoundingClientRect().top - cards[0].getBoundingClientRect().top) < 1);
        const bottoms = firstRow.map(card => card.querySelector('.pending-actions')!.getBoundingClientRect().bottom);
        return Math.max(...bottoms) - Math.min(...bottoms) < 1;
      })()
    }));
    expect(metrics.columns).toBe(columns); expect(metrics.overflow).toBe(false); expect(metrics.contentsFit).toBe(true);
    expect(metrics.buttonsTouchSize).toBe(true); expect(metrics.actionsAligned).toBe(true);
    if (width >= 1366) expect(metrics.width).toBeGreaterThan(width * .9);
    await expect(page.getByText('Sin teléfono', { exact: true })).toBeVisible();
    await expect(page.getByTestId('pending-customer-card').last()).toContainText('Descanso programado');
    await page.screenshot({ path: info.outputPath(`pending-${width}.png`), fullPage: false });
    expect(fixture.writes).toEqual([]);
  });
}

test('captura ocupa una fila completa y conserva borrador y foco al cambiar ancho; cancelar recupera la rejilla', async ({ page }, info) => {
  await page.setViewportSize({ width: 1920, height: 1080 }); const fixture = await openFixture(page);
  const card = page.getByTestId('pending-customer-card').filter({ hasText: 'Cliente demo 01' });
  await card.getByRole('button', { name: 'Capturar pedido', exact: true }).click();
  const capture = page.getByTestId('order-capture'); await expect(capture).toBeVisible();
  await page.getByRole('button', { name: 'Captura manual', exact: true }).click();
  const quantity = capture.getByRole('spinbutton').first(); await quantity.fill('12.5');
  await capture.getByText('Notas de entrega e internas', { exact: true }).click();
  await capture.getByLabel('Notas de entrega', { exact: true }).fill('Nota de prueba');
  await quantity.focus();
  await capture.evaluate(el => { (el as HTMLElement).dataset['draftMarker'] = 'same-instance'; });
  for (const width of [390, 320, 768, 1366, 2560]) {
    await page.setViewportSize({ width, height: 900 });
    await expect(quantity).toHaveValue('12.5'); await expect(quantity).toBeFocused();
    await expect(capture.getByLabel('Notas de entrega', { exact: true })).toHaveValue('Nota de prueba');
    await expect(capture).toHaveAttribute('data-draft-marker', 'same-instance');
    const gridBox = (await page.locator('.pending-grid').boundingBox())!;
    const cardBox = (await card.boundingBox())!;
    expect(Math.abs(cardBox.width - gridBox.width)).toBeLessThan(2);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
  await page.screenshot({ path: info.outputPath('pending-capture-desktop.png'), fullPage: false });
  await page.setViewportSize({ width: 390, height: 844 });
  await capture.scrollIntoViewIfNeeded();
  await page.screenshot({ path: info.outputPath('pending-capture-mobile.png'), fullPage: false });
  await capture.getByRole('button', { name: 'Cancelar', exact: true }).click();
  await expect(capture).toHaveCount(0); await expect(card).not.toHaveClass(/capture-open/);
  await page.setViewportSize({ width: 1920, height: 1080 });
  expect((await card.boundingBox())!.width).toBeLessThan(400);
  expect(fixture.writes).toEqual([]);
});
