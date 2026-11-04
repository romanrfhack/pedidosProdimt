import { expect, test, type Page } from '@playwright/test';
const api = 'http://127.0.0.1:5088';
const date = '2026-11-02';
const m1 = '44444444-4444-4444-4444-444444444401', m2 = '44444444-4444-4444-4444-444444444402';
const product = '22222222-2222-2222-2222-222222222201';
const orderId = '33333333-3333-3333-3333-333333333301';
function line(id: string, machine: string | null, quantity: number, allocated = 0) {
  return { orderLineId: id, productId: product, productName: '#11 Demo', unitCode: 'kg', quantity, assignedMachineId: machine,
    assignedMachineNumber: machine === m1 ? 1 : machine === m2 ? 2 : null, assignedMachineName: null,
    allocatedQuantity: allocated, pendingAllocationQuantity: quantity - allocated, notes: null };
}
function fixture() {
  const dashboard: any = { date, routeCode: null, summaryScope: 'all-routes', machines: [], batches: [{ id: 'batch-initial', date, machineId: m1, machineNumber: 1,
    machineName: null, productId: product, productName: '#11 Demo', unitCode: 'kg', quantity: 20, allocatedQuantity: 10, availableQuantity: 10, createdAt: `${date}T12:00:00Z` }],
    orders: [
      { orderId, customerId: 'customer1', customerName: 'Cliente Producción Uno', sequenceNumber: 1, routeCode: 'A', routeStopOrder: 1, orderVersion: 1,
        internalDeliveryInstructions: 'Separar bolsas de cinco kilos.', requestedDeliveryTime: '09:30:00', requestedDeliveryWindowStart: null, requestedDeliveryWindowEnd: null, lines: [line('line-one', m1, 30, 10)] },
      { orderId: 'order-two', customerId: 'customer2', customerName: 'Cliente Producción Dos', sequenceNumber: 1, routeCode: 'B', routeStopOrder: 2, orderVersion: 1,
        internalDeliveryInstructions: null, requestedDeliveryTime: null, requestedDeliveryWindowStart: null, requestedDeliveryWindowEnd: null, lines: [line('line-two', m2, 20)] },
      { orderId: 'order-three', customerId: 'customer3', customerName: 'Cliente Sin Asignar', sequenceNumber: 1, routeCode: 'A', routeStopOrder: 3, orderVersion: 1,
        internalDeliveryInstructions: null, requestedDeliveryTime: null, requestedDeliveryWindowStart: null, requestedDeliveryWindowEnd: null, lines: [line('line-three', null, 15)] }
    ], allocations: [{ id: 'allocation-initial', batchId: 'batch-initial', orderId, orderLineId: 'line-one', customerName: 'Cliente Producción Uno', quantity: 10,
      createdAt: `${date}T12:10:00Z`, reversedAt: null, reverseReason: null }] };
  const writes: Array<{ path: string; body: any }> = [];
  const operations = new Map<string, any>();
  let loseFirstBatchResponse = false, loseFirstAllocationResponse = false;
  function refresh() {
    for (const batch of dashboard.batches) { batch.allocatedQuantity = dashboard.allocations.filter((a: any) => a.batchId === batch.id && !a.reversedAt).reduce((sum: number, a: any) => sum + a.quantity, 0); batch.availableQuantity = batch.quantity - batch.allocatedQuantity; }
    for (const order of dashboard.orders) for (const item of order.lines) { item.allocatedQuantity = dashboard.allocations.filter((a: any) => a.orderLineId === item.orderLineId && !a.reversedAt).reduce((sum: number, a: any) => sum + a.quantity, 0); item.pendingAllocationQuantity = Math.max(0, item.quantity - item.allocatedQuantity); }
    dashboard.machines = [m1, m2].map((id, i) => { const assignedQuantity = dashboard.orders.flatMap((o: any) => o.lines).filter((l: any) => l.assignedMachineId === id).reduce((sum: number, l: any) => sum + l.quantity, 0);
      const batches = dashboard.batches.filter((b: any) => b.machineId === id), producedQuantity = batches.reduce((sum: number, b: any) => sum + b.quantity, 0);
      return { machineId: id, machineNumber: i + 1, machineName: `Máquina ${i + 1}`, isActive: true, assignedQuantity, producedQuantity, pendingQuantity: Math.max(0, assignedQuantity - producedQuantity), allocatedQuantity: batches.reduce((sum: number, b: any) => sum + b.allocatedQuantity, 0), availableQuantity: batches.reduce((sum: number, b: any) => sum + b.availableQuantity, 0) }; });
  }
  const plan = () => ({ date, routeCode: null, demandLabel: 'Por producir', preparationLabel: 'Por preparar', orders: dashboard.orders.map((o: any) => ({ ...o, status: 'Accepted' })), machineDemand: dashboard.orders.flatMap((o: any) => o.lines.map((l: any) => ({ machineId: l.assignedMachineId, machineNumber: l.assignedMachineNumber, machineName: l.assignedMachineName, productId: l.productId, productName: l.productName, unitCode: l.unitCode, quantity: l.quantity }))), warnings: [], pendingReview: [] });
  async function install(page: Page) {
    await page.route('**/api/admin/products', route => route.fulfill({ json: [
      { id: product, name: '#11 Demo', unitCode: 'kg', isActive: true, requiresMachine: true },
      { id: 'resale', name: 'Burreras de reventa', unitCode: 'kg', isActive: true, requiresMachine: false },
      { id: 'piece', name: 'Producto en piezas', unitCode: 'piece', isActive: true, requiresMachine: true }
    ] }));
    await page.route('**/api/admin/operations/**', async route => {
      const request = route.request(), url = new URL(request.url()), path = url.pathname;
      refresh();
      if (request.method() === 'GET') {
        if (path.endsWith('/plan')) { const value = plan(), routeCode = url.searchParams.get('routeCode'); value.orders = value.orders.filter((o: any) => !routeCode || o.routeCode === routeCode); value.machineDemand = value.orders.flatMap((o: any) => o.lines.map((l: any) => ({ machineId: l.assignedMachineId, machineNumber: l.assignedMachineNumber, machineName: l.assignedMachineName, productId: l.productId, productName: l.productName, unitCode: l.unitCode, quantity: l.quantity }))); return route.fulfill({ json: value }); }
        if (path.endsWith('/changes')) return route.fulfill({ json: { date, changes: [], pendingCount: 0 } });
        if (path.endsWith('/production')) { const routeCode = url.searchParams.get('routeCode'); return route.fulfill({ json: { ...dashboard, routeCode,
          orders: dashboard.orders.filter((o: any) => !routeCode || o.routeCode === routeCode), allocations: dashboard.allocations.filter((a: any) => !routeCode || dashboard.orders.some((o: any) => o.orderId === a.orderId && o.routeCode === routeCode)) } }); }
      }
      const body = request.postDataJSON(); writes.push({ path, body });
      if (operations.has(body.operationId)) return route.fulfill({ json: { ...operations.get(body.operationId), replayed: true } });
      let result: any = { id: `new-${writes.length}`, orderVersion: null, replayed: false };
      if (path.endsWith('/batches')) {
        dashboard.batches.push({ id: result.id, date, machineId: body.machineId, machineNumber: body.machineId === m1 ? 1 : 2, machineName: null,
          productId: body.productId, productName: '#11 Demo', unitCode: 'kg', quantity: body.quantity, allocatedQuantity: 0, availableQuantity: body.quantity, createdAt: `${date}T14:00:00Z` });
      } else if (path.endsWith('/allocations')) {
        const order = dashboard.orders.find((o: any) => o.orderId === body.orderId);
        dashboard.allocations.push({ id: result.id, batchId: body.batchId, orderId: body.orderId, orderLineId: body.orderLineId, customerName: order.customerName, quantity: body.quantity, createdAt: `${date}T14:05:00Z`, reversedAt: null, reverseReason: null });
        result.orderVersion = ++order.orderVersion;
      } else if (path.endsWith('/reverse')) {
        const allocation = dashboard.allocations.find((a: any) => a.id === path.split('/').at(-2)); allocation.reversedAt = `${date}T14:10:00Z`; allocation.reverseReason = body.reason;
        result.orderVersion = ++dashboard.orders.find((o: any) => o.orderId === allocation.orderId).orderVersion;
      } else if (path.endsWith('/machine')) {
        const id = path.split('/')[5], order = dashboard.orders.find((o: any) => o.orderId === id), item = order.lines.find((l: any) => l.orderLineId === path.split('/')[7]);
        item.assignedMachineId = body.machineId; item.assignedMachineNumber = body.machineId === m1 ? 1 : body.machineId === m2 ? 2 : null; result.orderVersion = ++order.orderVersion;
      }
      operations.set(body.operationId, result); refresh();
      if (path.endsWith('/batches') && loseFirstBatchResponse) { loseFirstBatchResponse = false; return route.abort('failed'); }
      if (path.endsWith('/allocations') && loseFirstAllocationResponse) { loseFirstAllocationResponse = false; return route.abort('failed'); }
      return route.fulfill({ json: result });
    });
  }
  refresh(); return { dashboard, writes, install, loseBatchResponse: () => { loseFirstBatchResponse = true; }, loseAllocationResponse: () => { loseFirstAllocationResponse = true; } };
}
async function login(page: Page) { await page.goto('/admin/login'); await page.getByRole('button', { name: 'Entrar', exact: true }).click(); await expect(page.getByRole('button', { name: 'Salir', exact: true })).toBeVisible(); }
async function board(page: Page, state: ReturnType<typeof fixture>) { await state.install(page); await login(page); await page.goto(`/admin/operacion?date=${date}`); await expect(page.getByTestId('preparation-order')).toHaveCount(3); }
const filters = (page: Page) => page.getByRole('region', { name: 'Máquinas a mostrar', exact: true });
test.beforeEach(async ({ request }) => { await request.post(`${api}/__test/reset`); });

