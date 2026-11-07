import { expect, test, type Page } from '@playwright/test';
import { deliveryDate, deliveryFixture, driverId } from '../delivery-fixture';

const api = 'http://127.0.0.1:5088';
const canonicalRoleId = '22220000-0000-0000-0000-000000000001';
const legacyRoleId = '91000000-0000-0000-0000-000000000099';
const prepareOrders = (page: Page) => page.getByTestId('orders-delivery-summary');
const navigation = (page: Page) => page.getByRole('navigation', { name: 'Navegacion principal', exact: true });

async function login(page: Page) {
  await page.goto('/admin/login');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Salir', exact: true })).toBeVisible();
}

/** Share fictional order state across the existing review and delivery HTTP contracts. */
async function integrationFixture(page: Page, permissions: string[], isSystemAdministrator = false) {
  const state = deliveryFixture(); state.setPermissions(permissions);
  const reads: Array<{ path: string; date: string | null }> = [];
  const reviews: Array<{ path: string; body: any }> = [];
  const deliveryReads: string[] = [];
  state.orders[0].status = 'Submitted'; state.orders[1].status = 'Accepted';
  for (const [index, status] of ['PendingAdminReview', 'NoOrder', 'Rejected', 'Cancelled', 'FutureState'].entries()) {
    const original = state.orders[0];
    state.orders.push({ ...original, orderId: `integration-order-${index}`, customerId: `integration-customer-${index}`,
      customerName: `Cliente ${status}`, sequenceNumber: index + 3, status,
      lines: [{ ...original.lines[0], orderLineId: `integration-line-${index}`, requestedQuantity: 12,
        deliveredQuantity: 0, reservedQuantity: 0, availableQuantity: 12 }] });
  }
  const rows = () => [...state.orders.map(order => ({ orderId: order.orderId, customerId: order.customerId,
    customerName: order.customerName, sequenceNumber: order.sequenceNumber, orderDate: deliveryDate,
    status: order.status, submittedAt: `${deliveryDate}T14:00:00Z`, requestedDeliveryTime: '09:30:00',
    requestedDeliveryWindowStart: null, requestedDeliveryWindowEnd: null, isLate: false,
    requiresAdminReview: order.status === 'PendingAdminReview', adminReviewReason: null, adminDecision: null })),
    { orderId: 'counter-order', customerId: null, customerName: 'Mostrador de ejemplo', sequenceNumber: 8,
      orderDate: deliveryDate, status: 'Submitted', submittedAt: `${deliveryDate}T14:00:00Z`,
      requestedDeliveryTime: null, requestedDeliveryWindowStart: null, requestedDeliveryWindowEnd: null,
      isLate: false, requiresAdminReview: false, adminReviewReason: null, adminDecision: null }];
  await state.install(page, isSystemAdministrator);
  page.on('request', request => {
    const url = new URL(request.url());
    if (request.method() === 'GET' && /\/api\/(?:admin\/)?delivery(?:[/?]|$)/.test(url.pathname)) deliveryReads.push(request.url());
  });
  await page.route('**/api/admin/orders/today?*', route => {
    const url = new URL(route.request().url()); reads.push({ path: url.pathname, date: url.searchParams.get('date') });
    return route.fulfill({ json: rows().map(row => ({ ...row, orderDate: url.searchParams.get('date') })) });
  });
  await page.route('**/api/admin/orders/pending-review?*', route => {
    const url = new URL(route.request().url()); reads.push({ path: url.pathname, date: url.searchParams.get('date') });
    return route.fulfill({ json: rows().filter(row => row.status === 'PendingAdminReview') });
  });
  await page.route('**/api/admin/orders/*/review', route => {
    const path = new URL(route.request().url()).pathname, body = route.request().postDataJSON();
    reviews.push({ path, body });
    const order = state.orders.find(item => item.orderId === path.split('/').at(-2));
    order.status = body.decision === 'Rejected' ? 'Rejected' : 'Accepted';
    return route.fulfill({ json: rows().find(row => row.orderId === order.orderId) });
  });
  await page.route('**/api/admin/orders/*/notification', route => {
    const id = new URL(route.request().url()).pathname.split('/').at(-2);
    const order = state.orders.find(item => item.orderId === id);
    if (!order) return route.fulfill({ status: 404, json: { error: 'El pedido no existe.' } });
    if (order.status !== 'Accepted') return route.fulfill({ status: 400, json: { error: 'Primero registra la decisión del pedido.' } });
    return route.fulfill({ json: { message: `Hola, ${order.customerName}. Tu pedido para el ${deliveryDate} ha quedado confirmado.`,
      whatsAppUrl: null, proposedDate: null } });
  });
  // Production API exposes only eligible orders. Keep that response contract here;
  // server-side eligibility and authorization are covered by backend tests.
  await page.route('**/api/admin/delivery?*', route => {
    if (route.request().method() !== 'GET') return route.fallback();
    state.refresh();
    return route.fulfill({ json: { ...state.dashboard,
      orders: state.orders.filter(order => ['Submitted', 'Accepted'].includes(order.status)) } });
  });
  return { ...state, reads, reviews, deliveryReads };
}

