const apiBaseUrl = (process.env.PRODIMT_API_BASE_URL ?? 'http://127.0.0.1:5088').replace(/\/$/, '');
const demoCustomerId = process.env.PRODIMT_DEMO_CUSTOMER_ID ?? '11111111-1111-1111-1111-111111111111';
const otherCustomerId = process.env.PRODIMT_OTHER_CUSTOMER_ID ?? '11111111-1111-1111-1111-111111111112';
const thirdCustomerId = process.env.PRODIMT_THIRD_CUSTOMER_ID ?? '11111111-1111-1111-1111-111111111113';
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

function assertArrayExcludesCustomer(customers, customerId, label) {
  assert(Array.isArray(customers), `${label} response must be an array.`);
  assert(!customers.some((customer) => customer.customerId === customerId), `${label} still includes customer ${customerId}.`);
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

  const smokeSuffix = Date.now().toString(36);
  const importCustomerCode = `SMK-C-${smokeSuffix}`;
  const importProductCode = `SMK-P-${smokeSuffix}`;
  const importMachineCode = `SMK-M-${smokeSuffix}`;
  const importMachineNumber = 800000 + Math.floor(Math.random() * 90000);
  const importCustomerCsv = [
    'externalCode,name,phoneNumber,isActive,preferredDeliveryTime,preferredDeliveryWindowStart,preferredDeliveryWindowEnd,deliveryNotes',
    `${importCustomerCode},Smoke Import Cliente ${smokeSuffix},0000008800,true,10:30,,,Cliente importado por smoke`
  ].join('\n');
  const importProductCsv = [
    'externalCode,name,description,isActive',
    `${importProductCode},Smoke Import Molde ${smokeSuffix},Producto importado por smoke,true`
  ].join('\n');
  const importFrequentCsv = [
    'customerExternalCode,customerName,productExternalCode,productName,defaultQuantity,sortOrder,isActive',
    `${importCustomerCode},,${importProductCode},,9,1,true`
  ].join('\n');
  const importMachineCsv = [
    'externalCode,number,name,isActive',
    `${importMachineCode},${importMachineNumber},Maquina importada smoke,true`
  ].join('\n');
  const importAssignmentCsv = [
    'customerExternalCode,customerName,machineExternalCode,machineNumber,isDefault,notes',
    `${importCustomerCode},,${importMachineCode},,true,Asignacion importada por smoke`
  ].join('\n');

  const importAttemptWithCustomerJwt = await fetchJson('/api/admin/import/customers/validate', {
    method: 'POST',
    headers: customerHeaders,
    body: JSON.stringify({
      content: importCustomerCsv,
      fileName: 'customers.csv'
    })
  });
  assertUnauthorizedOrForbidden(importAttemptWithCustomerJwt, 'Customer import endpoint attempt');
  console.log(`OK customer JWT cannot access import endpoints: ${importAttemptWithCustomerJwt.status}`);

  const importCustomerValidation = await requestJson('/api/admin/import/customers/validate', {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      content: importCustomerCsv,
      fileName: 'customers.csv'
    })
  });
  assert(importCustomerValidation.errorCount === 0, 'Customer import validation returned errors.');
  assert(importCustomerValidation.proposedCreateCount === 1, 'Customer import validation did not propose one create.');
  console.log('OK import customers dry-run');

  const importCustomerApply = await requestJson('/api/admin/import/customers/apply', {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      content: importCustomerCsv,
      fileName: 'customers.csv'
    })
  });
  assert(importCustomerApply.createdCount === 1, 'Customer import apply did not create one customer.');
  assert(importCustomerApply.auditLogIds?.length >= 1, 'Customer import apply did not return audit ids.');
  console.log('OK import customers apply');

  const importedCustomers = await requestJson('/api/admin/customers', {
    headers: adminHeaders
  });
  const importedCustomer = importedCustomers.find((customer) => customer.externalCode === importCustomerCode);
  assert(importedCustomer?.id, 'Imported customer was not found in catalog.');

  const importProductValidation = await requestJson('/api/admin/import/products/validate', {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      content: importProductCsv,
      fileName: 'products.csv'
    })
  });
  assert(importProductValidation.errorCount === 0, 'Product import validation returned errors.');

  const importProductApply = await requestJson('/api/admin/import/products/apply', {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      content: importProductCsv,
      fileName: 'products.csv'
    })
  });
  assert(importProductApply.createdCount === 1, 'Product import apply did not create one product.');
  console.log('OK import products validate/apply');

  const importedProducts = await requestJson('/api/admin/products', {
    headers: adminHeaders
  });
  const importedProduct = importedProducts.find((product) => product.externalCode === importProductCode);
  assert(importedProduct?.id, 'Imported product was not found in catalog.');

  const importFrequentValidation = await requestJson('/api/admin/import/customer-frequent-products/validate', {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      content: importFrequentCsv,
      fileName: 'customer-frequent-products.csv'
    })
  });
  assert(importFrequentValidation.errorCount === 0, 'Frequent products import validation returned errors.');

  const importFrequentApply = await requestJson('/api/admin/import/customer-frequent-products/apply', {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      content: importFrequentCsv,
      fileName: 'customer-frequent-products.csv'
    })
  });
  assert(importFrequentApply.updatedCount === 1, 'Frequent products import apply did not update one customer.');
  console.log('OK import frequent products validate/apply');

  const importMachineApply = await requestJson('/api/admin/import/machines/apply', {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      content: importMachineCsv,
      fileName: 'machines.csv'
    })
  });
  assert(importMachineApply.createdCount === 1, 'Machine import apply did not create one machine.');

  const importAssignmentValidation = await requestJson('/api/admin/import/customer-machine-assignments/validate', {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      content: importAssignmentCsv,
      fileName: 'customer-machine-assignments.csv'
    })
  });
  assert(importAssignmentValidation.errorCount === 0, 'Machine assignment import validation returned errors.');

  const importAssignmentApply = await requestJson('/api/admin/import/customer-machine-assignments/apply', {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      content: importAssignmentCsv,
      fileName: 'customer-machine-assignments.csv'
    })
  });
  assert(importAssignmentApply.updatedCount === 1, 'Machine assignment import apply did not update one customer.');
  console.log('OK import machines and assignments validate/apply');

  const importedCustomerToken = await requestJson(`/api/admin/customers/${importedCustomer.id}/access-tokens`, {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      description: `Token import smoke ${smokeSuffix}`,
      expiresAt: null
    })
  });
  assert(importedCustomerToken.plainToken, 'Imported customer token did not return plainToken.');

  const importedCustomerLogin = await requestJson('/api/auth/customer-token', {
    method: 'POST',
    body: JSON.stringify({
      token: importedCustomerToken.plainToken
    })
  });
  assert(importedCustomerLogin.customerId === importedCustomer.id, 'Imported customer login returned unexpected customer id.');
  const importedCustomerHeaders = bearer(importedCustomerLogin.accessToken);
  const importedCustomerToday = await requestJson(`/api/customer-orders/${importedCustomer.id}/today`, {
    headers: importedCustomerHeaders
  });
  assert(importedCustomerToday.products.some((item) => item.productId === importedProduct.id), 'Imported customer does not see imported frequent product.');
  assertCustomerPayloadDoesNotExposeInternalData(importedCustomerToday);
  console.log('OK imported customer login sees frequent product without machine data');

  const catalogCustomer = await requestJson('/api/admin/customers', {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      name: `Smoke Cliente ${smokeSuffix}`,
      phoneNumber: '0000009999',
      preferredDeliveryTime: null,
      preferredDeliveryWindowStart: '11:00',
      preferredDeliveryWindowEnd: '12:00',
      deliveryNotes: 'Cliente temporal smoke catalogos'
    })
  });
  assert(catalogCustomer.id, 'Catalog customer create did not return id.');
  console.log(`OK catalog customer created: ${catalogCustomer.id}`);

  const updatedCatalogCustomer = await requestJson(`/api/admin/customers/${catalogCustomer.id}`, {
    method: 'PUT',
    headers: adminHeaders,
    body: JSON.stringify({
      name: catalogCustomer.name,
      phoneNumber: catalogCustomer.phoneNumber,
      preferredDeliveryTime: '11:30',
      preferredDeliveryWindowStart: null,
      preferredDeliveryWindowEnd: null,
      deliveryNotes: 'Horario actualizado por smoke catalogos'
    })
  });
  assert(updatedCatalogCustomer.preferredDeliveryTime?.startsWith('11:30'), 'Catalog customer delivery time was not updated.');
  console.log('OK catalog customer delivery updated');

  const catalogProduct = await requestJson('/api/admin/products', {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      name: `Smoke Molde ${smokeSuffix}`,
      description: 'Producto temporal smoke catalogos'
    })
  });
  assert(catalogProduct.id, 'Catalog product create did not return id.');
  console.log(`OK catalog product created: ${catalogProduct.id}`);

  const frequentConfig = await requestJson(`/api/admin/customers/${catalogCustomer.id}/frequent-products`, {
    method: 'PUT',
    headers: adminHeaders,
    body: JSON.stringify({
      items: [
        {
          productId: catalogProduct.id,
          defaultQuantity: 7,
          sortOrder: 1,
          isActive: true
        }
      ]
    })
  });
  assert(frequentConfig.some((item) => item.productId === catalogProduct.id), 'Frequent product config did not include smoke product.');
  console.log('OK catalog frequent product configured');

  const createdCustomerToken = await requestJson(`/api/admin/customers/${catalogCustomer.id}/access-tokens`, {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      description: `Token smoke ${smokeSuffix}`,
      expiresAt: null
    })
  });
  assert(createdCustomerToken.plainToken, 'Created customer token did not return plainToken.');

  const listedCustomerTokens = await requestJson(`/api/admin/customers/${catalogCustomer.id}/access-tokens`, {
    headers: adminHeaders
  });
  assert(!JSON.stringify(listedCustomerTokens).includes(createdCustomerToken.plainToken), 'Token list exposed plain token.');
  console.log('OK customer access token created and plain token is one-time only');

  const smokeCustomerLogin = await requestJson('/api/auth/customer-token', {
    method: 'POST',
    body: JSON.stringify({
      token: createdCustomerToken.plainToken
    })
  });
  assert(smokeCustomerLogin.customerId === catalogCustomer.id, 'Generated token login returned unexpected customer id.');
  const smokeCustomerHeaders = bearer(smokeCustomerLogin.accessToken);
  console.log('OK generated customer token allows login');

  const smokeCustomerToday = await requestJson(`/api/customer-orders/${catalogCustomer.id}/today`, {
    headers: smokeCustomerHeaders
  });
  assert(smokeCustomerToday.products.some((item) => item.productId === catalogProduct.id), 'Customer today did not include configured frequent product.');
  assertCustomerPayloadDoesNotExposeInternalData(smokeCustomerToday);
  console.log('OK customer sees configured frequent product without machine data');

  const catalogAttemptWithCustomerJwt = await fetchJson('/api/admin/customers', {
    headers: smokeCustomerHeaders
  });
  assertUnauthorizedOrForbidden(catalogAttemptWithCustomerJwt, 'Customer catalog endpoint attempt');
  console.log(`OK customer JWT cannot access catalog endpoints: ${catalogAttemptWithCustomerJwt.status}`);

  const catalogMachine = await requestJson('/api/admin/machines', {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      number: 900000 + Math.floor(Math.random() * 90000),
      name: `Maquina smoke ${smokeSuffix}`
    })
  });
  assert(catalogMachine.id, 'Catalog machine create did not return id.');

  const machineAssignments = await requestJson(`/api/admin/customers/${catalogCustomer.id}/machine-assignments`, {
    method: 'PUT',
    headers: adminHeaders,
    body: JSON.stringify({
      items: [
        {
          machineId: catalogMachine.id,
          isDefault: true,
          isActive: true,
          notes: 'Asignacion smoke catalogos'
        }
      ]
    })
  });
  assert(machineAssignments.some((item) => item.machineId === catalogMachine.id && item.isDefault), 'Machine assignment did not include default smoke machine.');

  const smokeCustomerTodayAfterMachine = await requestJson(`/api/customer-orders/${catalogCustomer.id}/today`, {
    headers: smokeCustomerHeaders
  });
  assertCustomerPayloadDoesNotExposeInternalData(smokeCustomerTodayAfterMachine);
  console.log('OK machine assignment remains hidden from customer');

  await requestJson(`/api/admin/customers/${catalogCustomer.id}/access-tokens/${createdCustomerToken.tokenId}/revoke`, {
    method: 'PATCH',
    headers: adminHeaders
  });
  const revokedTokenLogin = await fetchJson('/api/auth/customer-token', {
    method: 'POST',
    body: JSON.stringify({
      token: createdCustomerToken.plainToken
    })
  });
  assert(revokedTokenLogin.status === 401, `Revoked token login returned ${revokedTokenLogin.status}; expected 401.`);
  console.log('OK revoked customer token cannot login');

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

  const firstOrderDetail = await requestJson(`/api/admin/orders/${firstOrder.orderId}`, {
    headers: adminHeaders
  });
  assert(firstOrderDetail.orderId === firstOrder.orderId, 'Admin detail returned unexpected order id.');
  assert(Array.isArray(firstOrderDetail.lines) && firstOrderDetail.lines.length > 0, 'Admin detail did not include order lines.');
  assert(firstOrderDetail.lines[0].productName, 'Admin detail line did not include productName.');
  assert('assignedMachineId' in firstOrderDetail.lines[0], 'Admin detail line did not include assignedMachineId.');
  console.log(`OK admin order detail includes lines: ${firstOrderDetail.lines.length}`);

  const customerDetailAttempt = await fetchJson(`/api/admin/orders/${firstOrder.orderId}`, {
    headers: customerHeaders
  });
  assertUnauthorizedOrForbidden(customerDetailAttempt, 'Customer admin detail endpoint attempt');
  console.log(`OK customer cannot access admin detail: ${customerDetailAttempt.status}`);

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

  const pendingCustomers = await requestJson('/api/admin/customers/pending-orders', {
    headers: adminHeaders
  });
  assert(Array.isArray(pendingCustomers) && pendingCustomers.length >= 2, 'Pending customers did not include expected demo customers.');
  assert(pendingCustomers.some((customer) => customer.customerId === otherCustomerId), 'Pending customers did not include Demo Customer 2.');
  assert(pendingCustomers.some((customer) => customer.customerId === thirdCustomerId), 'Pending customers did not include Demo Customer 3.');
  console.log(`OK admin pending customers: ${pendingCustomers.length}`);

  const adminNoOrder = await requestJson(`/api/admin/customers/${otherCustomerId}/orders/no-order`, {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      internalNotes: 'Smoke admin no-order'
    })
  });
  assert(adminNoOrder.status === 'NoOrder', 'Admin no-order did not return NoOrder status.');
  const pendingAfterNoOrder = await requestJson('/api/admin/customers/pending-orders', {
    headers: adminHeaders
  });
  assertArrayExcludesCustomer(pendingAfterNoOrder, otherCustomerId, 'Pending customers after admin no-order');
  console.log('OK admin no-order removes customer from pending list');

  const adminNoOrderAudit = await requestJson(`/api/admin/orders/${adminNoOrder.orderId}/audit`, {
    headers: adminHeaders
  });
  assertAuditIncludes(adminNoOrderAudit, 'AdminNoOrderMarked', 'Admin no-order');
  console.log('OK admin no-order audit includes AdminNoOrderMarked');

  const adminTemplate = await requestJson(`/api/admin/customers/${thirdCustomerId}/order-template`, {
    headers: adminHeaders
  });
  assert(adminTemplate.customerId === thirdCustomerId, 'Admin template returned unexpected customer id.');
  const adminProduct = firstPositiveProduct(adminTemplate);
  console.log(`OK admin order template: products=${adminTemplate.products.length}`);

  const adminCaptured = await requestJson(`/api/admin/customers/${thirdCustomerId}/orders/submit`, {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      lines: [
        {
          productId: adminProduct.productId,
          quantity: 3,
          notes: 'Smoke admin capture'
        }
      ],
      requestedDeliveryTime: '12:00',
      requestedDeliveryWindowStart: null,
      requestedDeliveryWindowEnd: null,
      deliveryNotes: 'Captura administrativa smoke',
      internalNotes: 'Capturado por llamada smoke'
    })
  });
  assert(adminCaptured.orderId, 'Admin capture response did not include orderId.');
  console.log(`OK admin captured order: ${adminCaptured.orderId}`);

  const adminCapturedAudit = await requestJson(`/api/admin/orders/${adminCaptured.orderId}/audit`, {
    headers: adminHeaders
  });
  assertAuditIncludes(adminCapturedAudit, 'AdminManualOrderCaptured', 'Admin capture');
  console.log('OK admin capture audit includes AdminManualOrderCaptured');

  const adminSecond = await requestJson(`/api/admin/customers/${thirdCustomerId}/orders/submit`, {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      lines: [
        {
          productId: adminProduct.productId,
          quantity: 4,
          notes: 'Smoke admin second capture'
        }
      ],
      requestedDeliveryTime: '12:30',
      requestedDeliveryWindowStart: null,
      requestedDeliveryWindowEnd: null,
      deliveryNotes: 'Segundo pedido administrativo smoke',
      internalNotes: 'Segundo pedido por llamada smoke'
    })
  });
  assert(adminSecond.requiresAdminReview === true, 'Second admin capture did not require admin review.');
  assert(adminSecond.adminReviewReason === 'AdditionalOrderSameDay', 'Second admin capture did not use AdditionalOrderSameDay.');
  console.log(`OK second admin capture pending review: ${adminSecond.orderId}`);

  const adminSecondAudit = await requestJson(`/api/admin/orders/${adminSecond.orderId}/audit`, {
    headers: adminHeaders
  });
  assertAuditIncludes(adminSecondAudit, 'AdminManualOrderCaptured', 'Second admin capture');
  assertAuditIncludes(adminSecondAudit, 'AdditionalOrderDetected', 'Second admin capture');
  console.log('OK second admin capture audit includes additional-order events');

  const adminSecondDetail = await requestJson(`/api/admin/orders/${adminSecond.orderId}`, {
    headers: adminHeaders
  });
  const adminSecondLine = adminSecondDetail.lines[0];
  assert(adminSecondLine.orderLineId, 'Second admin capture detail did not include line id.');

  const reviewed = await requestJson(`/api/admin/orders/${adminSecond.orderId}/review`, {
    method: 'POST',
    headers: adminHeaders,
    body: JSON.stringify({
      decision: 'AcceptedWithChanges',
      internalNotes: 'Smoke Fase 1 accepted with changes',
      requestedDeliveryTime: '13:00',
      requestedDeliveryWindowStart: null,
      requestedDeliveryWindowEnd: null,
      deliveryNotes: 'Smoke cambio de entrega',
      lineAdjustments: [
        {
          orderLineId: adminSecondLine.orderLineId,
          quantity: 5,
          notes: 'Smoke ajuste de cantidad'
        }
      ]
    })
  });
  assert(reviewed.adminDecision === 'AcceptedWithChanges', 'Admin review did not persist AcceptedWithChanges decision.');
  assert(reviewed.requiresAdminReview === false, 'Reviewed order still requires admin review.');
  console.log(`OK admin review accepted with changes: ${reviewed.orderId}`);

  const reviewedAudit = await requestJson(`/api/admin/orders/${adminSecond.orderId}/audit`, {
    headers: adminHeaders
  });
  assert(reviewedAudit.some((event) =>
    event.eventType === 'AdminDecisionRecorded' &&
    event.adminDecision === 'AcceptedWithChanges'), 'Reviewed order audit does not include AcceptedWithChanges admin decision.');
  assertAuditIncludes(reviewedAudit, 'AdminOrderChanged', 'Reviewed admin order');
  console.log('OK reviewed order audit includes admin decision and change event');

  const changedDetail = await requestJson(`/api/admin/orders/${adminSecond.orderId}`, {
    headers: adminHeaders
  });
  assert(changedDetail.requestedDeliveryTime?.startsWith('13:00'), 'AcceptedWithChanges did not persist requestedDeliveryTime.');
  assert(changedDetail.deliveryNotes === 'Smoke cambio de entrega', 'AcceptedWithChanges did not persist deliveryNotes.');
  assert(changedDetail.lines.some((line) => line.quantity === 5), 'AcceptedWithChanges did not persist line quantity adjustment.');
  console.log('OK AcceptedWithChanges persisted delivery and line changes');

  console.log('Smoke Fase 1 authenticated completed successfully.');
}

main().catch((error) => {
  console.error(error.message);
  process.exit(1);
});
