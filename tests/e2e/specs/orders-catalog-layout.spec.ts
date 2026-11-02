import { expect, test, type Page } from '@playwright/test';

const api = 'http://127.0.0.1:5088';
const orderId = '33333333-3333-3333-3333-333333333301';
const today = '2026-11-02';

async function fixture(page: Page, readOnly = false) {
  const states = ['Submitted', 'Accepted', 'PendingAdminReview', 'NoOrder', 'Rejected', 'FutureState'];
  const state = {
    orderReads: 0, changeReads: 0, lastDate: '', fail: false, writes: [] as string[],
    rows: Array.from({ length: 18 }, (_, i) => ({
      orderId: i === 0 ? orderId : `layout-order-${i}`, customerId: `layout-customer-${i}`,
      customerName: i === 7 ? 'ClienteNombreLargoSinEspaciosParaVerificarLaTarjetaDePedidos' : i === 6 ? 'Cliente Álamo' : `Cliente demo ${String(i + 1).padStart(2, '0')}`,
      sequenceNumber: i + 1, orderDate: today, status: states[i % states.length],
      submittedAt: `${today}T16:00:00Z`, requestedDeliveryTime: i % 2 ? '13:30:00' : null,
      requestedDeliveryWindowStart: null, requestedDeliveryWindowEnd: null,
      isLate: i % 5 === 0, requiresAdminReview: i % states.length === 2, adminReviewReason: null,
      adminDecision: i % states.length === 1 ? 'AcceptedWithChanges' : null
    }))
  };
  page.on('request', r => {
    if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(r.method()) && /\/api\/admin\/(orders|operations|customers)/.test(r.url())) state.writes.push(r.url());
  });
  await page.route('**/api/admin/orders/today?*', route => {
    state.orderReads++; state.lastDate = new URL(route.request().url()).searchParams.get('date')!;
    return route.fulfill(state.fail ? { status: 503, json: { error: 'Lectura de prueba no disponible' } }
      : { json: state.rows.map(row => ({ ...row, orderDate: state.lastDate })) });
  });
  await page.route('**/api/admin/operations/changes?*', route => {
    state.changeReads++;
    return route.fulfill({ json: { date: state.lastDate, pendingCount: 1, changes: [
      { id: 'pending-change-demo', orderId: 'layout-order-2', acknowledgedAt: null },
      { id: 'received-change-demo', orderId: 'layout-order-4', acknowledgedAt: `${today}T16:00:00Z` }
    ] } });
  });
  await page.route(`**/api/admin/orders/${orderId}`, async route => {
    const response = await route.fetch();
    await route.fulfill({ json: { ...await response.json(), ...state.rows[0], orderDate: state.lastDate } });
  });
  if (readOnly) await page.route('**/api/admin/security/me', route => route.fulfill({ json: { userId: 'readonly-layout-user', permissions: ['orders.read'] } }));
  await page.goto('/admin/login'); await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByTestId('order-card')).toHaveCount(18);
  return state;
}

test.beforeEach(async ({ request, page }) => {
  await request.post(api + '/__test/reset');
  await page.clock.install({ time: new Date('2026-11-02T16:00:00Z') });
});

for (const [width, height, columns] of [[320, 740, 1], [390, 844, 1], [768, 1024, 2], [1024, 768, 3], [1366, 768, 4], [1920, 1080, 6], [2560, 1080, 8]]) {
  test(`pedidos muestra ${columns} columnas a ${width}px, con acciones y textos completos`, async ({ page }, info) => {
    await page.setViewportSize({ width, height }); const state = await fixture(page);
    const metrics = await page.locator('.orders-grid').evaluate(el => {
      const cards = Array.from(el.querySelectorAll('[data-testid="order-card"]'));
      const firstRow = cards.filter(card => Math.abs(card.getBoundingClientRect().top - cards[0].getBoundingClientRect().top) < 1);
      const bottoms = firstRow.map(card => card.querySelector('.order-actions')!.getBoundingClientRect().bottom);
      return { columns: getComputedStyle(el).gridTemplateColumns.split(' ').length, width: el.getBoundingClientRect().width,
        overflow: document.documentElement.scrollWidth > innerWidth,
        contentsFit: Array.from(el.querySelectorAll('article, button, dl, dd, .badge')).every(item => item.scrollWidth <= item.clientWidth + 1),
        aligned: Math.max(...bottoms) - Math.min(...bottoms) < 1,
        touch: cards.every(card => card.querySelector('button')!.getBoundingClientRect().height >= 44) };
    });
    expect(metrics.columns).toBe(columns); expect(metrics.overflow).toBe(false); expect(metrics.contentsFit).toBe(true);
    expect(metrics.aligned).toBe(true); expect(metrics.touch).toBe(true);
    if (width >= 1366) expect(metrics.width).toBeGreaterThan(width * .9);
    await expect(page.getByTestId('orders-result-count')).toHaveText('Mostrando 18 de 18 registros para esta fecha');
    await page.screenshot({ path: info.outputPath(`orders-${width}.png`), fullPage: false });
    expect(state.writes).toEqual([]);
  });
}

