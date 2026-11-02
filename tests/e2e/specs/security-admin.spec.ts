import { expect, test, type Page } from '@playwright/test';

async function login(page: Page) {
  await page.goto('/admin/login');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
}
async function security(page: Page) {
  await login(page);
  await page.getByRole('link', { name: 'Seguridad', exact: true }).click();
  await expect(page.getByTestId('security-admin')).toBeVisible();
  await expect(page.getByTestId('security-user')).toHaveCount(2);
}
const editor = (page: Page, kind: string) => page.getByRole('region', { name: `Editor de ${kind}`, exact: true });

test.beforeEach(async ({ request }) => { await request.post('http://127.0.0.1:5088/__test/reset'); });

for (const width of [390, 768, 1440, 1920]) {
  test(`seguridad utiliza ancho disponible y conserva editor sin desbordamiento a ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 });
    await security(page);
    const screen = page.getByTestId('security-admin');
    const metrics = await screen.evaluate(el => ({ width: el.getBoundingClientRect().width,
      overflow: document.documentElement.scrollWidth > innerWidth }));
    expect(metrics.overflow).toBe(false);
    if (width >= 1440) expect(metrics.width).toBeGreaterThan(width * .9);
    await screen.getByRole('button', { name: 'Nuevo usuario', exact: true }).click();
    await expect(editor(page, 'usuario')).toBeVisible();
    await editor(page, 'usuario').getByLabel('Nombre visible', { exact: true }).fill('Borrador de usuario responsivo');
    await page.screenshot({ path: info.outputPath(`security-editor-${width}.png`), fullPage: false });
    await page.setViewportSize({ width: width === 390 ? 1440 : 390, height: 1000 });
    await expect(editor(page, 'usuario').getByLabel('Nombre visible', { exact: true })).toHaveValue('Borrador de usuario responsivo');
    expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
    await page.screenshot({ path: info.outputPath(`security-${width}.png`), fullPage: false });
  });
}

test('usuario combina varios roles y permiso directo, después quitar un origen conserva el heredado', async ({ page }) => {
  await security(page);
  await page.getByRole('button', { name: 'Nuevo usuario', exact: true }).click();
  const form = editor(page, 'usuario');
  await form.getByLabel('Usuario', { exact: true }).fill('captura.supervisor');
  await form.getByLabel('Nombre visible', { exact: true }).fill('Captura Supervisor');
  await form.getByLabel(/^Contraseña inicial/).fill('Fictional-password-2026!');
  await form.getByRole('checkbox', { name: 'Rol Capturista', exact: true }).check();
  await form.getByRole('checkbox', { name: 'Rol Supervisor', exact: true }).check();
  await form.getByRole('checkbox', { name: 'Permiso directo Consultar pedidos', exact: true }).check();
  await form.getByRole('checkbox', { name: 'Permiso directo Administrar precios', exact: true }).check();
  const save = page.waitForRequest(request => request.method() === 'POST' && request.url().endsWith('/api/admin/security/users'));
  await form.getByRole('button', { name: 'Guardar usuario', exact: true }).click();
  const body = (await save).postDataJSON();
  expect(body.roleIds).toHaveLength(2); expect(body.directPermissionIds).toHaveLength(2);
  expect(body.expectedRevision).toBeTruthy();
  await expect(page.getByTestId('security-user')).toHaveCount(3);
  await page.getByRole('button', { name: 'Editar usuario Captura Supervisor', exact: true }).click();
  await form.getByRole('checkbox', { name: 'Rol Capturista', exact: true }).uncheck();
  await form.getByRole('checkbox', { name: 'Permiso directo Consultar pedidos', exact: true }).uncheck();
  await form.getByRole('button', { name: 'Guardar usuario', exact: true }).click();
  await page.getByRole('button', { name: 'Editar usuario Captura Supervisor', exact: true }).click();
  await expect(form.getByRole('checkbox', { name: 'Rol Supervisor', exact: true })).toBeChecked();
  await expect(form.getByRole('checkbox', { name: 'Rol Capturista', exact: true })).not.toBeChecked();
  await expect(form.getByRole('checkbox', { name: 'Permiso directo Consultar pedidos', exact: true })).not.toBeChecked();
  await expect(form.getByRole('checkbox', { name: 'Permiso directo Administrar precios', exact: true })).toBeChecked();
  const effective = form.getByTestId('effective-permission');
  await expect(effective.filter({ hasText: 'Consultar pedidos' })).toContainText('Supervisor');
  await expect(effective.filter({ hasText: 'Capturar pedidos' })).toHaveCount(0);
  await expect(effective.filter({ hasText: 'Administrar precios' })).toContainText('Directo');
});

test('catálogo permite CRUD de roles y permisos propios y protege registros del sistema', async ({ page }) => {
  await security(page);
  await page.getByRole('tab', { name: /^Permisos/ }).click();
  await page.getByRole('button', { name: 'Nuevo permiso', exact: true }).click();
  const permission = editor(page, 'permiso');
  await permission.getByLabel(/^Clave/).fill('reports.export');
  await permission.getByLabel('Nombre del permiso', { exact: true }).fill('Exportar reportes');
  await permission.getByLabel('Módulo', { exact: true }).fill('Reportes');
  await permission.getByLabel('Descripción', { exact: true }).fill('Permiso propio de ejemplo');
  await permission.getByRole('button', { name: 'Guardar permiso', exact: true }).click();
  await expect(page.getByTestId('security-permission').filter({ hasText: 'Exportar reportes' })).toBeVisible();
  await page.getByRole('button', { name: 'Editar permiso Exportar reportes', exact: true }).click();
  await permission.getByLabel('Nombre del permiso', { exact: true }).fill('Exportar reportes de operación');
  await permission.getByRole('button', { name: 'Guardar permiso', exact: true }).click();
  await page.getByRole('tab', { name: /^Roles/ }).click();
  await page.getByRole('button', { name: 'Nuevo rol', exact: true }).click();
  const role = editor(page, 'rol');
  await role.getByLabel('Nombre del rol', { exact: true }).fill('Reportes');
  await role.getByLabel('Descripción', { exact: true }).fill('Rol específico');
  await role.getByRole('checkbox', { name: 'Permiso Exportar reportes de operación', exact: true }).check();
  await role.getByRole('button', { name: 'Guardar rol', exact: true }).click();
  await expect(page.getByTestId('security-role').filter({ hasText: 'Reportes' })).toBeVisible();
  await page.getByRole('button', { name: 'Editar rol Reportes', exact: true }).click();
  await role.getByLabel('Nombre del rol', { exact: true }).fill('Reportes internos');
  await role.getByRole('button', { name: 'Guardar rol', exact: true }).click();
  const admin = page.getByTestId('security-role').filter({ hasText: 'Administrador' });
  await expect(admin.getByRole('button', { name: /^Eliminar/ })).toBeDisabled();
  await page.getByRole('button', { name: 'Eliminar rol Reportes internos', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar eliminación', exact: true }).click();
  await expect(page.getByTestId('security-role').filter({ hasText: 'Reportes internos' })).toHaveCount(0);
  await page.getByRole('tab', { name: /^Permisos/ }).click();
  await page.getByRole('button', { name: 'Eliminar permiso Exportar reportes de operación', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar eliminación', exact: true }).click();
  await expect(page.getByTestId('security-permission').filter({ hasText: 'Exportar reportes de operación' })).toHaveCount(0);
});

test('conflicto de concurrencia conserva borrador y no reintenta escritura automáticamente', async ({ page }) => {
  await security(page);
  await page.getByRole('button', { name: 'Editar usuario Operador Demo', exact: true }).click();
  const form = editor(page, 'usuario');
  await form.getByLabel('Nombre visible', { exact: true }).fill('Borrador que debe conservarse');
  let writes = 0;
  await page.route('**/api/admin/security/users/*', route => {
    writes++; return route.fulfill({ status: 409, json: { error: 'La configuración cambió. Recarga antes de guardar.' } });
  });
  await form.getByRole('button', { name: 'Guardar usuario', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('configuración cambió');
  await expect(form.getByLabel('Nombre visible', { exact: true })).toHaveValue('Borrador que debe conservarse');
  await expect(form.getByRole('button', { name: 'Guardar usuario', exact: true })).toBeDisabled();
  expect(writes).toBe(1);
  await page.getByRole('button', { name: 'Recargar y descartar cambios', exact: true }).click();
  await expect(page.getByTestId('security-user')).toHaveCount(2);
  expect(writes).toBe(1);
});

test('guard consulta permisos del servidor y no concede acceso desde almacenamiento local', async ({ page }) => {
  await page.route('**/api/admin/security/me', route => route.fulfill({ json: {
    userId: '92000000-0000-0000-0000-000000000002', permissions: ['orders.read']
  } }));
  await login(page);
  await page.evaluate(() => {
    const session = JSON.parse(localStorage.getItem('prodimt.auth.session.v1')!);
    session.permissions = ['security.manage'];
    localStorage.setItem('prodimt.auth.session.v1', JSON.stringify(session));
  });
  await page.goto('/admin/seguridad');
  await expect(page.getByTestId('security-admin')).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Seguridad', exact: true })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Pedidos', exact: true })).toBeVisible();
});

test('eliminar usuario requiere confirmación y lo retira del listado operativo', async ({ page }) => {
  await security(page);
  const row = page.getByTestId('security-user').filter({ hasText: 'Operador Demo' });
  let writes = 0;
  page.on('request', request => { if (request.method() === 'DELETE' && request.url().includes('/api/admin/security/users/')) writes++; });
  await row.getByRole('button', { name: 'Eliminar usuario Operador Demo', exact: true }).click();
  expect(writes).toBe(0);
  const deletion = page.waitForRequest(request => request.method() === 'DELETE' && request.url().includes('/api/admin/security/users/'));
  await page.getByRole('button', { name: 'Confirmar eliminación', exact: true }).click();
  const request = await deletion;
  expect(new URL(request.url()).searchParams.get('expectedRevision')).toBeTruthy();
  await expect(row).toHaveCount(0);
  expect(writes).toBe(1);
});

for (const permission of ['tokens.manage', 'pricing.manage', 'products.read']) {
  test(`permiso directo aislado ${permission} habilita su función sin conceder otras acciones`, async ({ page }) => {
    const forbidden: string[] = [];
    await page.route('**/api/admin/**', async route => {
      const path = new URL(route.request().url()).pathname;
      if (path === '/api/admin/security/me') {
        await route.fulfill({ json: { userId: '92000000-0000-0000-0000-000000000002', permissions: [permission] } }); return;
      }
      const allowed = permission === 'products.read' ? path === '/api/admin/products' :
        ['/api/admin/customers', '/api/admin/customers/day-status'].includes(path) ||
        (permission === 'tokens.manage' && (path === '/api/admin/orders/next-delivery-date' || /\/customers\/[^/]+\/access-tokens$/.test(path))) ||
        (permission === 'pricing.manage' && (path === '/api/admin/products' || /\/customers\/[^/]+\/(ordering-profile|prices)$/.test(path)));
      if (!allowed) {
        forbidden.push(path); await route.fulfill({ status: 403, json: { error: 'Sin permiso para esta acción.' } }); return;
      }
      if (path.endsWith('/prices')) { await route.fulfill({ json: [] }); return; }
      await route.continue();
    });
    await page.goto('/admin/login');
    await page.getByRole('button', { name: 'Entrar', exact: true }).click();
    await expect(page.getByTestId('admin-catalogs')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Seguridad', exact: true })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Pedidos', exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: /Nuevo cliente|Guardar frecuentes|Desactivar|^Editar$/ })).toHaveCount(0);
    if (permission === 'products.read') {
      await expect(page.getByTestId('catalog-products')).toBeVisible();
      await expect(page.getByTestId('product-card')).toHaveCount(3);
      await expect(page.getByRole('button', { name: 'Nuevo producto', exact: true })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Clientes', exact: true })).toHaveCount(0);
    } else {
      const customer = page.locator('[data-testid="customer-card"][data-customer-id="11111111-1111-1111-1111-111111111111"]');
      await customer.getByRole('button', { name: 'Configurar', exact: true }).click();
      await expect(page.getByTestId('customer-config')).toBeVisible();
      await expect(page.getByRole('button', { name: /Nuevo cliente|Guardar frecuentes|Guardar maquinas|Editar datos y entrega/ })).toHaveCount(0);
      if (permission === 'tokens.manage') {
        await page.getByRole('button', { name: /Tokens de acceso/ }).click();
        await expect(page.getByRole('dialog')).toBeVisible();
        await expect(page.getByRole('button', { name: /Precio e historial/ })).toHaveCount(0);
      } else {
        await expect(page.getByRole('button', { name: /Precio e historial/ }).first()).toBeEnabled();
        await expect(page.getByRole('button', { name: /Tokens de acceso/ })).toHaveCount(0);
      }
    }
    expect(forbidden).toEqual([]);
  });
}
