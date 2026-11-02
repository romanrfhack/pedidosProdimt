import { expect, test, type Page } from '@playwright/test';
import { deliveryDate, deliveryFixture, partialAssignmentId, secondDeliveryTripId } from '../delivery-fixture';

type Fixture = ReturnType<typeof deliveryFixture>;
const api = 'http://127.0.0.1:5088';
const activeDialog = (page: Page) => page.getByRole('dialog');
const partialStop = (page: Page) => page.getByTestId('delivery-stop').filter({ hasText: 'Cliente Reparto Uno' });

async function board(page: Page, state: Fixture, admin = false) {
  await state.install(page);
  await page.goto('/admin/login');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Salir', exact: true })).toBeVisible();
  await expect(page).toHaveURL(admin ? /\/admin\/reparto(?:\?|$)/ : /\/reparto(?:\?|$)/);
  await page.goto(`${admin ? '/admin/reparto' : '/reparto'}?date=${deliveryDate}`);
  await expect(page.getByTestId('delivery-page')).toBeVisible();
}

async function openDelivery(page: Page) {
  await partialStop(page).getByRole('button', { name: /Registrar entrega/ }).click();
  const dialog = activeDialog(page);
  await expect(dialog.getByLabel('Cantidad (kg)', { exact: true })).toHaveValue('25');
  return dialog;
}

test.beforeEach(async ({ request }) => { await request.post(`${api}/__test/reset`); });

