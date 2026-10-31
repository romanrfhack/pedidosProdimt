import { expect, test, type Page } from '@playwright/test';

const apiBase = 'http://127.0.0.1:5088';
const customerId = '11111111-1111-1111-1111-111111111111';
const productIds = [
  '22222222-2222-2222-2222-222222222201',
  '22222222-2222-2222-2222-222222222202',
  '22222222-2222-2222-2222-222222222203'
];
const names = ['#9 1/2', '#10 1/2', '#11'];
const profileUrl = `**/api/admin/customers/${customerId}/ordering-profile`;
const saveUrl = `**/api/admin/customers/${customerId}/frequent-products`;

type ProfileItem = {
  productId: string; productName: string; unitCode: string | null; productIsActive: boolean;
  defaultQuantity: number | null; sortOrder: number; isActive: boolean;
  preferredMachineId: string | null; interpretationPosition: number | null;
};

function items(): ProfileItem[] {
  return productIds.slice(0, 2).map((productId, index) => ({
    productId, productName: names[index], unitCode: 'kg', productIsActive: true,
    defaultQuantity: index ? 10 : 20, sortOrder: index + 1, isActive: true,
    preferredMachineId: null, interpretationPosition: index + 1
  }));
}

async function mockProfile(page: Page, initialItems = items()) {
  const state = {
    customerId, orderingProfileVersion: 7, isOrderProfileApproved: false,
    items: initialItems
  };
  const writes: Array<{ expectedProfileVersion: number; approveForInterpretation: boolean; items: ProfileItem[] }> = [];
  await page.route(profileUrl, async route => { await route.fulfill({ json: state }); });
  await page.route(saveUrl, async route => {
    const body = route.request().postDataJSON();
    writes.push(body);
    if (body.expectedProfileVersion !== state.orderingProfileVersion) {
      await route.fulfill({ status: 409, json: { error: 'El perfil fue modificado por otro usuario.' } });
      return;
    }
    state.items = body.items.map((item: ProfileItem) => ({
      ...item,
      productName: names[productIds.indexOf(item.productId)],
      unitCode: state.items.some(existing => existing.productId === item.productId)
        ? state.items.find(existing => existing.productId === item.productId)!.unitCode : 'kg',
      productIsActive: true
    }));
    state.orderingProfileVersion++;
    state.isOrderProfileApproved = body.approveForInterpretation;
    await route.fulfill({ json: state.items });
  });
  return { state, writes };
}

async function openConfiguration(page: Page) {
  await page.goto('/admin/login');
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
  await page.goto(`/admin/clientes/${customerId}/configuracion`);
  const config = page.getByTestId('customer-config');
  await expect(config.getByText('Cargando configuración...')).toHaveCount(0);
  await expect(config.getByTestId('profile-product')).toHaveCount(2);
  await expect(config.getByLabel('Cantidad sugerida').first()).toBeEnabled();
  await expect(config.getByLabel('Cantidad sugerida').first()).toHaveValue('20');
  return config;
}

test.beforeEach(async ({ request }) => {
  await request.post(`${apiBase}/__test/reset`);
});

test('configuración enfocada separa reorden visual de posiciones hasta renumerar explícitamente', async ({ page }) => {
  const { writes } = await mockProfile(page);
  const config = await openConfiguration(page);
  const rows = config.getByTestId('profile-product');
  await expect(config).toContainText('Perfil versión 7');
  await rows.nth(1).getByRole('button', { name: 'Subir', exact: true }).click();
  await expect(rows.nth(0).getByRole('combobox', { name: 'Producto', exact: true })).toHaveValue(productIds[1]);
  await expect(rows.nth(0).getByLabel('Orden visual')).toHaveValue('1');
  await expect(rows.nth(0).getByLabel('Posición en mensaje')).toHaveValue('2');
  await expect(rows.nth(1).getByLabel('Posición en mensaje')).toHaveValue('1');
  await expect(config.getByTestId('position-preview')).toHaveText('Secuencia del cliente: 1 = #9 1/2 · 2 = #10 1/2');

  await config.getByRole('button', { name: 'Usar orden visual como posiciones 1, 2, 3...' }).click();
  await expect(rows.nth(0).getByLabel('Posición en mensaje')).toHaveValue('1');
  await expect(rows.nth(1).getByLabel('Posición en mensaje')).toHaveValue('2');
  await expect(config.getByTestId('position-preview')).toHaveText('Secuencia del cliente: 1 = #10 1/2 · 2 = #9 1/2');
  await config.getByRole('button', { name: 'Guardar frecuentes', exact: true }).click();
  await expect(config).toContainText('Perfil versión 8');
  expect(writes).toHaveLength(1);
  expect(writes[0]).toMatchObject({ expectedProfileVersion: 7, approveForInterpretation: false, items: [
    { productId: productIds[1], sortOrder: 1, interpretationPosition: 1 },
    { productId: productIds[0], sortOrder: 2, interpretationPosition: 2 }
  ] });
});

