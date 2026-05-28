#!/usr/bin/env node
import { mkdir, readFile, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const importOrder = [
  { importType: 'products', fileName: 'products.csv' },
  { importType: 'machines', fileName: 'machines.csv' },
  { importType: 'customers', fileName: 'customers.csv' },
  { importType: 'customer-frequent-products', fileName: 'customer-frequent-products.csv' },
  { importType: 'customer-machine-assignments', fileName: 'customer-machine-assignments.csv' }
];

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, '../..');
const reportDir = path.join(repoRoot, 'data/local-imports/reports');

class ApiError extends Error {
  constructor(method, apiPath, status, body, text) {
    super(`${method} ${apiPath} failed with ${status}`);
    this.method = method;
    this.apiPath = apiPath;
    this.status = status;
    this.body = body;
    this.text = text;
  }
}

function usage() {
  return [
    'Usage:',
    '  node scripts/dev/import-folder.mjs validate <csv-folder>',
    '  node scripts/dev/import-folder.mjs apply <csv-folder> --confirm',
    '',
    'Environment:',
    '  PRODIMT_API_BASE_URL       default http://127.0.0.1:5088',
    '  PRODIMT_ADMIN_USERNAME     default admin in Development',
    '  PRODIMT_ADMIN_PASSWORD     default prodimt-admin-demo in Development'
  ].join('\n');
}

function pad(value) {
  return value.toString().padStart(2, '0');
}

function timestampForFile(date) {
  return [
    date.getFullYear(),
    pad(date.getMonth() + 1),
    pad(date.getDate())
  ].join('') + '-' + [
    pad(date.getHours()),
    pad(date.getMinutes()),
    pad(date.getSeconds())
  ].join('');
}

function sanitize(value, key = '') {
  const normalizedKey = key.toLowerCase();
  if (
    normalizedKey.includes('password') ||
    normalizedKey.includes('token') ||
    normalizedKey.includes('secret') ||
    normalizedKey.includes('authorization') ||
    normalizedKey.includes('connectionstring')
  ) {
    return '[REDACTED]';
  }

  if (Array.isArray(value)) {
    return value.map((item) => sanitize(item));
  }

  if (value && typeof value === 'object') {
    return Object.fromEntries(Object.entries(value).map(([entryKey, entryValue]) => [
      entryKey,
      sanitize(entryValue, entryKey)
    ]));
  }

  return value;
}

function relativeToRepo(filePath) {
  return path.relative(repoRoot, filePath).replaceAll(path.sep, '/');
}

async function existsAsFile(filePath) {
  try {
    const info = await stat(filePath);
    return info.isFile();
  } catch {
    return false;
  }
}

async function existsAsDirectory(filePath) {
  try {
    const info = await stat(filePath);
    return info.isDirectory();
  } catch {
    return false;
  }
}

async function fetchJson(apiBaseUrl, method, apiPath, body, accessToken) {
  const response = await fetch(`${apiBaseUrl}${apiPath}`, {
    method,
    headers: {
      accept: 'application/json',
      ...(body ? { 'content-type': 'application/json' } : {}),
      ...(accessToken ? { authorization: `Bearer ${accessToken}` } : {})
    },
    body: body ? JSON.stringify(body) : undefined
  });
  const text = await response.text();
  let payload = null;

  if (text) {
    try {
      payload = JSON.parse(text);
    } catch {
      payload = text;
    }
  }

  if (!response.ok) {
    throw new ApiError(method, apiPath, response.status, payload, text);
  }

  return payload;
}

async function loginAdmin(apiBaseUrl, userName, password) {
  const response = await fetchJson(apiBaseUrl, 'POST', '/api/auth/admin/login', {
    userName,
    password
  });

  if (!response?.accessToken) {
    throw new Error('Admin login did not return accessToken.');
  }

  return response;
}

async function discoverFiles(folderPath) {
  const found = [];
  const missing = [];

  for (const definition of importOrder) {
    const filePath = path.join(folderPath, definition.fileName);
    if (await existsAsFile(filePath)) {
      found.push({
        ...definition,
        absolutePath: filePath,
        relativePath: relativeToRepo(filePath)
      });
    } else {
      missing.push(definition.fileName);
    }
  }

  return { found, missing };
}