async function orders(page: Page) {
  await page.goto(`/admin/pedidos?date=${deliveryDate}`);
  await expect(page.getByTestId('admin-today')).toBeVisible();
  await expect(prepareOrders(page)).toBeVisible();
}

async function openCorrection(page: Page) {
  const state = await integrationFixture(page, ['orders.read', 'orders.capture', 'delivery.manage'], true);
  const order = state.orders[0];
  const orderWrites: string[] = [];
  page.on('request', request => {
    if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())
      && new URL(request.url()).pathname.startsWith('/api/admin/orders/')) orderWrites.push(request.url());
  });
  await page.route(`**/api/admin/orders/${order.orderId}`, route => route.fulfill({ json: {
    orderId: order.orderId, customerId: order.customerId, customerName: order.customerName,
    orderDate: deliveryDate, submittedAt: `${deliveryDate}T14:00:00Z`, status: 'Submitted', sequenceNumber: 1,
    isLate: false, requiresAdminReview: false, adminReviewReason: null, adminDecision: null,
    requestedDeliveryTime: '09:30:00', requestedDeliveryWindowStart: null, requestedDeliveryWindowEnd: null,
    deliveryNotes: 'Recepción de ejemplo.', internalNotes: null, source: null, externalOrderKey: null,
    salesChannelName: 'Cliente', salesChannelType: 'Customer', version: 1, totalAmount: null, hasUnpricedLines: true,
    lines: order.lines.map((line: any) => ({ orderLineId: line.orderLineId, productId: line.productId,
      productName: line.productName, unitCode: line.unitCode, quantity: line.requestedQuantity, notes: null,
      assignedMachineId: null, assignedMachineName: null, assignedMachineNumber: null,
      unitPrice: null, lineAmount: null, currencyCode: 'MXN', priceRevisionId: null }))
  } }));
  await page.route('**/api/admin/products', route => route.fulfill({ json: order.lines.map((line: any) => ({
    id: line.productId, name: line.productName, unitCode: line.unitCode, description: 'Producto ficticio',
    isActive: true, createdAt: `${deliveryDate}T00:00:00Z`, updatedAt: `${deliveryDate}T00:00:00Z`
  })) }));
  await login(page); await orders(page);
  await page.getByTestId('order-card').filter({ hasText: order.customerName })
    .getByRole('button', { name: 'Ver detalle', exact: true }).click();
  await page.getByRole('button', { name: 'Corregir pedido', exact: true }).click();
  const form = page.getByTestId('order-correction'); await expect(form).toBeVisible();
  await form.getByLabel('Cantidad corregida 1', { exact: true }).fill('51');
  await form.getByLabel('Motivo de la corrección', { exact: true }).fill('Conservar este borrador de navegación.');
  return { state, form, orderWrites };
}

test.beforeEach(async ({ page, request }) => {
  await request.post(`${api}/__test/reset`);
  await page.clock.setFixedTime(new Date('2026-11-03T18:00:00Z'));
});

