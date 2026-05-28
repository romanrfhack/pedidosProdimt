const apiBaseUrl = (process.env.PRODIMT_API_BASE_URL ?? 'http://127.0.0.1:5088').replace(/\/$/, '');
const demoCustomerId = process.env.PRODIMT_DEMO_CUSTOMER_ID ?? '11111111-1111-1111-1111-111111111111';
const otherCustomerId = process.env.PRODIMT_OTHER_CUSTOMER_ID ?? '11111111-1111-1111-1111-111111111112';
const demoCustomerToken = process.env.PRODIMT_DEMO_CUSTOMER_TOKEN ?? 'demo-customer-token';
const demoAdminUserName = process.env.PRODIMT_DEMO_ADMIN_USERNAME ?? 'admin';
const demoAdminPassword = process.env.PRODIMT_DEMO_ADMIN_PASSWORD ?? 'prodimt-admin-demo';

const forbiddenCustomerTokens = [
  'audit',
  'auditlog',
  'machine',
  'machineid',
  'assignedmachineid',
  'assignedmachine',
  'maquina'
];

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

async function fetchJson(path, options = {}) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...options,
    headers: {
      accept: 'application/json',
      ...(options.body ? { 'content-type': 'application/json' } : {}),
      ...(options.headers ?? {})
    }
  });
  const text = await response.text();
  let body = null;

  if (text) {
    try {
      body = JSON.parse(text);
    } catch {
      body = text;
    }
  }

  return {
    ok: response.ok,
    status: response.status,
    body,
    text
  };
}

async function requestJson(path, options = {}) {
  const result = await fetchJson(path, options);

  if (!result.ok) {
    throw new Error(`${options.method ?? 'GET'} ${path} failed with ${result.status}: ${result.text}`);
  }

  return result.body;
}

function bearer(accessToken) {
  return {
    authorization: `Bearer ${accessToken}`
  };
}

function assertUnauthorizedOrForbidden(result, label) {
  assert(result.status === 401 || result.status === 403, `${label} returned ${result.status}; expected 401 or 403.`);
}

function assertCustomerPayloadDoesNotExposeInternalData(payload) {
  const raw = JSON.stringify(payload).toLowerCase();
  const leakedToken = forbiddenCustomerTokens.find((token) => raw.includes(token));
  assert(!leakedToken, `Customer payload exposes internal information: ${leakedToken}`);
}

function assertArrayIncludesOrder(orders, orderId, label) {
  assert(Array.isArray(orders), `${label} response must be an array.`);
  assert(orders.some((order) => order.orderId === orderId), `${label} does not include order ${orderId}.`);
}

function assertAuditIncludes(auditLogs, eventType, label) {
  assert(Array.isArray(auditLogs), `${label} audit response must be an array.`);
  assert(auditLogs.some((event) => event.eventType === eventType), `${label} audit does not include ${eventType}.`);
}

function firstPositiveProduct(today) {
  assert(Array.isArray(today.products), 'Customer today response must include products.');
  assert(today.products.length > 0, 'Customer today response must include at least one product.');
  return today.products[0];
}

