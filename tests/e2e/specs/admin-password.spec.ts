import { expect, test } from '@playwright/test';

test.beforeEach(async ({ request }) => { await request.post('http://127.0.0.1:5088/__test/reset'); });

test('cambiar contraseña valida confirmación, envía sólo credenciales propias y cierra sesión', async ({ page }) => {
  await page.goto('/admin/login');
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
  await page.getByRole('link', { name: 'Cambiar contraseña' }).click();
  const screen = page.getByTestId('admin-password');
  await screen.getByLabel('Contraseña actual', { exact: true }).fill('Fictional-current-password');
  await screen.getByLabel('Nueva contraseña', { exact: true }).fill('Fictional-new-password-2026!');
  await screen.getByLabel('Confirmar nueva contraseña').fill('Does not match');
  let writes = 0;
  await page.route('**/api/admin/account/change-password', async route => {
    writes++;
    expect(route.request().postDataJSON()).toEqual({ currentPassword: 'Fictional-current-password',
      newPassword: 'Fictional-new-password-2026!', confirmPassword: 'Fictional-new-password-2026!' });
    expect(route.request().headers()['authorization']).toContain('Bearer ');
    await route.fulfill({ status: 204, body: '' });
  });
  await screen.getByRole('button', { name: 'Guardar contraseña' }).click();
  await expect(screen.getByRole('alert')).toContainText('no coinciden');
  expect(writes).toBe(0);
  await screen.getByLabel('Confirmar nueva contraseña').fill('Fictional-new-password-2026!');
  await screen.getByRole('button', { name: 'Guardar contraseña' }).click();
  await expect(page).toHaveURL(/admin\/login\?passwordChanged=1/);
  await expect(page.getByRole('status')).toContainText('Contraseña actualizada');
  expect(await page.evaluate(() => localStorage.getItem('prodimt.auth.session.v1'))).toBeNull();
  await expect(page.getByRole('link', { name: 'Cambiar contraseña' })).toHaveCount(0);
  expect(writes).toBe(1);
});

test('contraseña actual incorrecta conserva formulario y permite corregir', async ({ page }) => {
  await page.goto('/admin/login');
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
  await page.goto('/admin/cambiar-contrasena');
  await page.route('**/api/admin/account/change-password', route => route.fulfill({ status: 400,
    json: { error: 'La contraseña actual es incorrecta.' } }));
  await page.getByLabel('Contraseña actual', { exact: true }).fill('Fictional-wrong-password');
  await page.getByLabel('Nueva contraseña', { exact: true }).fill('Fictional-new-password-2026!');
  await page.getByLabel('Confirmar nueva contraseña').fill('Fictional-new-password-2026!');
  await page.getByRole('button', { name: 'Guardar contraseña' }).click();
  await expect(page.getByRole('alert')).toContainText('actual es incorrecta');
  await expect(page.getByRole('button', { name: 'Guardar contraseña' })).toBeEnabled();
  expect(await page.evaluate(() => localStorage.getItem('prodimt.auth.session.v1'))).not.toBeNull();
});
