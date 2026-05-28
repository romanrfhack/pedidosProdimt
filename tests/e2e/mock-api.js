const http = require('node:http');
const { randomUUID } = require('node:crypto');

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

const catalogCustomersBase = [
  {
    id: customerTodayBase.customerId,
    name: 'Gran Takito',
    phoneNumber: '0000000001',
    isActive: true,
    preferredDeliveryTime: null,
    preferredDeliveryWindowStart: '12:00:00',
    preferredDeliveryWindowEnd: '14:00:00',
    deliveryNotes: 'Cliente demo.',
    createdAt: '2026-05-27T00:00:00Z',
    updatedAt: '2026-05-27T00:00:00Z'
  },
  {
    id: '11111111-1111-1111-1111-111111111112',
    name: 'Cliente Demo 2',
    phoneNumber: '0000000002',
    isActive: true,
    preferredDeliveryTime: '13:30:00',
    preferredDeliveryWindowStart: null,
    preferredDeliveryWindowEnd: null,
    deliveryNotes: 'Llamar antes de enviar.',
    createdAt: '2026-05-27T00:00:00Z',
    updatedAt: '2026-05-27T00:00:00Z'
  }
];

const catalogProductsBase = [
  {
    id: '22222222-2222-2222-2222-222222222201',
    name: '#9 1/2',
    description: 'Producto demo',
    isActive: true
  },
  {
    id: '22222222-2222-2222-2222-222222222202',
    name: '#10 1/2',
    description: 'Producto demo',
    isActive: true
  },
  {
    id: '22222222-2222-2222-2222-222222222203',
    name: '#11',
    description: 'Producto demo',
    isActive: true
  }
];

const catalogMachinesBase = [
  {
    id: '44444444-4444-4444-4444-444444444401',
    number: 1,
    name: 'Maquina 1',
    isActive: true
  },
  {
    id: '44444444-4444-4444-4444-444444444402',
    number: 2,
    name: 'Maquina 2',
    isActive: true
  }
];

const frequentProductsBase = {
  [customerTodayBase.customerId]: [
    {
      productId: '22222222-2222-2222-2222-222222222201',
      productName: '#9 1/2',
      defaultQuantity: 20,
      sortOrder: 1,
      isActive: true
    },
    {
      productId: '22222222-2222-2222-2222-222222222202',
      productName: '#10 1/2',
      defaultQuantity: 10,
      sortOrder: 2,
      isActive: true
    }
  ]
};

const machineAssignmentsBase = {
  [customerTodayBase.customerId]: [
    {
      machineId: '44444444-4444-4444-4444-444444444401',
      machineNumber: 1,
      machineName: 'Maquina 1',
      isDefault: true,
      isActive: true,
      notes: 'Asignacion demo'
    }
  ]
};

const accessTokensBase = {
  [customerTodayBase.customerId]: [
    {
      tokenId: '55555555-5555-5555-5555-555555555501',
      customerId: customerTodayBase.customerId,
      description: 'Token demo Gran Takito',
      expiresAt: null,
      isActive: true,
      createdAt: '2026-05-27T00:00:00Z',
      lastUsedAt: null
    }
  ]
};

let currentOrder = null;
let pendingOrders = [pendingOrderBase];
let pendingCustomers = [...pendingCustomerBase];
let catalogCustomers = [];
let catalogProducts = [];
let catalogMachines = [];
let frequentProductsByCustomer = {};
let machineAssignmentsByCustomer = {};
let accessTokensByCustomer = {};
let submitCalls = 0;
let adminSubmitCalls = 0;

function clone(value) {
  return JSON.parse(JSON.stringify(value));
}

function resetState() {
  currentOrder = null;
  pendingOrders = [pendingOrderBase];
  pendingCustomers = [...pendingCustomerBase];
  catalogCustomers = clone(catalogCustomersBase);
  catalogProducts = clone(catalogProductsBase);
  catalogMachines = clone(catalogMachinesBase);
  frequentProductsByCustomer = clone(frequentProductsBase);
  machineAssignmentsByCustomer = clone(machineAssignmentsBase);
  accessTokensByCustomer = clone(accessTokensBase);
  submitCalls = 0;
  adminSubmitCalls = 0;
}

