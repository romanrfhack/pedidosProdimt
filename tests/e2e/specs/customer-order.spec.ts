import { expect, test } from '@playwright/test';

test('Mi pedido de hoy muestra acciones principales', async ({ page }) => {
  await page.goto('/cliente');

  await expect(page.getByRole('heading', { name: 'Mi pedido de hoy' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Enviar pedido' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'No pedir hoy' })).toBeVisible();
});

test('la vista del cliente no muestra informacion de maquina', async ({ page }) => {
  await page.goto('/cliente');

  await expect(page.getByTestId('customer-today')).not.toContainText(/maquina|máquina|machine/i);
});
