import fs from 'node:fs';
import crypto from 'node:crypto';
import path from 'node:path';
import { stripVTControlCharacters } from 'node:util';
import { pathToFileURL } from 'node:url';

const messageLimit = 1500;
const cleanMessage = value => Array.from(stripVTControlCharacters(String(value ?? '')))
  .slice(0, messageLimit).join('');
const errorMessage = error => typeof error === 'string' ? error
  : error?.message ?? error?.value ?? error?.stack ?? 'Error without a message';

// Diagnostic only. This helper never creates or changes an installation gate.
export function summarizeReport(report, reportSha256) {
  const failures = [];
  let collected = 0;
  const visit = (suites, parents = []) => {
    for (const suite of suites ?? []) {
      const titles = [...parents, suite.title].filter(Boolean);
      for (const spec of suite.specs ?? []) {
        for (const test of spec.tests ?? []) {
          collected++;
          if (!['unexpected', 'flaky'].includes(test.status)) continue;
          const messages = [];
          for (const result of test.results ?? []) {
            const errors = result.errors?.length ? result.errors : result.error ? [result.error] : [];
            for (const error of errors) {
              const message = stripVTControlCharacters(String(errorMessage(error)));
              if (!messages.includes(message)) messages.push(message);
            }
          }
          failures.push({
            file: spec.file ?? suite.file ?? null,
            line: spec.line ?? null,
            column: spec.column ?? null,
            title: [...titles, spec.title].filter(Boolean).join(' › '),
            project: test.projectName ?? null,
            status: test.status,
            expectedStatus: test.expectedStatus ?? null,
            actualStatuses: (test.results ?? []).map(result => result.status),
            message: cleanMessage(messages.join('\n\n') ||
              `Expected ${test.expectedStatus ?? 'passed'}; actual ${(test.results ?? []).map(result => result.status).join(', ') || test.status}.`)
          });
        }
      }
      visit(suite.suites, titles);
    }
  };
  visit(report.suites);
  return {
    schema: 'prodimt.c1.browser-diagnostic.v1',
    status: 'failed',
    authorizesInstall: false,
    api: 'mock',
    reportSha256,
    collected,
    stats: report.stats ?? null,
    failures,
    runnerErrors: (report.errors ?? []).map(error => ({
      file: error.location?.file ?? null,
      line: error.location?.line ?? null,
      message: cleanMessage(errorMessage(error))
    }))
  };
}

export function summarizeFile(reportPath, summaryPath) {
  const raw = fs.readFileSync(reportPath);
  const report = JSON.parse(raw.toString('utf8'));
  if (!report || typeof report !== 'object' || !Array.isArray(report.suites)) {
    throw new Error('The Playwright report has no suite collection.');
  }
  const summary = summarizeReport(report, crypto.createHash('sha256').update(raw).digest('hex'));
  fs.writeFileSync(summaryPath, `${JSON.stringify(summary, null, 2)}\n`, { mode: 0o600 });
  return summary;
}

export function printSummary(summary) {
  const stats = summary.stats ?? {};
  console.error(`C1 browser diagnostic: ${summary.collected} collected; ${stats.expected ?? 0} passed; ${stats.unexpected ?? 0} failed; ${stats.skipped ?? 0} skipped; ${stats.flaky ?? 0} flaky.`);
  for (const failure of summary.failures) {
    console.error(`\n[${failure.status}] ${failure.file ?? '(unknown file)'}:${failure.line ?? '?'} — ${failure.title}`);
    console.error(failure.message);
  }
  for (const error of summary.runnerErrors) {
    console.error(`\n[runner] ${error.file ?? '(outside a test)'}${error.line ? `:${error.line}` : ''}`);
    console.error(error.message);
  }
  console.error('\nThis diagnostic authorizes no installation. The original Playwright report and full log remain available.');
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    if (process.argv.length !== 4) throw new Error('Usage: node summarize-c1-browser.mjs PLAYWRIGHT_JSON SUMMARY_JSON');
    const summary = summarizeFile(process.argv[2], process.argv[3]);
    printSummary(summary);
    console.error(`Diagnostic JSON: ${process.argv[3]}`);
  } catch (error) {
    console.error(`Cannot summarize the browser report: ${cleanMessage(error.message)}`);
    process.exitCode = 1;
  }
}
