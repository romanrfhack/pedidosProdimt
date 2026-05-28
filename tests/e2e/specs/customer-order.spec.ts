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

test('admin muestra pendientes y acepta pedido con refresco', async ({ page }) => {
  await loginAdmin(page);
  await page.goto('/admin/pendientes');

  await expect(page.getByRole('heading', { name: 'Pendientes de revision' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Aceptar' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Rechazar' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Ver auditoria' })).toBeVisible();

  await page.getByRole('button', { name: 'Aceptar' }).click();

  await expect(page.getByText('Pedido aceptado.')).toBeVisible();
  await expect(page.getByText('No hay pedidos pendientes de revision.')).toBeVisible();
});

test('admin sin login ve pantalla de login', async ({ page }) => {
  await page.goto('/admin/pedidos');

  await expect(page.getByTestId('admin-login')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Entrar' })).toBeVisible();
});