test('filtros combinables mantienen totales globales, evitan duplicados y separan selección vacía', async ({ page }) => {
  const state = fixture(); await board(page, state);
  const initialSummary = await page.getByTestId('production-machine').allTextContents();
  await filters(page).getByRole('button', { name: /Máquina 2/ }).click();
  await expect(page.getByTestId('preparation-order')).toHaveCount(2);
  expect(await page.getByTestId('production-machine').allTextContents()).toEqual(initialSummary);
  await filters(page).getByRole('button', { name: /Sin máquina asignada/ }).click();
  await expect(page.getByTestId('preparation-order')).toHaveCount(1);
  await filters(page).getByRole('button', { name: /Máquina 1/ }).click();
  await expect(page.getByText('Selecciona al menos una opción para ver los pedidos.')).toBeVisible();
  await filters(page).getByRole('button', { name: 'Seleccionar todas' }).click();
  await expect(page.getByTestId('preparation-order')).toHaveCount(3);
  await expect(page.getByRole('button', { name: /No requiere máquina/ })).toHaveCount(0);
  await page.getByLabel('Código de ruta').fill('A'); await page.getByRole('button', { name: 'Consultar plan' }).click();
  await expect(page.getByTestId('preparation-order')).toHaveCount(2);
  expect(await page.getByTestId('production-machine').allTextContents()).toEqual(initialSummary);
});