async function readReferenceContents(foundFiles) {
  const entries = await Promise.all(foundFiles.map(async (fileDefinition) => [
    fileDefinition.importType,
    await readFile(fileDefinition.absolutePath, 'utf8')
  ]));

  return Object.fromEntries(entries);
}

async function validateFile(apiBaseUrl, accessToken, fileDefinition, referenceContents) {
  const content = await readFile(fileDefinition.absolutePath, 'utf8');
  const response = await fetchJson(
    apiBaseUrl,
    'POST',
    `/api/admin/import/${fileDefinition.importType}/validate`,
    {
      content,
      fileName: fileDefinition.fileName,
      referenceContents
    },
    accessToken);

  return {
    importType: fileDefinition.importType,
    fileName: fileDefinition.fileName,
    path: fileDefinition.relativePath,
    response: sanitize(response)
  };
}

async function applyFile(apiBaseUrl, accessToken, fileDefinition) {
  const content = await readFile(fileDefinition.absolutePath, 'utf8');
  const response = await fetchJson(
    apiBaseUrl,
    'POST',
    `/api/admin/import/${fileDefinition.importType}/apply`,
    {
      content,
      fileName: fileDefinition.fileName
    },
    accessToken);

  return {
    importType: fileDefinition.importType,
    fileName: fileDefinition.fileName,
    path: fileDefinition.relativePath,
    response: sanitize(response)
  };
}

function validationHasBlockingErrors(validationResults) {
  return validationResults.some((result) => (result.response?.errorCount ?? 0) > 0);
}

function applyHasBlockingErrors(applyResults) {
  return applyResults.some((result) => (result.response?.errors?.length ?? 0) > 0);
}

function apiErrorToReport(error, fileDefinition) {
  if (error instanceof ApiError) {
    return {
      importType: fileDefinition?.importType ?? null,
      fileName: fileDefinition?.fileName ?? null,
      method: error.method,
      path: error.apiPath,
      status: error.status,
      body: sanitize(error.body),
      text: sanitize(error.text)
    };
  }

  return {
    importType: fileDefinition?.importType ?? null,
    fileName: fileDefinition?.fileName ?? null,
    message: error instanceof Error ? error.message : String(error)
  };
}

function buildRecommendations(report) {
  const recommendations = [];
  if (report.files.missing.length > 0) {
    recommendations.push('Confirmar si los archivos faltantes son intencionales; solo se procesan los CSV presentes.');
  }

  if (report.result === 'validation-errors') {
    recommendations.push('Corregir errores bloqueantes en los CSV y repetir validate antes de aplicar.');
  } else if (report.result === 'api-error') {
    recommendations.push('Revisar que la API este corriendo, que las credenciales admin sean correctas y repetir.');
  } else if (report.mode === 'validate' && report.result === 'ok') {
    recommendations.push('Revisar advertencias operativas y ejecutar apply con --confirm cuando la muestra sea aceptada.');
  } else if (report.mode === 'apply' && report.result === 'ok') {
    recommendations.push('Verificar catalogos en la UI admin y generar tokens de cliente desde el sistema.');
  } else if (report.result === 'no-files') {
    recommendations.push('Colocar al menos un CSV con nombre esperado en la carpeta de importacion.');
  }

  recommendations.push('No commitear archivos en data/local-imports, CSV reales, .xlsm, tokens ni reportes locales.');
  return recommendations;
}

function tableRow(cells) {
  return `| ${cells.join(' | ')} |`;
}

