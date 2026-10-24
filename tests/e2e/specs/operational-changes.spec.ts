import { expect, test, type Page } from '@playwright/test';
const api = 'http://127.0.0.1:5088';
const date = '2026-10-25';
const orderId = '33333333-3333-3333-3333-333333333301';
const customerId = '11111111-1111-1111-1111-111111111112';
const changeId = '66666666-6666-6666-6666-666666666601';
const machineId = '44444444-4444-4444-4444-444444444401';
async function login(page: Page) {
  await page.goto('/admin/login');
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
}
function notice(overrides: any = {}) {
  return { id: changeId, orderId, orderVersion: 2, version: 1, kind: 'correction', serviceDate: date,
    customerId, customerName: 'Cliente de aviso demo', routeCode: 'DEMO', machineId, machineNumber: 1, machineName: null,
    reason: 'Cliente pidió cinco kilos más', actorDisplayName: 'Operador demo', occurredAt: `${date}T12:00:00Z`, acknowledgedAt: null, acknowledgedBy: null,
    lines: [{ productId: 'product-kg', productName: 'Producto de prueba', unitCode: 'kg', beforeQuantity: 15, afterQuantity: 20, deltaQuantity: 5, beforeNotes: null, afterNotes: 'Total corregido' }], ...overrides };
}
function plan(version = 2, quantity = 20) {
  return { date, routeCode: null, demandLabel: 'Por producir', preparationLabel: 'Por preparar',
    orders: [{ orderId, orderVersion: version, customerId, customerName: 'Cliente de aviso demo', sequenceNumber: 1, status: 'Accepted', routeCode: 'DEMO', routeStopOrder: 1,
      hasStoreKey: null, paymentTermCode: null, internalDeliveryInstructions: null, requestedDeliveryTime: null, requestedDeliveryWindowStart: null, requestedDeliveryWindowEnd: null,
      deliveryNotes: null, orderingProfileVersion: 1,
      lines: [{ orderLineId: 'line-kg', productId: 'product-kg', productName: 'Producto de prueba', unitCode: 'kg', quantity, assignedMachineId: machineId, assignedMachineNumber: 1, assignedMachineName: null, notes: null }] }],
    machineDemand: [{ machineId, machineNumber: 1, machineName: null, productId: 'product-kg', productName: 'Producto de prueba', unitCode: 'kg', quantity }],
    pendingReview: [], warnings: [] };
}
async function board(page: Page, notices: () => any[], readPlan: () => any = () => plan()) {
  await page.route(/\/api\/admin\/operations\/plan\?/, route => route.fulfill({ json: readPlan() }));
  await page.route(/\/api\/admin\/operations\/changes\?/, route => route.fulfill({ json: { date, pendingCount: notices().filter(item => !item.acknowledgedAt).length, changes: notices() } }));
  await page.goto(`/admin/operacion?date=${date}`);
  await expect(page.getByTestId('operations-changes')).toBeVisible();
}
async function visibleAgain(page: Page) { await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange'))); }
test.beforeEach(async ({ request }) => { await request.post(`${api}/__test/reset`); });

test('aviso conserva antes después y diferencia al recargar; recibir una revisión no recibe la siguiente', async ({ page }) => {
  await login(page);
  let notices = [notice()];
  await board(page, () => notices, () => plan(3));
  const pending = page.getByTestId('pending-operation-change');
  await expect(pending).toContainText('Antes: 15 kg → Después: 20 kg');
  await expect(pending).toContainText('+5 kg solicitados');
  await expect(pending).toContainText('Total corregido');
  await page.reload();
  await expect(pending).toHaveCount(1);
  notices.push(notice({ id: 'second-change', orderVersion: 3, reason: 'Segunda corrección demo' }));
  await visibleAgain(page);
  await expect(pending).toHaveCount(2);
  await page.route(`**/operations/changes/${changeId}/acknowledge`, async route => {
    expect(route.request().postDataJSON()).toMatchObject({ expectedVersion: 1 });
    notices = notices.map(item => item.id === changeId ? { ...item, version: 2, acknowledgedAt: `${date}T12:05:00Z`, acknowledgedBy: 'Operador receptor' } : item);
    await route.fulfill({ json: notices[0] });
  });
  await pending.first().getByRole('button', { name: 'Cambio recibido' }).click();
  await expect(pending).toHaveCount(1);
  await expect(pending).toContainText('Segunda corrección demo');
  await page.getByText('1 avisos recibidos · ver historial').click();
  await expect(page.getByTestId('received-operation-change')).toContainText('Operador receptor');
  await page.reload();
  await expect(pending).toHaveCount(1);
  await expect(page.getByTestId('machine-demand')).toContainText('20 kg');
});

test('respuesta de recepción perdida permite reintentar la misma referencia sin ocultar el aviso', async ({ page }) => {
  await login(page);
  await board(page, () => [notice()]);
  const requests: any[] = [];
  await page.route(`**/operations/changes/${changeId}/acknowledge`, async route => {
    requests.push(route.request().postDataJSON());
    if (requests.length === 1) return route.abort('failed');
    await route.fulfill({ json: notice({ version: 2, acknowledgedAt: `${date}T12:05:00Z`, acknowledgedBy: 'Operador demo' }) });
  });
  await page.getByRole('button', { name: 'Cambio recibido' }).click();
  await expect(page.getByRole('alert')).toContainText('No se recibió confirmación');
  await expect(page.getByTestId('pending-operation-change')).toBeVisible();
  await page.getByRole('button', { name: 'Cambio recibido' }).click();
  await expect(page.getByTestId('pending-operation-change')).toHaveCount(0);
  expect(requests).toHaveLength(2);
  expect(requests[1]).toEqual(requests[0]);
  expect(requests[0].operationKey).toBeTruthy();
});

test('filtro incluye avisos sin máquina, bajas y unidades separadas; móvil no desborda', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(page);
  const notices = [notice(), notice({ id: 'unassigned-notice', machineId: null, machineNumber: null, reason: 'Cliente retiró las docenas',
    lines: [{ productId: 'product-dozen', productName: 'Producto por docena', unitCode: 'dozen', beforeQuantity: 2, afterQuantity: 0, deltaQuantity: -2, beforeNotes: 'Antigua', afterNotes: null }] })];
  await board(page, () => notices);
  await expect(page.getByTestId('pending-operation-change')).toHaveCount(2);
  await page.getByLabel('Máquina a consultar').selectOption('unassigned');
  const pending = page.getByTestId('pending-operation-change');
  await expect(pending).toHaveCount(1);
  await expect(pending).toContainText('Sin máquina asignada');
  await expect(pending).toContainText('Antes: 2 docenas → Después: 0 docenas');
  await expect(pending).toContainText('-2 docenas solicitados');
  await expect(pending).toContainText('excedente');
  await expect(pending.getByRole('link', { name: 'Ver pedido y sus cambios' })).toHaveAttribute('href', `/admin/pedidos?date=${date}&orderId=${orderId}`);
  await expect(page.getByTestId('preparation-order')).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('changes-mobile.png'), fullPage: true });
  await page.getByLabel('Máquina a consultar').selectOption(machineId);
  await expect(pending).toHaveCount(1);
  await expect(pending).toContainText('+5 kg');
});

