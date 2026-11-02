import { expect, test, type Page } from '@playwright/test';

const customerId = '11111111-1111-1111-1111-111111111111';
const metadataUrl = '**/api/admin/orders/next-delivery-date';
const tokenListUrl = new RegExp(`/api/admin/customers/${customerId}/access-tokens(?:\\?.*)?$`);

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
  await page.getByRole('button', { name: 'Tokens de acceso', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Tokens de acceso', exact: true });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByLabel('Descripción', { exact: true })).toHaveValue('Recuerda hacer tu próximo pedido');
  await dialog.getByLabel('Disponible hasta').fill('2030-01-01T18:00');
  return dialog;
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
      description: 'Recuerda hacer tu próximo pedido', expiresAt: '2030-01-02T00:00:00.000Z', reminderLink: true
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
  await page.getByRole('button', { name: 'Abrir WhatsApp', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as any).__whatsAppPreview.location.href)).toContain('https://wa.me/525512345678?text=');
  const url = new URL(await page.evaluate(() => (window as any).__whatsAppPreview.location.href));
  expect(url.searchParams.get('text')).toContain('Recuerda hacer tu pedido de tortillas');
  expect(url.searchParams.get('text')).toContain('domingo, 1 de noviembre de 2026');
  expect(url.searchParams.get('text')).toContain('/cliente#token=mock-generated-token');
  await expect(page.getByText('WhatsApp abierto con el mensaje preparado. Confirma el envío desde WhatsApp.')).toBeVisible();
});

test('el modal conserva borrador al cerrar, aísla el fondo y devuelve el foco sin escrituras', async ({ page }) => {
  const writes: string[] = [];
  page.on('request', request => {
    if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method()) && request.url().includes(`/api/admin/customers/${customerId}`))
      writes.push(request.url());
  });
  const dialog = await openAccess(page, true);
  await dialog.getByLabel('Teléfono de WhatsApp').fill('5512345678');
  await dialog.getByLabel('Descripción', { exact: true }).fill('Recordatorio pendiente de revisión.');
  await dialog.getByLabel('Disponible hasta').fill('2030-02-03T17:30');
  expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
  expect(await page.evaluate(() => document.body.style.overflow)).toBe('hidden');
  // Native showModal makes the underlying controls inert, including programmatic focus.
  await page.getByTestId('customer-config').locator('button').filter({ hasText: 'Guardar frecuentes' }).evaluate(button => button.focus());
  expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
  await page.keyboard.press('Escape');
  await expect(dialog).not.toBeVisible();
  const launcher = page.getByRole('button', { name: 'Tokens de acceso', exact: true });
  await expect(launcher).toBeFocused();
  expect(await page.evaluate(() => document.body.style.overflow)).not.toBe('hidden');
  await launcher.click();
  await expect(dialog.getByLabel('Teléfono de WhatsApp')).toHaveValue('5512345678');
  await expect(dialog.getByLabel('Descripción', { exact: true })).toHaveValue('Recordatorio pendiente de revisión.');
  await expect(dialog.getByLabel('Disponible hasta')).toHaveValue('2030-02-03T17:30');
  await expect(dialog.getByLabel('Vista previa del recordatorio')).toHaveValue(/Recordatorio pendiente de revisión/);
  await dialog.getByRole('button', { name: 'Cerrar tokens de acceso', exact: true }).click();
  await expect(dialog).not.toBeVisible();
  await launcher.click();
  await expect(dialog.getByLabel('Descripción', { exact: true })).toHaveValue('Recordatorio pendiente de revisión.');
  await dialog.getByRole('button', { name: 'Cerrar', exact: true }).click();
  await expect(dialog).not.toBeVisible();
  expect(writes).toHaveLength(0);
});

