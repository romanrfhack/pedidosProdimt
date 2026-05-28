const http = require('node:http');

const port = 5088;

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

let currentOrder = null;
let pendingOrders = [pendingOrderBase];
let submitCalls = 0;

function resetState() {
  currentOrder = null;
  pendingOrders = [pendingOrderBase];
  submitCalls = 0;
}

function sendJson(response, statusCode, body) {
  response.writeHead(statusCode, {
    'access-control-allow-origin': '*',
    'access-control-allow-headers': 'content-type',
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

  if (request.method === 'POST' && url.pathname === '/__test/reset') {
    resetState();
    sendJson(response, 200, { status: 'reset' });
    return;
  }

  if (request.method === 'GET' && url.pathname === '/__test/state') {
    sendJson(response, 200, { submitCalls });
    return;
  }

  if (request.method === 'GET' && /^\/api\/customer-orders\/[^/]+\/today$/.test(url.pathname)) {
    sendJson(response, 200, {
      ...customerTodayBase,
      currentOrder
    });
    return;
  }

  if (request.method === 'POST' && /^\/api\/customer-orders\/[^/]+\/submit$/.test(url.pathname)) {
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

  if (request.method === 'POST' && /^\/api\/customer-orders\/[^/]+\/no-order$/.test(url.pathname)) {
    currentOrder = noOrder;
    sendJson(response, 200, noOrder);
    return;
  }

  if (request.method === 'GET' && url.pathname === '/api/admin/orders/today') {
    sendJson(response, 200, [pendingOrderBase]);
    return;
  }

  if (request.method === 'GET' && url.pathname === '/api/admin/orders/pending-review') {
    sendJson(response, 200, pendingOrders);
    return;
  }

  if (request.method === 'POST' && /^\/api\/admin\/orders\/[^/]+\/review$/.test(url.pathname)) {
    const body = await readJson(request);
    const decision = body.decision === 'Rejected' ? 'Rejected' : 'Accepted';
    pendingOrders = [];
    sendJson(response, 200, {
      ...pendingOrderBase,
      status: decision === 'Rejected' ? 'Rejected' : 'Accepted',
      requiresAdminReview: false,
      adminDecision: decision
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
