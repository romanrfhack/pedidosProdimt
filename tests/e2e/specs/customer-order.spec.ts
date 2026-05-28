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