test('cerrar y reabrir conserva exactamente el enlace generado sin crear otro token', async ({ page }) => {
  let created = 0;
  page.on('request', request => { if (request.method() === 'POST' && request.url().endsWith('/access-tokens')) created++; });
  const dialog = await openAccess(page);
  await dialog.getByLabel('Teléfono de WhatsApp').fill('5512345678');
  await dialog.getByLabel('Descripción', { exact: true }).fill('Recordatorio único para la próxima entrega.');
  await dialog.getByRole('button', { name: 'Crear enlace', exact: true }).click();
  await expect(dialog.getByLabel('Enlace de acceso', { exact: true })).toHaveValue(/#token=mock-generated-token$/);
  const link = await dialog.getByLabel('Enlace de acceso', { exact: true }).inputValue();
  const message = await dialog.getByLabel('Vista previa del recordatorio').inputValue();
  await dialog.getByRole('button', { name: 'Cerrar', exact: true }).click();
  await page.getByRole('button', { name: 'Tokens de acceso', exact: true }).click();
  await expect(dialog.getByLabel('Enlace de acceso', { exact: true })).toHaveValue(link);
  await expect(dialog.getByLabel('Vista previa del recordatorio')).toHaveValue(message);
  await expect(dialog.getByRole('button', { name: 'Copiar enlace', exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Abrir WhatsApp', exact: true })).toBeVisible();
  expect(created).toBe(1);
});

test('crear un enlace pendiente bloquea cierre, Escape y fondo hasta terminar', async ({ page }) => {
  let release!: () => void;
  const gate = new Promise<void>(resolve => release = resolve);
  let created = 0;
  let createdToken: any = null;
  await page.route(tokenListUrl, async route => {
    if (route.request().method() === 'GET' && createdToken) { await route.fulfill({ json: [createdToken] }); return; }
    if (route.request().method() !== 'POST') { await route.fallback(); return; }
    created++;
    const body = route.request().postDataJSON();
    await gate;
    createdToken = { tokenId: 'busy-create-demo', customerId, description: body.description, expiresAt: body.expiresAt,
      isActive: true, isUsable: true, canReveal: true, version: 1, reminderLink: true, createdAt: '2026-10-31T09:00:00-06:00', lastUsedAt: null };
    await route.fulfill({ status: 201, json: { ...createdToken, plainToken: 'busy-token-demo' } });
  });
  try {
    const dialog = await openAccess(page);
    await dialog.getByLabel('Teléfono de WhatsApp').fill('5512345678');
    await dialog.getByRole('button', { name: 'Crear enlace', exact: true }).click();
    await expect.poll(() => created).toBe(1);
    await expect(dialog.getByRole('button', { name: 'Cerrar tokens de acceso', exact: true })).toBeDisabled();
    await expect(dialog.getByRole('button', { name: 'Cerrar', exact: true })).toBeDisabled();
    await page.keyboard.press('Escape');
    await page.mouse.click(2, 2);
    await expect(dialog).toBeVisible();
    release();
    await expect(dialog.getByLabel('Enlace de acceso', { exact: true })).toHaveValue(/#token=busy-token-demo$/);
    await expect(dialog.getByRole('button', { name: 'Cerrar', exact: true })).toBeEnabled();
    await dialog.getByRole('button', { name: 'Cerrar', exact: true }).click();
    await expect(dialog).not.toBeVisible();
    expect(created).toBe(1);
  } finally { release(); }
});

test('preparar WhatsApp bloquea cierre y conserva el enlace sin enviar mensajes', async ({ page }) => {
  const dialog = await openAccess(page);
  await dialog.getByLabel('Teléfono de WhatsApp').fill('5512345678');
  await dialog.getByRole('button', { name: 'Crear enlace', exact: true }).click();
  await expect(dialog.getByLabel('Enlace de acceso', { exact: true })).toBeVisible();
  const link = await dialog.getByLabel('Enlace de acceso', { exact: true }).inputValue();
  let release!: () => void;
  const gate = new Promise<void>(resolve => release = resolve);
  await page.route(metadataUrl, async route => {
    await gate;
    await route.fulfill({ json: { deliveryDate: '2026-11-01', serverNow: '2026-10-31T19:00:00-06:00' } });
  });
  await page.evaluate(() => {
    const popup = { opener: null, location: { href: '' }, close() {} };
    (window as any).__whatsAppModalPreview = popup;
    window.open = (() => popup) as any;
  });
  try {
    await dialog.getByRole('button', { name: 'Abrir WhatsApp', exact: true }).click();
    await expect(dialog.getByRole('button', { name: 'Cerrar tokens de acceso', exact: true })).toBeDisabled();
    await expect(dialog.getByRole('button', { name: 'Cerrar', exact: true })).toBeDisabled();
    await page.keyboard.press('Escape');
    await expect(dialog).toBeVisible();
    expect(await page.evaluate(() => (window as any).__whatsAppModalPreview.location.href)).toBe('');
    release();
    await expect(dialog.getByText('WhatsApp abierto con el mensaje preparado. Confirma el envío desde WhatsApp.')).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Cerrar', exact: true })).toBeEnabled();
    await dialog.getByRole('button', { name: 'Cerrar', exact: true }).click();
    await page.getByRole('button', { name: 'Tokens de acceso', exact: true }).click();
    await expect(dialog.getByLabel('Enlace de acceso', { exact: true })).toHaveValue(link);
  } finally { release(); }
});

for (const viewport of [
  { width: 320, height: 740 }, { width: 390, height: 844 }, { width: 768, height: 1024 },
  { width: 1366, height: 768 }, { width: 1920, height: 1080 }
]) {
  test(`modal de tokens a ${viewport.width}px mantiene controles e historial largo accesibles`, async ({ page }) => {
    await page.setViewportSize(viewport);
    const history = Array.from({ length: 35 }, (_, index) => ({ tokenId: `history-token-${index}`, customerId,
      description: `Recordatorio ${index + 1}: ${'Descripción extensa para comprobar ajuste de texto y acciones. '.repeat(3)}`,
      isActive: true, version: 1, canReveal: true, isUsable: true, reminderLink: true, expiresAt: '2030-01-01T00:00:00Z', createdAt: '2026-10-31T09:00:00-06:00', lastUsedAt: null }));
    await page.route(tokenListUrl, async route => {
      if (route.request().method() === 'GET') await route.fulfill({ json: history });
      else if (route.request().method() === 'POST') {
        const response = await route.fetch();
        const created = await response.json();
        const { plainToken: _plainToken, ...metadata } = created;
        history.push(metadata);
        await route.fulfill({ response, json: created });
      }
      else await route.fallback();
    });
    const dialog = await openAccess(page);
    const bounds = (await dialog.boundingBox())!;
    expect(bounds.x).toBeGreaterThanOrEqual(0);
    expect(bounds.y).toBeGreaterThanOrEqual(0);
    expect(bounds.x + bounds.width).toBeLessThanOrEqual(viewport.width + 1);
    expect(bounds.y + bounds.height).toBeLessThanOrEqual(viewport.height + 1);
    expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    const settings = (await dialog.getByRole('region', { name: 'Configurar enlace', exact: true }).boundingBox())!;
    const preview = (await dialog.getByRole('region', { name: 'Recordatorio para el cliente', exact: true }).boundingBox())!;
    if (viewport.width >= 1366) {
      expect(bounds.width).toBeGreaterThanOrEqual(1150);
      expect(Math.abs(settings.y - preview.y)).toBeLessThan(3);
      expect(preview.x).toBeGreaterThanOrEqual(settings.x + settings.width);
    } else {
      expect(preview.y).toBeGreaterThanOrEqual(settings.y + settings.height);
    }
    await expect(dialog.getByRole('button', { name: 'Cerrar tokens de acceso', exact: true })).toBeInViewport();
    await expect(dialog.getByRole('button', { name: 'Cerrar', exact: true })).toBeInViewport();
    await page.screenshot({ path: test.info().outputPath(`tokens-modal-initial-${viewport.width}.png`), fullPage: false });
    await dialog.getByRole('heading', { name: 'Tokens activos', exact: true }).scrollIntoViewIfNeeded();
    await expect(dialog.getByText(history[34].description, { exact: true })).toBeVisible();
    await dialog.getByRole('button', { name: 'Revocar', exact: true }).last().scrollIntoViewIfNeeded();
    await expect(dialog.getByRole('button', { name: 'Revocar', exact: true }).last()).toBeInViewport();
    await expect(dialog.getByRole('button', { name: 'Cerrar', exact: true })).toBeInViewport();
    await page.screenshot({ path: test.info().outputPath(`tokens-modal-history-${viewport.width}.png`), fullPage: false });
    if (viewport.width === 1366) {
      await dialog.getByLabel('Teléfono de WhatsApp').fill('5512345678');
      await dialog.getByRole('button', { name: 'Crear enlace', exact: true }).click();
      await expect(dialog.getByLabel('Enlace de acceso', { exact: true })).toHaveValue(/#token=mock-generated-token$/);
      await dialog.getByRole('heading', { name: 'Configurar enlace', exact: true }).scrollIntoViewIfNeeded();
      await page.screenshot({ path: test.info().outputPath('tokens-modal-generated-1366.png'), fullPage: false });
    }
    await dialog.getByRole('button', { name: 'Cerrar', exact: true }).click();
    await expect(dialog).not.toBeVisible();
    await expect(page.getByRole('button', { name: 'Tokens de acceso', exact: true })).toBeFocused();
  });
}

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
