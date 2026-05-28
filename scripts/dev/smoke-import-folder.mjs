#!/usr/bin/env node
import { copyFile, mkdir, readdir, rm } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, '../..');
const apiBaseUrl = (process.env.PRODIMT_API_BASE_URL ?? 'http://127.0.0.1:5088').replace(/\/$/, '');
const adminUserName = process.env.PRODIMT_ADMIN_USERNAME ?? 'admin';
const adminPassword = process.env.PRODIMT_ADMIN_PASSWORD ?? 'prodimt-admin-demo';
const demoCustomerToken = process.env.PRODIMT_DEMO_CUSTOMER_TOKEN ?? 'demo-customer-token';
const reportDir = path.join(repoRoot, 'data/local-imports/reports');
const importedCustomerExternalCode = 'C-DEMO-001';

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

function bearer(accessToken) {
  return {
    authorization: `Bearer ${accessToken}`
  };
}

async function fetchJson(apiPath, options = {}) {
  const response = await fetch(`${apiBaseUrl}${apiPath}`, {
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

async function requestJson(apiPath, options = {}) {
  const result = await fetchJson(apiPath, options);
  if (!result.ok) {
    throw new Error(`${options.method ?? 'GET'} ${apiPath} failed with ${result.status}: ${result.text}`);
  }

  return result.body;
}

function assertUnauthorizedOrForbidden(result, label) {
  assert(result.status === 401 || result.status === 403, `${label} returned ${result.status}; expected 401 or 403.`);
}

function assertCustomerPayloadDoesNotExposeInternalData(payload) {
  const raw = JSON.stringify(payload).toLowerCase();
  const leakedToken = forbiddenCustomerTokens.find((token) => raw.includes(token));
  assert(!leakedToken, `Customer payload exposes internal information: ${leakedToken}`);
}

async function listReports(prefix) {
  try {
    const files = await readdir(reportDir);
    return new Set(files.filter((file) => file.startsWith(prefix)));
  } catch {
    return new Set();
  }
}

function hasNewReport(before, after) {
  return [...after].some((file) => !before.has(file));
}

function runScript(scriptName, args) {
  const scriptPath = path.join(repoRoot, 'scripts/dev', scriptName);
  const result = spawnSync('bash', [scriptPath, ...args], {
    cwd: repoRoot,
    env: process.env,
    encoding: 'utf8'
  });

  if (result.stdout) {
    process.stdout.write(result.stdout);
  }

  if (result.stderr) {
    process.stderr.write(result.stderr);
  }

  assert(result.status === 0, `${scriptName} exited with ${result.status}.`);
}

async function prepareSampleFolder() {
  const folder = path.join(repoRoot, 'data/local-imports', `smoke-import-${Date.now().toString(36)}`);
  const examples = path.join(repoRoot, 'docs/import-templates/examples');
  await mkdir(folder, { recursive: true });

  await copyFile(path.join(examples, 'products-demo.csv'), path.join(folder, 'products.csv'));
  await copyFile(path.join(examples, 'machines-demo.csv'), path.join(folder, 'machines.csv'));
  await copyFile(path.join(examples, 'customers-demo.csv'), path.join(folder, 'customers.csv'));
  await copyFile(path.join(examples, 'customer-frequent-products-demo.csv'), path.join(folder, 'customer-frequent-products.csv'));
  await copyFile(path.join(examples, 'customer-machine-assignments-demo.csv'), path.join(folder, 'customer-machine-assignments.csv'));

  return folder;
}

async function main() {
  console.log(`Smoke import folder API: ${apiBaseUrl}`);
  const sampleFolder = await prepareSampleFolder();
  const sampleFolderArg = path.relative(repoRoot, sampleFolder);

  try {
    const validationReportsBefore = await listReports('import-validation-');
    runScript('validate-import-folder.sh', [sampleFolderArg]);
    const validationReportsAfter = await listReports('import-validation-');
    assert(hasNewReport(validationReportsBefore, validationReportsAfter), 'validate-import-folder did not create a validation report.');
    console.log('OK validate-import-folder generated report');

    const applyReportsBefore = await listReports('import-apply-');
    runScript('apply-import-folder.sh', [sampleFolderArg, '--confirm']);
    const applyReportsAfter = await listReports('import-apply-');
    assert(hasNewReport(applyReportsBefore, applyReportsAfter), 'apply-import-folder did not create an apply report.');
    console.log('OK apply-import-folder generated report');

    const adminLogin = await requestJson('/api/auth/admin/login', {
      method: 'POST',
      body: JSON.stringify({
        userName: adminUserName,
        password: adminPassword
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
    const customerHeaders = bearer(customerLogin.accessToken);

    const importAttemptWithCustomerJwt = await fetchJson('/api/admin/import/customers/validate', {
      method: 'POST',
      headers: customerHeaders,
      body: JSON.stringify({
        content: 'externalCode,name,phoneNumber,isActive,preferredDeliveryTime,preferredDeliveryWindowStart,preferredDeliveryWindowEnd,deliveryNotes\nC-X,Cliente X,555,true,,,,',
        fileName: 'customers.csv'
      })
    });
    assertUnauthorizedOrForbidden(importAttemptWithCustomerJwt, 'Customer import endpoint attempt');
    console.log(`OK customer JWT cannot access import endpoints: ${importAttemptWithCustomerJwt.status}`);

    const importedCustomers = await requestJson('/api/admin/customers', {
      headers: adminHeaders
    });
    const importedCustomer = importedCustomers.find((customer) => customer.externalCode === importedCustomerExternalCode);
    assert(importedCustomer?.id, `Imported customer ${importedCustomerExternalCode} was not found.`);

    const importedCustomerToken = await requestJson(`/api/admin/customers/${importedCustomer.id}/access-tokens`, {
      method: 'POST',
      headers: adminHeaders,
      body: JSON.stringify({
        description: `Smoke import folder ${Date.now().toString(36)}`,
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
    assert(Array.isArray(importedCustomerToday.products) && importedCustomerToday.products.length > 0, 'Imported customer has no frequent products.');
    assertCustomerPayloadDoesNotExposeInternalData(importedCustomerToday);
    console.log('OK imported customer sees frequent products without machine data');
  } finally {
    await rm(sampleFolder, { recursive: true, force: true });
  }
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
});
