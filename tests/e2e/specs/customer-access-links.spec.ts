import { expect, test, type Page } from '@playwright/test';

const customerId = '11111111-1111-1111-1111-111111111111';
const metadataUrl = '**/api/admin/orders/next-delivery-date';

async function openAccess(page: Page, missingPhone = false) {
  await page.route(metadataUrl, route => route.fulfill({ json: { deliveryDate: '2026-10-31', serverNow: '2026-10-31T09:00:00-06:00' } }));
  if (missingPhone) await page.route('**/api/admin/customers', async route => {
    const response = await route.fetch();
    const customers = await response.json();
    customers.find((x: { id: string }) => x.id === customerId).phoneNumber = '';
    await route.fulfill({ json: customers });
  });
  await page.goto('/admin/login');
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
  await page.goto(`/admin/clientes/${customerId}/configuracion`);
  await expect(page.getByTestId('customer-config').getByText('Cargando configuración...')).toHaveCount(0);
  await page.locator('.config-access > summary').click();
  await expect(page.getByLabel('Descripción', { exact: true })).toHaveValue('Recuerda hacer tu próximo pedido');
  await page.getByLabel('Disponible hasta').fill('2026-11-01T18:00');
}

test.beforeEach(async ({ request }) => { await request.post('http://127.0.0.1:5088/__test/reset'); });

test('sin teléfono exige guardarlo antes de crear enlace y conserva la zona CDMX', async ({ page }) => {
  const writes: { path: string; body: unknown }[] = [];
  page.on('request', request => {
    if (request.method() === 'PATCH' && request.url().endsWith('/phone') || request.method() === 'POST' && request.url().endsWith('/access-tokens'))
      writes.push({ path: new URL(request.url()).pathname, body: request.postDataJSON() });
  });
  await openAccess(page, true);
  await page.getByRole('button', { name: 'Guardar teléfono y crear enlace' }).click();
  await expect(page.getByRole('alert')).toContainText('Captura un teléfono');
  expect(writes).toHaveLength(0);
  await page.getByLabel('Teléfono de WhatsApp').fill('55 1234 5678');
  await page.getByRole('button', { name: 'Guardar teléfono y crear enlace' }).click();
  await expect(page.getByLabel('Enlace de acceso', { exact: true })).toHaveValue(/\/cliente#token=mock-generated-token$/);
  expect(writes).toEqual([
    { path: `/api/admin/customers/${customerId}/phone`, body: { phoneNumber: '+525512345678' } },
    { path: `/api/admin/customers/${customerId}/access-tokens`, body: {
      description: 'Recuerda hacer tu próximo pedido', expiresAt: '2026-11-02T00:00:00.000Z', reminderLink: true
    } }
  ]);
});

test('si falla guardar teléfono no crea token y conserva el borrador', async ({ page }) => {
  let created = 0;
  page.on('request', request => { if (request.method() === 'POST' && request.url().endsWith('/access-tokens')) created++; });
  await openAccess(page, true);
  await page.route('**/phone', route => route.fulfill({ status: 409, json: { error: 'No se pudo guardar el teléfono.' } }));
  await page.getByLabel('Teléfono de WhatsApp').fill('5512345678');
  await page.getByLabel('Descripción', { exact: true }).fill('Captura tu pedido, por favor.');
  await page.getByRole('button', { name: 'Guardar teléfono y crear enlace' }).click();
  await expect(page.getByRole('alert')).toContainText('No se pudo guardar el teléfono.');
  await expect(page.getByLabel('Descripción', { exact: true })).toHaveValue('Captura tu pedido, por favor.');
  expect(created).toBe(0);
});

test('WhatsApp individual usa teléfono guardado, descripción, enlace y fecha fresca sin enviar', async ({ page }) => {
  await openAccess(page);
  await page.getByLabel('Teléfono de WhatsApp').fill('5512345678');
  await page.getByLabel('Descripción', { exact: true }).fill('Recuerda hacer tu pedido de tortillas');
  await page.getByRole('button', { name: 'Crear enlace', exact: true }).click();
  await expect(page.getByLabel('Enlace de acceso', { exact: true })).toBeVisible();
  await page.route(metadataUrl, route => route.fulfill({ json: { deliveryDate: '2026-11-01', serverNow: '2026-10-31T19:00:00-06:00' } }));
  await page.evaluate(() => {
    const fixture = { opener: null, location: { href: '' }, close() {} };
    (window as any).__whatsAppPreview = fixture;
    window.open = (() => fixture) as any;
  });
  await page.getByRole('button', { name: 'Abrir WhatsApp' }).click();
  await expect.poll(() => page.evaluate(() => (window as any).__whatsAppPreview.location.href)).toContain('https://wa.me/525512345678?text=');
  const url = new URL(await page.evaluate(() => (window as any).__whatsAppPreview.location.href));
  expect(url.searchParams.get('text')).toContain('Recuerda hacer tu pedido de tortillas');
  expect(url.searchParams.get('text')).toContain('domingo, 1 de noviembre de 2026');
  expect(url.searchParams.get('text')).toContain('/cliente#token=mock-generated-token');
  await expect(page.getByText('WhatsApp abierto con el mensaje preparado. Confirma el envío desde WhatsApp.')).toBeVisible();
});

test('captura administrativa usa próxima fecha del servidor y revalida corte conservando cantidades', async ({ page }) => {
  let date = '2026-11-01';
  await page.route(metadataUrl, route => route.fulfill({ json: { deliveryDate: date, serverNow: '2026-10-31T19:00:00-06:00' } }));
  await page.goto('/admin/login'); await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByTestId('admin-today')).toBeVisible(); await page.goto('/admin/clientes-pendientes');
  await expect(page.getByLabel('Fecha de servicio')).toHaveValue(date);
  await page.getByRole('button', { name: 'Capturar pedido' }).first().click();
  await page.getByRole('button', { name: 'Captura manual', exact: true }).click();
  await page.getByRole('spinbutton').first().fill('8');
  const attempts: any[] = [];
  await page.route('**/orders/capture', async route => {
    attempts.push(route.request().postDataJSON());
    if (attempts.length === 1) {
      date = '2026-11-02';
      await route.fulfill({ status: 409, json: { code: 'DeliveryDateChanged', deliveryDate: date,
        serverNow: '2026-11-01T18:00:00-06:00', error: 'Cambió la fecha de entrega. Confirma de nuevo.' } });
    } else await route.fulfill({ json: { order: { orderId: 'order-demo' }, replayed: false } });
  });
  await page.getByRole('button', { name: 'Guardar pedido' }).click();
  await expect(page.getByLabel('Fecha de servicio')).toHaveValue('2026-11-02');
  await expect(page.getByRole('spinbutton').first()).toHaveValue('8');
  expect(attempts[0]).toMatchObject({ serviceDate: '2026-11-01', autoDeliveryDate: true, expectedDeliveryDate: '2026-11-01' });
  await page.getByRole('button', { name: 'Guardar pedido' }).click();
  await expect(page.getByText('Pedido capturado por administracion.')).toBeVisible();
  expect(attempts[1]).toMatchObject({ serviceDate: '2026-11-02', autoDeliveryDate: true, expectedDeliveryDate: '2026-11-02' });
});
