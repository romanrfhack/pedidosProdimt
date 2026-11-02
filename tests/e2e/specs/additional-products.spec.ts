import { expect, test, type Page, type Route } from '@playwright/test';

const customerId = '11111111-1111-1111-1111-111111111111';
const habitual = '22222222-2222-2222-2222-222222222201';
const extraId = '22222222-2222-2222-2222-222222222299';
const setupId = '55555555-5555-4555-8555-555555555501';
const extras = [{ productId: extraId, productName: 'Burrera de prueba', unitCode: 'package' },
  { productId: '22222222-2222-2222-2222-222222222298', productName: 'Harina de prueba', unitCode: 'kg' }];
const cors = { 'access-control-allow-origin': '*', 'access-control-allow-headers': 'authorization,content-type',
  'access-control-allow-methods': 'GET,POST,PUT,PATCH,OPTIONS' };
const respond = (route: Route, json: unknown, status = 200) => route.fulfill({ headers: cors,
  status: route.request().method() === 'OPTIONS' ? 204 : status,
  ...(route.request().method() === 'OPTIONS' ? {} : { json }) });
test.beforeEach(async ({ request }) => { await request.post('http://127.0.0.1:5088/__test/reset'); });

async function portal(page: Page, loseResponse = false) {
  const state: any = { writes: [], saved: false, conflict: false, next: { customerId, customerName: 'Cliente de prueba',
    deliveryDate: '2026-11-02', serverNow: '2026-11-02T08:00:00-06:00', profileVersion: 1,
    isProfileApproved: false, hasActiveOrder: false, currentOrder: null,
    products: [{ productId: habitual, productName: 'Tortilla habitual de prueba', unitCode: 'kg', interpretationPosition: 1, captureAlias: null, sortOrder: 1 }],
    preferredDeliveryTime: null, preferredDeliveryWindowStart: null, preferredDeliveryWindowEnd: null } };
  await page.route('**/api/auth/customer-token', route => respond(route, { accessToken: 'fixture-customer-token',
    tokenType: 'Bearer', expiresAt: '2099-01-01T00:00:00Z', accessLinkExpiresAt: '2099-01-01T00:00:00Z', customerId, customerName: state.next.customerName }));
  await page.route(`**/api/customer-orders/${customerId}/next`, route => respond(route, state.next));
  await page.route(`**/api/customer-orders/${customerId}/additional-products`, route => respond(route, extras));
  await page.route(`**/api/customer-orders/${customerId}/capture`, async route => {
    if (route.request().method() === 'OPTIONS') { await respond(route, {}); return; }
    const body = route.request().postDataJSON(); state.writes.push(body);
    if (state.conflict) { state.conflict = false; await respond(route, { code: "CatalogChanged", error: "Revisa la configuración actualizada." }, 409); return; }
    const replayed = state.saved; state.saved = true;
    state.next.hasActiveOrder = true;
    state.next.currentOrder = { orderId: 'fixture-order', deliveryDate: body.expectedDeliveryDate,
      status: 'Submitted', submittedAt: state.next.serverNow, deliveryNotes: body.deliveryNotes,
      lines: body.lines.map((x: any) => ({ productId: x.productId, productName: x.productId === habitual
        ? 'Tortilla habitual de prueba' : extras.find(p => p.productId === x.productId)!.productName,
        unitCode: x.expectedUnitCode, quantity: x.quantity, notes: x.notes })) };
    if (loseResponse && state.writes.length === 1) { await route.abort('failed'); return; }
    await respond(route, { replayed, order: { orderId: 'fixture-order', customerId, orderDate: body.expectedDeliveryDate,
      status: 'Submitted', sequenceNumber: 1, submittedAt: state.next.serverNow, isLate: false, requiresAdminReview: false, adminReviewReason: null } });
  });
  await page.goto('/cliente#token=fixture-access-token');
  await expect(page.getByRole('heading', { name: 'Mi próximo pedido' })).toBeVisible();
  return state;
}
async function choose(page: Page, quantity = '3') {
  await page.getByRole('button', { name: 'Pedir otro producto' }).click();
  const dialog = page.getByRole('dialog'); await expect(dialog.getByLabel('Producto', { exact: true })).toBeEnabled();
  await dialog.getByLabel('Producto', { exact: true }).selectOption(extraId);
  await dialog.getByLabel('Cantidad', { exact: true }).fill(quantity);
  return dialog;
}
async function loginAdmin(page: Page) {
  await page.goto('/admin/login'); await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
}

