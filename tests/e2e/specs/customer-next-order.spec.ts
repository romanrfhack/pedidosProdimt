import { expect, test, type Page } from '@playwright/test';

const customerId = '11111111-1111-1111-1111-111111111111';
const productA = '22222222-2222-2222-2222-222222222201';
const productB = '22222222-2222-2222-2222-222222222202';
const corsHeaders = {
  'access-control-allow-origin': '*', 'access-control-allow-headers': 'authorization,content-type',
  'access-control-allow-methods': 'GET,POST,OPTIONS'
};

function nextFixture() {
  return {
    customerId, customerName: 'Cliente Enlace Demo', deliveryDate: '2026-10-31', serverNow: '2026-10-31T17:55:00-06:00',
    profileVersion: 5, isProfileApproved: true, hasActiveOrder: false,
    products: [
      { productId: productA, productName: 'Tortilla demo #11', unitCode: 'kg' as string | null, interpretationPosition: 1, captureAlias: null, sortOrder: 1 },
      { productId: productB, productName: 'Tortilla demo #12', unitCode: 'kg' as string | null, interpretationPosition: 2, captureAlias: null, sortOrder: 2 }
    ],
    currentOrder: null as null | { orderId: string; deliveryDate: string; status: string; submittedAt: string; deliveryNotes: string | null;
      lines: Array<{ productId: string; productName: string; quantity: number; unitCode: string | null; notes: string | null }> },
    preferredDeliveryTime: '14:00:00', preferredDeliveryWindowStart: null, preferredDeliveryWindowEnd: null
  };
}

async function mockPortal(page: Page, options: { approved?: boolean; existing?: boolean; unknownUnit?: boolean; expired?: boolean } = {}) {
  const next = nextFixture();
  next.isProfileApproved = options.approved ?? true;
  if (options.unknownUnit) next.products[0].unitCode = null;
  if (options.existing) {
    next.hasActiveOrder = true;
    next.currentOrder = { orderId: 'existing-demo', deliveryDate: next.deliveryDate, status: 'Submitted', submittedAt: next.serverNow,
      deliveryNotes: 'Tocar el timbre.', lines: [{ productId: productA, productName: next.products[0].productName, quantity: 14, unitCode: 'kg', notes: null }] };
  }
  const state = { next, captures: [] as any[], noOrders: [] as any[], loginUrls: [] as string[],
    failCapture: false, crossCutoff: false, denyCapture: false };
  await page.route(/\/api\/auth\/customer-token$/, async route => {
    if (route.request().method() === 'OPTIONS') { await route.fulfill({ status: 204, headers: corsHeaders }); return; }
    state.loginUrls.push(page.url());
    await route.fulfill({ headers: corsHeaders, status: options.expired ? 401 : 200, json: options.expired
      ? { error: 'El enlace venció.' }
      : { accessToken: 'mock-customer-jwt', tokenType: 'Bearer', expiresAt: '2099-01-01T00:00:00Z',
          accessLinkExpiresAt: '2099-01-01T00:00:00Z', customerId, customerName: next.customerName } });
  });
  await page.route(/\/api\/customer-orders\/[^/]+\/(next|preview|capture|next\/no-order)$/, async route => {
    if (route.request().method() === 'OPTIONS') { await route.fulfill({ status: 204, headers: corsHeaders }); return; }
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/next')) { await route.fulfill({ headers: corsHeaders, json: next }); return; }
    const body = route.request().postDataJSON();
    if (path.endsWith('/preview')) {
      const quantities = String(body.text).split('+').map(Number);
      await route.fulfill({ headers: corsHeaders, json: {
        deliveryDate: next.deliveryDate, serverNow: next.serverNow, text: body.text, fingerprint: `preview-${body.text}-${next.deliveryDate}`,
        profileVersion: next.profileVersion, lines: next.products.map((p, index) => ({ ...p, quantity: quantities[index] ?? 0 })).filter(x => x.quantity > 0)
      } }); return;
    }
    const noOrder = path.endsWith('/next/no-order');
    (noOrder ? state.noOrders : state.captures).push(body);
    if (state.failCapture && !noOrder) { state.failCapture = false; await route.abort('failed'); return; }
    if (state.denyCapture) { await route.fulfill({ status: 401, headers: corsHeaders, json: { error: 'Acceso revocado.' } }); return; }
    if (state.crossCutoff) {
      state.crossCutoff = false; next.deliveryDate = '2026-11-01'; next.serverNow = '2026-10-31T18:01:00-06:00';
      await route.fulfill({ status: 409, headers: corsHeaders, json: { error: 'La fecha de entrega cambió.', code: 'DeliveryDateChanged',
        deliveryDate: next.deliveryDate, serverNow: next.serverNow } }); return;
    }
    const quantities = body.text ? String(body.text).split('+').map(Number) : null;
    const lines = noOrder ? [] : next.products.map((p, index) => ({ productId: p.productId, productName: p.productName,
      unitCode: p.unitCode, quantity: quantities ? quantities[index] ?? 0 : body.lines.find((x: any) => x.productId === p.productId)?.quantity ?? 0,
      notes: null })).filter(x => x.quantity > 0);
    next.hasActiveOrder = !noOrder;
    next.currentOrder = { orderId: 'order-demo', deliveryDate: body.expectedDeliveryDate, status: noOrder ? 'NoOrder' : 'Submitted',
      submittedAt: next.serverNow, deliveryNotes: body.deliveryNotes ?? null, lines };
    await route.fulfill({ headers: corsHeaders, json: { replayed: state.captures.length > 1, order: { orderId: 'order-demo', customerId,
      orderDate: body.expectedDeliveryDate, status: next.currentOrder.status, sequenceNumber: 1, submittedAt: next.serverNow,
      isLate: false, requiresAdminReview: false, adminReviewReason: null } } });
  });
  return state;
}