function buildMarkdown(report) {
  const lines = [
    `# Reporte ${report.mode === 'apply' ? 'aplicacion' : 'validacion'} importacion`,
    '',
    `- Fecha/hora: ${report.startedAt}`,
    `- API base: ${report.apiBaseUrl}`,
    `- Carpeta: ${report.folder}`,
    `- Resultado: ${report.result}`,
    `- Usuario admin: ${report.auth.userName} (${report.auth.source})`,
    '',
    '## Archivos',
    '',
    `- Encontrados: ${report.files.found.length > 0 ? report.files.found.join(', ') : 'ninguno'}`,
    `- Faltantes: ${report.files.missing.length > 0 ? report.files.missing.join(', ') : 'ninguno'}`
  ];

  if (report.validationResults.length > 0) {
    lines.push(
      '',
      '## Validacion',
      '',
      tableRow(['Tipo', 'Archivo', 'Total', 'Validas', 'Errores', 'Advertencias', 'Crear', 'Actualizar', 'Desactivar']),
      tableRow(['---', '---', '---:', '---:', '---:', '---:', '---:', '---:', '---:'])
    );

    for (const result of report.validationResults) {
      const response = result.response ?? {};
      lines.push(tableRow([
        result.importType,
        result.fileName,
        response.totalRows ?? 0,
        response.validRows ?? 0,
        response.errorCount ?? 0,
        response.warningCount ?? 0,
        response.proposedCreateCount ?? 0,
        response.proposedUpdateCount ?? 0,
        response.proposedDeactivateCount ?? 0
      ].map(String)));
    }
  }

  if (report.applyResults.length > 0) {
    lines.push(
      '',
      '## Aplicacion',
      '',
      tableRow(['Tipo', 'Archivo', 'Total', 'Creados', 'Actualizados', 'Omitidos', 'Advertencias', 'Auditoria', 'Errores']),
      tableRow(['---', '---', '---:', '---:', '---:', '---:', '---:', '---:', '---:'])
    );

    for (const result of report.applyResults) {
      const response = result.response ?? {};
      lines.push(tableRow([
        result.importType,
        result.fileName,
        response.totalRows ?? 0,
        response.createdCount ?? 0,
        response.updatedCount ?? 0,
        response.skippedCount ?? 0,
        response.warningCount ?? 0,
        response.auditLogIds?.length ?? 0,
        response.errors?.length ?? 0
      ].map(String)));
    }
  }

  if (report.apiErrors.length > 0) {
    lines.push('', '## Errores de API', '');
    for (const error of report.apiErrors) {
      lines.push(`- ${error.fileName ?? 'general'}: ${error.method ?? ''} ${error.path ?? ''} ${error.status ?? ''} ${error.message ?? ''}`.trim());
    }
  }

  lines.push('', '## Recomendaciones', '');
  for (const recommendation of report.recommendations) {
    lines.push(`- ${recommendation}`);
  }

  return `${lines.join('\n')}\n`;
}

async function writeReports(report) {
  await mkdir(reportDir, { recursive: true });
  const prefix = report.mode === 'apply' ? 'import-apply' : 'import-validation';
  const jsonPath = path.join(reportDir, `${prefix}-${report.timestamp}.json`);
  const mdPath = path.join(reportDir, `${prefix}-${report.timestamp}.md`);
  const reportWithPaths = {
    ...report,
    reportFiles: {
      json: relativeToRepo(jsonPath),
      markdown: relativeToRepo(mdPath)
    }
  };

  await writeFile(jsonPath, `${JSON.stringify(reportWithPaths, null, 2)}\n`, 'utf8');
  await writeFile(mdPath, buildMarkdown(reportWithPaths), 'utf8');
  return reportWithPaths.reportFiles;
}

