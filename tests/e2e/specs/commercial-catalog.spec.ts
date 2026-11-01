import { expect, test, type Page } from '@playwright/test';

const api = 'http://127.0.0.1:5088';
const customer = '11111111-1111-1111-1111-111111111111';
const product = '22222222-2222-2222-2222-222222222201';
const other = '22222222-2222-2222-2222-222222222202';
const day = '2026-11-01';
test.beforeEach(async ({ request }) => { await request.post(api + '/__test/reset'); });

async function login(page: Page) {
  await page.goto('/admin/login'); await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByTestId('admin-today')).toBeVisible();
}

async function priceFixture(page: Page, options: { lost?: boolean; conflict?: boolean; readError?: boolean } = {}) {
  let book: any = { customerId: customer, productId: product, productName: '#9 1/2', unitCode: 'kg', version: 1,
    asOfDate: day, unitPrice: 20, currencyCode: 'MXN', appliedRevisionId: 'rev-1', history: [{ id: 'rev-1', version: 1,
      unitPrice: 20, currencyCode: 'MXN', unitCode: 'kg', productName: '#9 1/2', effectiveFrom: day,
      effectiveUntil: null, recordedAt: day + 'T15:00:00Z', actorId: 'synthetic-admin', actorDisplayName: 'Operador de prueba',
      reason: 'Precio inicial ficticio', operationId: 'old-operation' }] };
  const requests: any[] = [];
  await page.route(`**/api/admin/customers/${customer}/prices`, route => options.readError
    ? route.fulfill({ status: 503, json: { error: 'No disponible' } }) : route.fulfill({ json: [book] }));
  await page.route(`**/api/admin/customers/${customer}/products/${product}/prices`, async route => {
    const body = route.request().postDataJSON(); requests.push(body);
    if (options.conflict && requests.length === 1) {
      book = { ...book, version: 2, unitPrice: 21 };
      await route.fulfill({ status: 409, json: { code: 'CatalogChanged', error: 'Otro administrador cambió el precio. Revisa el historial.' } }); return;
    }
    if (!book.history.some((x: any) => x.operationId === body.operationId)) {
      const revision = { id: `rev-${book.version + 1}`, version: book.version + 1, unitPrice: body.unitPrice,
        currencyCode: 'MXN', unitCode: body.expectedUnitCode, productName: book.productName,
        effectiveFrom: body.effectiveFrom, effectiveUntil: null, recordedAt: day + 'T15:30:00Z',
        actorId: 'synthetic-admin', actorDisplayName: 'Operador de prueba', reason: body.reason, operationId: body.operationId };
      book = { ...book, version: revision.version, history: [revision, ...book.history],
        unitPrice: body.effectiveFrom <= day ? body.unitPrice : book.unitPrice };
    }
    if (options.lost && requests.length === 1) await route.abort('failed');
    else await route.fulfill({ json: book });
  });
  await login(page); await page.goto(`/admin/clientes/${customer}/configuracion`);
  await expect(page.getByTestId('customer-config')).toBeVisible();
  const trigger = page.getByRole('button', { name: 'Precio e historial de #9 1/2', exact: true });
  if (!options.readError) await expect(trigger).toBeEnabled();
  return { requests, trigger, getBook: () => book };
}
async function fillPrice(page: Page, value: string, date = day) {
  const dialog = page.getByRole('dialog');
  const amount = dialog.getByLabel('Precio por kg (MXN)', { exact: true });
  // Opening the native dialog is synchronous; ngModel renders its initial value
  // on the next Angular update. Wait for this fixture's price before replacing it.
  await expect(amount).toHaveValue('20');
  await amount.fill(value);
  await expect(amount).toHaveJSProperty('valueAsNumber', Number(value));
  await dialog.getByLabel('Vigente desde', { exact: true }).fill(date);
  await dialog.getByLabel('Motivo del cambio', { exact: true }).fill('Nuevo acuerdo ficticio');
  return dialog;
}

test('precio por cliente guarda vigencia, unidad, motivo y versión sin guardar ni perder cantidades del perfil', async ({ page }) => {
  const fixture = await priceFixture(page);
  const quantity = page.getByLabel('Cantidad sugerida', { exact: true }).first(); await quantity.fill('33');
  let profileWrites = 0;
  page.on('request', request => { if (request.method() === 'PUT' && request.url().endsWith('/frequent-products')) profileWrites++; });
  await fixture.trigger.click(); const dialog = await fillPrice(page, '25.1250');
  await expect(dialog).toContainText('Operador de prueba'); await expect(dialog).toContainText('Precio inicial ficticio');
  await dialog.getByRole('button', { name: 'Guardar precio', exact: true }).click();
  await expect(dialog.getByRole('status')).toContainText('Cambio guardado');
  expect(fixture.requests).toHaveLength(1);
  expect(fixture.requests[0]).toMatchObject({ unitPrice: 25.125, expectedVersion: 1, expectedUnitCode: 'kg', effectiveFrom: day, reason: 'Nuevo acuerdo ficticio' });
  expect(fixture.requests[0].operationId).toMatch(/^[0-9a-f-]{36}$/i);
  await dialog.getByRole('button', { name: 'Cerrar precio' }).click();
  await expect(quantity).toHaveValue('33'); expect(profileWrites).toBe(0);
});