async function enter(page: Page, fragment = true) {
  await page.goto(fragment ? '/cliente#token=link-demo' : '/cliente?token=link-demo');
  await expect(page.getByTestId('customer-delivery-date')).toBeVisible();
}

async function enterQuick(page: Page, text = '13+7') {
  await page.getByLabel('Tu pedido', { exact: true }).fill(text);
  await page.getByLabel('Tu pedido', { exact: true }).press('Tab');
  await expect(page.getByTestId('customer-capture-breakdown')).toBeVisible();
}

test('enlace fragmento y query legado limpian la URL antes de autenticar', async ({ page }) => {
  const state = await mockPortal(page);
  await enter(page);
  expect(state.loginUrls[0]).not.toContain('token');
  await expect(page).toHaveURL(/\/cliente$/);
  await expect(page.getByTestId('customer-delivery-date')).toContainText('sábado, 31 de octubre de 2026');
  await expect(page.getByText(/Enlace disponible hasta/)).toBeVisible();
  await enter(page, false);
  expect(state.loginUrls[1]).not.toContain('token');
  await expect(page).toHaveURL(/\/cliente$/);
});

test('un enlace vencido limpia la URL y permite pegar otro token', async ({ page }) => {
  const state = await mockPortal(page, { expired: true });
  await page.goto('/cliente#token=expired-demo');
  await expect(page.getByRole('alert')).toContainText('El enlace venció.');
  expect(state.loginUrls[0]).not.toContain('token');
  await expect(page.getByLabel('Token de acceso')).toBeVisible();
  await expect(page.getByTestId('customer-capture')).toHaveCount(0);
});

test('un enlace inválido no conserva la identidad de otro cliente previamente autenticado', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('prodimt.auth.session.v1', JSON.stringify({
    accessToken: 'another-customer-session', tokenType: 'Bearer', expiresAt: '2099-01-01T00:00:00Z', actorType: 'Customer',
    customerId: '11111111-1111-1111-1111-111111111112', customerName: 'Otro cliente', displayName: null
  })));
  await mockPortal(page, { expired: true });
  await page.goto('/cliente#token=invalid-link-demo');
  await expect(page.getByRole('alert')).toContainText('El enlace venció.');
  await expect(page.getByLabel('Token de acceso')).toBeVisible();
  await expect(page.getByTestId('customer-delivery-date')).toHaveCount(0);
  expect(await page.evaluate(() => localStorage.getItem('prodimt.auth.session.v1'))).toBeNull();
});

test('rápida y manual conservan cantidades y notas al cambiar de pestaña', async ({ page }) => {
  await mockPortal(page); await enter(page); await enterQuick(page);
  await page.getByLabel('Notas de entrega').fill('Tocar el timbre.');
  await page.getByRole('button', { name: 'Captura manual' }).click();
  await expect(page.getByLabel('Cantidad de Tortilla demo #11')).toHaveValue('13');
  await expect(page.getByLabel('Cantidad de Tortilla demo #12')).toHaveValue('7');
  await page.getByLabel('Cantidad de Tortilla demo #11').fill('17');
  await page.getByRole('button', { name: 'Captura rápida' }).click();
  await expect(page.getByLabel('Tu pedido', { exact: true })).toHaveValue('17+7');
  await expect(page.getByTestId('customer-capture-breakdown')).toContainText('17 kg');
  await page.getByRole('button', { name: 'Captura manual' }).click();
  await expect(page.getByLabel('Cantidad de Tortilla demo #11')).toHaveValue('17');
  await expect(page.getByLabel('Notas de entrega')).toHaveValue('Tocar el timbre.');
});

test('pasar directamente del texto rápido a manual espera el desglose y conserva las cantidades', async ({ page }) => {
  await mockPortal(page); await enter(page);
  await page.getByLabel('Tu pedido', { exact: true }).fill('13+7');
  await page.getByRole('button', { name: 'Captura manual' }).click();
  await expect(page.getByLabel('Cantidad de Tortilla demo #11')).toHaveValue('13');
  await expect(page.getByLabel('Cantidad de Tortilla demo #12')).toHaveValue('7');
});