async function main() {
  const [mode, folderArg, ...rest] = process.argv.slice(2);
  if (!['validate', 'apply'].includes(mode) || !folderArg) {
    console.error(usage());
    process.exit(1);
  }

  if (mode === 'apply' && !rest.includes('--confirm')) {
    console.error('apply requires --confirm.');
    console.error(usage());
    process.exit(1);
  }

  const startedAtDate = new Date();
  const folder = path.resolve(process.cwd(), folderArg);
  const apiBaseUrl = (process.env.PRODIMT_API_BASE_URL ?? 'http://127.0.0.1:5088').replace(/\/$/, '');
  const userName = process.env.PRODIMT_ADMIN_USERNAME ?? 'admin';
  const password = process.env.PRODIMT_ADMIN_PASSWORD ?? 'prodimt-admin-demo';
  const authSource = process.env.PRODIMT_ADMIN_USERNAME && process.env.PRODIMT_ADMIN_PASSWORD
    ? 'environment'
    : 'development-defaults';

  const report = {
    mode,
    timestamp: timestampForFile(startedAtDate),
    startedAt: startedAtDate.toISOString(),
    apiBaseUrl,
    folder: relativeToRepo(folder),
    importOrder: importOrder.map((item) => item.importType),
    auth: {
      userName,
      source: authSource
    },
    files: {
      expected: importOrder.map((item) => item.fileName),
      found: [],
      missing: []
    },
    validationResults: [],
    applyResults: [],
    apiErrors: [],
    result: 'ok',
    recommendations: []
  };

  if (!await existsAsDirectory(folder)) {
    report.result = 'api-error';
    report.apiErrors.push({ message: `Folder not found: ${folder}` });
    report.recommendations = buildRecommendations(report);
    await writeReports(report);
    console.error(`Folder not found: ${folder}`);
    process.exit(1);
  }

  const { found, missing } = await discoverFiles(folder);
  report.files.found = found.map((file) => file.fileName);
  report.files.missing = missing;

  if (found.length === 0) {
    report.result = 'no-files';
    report.recommendations = buildRecommendations(report);
    const reportFiles = await writeReports(report);
    console.error('No expected CSV files were found.');
    console.error(`JSON report: ${reportFiles.json}`);
    console.error(`Markdown report: ${reportFiles.markdown}`);
    process.exit(1);
  }

  let accessToken;
  try {
    const login = await loginAdmin(apiBaseUrl, userName, password);
    accessToken = login.accessToken;
    console.log(`Admin login OK: ${login.displayName ?? userName}`);
    if (authSource === 'development-defaults') {
      console.log('Using Development default admin credentials because PRODIMT_ADMIN_USERNAME/PASSWORD were not both set.');
    }
  } catch (error) {
    report.result = 'api-error';
    report.apiErrors.push(apiErrorToReport(error));
    report.recommendations = buildRecommendations(report);
    const reportFiles = await writeReports(report);
    console.error('Admin login failed. Check API URL and admin credentials.');
    console.error(`JSON report: ${reportFiles.json}`);
    console.error(`Markdown report: ${reportFiles.markdown}`);
    process.exit(1);
  }

  const referenceContents = await readReferenceContents(found);

  if (mode === 'validate') {
    for (const file of found) {
      try {
        console.log(`Validating ${file.fileName} as ${file.importType}...`);
        report.validationResults.push(await validateFile(apiBaseUrl, accessToken, file, referenceContents));
      } catch (error) {
        report.apiErrors.push(apiErrorToReport(error, file));
        report.result = 'api-error';
      }
    }

    if (report.result === 'api-error') {
      report.recommendations = buildRecommendations(report);
      const reportFiles = await writeReports(report);
      console.error('Validation failed because an API call failed.');
      console.error(`JSON report: ${reportFiles.json}`);
      console.error(`Markdown report: ${reportFiles.markdown}`);
      process.exit(1);
    }

    if (validationHasBlockingErrors(report.validationResults)) {
      report.result = 'validation-errors';
      report.recommendations = buildRecommendations(report);
      const reportFiles = await writeReports(report);
      console.error('Validation completed with blocking errors. Apply was not run.');
      console.error(`JSON report: ${reportFiles.json}`);
      console.error(`Markdown report: ${reportFiles.markdown}`);
      process.exit(2);
    }
  } else {
    for (const file of found) {
      try {
        console.log(`Validating ${file.fileName} as ${file.importType} before apply...`);
        const validationResult = await validateFile(apiBaseUrl, accessToken, file, referenceContents);
        report.validationResults.push(validationResult);
        if ((validationResult.response?.errorCount ?? 0) > 0) {
          report.result = 'validation-errors';
          break;
        }

        console.log(`Applying ${file.fileName} as ${file.importType}...`);
        const result = await applyFile(apiBaseUrl, accessToken, file);
        report.applyResults.push(result);
        if ((result.response?.errors?.length ?? 0) > 0) {
          report.result = 'validation-errors';
          break;
        }
      } catch (error) {
        report.apiErrors.push(apiErrorToReport(error, file));
        report.result = 'api-error';
        break;
      }
    }

    if (applyHasBlockingErrors(report.applyResults)) {
      report.result = 'validation-errors';
    }
  }

  report.recommendations = buildRecommendations(report);
  const reportFiles = await writeReports(report);
  console.log(`JSON report: ${reportFiles.json}`);
  console.log(`Markdown report: ${reportFiles.markdown}`);

  if (report.result !== 'ok') {
    process.exit(report.result === 'validation-errors' ? 2 : 1);
  }
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
});
