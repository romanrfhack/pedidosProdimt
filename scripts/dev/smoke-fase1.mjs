const apiBaseUrl = (process.env.PRODIMT_API_BASE_URL ?? 'http://127.0.0.1:5088').replace(/\/$/, '');
const demoCustomerId = process.env.PRODIMT_DEMO_CUSTOMER_ID ?? '11111111-1111-1111-1111-111111111111';
const noOrderCustomerId = process.env.PRODIMT_NO_ORDER_CUSTOMER_ID ?? '11111111-1111-1111-1111-111111111112';

const forbiddenCustomerTokens = [
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

async function requestJson(path, options = {}) {
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

  if (!response.ok) {
    throw new Error(`${options.method ?? 'GET'} ${path} failed with ${response.status}: ${text}`);
  }

  return body;
}

function assertCustomerPayloadDoesNotExposeMachine(payload) {
  const raw = JSON.stringify(payload).toLowerCase();
  const leakedToken = forbiddenCustomerTokens.find((token) => raw.includes(token));
  assert(!leakedToken, `Customer payload exposes internal machine information: ${leakedToken}`);
}

function assertArrayIncludesOrder(orders, orderId, label) {
  assert(Array.isArray(orders), `${label} response must be an array.`);
  assert(orders.some((order) => order.orderId === orderId), `${label} does not include order ${orderId}.`);
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

  const today = await requestJson(`/api/customer-orders/${demoCustomerId}/today`);
  assert(today.customerId === demoCustomerId, 'Customer today response returned an unexpected customer id.');
  assert(today.customerName === 'Gran Takito', 'Seed customer Gran Takito was not returned.');
  assert(today.products.length >= 1, 'Seed frequent products were not returned.');
  assertCustomerPayloadDoesNotExposeMachine(today);
  console.log(`OK customer today: ${today.customerName}, products=${today.products.length}`);

  const product = firstPositiveProduct(today);
  const firstOrder = await requestJson(`/api/customer-orders/${demoCustomerId}/submit`, {
    method: 'POST',
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
  console.log(`OK first submit: ${firstOrder.orderId}, status=${firstOrder.status}`);

  const todayAfterSubmit = await requestJson(`/api/customer-orders/${demoCustomerId}/today`);
  assert(todayAfterSubmit.currentOrder?.orderId, 'Customer today response did not include currentOrder after submit.');
  assertCustomerPayloadDoesNotExposeMachine(todayAfterSubmit);
  console.log(`OK currentOrder: ${todayAfterSubmit.currentOrder.orderId}`);

  const adminToday = await requestJson('/api/admin/orders/today');
  assertArrayIncludesOrder(adminToday, firstOrder.orderId, 'Admin today');
  console.log('OK admin today includes submitted order');

  const secondOrder = await requestJson(`/api/customer-orders/${demoCustomerId}/submit`, {
    method: 'POST',
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
  console.log(`OK second submit pending review: ${secondOrder.orderId}`);

  const pendingReview = await requestJson('/api/admin/orders/pending-review');
  assertArrayIncludesOrder(pendingReview, secondOrder.orderId, 'Pending review');
  console.log('OK pending review includes second order');

  const reviewed = await requestJson(`/api/admin/orders/${secondOrder.orderId}/review`, {
    method: 'POST',
    body: JSON.stringify({
      decision: 'Accepted',
      internalNotes: 'Smoke Fase 1 accepted'
    })
  });
  assert(reviewed.adminDecision === 'Accepted', 'Admin review did not persist Accepted decision.');
  assert(reviewed.requiresAdminReview === false, 'Reviewed order still requires admin review.');
  console.log(`OK admin review accepted: ${reviewed.orderId}`);

  const noOrder = await requestJson(`/api/customer-orders/${noOrderCustomerId}/no-order`, {
    method: 'POST',
    body: '{}'
  });
  assert(noOrder.status === 'NoOrder', 'NoOrder endpoint did not return NoOrder status.');
  console.log(`OK no-order for customer ${noOrderCustomerId}: ${noOrder.orderId}`);

  console.log('Smoke Fase 1 completed successfully.');
}

main().catch((error) => {
  console.error(error.message);
  process.exit(1);
});