test('confirmar rápida envía fecha observada y huella y después muestra pedido sin duplicar', async ({ page }) => {
  const state = await mockPortal(page); await enter(page); await enterQuick(page);
  expect(state.captures).toHaveLength(0);
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await expect(page.getByTestId('customer-current-order')).toContainText('13 kg');
  await expect(page.getByTestId('customer-capture')).toHaveCount(0);
  expect(state.captures).toHaveLength(1);
  expect(state.captures[0]).toMatchObject({ text: '13+7', expectedProfileVersion: 5, expectedDeliveryDate: '2026-10-31',
    previewFingerprint: 'preview-13+7-2026-10-31' });
  expect(state.captures[0]).not.toHaveProperty('internalNotes');
  await expect(page.getByTestId('customer-today')).not.toContainText(/máquina|maquina|machine|auditoría|audit|configurar cliente/i);
});

test('manual funciona sin aprobación y bloquea el producto cuya unidad no está confirmada', async ({ page }) => {
  const state = await mockPortal(page, { approved: false, unknownUnit: true }); await enter(page);
  await expect(page.getByLabel('Cantidad de Tortilla demo #11')).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Confirmar pedido', exact: true })).toBeDisabled();
  await page.getByLabel('Cantidad de Tortilla demo #12').fill('4');
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await expect(page.getByTestId('customer-current-order')).toContainText('4 kg');
  expect(state.captures[0].lines).toEqual([{ productId: productB, quantity: 4, expectedUnitCode: 'kg', notes: null }]);
});

test('cruzar las 18:00 conserva manual y exige confirmar de nuevo para la nueva fecha', async ({ page }) => {
  const state = await mockPortal(page); await enter(page);
  await page.getByRole('button', { name: 'Captura manual' }).click();
  await page.getByLabel('Cantidad de Tortilla demo #11').fill('13');
  await page.getByLabel('Notas de entrega').fill('Entrada principal.');
  state.crossCutoff = true;
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await expect(page.getByTestId('customer-delivery-date')).toContainText('domingo, 1 de noviembre de 2026');
  await expect(page.getByText(/Conservamos tus cantidades/)).toBeVisible();
  await expect(page.getByLabel('Cantidad de Tortilla demo #11')).toHaveValue('13');
  await expect(page.getByLabel('Notas de entrega')).toHaveValue('Entrada principal.');
  expect(state.captures).toHaveLength(1);
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await expect(page.getByTestId('customer-current-order')).toContainText('13 kg');
  expect(state.captures[1].expectedDeliveryDate).toBe('2026-11-01');
  expect(state.captures[1].operationId).not.toBe(state.captures[0].operationId);
});

test('respuesta perdida bloquea el borrador y reintenta con exactamente la misma referencia', async ({ page }) => {
  const state = await mockPortal(page); await enter(page); await enterQuick(page);
  state.failCapture = true;
  await page.getByRole('button', { name: 'Confirmar pedido', exact: true }).click();
  await expect(page.getByTestId('customer-capture-retry')).toBeVisible();
  await expect(page.getByLabel('Tu pedido', { exact: true })).toBeDisabled();
  await expect(page.getByLabel('Notas de entrega')).toBeDisabled();
  await page.getByTestId('customer-capture-retry').click();
  await expect(page.getByTestId('customer-current-order')).toBeVisible();
  expect(state.captures).toHaveLength(2);
  expect(state.captures[1]).toEqual(state.captures[0]);
});

test('No pedir requiere confirmación explícita para la fecha de entrega visible', async ({ page }) => {
  const state = await mockPortal(page); await enter(page);
  await page.getByRole('button', { name: 'No pedir para esta entrega' }).click();
  await expect(page.getByTestId('customer-no-order-confirmation')).toContainText('sábado, 31 de octubre de 2026');
  expect(state.noOrders).toHaveLength(0);
  await page.getByRole('button', { name: 'Confirmar que no pediré' }).click();
  await expect(page.getByTestId('customer-current-order')).toContainText('Confirmaste que no pedirás para esta entrega');
  expect(state.noOrders[0].expectedDeliveryDate).toBe('2026-10-31');
  await expect(page.getByRole('button', { name: 'No pedir para esta entrega' })).toBeDisabled();
});

test('pedido existente muestra detalle y contacto, con portal adaptable a 360px', async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 800 });
  const state = await mockPortal(page, { existing: true }); await enter(page);
  await expect(page.getByTestId('customer-current-order')).toContainText('14 kg');
  await expect(page.getByText(/Si necesitas cambiarlo, comunícate con PRODIMT/)).toBeVisible();
  await expect(page.getByTestId('customer-capture')).toHaveCount(0);
  expect(state.captures).toHaveLength(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