for (const width of [320, 768, 1440]) {
  test(`Pedidos descubre reparto, separa revisión y conserva fecha a ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 960 });
    const state = await integrationFixture(page, ['orders.read', 'orders.review', 'delivery.manage']);
    await login(page); await orders(page);
    await expect(prepareOrders(page).getByTestId('delivery-eligible-count')).toHaveText('2');
    await expect(prepareOrders(page).getByTestId('delivery-review-count')).toHaveText('1');
    await expect(prepareOrders(page).getByTestId('delivery-no-order-count')).toHaveText('1');
    const link = prepareOrders(page).getByRole('link', { name: 'Gestionar reparto', exact: true });
    await expect(link).toHaveAttribute('href', `/admin/reparto?date=${deliveryDate}`);
    await expect(prepareOrders(page).getByRole('link', { name: 'Revisar pedidos', exact: true }))
      .toHaveAttribute('href', `/admin/pendientes?date=${deliveryDate}`);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`orders-delivery-${width}.png`), fullPage: false });
    await link.click();
    await expect(page.getByTestId('delivery-page')).toBeVisible();
    await expect(page.getByLabel('Fecha de reparto', { exact: true })).toHaveValue(deliveryDate);
    expect(state.deliveryReads.some(url => new URL(url).searchParams.get('date') === deliveryDate)).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect(state.writes).toEqual([]); expect(state.reviews).toEqual([]);
  });
}

test('personal con permisos propios descubre Mi reparto desde Pedidos sin acceso a gestión', async ({ page }) => {
  const state = await integrationFixture(page, ['orders.read', 'delivery.read.own', 'delivery.operate.own']);
  await login(page); await orders(page);
  const link = prepareOrders(page).getByRole('link', { name: 'Mi reparto', exact: true });
  await expect(link).toHaveAttribute('href', `/reparto?date=${deliveryDate}`);
  await expect(prepareOrders(page).getByRole('link', { name: 'Gestionar reparto', exact: true })).toHaveCount(0);
  await expect(prepareOrders(page).getByRole('link', { name: 'Revisar pedidos', exact: true })).toHaveCount(0);
  await link.click(); await expect(page.getByTestId('delivery-page')).toBeVisible();
  expect(state.deliveryReads.every(url => !new URL(url).pathname.startsWith('/api/admin/'))).toBe(true);
  await page.goto('/admin/reparto'); await expect(page).toHaveURL(/\/admin\/sin-acceso/);
  expect(state.writes).toEqual([]);
});

for (const canManageSecurity of [false, true]) {
  test(`personal sin permisos de reparto recibe guía y ${canManageSecurity ? 'puede abrir' : 'no recibe acceso a'} Seguridad`, async ({ page }) => {
    await page.setViewportSize({ width: canManageSecurity ? 1440 : 320, height: 960 });
    const state = await integrationFixture(page, ['orders.read', ...(canManageSecurity ? ['security.manage'] : [])]);
    const securityReads: string[] = [];
    page.on('request', request => { if (new URL(request.url()).pathname === '/api/admin/security') securityReads.push(request.url()); });
    await login(page); await orders(page);
    await expect(prepareOrders(page).getByRole('link', { name: 'Habilitar reparto', exact: true })).toBeVisible();
    await navigation(page).getByRole('link', { name: 'Reparto', exact: true }).click();
    await expect(page).toHaveURL(/\/personal\/reparto(?:\?|$)/);
    const guide = page.getByTestId('delivery-onboarding'); await expect(guide).toBeVisible();
    await expect(page.getByRole('region', { name: 'Cómo habilitar reparto', exact: true })).toBeVisible();
    await expect(guide).toContainText('Repartidor - Camioneta (sistema)');
    await expect(guide.getByRole('link', { name: 'Seguridad', exact: true })).toHaveCount(canManageSecurity ? 1 : 0);
    await expect(page.getByTestId('delivery-page')).toHaveCount(0);
    expect(state.deliveryReads).toEqual([]); expect(securityReads).toEqual([]); expect(state.writes).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.goBack();
    await prepareOrders(page).getByRole('link', { name: 'Habilitar reparto', exact: true }).click();
    await expect(guide).toBeVisible();
    await expect(guide.getByRole('link', { name: 'Ver pedidos', exact: true }))
      .toHaveAttribute('href', `/admin/pedidos?date=${deliveryDate}`);
    expect(state.deliveryReads).toEqual([]); expect(securityReads).toEqual([]);
    if (!canManageSecurity) {
      state.setPermissions(['orders.read', 'delivery.read.own']);
      await guide.getByRole('button', { name: 'Volver a consultar mis permisos', exact: true }).click();
      await expect(page).toHaveURL(`/reparto?date=${deliveryDate}`);
      await expect(page.getByTestId('delivery-page')).toBeVisible();
      await expect(page.getByLabel('Fecha de reparto', { exact: true })).toHaveValue(deliveryDate);
      expect(state.deliveryReads.length).toBeGreaterThan(0);
      expect(state.deliveryReads.every(url => new URL(url).pathname === '/api/delivery'
        && new URL(url).searchParams.get('date') === deliveryDate)).toBe(true);
      expect(state.writes).toEqual([]); expect(securityReads).toEqual([]);
    }
  });
}

test('preparar reparto distingue falta de conductores, ruta, vuelta y asignaciones', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 960 });
  const state = await integrationFixture(page, ['delivery.manage', 'security.manage', 'customers.manage']);
  const headers = { 'access-control-allow-origin': '*', 'access-control-allow-headers': 'authorization,content-type', 'access-control-allow-methods': 'GET,PUT,OPTIONS' };
  const schedule: { routeCode: string; cutoff: string | null; version: number } = { routeCode: 'A', cutoff: null, version: 0 };
  const client = { customerId: '56000000-0000-0000-0000-000000000001', customerName: 'Cliente de prueba sin ruta',
    routeCode: null as string | null, routeStopOrder: null as number | null, updatedAt: `${deliveryDate}T13:00:00Z`, isActive: true };
  const cutoffWrites: unknown[] = [];
  const customerRouteWrites: unknown[] = [];
  await page.route(/\/api\/admin\/ordering-schedules(?:\/routes)?\/?$/, async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers });
    if (request.method() === 'GET') return route.fulfill({ headers, json: { routes: [schedule], customers: [] } });
    const body = request.postDataJSON();
    cutoffWrites.push(body);
    schedule.cutoff = body.cutoff;
    schedule.version++;
    return route.fulfill({ status: 204, headers });
  });
  await page.route('**/api/admin/customers/route-assignments', route => route.request().method() === 'OPTIONS'
    ? route.fulfill({ status: 204, headers }) : route.fulfill({ headers, json: [client] }));
  await page.route(`**/api/admin/customers/${client.customerId}/route`, async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers });
    const body = request.postDataJSON();
    customerRouteWrites.push(body);
    client.routeCode = body.routeCode;
    client.routeStopOrder = body.routeStopOrder;
    client.updatedAt = `${deliveryDate}T13:01:00Z`;
    return route.fulfill({ headers, json: client });
  });
  const driver = state.dashboard.drivers[0];
  state.dashboard.drivers = []; state.dashboard.routes = []; state.dashboard.trips = [];
  await login(page);
  await expect(page).toHaveURL('/admin/catalogos');
  await navigation(page).getByRole('link', { name: 'Reparto', exact: true }).click();
  await expect(page.getByTestId('delivery-page')).toBeVisible();
  const checklist = page.getByTestId('delivery-setup-checklist');
  await expect(checklist).toBeVisible();
  for (const part of ['drivers', 'routes', 'trips', 'assignments']) {
    await expect(checklist.getByTestId(`delivery-setup-${part}`)).toHaveAttribute('data-complete', 'false');
  }
  await expect(checklist.getByRole('link', { name: 'Seguridad', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Nueva vuelta', exact: true })).toBeDisabled();
  state.dashboard.drivers.push(driver);
  await page.getByRole('button', { name: 'Consultar reparto', exact: true }).click();
  await expect(checklist.getByTestId('delivery-setup-drivers')).toHaveAttribute('data-complete', 'true');
  await checklist.getByRole('button', { name: 'Configurar ruta', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Código de ruta', { exact: true }).fill('A');
  await dialog.getByLabel('Nombre de la ruta', { exact: true }).fill('Ruta A de ejemplo');
  await dialog.getByRole('combobox', { name: 'Repartidor habitual', exact: true }).selectOption(driverId);
  await dialog.getByRole('button', { name: 'Guardar ruta', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(checklist.getByTestId('delivery-setup-routes')).toHaveAttribute('data-complete', 'true');

  await page.getByRole('button', { name: 'Editar ruta', exact: true }).click();
  const routeSchedule = dialog.getByTestId('delivery-route-schedule');
  await routeSchedule.getByLabel('Hora límite de confirmación automática').fill('11:00');
  await routeSchedule.getByRole('button', { name: 'Guardar horario', exact: true }).click();
  await expect(routeSchedule.getByText('Horario guardado.', { exact: true })).toBeVisible();
  expect(cutoffWrites).toEqual([{ routeCode: 'A', cutoff: '11:00:00', expectedVersion: 0 }]);

  const routeCustomers = dialog.getByTestId('delivery-route-customers');
  await routeCustomers.getByLabel('Cliente para asignar').selectOption(client.customerId);
  await routeCustomers.getByRole('button', { name: 'Agregar a la ruta', exact: true }).click();
  const member = routeCustomers.getByTestId('delivery-route-member').filter({ hasText: client.customerName });
  await expect(member).toBeVisible();
  expect(customerRouteWrites).toEqual([{ routeCode: 'A', routeStopOrder: null, expectedUpdatedAt: `${deliveryDate}T13:00:00Z` }]);
  await member.getByLabel(`Orden de visita de ${client.customerName}`).fill('2');
  await member.getByRole('button', { name: 'Guardar orden', exact: true }).click();
  await expect(routeCustomers.getByRole('status')).toHaveText('Orden de visita guardado.');
  expect(customerRouteWrites[1]).toEqual({ routeCode: 'A', routeStopOrder: 2, expectedUpdatedAt: `${deliveryDate}T13:01:00Z` });
  await dialog.getByRole('button', { name: 'Cerrar', exact: true }).click();
  await expect(dialog).toHaveCount(0);

  await page.setViewportSize({ width: 768, height: 960 });
  await checklist.getByRole('button', { name: 'Crear recorrido', exact: true }).click();
  await dialog.getByRole('combobox', { name: 'Repartidor de esta vuelta', exact: true }).selectOption(driverId);
  await dialog.getByRole('button', { name: 'Crear vuelta', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(checklist.getByTestId('delivery-setup-trips')).toHaveAttribute('data-complete', 'true');
  await page.setViewportSize({ width: 1440, height: 960 });
  await page.getByRole('button', { name: /Asignar a vuelta.*Cliente Reparto Uno.*Tortilla demo/ }).click();
  await dialog.getByLabel('Cantidad a asignar (kg)', { exact: true }).fill('15');
  await dialog.getByRole('button', { name: 'Confirmar asignación', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(checklist.getByTestId('delivery-setup-assignments')).toHaveAttribute('data-complete', 'true');
  expect(state.writes.map(write => write.path.split('/').at(-1))).toEqual(['routes', 'trips', 'assignments']);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('revisión conserva fecha y sólo un pedido aceptado pasa a pendientes de reparto', async ({ page }) => {
  const state = await integrationFixture(page, ['orders.read', 'orders.review', 'delivery.manage'], true);
  await login(page); await orders(page);
  await prepareOrders(page).getByRole('link', { name: 'Gestionar reparto', exact: true }).click();
  const pending = page.getByTestId('delivery-pending-order');
  await expect(pending).toHaveCount(1);
  for (const status of ['PendingAdminReview', 'NoOrder', 'Rejected', 'Cancelled', 'FutureState']) {
    await expect(pending.filter({ hasText: `Cliente ${status}` })).toHaveCount(0);
  }
  await page.goBack();
  await prepareOrders(page).getByRole('link', { name: 'Revisar pedidos', exact: true }).click();
  await expect(page.getByTestId('admin-pending')).toBeVisible();
  await expect(page.getByLabel('Fecha de servicio', { exact: true })).toHaveValue(deliveryDate);
  expect(state.reads.some(read => read.path.endsWith('/pending-review') && read.date === deliveryDate)).toBe(true);
  await page.getByRole('button', { name: 'Aceptar', exact: true }).click();
  await expect(page.getByText('No hay pedidos pendientes de revision.', { exact: true })).toBeVisible();
  await expect(page.getByTestId('admin-pending').locator('.admin-row')).toHaveCount(0);
  await expect(page.getByLabel('Fecha de servicio', { exact: true })).toHaveValue(deliveryDate);
  await expect(page.getByTestId('decision-whatsapp-draft')).toContainText('ha quedado confirmado');
  expect(state.reviews).toHaveLength(1); expect(state.reviews[0].body.decision).toBe('Accepted');
  await page.goBack(); await expect(prepareOrders(page).getByTestId('delivery-eligible-count')).toHaveText('3');
  await expect(prepareOrders(page).getByTestId('delivery-review-count')).toHaveText('0');
  await prepareOrders(page).getByRole('link', { name: 'Gestionar reparto', exact: true }).click();
  const accepted = pending.filter({ hasText: 'Cliente PendingAdminReview' });
  await expect(accepted).toHaveCount(1); await expect(accepted).toContainText('Disponible: 12 kg');
  await expect(accepted.getByRole('button', { name: /Asignar a vuelta/ })).toBeEnabled();
  expect(state.writes).toEqual([]);
});

test('activar el rol canónico conserva el rol legado y permisos directos antes del acceso propio', async ({ page }) => {
  const state = await integrationFixture(page, ['orders.read', 'security.manage']);
  let snapshot: any; const saved: any[] = [];
  const effective = (user: any) => snapshot.permissions.filter((permission: any) => permission.isActive &&
    (user.directPermissionIds.includes(permission.id) || snapshot.roles.some((role: any) => role.isActive &&
      user.roleIds.includes(role.id) && role.permissionIds.includes(permission.id))))
    .map((permission: any) => ({ ...permission, isDirect: user.directPermissionIds.includes(permission.id),
      roleNames: snapshot.roles.filter((role: any) => user.roleIds.includes(role.id) && role.permissionIds.includes(permission.id)).map((role: any) => role.name) }));
  const response = () => ({ ...snapshot, users: snapshot.users.map((user: any) => ({ ...user, effectivePermissions: effective(user) })) });
  await page.route('**/api/admin/security', async route => {
    if (!snapshot) {
      snapshot = await (await route.fetch()).json();
      const ownPermissions = ['delivery.read.own', 'delivery.operate.own'].map((key, index) => ({
        id: `delivery-permission-${index}`, key, name: index ? 'Registrar mi conteo, carga y entregas' : 'Consultar mi reparto',
        module: 'Reparto', description: 'Permiso propio de reparto', isActive: true, isSystem: true }));
      snapshot.permissions.push(...ownPermissions);
      snapshot.roles.push({ id: legacyRoleId, name: 'Repartidor - Camioneta', description: 'Rol previo conservado',
        isActive: true, isSystem: false, permissionIds: [snapshot.permissions.find((item: any) => item.key === 'orders.read').id] },
      { id: canonicalRoleId, name: 'Repartidor - Camioneta (sistema)', description: 'Operación de vueltas propias',
        isActive: true, isSystem: true, permissionIds: ownPermissions.map(item => item.id) });
      const user = snapshot.users.find((item: any) => item.id === driverId);
      user.roleIds = [legacyRoleId]; user.directPermissionIds = [snapshot.permissions.find((item: any) => item.key === 'pricing.manage').id];
    }
    return route.fulfill({ json: response() });
  });
  await page.route(`**/api/admin/security/users/${driverId}`, route => {
    const body = route.request().postDataJSON(); saved.push(body);
    const { expectedRevision: _revision, password: _password, ...userFields } = body;
    Object.assign(snapshot.users.find((item: any) => item.id === driverId), userFields);
    snapshot.revision = 'delivery-role-revision-2';
    return route.fulfill({ json: response() });
  });
  await login(page); await navigation(page).getByRole('link', { name: 'Seguridad', exact: true }).click();
  await page.getByRole('button', { name: 'Editar usuario Operador Demo', exact: true }).click();
  const form = page.getByRole('region', { name: 'Editor de usuario', exact: true });
  await expect(form.getByRole('checkbox', { name: 'Rol Repartidor - Camioneta', exact: true })).toBeChecked();
  await form.getByRole('checkbox', { name: 'Rol Repartidor - Camioneta (sistema)', exact: true }).check();
  await form.getByRole('button', { name: 'Guardar usuario', exact: true }).click();
  await expect(form).toHaveCount(0); expect(saved).toHaveLength(1);
  expect(saved[0].roleIds).toEqual([legacyRoleId, canonicalRoleId]);
  expect(saved[0].directPermissionIds).toEqual([snapshot.permissions.find((item: any) => item.key === 'pricing.manage').id]);
  const permissions = effective(snapshot.users.find((item: any) => item.id === driverId)).map((item: any) => item.key);
  expect(permissions).toEqual(expect.arrayContaining(['orders.read', 'pricing.manage', 'delivery.read.own', 'delivery.operate.own']));
  expect(permissions).not.toContain('delivery.manage');
  await page.getByRole('button', { name: 'Salir', exact: true }).click(); state.setPermissions(permissions);
  await login(page); await expect(page).toHaveURL(/\/reparto(?:\?|$)/);
  await expect(page.getByTestId('delivery-page')).toBeVisible();
  await expect(navigation(page).getByRole('link', { name: 'Mi reparto', exact: true })).toBeVisible();
  await expect(navigation(page).getByRole('link', { name: 'Reparto', exact: true })).toHaveCount(0);
  expect(state.writes).toEqual([]);
});

test('navegar a Reparto durante una corrección conserva el borrador hasta cancelar', async ({ page }) => {
  const { state, form, orderWrites } = await openCorrection(page);
  const url = page.url(), dialogs: string[] = [];
  page.on('dialog', async dialog => { dialogs.push(dialog.type()); await dialog.dismiss(); });
  await navigation(page).getByRole('link', { name: 'Reparto', exact: true }).click();
  await expect(page.getByTestId('orders-navigation-blocked'))
    .toHaveText('Guarda o cancela la corrección antes de cambiar de pantalla.');
  await expect(page).toHaveURL(url);
  await expect(form.getByLabel('Cantidad corregida 1', { exact: true })).toHaveValue('51');
  await expect(form.getByLabel('Motivo de la corrección', { exact: true }))
    .toHaveValue('Conservar este borrador de navegación.');
  expect(state.deliveryReads).toEqual([]); expect(dialogs).toEqual([]);
  await form.getByRole('button', { name: 'Cancelar corrección', exact: true }).click();
  await expect(form).toHaveCount(0);
  await navigation(page).getByRole('link', { name: 'Reparto', exact: true }).click();
  await expect(page).toHaveURL(/\/admin\/reparto(?:\?|$)/);
  await expect(page.getByTestId('delivery-page')).toBeVisible();
  expect(orderWrites).toEqual([]); expect(state.writes).toEqual([]); expect(dialogs).toEqual([]);
});

test('cerrar sesión con una corrección abierta permite salir sin conservar acceso administrativo', async ({ page }) => {
  const { state, form, orderWrites } = await openCorrection(page);
  const dialogs: string[] = [];
  page.on('dialog', async dialog => { dialogs.push(dialog.type()); await dialog.dismiss(); });
  await page.getByRole('button', { name: 'Salir', exact: true }).click();
  await expect(page).toHaveURL(/\/cliente(?:\?|$)/);
  await expect(form).toHaveCount(0);
  await expect(navigation(page).getByRole('link', { name: 'Personal', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Salir', exact: true })).toHaveCount(0);
  expect(orderWrites).toEqual([]); expect(state.writes).toEqual([]); expect(dialogs).toEqual([]);
});