test('entrega parcial conserva pedido de 50, carga de 25, entrega de 20 y saldo de 30 con 5 en custodia', async ({ page }) => {
  const state = deliveryFixture(); await board(page, state);
  const dialog = await openDelivery(page);
  await dialog.getByLabel('Cantidad (kg)', { exact: true }).fill('20');
  await dialog.getByRole('button', { name: 'Guardar entrega', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText(/motivo|diferencia/i);
  expect(state.writes).toHaveLength(0);
  await dialog.getByLabel('Motivo de la diferencia', { exact: true }).fill('El cliente recibió menos.');
  await dialog.getByLabel('Producto cargado que no se entregó', { exact: true }).selectOption('in-vehicle');
  await dialog.getByLabel('Observaciones', { exact: true }).fill('No pagó; se dejó una caja.');
  await dialog.getByRole('button', { name: 'Guardar entrega', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(state.writes).toHaveLength(1);
  expect(state.writes[0].path).toBe(`/api/delivery/assignments/${partialAssignmentId}/deliver`);
  expect(state.writes[0].body).toMatchObject({ quantity: 20, reason: 'El cliente recibió menos.',
    remainderDisposition: 'in-vehicle', notes: 'No pagó; se dejó una caja.', expectedVersion: 1, expectedOrderVersion: 1 });
  const assignment = state.trip.assignments[0];
  expect(assignment.requestedQuantity).toBe(50); expect(assignment.loadedQuantity).toBe(25);
  expect(assignment.deliveredQuantity).toBe(20); expect(assignment.pendingQuantity).toBe(30); expect(assignment.custodyQuantity).toBe(5);
  expect(state.orders[0].lines[0].availableQuantity).toBe(25);
  await expect(partialStop(page)).toContainText(/parcial/i);
  await expect(partialStop(page)).toContainText(/30/);
});

test('cantidades compuestas 10+2 conservan partidas y conteo independiente', async ({ page }) => {
  const state = deliveryFixture(); await board(page, state);
  const stop = page.getByTestId('delivery-stop').filter({ hasText: 'Cliente Compuesto' });
  const a = stop.getByTestId('delivery-line').filter({ hasText: 'Producto A' });
  const b = stop.getByTestId('delivery-line').filter({ hasText: 'Producto B' });
  await a.getByRole('button', { name: /Registrar conteo/ }).click();
  await expect(activeDialog(page).getByLabel('Cantidad (kg)', { exact: true })).toHaveValue('10');
  await activeDialog(page).getByRole('button', { name: 'Guardar conteo', exact: true }).click();
  await expect(activeDialog(page)).toHaveCount(0);
  await b.getByRole('button', { name: /Registrar conteo/ }).click();
  await expect(activeDialog(page).getByLabel('Cantidad (kg)', { exact: true })).toHaveValue('2');
  await activeDialog(page).getByRole('button', { name: 'Guardar conteo', exact: true }).click();
  await expect(activeDialog(page)).toHaveCount(0);
  const counted = state.trip.assignments.filter((item: any) => item.customerName === 'Cliente Compuesto');
  expect(counted.map((item: any) => item.requestedQuantity)).toEqual([10, 2]);
  expect(counted.map((item: any) => item.countedQuantity)).toEqual([10, 2]);
  expect(state.writes.map(item => item.body.quantity)).toEqual([10, 2]);
  expect(state.writes[0].path).not.toEqual(state.writes[1].path);
});

test('surtir una partida no completa el pedido cuando otra partida sigue sin asignar', async ({ page }) => {
  const state = deliveryFixture();
  state.trip.assignments = state.trip.assignments.filter((item: any) => item.productName !== 'Producto B');
  await board(page, state);
  const stop = page.getByTestId('delivery-stop').filter({ hasText: 'Cliente Compuesto' });
  const line = stop.getByTestId('delivery-line').filter({ hasText: 'Producto A' });
  await line.getByRole('button', { name: /Registrar conteo/ }).click();
  await activeDialog(page).getByRole('button', { name: 'Guardar conteo', exact: true }).click();
  await expect(activeDialog(page)).toHaveCount(0);
  await line.getByRole('button', { name: /Confirmar carga/ }).click();
  await activeDialog(page).getByRole('button', { name: 'Confirmar carga', exact: true }).click();
  await expect(activeDialog(page)).toHaveCount(0);
  await line.getByRole('button', { name: /Registrar entrega/ }).click();
  await activeDialog(page).getByRole('button', { name: 'Guardar entrega', exact: true }).click();
  await expect(activeDialog(page)).toHaveCount(0);
  const assigned = state.trip.assignments.find((item: any) => item.productName === 'Producto A');
  expect(assigned.pendingQuantity).toBe(0); expect(assigned.orderIsComplete).toBe(false);
  expect(state.orders[1].lines[1].deliveredQuantity).toBe(0);
  await expect(stop).not.toHaveClass(/delivery-complete/);
  await expect(stop).toContainText(/otras partidas.*pendientes/i);
  await expect(page.locator('.delivery-trip-summary')).toContainText(/0\s*\/\s*2/);
});

test('intento con entrega de cero conserva mercancía y muestra No entregado', async ({ page }) => {
  const state = deliveryFixture(); await board(page, state);
  const dialog = await openDelivery(page);
  await dialog.getByLabel('Cantidad (kg)', { exact: true }).fill('0');
  await dialog.getByLabel('Motivo de la diferencia', { exact: true }).fill('Cliente ausente; se conserva la carga.');
  await dialog.getByLabel('Producto cargado que no se entregó', { exact: true }).selectOption('in-vehicle');
  await dialog.getByRole('button', { name: 'Guardar entrega', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(state.trip.assignments[0].deliveredQuantity).toBe(0);
  expect(state.trip.assignments[0].custodyQuantity).toBe(25);
  expect(state.trip.assignments[0].pendingQuantity).toBe(50);
  await expect(partialStop(page).locator('.delivery-state').first()).toHaveText('No entregado');
  await expect(partialStop(page).locator('.delivery-custody')).toContainText('25 kg');
});

test('permiso de lectura oculta mutaciones y bloquea la gestión de reparto', async ({ page }) => {
  const state = deliveryFixture(); state.setPermissions(['delivery.read.own']); await board(page, state);
  await expect(page.getByTestId('delivery-stop')).toHaveCount(2);
  await expect(page.getByRole('button', { name: /Registrar conteo|Confirmar carga|Registrar entrega|Registrar devolución/ })).toHaveCount(0);
  await page.goto('/admin/reparto');
  await expect(page).toHaveURL(/\/admin\/sin-acceso/);
  expect(state.writes).toHaveLength(0);
});

test('conflicto conserva cantidades y observaciones sin reescritura automática', async ({ page }) => {
  const state = deliveryFixture(); await board(page, state); state.setConflict(true);
  const dialog = await openDelivery(page);
  await dialog.getByLabel('Observaciones', { exact: true }).fill('Conservar esta observación al revisar.');
  await dialog.getByRole('button', { name: 'Guardar entrega', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('El pedido cambió');
  await expect(dialog.getByLabel('Cantidad (kg)', { exact: true })).toHaveValue('25');
  await expect(dialog.getByLabel('Observaciones', { exact: true })).toHaveValue('Conservar esta observación al revisar.');
  expect(state.writes).toHaveLength(1);
  await expect(dialog.getByRole('button', { name: 'Guardar entrega', exact: true })).toBeDisabled();
});

test('respuesta incierta reintenta la misma operación sin duplicar entrega', async ({ page }) => {
  const state = deliveryFixture(); await board(page, state); state.loseNextDeliveryResponse();
  const dialog = await openDelivery(page);
  await dialog.getByRole('button', { name: 'Guardar entrega', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText(/confirmación|respuesta/i);
  await expect(dialog.getByLabel('Cantidad (kg)', { exact: true })).toBeDisabled();
  await dialog.getByRole('button', { name: 'Reintentar operación', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(state.writes).toHaveLength(2); expect(state.writes[1].body).toEqual(state.writes[0].body);
  expect(state.trip.assignments[0].deliveredQuantity).toBe(25);
  expect(state.trip.events.filter((item: any) => item.type === 'Delivered')).toHaveLength(1);
});

test('recargar conserva la referencia pendiente y recupera la entrega sin duplicarla', async ({ page }) => {
  const state = deliveryFixture(); await board(page, state); state.loseNextDeliveryResponse();
  const dialog = await openDelivery(page);
  await dialog.getByRole('button', { name: 'Guardar entrega', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText(/confirmación|respuesta/i);
  const original = state.writes[0].body;
  const pending = await page.evaluate(() => Object.entries(sessionStorage)
    .filter(([key]) => key.startsWith('prodimt.delivery.pending.'))
    .map(([, value]) => JSON.parse(value)));
  expect(pending).toHaveLength(1); expect(pending[0].body).toEqual(original);
  expect(JSON.stringify(pending)).not.toContain('mock-admin-jwt');
  page.once('dialog', prompt => prompt.accept());
  await page.reload();
  const recovery = page.getByRole('region', { name: 'Operación sin confirmación', exact: true });
  await expect(recovery).toBeVisible();
  await recovery.getByRole('button', { name: 'Reintentar operación', exact: true }).click();
  await expect(recovery).toHaveCount(0);
  expect(state.writes).toHaveLength(2); expect(state.writes[1].body).toEqual(original);
  expect(state.trip.assignments[0].deliveredQuantity).toBe(25);
  expect(state.trip.events.filter((item: any) => item.type === 'Delivered')).toHaveLength(1);
  expect(await page.evaluate(() => Object.keys(sessionStorage).filter(key => key.startsWith('prodimt.delivery.pending.')))).toEqual([]);
});

test('gestión reserva sólo la cantidad indicada para una vuelta', async ({ page }) => {
  const state = deliveryFixture(); state.setPermissions(['delivery.manage']); await board(page, state, true);
  await page.getByRole('button', { name: /Asignar a vuelta.*Cliente Reparto Uno.*Tortilla demo/ }).click();
  const dialog = activeDialog(page);
  await dialog.getByLabel('Vuelta de destino', { exact: true }).selectOption(secondDeliveryTripId);
  await dialog.getByLabel('Cantidad a asignar (kg)', { exact: true }).fill('15');
  await dialog.getByRole('button', { name: 'Confirmar asignación', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(state.writes).toHaveLength(1); expect(state.writes[0].path).toMatch(/\/api\/admin\/delivery\/trips\/.*\/assignments$/);
  expect(state.writes[0].body.quantity).toBe(15);
  expect(state.orders[0].lines[0].requestedQuantity).toBe(50);
  expect(state.orders[0].lines[0].availableQuantity).toBe(10);
});

for (const width of [320, 1440]) {
  test(`reparto conserva controles y borrador sin desbordamiento a ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 950 }); const state = deliveryFixture(); await board(page, state);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    const dialog = await openDelivery(page);
    await dialog.getByLabel('Observaciones', { exact: true }).fill('Borrador de entrega responsivo.');
    await page.setViewportSize({ width: width === 320 ? 1440 : 320, height: 950 });
    await expect(dialog.getByLabel('Observaciones', { exact: true })).toHaveValue('Borrador de entrega responsivo.');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`delivery-${width}.png`), fullPage: false });
    expect(state.writes).toHaveLength(0);
  });
}
