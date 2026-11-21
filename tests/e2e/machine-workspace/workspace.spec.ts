import { expect, test, type Page } from '@playwright/test';

const date = '2026-11-21';
const machineId = '44444444-4444-4444-4444-444444444401';
const userId = '92000000-0000-0000-0000-000000000002';
const productId = '22222222-2222-2222-2222-222222222201';
const machine = { id: machineId, number: 1, name: 'Máquina 1' };
const otherMachine = { id: '44444444-4444-4444-4444-444444444402', number: 2, name: 'Máquina 2' };

function fixture() {
  const order = (id: number, customerId: string, customerName: string, quantity: number, allocated: number) => ({
    orderId: '33333333-3333-3333-3333-' + String(id).padStart(12, '0'),
    customerId, customerName, sequenceNumber: id, orderVersion: 1, routeCode: 'R1', routeStopOrder: id,
    lines: [{
      orderLineId: '77777777-7777-7777-7777-' + String(id).padStart(12, '0'),
      productId, productName: id === 2 ? 'Tortilla amarilla' : 'Tortilla blanca', unitCode: 'kg', quantity,
      assignedMachineId: machineId, assignedMachineNumber: 1, assignedMachineName: 'Máquina 1',
      allocatedQuantity: allocated, pendingQuantity: quantity - allocated, pendingAllocationQuantity: quantity - allocated,
      releasedQuantity: 0, notes: null
    }]
  });
  const orders: any[] = [
    order(1, 'customer-one', 'Cocina San Luis', 5, 2),
    order(2, 'customer-one', 'Cocina San Luis', 4, 4),
    order(3, 'customer-two', 'Taquería El Portal', 10, 0)
  ];
  const workers = ['Mario Ríos', 'Elena Soto', 'Luis Vega', 'Ana Cruz', 'Jorge Peña'].map((name, index) =>
    ({ id: '18180000-0000-0000-0000-' + String(index + 1).padStart(12, '0'), name, isActive: true }));
  const slots: any[] = ['Maquinista', 'Recolector 1', 'Recolector 2', 'Empaquetador'].map((label, index) => ({
    position: index + 1, label, regularWorkerId: workers[index].id, regularWorkerName: workers[index].name,
    workerId: workers[index].id, workerName: workers[index].name, status: 'regular'
  }));
  const future: Record<string, any[]> = {};
  const batches: any[] = [{ id: '55555555-5555-5555-5555-555555555501', productId, productName: 'Tortilla blanca',
    quantity: 3, allocatedQuantity: 0, availableQuantity: 3, handedOffQuantity: 1, createdAt: date + 'T12:00:00Z' }];
  const writes: Array<{ action: string; body: any }> = [], notices: any[] = [];
  const receipts = new Map<string, any>();
  let loseCapture = false;
  let version = 1;
  const summary = () => {
    const lines = orders.flatMap(item => item.lines);
    return {
      producedQuantity: lines.reduce((total, item) => total + item.allocatedQuantity, 0) + batches.reduce((total, item) => total + item.availableQuantity, 0),
      allocatedQuantity: lines.reduce((total, item) => total + item.allocatedQuantity, 0),
      pendingQuantity: lines.reduce((total, item) => total + item.pendingQuantity, 0),
      availableQuantity: batches.reduce((total, item) => total + item.availableQuantity, 0)
    };
  };
  async function install(page: Page) {
    await page.route('**/api/admin/security/me', route => route.fulfill({ json: { userId, permissions: ['operations.machine.work', 'operations.read'] } }));
    await page.route('**/api/admin/machine-workspace/**', async route => {
      const request = route.request(), url = new URL(request.url()), action = url.pathname.split('/').at(-1)!;
      if (request.method() === 'GET') {
        if (action === 'machines') return route.fulfill({ json: [machine] });
        if (action === 'team') {
          const selectedDate = url.searchParams.get('date') || date;
          return route.fulfill({ json: {
            machineId, date: selectedDate, today: date, version: 'team-' + version, effectiveDate: date,
            slots: future[selectedDate] ?? slots, workers, destinations: [otherMachine],
            canManageWorkers: false, canEditDaily: selectedDate === date, history: []
          } });
        }
        return route.fulfill({ json: { date, machine, totals: summary(), orders, batches,
          products: [{ id: productId, name: 'Tortilla blanca' }], notices } });
      }
      const body = request.postDataJSON();
      writes.push({ action, body });
      if (receipts.has(body.operationId)) return route.fulfill({ json: { ...receipts.get(body.operationId), replayed: true } });
      if (action === 'capture' || action === 'allocate') {
        const selected = orders.find(item => item.orderId === body.orderId), line = selected.lines.find((item: any) => item.orderLineId === body.orderLineId);
        line.allocatedQuantity += body.quantity;
        line.pendingQuantity -= body.quantity;
        line.pendingAllocationQuantity = line.pendingQuantity;
        selected.orderVersion++;
        if (action === 'allocate') {
          const lot = batches.find(item => item.id === body.batchId);
          lot.availableQuantity -= body.quantity;
          lot.allocatedQuantity += body.quantity;
        }
      }
      if (action === 'release') {
        const selected = orders.find(item => item.orderId === body.orderId);
        for (const line of selected.lines.filter((item: any) => !body.orderLineId || item.orderLineId === body.orderLineId)) {
          const quantity = body.quantity ?? line.quantity - line.releasedQuantity;
          line.releasedQuantity += quantity;
          notices.push({ id: 'notice-' + notices.length, machineId, machineNumber: 1, machineName: 'Máquina 1',
            orderId: selected.orderId, orderLineId: line.orderLineId, customerName: selected.customerName, productName: line.productName,
            quantity, createdAt: date + 'T13:30:00Z' });
        }
      }
      if (action === 'lots') {
        batches.push({ id: 'new-lot', productId: body.productId, productName: 'Tortilla blanca', quantity: body.quantity,
          allocatedQuantity: 0, availableQuantity: body.quantity, handedOffQuantity: 0, createdAt: date + 'T13:20:00Z' });
      }
      if (action === 'handoff') batches.find(item => item.id === body.batchId).handedOffQuantity += body.quantity;
      if (action === 'daily') {
        const slot = slots.find(item => item.position === body.position);
        if (body.action === 'cover') {
          slot.workerId = body.workerId; slot.workerName = workers.find(item => item.id === body.workerId)!.name; slot.status = 'covered';
        } else {
          slot.workerId = null; slot.workerName = null; slot.status = body.action === 'move' ? 'moved-out' : 'absent';
        }
        version++;
      }
      if (action === 'regular') {
        future[body.effectiveDate] = slots.map(slot => {
          const value = body.slots.find((item: any) => item.position === slot.position).workerId;
          const person = workers.find(item => item.id === value);
          return { ...slot, regularWorkerId: value, regularWorkerName: person?.name ?? null,
            workerId: value, workerName: person?.name ?? null, status: person ? 'regular' : 'vacant' };
        });
        version++;
      }
      const result = { id: body.operationId, orderVersion: null, replayed: false };
      receipts.set(body.operationId, result);
      if (action === 'capture' && loseCapture) { loseCapture = false; return route.abort('failed'); }
      return route.fulfill({ json: result });
    });
    await page.route('**/api/admin/operations/**', async route => {
      const action = new URL(route.request().url()).pathname.split('/').at(-1)!;
      if (action === 'changes') return route.fulfill({ json: { date, pendingCount: 0, changes: [] } });
      if (action === 'plan') return route.fulfill({ json: { date, routeCode: null, demandLabel: 'Por producir', preparationLabel: 'Por preparar',
        orders: orders.map(item => ({ ...item, status: 'Accepted' })), machineDemand: [], pendingReview: [], warnings: [] } });
      return route.fulfill({ json: { date, routeCode: null, summaryScope: 'all-routes', orders,
        machines: [{ machineId, machineName: 'Máquina 1', machineNumber: 1, assignedQuantity: 19,
          producedQuantity: summary().producedQuantity, pendingQuantity: summary().pendingQuantity,
          allocatedQuantity: summary().allocatedQuantity, availableQuantity: summary().availableQuantity }],
        batches: [], allocations: [], releases: notices } });
    });
  }
  return { install, workers, writes, notices, orders, batches, receipts, loseCaptureResponse: () => { loseCapture = true; } };
}