test('colores y etiquetas distinguen estados sin presentar un desconocido como aceptado', async ({ page }) => {
  const state = await fixture(page); const cards = page.getByTestId('order-card');
  const expected = [['Registrado', 'order-registered'], ['Aceptado con cambios', 'order-registered'], ['Por revisar', 'order-review'],
    ['No pide en esta fecha', 'order-off'], ['Rechazado', 'order-rejected'], ['Sin clasificar', 'order-unknown']];
  const borders: string[] = [];
  for (let i = 0; i < expected.length; i++) {
    await expect(cards.nth(i).locator('.order-status')).toHaveText(expected[i][0]);
    await expect(cards.nth(i)).toHaveClass(new RegExp(expected[i][1]));
    borders.push(await cards.nth(i).evaluate(el => getComputedStyle(el).borderInlineStartColor));
  }
  expect(new Set(borders).size).toBe(5);
  await expect(cards.nth(2).getByRole('link', { name: '1 avisos pendientes de recibir' })).toHaveAttribute('href', `/admin/operacion?date=${today}`);
  expect(state.writes).toEqual([]);
});

test('buscar sin acentos o por número combina con filtros, permite limpiar y conserva ancho con un resultado', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 }); const state = await fixture(page);
  const cards = page.getByTestId('order-card'); const search = page.getByRole('searchbox');
  await search.fill('alamo'); await expect(cards).toHaveCount(1); await expect(cards).toContainText('Cliente Álamo');
  expect((await cards.boundingBox())!.width).toBeLessThan(400);
  await page.getByRole('button', { name: /^Por revisar/ }).click(); await expect(cards).toHaveCount(0);
  await expect(page.getByText('No hay pedidos que coincidan con la búsqueda o el filtro.')).toBeVisible();
  await page.getByRole('button', { name: 'Limpiar filtros de pedidos', exact: true }).click();
  await expect(search).toHaveValue(''); await expect(cards).toHaveCount(18);
  for (const [label, count] of [['Registrados / aceptados', 6], ['Por revisar', 3], ['No pide', 3], ['Rechazados', 3], ['Tardíos', 4], ['Avisos pendientes', 1]] as const) {
    await page.getByRole('button', { name: new RegExp('^' + label.replace('/', '\\/')) }).click();
    await expect(cards).toHaveCount(count);
  }
  await page.getByRole('button', { name: /^Todos/ }).click();
  await search.fill('#18'); await expect(cards).toHaveCount(1);
  await expect(cards).toContainText('Pedido #18');
  expect(state.writes).toEqual([]);
});

test('refresco automático actualiza tarjetas y contadores conservando búsqueda y filtro', async ({ page }) => {
  const state = await fixture(page);
  await page.getByRole('searchbox').fill('Cliente demo');
  const filter = page.getByRole('button', { name: /^Por revisar/ }); await filter.click();
  await expect(page.getByTestId('order-card')).toHaveCount(3);
  state.rows[0].status = 'PendingAdminReview'; state.rows[0].requiresAdminReview = true;
  const reads = state.orderReads;
  await page.clock.runFor(15_100);
  await expect.poll(() => state.orderReads).toBeGreaterThan(reads);
  await expect(page.getByTestId('order-card')).toHaveCount(4);
  await expect(filter).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('searchbox')).toHaveValue('Cliente demo');
  await expect(filter).toContainText('4'); expect(state.writes).toEqual([]);
});

