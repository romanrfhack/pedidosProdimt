import { expect, test, type Page } from '@playwright/test';

const customerId = '11111111-1111-1111-1111-111111111111';
const tokenId = '55555555-5555-5555-5555-555555555512';
const plainToken = 'protected-access-demo-value';
const listPattern = new RegExp(`/api/admin/customers/${customerId}/access-tokens(?:\\?.*)?$`);
const metadataPattern = '**/api/admin/orders/next-delivery-date';

async function managerFixture(page: Page, includeInactive = false) {
  const current = { tokenId, customerId, description: 'Acceso protegido vigente', expiresAt: '2030-01-02T00:00:00.000Z',
    isActive: true, createdAt: '2026-11-01T09:00:00-06:00', lastUsedAt: null, version: 3, canReveal: true, isUsable: true, reminderLink: true };
  const state = { current, serverNow: '2026-11-01T09:00:00-06:00', deliveryDate: '2026-11-01', reveals: [] as string[],
    updates: [] as any[], creates: 0, queries: [] as string[], conflictNext: false, revealRejected: false };
  await page.addInitScript(() => {
    (window as any).__copiedAccess = [];
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: {
      writeText: async (value: string) => { (window as any).__copiedAccess.push(value); }
    } });
  });
  await page.route(metadataPattern, route => route.fulfill({ json: { deliveryDate: state.deliveryDate, serverNow: state.serverNow } }));
  await page.route('**/api/admin/customers', async route => {
    if (route.request().method() !== 'GET') { await route.fallback(); return; }
    const response = await route.fetch(); const customers = await response.json();
    customers.find((customer: any) => customer.id === customerId).phoneNumber = '+525512345678';
    await route.fulfill({ response, json: customers });
  });
  await page.route(listPattern, async route => {
    if (route.request().method() === 'POST') { state.creates++; await route.fallback(); return; }
    state.queries.push(new URL(route.request().url()).searchParams.get('activeOnly') ?? '');
    const rows = [state.current];
    if (includeInactive) rows.push(
      { ...current, tokenId: 'revoked-fixture', description: 'No mostrar revocado', isActive: false },
      { ...current, tokenId: 'expired-fixture', description: 'No mostrar expirado', expiresAt: '2026-10-31T12:00:00Z' }
    );
    await route.fulfill({ json: rows });
  });
  await page.route(`**/access-tokens/${tokenId}/reveal`, async route => {
    state.reveals.push(tokenId);
    if (state.revealRejected) { await route.fulfill({ status: 409, json: { error: 'El token venció o fue revocado.' } }); return; }
    await route.fulfill({ headers: { 'cache-control': 'no-store' }, json: { tokenId, customerId, plainToken,
      description: current.description, expiresAt: current.expiresAt, version: current.version, reminderLink: true } });
  });
  await page.route(`**/access-tokens/${tokenId}`, async route => {
    const body = route.request().postDataJSON(); state.updates.push(body);
    if (state.conflictNext) {
      state.conflictNext = false; current.version++; current.description = 'Actualizado en otra pantalla';
      await route.fulfill({ status: 409, json: { error: 'El token cambió. Recarga sus datos antes de guardar.' } }); return;
    }
    expect(body.expectedVersion).toBe(current.version);
    current.version++; current.description = body.description; current.expiresAt = body.expiresAt;
    await route.fulfill({ json: current });
  });
  return state;
}

