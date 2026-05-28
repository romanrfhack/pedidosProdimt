const http = require('node:http');

const port = 5088;
const demoCustomerToken = 'demo-customer-token';
const demoAdminUserName = 'admin';
const demoAdminPassword = 'prodimt-admin-demo';
const customerJwt = 'mock-customer-jwt';
const adminJwt = 'mock-admin-jwt';

const customerTodayBase = {
  customerId: '11111111-1111-1111-1111-111111111111',
  customerName: 'Gran Takito',
  orderDate: '2026-05-27',
  preferredDeliveryTime: null,
  preferredDeliveryWindowStart: '12:00:00',
  preferredDeliveryWindowEnd: '14:00:00',
  deliveryNotes: 'Cliente demo.',
  currentOrder: null,
  products: [
    {
      productId: '22222222-2222-2222-2222-222222222201',
      name: '#9 1/2',
      description: 'Producto demo',
      suggestedQuantity: 20
    },
    {
      productId: '22222222-2222-2222-2222-222222222202',
      name: '#10 1/2',
      description: 'Producto demo',
      suggestedQuantity: 10
    }
  ]
};

const noOrder = {
  orderId: '33333333-3333-3333-3333-333333333399',
  customerId: customerTodayBase.customerId,
  orderDate: customerTodayBase.orderDate,
  status: 'NoOrder',
  sequenceNumber: 1,
  submittedAt: '2026-05-27T09:10:00-06:00',
  isLate: false,
  requiresAdminReview: false,
  adminReviewReason: null
};

const pendingOrderBase = {
  orderId: '33333333-3333-3333-3333-333333333301',
  customerId: customerTodayBase.customerId,
  customerName: 'Gran Takito',
  orderDate: '2026-05-27',
  submittedAt: '2026-05-27T10:15:00-06:00',
  status: 'PendingAdminReview',
  sequenceNumber: 2,
  isLate: true,
  requiresAdminReview: true,
  adminReviewReason: 'LateSubmission',
  requestedDeliveryTime: null,
  requestedDeliveryWindowStart: '12:00:00',
  requestedDeliveryWindowEnd: '14:00:00',
  deliveryNotes: 'Cliente demo.',
  adminDecision: 'Pending'
};

const pendingOrderDetail = {
  ...pendingOrderBase,
  internalNotes: 'Pedido demo pendiente.',
  salesChannelName: 'Cliente',
  salesChannelType: 'Customer',
  lines: [
    {
      orderLineId: '77777777-7777-7777-7777-777777777701',
      productId: '22222222-2222-2222-2222-222222222201',
      productName: '#9 1/2',
      quantity: 20,
      notes: 'Linea demo',
      assignedMachineId: '44444444-4444-4444-4444-444444444401',
      assignedMachineName: 'Maquina 1',
      assignedMachineNumber: 1
    }
  ]
};

const pendingCustomerBase = [
  {
    customerId: '11111111-1111-1111-1111-111111111112',
    customerName: 'Cliente Demo 2',
    phoneNumber: '0000000002',
    preferredDeliveryTime: '13:30:00',
    preferredDeliveryWindowStart: null,
    preferredDeliveryWindowEnd: null,
    deliveryNotes: 'Llamar antes de enviar.',
    frequentProductsCount: 1
  },
  {
    customerId: '11111111-1111-1111-1111-111111111113',
    customerName: 'Cliente Demo 3',
    phoneNumber: '0000000003',
    preferredDeliveryTime: null,
    preferredDeliveryWindowStart: null,
    preferredDeliveryWindowEnd: null,
    deliveryNotes: null,
    frequentProductsCount: 1
  }
];

let currentOrder = null;
let pendingOrders = [pendingOrderBase];
let pendingCustomers = [...pendingCustomerBase];
let submitCalls = 0;
let adminSubmitCalls = 0;

function resetState() {
  currentOrder = null;
  pendingOrders = [pendingOrderBase];
  pendingCustomers = [...pendingCustomerBase];
  submitCalls = 0;
  adminSubmitCalls = 0;
}

function sendJson(response, statusCode, body) {
  response.writeHead(statusCode, {
    'access-control-allow-origin': '*',
    'access-control-allow-headers': 'authorization,content-type',
    'access-control-allow-methods': 'GET,POST,OPTIONS',
    'content-type': 'application/json'
  });
  response.end(JSON.stringify(body));
}

function readJson(request) {
  return new Promise((resolve) => {
    let body = '';
    request.on('data', (chunk) => {
      body += chunk;
    });
    request.on('end', () => {
      if (!body) {
        resolve({});
        return;
      }

      try {
        resolve(JSON.parse(body));
      } catch {
        resolve({});
      }
    });
  });
}

function bearerToken(request) {
  const authorization = request.headers.authorization ?? '';
  return authorization.startsWith('Bearer ') ? authorization.slice('Bearer '.length) : null;
}