test('cliente agrega desde catálogo con unidad, guarda sólo al confirmar y conserva cantidades habituales', async ({ page }) => {
  const state = await portal(page);
  await page.getByLabel('Cantidad de Tortilla habitual de prueba').fill('15');
  const dialog = await choose(page);
  await expect(dialog).toContainText('Unidad de venta: paquetes');
  await expect(dialog.getByText(/precio|máquina/i)).toHaveCount(0);
  await dialog.getByRole('button', { name: 'Agregar al pedido', exact: true }).click();
  expect(state.writes).toEqual([]);
  await expect(page.getByLabel('Cantidad de Burrera de prueba')).toHaveValue('3');
  await expect(page.getByLabel('Cantidad de Tortilla habitual de prueba')).toHaveValue('15');
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await expect.poll(() => state.writes.length).toBe(1);
  expect(state.writes[0].lines).toEqual(expect.arrayContaining([
    { productId: habitual, quantity: 15, expectedUnitCode: 'kg', notes: null },
    { productId: extraId, quantity: 3, expectedUnitCode: 'package', notes: null }
  ]));
  await expect(page.getByText('3 paquetes de Burrera de prueba', { exact: true })).toBeVisible();
});

test('cancelar no escribe, cambiar producto limpia cantidad y no permite cantidad inválida', async ({ page }) => {
  const state = await portal(page); const dialog = await choose(page, '-2');
  await expect(dialog.getByRole('button', { name: 'Agregar al pedido' })).toBeDisabled();
  await dialog.getByLabel('Cantidad', { exact: true }).fill('1.234');
  await expect(dialog.getByRole('button', { name: 'Agregar al pedido' })).toBeDisabled();
  await dialog.getByLabel('Cantidad', { exact: true }).fill('2');
  await dialog.getByLabel('Producto', { exact: true }).selectOption(extras[1].productId);
  await expect(dialog.getByLabel('Cantidad', { exact: true })).toHaveValue('');
  await page.keyboard.press('Escape'); await expect(dialog).toHaveCount(0);
  expect(state.writes).toEqual([]); await expect(page.getByRole('button', { name: 'Pedir otro producto' })).toBeFocused();
});