test('corrección entre plan y avisos provoca nueva lectura antes de permitir recibir', async ({ page }) => {
  await login(page);
  let reads = 0;
  await board(page, () => [notice()], () => ++reads === 1 ? plan(1, 15) : plan());
  expect(reads).toBe(2);
  await expect(page.getByTestId('machine-demand')).toContainText('20 kg');
  await expect(page.getByRole('button', { name: 'Cambio recibido' })).toBeEnabled();
});

test('lectura inconsistente se limita, conserva lo visible y bloquea recepción hasta refrescar', async ({ page }) => {
  await login(page);
  let stale = false;
  let reads = 0;
  await board(page, () => [notice()], () => { reads++; return stale ? plan(1, 15) : plan(); });
  stale = true;
  await visibleAgain(page);
  await expect(page.getByRole('alert')).toContainText('El plan cambió durante la consulta');
  expect(reads).toBe(4);
  await expect(page.getByTestId('machine-demand')).toContainText('20 kg');
  await expect(page.getByRole('button', { name: 'Cambio recibido' })).toBeDisabled();
  stale = false;
  await visibleAgain(page);
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Cambio recibido' })).toBeEnabled();
});

test('fallo de refresco conserva datos y recuperar visibilidad consulta avisos nuevos', async ({ page }) => {
  await login(page);
  let notices = [notice()];
  await board(page, () => notices, () => plan(3));
  await page.route(/\/api\/admin\/operations\/plan\?/, route => route.fulfill({ status: 503, json: { error: 'Servicio temporalmente no disponible' } }));
  await visibleAgain(page);
  await expect(page.getByRole('alert')).toContainText('pueden estar desactualizados');
  await expect(page.getByTestId('pending-operation-change')).toHaveCount(1);
  await expect(page.getByRole('button', { name: 'Cambio recibido' })).toBeDisabled();
  await page.unroute(/\/api\/admin\/operations\/plan\?/);
  await page.route(/\/api\/admin\/operations\/plan\?/, route => route.fulfill({ json: plan(3) }));
  notices.push(notice({ id: 'new-visible-change', orderVersion: 3, reason: 'Cambio posterior demo' }));
  await visibleAgain(page);
  await expect(page.getByTestId('pending-operation-change')).toHaveCount(2);
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('refresco de pedidos no pisa la corrección abierta y enlace de aviso conserva fecha', async ({ page }) => {
  await login(page);
  await page.route(/\/api\/admin\/operations\/changes\?/, route => route.fulfill({ json: { date, pendingCount: 1, changes: [notice()] } }));
  await page.goto(`/admin/pedidos?date=${date}`);
  await expect(page.getByRole('link', { name: '1 avisos pendientes de recibir' })).toHaveAttribute('href', `/admin/operacion?date=${date}`);
  await page.getByRole('button', { name: 'Ver detalle' }).click();
  await page.getByRole('button', { name: 'Corregir pedido', exact: true }).click();
  const correction = page.getByTestId('order-correction');
  await correction.getByLabel('Cantidad corregida 1').fill('25');
  await correction.getByLabel('Motivo de la corrección').fill('No perder este borrador');
  let reads = 0;
  page.on('request', request => { if (request.url().includes('/orders/today?')) reads++; });
  await visibleAgain(page);
  await expect(correction.getByLabel('Cantidad corregida 1')).toHaveValue('25');
  await expect(correction.getByLabel('Motivo de la corrección')).toHaveValue('No perder este borrador');
  expect(reads).toBe(0);
});

function existingOrder() {
  return { orderId, customerId, customerName: 'Cliente Demo 2', orderDate: date, status: 'Accepted', sequenceNumber: 1,
    submittedAt: `${date}T12:00:00Z`, requestedDeliveryTime: null, requestedDeliveryWindowStart: null, requestedDeliveryWindowEnd: null,
    isLate: false, requiresAdminReview: false, adminReviewReason: null, adminDecision: 'Accepted' };
}

test('captura detecta pedido llegado por otro canal durante edición y exige resolverlo antes de crear otro', async ({ page, request }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(page);
  let existing = false;
  await page.route(/\/api\/admin\/orders\/today\?/, route => route.fulfill({ json: existing ? [existingOrder()] : [] }));
  await page.goto('/admin/clientes-pendientes');
  await page.getByLabel('Fecha de servicio').fill(date);
  await page.getByRole('button', { name: 'Consultar clientes' }).click();
  await page.getByRole('button', { name: 'Capturar pedido' }).first().click();
  await expect(page.getByText('Captura para Cliente Demo 2')).toBeVisible();
  await expect(page.getByTestId('capture-existing-orders')).toHaveCount(0);
  existing = true;
  await page.getByRole('button', { name: 'Guardar pedido' }).click();
  await expect(page.getByRole('alert')).toContainText('Ya existe un pedido');
  expect((await (await request.get(`${api}/__test/state`)).json()).adminSubmitCalls).toBe(0);
  await expect(page.getByRole('link', { name: 'Abrir pedido #1 y corregir' })).toHaveAttribute('href', `/admin/pedidos?date=${date}&orderId=${orderId}`);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('capture-existing-mobile.png'), fullPage: true });
  await page.getByLabel('Confirmo que es una entrega adicional independiente').check();
  await page.getByRole('button', { name: 'Guardar pedido' }).click();
  await expect(page.getByText('Pedido independiente capturado; verifica su revisión antes de producir.')).toBeVisible();
  expect((await (await request.get(`${api}/__test/state`)).json()).adminSubmitCalls).toBe(1);
});

