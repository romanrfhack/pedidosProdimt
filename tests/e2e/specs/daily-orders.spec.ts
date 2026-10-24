import { expect, test, type Page } from '@playwright/test';

const mockApiBaseUrl = 'http://127.0.0.1:5088';
const orderId = '33333333-3333-3333-3333-333333333301';
const csv = 'externalOrderKey,serviceDate,customerExternalCode,action,productExternalCode,quantity,unitCode,notes\nDEMO-001,2026-10-25,C-DEMO,order,P-DEMO,12,kg,Prueba';

async function login(page: Page) {
  await page.goto('/admin/login');
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
}

function preview(batchKey: string) {
  return {
    batchKey, canApply: true, applied: false, replayed: false, errors: [], warnings: ['Revisa el estado antes de producir.'],
    orders: [{ externalOrderKey: 'DEMO-001', customerId: 'customer-demo', customerName: 'Cliente de prueba', serviceDate: '2026-10-25', action: 'order', disposition: 'create', orderId: null,
      lines: [{ productId: 'product-demo', productName: 'Tortilla de prueba', quantity: 12, unitCode: 'kg', notes: null }] }]
  };
}

async function setupDaily(page: Page) {
  await page.route('**/api/admin/daily-orders/validate', async (route) => {
    await route.fulfill({ json: preview(route.request().postDataJSON().batchKey) });
  });
  await login(page);
  await page.getByRole('link', { name: 'Importar pedidos', exact: true }).click();
  await page.getByLabel('Origen de los pedidos').fill('ruta-demo');
  await page.getByLabel('Contenido CSV de pedidos').fill(csv);
}

test.beforeEach(async ({ request }) => {
  await request.post(`${mockApiBaseUrl}/__test/reset`);
});

test('captura administrativa consulta y envía la fecha de servicio seleccionada', async ({ page }) => {
  await login(page);
  await page.goto('/admin/clientes-pendientes');
  await page.getByLabel('Fecha de servicio').fill('2026-10-25');
  const pending = page.waitForRequest((request) => request.url().includes('/pending-orders?date=2026-10-25'));
  await page.getByRole('button', { name: 'Consultar clientes' }).click();
  await pending;
  await page.getByRole('button', { name: 'Capturar pedido' }).first().click();
  const submit = page.waitForRequest((request) => request.method() === 'POST' && request.url().endsWith('/orders/submit'));
  await page.getByRole('button', { name: 'Guardar pedido' }).click();
  expect((await submit).postDataJSON().serviceDate).toBe('2026-10-25');
  await expect(page.getByText('Pedido capturado por administracion.')).toBeVisible();
  const noOrder = page.waitForRequest((request) => request.method() === 'POST' && request.url().endsWith('/orders/no-order'));
  await page.getByRole('button', { name: 'No pide en esta fecha' }).first().click();
  expect((await noOrder).postDataJSON().serviceDate).toBe('2026-10-25');
});

test('consulta de pedidos retira datos anteriores al cambiar la fecha', async ({ page }) => {
  await login(page);
  await expect(page.getByText('Gran Takito')).toBeVisible();
  await page.getByLabel('Fecha de servicio').fill('2026-10-25');
  await expect(page.getByText('Gran Takito')).toHaveCount(0);
  const query = page.waitForRequest((request) => request.url().endsWith('/orders/today?date=2026-10-25'));
  await page.getByRole('button', { name: 'Consultar pedidos' }).click();
  await query;
  await expect(page.getByText('Gran Takito')).toBeVisible();
});