test('asigna máquina, registra lote, distribuye kilos al cliente y revierte sin borrar producción', async ({ page }) => {
  const state = fixture(); await board(page, state);
  await page.getByRole('button', { name: 'Asignar máquina a Cliente Sin Asignar #11 Demo', exact: true }).click();
  let dialog = page.getByRole('dialog');
  await dialog.getByLabel('Máquina para este pedido').selectOption(m1); await dialog.getByLabel('Motivo de la asignación').fill('Carga del turno');
  await dialog.getByRole('button', { name: 'Guardar asignación', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await page.getByRole('button', { name: 'Registrar producción', exact: true }).click(); dialog = page.getByRole('dialog');
  await dialog.getByLabel('Máquina de producción').selectOption(m1); await dialog.getByLabel('Producto producido').selectOption(product);
  await expect(dialog.getByLabel('Producto producido').locator('option')).toHaveCount(2);
  await dialog.getByLabel('Kilos producidos').fill('15'); await dialog.getByLabel('Nota de producción (opcional)').fill('Lote del turno');
  await dialog.getByRole('button', { name: 'Guardar producción', exact: true }).click(); await expect(dialog).toHaveCount(0);
  const customer = page.getByTestId('preparation-order').filter({ hasText: 'Cliente Sin Asignar' });
  await customer.getByRole('button', { name: 'Asignar kilos a Cliente Sin Asignar #11 Demo', exact: true }).click(); dialog = page.getByRole('dialog');
  const batchId = state.dashboard.batches[1].id;
  await dialog.getByLabel('Lote disponible').selectOption(batchId); await dialog.getByLabel('Kilos a asignar al pedido').fill('15');
  await dialog.getByRole('button', { name: 'Asignar kilos', exact: true }).click(); await expect(dialog).toHaveCount(0);
  await expect(customer).toContainText('Asignado a pedido'); await expect(customer).toContainText('15 kg');
  await customer.getByText('Asignaciones del pedido', { exact: true }).click(); await customer.getByRole('button', { name: /Revertir asignación de 15 kilos/ }).click(); dialog = page.getByRole('dialog');
  await dialog.getByLabel('Motivo de la reversión').fill('Preparar otro pedido primero'); await dialog.getByRole('button', { name: 'Confirmar reversión' }).click();
  await expect(dialog).toHaveCount(0); await expect(customer).toContainText('Por preparar');
  expect(state.dashboard.batches[1].quantity).toBe(15); expect(state.dashboard.batches[1].availableQuantity).toBe(15);
  expect(state.writes.map(w => w.path.split('/').at(-1))).toEqual(['machine','batches','allocations','reverse']);
  expect(state.writes[2].body).toMatchObject({ orderId: 'order-three', orderLineId: 'line-three', expectedOrderVersion: 2, quantity: 15 });
});

test('respuesta perdida conserva operación y borrador para reintentar sin duplicar el lote', async ({ page }) => {
  const state = fixture(); await board(page, state); state.loseBatchResponse();
  await page.getByRole('button', { name: 'Registrar producción', exact: true }).click(); const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Máquina de producción').selectOption(m1); await dialog.getByLabel('Producto producido').selectOption(product); await dialog.getByLabel('Kilos producidos').fill('7');
  await dialog.getByRole('button', { name: 'Guardar producción', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('No se recibió confirmación');
  await expect(dialog.getByLabel('Kilos producidos')).toHaveValue('7'); await expect(dialog.getByLabel('Kilos producidos')).toBeDisabled();
  await expect(dialog.getByRole('button', { name: 'Cerrar', exact: true })).toBeDisabled();
  await expect(dialog.getByRole('button', { name: 'Cancelar', exact: true })).toBeDisabled();
  await page.keyboard.press('Escape'); await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Reintentar operación', exact: true }).click(); await expect(dialog).toHaveCount(0);
  expect(state.writes).toHaveLength(2); expect(state.writes[1].body).toEqual(state.writes[0].body); expect(state.dashboard.batches).toHaveLength(2);
});

test('editor conserva borrador ante refresco y conflicto sin emitir escritura automática', async ({ page }) => {
  const state = fixture(); await board(page, state); await page.clock.install();
  await page.getByRole('button', { name: 'Asignar kilos a Cliente Producción Uno #11 Demo', exact: true }).click(); const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Kilos a asignar al pedido').fill('8');
  let reads = 0; page.on('request', request => { if (request.url().includes('/operations/production?')) reads++; });
  await page.clock.fastForward('00:00:16'); await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
  await expect(dialog.getByLabel('Kilos a asignar al pedido')).toHaveValue('8'); expect(reads).toBe(0);
  let writes = 0;
  await page.route('**/api/admin/operations/production/allocations', route => { writes++; return route.fulfill({ status: 409, json: { error: 'El pedido cambió. Consulta su versión actual.', retrySameOperation: false } }); });
  await dialog.getByRole('button', { name: 'Asignar kilos', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('El pedido cambió'); await expect(dialog.getByLabel('Kilos a asignar al pedido')).toHaveValue('8');
  await expect(dialog.getByRole('button', { name: 'Reintentar operación', exact: true })).toBeDisabled(); expect(writes).toBe(1);
});

test('permiso de lectura no muestra escritura y concesión de producción aislada habilita su acción', async ({ page }) => {
  const state = fixture(); await state.install(page);
  let permissions = ['operations.read'];
  await page.route('**/api/admin/security/me', route => route.fulfill({ json: { userId: '92000000-0000-0000-0000-000000000002', permissions } }));
  await login(page); await page.goto(`/admin/operacion?date=${date}`); await expect(page.getByTestId('preparation-order')).toHaveCount(3);
  await expect(page.getByRole('button', { name: /Registrar producción|Asignar kilos|Asignar máquina a/ })).toHaveCount(0);
  permissions = ['operations.produce']; await page.reload(); await expect(page.getByRole('button', { name: 'Registrar producción', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: /Asignar kilos a|Asignar máquina a/ })).toHaveCount(0);
});

for (const width of [320,768,1440,1920]) {
  test(`producción usa ancho disponible sin desbordamiento a ${width}px e imprime el alcance visible`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 }); const state = fixture(); await board(page, state);
    await expect(page.getByTestId('preparation-order').first()).not.toContainText('Acceso:'); await expect(page.getByTestId('preparation-order').first()).not.toContainText('Cobro:');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    const columnCount = await page.locator('.production-order-grid').evaluate(element => getComputedStyle(element).gridTemplateColumns.split(' ').length);
    if (width >= 1440) expect(columnCount).toBe(3); if (width === 320) expect(columnCount).toBe(1);
    await page.screenshot({ path: test.info().outputPath(`production-${width}.png`), fullPage: true });
    await filters(page).getByRole('button', { name: /Máquina 2/ }).click(); await expect(page.getByTestId('preparation-order')).toHaveCount(2);
    await page.emulateMedia({ media: 'print' }); await expect(page.getByRole('button', { name: 'Imprimir plan', exact: true })).not.toBeVisible(); await expect(page.getByTestId('preparation-order')).toHaveCount(2); await expect(page.locator('.production-print-scope')).toContainText('Máquina 1'); await expect(page.locator('.production-print-scope')).not.toContainText('Máquina 2');
  });
}

test('asigna desde un lote a un cliente seleccionado sin superar su saldo', async ({ page }) => {
  const state = fixture(); state.dashboard.orders[2].lines[0].assignedMachineId = m1; state.dashboard.orders[2].lines[0].assignedMachineNumber = 1;
  await board(page, state);
  await page.getByRole('button', { name: 'Asignar lote de #11 Demo a pedido', exact: true }).click(); const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Pedido del cliente').selectOption('order-three:line-three');
  await expect(dialog).toContainText('Cliente Sin Asignar'); await expect(dialog).toContainText('Máximo disponible para este pedido: 10 kg');
  await dialog.getByLabel('Kilos a asignar al pedido').fill('11'); await dialog.getByRole('button', { name: 'Asignar kilos', exact: true }).click();
  expect(state.writes).toHaveLength(0);
  await dialog.getByLabel('Kilos a asignar al pedido').fill('6'); await dialog.getByRole('button', { name: 'Asignar kilos', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(state.writes[0].body).toMatchObject({ batchId: 'batch-initial', orderId: 'order-three', orderLineId: 'line-three', quantity: 6 });
  expect(state.dashboard.batches[0].quantity).toBe(20); expect(state.dashboard.batches[0].availableQuantity).toBe(4);
});

test('cerrar consulta de productos pendiente no bloquea otros editores', async ({ page }) => {
  const state = fixture(); await board(page, state);
  let release!: () => void;
  const responseReady = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/admin/products', async route => { await responseReady; await route.fulfill({ json: [] }); });
  await page.getByRole('button', { name: 'Registrar producción', exact: true }).click(); let dialog = page.getByRole('dialog');
  await expect(dialog.getByLabel('Producto producido')).toBeDisabled(); await dialog.getByRole('button', { name: 'Cerrar', exact: true }).click();
  await page.getByRole('button', { name: 'Asignar máquina a Cliente Sin Asignar #11 Demo', exact: true }).click(); dialog = page.getByRole('dialog');
  await dialog.getByLabel('Máquina para este pedido').selectOption(m1); await dialog.getByLabel('Motivo de la asignación').fill('Asignación verificada');
  await expect(dialog.getByRole('button', { name: 'Guardar asignación', exact: true })).toBeEnabled();
  await dialog.getByRole('button', { name: 'Guardar asignación', exact: true }).click(); await expect(dialog).toHaveCount(0); release();
  expect(state.writes).toHaveLength(1);
});


for (const kind of ['batch', 'allocation'] as const) {
  test(`F01 ${kind}: recupera tras recargar con la misma referencia y sin duplicados`, async ({ page }) => {
    const state = fixture(); await board(page, state);
    if (kind === 'batch') {
      state.loseBatchResponse(); await page.getByRole('button', { name: 'Registrar producción', exact: true }).click();
      const dialog = page.getByRole('dialog'); await dialog.getByLabel('Máquina de producción').selectOption(m1);
      await dialog.getByLabel('Producto producido').selectOption(product); await dialog.getByLabel('Kilos producidos').fill('7');
      await dialog.getByRole('button', { name: 'Guardar producción', exact: true }).click();
    } else {
      state.loseAllocationResponse(); await page.getByRole('button', { name: 'Asignar kilos a Cliente Producción Uno #11 Demo', exact: true }).click();
      const dialog = page.getByRole('dialog'); await dialog.getByLabel('Kilos a asignar al pedido').fill('7');
      await dialog.getByRole('button', { name: 'Asignar kilos', exact: true }).click();
    }
    await expect(page.getByRole('dialog').getByRole('alert')).toContainText('No se recibió confirmación');
    const first = structuredClone(state.writes[0].body);
    page.on('dialog', dialog => dialog.accept()); await page.reload();
    const recovery = page.getByTestId('production-recovery'); await expect(recovery).toContainText(first.operationId);
    await expect(page.getByRole('button', { name: 'Registrar producción', exact: true })).toBeDisabled();
    expect(state.writes).toHaveLength(1);
    await recovery.getByRole('button', { name: 'Recuperar resultado', exact: true }).click();
    await expect(recovery).toHaveCount(0); expect(state.writes).toHaveLength(2); expect(state.writes[1].body).toEqual(first);
    expect(kind === 'batch' ? state.dashboard.batches : state.dashboard.allocations).toHaveLength(2);
  });
}

test('F01 salir de sesión conserva el pendiente sólo para la misma cuenta', async ({ page }) => {
  const state = fixture(); await board(page, state); state.loseBatchResponse();
  await page.getByRole('button', { name: 'Registrar producción', exact: true }).click();
  const dialog = page.getByRole('dialog'); await dialog.getByLabel('Máquina de producción').selectOption(m1);
  await dialog.getByLabel('Producto producido').selectOption(product); await dialog.getByLabel('Kilos producidos').fill('7');
  await dialog.getByRole('button', { name: 'Guardar producción', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('No se recibió confirmación');
  await dialog.getByRole('button', { name: 'Cerrar y conservar pendiente', exact: true }).click();
  await page.getByRole('button', { name: 'Salir', exact: true }).click(); await login(page);
  await page.goto(`/admin/operacion?date=${date}`); await expect(page.getByTestId('production-recovery')).toBeVisible();
  await page.route('**/api/admin/security/me', route => route.fulfill({ json: { userId: 'another-admin', permissions: ['operations.read', 'operations.produce'] } }));
  page.on('dialog', dialog => dialog.accept()); await page.reload();
  await expect(page.getByTestId('preparation-order')).toHaveCount(3); await expect(page.getByTestId('production-recovery')).toHaveCount(0);
  expect(state.writes).toHaveLength(1);
});

test('F01 almacenamiento bloqueado impide enviar sin referencia durable', async ({ page }) => {
  await page.addInitScript(() => {
    const original = Storage.prototype.setItem;
    Storage.prototype.setItem = function(key, value) {
      if (key.startsWith('prodimt.production.pending.')) throw new DOMException('Storage blocked', 'QuotaExceededError');
      original.call(this, key, value);
    };
  });
  const state = fixture(); await board(page, state);
  await page.getByRole('button', { name: 'Registrar producción', exact: true }).click();
  const dialog = page.getByRole('dialog'); await dialog.getByLabel('Máquina de producción').selectOption(m1);
  await dialog.getByLabel('Producto producido').selectOption(product); await dialog.getByLabel('Kilos producidos').fill('7');
  await dialog.getByRole('button', { name: 'Guardar producción', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('No se envió una nueva operación'); expect(state.writes).toHaveLength(0);
});

test('F09 revisión conserva la fecha del plan consultado', async ({ page }) => {
  const state = fixture(); await state.install(page);
  await page.route('**/api/admin/operations/plan?**', async route => {
    const url = new URL(route.request().url());
    await route.fulfill({ json: { date: url.searchParams.get('date'), routeCode: null, orders: [], machineDemand: [], warnings: [], pendingReview: [
      { orderId: 'pending-one', customerName: 'Cliente de prueba', sequenceNumber: 1 }
    ] } });
  });
  await page.route('**/api/admin/operations/production?**', route => route.fulfill({ json: { date: '2026-10-30', orders: [], batches: [], machines: [], allocations: [] } }));
  await login(page); await page.goto('/admin/operacion?date=2026-10-30');
  const link = page.getByRole('link', { name: 'Ir a revisión administrativa' });
  await expect(link).toHaveAttribute('href', '/admin/pendientes?date=2026-10-30');
  await link.click(); await expect(page).toHaveURL(/\/admin\/pendientes\?date=2026-10-30$/);
});