function requireCustomerAccess(request, response, customerId) {
  const token = bearerToken(request);

  if (!token) {
    sendJson(response, 401, { error: 'Unauthorized' });
    return false;
  }

  if (token !== customerJwt || customerId !== customerTodayBase.customerId) {
    sendJson(response, 403, { error: 'Forbidden' });
    return false;
  }

  return true;
}

function requireAdminAccess(request, response) {
  const token = bearerToken(request);

  if (!token) {
    sendJson(response, 401, { error: 'Unauthorized' });
    return false;
  }

  if (token !== adminJwt) {
    sendJson(response, 403, { error: 'Forbidden' });
    return false;
  }

  return true;
}

const server = http.createServer(async (request, response) => {
  const url = new URL(request.url ?? '/', `http://127.0.0.1:${port}`);

  if (request.method === 'OPTIONS') {
    sendJson(response, 204, {});
    return;
  }

  if (request.method === 'GET' && (url.pathname === '/health' || url.pathname === '/__test/health')) {
    sendJson(response, 200, { status: 'ok' });
    return;
  }

  if (request.method === 'POST' && url.pathname === '/api/auth/customer-token') {
    const body = await readJson(request);

    if (body.token !== demoCustomerToken) {
      sendJson(response, 401, { error: 'Token de cliente invalido.' });
      return;
    }

    sendJson(response, 200, {
      accessToken: customerJwt,
      tokenType: 'Bearer',
      expiresAt: '2030-01-01T00:00:00Z',
      customerId: customerTodayBase.customerId,
      customerName: customerTodayBase.customerName
    });
    return;
  }

  if (request.method === 'POST' && url.pathname === '/api/auth/admin/login') {
    const body = await readJson(request);

    if (body.userName !== demoAdminUserName || body.password !== demoAdminPassword) {
      sendJson(response, 401, { error: 'Credenciales administrativas invalidas.' });
      return;
    }

    sendJson(response, 200, {
      accessToken: adminJwt,
      tokenType: 'Bearer',
      expiresAt: '2030-01-01T00:00:00Z',
      displayName: 'Administrador Demo'
    });
    return;
  }

  if (request.method === 'POST' && url.pathname === '/__test/reset') {
    resetState();
    sendJson(response, 200, { status: 'reset' });
    return;
  }

  if (request.method === 'GET' && url.pathname === '/__test/state') {
    sendJson(response, 200, { submitCalls, adminSubmitCalls });
    return;
  }

  const customerTodayMatch = url.pathname.match(/^\/api\/customer-orders\/([^/]+)\/today$/);
  if (request.method === 'GET' && customerTodayMatch) {
    if (!requireCustomerAccess(request, response, customerTodayMatch[1])) {
      return;
    }

    sendJson(response, 200, {
      ...customerTodayBase,
      currentOrder
    });
    return;
  }

  const customerSubmitMatch = url.pathname.match(/^\/api\/customer-orders\/([^/]+)\/submit$/);
  if (request.method === 'POST' && customerSubmitMatch) {
    if (!requireCustomerAccess(request, response, customerSubmitMatch[1])) {
      return;
    }

    submitCalls += 1;
    const body = await readJson(request);
    const positiveLines = Array.isArray(body.lines)
      ? body.lines.filter((line) => Number(line.quantity) > 0)
      : [];

    if (positiveLines.length === 0) {
      sendJson(response, 400, { error: 'Captura al menos una cantidad o usa No pedir hoy.' });
      return;
    }

    currentOrder = {
      orderId: '33333333-3333-3333-3333-333333333302',
      customerId: customerTodayBase.customerId,
      orderDate: customerTodayBase.orderDate,
      status: 'Submitted',
      sequenceNumber: 1,
      submittedAt: '2026-05-27T09:30:00-06:00',
      isLate: false,
      requiresAdminReview: false,
      adminReviewReason: null
    };
    sendJson(response, 200, currentOrder);
    return;
  }

  const customerNoOrderMatch = url.pathname.match(/^\/api\/customer-orders\/([^/]+)\/no-order$/);
  if (request.method === 'POST' && customerNoOrderMatch) {
    if (!requireCustomerAccess(request, response, customerNoOrderMatch[1])) {
      return;
    }

    currentOrder = noOrder;
    sendJson(response, 200, noOrder);
    return;
  }

  if (request.method === 'GET' && url.pathname === '/api/admin/orders/today') {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    sendJson(response, 200, [pendingOrderBase]);
    return;
  }

  if (request.method === 'GET' && url.pathname === '/api/admin/orders/pending-review') {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    sendJson(response, 200, pendingOrders);
    return;
  }

  const adminOrderDetailMatch = url.pathname.match(/^\/api\/admin\/orders\/([^/]+)$/);
  if (request.method === 'GET' && adminOrderDetailMatch) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    sendJson(response, 200, pendingOrderDetail);
    return;
  }

  if (request.method === 'GET' && /^\/api\/admin\/orders\/[^/]+\/audit$/.test(url.pathname)) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    sendJson(response, 200, [
      {
        id: '66666666-6666-6666-6666-666666666601',
        orderId: pendingOrderBase.orderId,
        customerId: pendingOrderBase.customerId,
        eventType: 'OrderSubmitted',
        occurredAt: '2026-05-27T10:15:00-06:00',
        actorType: 'Customer',
        actorId: null,
        actorDisplayName: null,
        orderStatus: 'PendingAdminReview',
        adminReviewReason: 'LateSubmission',
        adminDecision: 'Pending',
        summary: 'Pedido enviado por cliente.',
        metadataJson: null
      }
    ]);
    return;
  }

  if (request.method === 'POST' && /^\/api\/admin\/orders\/[^/]+\/review$/.test(url.pathname)) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    const body = await readJson(request);
    const decision = body.decision === 'Rejected'
      ? 'Rejected'
      : body.decision === 'AcceptedWithChanges'
        ? 'AcceptedWithChanges'
        : 'Accepted';
    pendingOrders = [];
    sendJson(response, 200, {
      ...pendingOrderBase,
      status: decision === 'Rejected' ? 'Rejected' : 'Accepted',
      requiresAdminReview: false,
      adminDecision: decision
    });
    return;
  }

  if (request.method === 'GET' && url.pathname === '/api/admin/customers/pending-orders') {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    sendJson(response, 200, pendingCustomers);
    return;
  }

  const adminTemplateMatch = url.pathname.match(/^\/api\/admin\/customers\/([^/]+)\/order-template$/);
  if (request.method === 'GET' && adminTemplateMatch) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    const customer = pendingCustomerBase.find((item) => item.customerId === adminTemplateMatch[1]);
    sendJson(response, 200, {
      customerId: adminTemplateMatch[1],
      customerName: customer?.customerName ?? 'Cliente Demo',
      preferredDeliveryTime: customer?.preferredDeliveryTime ?? null,
      preferredDeliveryWindowStart: null,
      preferredDeliveryWindowEnd: null,
      deliveryNotes: customer?.deliveryNotes ?? null,
      products: [
        {
          productId: '22222222-2222-2222-2222-222222222203',
          name: '#11',
          description: 'Producto demo',
          suggestedQuantity: 8
        }
      ]
    });
    return;
  }

  const adminNoOrderMatch = url.pathname.match(/^\/api\/admin\/customers\/([^/]+)\/orders\/no-order$/);
  if (request.method === 'POST' && adminNoOrderMatch) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    pendingCustomers = pendingCustomers.filter((customer) => customer.customerId !== adminNoOrderMatch[1]);
    sendJson(response, 200, {
      orderId: '33333333-3333-3333-3333-333333333398',
      customerId: adminNoOrderMatch[1],
      customerName: 'Cliente Demo 2',
      orderDate: '2026-05-27',
      submittedAt: '2026-05-27T09:40:00-06:00',
      status: 'NoOrder',
      sequenceNumber: 1,
      isLate: false,
      requiresAdminReview: false,
      adminReviewReason: null,
      requestedDeliveryTime: null,
      requestedDeliveryWindowStart: null,
      requestedDeliveryWindowEnd: null,
      deliveryNotes: null,
      adminDecision: null
    });
    return;
  }

  const adminSubmitMatch = url.pathname.match(/^\/api\/admin\/customers\/([^/]+)\/orders\/submit$/);
  if (request.method === 'POST' && adminSubmitMatch) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    adminSubmitCalls += 1;
    const body = await readJson(request);
    const positiveLines = Array.isArray(body.lines)
      ? body.lines.filter((line) => Number(line.quantity) > 0)
      : [];

    if (positiveLines.length === 0) {
      sendJson(response, 400, { error: 'Captura al menos una cantidad o marca No pedir hoy.' });
      return;
    }

    pendingCustomers = pendingCustomers.filter((customer) => customer.customerId !== adminSubmitMatch[1]);
    sendJson(response, 200, {
      orderId: '33333333-3333-3333-3333-333333333397',
      customerId: adminSubmitMatch[1],
      customerName: 'Cliente Demo 2',
      orderDate: '2026-05-27',
      submittedAt: '2026-05-27T09:45:00-06:00',
      status: 'Submitted',
      sequenceNumber: 1,
      isLate: false,
      requiresAdminReview: false,
      adminReviewReason: null,
      requestedDeliveryTime: body.requestedDeliveryTime ?? null,
      requestedDeliveryWindowStart: null,
      requestedDeliveryWindowEnd: null,
      deliveryNotes: body.deliveryNotes ?? null,
      adminDecision: null
    });
    return;
  }

  sendJson(response, 404, { error: 'Not found' });
});

server.listen(port, '127.0.0.1', () => {
  console.log(`Mock API listening on http://127.0.0.1:${port}`);
});

process.on('SIGTERM', () => server.close(() => process.exit(0)));
process.on('SIGINT', () => server.close(() => process.exit(0)));