test('importar pedidos requiere vista previa y la invalida al editar origen o CSV', async ({ page }) => {
  await setupDaily(page);
  const apply = page.getByRole('button', { name: 'Aplicar pedidos', exact: true });
  await expect(apply).toBeDisabled();
  await page.getByRole('button', { name: 'Validar pedidos' }).click();
  await expect(page.getByTestId('daily-preview-order')).toContainText('12 kg');
  await expect(apply).toBeEnabled();
  await page.getByLabel('Origen de los pedidos').fill('otra-ruta');
  await expect(apply).toBeDisabled();
  await expect(page.getByTestId('daily-import-preview')).toHaveCount(0);
  await page.getByRole('button', { name: 'Validar pedidos' }).click();
  await expect(apply).toBeEnabled();
  await page.getByLabel('Contenido CSV de pedidos').fill(csv.replace(',12,', ',13,'));
  await expect(apply).toBeDisabled();
});

test('archivo inválido no conserva contenido anterior listo para validar', async ({ page }) => {
  await setupDaily(page);
  await page.getByRole('button', { name: 'Validar pedidos' }).click();
  await expect(page.getByRole('button', { name: 'Aplicar pedidos' })).toBeEnabled();
  await page.getByLabel('Archivo de pedidos CSV').setInputFiles({ name: 'grande.csv', mimeType: 'text/csv', buffer: Buffer.alloc(2 * 1024 * 1024 + 1, 'x') });
  await expect(page.getByRole('alert')).toContainText('supera el límite');
  await expect(page.getByLabel('Contenido CSV de pedidos')).toHaveValue('');
  await expect(page.getByRole('button', { name: 'Validar pedidos' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Aplicar pedidos' })).toBeDisabled();
});

test('reintento de lote tras respuesta perdida conserva exactamente contenido y clave', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await setupDaily(page);
  const requests: unknown[] = [];
  await page.route('**/api/admin/daily-orders/apply', async (route) => {
    const body = route.request().postDataJSON();
    requests.push(body);
    if (requests.length === 1) { await route.abort('failed'); return; }
    await route.fulfill({ json: { ...preview(body.batchKey), applied: true, replayed: true } });
  });
  await page.getByRole('button', { name: 'Validar pedidos' }).click();
  await page.getByRole('button', { name: 'Aplicar pedidos' }).click();
  await expect(page.getByRole('alert')).toContainText('No se recibió confirmación');
  await expect(page.getByLabel('Origen de los pedidos')).toBeDisabled();
  await expect(page.getByLabel('Contenido CSV de pedidos')).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Preparar otro lote' })).toBeDisabled();
  await page.getByRole('button', { name: 'Reintentar mismo lote' }).click();
  expect(requests).toHaveLength(2);
  expect(requests[1]).toEqual(requests[0]);
  await expect(page.getByRole('status')).toContainText('sin duplicar pedidos');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('daily-import-mobile.png'), fullPage: true });
});