test('un precio programado queda en historial con fecha y usuario y conserva el precio de hoy', async ({ page }) => {
  const f = await priceFixture(page); await f.trigger.click(); const dialog = await fillPrice(page, '30', '2026-11-05');
  await dialog.getByRole('button', { name: 'Guardar precio', exact: true }).click();
  await expect(dialog.getByRole('status')).toContainText('Precio programado');
  await expect(dialog).toContainText('05/11/2026'); await expect(dialog).toContainText('CDMX');
  await dialog.getByRole('button', { name: 'Cerrar precio' }).click();
  await expect(f.trigger).toContainText('$20.00'); expect(f.getBook().history).toHaveLength(2);
});

test('precio cero se envía explícitamente y dejar sin precio se envía como null', async ({ page }) => {
  const f = await priceFixture(page); await f.trigger.click(); const dialog = await fillPrice(page, '0');
  await dialog.getByRole('button', { name: 'Guardar precio', exact: true }).click();
  await expect(dialog.getByRole('status')).toBeVisible(); expect(f.requests[0].unitPrice).toBe(0);
  await dialog.getByRole('button', { name: 'Cerrar precio' }).click(); await expect(f.trigger).toContainText('$0.00');
  await f.trigger.click(); await dialog.getByLabel('Dejar sin precio desde esa fecha').check();
  await dialog.getByLabel('Motivo del cambio').fill('Precio retirado por revisión ficticia');
  await dialog.getByRole('button', { name: 'Guardar precio', exact: true }).click();
  await expect.poll(() => f.requests.length).toBe(2); expect(f.requests[1].unitPrice).toBeNull();
});

