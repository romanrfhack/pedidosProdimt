import { expect, test, type Page } from '@playwright/test';

const mockApiBaseUrl = 'http://127.0.0.1:5088';
const demoCustomerToken = 'demo-customer-token';

async function loginCustomer(page: Page) {
  await page.goto(`/cliente?token=${demoCustomerToken}`);
  await expect(page.getByRole('heading', { name: 'Mi pedido de hoy' })).toBeVisible();
}

async function loginAdmin(page: Page) {
  await page.goto('/admin/login');
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByRole('heading', { name: 'Pedidos de hoy' })).toBeVisible();
}

async function mockImportEndpoints(page: Page) {
  const corsHeaders = {
    'access-control-allow-origin': '*',
    'access-control-allow-headers': 'authorization,content-type',
    'access-control-allow-methods': 'GET,POST,PUT,PATCH,OPTIONS'
  };
  await page.route(/\/api\/admin\/import\/products\/validate$/, async (route) => {
    if (route.request().method() === 'OPTIONS') {
      await route.fulfill({ status: 204, headers: corsHeaders });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      headers: corsHeaders,
      body: JSON.stringify({
        importType: 'products',
        totalRows: 1,
        validRows: 1,
        errorCount: 0,
        warningCount: 0,
        proposedCreateCount: 1,
        proposedUpdateCount: 0,
        proposedDeactivateCount: 0,
        errors: [],
        warnings: [],
        proposedChanges: [
          {
            rowNumber: 2,
            action: 'Create',
            entityType: 'Product',
            entityId: null,
            entityDisplayName: 'Molde E2E',
            summary: 'Crear producto.'
          }
        ]
      })
    });
  });
  await page.route(/\/api\/admin\/import\/products\/apply$/, async (route) => {
    if (route.request().method() === 'OPTIONS') {
      await route.fulfill({ status: 204, headers: corsHeaders });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      headers: corsHeaders,
      body: JSON.stringify({
        importType: 'products',
        totalRows: 1,
        createdCount: 1,
        updatedCount: 0,
        skippedCount: 0,
        warningCount: 0,
        auditLogIds: ['aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'],
        errors: []
      })
    });
  });
  await page.route(/\/api\/admin\/import\/customers\/validate$/, async (route) => {
    if (route.request().method() === 'OPTIONS') {
      await route.fulfill({ status: 204, headers: corsHeaders });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      headers: corsHeaders,
      body: JSON.stringify({
        importType: 'customers',
        totalRows: 1,
        validRows: 0,
        errorCount: 1,
        warningCount: 0,
        proposedCreateCount: 0,
        proposedUpdateCount: 0,
        proposedDeactivateCount: 0,
        errors: [
          {
            rowNumber: 2,
            field: 'name',
            code: 'Required',
            message: 'Campo requerido vacio: name.',
            rawValue: ''
          }
        ],
        warnings: [],
        proposedChanges: []
      })
    });
  });
}

test.beforeEach(async ({ request }) => {
  await request.post(`${mockApiBaseUrl}/__test/reset`);
});