test('recargar configuración tras un conflicto conserva el producto extra y bloquea cambio a rápida', async ({ page }) => {
  const state = await portal(page); const dialog = await choose(page, '4');
  await dialog.getByRole('button', { name: 'Agregar al pedido' }).click();
  state.conflict = true;
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Revisa la configuración actualizada.');
  await expect(page.getByLabel('Cantidad de Burrera de prueba')).toHaveValue('4');
  await page.getByRole('button', { name: 'Captura rápida', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Conserva la captura manual');
  await expect(page.getByLabel('Cantidad de Burrera de prueba')).toHaveValue('4');
  await page.getByRole('button', { name: 'Quitar Burrera de prueba' }).click();
  await expect(page.getByLabel('Cantidad de Burrera de prueba')).toHaveCount(0);
});

test('respuesta de captura perdida conserva la referencia y el producto agregado al reintentar', async ({ page }) => {
  const state = await portal(page, true); const dialog = await choose(page);
  await dialog.getByRole('button', { name: 'Agregar al pedido' }).click();
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await expect(page.getByTestId('customer-capture-retry')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Pedir otro producto' })).toBeDisabled();
  await page.getByTestId('customer-capture-retry').click();
  await expect.poll(() => state.writes.length).toBe(2); expect(state.writes[1]).toEqual(state.writes[0]);
  await expect(page.getByText('3 paquetes de Burrera de prueba', { exact: true })).toBeVisible();
});

for (const width of [320, 768, 1366]) {
  test(`selector compartido se adapta a ${width}px y recupera el foco`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 800 }); await portal(page); const dialog = await choose(page);
    const box = await dialog.boundingBox(); expect(box!.x).toBeGreaterThanOrEqual(0);
    expect(box!.x + box!.width).toBeLessThanOrEqual(width); expect(box!.height).toBeLessThanOrEqual(800);
    expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
    if (width === 1366 || width === 320) await page.screenshot({ path: testInfo.outputPath('additional-product-picker.png') });
    await dialog.getByRole('button', { name: 'Cancelar', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Pedir otro producto' })).toBeFocused();
  });
}

test('administración usa el mismo selector y envía el producto adicional como parte del mismo pedido', async ({ page }) => {
  const writes: any[] = [];
  await page.route('**/api/admin/customers/11111111-1111-1111-1111-111111111112/additional-products', route => respond(route, extras));
  await page.route('**/api/admin/customers/11111111-1111-1111-1111-111111111112/orders/capture', route => {
    if (route.request().method() === 'OPTIONS') return respond(route, {});
    writes.push(route.request().postDataJSON());
    return respond(route, { replayed: false, order: { orderId: 'fixture-admin-order', customerId,
      orderDate: '2026-11-02', status: 'Submitted', sequenceNumber: 1, submittedAt: '2026-11-02T14:00:00Z',
      isLate: false, requiresAdminReview: false, adminReviewReason: null, customerName: 'Cliente Demo 2', totalQuantity: 2, lineCount: 1 } });
  });
  await loginAdmin(page); await page.goto('/admin/clientes-pendientes');
  await page.locator('article').filter({ hasText: 'Cliente Demo 2' }).getByRole('button', { name: 'Capturar pedido', exact: true }).click();
  await page.getByRole('button', { name: 'Captura manual', exact: true }).click();
  const dialog = await choose(page, '2'); await dialog.getByRole('button', { name: 'Agregar al pedido' }).click();
  expect(writes).toEqual([]);
  await page.getByTestId('order-capture').getByRole('button', { name: 'Guardar pedido', exact: true }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(writes[0].lines).toContainEqual({ productId: extraId, quantity: 2, expectedUnitCode: 'package', notes: null });
});

async function inbox(page: Page, options: { lost?: boolean; stale?: boolean; blocked?: boolean } = {}) {
  const state: any = { writes: [], resolved: false, preview: { id: setupId, version: 1, fingerprint: 'a'.repeat(64),
    canComplete: !options.blocked, blockers: options.blocked ? ['Guarda primero el producto y su tarifa.'] : [],
    lines: [{ orderId: 'fixture-one', orderVersion: 1, orderLineId: 'line-one', serviceDate: '2026-11-02',
      quantity: 2, unitCode: 'package', isActive: true, currentPrice: null, proposedPrice: 30,
      revisionId: 'fixture-revision', effectiveFrom: '2026-11-02', applyPrice: true },
    { orderId: 'fixture-two', orderVersion: 1, orderLineId: 'line-two', serviceDate: '2026-11-03',
      quantity: 3, unitCode: 'package', isActive: true, currentPrice: 0, proposedPrice: 30,
      revisionId: 'fixture-revision', effectiveFrom: '2026-11-02', applyPrice: false }] } };
  const setup: any = { id: setupId, version: 1, customerId, customerName: 'Cliente de prueba', productId: extraId,
    productName: 'Burrera de prueba', unitCode: 'package', isResolved: false, isConfigured: true,
    createdAt: '2026-11-02T14:00:00Z', lastRequestedAt: '2026-11-02T14:00:00Z', orders: [
      { orderId: 'fixture-one', serviceDate: '2026-11-02', quantity: 2, unitCode: 'package', source: 'Customer',
        requestedAt: '2026-11-02T14:00:00Z', requestedBy: 'Cliente de prueba', isActive: true, unitPrice: null, pricePending: true }] };
  state.setup = setup;
  await page.route('**/api/admin/product-setups/count', route => respond(route, { pendingCount: state.resolved ? 0 : 1 }));
  await page.route('**/api/admin/product-setups', route => respond(route, state.resolved ? [] : [setup]));
  await page.route(`**/api/admin/product-setups/${setupId}`, route => respond(route, { ...setup, isResolved: state.resolved }));
  await page.route(`**/api/admin/product-setups/${setupId}/resolution-preview`, route => respond(route, state.preview));
  await page.route(`**/api/admin/product-setups/${setupId}/complete`, async route => {
    if (route.request().method() === 'OPTIONS') { await respond(route, {}); return; }
    state.writes.push(route.request().postDataJSON());
    if (options.stale && state.writes.length === 1) { state.preview.fingerprint = 'b'.repeat(64); await respond(route, { error: 'La tarifa cambió. Revisa de nuevo.' }, 409); return; }
    state.resolved = true;
    if (options.lost && state.writes.length === 1) { await route.abort('failed'); return; }
    await respond(route, { id: setupId, version: 2, resolvedAt: '2026-11-02T14:05:00Z', pricedLines: 1, replayed: state.writes.length > 1 });
  });
  await loginAdmin(page); await page.goto(`/admin/productos-por-configurar?requestId=${setupId}`);
  return state;
}
async function confirmReview(page: Page) {
  const review = page.getByTestId('product-setup-review');
  await review.getByLabel('Motivo de la revisión').fill('Configuración y tarifa de prueba revisadas');
  await review.getByLabel('He revisado las tarifas y los pedidos incluidos.').check();
  await review.getByRole('button', { name: 'Completar configuración', exact: true }).click();
}

test('bandeja muestra origen y precio pendiente, permite revisar sin reemplazar un precio cero y completa', async ({ page }) => {
  const state = await inbox(page); const review = page.getByTestId('product-setup-review');
  await expect(page.getByTestId('product-setup-card')).toContainText('Precio pendiente');
  await expect(page.getByTestId('product-setup-card')).toContainText('Capturado por el cliente');
  await expect(review).toContainText('Conserva $0.00');
  await expect(review.getByRole('button', { name: 'Completar configuración' })).toBeDisabled();
  await confirmReview(page);
  await expect(review.getByRole('status')).toContainText('Configuración completada');
  expect(state.writes).toHaveLength(1); expect(state.writes[0].previewFingerprint).toBe('a'.repeat(64));
  await expect(page.getByTestId('product-setup-card')).toHaveCount(0);
});

test('revisión incompleta impide cerrar el pendiente y no crea un precio automático', async ({ page }) => {
  const state = await inbox(page, { blocked: true }); const review = page.getByTestId('product-setup-review');
  await expect(review).toContainText('Guarda primero el producto y su tarifa.');
  await expect(review.getByRole('button', { name: 'Completar configuración' })).toBeDisabled(); expect(state.writes).toEqual([]);
});

test('bandeja abre configuración completa y agrega relación sin aprobar posiciones ni heredar máquina', async ({ page }) => {
  const writes: any[] = [];
  await page.route('**/api/admin/products', async route => {
    if (route.request().method() === 'OPTIONS') return respond(route, {});
    const response = await route.fetch(); const products = await response.json();
    return respond(route, [...products, { id: extraId, name: 'Burrera de prueba', unitCode: 'package', isActive: true, description: null }]);
  });
  page.on('request', request => { if (request.method() === 'PUT' && request.url().endsWith('/frequent-products')) writes.push(request.postDataJSON()); });
  const state = await inbox(page, { blocked: true }); state.setup.isConfigured = false;
  await page.getByTestId('product-setup-card').getByRole('link', { name: 'Configurar producto', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/admin/clientes/${customerId}/configuracion\\?setupRequestId=${setupId}`));
  const review = page.getByTestId('product-setup-review');
  await review.getByRole('button', { name: 'Agregar a productos del cliente' }).click();
  const row = page.getByTestId('profile-product').filter({ has: page.getByRole('button', { name: 'Precio e historial de Burrera de prueba', exact: true }) });
  await expect(row).toHaveCount(1);
  await expect(row.getByLabel('Máquina para este producto', { exact: true })).toHaveValue('none');
  await expect(row.getByLabel('Posición en mensaje', { exact: true })).toHaveValue('');
  await expect(row.getByRole('button', { name: 'Precio e historial de Burrera de prueba', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Guardar frecuentes', exact: true }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(writes[0].approveForInterpretation).toBe(false);
  expect(writes[0].items.find((x: any) => x.productId === extraId)).toMatchObject({
    preferredMachineId: null, useCustomerDefaultMachine: false, interpretationPosition: null, expectedUnitCode: 'package'
  });
  await expect(row.getByRole('button', { name: 'Precio e historial de Burrera de prueba', exact: true })).toBeEnabled();
  await row.getByRole('button', { name: 'Precio e historial de Burrera de prueba', exact: true }).click();
  await expect(page.getByRole('dialog').getByLabel('Precio por paquetes (MXN)', { exact: true })).toBeVisible();
});

test('confirmación perdida se recupera después de recargar con la misma operación', async ({ page }) => {
  const state = await inbox(page, { lost: true }); await confirmReview(page);
  await expect(page.getByRole('button', { name: 'Reintentar confirmación' })).toBeVisible();
  await page.reload(); await expect(page.getByRole('button', { name: 'Reintentar confirmación' })).toBeVisible();
  await page.getByRole('button', { name: 'Reintentar confirmación' }).click();
  await expect(page.getByTestId('product-setup-review').getByRole('status')).toContainText('Configuración completada');
  expect(state.writes).toHaveLength(2); expect(state.writes[1]).toEqual(state.writes[0]);
});

test('conflicto exige actualizar vista previa y volver a confirmar con una referencia nueva', async ({ page }) => {
  const state = await inbox(page, { stale: true }); await confirmReview(page);
  const review = page.getByTestId('product-setup-review');
  await expect(review.getByRole('alert')).toContainText('La tarifa cambió');
  await review.getByRole('button', { name: 'Actualizar revisión' }).click(); await confirmReview(page);
  await expect(review.getByRole('status')).toContainText('Configuración completada');
  expect(state.writes).toHaveLength(2); expect(state.writes[1].operationId).not.toBe(state.writes[0].operationId);
  expect(state.writes[1].previewFingerprint).toBe('b'.repeat(64));
});