async function main() {
  console.log(`Smoke Fase 1 API: ${apiBaseUrl}`);

  const health = await requestJson('/health');
  assert(health.status === 'ok', 'GET /health did not return ok.');
  console.log('OK /health');

  const dbHealth = await requestJson('/health/db');
  assert(dbHealth.status === 'ok' && dbHealth.database === 'reachable', 'GET /health/db did not return reachable.');
  console.log('OK /health/db');

  const anonymousAdmin = await fetchJson('/api/admin/orders/today');
  assertUnauthorizedOrForbidden(anonymousAdmin, 'Anonymous admin endpoint');
  console.log(`OK anonymous admin rejected: ${anonymousAdmin.status}`);

  const anonymousCustomer = await fetchJson(`/api/customer-orders/${demoCustomerId}/today`);
  assertUnauthorizedOrForbidden(anonymousCustomer, 'Anonymous customer endpoint');
  console.log(`OK anonymous customer rejected: ${anonymousCustomer.status}`);

  const adminLogin = await requestJson('/api/auth/admin/login', {
    method: 'POST',
    body: JSON.stringify({
      userName: demoAdminUserName,
      password: demoAdminPassword
    })
  });
  assert(adminLogin.accessToken, 'Admin login did not return accessToken.');
  const adminHeaders = bearer(adminLogin.accessToken);
  console.log(`OK admin login: ${adminLogin.displayName}`);

  const customerLogin = await requestJson('/api/auth/customer-token', {
    method: 'POST',
    body: JSON.stringify({
      token: demoCustomerToken
    })
  });
  assert(customerLogin.accessToken, 'Customer login did not return accessToken.');
  assert(customerLogin.customerId === demoCustomerId, 'Customer login returned unexpected customerId.');
  const customerHeaders = bearer(customerLogin.accessToken);
  console.log(`OK customer token login: ${customerLogin.customerName}`);

  const forbiddenOtherCustomer = await fetchJson(`/api/customer-orders/${otherCustomerId}/no-order`, {
    method: 'POST',
    headers: customerHeaders,
    body: '{}'
  });
  assert(forbiddenOtherCustomer.status === 403, `Customer access to another customer returned ${forbiddenOtherCustomer.status}; expected 403.`);
  console.log('OK customer cannot operate another customerId');

  const today = await requestJson(`/api/customer-orders/${demoCustomerId}/today`, {
    headers: customerHeaders
  });
  assert(today.customerId === demoCustomerId, 'Customer today response returned an unexpected customer id.');
  assert(today.customerName === 'Gran Takito', 'Seed customer Gran Takito was not returned.');
  assert(today.products.length >= 1, 'Seed frequent products were not returned.');
  assertCustomerPayloadDoesNotExposeInternalData(today);
  console.log(`OK customer today: ${today.customerName}, products=${today.products.length}`);

  const noOrder = await requestJson(`/api/customer-orders/${demoCustomerId}/no-order`, {
    method: 'POST',
    headers: customerHeaders,
    body: '{}'
  });
  assert(noOrder.status === 'NoOrder', 'NoOrder endpoint did not return NoOrder status.');
  assertCustomerPayloadDoesNotExposeInternalData(noOrder);
  console.log(`OK no-order for customer ${demoCustomerId}: ${noOrder.orderId}`);

  const noOrderAudit = await requestJson(`/api/admin/orders/${noOrder.orderId}/audit`, {
    headers: adminHeaders
  });
  assertAuditIncludes(noOrderAudit, 'NoOrderMarked', 'NoOrder');
  console.log('OK no-order audit includes NoOrderMarked');

  const product = firstPositiveProduct(today);
  const firstOrder = await requestJson(`/api/customer-orders/${demoCustomerId}/submit`, {
    method: 'POST',
    headers: customerHeaders,
    body: JSON.stringify({
      lines: [
        {
          productId: product.productId,
          quantity: 1,
          notes: 'Smoke Fase 1'
        }
      ]
    })
  });
  assert(firstOrder.orderId, 'Submit response did not include orderId.');
  assertCustomerPayloadDoesNotExposeInternalData(firstOrder);
  console.log(`OK first submit: ${firstOrder.orderId}, status=${firstOrder.status}`);

  const customerAuditAttempt = await fetchJson(`/api/admin/orders/${firstOrder.orderId}/audit`, {
    headers: customerHeaders
  });
  assertUnauthorizedOrForbidden(customerAuditAttempt, 'Customer audit endpoint attempt');
  console.log(`OK customer cannot access audit: ${customerAuditAttempt.status}`);

  const firstOrderAudit = await requestJson(`/api/admin/orders/${firstOrder.orderId}/audit`, {
    headers: adminHeaders
  });
  assertAuditIncludes(firstOrderAudit, 'OrderSubmitted', 'First order');
  if (firstOrder.adminReviewReason === 'LateSubmission') {
    assertAuditIncludes(firstOrderAudit, 'OrderMarkedLate', 'First order');
    assertAuditIncludes(firstOrderAudit, 'OrderRequiresAdminReview', 'First order');
  }
  console.log('OK first order audit includes submission events');

  const todayAfterSubmit = await requestJson(`/api/customer-orders/${demoCustomerId}/today`, {
    headers: customerHeaders
  });
  assert(todayAfterSubmit.currentOrder?.orderId, 'Customer today response did not include currentOrder after submit.');
  assertCustomerPayloadDoesNotExposeInternalData(todayAfterSubmit);
  console.log(`OK currentOrder: ${todayAfterSubmit.currentOrder.orderId}`);

  const adminToday = await requestJson('/api/admin/orders/today', {
    headers: adminHeaders
  });
  assertArrayIncludesOrder(adminToday, firstOrder.orderId, 'Admin today');
  console.log('OK admin today includes submitted order');

  const secondOrder = await requestJson(`/api/customer-orders/${demoCustomerId}/submit`, {
    method: 'POST',
    headers: customerHeaders,
    body: JSON.stringify({
      lines: [
        {
          productId: product.productId,
          quantity: 2,
          notes: 'Smoke Fase 1 additional'
        }
      ]
    })
  });
  assert(secondOrder.orderId, 'Second submit response did not include orderId.');
  assert(secondOrder.requiresAdminReview === true, 'Second order did not require admin review.');
  assert(secondOrder.adminReviewReason === 'AdditionalOrderSameDay', 'Second order did not use AdditionalOrderSameDay.');
  assertCustomerPayloadDoesNotExposeInternalData(secondOrder);
  console.log(`OK second submit pending review: ${secondOrder.orderId}`);

  const secondOrderAudit = await requestJson(`/api/admin/orders/${secondOrder.orderId}/audit`, {
    headers: adminHeaders
  });
  assertAuditIncludes(secondOrderAudit, 'OrderSubmitted', 'Second order');
  assertAuditIncludes(secondOrderAudit, 'AdditionalOrderDetected', 'Second order');
  assert(secondOrderAudit.some((event) =>
    event.eventType === 'OrderRequiresAdminReview' &&
    event.adminReviewReason === 'AdditionalOrderSameDay'), 'Second order audit does not include AdditionalOrderSameDay review reason.');
  console.log('OK second order audit includes additional-order review events');

  const pendingReview = await requestJson('/api/admin/orders/pending-review', {
    headers: adminHeaders
  });
  assertArrayIncludesOrder(pendingReview, secondOrder.orderId, 'Pending review');
  console.log('OK pending review includes second order');

  const reviewed = await requestJson(`/api/admin/orders/${secondOrder.orderId}/review`, {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      decision: 'Accepted',
      internalNotes: 'Smoke Fase 1 accepted'
    })
  });
  assert(reviewed.adminDecision === 'Accepted', 'Admin review did not persist Accepted decision.');
  assert(reviewed.requiresAdminReview === false, 'Reviewed order still requires admin review.');
  console.log(`OK admin review accepted: ${reviewed.orderId}`);

  const reviewedAudit = await requestJson(`/api/admin/orders/${secondOrder.orderId}/audit`, {
    headers: adminHeaders
  });
  assert(reviewedAudit.some((event) =>
    event.eventType === 'AdminDecisionRecorded' &&
    event.adminDecision === 'Accepted'), 'Reviewed order audit does not include Accepted admin decision.');
  console.log('OK reviewed order audit includes admin decision');

  console.log('Smoke Fase 1 authenticated completed successfully.');
}

main().catch((error) => {
  console.error(error.message);
  process.exit(1);
});