test('detalle y corrección ocupan toda la fila; redimensionar conserva borrador y pausa filtros y refresco', async ({ page }, info) => {
  await page.setViewportSize({ width: 1920, height: 1080 }); const state = await fixture(page);
  await page.getByRole('searchbox').fill('Cliente demo 01');
  const card = page.getByTestId('order-card'); await card.getByRole('button', { name: 'Ver detalle' }).click();
  await page.getByRole('button', { name: 'Corregir pedido', exact: true }).click();
  const form = page.getByTestId('order-correction');
  await form.getByLabel('Cantidad corregida 1').fill('25'); await form.getByLabel('Motivo de la corrección').fill('Conservar este borrador');
  await form.getByLabel('Nota del producto 1').fill('Nota de prueba conservada');
  await form.evaluate(el => { (el as HTMLElement).dataset['draftMarker'] = 'same-instance'; });
  const reads = state.orderReads;
  for (const width of [390, 320, 768, 1366, 2560]) {
    await page.setViewportSize({ width, height: 900 });
    await expect(form.getByLabel('Cantidad corregida 1')).toHaveValue('25');
    await expect(form.getByLabel('Motivo de la corrección')).toHaveValue('Conservar este borrador');
    await expect(form.getByLabel('Nota del producto 1')).toHaveValue('Nota de prueba conservada');
    await expect(form).toHaveAttribute('data-draft-marker', 'same-instance');
    expect(Math.abs((await card.boundingBox())!.width - (await page.locator('.orders-grid').boundingBox())!.width)).toBeLessThan(2);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
  await expect(page.getByRole('searchbox')).toBeDisabled();
  for (const button of await page.getByRole('group', { name: 'Filtrar pedidos' }).getByRole('button').all()) await expect(button).toBeDisabled();
  await page.clock.runFor(16_000); expect(state.orderReads).toBe(reads);
  await page.setViewportSize({ width: 1366, height: 1000 }); await form.scrollIntoViewIfNeeded();
  expect((await form.getByLabel('Nota del producto 1').boundingBox())!.width).toBeGreaterThan(180);
  await page.screenshot({ path: info.outputPath('order-correction-desktop.png'), fullPage: false });
  await form.getByRole('button', { name: 'Cancelar corrección', exact: true }).click();
  await expect(form).toHaveCount(0); await expect(page.getByRole('searchbox')).toBeEnabled();
  await card.getByRole('button', { name: 'Ver detalle' }).click();
  await expect(card).not.toHaveClass(/detail-open/);
  expect((await card.boundingBox())!.width).toBeLessThan(400); expect(state.writes).toEqual([]);
});

test('permisos de sólo lectura conservan detalle sin cargar avisos ni habilitar corrección', async ({ page }) => {
  const state = await fixture(page, true);
  await page.getByTestId('order-card').first().getByRole('button', { name: 'Ver detalle' }).click();
  await expect(page.getByText('Detalle interno', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Corregir pedido', exact: true })).toHaveCount(0);
  expect(state.changeReads).toBe(0); expect(state.writes).toEqual([]);
});

test('error de refresco conserva últimos pedidos; recuperar una lista vacía limpia el aviso', async ({ page }) => {
  const state = await fixture(page); state.fail = true;
  await page.clock.runFor(15_100);
  await expect(page.getByRole('alert')).toContainText('Los datos visibles pueden estar desactualizados');
  await expect(page.getByTestId('order-card')).toHaveCount(18);
  state.fail = false; state.rows = [];
  await page.clock.runFor(15_100);
  await expect(page.getByText('No hay pedidos registrados para esta fecha.')).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0); await expect(page.getByTestId('order-card')).toHaveCount(0);
  expect(state.writes).toEqual([]);
});

test('hoy usa CDMX, la fecha elegida limpia datos anteriores y el enlace directo abre el detalle correcto', async ({ page }) => {
  await page.clock.setFixedTime(new Date('2026-11-03T04:00:00Z'));
  const state = await fixture(page);
  await expect(page.getByLabel('Fecha de servicio')).toHaveValue(today);
  await expect(page.getByRole('heading', { name: 'Pedidos de hoy', exact: true })).toBeVisible();
  await page.getByLabel('Fecha de servicio').fill('2026-10-25');
  await expect(page.getByTestId('order-card')).toHaveCount(0);
  await page.getByRole('button', { name: 'Consultar pedidos', exact: true }).click();
  await expect(page.getByTestId('order-card')).toHaveCount(18); expect(state.lastDate).toBe('2026-10-25');
  await page.getByRole('button', { name: 'Hoy', exact: true }).click();
  await expect(page.getByLabel('Fecha de servicio')).toHaveValue(today);
  await expect(page.getByTestId('order-card')).toHaveCount(18); expect(state.lastDate).toBe(today);
  await page.goto(`/admin/pedidos?date=2026-10-25&orderId=${orderId}`);
  await expect(page.getByText('Detalle interno', { exact: true })).toBeVisible();
  await expect(page.getByLabel('Fecha de servicio')).toHaveValue('2026-10-25');
  await expect(page.getByTestId('order-card').first()).toHaveClass(/detail-open/);
  expect(state.writes).toEqual([]);
});