test('Mi pedido de hoy muestra productos y acciones principales', async ({ page }) => {
  await loginCustomer(page);

  await expect(page.getByRole('heading', { name: 'Mi pedido de hoy' })).toBeVisible();
  await expect(page.getByText('#9 1/2')).toBeVisible();
  await expect(page.getByText('#10 1/2')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Enviar pedido' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'No pedir hoy' })).toBeVisible();
});

test('la vista del cliente no muestra informacion de maquina', async ({ page }) => {
  await loginCustomer(page);

  await expect(page.getByText('#9 1/2')).toBeVisible();
  await expect(page.getByTestId('customer-today')).not.toContainText(/maquina|máquina|machine/i);
  await expect(page.getByTestId('customer-today')).not.toContainText(/auditoria|audit/i);
});

test('no permite enviar si todas las cantidades estan en cero', async ({ page, request }) => {
  await loginCustomer(page);
  await page.getByLabel('Cantidad #9 1/2').fill('0');
  await page.getByLabel('Cantidad #10 1/2').fill('0');
  await page.getByRole('button', { name: 'Enviar pedido' }).click();

  await expect(page.getByText('Captura al menos una cantidad o usa No pedir hoy.')).toBeVisible();

  const stateResponse = await request.get(`${mockApiBaseUrl}/__test/state`);
  const state = await stateResponse.json();
  expect(state.submitCalls).toBe(0);
});

test('No pedir hoy llama API y muestra confirmacion', async ({ page }) => {
  await loginCustomer(page);
  await page.getByRole('button', { name: 'No pedir hoy' }).click();

  await expect(page.getByText('No pedir hoy registrado.')).toBeVisible();
  await expect(page.getByText('No pedir hoy registrado').first()).toBeVisible();
});

test('admin muestra Pedidos de hoy desde API', async ({ page }) => {
  await loginAdmin(page);

  await expect(page.getByRole('heading', { name: 'Pedidos de hoy' })).toBeVisible();
  await expect(page.getByText('Gran Takito')).toBeVisible();
  await expect(page.getByTestId('admin-today').getByText('Revision')).toBeVisible();
});

test('admin abre detalle de pedido y ve lineas', async ({ page }) => {
  await loginAdmin(page);

  await page.getByRole('button', { name: 'Ver detalle' }).click();

  await expect(page.getByText('Detalle interno')).toBeVisible();
  await expect(page.getByText('#9 1/2')).toBeVisible();
  await expect(page.getByText('Cantidad: 20')).toBeVisible();
  await expect(page.getByText(/Maquina: #1/)).toBeVisible();
});

test('admin muestra pendientes y acepta pedido con refresco', async ({ page }) => {
  await loginAdmin(page);
  await page.goto('/admin/pendientes');

  await expect(page.getByRole('heading', { name: 'Pendientes de revision' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Aceptar' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Rechazar' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Cambios' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Ver auditoria' })).toBeVisible();

  await page.getByRole('button', { name: 'Aceptar' }).click();

  await expect(page.getByText('Pedido aceptado.')).toBeVisible();
  await expect(page.getByText('No hay pedidos pendientes de revision.')).toBeVisible();
});

test('admin puede aceptar pendiente con cambios', async ({ page }) => {
  await loginAdmin(page);
  await page.goto('/admin/pendientes');

  await page.getByRole('button', { name: 'Cambios' }).click();
  await expect(page.getByText('Aceptar con cambios').first()).toBeVisible();
  await page.getByLabel('Hora entrega').fill('13:00');
  await page.getByLabel('Notas de entrega').fill('Entregar despues de las 13');
  await page.getByLabel('Cantidad actualizable').fill('25');
  await page.getByRole('button', { name: 'Aceptar con cambios' }).click();

  await expect(page.getByText('Pedido aceptado con cambios.')).toBeVisible();
  await expect(page.getByText('No hay pedidos pendientes de revision.')).toBeVisible();
});

test('admin ve clientes pendientes y puede marcar No pedir hoy', async ({ page }) => {
  await loginAdmin(page);
  await page.goto('/admin/clientes-pendientes');

  await expect(page.getByRole('heading', { name: 'Clientes pendientes' })).toBeVisible();
  await expect(page.getByText('Cliente Demo 2')).toBeVisible();
  await expect(page.getByRole('button', { name: 'No pedir hoy' }).first()).toBeVisible();

  await page.getByRole('button', { name: 'No pedir hoy' }).first().click();

  await expect(page.getByText('No pedir hoy registrado por administracion.')).toBeVisible();
  await expect(page.getByText('Cliente Demo 2')).not.toBeVisible();
});

test('admin puede abrir captura administrativa', async ({ page, request }) => {
  await loginAdmin(page);
  await page.goto('/admin/clientes-pendientes');

  await page.getByRole('button', { name: 'Capturar pedido' }).first().click();
  await expect(page.getByText('Captura para Cliente Demo 2')).toBeVisible();
  await expect(page.getByText('#11')).toBeVisible();
  await page.getByRole('button', { name: 'Guardar pedido' }).click();

  await expect(page.getByText('Pedido capturado por administracion.')).toBeVisible();
  const stateResponse = await request.get(`${mockApiBaseUrl}/__test/state`);
  const state = await stateResponse.json();
  expect(state.adminSubmitCalls).toBe(1);
});

test('admin ve navegacion de catalogos y abre secciones', async ({ page }) => {
  await loginAdmin(page);

  await expect(page.getByRole('link', { name: 'Catalogos' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Importacion' })).toBeVisible();
  await page.getByRole('link', { name: 'Catalogos' }).click();

  await expect(page.getByTestId('admin-catalogs')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Catalogos' })).toBeVisible();
  await expect(page.getByTestId('catalog-customers')).toBeVisible();
  await expect(page.getByText('Gran Takito')).toBeVisible();

  await page.getByRole('button', { name: 'Productos' }).click();
  await expect(page.getByTestId('catalog-products')).toBeVisible();
  await expect(page.getByText('#9 1/2')).toBeVisible();

  await page.getByRole('button', { name: 'Maquinas' }).click();
  await expect(page.getByTestId('catalog-machines')).toBeVisible();
  await expect(page.getByText('#1')).toBeVisible();
});

test('admin puede validar y aplicar CSV demo de importacion', async ({ page }) => {
  await mockImportEndpoints(page);
  await loginAdmin(page);
  await page.getByRole('link', { name: 'Importacion' }).click();

  await expect(page.getByTestId('admin-import')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Importacion' })).toBeVisible();
  await expect(page.getByTestId('import-private-data-warning')).toContainText('no subas CSV reales');

  await page.getByLabel('Tipo').selectOption('products');
  await expect(page.getByLabel('Tipo')).toHaveValue('products');

  await page.getByLabel('Contenido CSV').fill('externalCode,name,description,isActive\nP-E2E,Molde E2E,Producto demo,true');
  await page.getByRole('button', { name: 'Validar' }).click();

  await expect(page.getByTestId('import-validation')).toBeVisible();
  await expect(page.getByText('CSV validado sin errores bloqueantes.')).toBeVisible();
  await expect(page.getByText('Crear: 1')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Aplicar importacion' })).toBeEnabled();

  await page.getByRole('button', { name: 'Aplicar importacion' }).click();
  await expect(page.getByTestId('import-apply-result')).toBeVisible();
  await expect(page.getByText('Importacion aplicada.')).toBeVisible();
});

test('admin ve errores de importacion y no puede aplicar', async ({ page }) => {
  await mockImportEndpoints(page);
  await loginAdmin(page);
  await page.goto('/admin/importacion');

  await page.getByLabel('Contenido CSV').fill('externalCode,name,phoneNumber,isActive,preferredDeliveryTime,preferredDeliveryWindowStart,preferredDeliveryWindowEnd,deliveryNotes\nC-ERR,,555,true,,,,');
  await page.getByRole('button', { name: 'Validar' }).click();

  await expect(page.getByTestId('import-errors')).toBeVisible();
  await expect(page.getByText(/Campo requerido vacio: name/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Aplicar importacion' })).toBeDisabled();
});

test('admin abre configuracion de cliente y gestiona token', async ({ page }) => {
  await loginAdmin(page);
  await page.goto('/admin/catalogos');

  await page.getByRole('button', { name: 'Configurar' }).first().click();
  await expect(page.getByTestId('customer-config')).toBeVisible();
  await expect(page.getByText('Productos frecuentes')).toBeVisible();
  await expect(page.getByText('Maquinas asignadas')).toBeVisible();
  await expect(page.getByText('Tokens de acceso')).toBeVisible();

  await page.getByLabel('Descripcion').fill('Token e2e');
  await page.getByRole('button', { name: 'Crear token' }).click();
  await expect(page.getByText(/Token generado:/)).toBeVisible();
  await expect(page.getByText('mock-generated-token')).toBeVisible();
});

test('cliente no ve navegacion ni pantallas de catalogos', async ({ page }) => {
  await loginCustomer(page);

  await expect(page.getByRole('link', { name: 'Catalogos' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Importacion' })).toHaveCount(0);
  await page.goto('/admin/catalogos');
  await expect(page.getByTestId('admin-login')).toBeVisible();

  await page.goto('/admin/importacion');
  await expect(page.getByTestId('admin-login')).toBeVisible();
});

test('admin sin login ve pantalla de login', async ({ page }) => {
  await page.goto('/admin/pedidos');

  await expect(page.getByTestId('admin-login')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Entrar' })).toBeVisible();
});

const operationsFixture = {
  date: '2026-10-24', routeCode: 'PILOTO-DEMO', demandLabel: 'Por producir', preparationLabel: 'Por preparar',
  orders: [{
    orderId: '33333333-3333-3333-3333-333333333310', customerId: '11111111-1111-1111-1111-111111111110',
    customerName: 'Cliente Piloto Demo', sequenceNumber: 1, status: 'Accepted', routeCode: 'PILOTO-DEMO', routeStopOrder: 1,
    hasStoreKey: true, paymentTermCode: 'cash_on_delivery', internalDeliveryInstructions: 'Dejar en la mesa derecha.',
    requestedDeliveryTime: '10:30:00', requestedDeliveryWindowStart: null, requestedDeliveryWindowEnd: null,
    deliveryNotes: null, orderingProfileVersion: 3,
    lines: [
      { orderLineId: 'line-a', productId: 'product-a', productName: 'Tortilla demo #11', unitCode: 'kg', quantity: 15,
        assignedMachineId: 'machine-a', assignedMachineNumber: 1, assignedMachineName: null, notes: null },
      { orderLineId: 'line-b', productId: 'product-b', productName: 'Huarache demo', unitCode: 'dozen', quantity: 2,
        assignedMachineId: 'machine-b', assignedMachineNumber: 2, assignedMachineName: null, notes: null }
    ]
  }],
  machineDemand: [
    { machineId: 'machine-a', machineNumber: 1, machineName: null, productId: 'product-a', productName: 'Tortilla demo #11', unitCode: 'kg', quantity: 15 },
    { machineId: 'machine-b', machineNumber: 2, machineName: null, productId: 'product-b', productName: 'Huarache demo', unitCode: 'dozen', quantity: 2 }
  ],
  pendingReview: [{ orderId: 'pending-demo', customerId: 'customer-pending', customerName: 'Cliente Pendiente Demo', sequenceNumber: 2, reviewReason: 'AdditionalOrderSameDay' }],
  warnings: []
};

async function mockOperationsPlan(page: Page) {
  await page.route(/\/api\/admin\/operations\/plan\?/, async (route) => {
    const requestUrl = new URL(route.request().url());
    await route.fulfill({ json: {
      ...operationsFixture,
      date: requestUrl.searchParams.get('date'),
      routeCode: requestUrl.searchParams.get('routeCode')
    } });
  });
}

test('catálogo conserva desconocido, llave y cobro como campos independientes', async ({ page }) => {
  await loginAdmin(page);
  await page.goto('/admin/catalogos');
  const form = page.getByTestId('catalog-customers').locator('form');
  await expect(form.getByLabel('Llave del local')).toHaveValue('0: null');
  await expect(form.getByLabel('Forma de cobro')).toHaveValue('0: null');
  await form.getByLabel('Nombre', { exact: true }).fill('Cliente Piloto Prueba');
  await form.getByLabel('Código de ruta').fill('PILOTO-DEMO');
  await form.getByLabel('Orden de visita').fill('3');
  await form.getByLabel('Llave del local').selectOption({ label: 'Tenemos llave' });
  await form.getByLabel('Forma de cobro').selectOption('cash_on_delivery');
  await form.getByLabel('Instrucciones internas de entrega').fill('Dejar en la barra de prueba.');
  const createdRequest = page.waitForRequest((request) => request.method() === 'POST' && request.url().endsWith('/api/admin/customers'));
  await form.getByRole('button', { name: 'Crear cliente' }).click();
  expect((await createdRequest).postDataJSON()).toMatchObject({
    routeCode: 'PILOTO-DEMO', routeStopOrder: 3, hasStoreKey: true,
    paymentTermCode: 'cash_on_delivery', internalDeliveryInstructions: 'Dejar en la barra de prueba.'
  });
  await expect(page.getByText('Cliente guardado.')).toBeVisible();
  const customer = page.locator('article').filter({ has: page.getByText('Cliente Piloto Prueba', { exact: true }) });
  await customer.getByRole('button', { name: 'Editar', exact: true }).click();
  await expect(form.getByLabel('Llave del local')).toHaveValue('1: true');
  await expect(form.getByLabel('Forma de cobro')).toHaveValue('cash_on_delivery');
  await form.getByLabel('Llave del local').selectOption({ label: 'Por confirmar' });
  const updatedRequest = page.waitForRequest((request) => request.method() === 'PUT' && /\/api\/admin\/customers\/[^/]+$/.test(request.url()));
  await form.getByRole('button', { name: 'Guardar cliente' }).click();
  expect((await updatedRequest).postDataJSON()).toMatchObject({ hasStoreKey: null, paymentTermCode: 'cash_on_delivery' });
});

test('producto nuevo no presupone kilos y permite guardar docenas', async ({ page }) => {
  await loginAdmin(page);
  await page.goto('/admin/catalogos');
  await page.getByRole('button', { name: 'Productos', exact: true }).click();
  const form = page.getByTestId('catalog-products').locator('form');
  await expect(form.getByLabel('Unidad de venta')).toHaveValue('0: null');
  await form.getByLabel('Nombre', { exact: true }).fill('Huarache catálogo demo');
  await form.getByLabel('Unidad de venta').selectOption('dozen');
  const request = page.waitForRequest((request) => request.method() === 'POST' && request.url().endsWith('/api/admin/products'));
  await form.getByRole('button', { name: 'Crear producto' }).click();
  expect((await request).postDataJSON().unitCode).toBe('dozen');
  await expect(page.getByText('Unidad: docenas')).toBeVisible();
});

test('perfil requiere aprobación explícita y separa posición de orden visual', async ({ page }) => {
  await loginAdmin(page);
  await page.goto('/admin/catalogos');
  await page.getByRole('button', { name: 'Configurar' }).first().click();
  const config = page.getByTestId('customer-config');
  const approval = config.getByLabel('He verificado los productos y sus posiciones; aprobar este perfil');
  await expect(approval).not.toBeChecked();
  await config.getByLabel('Posición en mensaje', { exact: true }).nth(0).fill('2');
  await config.getByLabel('Posición en mensaje', { exact: true }).nth(1).fill('1');
  await config.getByLabel('Máquina para este producto').nth(0).selectOption('44444444-4444-4444-4444-444444444402');
  await approval.check();
  const saveRequest = page.waitForRequest((request) => request.method() === 'PUT' && request.url().endsWith('/frequent-products'));
  await config.getByRole('button', { name: 'Guardar frecuentes' }).click();
  const body = (await saveRequest).postDataJSON();
  expect(body.approveForInterpretation).toBe(true);
  expect(body.items[0]).toMatchObject({ sortOrder: 1, interpretationPosition: 2, preferredMachineId: '44444444-4444-4444-4444-444444444402' });
  await expect(config.getByText(/Perfil versión 1 · Aprobado/)).toBeVisible();
  await expect(approval).not.toBeChecked();
  await approval.check();
  await config.getByLabel('Cantidad sugerida').first().fill('21');
  await expect(approval).not.toBeChecked();
  const changedSave = page.waitForRequest((request) => request.method() === 'PUT' && request.url().endsWith('/frequent-products'));
  await config.getByRole('button', { name: 'Guardar frecuentes' }).click();
  const changedBody = (await changedSave).postDataJSON();
  expect(changedBody.approveForInterpretation).toBe(false);
  expect(changedBody.items[0].defaultQuantity).toBe(21);
  await expect(config.getByText(/Perfil versión 2 · Pendiente de aprobación/)).toBeVisible();
});

test('plan móvil e impresión separan unidades y muestran preparación e instrucciones', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockOperationsPlan(page);
  await loginAdmin(page);
  await page.getByRole('link', { name: 'Operación' }).click();
  const plan = page.getByTestId('operations-plan');
  await expect(plan.getByRole('heading', { name: 'Por producir', exact: true })).toBeVisible();
  await expect(page.getByTestId('machine-demand').locator('tbody tr')).toHaveCount(2);
  await expect(page.getByTestId('machine-demand').locator('tbody tr').nth(0)).toContainText('kg');
  await expect(page.getByTestId('machine-demand').locator('tbody tr').nth(1)).toContainText('docenas');
  await expect(page.getByTestId('preparation-order')).toContainText('Con llave del local');
  await expect(page.getByTestId('preparation-order')).toContainText('Cobro diario contraentrega · requiere ticket');
  await expect(page.getByText('Dejar en la mesa derecha.', { exact: false })).toBeVisible();
  await expect(page.getByTestId('plan-pending-review')).toContainText('Cliente Pendiente Demo');
  await expect(page.getByTestId('machine-demand')).not.toContainText('Cliente Pendiente Demo');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('operations-mobile.png'), fullPage: true });
  await page.emulateMedia({ media: 'print' });
  await expect(page.getByRole('button', { name: 'Imprimir plan' })).not.toBeVisible();
  await expect(page.locator('.app-shell__header')).not.toBeVisible();
  await expect(page.getByText('Plan de preparación. No es comprobante de entrega ni de pago.')).toBeVisible();
  await expect(page.getByTestId('preparation-order')).toBeVisible();
  await page.pdf({ path: test.info().outputPath('operations-print.pdf'), format: 'A4', printBackground: true });
});

test('cambiar filtro retira el plan anterior y un error no permite imprimir datos viejos', async ({ page }) => {
  await mockOperationsPlan(page);
  await loginAdmin(page);
  await page.goto('/admin/operacion');
  await expect(page.getByTestId('preparation-order')).toBeVisible();
  await page.getByLabel('Código de ruta').fill('OTRA-RUTA');
  await expect(page.getByTestId('preparation-order')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Imprimir plan' })).toHaveCount(0);
  await page.route(/\/api\/admin\/operations\/plan\?/, async (route) => {
    expect(new URL(route.request().url()).searchParams.get('routeCode')).toBe('OTRA-RUTA');
    await route.fulfill({ status: 503, json: { error: 'Plan temporalmente no disponible.' } });
  });
  await page.getByRole('button', { name: 'Consultar plan' }).click();
  await expect(page.getByRole('alert')).toContainText('Plan temporalmente no disponible.');
  await expect(page.getByRole('button', { name: 'Imprimir plan' })).toHaveCount(0);
});

test('captura cliente bloquea producto con unidad desconocida sin inventar kilos', async ({ page, request }) => {
  await page.route(/\/api\/customer-orders\/[^/]+\/today$/, async (route) => {
    const response = await route.fetch();
    const body = await response.json();
    body.products[0].unitCode = null;
    await route.fulfill({ response, json: body });
  });
  await loginCustomer(page);
  await expect(page.getByText(/unidad por confirmar/)).toBeVisible();
  await page.getByRole('button', { name: 'Enviar pedido' }).click();
  await expect(page.getByText(/La unidad de un producto está pendiente de confirmar/)).toBeVisible();
  const state = await (await request.get(`${mockApiBaseUrl}/__test/state`)).json();
  expect(state.submitCalls).toBe(0);
});

test('cliente no puede abrir el plan de operación', async ({ page }) => {
  await loginCustomer(page);
  await expect(page.getByRole('link', { name: 'Operación' })).toHaveCount(0);
  await page.goto('/admin/operacion');
  await expect(page.getByTestId('admin-login')).toBeVisible();
});