function sendJson(response, statusCode, body) {
  response.writeHead(statusCode, {
      'access-control-allow-origin': '*',
      'access-control-allow-headers': 'authorization,content-type',
      'access-control-allow-methods': 'GET,POST,PUT,PATCH,OPTIONS',
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

function validateImport(importType, body) {
  const content = String(body.content ?? '');
  const rows = content.split(/\r?\n/).filter((row) => row.trim().length > 0);
  const dataRows = Math.max(0, rows.length - 1);
  const errors = [];
  const warnings = [];
  const proposedChanges = [];

  rows.slice(1).forEach((row, index) => {
    const rowNumber = index + 2;
    const columns = row.split(',');
    const name = columns[1]?.trim() ?? '';

    if ((importType === 'customers' || importType === 'products') && !name) {
      errors.push({
        rowNumber,
        field: 'name',
        code: 'Required',
        message: 'Campo requerido vacio: name.',
        rawValue: ''
      });
      return;
    }

    proposedChanges.push({
      rowNumber,
      action: 'Create',
      entityType: importType === 'products' ? 'Product' : 'Customer',
      entityId: null,
      entityDisplayName: name || 'Registro demo',
      summary: 'Crear registro desde CSV.'
    });
  });

  return {
    importType,
    totalRows: dataRows,
    validRows: errors.length > 0 ? Math.max(0, dataRows - errors.length) : dataRows,
    errorCount: errors.length,
    warningCount: warnings.length,
    proposedCreateCount: proposedChanges.length,
    proposedUpdateCount: 0,
    proposedDeactivateCount: 0,
    errors,
    warnings,
    proposedChanges: errors.length > 0 ? [] : proposedChanges
  };
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

  if (request.method === 'GET' && url.pathname === '/api/admin/import/templates') {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    sendJson(response, 200, {
      maxFileSizeBytes: 2097152,
      mode: 'stateless-validate-then-apply',
      templates: [
        {
          importType: 'customers',
          description: 'Clientes externos para piloto.',
          templatePath: 'docs/import-templates/customers.csv',
          examplePath: 'docs/import-templates/examples/customers-demo.csv',
          columns: []
        },
        {
          importType: 'products',
          description: 'Productos o moldes.',
          templatePath: 'docs/import-templates/products.csv',
          examplePath: 'docs/import-templates/examples/products-demo.csv',
          columns: []
        },
        {
          importType: 'customer-frequent-products',
          description: 'Productos frecuentes por cliente.',
          templatePath: 'docs/import-templates/customer-frequent-products.csv',
          examplePath: 'docs/import-templates/examples/customer-frequent-products-demo.csv',
          columns: []
        },
        {
          importType: 'machines',
          description: 'Maquinas internas.',
          templatePath: 'docs/import-templates/machines.csv',
          examplePath: 'docs/import-templates/examples/machines-demo.csv',
          columns: []
        },
        {
          importType: 'customer-machine-assignments',
          description: 'Asignaciones internas cliente-maquina.',
          templatePath: 'docs/import-templates/customer-machine-assignments.csv',
          examplePath: 'docs/import-templates/examples/customer-machine-assignments-demo.csv',
          columns: []
        }
      ]
    });
    return;
  }

  const importMatch = url.pathname.match(/^\/api\/admin\/import\/([^/]+)\/(validate|apply)$/);
  if (request.method === 'POST' && importMatch) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    request.resume();
    const validation = importMatch[1] === 'customers'
      ? {
          importType: importMatch[1],
          totalRows: 1,
          validRows: 0,
          errorCount: 1,
          warningCount: 0,
          proposedCreateCount: 0,
          proposedUpdateCount: 0,
          proposedDeactivateCount: 0,
          errors: [
            {
              rowNumber: 2,
              field: 'name',
              code: 'Required',
              message: 'Campo requerido vacio: name.',
              rawValue: ''
            }
          ],
          warnings: [],
          proposedChanges: []
        }
      : validateImport(importMatch[1], {
          content: 'externalCode,name,description,isActive\nP-E2E,Molde E2E,Producto demo,true'
        });

    if (importMatch[2] === 'validate') {
      sendJson(response, 200, validation);
      return;
    }

    if (validation.errorCount > 0) {
      sendJson(response, 400, {
        importType: importMatch[1],
        totalRows: validation.totalRows,
        createdCount: 0,
        updatedCount: 0,
        skippedCount: validation.totalRows,
        warningCount: validation.warningCount,
        auditLogIds: [],
        errors: validation.errors
      });
      return;
    }

    sendJson(response, 200, {
      importType: importMatch[1],
      totalRows: validation.totalRows,
      createdCount: validation.proposedCreateCount,
      updatedCount: validation.proposedUpdateCount,
      skippedCount: 0,
      warningCount: validation.warningCount,
      auditLogIds: ['aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'],
      errors: []
    });
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

  if (request.method === 'GET' && url.pathname === '/api/admin/customers') {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    sendJson(response, 200, catalogCustomers);
    return;
  }

  if (request.method === 'POST' && url.pathname === '/api/admin/customers') {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    const body = await readJson(request);
    const customer = {
      id: randomUUID(),
      name: body.name,
      phoneNumber: body.phoneNumber ?? '',
      isActive: true,
      preferredDeliveryTime: body.preferredDeliveryTime ?? null,
      preferredDeliveryWindowStart: body.preferredDeliveryWindowStart ?? null,
      preferredDeliveryWindowEnd: body.preferredDeliveryWindowEnd ?? null,
      deliveryNotes: body.deliveryNotes ?? null,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString()
    };
    catalogCustomers.push(customer);
    sendJson(response, 201, customer);
    return;
  }

  const catalogCustomerMatch = url.pathname.match(/^\/api\/admin\/customers\/([^/]+)$/);
  if (catalogCustomerMatch && (request.method === 'GET' || request.method === 'PUT')) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    const customer = catalogCustomers.find((item) => item.id === catalogCustomerMatch[1]);
    if (!customer) {
      sendJson(response, 404, { error: 'Customer was not found.' });
      return;
    }

    if (request.method === 'PUT') {
      const body = await readJson(request);
      Object.assign(customer, {
        name: body.name,
        phoneNumber: body.phoneNumber ?? '',
        preferredDeliveryTime: body.preferredDeliveryTime ?? null,
        preferredDeliveryWindowStart: body.preferredDeliveryWindowStart ?? null,
        preferredDeliveryWindowEnd: body.preferredDeliveryWindowEnd ?? null,
        deliveryNotes: body.deliveryNotes ?? null,
        updatedAt: new Date().toISOString()
      });
    }

    sendJson(response, 200, customer);
    return;
  }

  const customerActivationMatch = url.pathname.match(/^\/api\/admin\/customers\/([^/]+)\/(activate|deactivate)$/);
  if (request.method === 'PATCH' && customerActivationMatch) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    const customer = catalogCustomers.find((item) => item.id === customerActivationMatch[1]);
    if (!customer) {
      sendJson(response, 404, { error: 'Customer was not found.' });
      return;
    }

    customer.isActive = customerActivationMatch[2] === 'activate';
    customer.updatedAt = new Date().toISOString();
    sendJson(response, 200, customer);
    return;
  }

  const frequentMatch = url.pathname.match(/^\/api\/admin\/customers\/([^/]+)\/frequent-products$/);
  if (frequentMatch && (request.method === 'GET' || request.method === 'PUT')) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    if (request.method === 'PUT') {
      const body = await readJson(request);
      frequentProductsByCustomer[frequentMatch[1]] = (body.items ?? []).map((item) => {
        const product = catalogProducts.find((candidate) => candidate.id === item.productId);
        return {
          productId: item.productId,
          productName: product?.name ?? 'Producto',
          defaultQuantity: item.defaultQuantity ?? null,
          sortOrder: item.sortOrder,
          isActive: item.isActive
        };
      });
    }

    sendJson(response, 200, frequentProductsByCustomer[frequentMatch[1]] ?? []);
    return;
  }

  const assignmentMatch = url.pathname.match(/^\/api\/admin\/customers\/([^/]+)\/machine-assignments$/);
  if (assignmentMatch && (request.method === 'GET' || request.method === 'PUT')) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    if (request.method === 'PUT') {
      const body = await readJson(request);
      machineAssignmentsByCustomer[assignmentMatch[1]] = (body.items ?? []).map((item) => {
        const machine = catalogMachines.find((candidate) => candidate.id === item.machineId);
        return {
          machineId: item.machineId,
          machineNumber: machine?.number ?? 0,
          machineName: machine?.name ?? null,
          isDefault: item.isDefault,
          isActive: item.isActive,
          notes: item.notes ?? null
        };
      });
    }

    sendJson(response, 200, machineAssignmentsByCustomer[assignmentMatch[1]] ?? []);
    return;
  }

  const tokenMatch = url.pathname.match(/^\/api\/admin\/customers\/([^/]+)\/access-tokens$/);
  if (tokenMatch && (request.method === 'GET' || request.method === 'POST')) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    if (request.method === 'POST') {
      const body = await readJson(request);
      const token = {
        tokenId: randomUUID(),
        customerId: tokenMatch[1],
        description: body.description ?? 'Token piloto',
        expiresAt: body.expiresAt ?? null,
        isActive: true,
        createdAt: new Date().toISOString(),
        lastUsedAt: null
      };
      accessTokensByCustomer[tokenMatch[1]] = [...(accessTokensByCustomer[tokenMatch[1]] ?? []), token];
      sendJson(response, 201, {
        ...token,
        plainToken: 'mock-generated-token'
      });
      return;
    }

    sendJson(response, 200, accessTokensByCustomer[tokenMatch[1]] ?? []);
    return;
  }

  const tokenRevokeMatch = url.pathname.match(/^\/api\/admin\/customers\/([^/]+)\/access-tokens\/([^/]+)\/revoke$/);
  if (request.method === 'PATCH' && tokenRevokeMatch) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    const tokens = accessTokensByCustomer[tokenRevokeMatch[1]] ?? [];
    const token = tokens.find((item) => item.tokenId === tokenRevokeMatch[2]);
    if (!token) {
      sendJson(response, 404, { error: 'Token not found.' });
      return;
    }

    token.isActive = false;
    sendJson(response, 200, token);
    return;
  }

  if (request.method === 'GET' && url.pathname === '/api/admin/products') {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    sendJson(response, 200, catalogProducts);
    return;
  }

  if (request.method === 'POST' && url.pathname === '/api/admin/products') {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    const body = await readJson(request);
    const product = {
      id: randomUUID(),
      name: body.name,
      description: body.description ?? null,
      isActive: true
    };
    catalogProducts.push(product);
    sendJson(response, 201, product);
    return;
  }

  const catalogProductMatch = url.pathname.match(/^\/api\/admin\/products\/([^/]+)$/);
  if (catalogProductMatch && (request.method === 'GET' || request.method === 'PUT')) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    const product = catalogProducts.find((item) => item.id === catalogProductMatch[1]);
    if (!product) {
      sendJson(response, 404, { error: 'Product was not found.' });
      return;
    }

    if (request.method === 'PUT') {
      const body = await readJson(request);
      product.name = body.name;
      product.description = body.description ?? null;
    }

    sendJson(response, 200, product);
    return;
  }

  const productActivationMatch = url.pathname.match(/^\/api\/admin\/products\/([^/]+)\/(activate|deactivate)$/);
  if (request.method === 'PATCH' && productActivationMatch) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    const product = catalogProducts.find((item) => item.id === productActivationMatch[1]);
    if (!product) {
      sendJson(response, 404, { error: 'Product was not found.' });
      return;
    }

    product.isActive = productActivationMatch[2] === 'activate';
    sendJson(response, 200, product);
    return;
  }

  if (request.method === 'GET' && url.pathname === '/api/admin/machines') {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    sendJson(response, 200, catalogMachines);
    return;
  }

  if (request.method === 'POST' && url.pathname === '/api/admin/machines') {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    const body = await readJson(request);
    const machine = {
      id: randomUUID(),
      number: Number(body.number),
      name: body.name ?? null,
      isActive: true
    };
    catalogMachines.push(machine);
    sendJson(response, 201, machine);
    return;
  }

  const catalogMachineMatch = url.pathname.match(/^\/api\/admin\/machines\/([^/]+)$/);
  if (catalogMachineMatch && (request.method === 'GET' || request.method === 'PUT')) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    const machine = catalogMachines.find((item) => item.id === catalogMachineMatch[1]);
    if (!machine) {
      sendJson(response, 404, { error: 'Machine was not found.' });
      return;
    }

    if (request.method === 'PUT') {
      const body = await readJson(request);
      machine.number = Number(body.number);
      machine.name = body.name ?? null;
    }

    sendJson(response, 200, machine);
    return;
  }

  const machineActivationMatch = url.pathname.match(/^\/api\/admin\/machines\/([^/]+)\/(activate|deactivate)$/);
  if (request.method === 'PATCH' && machineActivationMatch) {
    if (!requireAdminAccess(request, response)) {
      return;
    }

    const machine = catalogMachines.find((item) => item.id === machineActivationMatch[1]);
    if (!machine) {
      sendJson(response, 404, { error: 'Machine was not found.' });
      return;
    }

    machine.isActive = machineActivationMatch[2] === 'activate';
    sendJson(response, 200, machine);
    return;
  }

  sendJson(response, 404, { error: 'Not found' });
});

resetState();

server.listen(port, '127.0.0.1', () => {
  console.log(`Mock API listening on http://127.0.0.1:${port}`);
});

process.on('SIGTERM', () => server.close(() => process.exit(0)));
process.on('SIGINT', () => server.close(() => process.exit(0)));