async function openManager(page: Page, login = true) {
  if (login) {
    await page.goto('/admin/login'); await page.getByRole('button', { name: 'Entrar', exact: true }).click();
    await expect(page.getByTestId('admin-today')).toBeVisible();
  }
  await page.goto(`/admin/clientes/${customerId}/configuracion`);
  await page.getByRole('button', { name: 'Tokens de acceso', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Tokens de acceso', exact: true });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByTestId('access-token-row').filter({ hasText: 'Acceso protegido vigente' })).toBeVisible();
  return dialog;
}

test.beforeEach(async ({ request }) => { await request.post('http://127.0.0.1:5088/__test/reset'); });

test('sólo lista tokens activos no vencidos y solicita activeOnly al servidor', async ({ page }) => {
  const state = await managerFixture(page, true); const dialog = await openManager(page);
  await expect(dialog.getByTestId('access-token-row')).toHaveCount(1);
  await expect(dialog.getByText('No mostrar revocado')).toHaveCount(0);
  await expect(dialog.getByText('No mostrar expirado')).toHaveCount(0);
  expect(state.queries.length).toBeGreaterThan(0);
  expect(state.queries.every(value => value === 'true')).toBe(true);
  state.serverNow = '2030-01-02T00:00:00.000Z';
  await dialog.getByRole('button', { name: 'Cerrar', exact: true }).click();
  await page.getByRole('button', { name: 'Tokens de acceso', exact: true }).click();
  await expect(dialog.getByTestId('access-token-row')).toHaveCount(0);
  await expect(dialog.getByText(/No hay tokens activos/)).toBeVisible();
});

test('tras recargar permite copiar el mismo token y enlace sin crear ni almacenar el secreto', async ({ page }) => {
  const state = await managerFixture(page); let dialog = await openManager(page);
  await expect(dialog.getByLabel('Token de acceso', { exact: true })).toHaveCount(0);
  expect(state.reveals).toHaveLength(0);
  await dialog.getByRole('button', { name: 'Copiar token', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as any).__copiedAccess.at(-1))).toBe(plainToken);
  await dialog.getByRole('button', { name: 'Copiar enlace', exact: true }).click();
  const expectedLink = `${new URL(page.url()).origin}/cliente#token=${plainToken}`;
  await expect.poll(() => page.evaluate(() => (window as any).__copiedAccess.at(-1))).toBe(expectedLink);
  await page.reload(); dialog = await openManager(page, false);
  await dialog.getByRole('button', { name: 'Copiar token', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as any).__copiedAccess.at(-1))).toBe(plainToken);
  expect(state.reveals).toHaveLength(3); expect(state.creates).toBe(0);
  expect(await page.evaluate(() => JSON.stringify(localStorage) + JSON.stringify(sessionStorage))).not.toContain(plainToken);
});

test('Editar precarga metadatos sin revelar y guarda descripción y vigencia conservando token y enlace', async ({ page }) => {
  const state = await managerFixture(page); const dialog = await openManager(page);
  await dialog.getByRole('button', { name: 'Editar', exact: true }).click();
  await expect(dialog.getByLabel('Descripción', { exact: true })).toHaveValue('Acceso protegido vigente');
  await expect(dialog.getByLabel('Disponible hasta')).toHaveValue('2030-01-01T18:00');
  expect(state.reveals).toHaveLength(0);
  await dialog.getByLabel('Descripción', { exact: true }).fill('Texto actualizado para el mismo acceso');
  await dialog.getByLabel('Disponible hasta').fill('2030-01-04T17:00');
  await expect(dialog.getByLabel('Vista previa del recordatorio')).toHaveValue(/Texto actualizado para el mismo acceso/);
  await dialog.getByRole('button', { name: 'Guardar cambios', exact: true }).click();
  await expect(dialog.getByText(/Cambios del token guardados/)).toBeVisible();
  expect(state.updates).toEqual([{ expectedVersion: 3, description: 'Texto actualizado para el mismo acceso', expiresAt: '2030-01-04T23:00:00.000Z' }]);
  expect(state.creates).toBe(0); expect(state.reveals).toHaveLength(0);
  await dialog.getByRole('button', { name: 'Copiar token', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as any).__copiedAccess.at(-1))).toBe(plainToken);
  await expect(dialog.getByLabel('Enlace de acceso', { exact: true })).toHaveValue(new RegExp(`#token=${plainToken}$`));
});