test('captura muestra pedidos existentes al abrir y falla sin escribir si no puede volver a verificarlos', async ({ page, request }) => {
  await login(page);
  let failed = false;
  await page.route(/\/api\/admin\/orders\/today\?/, route => failed ? route.fulfill({ status: 503, json: { error: 'No disponible' } }) : route.fulfill({ json: [existingOrder()] }));
  await page.goto('/admin/clientes-pendientes');
  await page.getByRole('button', { name: 'Capturar pedido' }).first().click();
  await expect(page.getByTestId('capture-existing-orders')).toBeVisible();
  await page.getByLabel('Confirmo que es una entrega adicional independiente').check();
  failed = true;
  await page.getByRole('button', { name: 'Guardar pedido' }).click();
  await expect(page.getByRole('alert')).toContainText('No disponible');
  await expect(page.getByText('Captura para Cliente Demo 2')).toBeVisible();
  expect((await (await request.get(`${api}/__test/state`)).json()).adminSubmitCalls).toBe(0);
});


test('sondeo de 15 segundos consulta cambios sin recargar la pantalla', async ({ page }) => {
  await login(page);
  await page.clock.install();
  let notices: any[] = [];
  await board(page, () => notices);
  await expect(page.getByTestId('pending-operation-change')).toHaveCount(0);
  notices = [notice()];
  await page.clock.fastForward(15_000);
  await expect(page.getByTestId('pending-operation-change')).toHaveCount(1);
  await expect(page.getByTestId('pending-operation-change')).toContainText('+5 kg solicitados');
});
