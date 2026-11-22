import { expect, test, type Page } from '@playwright/test';
import { deliveryFixture, driverId, partialAssignmentId, deliveryDate, deliveryTripId } from '../delivery-fixture';

const preview = (pricePending = false) => ({
  paymentTermCode: 'cash_on_delivery', customerId: 'customer-delivery-one', customerName: 'Cliente Reparto Uno',
  ticketLines: [{ orderId: 'order-one', orderLineId: 'line-one', productName: 'Tortilla demo', unitCode: 'kg',
    quantity: 50, unitPrice: pricePending ? null : 10, amount: pricePending ? null : 500 }],
  ticketTotal: pricePending ? null : 500, priorBalance: 300, collectedAmount: 0, suggestedAmount: pricePending ? null : 800,
  amountDue: pricePending ? null : 800, balance: pricePending ? null : 800, creditBalance: pricePending ? null : 0,
  version: 'collection-v1', currencyCode: 'MXN', pricePending
});

async function login(page: Page) {
  await page.goto('/admin/login');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Salir', exact: true })).toBeVisible();
}

async function driver(page: Page, options: { pendingPrice?: boolean; credit?: boolean } = {}) {
  const state = deliveryFixture();
  state.setPermissions(['delivery.read.own', 'delivery.operate.own', 'operations.collections.work']);
  if (options.credit) state.trip.assignments[0].paymentTermCode = 'credit';
  await state.install(page);
  const previews: number[] = [];
  await page.route('**/assignments/*/collection-preview?*', route => {
    previews.push(Number(new URL(route.request().url()).searchParams.get('quantity')));
    return route.fulfill({ json: preview(options.pendingPrice) });
  });
  await page.route('**/trips/*/collections', route => route.fulfill({ json: {
    tripId: deliveryTripId, customers: [{ customerId: 'customer-delivery-one', customerName: 'Cliente Reparto Uno', chargeAmount: 500,
      paidAmount: 400, balance: 400, pricePending: false }], chargeAmount: 500, paidAmount: 400, pendingCashAmount: 400, pricePending: false
  } }));
  await login(page);
  await page.goto('/reparto?date=' + deliveryDate);
  await page.getByRole('button', { name: 'Registrar entrega', exact: true }).click();
  await page.getByRole('button', { name: 'Registrar entrega para Cliente Reparto Uno', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Registrar entrega', exact: true });
  return { state, dialog, previews };
}

for (const amount of [400, 600, 800]) {
  test('ticket de 500 más deuda de 300 admite pago editable de ' + amount, async ({ page }) => {
    const { state, dialog } = await driver(page);
    await expect(dialog.getByLabel('Importe cobrado (MXN)', { exact: true })).toHaveValue('800');
    await expect(dialog.getByTestId('delivery-collection')).toContainText('Ticket del día');
    await dialog.getByText('Ver desglose del ticket', { exact: true }).click();
    await expect(dialog.getByRole('table')).toContainText('50 kg');
    await dialog.getByLabel('Importe cobrado (MXN)', { exact: true }).fill(String(amount));
    await expect(dialog).toContainText('Saldo después del cobro');
    await dialog.getByRole('button', { name: 'Guardar entrega', exact: true }).click();
    await expect(dialog).not.toBeVisible();
    expect(state.writes[0].body).toMatchObject({ quantity: 25, collectedAmount: amount, collectionVersion: 'collection-v1' });
  });
}

test('No pagó registra cero y un precio pendiente solicita importe explícito sin precarga', async ({ page }) => {
  const { state, dialog } = await driver(page, { pendingPrice: true });
  await expect(dialog.getByLabel('Importe cobrado (MXN)', { exact: true })).toHaveValue('');
  await expect(dialog).toContainText('Falta precio para calcular');
  await dialog.getByRole('button', { name: 'Guardar entrega', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('importe cobrado');
  expect(state.writes).toHaveLength(0);
  await dialog.getByRole('button', { name: 'No pagó', exact: true }).click();
  await dialog.getByRole('button', { name: 'Guardar entrega', exact: true }).click();
  await expect(dialog).not.toBeVisible();
  expect(state.writes[0].body.collectedAmount).toBe(0);
});

test('cliente de crédito conserva la entrega y no ofrece el campo de cobro', async ({ page }) => {
  const { state, dialog, previews } = await driver(page, { credit: true });
  await expect(dialog.getByTestId('delivery-collection')).toHaveCount(0);
  await dialog.getByRole('button', { name: 'Guardar entrega', exact: true }).click();
  await expect(dialog).not.toBeVisible();
  expect(previews).toEqual([]);
  expect(state.writes[0].body).not.toHaveProperty('collectedAmount');
});

test('respuesta perdida conserva la entrega y su pago al recargar, y el resumen suma por cliente', async ({ page }) => {
  const { state, dialog } = await driver(page);
  state.loseNextDeliveryResponse();
  await dialog.getByLabel('Importe cobrado (MXN)', { exact: true }).fill('400');
  await dialog.getByRole('button', { name: 'Guardar entrega', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('confirmación');
  const command = state.writes[0].body;
  expect(await page.evaluate(() => Object.keys(localStorage).filter(key => key.startsWith('prodimt.delivery.collection.pending.')).length)).toBe(1);
  page.once('dialog', prompt => prompt.accept());
  await page.reload();
  const pending = page.getByRole('region', { name: 'Operación sin confirmación', exact: true });
  await pending.getByRole('button', { name: 'Reintentar operación', exact: true }).click();
  await expect(pending).toHaveCount(0);
  expect(state.writes[1].body).toEqual(command);
  await page.getByRole('button', { name: 'Cobranza de ruta', exact: true }).click();
  const summary = page.getByRole('dialog', { name: 'Cobranza de ruta', exact: true });
  await expect(summary.getByRole('table')).toContainText('Cliente Reparto Uno');
  await expect(summary.getByRole('table')).toContainText('$400.00');
});

async function cashier(page: Page) {
  const receiptRows: any[] = [];
  const trip: any = { tripId: deliveryTripId, date: deliveryDate, routeCode: 'A', routeName: 'Ruta A', tripNumber: 1,
    driverUserId: driverId, driverName: 'Chofer del día', departedAt: deliveryDate + 'T14:00:00Z', status: 'Departed',
    expectedAmount: 500, accountedAmount: 0, receivedAmount: 0, pendingAmount: 500, difference: 0, version: 'cash-v1', receipts: receiptRows };
  const customer = { customerId: 'customer-delivery-one', customerName: 'Cliente Reparto Uno', dayChargeAmount: 500,
    dayPaidAmount: 400, balance: 400, creditBalance: 0, pricePending: false };
  const writes: any[] = [], receipts = new Set<string>();
  let loseResponse = false;
  await page.route('**/api/admin/security/me', route => route.fulfill({ json: {
    userId: 'cashier-one', permissions: ['operations.collections.receive', 'operations.collections.read']
  } }));
  await page.route('**/api/admin/collections/**', async route => {
    const request = route.request(), url = new URL(request.url());
    if (url.pathname.endsWith('/cashier')) return route.fulfill({ json: { date: deliveryDate, routeCode: null, trips: [trip],
      expectedAmount: 500, receivedAmount: trip.receivedAmount, pendingAmount: trip.pendingAmount,
      difference: trip.difference, myReceivedAmount: trip.receivedAmount, customers: [customer] } });
    if (url.pathname.includes('/trips/')) return route.fulfill({ json: { tripId: deliveryTripId,
      customers: [{ customerId: customer.customerId, customerName: customer.customerName, chargeAmount: 500, paidAmount: 500,
        balance: 300, pricePending: false }], chargeAmount: 500, paidAmount: 500, pendingCashAmount: 500, pricePending: false } });
    if (request.method() === 'GET') return route.fulfill({ json: {
      customerId: customer.customerId, customerName: customer.customerName, chargesTotal: 800, paidTotal: 400,
      balance: 400, creditBalance: 0, pricePending: false, allocationPolicy: 'service-day-then-oldest-debt-then-credit',
      charges: [{ id: 'charge-one', date: deliveryDate, productName: 'Tortilla demo', unitCode: 'kg', requestedQuantity: 50,
        quantity: 50, isCredit: false, unitPrice: 10, amount: 500, routeCode: 'A', tripNumber: 1, driverName: 'Chofer del día', recorded: true }],
      payments: [{ id: 'payment-one', amount: 400, createdAt: deliveryDate + 'T15:00:00Z', actorName: 'Chofer del día',
        driverName: 'Chofer del día', serviceDate: deliveryDate, routeCode: 'A', tripNumber: 1 }]
    } });
    const body = request.postDataJSON();
    writes.push(body);
    if (!receipts.has(body.operationId)) {
      receipts.add(body.operationId);
      trip.accountedAmount += body.declaredAmount;
      trip.receivedAmount += body.receivedAmount;
      trip.pendingAmount -= body.declaredAmount;
      trip.difference += body.declaredAmount - body.receivedAmount;
      receiptRows.push({ id: body.operationId, createdAt: deliveryDate + 'T18:00:00Z', receiverName: 'Mostrador de prueba',
        expectedAmount: body.declaredAmount, receivedAmount: body.receivedAmount, difference: body.declaredAmount - body.receivedAmount, reason: body.reason });
    }
    if (loseResponse) { loseResponse = false; return route.abort('failed'); }
    return route.fulfill({ json: { id: body.operationId } });
  });
  await login(page);
  await page.goto('/admin/caja');
  await expect(page.getByTestId('cash-trip')).toHaveCount(1);
  return { writes, trip, loseNextResponse: () => { loseResponse = true; } };
}

test('Mostrador registra diferencia, recupera recepción perdida y consulta historial y adeudos', async ({ page }) => {
  const state = await cashier(page);
  state.loseNextResponse();
  await page.getByRole('button', { name: 'Recibir efectivo', exact: true }).click();
  let dialog = page.getByRole('dialog', { name: 'Recibir efectivo', exact: true });
  await dialog.getByLabel('Efectivo recibido (MXN)', { exact: true }).fill('450');
  await dialog.getByRole('button', { name: 'Confirmar recepción', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('motivo');
  await dialog.getByLabel(/Motivo de la diferencia/).click();
  await dialog.getByLabel(/Motivo de la diferencia/).pressSequentially('Faltaron 50 pesos al contar');
  await expect(dialog.getByLabel(/Motivo de la diferencia/)).toHaveValue('Faltaron 50 pesos al contar');
  await dialog.getByRole('button', { name: 'Confirmar recepción', exact: true }).click();
  await expect(dialog.getByRole('button', { name: 'Reintentar recepción', exact: true })).toBeVisible();
  page.once('dialog', prompt => prompt.accept());
  await page.reload();
  await page.getByRole('button', { name: 'Reintentar recepción', exact: true }).click();
  await expect(page.getByText('Efectivo recibido. La hora y el receptor quedaron registrados.', { exact: true })).toBeVisible();
  expect(state.writes[1]).toEqual(state.writes[0]);
  expect(state.trip.receipts).toHaveLength(1);
  await expect(page.getByRole('region', { name: 'Totales de caja', exact: true })).toContainText('$450.00');
  await page.getByRole('button', { name: 'Cobros', exact: true }).click();
  const cobros = page.getByRole('dialog', { name: 'Cobros de la vuelta', exact: true });
  await expect(cobros).toContainText('Cliente Reparto Uno');
  await cobros.getByRole('button', { name: 'Cerrar', exact: true }).click();
  await page.getByRole('button', { name: 'Recibos', exact: true }).click();
  dialog = page.getByRole('dialog', { name: 'Recepciones registradas', exact: true });
  await expect(dialog).toContainText('Mostrador de prueba');
  await expect(dialog).toContainText('Faltaron 50 pesos');
  await dialog.getByRole('button', { name: 'Cerrar', exact: true }).click();
  await page.getByRole('button', { name: 'Adeudos por cliente', exact: true }).click();
  await expect(page.getByTestId('cash-customer')).toContainText('$400.00');
  await page.getByRole('button', { name: 'Ver cuenta', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Cuenta del cliente', exact: true })).toContainText('Tortilla demo');
});

for (const width of [320, 1440]) {
  test('Caja y cobro caben a ' + width + ' px', async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await cashier(page);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.getByRole('button', { name: 'Adeudos por cliente', exact: true }).click();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.getByRole('button', { name: 'Ver cuenta', exact: true }).click();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