test('agregar y quitar productos guarda el conjunto del cliente con la versión leída', async ({ page }) => {
  const { writes } = await mockProfile(page);
  const config = await openConfiguration(page);
  const rows = config.getByTestId('profile-product');
  await config.getByRole('button', { name: 'Agregar producto', exact: true }).click();
  await expect(rows).toHaveCount(3);
  await expect(rows.nth(2).getByRole('combobox', { name: 'Producto', exact: true })).toHaveValue(productIds[2]);
  await rows.nth(2).getByLabel('Cantidad sugerida').fill('3');
  await rows.nth(1).getByRole('button', { name: 'Quitar', exact: true }).click();
  await expect(rows).toHaveCount(2);
  await expect(rows.nth(1).getByLabel('Orden visual')).toHaveValue('2');
  await expect(rows.nth(1).getByLabel('Posición en mensaje')).toHaveValue('');
  await config.getByRole('button', { name: 'Guardar frecuentes', exact: true }).click();
  await expect(config).toContainText('Perfil versión 8');
  expect(writes).toHaveLength(1);
  expect(writes[0]).toMatchObject({ expectedProfileVersion: 7, approveForInterpretation: false, items: [
    { productId: productIds[0], defaultQuantity: 20, interpretationPosition: 1 },
    { productId: productIds[2], defaultQuantity: 3, sortOrder: 2, interpretationPosition: null }
  ] });
  expect(writes[0].items).toHaveLength(2);
});

test('conflicto del perfil preserva el borrador y exige recargar la versión vigente', async ({ page }) => {
  const { state, writes } = await mockProfile(page);
  const config = await openConfiguration(page);
  await config.getByLabel('Cantidad sugerida').first().fill('31');
  // Simulate another operator saving after this screen loaded version 7.
  state.orderingProfileVersion = 8;
  state.items = state.items.map((item, index) => ({ ...item, defaultQuantity: index ? 10 : 5 }));
  await config.getByRole('button', { name: 'Guardar frecuentes', exact: true }).click();
  await expect(config.getByRole('alert')).toContainText('Tus cambios siguen en pantalla');
  await expect(config.getByLabel('Cantidad sugerida').first()).toHaveValue('31');
  await expect(config.getByRole('button', { name: 'Guardar frecuentes', exact: true })).toBeDisabled();
  expect(writes).toHaveLength(1);
  expect(writes[0].expectedProfileVersion).toBe(7);

  await config.getByRole('button', { name: 'Recargar perfil y descartar mis cambios' }).click();
  await expect(config).toContainText('Perfil versión 8');
  await expect(config.getByLabel('Cantidad sugerida').first()).toHaveValue('5');
  await expect(config.getByRole('button', { name: 'Guardar frecuentes', exact: true })).toBeEnabled();
  await expect(config.getByRole('alert')).toHaveCount(0);
});

test('unidad sin confirmar impide aprobar pero permite guardar el perfil pendiente', async ({ page }) => {
  const initialItems = items();
  initialItems[0].unitCode = null;
  const { writes } = await mockProfile(page, initialItems);
  const config = await openConfiguration(page);
  const approval = config.getByLabel('He verificado los productos y sus posiciones; aprobar este perfil');
  await expect(config.getByTestId('profile-product').first()).toContainText('Falta unidad');
  await approval.check();
  await expect(config.getByTestId('profile-validation')).toContainText('unidad confirmada');
  await expect(config.getByRole('button', { name: 'Guardar frecuentes', exact: true })).toBeDisabled();
  await approval.uncheck();
  await expect(config.getByRole('button', { name: 'Guardar frecuentes', exact: true })).toBeEnabled();
  await config.getByRole('button', { name: 'Guardar frecuentes', exact: true }).click();
  await expect(config).toContainText('Perfil versión 8');
  expect(writes).toHaveLength(1);
  expect(writes[0].approveForInterpretation).toBe(false);
});

test('unidad y nombre del perfil corresponden a su versión aunque el catálogo leído antes cambie', async ({ page }) => {
  const initialItems = items();
  initialItems[0].productName = 'Producto de prueba por pieza';
  initialItems[0].unitCode = 'piece';
  const { writes } = await mockProfile(page, initialItems);
  const config = await openConfiguration(page);
  const first = config.getByTestId('profile-product').first();
  await expect(first.locator('.profile-unit')).toContainText('Unidad de venta: piezas');
  await expect(first.locator('.profile-product-heading')).toContainText('Producto de prueba por pieza');
  await expect(config.getByTestId('position-preview')).toContainText('1 = Producto de prueba por pieza');
  await expect(first.getByRole('combobox', { name: 'Producto', exact: true }).locator('option:checked')).toContainText('Producto de prueba por pieza');
  await config.getByLabel('He verificado los productos y sus posiciones; aprobar este perfil').check();
  await config.getByRole('button', { name: 'Guardar frecuentes', exact: true }).click();
  await expect(config).toContainText('Perfil versión 8');
  expect(writes).toHaveLength(1);
  expect(writes[0]).toMatchObject({
    expectedProfileVersion: 7,
    approveForInterpretation: true,
    items: [
      { productId: productIds[0], expectedUnitCode: 'piece' },
      { productId: productIds[1], expectedUnitCode: 'kg' }
    ]
  });
});

test('configuración por cliente permite revisar y reordenar en móvil sin desbordamiento', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockProfile(page);
  const config = await openConfiguration(page);
  await config.getByTestId('profile-product').nth(1).getByRole('button', { name: 'Subir', exact: true }).click();
  await expect(config.getByTestId('profile-product').first().getByRole('combobox', { name: 'Producto', exact: true })).toHaveValue(productIds[1]);
  await expect(config.getByText('Cambios sin guardar')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('customer-configuration-mobile.png'), fullPage: true });
});