async function enter(page: Page, state: ReturnType<typeof fixture>) {
  await state.install(page);
  await page.goto('/admin/login');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Salir', exact: true })).toBeVisible();
  await page.goto('/admin/maquina');
  await expect(page.getByTestId('machine-customer')).toHaveCount(2);
}

test('respuesta perdida y recarga recuperan la misma captura; libera una partida del pedido correcto', async ({ page }) => {
  const state = fixture();
  await enter(page, state);
  state.loseCaptureResponse();
  await page.getByRole('button', { name: 'Registrar kilos para Cocina San Luis' }).click();
  await page.getByLabel('Kilos', { exact: true }).fill('1,25');
  await page.getByRole('button', { name: 'Guardar', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Reintentar operación', exact: true })).toBeVisible();
  page.once('dialog', dialog => dialog.accept());
  await page.reload();
  await page.getByRole('button', { name: 'Reintentar operación', exact: true }).click();
  await expect(page.getByText('Kilos registrados para el pedido.', { exact: true })).toBeVisible();
  expect(state.writes[1].body).toEqual(state.writes[0].body);
  expect(state.orders[0].lines[0].allocatedQuantity).toBe(3.25);
  await page.getByRole('button', { name: 'Vista rápida de Cocina San Luis' }).click();
  await expect(page.getByText('Pedido #2', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Liberar kilos de Tortilla blanca' }).click();
  await page.getByLabel('Kilos', { exact: true }).fill('1');
  await page.getByRole('button', { name: 'Guardar', exact: true }).click();
  await expect(page.getByText('Producción liberada para Embarques.', { exact: true })).toBeVisible();
  expect(state.writes.at(-1)!.body.orderId).toBe(state.orders[0].orderId);
  expect(state.writes.at(-1)!.body.quantity).toBe(1);
});

test('registra lote sin cliente y sólo entrega el saldo que sigue en máquina', async ({ page }) => {
  const state = fixture();
  await enter(page, state);
  await page.getByRole('button', { name: 'Lotes sin cliente', exact: true }).click();
  await page.getByRole('button', { name: 'Registrar lote', exact: true }).click();
  await page.getByRole('combobox', { name: 'Producto', exact: true }).selectOption(productId);
  await page.getByLabel('Kilos', { exact: true }).fill('2,5');
  await page.getByRole('button', { name: 'Guardar', exact: true }).click();
  await expect(page.getByText('Lote sin cliente registrado.', { exact: true })).toBeVisible();
  expect(state.writes[0].body.date).toBe(date);
  await page.getByRole('button', { name: 'Lotes sin cliente', exact: true }).click();
  await page.getByRole('button', { name: 'Entregar a Embarques', exact: true }).first().click();
  await page.getByLabel('Kilos', { exact: true }).fill('3');
  await page.getByRole('button', { name: 'Guardar', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('hasta 2 kg');
  expect(state.writes).toHaveLength(1);
  await page.getByLabel('Kilos', { exact: true }).fill('2');
  await page.getByRole('button', { name: 'Guardar', exact: true }).click();
  await expect(page.getByText('Entrega del lote a Embarques registrada.', { exact: true })).toBeVisible();
  expect(state.batches[0].handedOffQuantity).toBe(3);
});

test('ajusta ausencia, suplencia y traslado del día; programa plantilla para mañana', async ({ page }) => {
  const state = fixture();
  await enter(page, state);
  await page.getByRole('button', { name: 'Configurar equipo', exact: true }).click();
  await page.getByRole('button', { name: 'Ajustar Recolector 2', exact: true }).click();
  await page.getByRole('button', { name: 'Guardar ajuste', exact: true }).click();
  await expect(page.getByText('3/4 plazas cubiertas', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Ajustar Recolector 2', exact: true }).click();
  await page.getByRole('combobox', { name: 'Suplente', exact: true }).selectOption(state.workers[4].id);
  await page.getByRole('button', { name: 'Guardar ajuste', exact: true }).click();
  await expect(page.getByText('4/4 plazas cubiertas', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Ajustar Recolector 1', exact: true }).click();
  await page.getByRole('combobox', { name: 'Movimiento', exact: true }).selectOption('move');
  await page.getByRole('combobox', { name: 'Máquina de destino', exact: true }).selectOption(otherMachine.id);
  await page.getByRole('button', { name: 'Guardar ajuste', exact: true }).click();
  await expect(page.getByText('3/4 plazas cubiertas', { exact: true })).toBeVisible();
  expect(state.writes.at(-1)!.body.otherMachineId).toBe(otherMachine.id);
  expect(state.writes.at(-1)!.body.expectedVersion).toBe('team-3');
  await page.getByRole('button', { name: 'Equipo fijo', exact: true }).click();
  await page.getByLabel('Empaquetador', { exact: true }).selectOption(state.workers[2].id);
  await page.getByRole('button', { name: 'Guardar equipo fijo', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Una persona solo puede cubrir una plaza');
  await page.getByLabel('Empaquetador', { exact: true }).selectOption('');
  await page.getByRole('button', { name: 'Guardar equipo fijo', exact: true }).click();
  await expect(page.getByText('Equipo fijo actualizado.', { exact: true })).toBeVisible();
  expect(state.writes.at(-1)!.body.effectiveDate).toBe('2026-11-22');
});

test('Operación recibe avisos agrupados con el refresco y sin interrumpir la vista', async ({ page }) => {
  const state = fixture();
  await enter(page, state);
  await page.clock.install();
  await page.goto('/admin/operacion?date=' + date);
  await expect(page.getByTestId('preparation-order')).toHaveCount(3);
  await expect(page.getByTestId('production-releases')).toHaveCount(0);
  state.notices.push({ id: 'release-one', machineId, machineNumber: 1, machineName: 'Máquina 1',
    orderId: state.orders[0].orderId, orderLineId: state.orders[0].lines[0].orderLineId,
    customerName: 'Cocina San Luis', productName: 'Tortilla blanca', quantity: 2.5, createdAt: date + 'T12:30:00Z' });
  await page.clock.fastForward('00:00:16');
  const feed = page.getByTestId('production-releases');
  await expect(feed).toContainText('2.5 kg liberados');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await feed.getByText('Máquina 1', { exact: true }).click();
  await expect(feed.getByText(/Cocina San Luis · Tortilla blanca · 2.5 kg/)).toBeVisible();
});

for (const width of [320, 390, 768, 1440]) {
  test('una fila por cliente con columnas alineadas a ' + width + ' px', async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await enter(page, fixture());
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    const positions = await page.locator('.machine-row').evaluateAll(rows => rows.map(row =>
      [...row.querySelectorAll<HTMLElement>('.machine-numeric')].find(cell => cell.offsetWidth)?.getBoundingClientRect().right));
    expect(positions.every(value => value !== undefined && Math.abs(value - positions[0]!) < 1)).toBe(true);
    await page.getByRole('button', { name: 'Configurar equipo', exact: true }).click();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.getByRole('button', { name: 'Vista rápida de Cocina San Luis' }).click();
    await expect(page.getByText('Pedido #2', { exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