test('conflicto de versión conserva borrador y exige recargar explícitamente antes de guardar', async ({ page }) => {
  const state = await managerFixture(page); const dialog = await openManager(page);
  await dialog.getByRole('button', { name: 'Editar', exact: true }).click();
  await dialog.getByLabel('Descripción', { exact: true }).fill('Mi borrador no debe perderse');
  await dialog.getByLabel('Disponible hasta').fill('2030-01-03T17:30'); state.conflictNext = true;
  await dialog.getByRole('button', { name: 'Guardar cambios', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('El token cambió.');
  await expect(dialog.getByLabel('Descripción', { exact: true })).toHaveValue('Mi borrador no debe perderse');
  await expect(dialog.getByLabel('Disponible hasta')).toHaveValue('2030-01-03T17:30');
  await expect(dialog.getByRole('button', { name: 'Guardar cambios', exact: true })).toBeDisabled();
  await dialog.getByRole('button', { name: 'Recargar datos del token', exact: true }).click();
  await expect(dialog.getByLabel('Descripción', { exact: true })).toHaveValue('Actualizado en otra pantalla');
  await dialog.getByLabel('Descripción', { exact: true }).fill('Revisión explícita del valor vigente');
  await dialog.getByRole('button', { name: 'Guardar cambios', exact: true }).click();
  await expect(dialog.getByText(/Cambios del token guardados/)).toBeVisible();
  expect(state.updates[1].expectedVersion).toBe(4); expect(state.creates).toBe(0);
});

test('editar sólo descripción conserva los segundos del vencimiento original', async ({ page }) => {
  const state = await managerFixture(page);
  state.current.expiresAt = '2030-01-02T00:00:37.000Z';
  const dialog = await openManager(page);
  await dialog.getByRole('button', { name: 'Editar', exact: true }).click();
  await dialog.getByLabel('Descripción', { exact: true }).fill('Sólo cambia este mensaje');
  await dialog.getByRole('button', { name: 'Guardar cambios', exact: true }).click();
  await expect(dialog.getByText(/Cambios del token guardados/)).toBeVisible();
  expect(state.updates[0].expiresAt).toBe('2030-01-02T00:00:37.000Z');
});

test('Guardar y abrir WhatsApp reserva ventana, guarda y usa el mismo token con mensaje actualizado', async ({ page }) => {
  const state = await managerFixture(page); const dialog = await openManager(page);
  await page.evaluate(() => {
    const popup = { opener: null, location: { href: '' }, closed: false, close() { this.closed = true; } };
    (window as any).__accessPopup = popup; window.open = (() => popup) as any;
  });
  await dialog.getByRole('button', { name: 'Editar', exact: true }).click();
  await dialog.getByLabel('Descripción', { exact: true }).fill('Mensaje editado antes de compartir');
  state.deliveryDate = '2026-11-02';
  await dialog.getByRole('button', { name: 'Guardar y abrir WhatsApp', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as any).__accessPopup.location.href)).toContain('https://wa.me/525512345678?text=');
  const url = new URL(await page.evaluate(() => (window as any).__accessPopup.location.href));
  expect(url.searchParams.get('text')).toContain('Mensaje editado antes de compartir');
  expect(url.searchParams.get('text')).toContain('Haz tu pedido aquí:\n');
  expect(url.searchParams.get('text')).toContain('Enlace disponible hasta el 1 de enero de 2030');
  expect(url.searchParams.get('text')).not.toContain('Entrega prevista si lo confirmas ahora:');
  expect(url.searchParams.get('text')).not.toContain('Los pedidos confirmados desde las 6:00 p. m.');
  expect(url.searchParams.get('text')).toContain(`/cliente#token=${plainToken}`);
  expect(state.updates).toHaveLength(1); expect(state.reveals).toHaveLength(1); expect(state.creates).toBe(0);
});

test('vista previa, copia, WhatsApp y copia manual comparten plantilla sin cambiar el acceso', async ({ page }) => {
  const state = await managerFixture(page); const dialog = await openManager(page);
  await dialog.getByRole('button', { name: 'Editar', exact: true }).click();
  const preview = dialog.getByLabel('Vista previa del recordatorio');
  await expect(preview).toHaveValue(/Haz tu pedido aquí:\n\{enlace de acceso\}/);
  await dialog.getByRole('button', { name: 'Copiar enlace', exact: true }).click();
  await expect(dialog.getByLabel('Enlace de acceso', { exact: true })).toHaveValue(new RegExp(`#token=${plainToken}$`));
  await dialog.getByRole('button', { name: 'Copiar mensaje', exact: true }).click();
  await expect(dialog.getByText('Mensaje copiado.', { exact: true })).toBeVisible();
  const copied = await page.evaluate(() => (window as any).__copiedAccess.at(-1)) as string;
  expect(copied).toContain('Hola, Gran Takito. Acceso protegido vigente');
  const link = `${new URL(page.url()).origin}/cliente#token=${plainToken}`;
  expect(copied).toContain(`\n\nHaz tu pedido aquí:\n${link}\n\nEnlace disponible hasta el `);
  expect(copied).toContain('1 de enero de 2030');
  expect(copied).toMatch(/hora de Ciudad de México\.$/);
  expect(copied).not.toContain('Entrega prevista si lo confirmas ahora:');
  expect(copied).not.toContain('Los pedidos confirmados desde las 6:00 p. m.');
  await expect(preview).toHaveValue(copied);
  state.deliveryDate = '2026-11-02'; state.serverNow = '2026-11-01T19:00:00-06:00';
  await page.evaluate(() => {
    const popup = { opener: null, location: { href: '' }, close() {} };
    (window as any).__accessPopup = popup; window.open = (() => popup) as any;
  });
  await dialog.getByRole('button', { name: 'Abrir WhatsApp', exact: true }).first().click();
  await expect.poll(() => page.evaluate(() => (window as any).__accessPopup.location.href)).toContain('https://wa.me/525512345678?text=');
  const url = new URL(await page.evaluate(() => (window as any).__accessPopup.location.href));
  expect(url.searchParams.get('text')).toBe(copied);
  expect(new URL(link).search).toBe('');
  expect(new URL(link).hash).toBe(`#token=${plainToken}`);
  await expect(preview).toHaveValue(copied);
  await page.evaluate(() => {
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: async () => { throw new Error('Denied'); } } });
  });
  await dialog.getByRole('button', { name: 'Copiar mensaje', exact: true }).click();
  await expect(dialog.getByLabel('Contenido para copiar manualmente')).toHaveValue(copied);
  expect(state.creates).toBe(0); expect(state.updates).toHaveLength(0);
});

test('fallo de clipboard ofrece selección manual y un reveal revocado nunca prepara WhatsApp', async ({ page }) => {
  const state = await managerFixture(page); const dialog = await openManager(page);
  await page.evaluate(() => {
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: async () => { throw new Error('Denied'); } } });
  });
  await dialog.getByRole('button', { name: 'Copiar enlace', exact: true }).click();
  await expect(dialog.getByLabel('Contenido para copiar manualmente')).toHaveValue(new RegExp(`#token=${plainToken}$`));
  await page.evaluate(() => {
    const popup = { opener: null, location: { href: '' }, closed: false, close() { this.closed = true; } };
    (window as any).__accessPopup = popup; window.open = (() => popup) as any;
  });
  state.revealRejected = true;
  await dialog.getByRole('button', { name: 'Abrir WhatsApp', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('El token venció o fue revocado.');
  expect(await page.evaluate(() => (window as any).__accessPopup.location.href)).toBe('');
  expect(await page.evaluate(() => (window as any).__accessPopup.closed)).toBe(true);
  await expect(dialog.getByTestId('access-token-row')).toHaveCount(0);
  await expect(dialog.getByLabel('Contenido para copiar manualmente')).toHaveCount(0);
});