test('conflicto de lote exige validar nuevamente y permite corregir la entrada', async ({ page }) => {
  await setupDaily(page);
  await page.route('**/api/admin/daily-orders/apply', async (route) => {
    await route.fulfill({ status: 409, json: { error: 'La referencia ya se usó con otro contenido.' } });
  });
  await page.getByRole('button', { name: 'Validar pedidos' }).click();
  await page.getByRole('button', { name: 'Aplicar pedidos' }).click();
  await expect(page.getByRole('alert')).toContainText('otro contenido');
  await expect(page.getByLabel('Contenido CSV de pedidos')).toBeEnabled();
  await expect(page.getByRole('button', { name: 'Aplicar pedidos' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Validar pedidos' })).toBeEnabled();
});

test('errores de validación impiden importar pedidos', async ({ page }) => {
  await setupDaily(page);
  await page.route('**/api/admin/daily-orders/validate', async (route) => {
    await route.fulfill({ json: { ...preview(route.request().postDataJSON().batchKey), canApply: false, errors: [{ rowNumber: 2, message: 'La unidad no coincide con el producto.' }] } });
  });
  await page.getByRole('button', { name: 'Validar pedidos' }).click();
  await expect(page.getByRole('alert')).toContainText('Fila 2: La unidad no coincide');
  await expect(page.getByRole('button', { name: 'Aplicar pedidos' })).toBeDisabled();
});

test('corrección reemplaza líneas con motivo y versión; reintento conserva identidad', async ({ page }) => {
  await login(page);
  await page.getByRole('button', { name: 'Ver detalle' }).click();
  await page.getByRole('button', { name: 'Corregir pedido', exact: true }).click();
  const form = page.getByTestId('order-correction');
  await form.getByLabel('Motivo de la corrección').fill('Cliente corrigió los kilos solicitados');
  await form.getByLabel('Cantidad corregida 1').fill('35');
  await form.getByRole('button', { name: 'Agregar producto' }).click();
  await form.getByRole('combobox', { name: 'Producto 2', exact: true }).selectOption('22222222-2222-2222-2222-222222222202');
  await form.getByLabel('Cantidad corregida 2').fill('4');
  const requests: any[] = [];
  await page.route(`**/api/admin/orders/${orderId}/corrections`, async (route) => {
    requests.push(route.request().postDataJSON());
    if (requests.length === 1) { await route.abort('failed'); return; }
    await route.fulfill({ json: {} });
  });
  await form.getByRole('button', { name: 'Guardar corrección' }).click();
  await expect(form.getByRole('alert')).toContainText('No se recibió confirmación');
  await expect(form.getByLabel('Cantidad corregida 1')).toBeDisabled();
  await form.getByRole('button', { name: 'Reintentar misma corrección' }).click();
  expect(requests).toHaveLength(2);
  expect(requests[1]).toEqual(requests[0]);
  expect(requests[0]).toMatchObject({ expectedVersion: 1, reason: 'Cliente corrigió los kilos solicitados', lines: [{ productId: '22222222-2222-2222-2222-222222222201', quantity: 35 }, { productId: '22222222-2222-2222-2222-222222222202', quantity: 4 }] });
  expect(requests[0].operationKey).toBeTruthy();
  await expect(page.getByRole('status')).toContainText('Corrección guardada');
});

test('corrección con versión desactualizada exige recargar, sin sobrescribir', async ({ page }) => {
  await login(page);
  await page.getByRole('button', { name: 'Ver detalle' }).click();
  await page.getByRole('button', { name: 'Corregir pedido', exact: true }).click();
  const form = page.getByTestId('order-correction');
  await form.getByLabel('Motivo de la corrección').fill('Ajuste de prueba');
  await page.route(`**/api/admin/orders/${orderId}/corrections`, async (route) => {
    await route.fulfill({ status: 409, json: { error: 'Versión desactualizada' } });
  });
  await form.getByRole('button', { name: 'Guardar corrección' }).click();
  await expect(form.getByRole('alert')).toContainText('El pedido cambió');
  await expect(form.getByRole('button', { name: 'Reintentar misma corrección' })).toBeDisabled();
  await form.getByRole('button', { name: 'Recargar pedido actualizado' }).click();
  await expect(form).toHaveCount(0);
  await expect(page.getByText('Detalle interno')).toBeVisible();
});

test('cliente no puede abrir importación diaria ni ver su navegación', async ({ page }) => {
  await page.goto('/cliente?token=demo-customer-token');
  await expect(page.getByRole('heading', { name: 'Mi pedido de hoy' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Importar pedidos', exact: true })).toHaveCount(0);
  await page.goto('/admin/importar-pedidos');
  await expect(page.getByTestId('admin-login')).toBeVisible();
});

test('pendientes de otra fecha se pueden consultar y aceptar', async ({ page }) => {
  await login(page);
  await page.goto('/admin/pendientes');
  await page.getByLabel('Fecha de servicio').fill('2026-10-23');
  const query = page.waitForRequest((request) => request.url().endsWith('/orders/pending-review?date=2026-10-23'));
  await page.getByRole('button', { name: 'Consultar pendientes' }).click();
  await query;
  await expect(page.getByText('Gran Takito')).toBeVisible();
  await page.getByRole('button', { name: 'Aceptar', exact: true }).click();
  await expect(page.getByText('Pedido aceptado.')).toBeVisible();
  await expect(page.getByText('No hay pedidos pendientes de revision.')).toBeVisible();
});

test('corrección conserva nombre y unidad históricos cuando cambia el catálogo', async ({ page }) => {
  await page.route('**/api/admin/products', async (route) => {
    const response = await route.fetch();
    const products = await response.json();
    products[0].name = 'Nombre nuevo del catálogo';
    products[0].unitCode = 'package';
    await route.fulfill({ response, json: products });
  });
  await login(page);
  await page.getByRole('button', { name: 'Ver detalle' }).click();
  await page.getByRole('button', { name: 'Corregir pedido', exact: true }).click();
  const form = page.getByTestId('order-correction');
  await expect(form.getByText(/Esta corrección conserva el producto y la unidad del pedido: kg/)).toBeVisible();
  await expect(form.getByText('Producto original del pedido: #9 1/2 · cantidades en kg.')).toBeVisible();
  await expect(form.getByRole('combobox').first().locator('option:checked')).toHaveText('#9 1/2 · kg');
  await form.getByLabel('Motivo de la corrección').fill('Ajuste histórico en kilos');
  await form.getByLabel('Cantidad corregida 1').fill('21');
  await page.route(`**/api/admin/orders/${orderId}/corrections`, async (route) => {
    expect(route.request().postDataJSON().lines[0].quantity).toBe(21);
    await route.fulfill({ json: {} });
  });
  await form.getByRole('button', { name: 'Guardar corrección' }).click();
  await expect(page.getByRole('status')).toContainText('Corrección guardada');
});

test('historial muestra motivo y cantidades anteriores y nuevas sin JSON crudo', async ({ page }) => {
  await page.route(`**/api/admin/orders/${orderId}/audit`, async (route) => {
    await route.fulfill({ json: [{ id: 'audit-correction', eventType: 'OrderCorrected', occurredAt: '2026-10-24T17:00:00Z', actorType: 'Admin', summary: 'Corrección comercial: reemplazo completo de partidas.', metadataJson: JSON.stringify({
      reason: 'Cliente confirmó 35 kilos', before: { lines: [{ productId: 'product-one', productName: 'Tortilla demo', quantity: 20, unitCode: 'kg', notes: null }] },
      after: { lines: [{ productId: 'product-one', productName: 'Tortilla demo', quantity: 35, unitCode: 'kg', notes: 'Entrega por la tarde' }] }
    }) }] });
  });
  await login(page);
  await page.getByRole('button', { name: 'Ver detalle' }).click();
  await page.getByRole('button', { name: 'Ver historial' }).click();
  const history = page.getByRole('region', { name: 'Historial del pedido' });
  await expect(history).toContainText('Motivo: Cliente confirmó 35 kilos');
  await expect(history).toContainText('Antes: 20 kg → Después: 35 kg');
  await expect(history).toContainText('Nota nueva: Entrega por la tarde');
});

test('instancia sin SQL Server muestra rechazo explícito y permite corregir o cancelar', async ({ page }) => {
  await setupDaily(page);
  await page.route('**/api/admin/daily-orders/apply', async (route) => {
    await route.fulfill({ status: 503, json: { code: 'PersistenceNotSupported', detail: 'SQL Server requerido para guardar importaciones.' } });
  });
  await page.getByRole('button', { name: 'Validar pedidos' }).click();
  await page.getByRole('button', { name: 'Aplicar pedidos' }).click();
  await expect(page.getByRole('alert')).toContainText('SQL Server requerido');
  await expect(page.getByLabel('Contenido CSV de pedidos')).toBeEnabled();
  await expect(page.getByRole('button', { name: 'Preparar otro lote' })).toBeEnabled();
});