test('respuesta de precio perdida reintenta la misma referencia y conserva una sola revisión', async ({ page }) => {
  const f = await priceFixture(page, { lost: true }); await f.trigger.click(); const dialog = await fillPrice(page, '25');
  await dialog.getByRole('button', { name: 'Guardar precio', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('No pudimos confirmar');
  await dialog.getByRole('button', { name: 'Reintentar cambio de precio' }).click();
  await expect(dialog.getByRole('status')).toBeVisible();
  expect(f.requests).toHaveLength(2); expect(f.requests[1]).toEqual(f.requests[0]); expect(f.getBook().history).toHaveLength(2);
});

test('al recargar recupera la confirmación del precio desde historial sin enviar otra escritura', async ({ page }) => {
  const f = await priceFixture(page, { lost: true }); await f.trigger.click(); const dialog = await fillPrice(page, '25');
  await dialog.getByRole('button', { name: 'Guardar precio', exact: true }).click();
  await expect(dialog.getByRole('alert')).toBeVisible(); await dialog.getByRole('button', { name: 'Cerrar precio' }).click();
  await page.reload(); await expect(f.trigger).toContainText('$25.00');
  expect(f.requests).toHaveLength(1);
});

test('conflicto de versión exige revisar y confirmar otra vez sin perder el precio escrito', async ({ page }) => {
  const f = await priceFixture(page, { conflict: true }); await f.trigger.click(); const dialog = await fillPrice(page, '25');
  await dialog.getByRole('button', { name: 'Guardar precio', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('Otro administrador');
  await expect(dialog.getByLabel('Precio por kg (MXN)', { exact: true })).toHaveValue('25'); expect(f.requests).toHaveLength(1);
  await expect(dialog.getByRole('button', { name: 'Guardar precio', exact: true })).toBeEnabled();
  await dialog.getByRole('button', { name: 'Guardar precio', exact: true }).click();
  await expect(dialog.getByRole('status')).toBeVisible();
  expect(f.requests[1].expectedVersion).toBe(2); expect(f.requests[1].operationId).not.toBe(f.requests[0].operationId);
});

test('fallo de consulta de precios no inventa precio ni bloquea la edición del perfil', async ({ page }) => {
  const f = await priceFixture(page, { readError: true });
  await expect(page.getByText(/No se pudieron consultar los precios/)).toBeVisible();
  await expect(f.trigger).toBeDisabled(); await expect(page.getByLabel('Cantidad sugerida', { exact: true }).first()).toBeEnabled();
});

test('precio e historial se adaptan a teléfono sin desbordar ni cerrar por clic fuera', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 }); const f = await priceFixture(page); await f.trigger.click();
  const dialog = page.getByRole('dialog'); const box = await dialog.boundingBox();
  expect(box!.x).toBeGreaterThanOrEqual(0); expect(box!.x + box!.width).toBeLessThanOrEqual(390);
  expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
  await page.keyboard.press('Escape'); await expect(dialog).not.toBeVisible();
});

async function mergeFixture(page: Page, blocked = false, lost = false) {
  const names = ['#11 (Cliente Alpha)', '#11 (Cliente Beta)'];
  let products: any[] = [{ id: product, name: names[0], description: null, unitCode: 'kg', isActive: true },
    { id: other, name: names[1], description: null, unitCode: 'kg', isActive: true }];
  let applied = false; const requests: any[] = [];
  await page.route('**/api/admin/products', route => route.fulfill({ json: products }));
  await page.route('**/api/admin/products/consolidation/candidates', route => route.fulfill({ json: applied ? [] : [{ suggestedName: '#11', suggestedTargetId: product, products }] }));
  await page.route('**/api/admin/products/consolidation/preview', route => route.fulfill({ json: {
    targetProductId: product, sharedName: '#11', unitCode: 'kg', sources: [products[1]],
    customers: [{ customerId: customer, name: 'Cliente Alpha', profileVersion: 1 }], priceAssignments: 2,
    fingerprint: 'a'.repeat(64), errors: blocked ? ['Cliente Alpha tiene dos productos seleccionados; revisa posiciones.'] : [], canApply: !blocked } }));
  await page.route('**/api/admin/products/consolidation/apply', async route => {
    requests.push(route.request().postDataJSON());
    if (!applied) { applied = true; products = [{ ...products[0], name: '#11' }, { ...products[1], isActive: false, mergedIntoProductId: product }]; }
    if (lost && requests.length === 1) await route.abort('failed');
    else await route.fulfill({ json: { operationId: requests.at(-1).operationId, targetProductId: product, sharedName: '#11', archivedProducts: 1, updatedProfiles: 1, priceAssignments: 2, replayed: requests.length > 1 } });
  });
  await login(page); await page.goto('/admin/catalogos'); await page.getByRole('button', { name: 'Productos', exact: true }).click();
  await page.getByRole('button', { name: 'Revisar productos duplicados' }).click();
  const dialog = page.getByRole('dialog'); await dialog.getByRole('button', { name: 'Revisar grupo' }).click();
  await dialog.getByRole('button', { name: 'Ver impacto antes de consolidar' }).click();
  await expect(dialog.getByText(/duplicados se archivarán/)).toBeVisible();
  return { dialog, requests, names };
}

test('consolidación muestra impacto, exige confirmación y deja un solo producto visible con archivo consultable', async ({ page }) => {
  const f = await mergeFixture(page);
  const apply = f.dialog.getByRole('button', { name: 'Consolidar productos revisados' }); await expect(apply).toBeDisabled();
  await f.dialog.getByLabel('Motivo de consolidación').fill('Mismo producto confirmado en prueba');
  await f.dialog.getByLabel('Confirmo que son el mismo producto').check(); await apply.click();
  await expect(f.dialog.getByRole('status').filter({ hasText: 'duplicados archivados' })).toBeVisible();
  expect(f.requests).toHaveLength(1); expect(f.requests[0]).toMatchObject({ targetProductId: product, sourceProductIds: [other], sharedName: '#11', fingerprint: 'a'.repeat(64) });
  await f.dialog.getByRole('button', { name: 'Cerrar consolidación' }).click();
  await expect(page.getByTestId('catalog-products').getByText('#11', { exact: true })).toBeVisible();
  await expect(page.getByTestId('catalog-products').getByText(f.names[1], { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Mostrar productos archivados por consolidación' }).click();
  const archived = page.locator('article').filter({ hasText: f.names[1] }); await expect(archived).toContainText('Consolidado');
  await expect(archived.getByRole('button', { name: 'Activar', exact: true })).toHaveCount(0);
});

test('consolidación bloqueada no permite aceptar ni sumar configuraciones del mismo cliente', async ({ page }) => {
  const f = await mergeFixture(page, true);
  await expect(f.dialog).toContainText('tiene dos productos seleccionados');
  await expect(f.dialog.getByRole('button', { name: 'Consolidar productos revisados' })).toHaveCount(0); expect(f.requests).toHaveLength(0);
});

test('consolidación con respuesta perdida conserva exactamente la operación al reintentar', async ({ page }) => {
  const f = await mergeFixture(page, false, true);
  await f.dialog.getByLabel('Motivo de consolidación').fill('Equivalencia ficticia revisada');
  await f.dialog.getByLabel('Confirmo que son el mismo producto').check();
  await f.dialog.getByRole('button', { name: 'Consolidar productos revisados' }).click();
  await expect(f.dialog.getByRole('alert')).toContainText('No pudimos confirmar');
  await f.dialog.getByRole('button', { name: 'Reintentar consolidación' }).click();
  await expect(f.dialog.getByRole('status').filter({ hasText: 'duplicados archivados' })).toBeVisible(); expect(f.requests).toHaveLength(2); expect(f.requests[1]).toEqual(f.requests[0]);
});
