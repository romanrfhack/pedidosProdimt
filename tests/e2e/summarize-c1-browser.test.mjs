import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import { summarizeFile, summarizeReport } from './summarize-c1-browser.mjs';

test('diagnostic keeps nested failed/flaky cases and global errors without treating expected failures as defects', () => {
  const report = {
    suites: [{ title: 'fixture.spec.ts', file: 'specs/fixture.spec.ts', suites: [{
      title: 'portal', specs: [
        { title: 'quantity remains visible', file: 'specs/fixture.spec.ts', line: 21, column: 5,
          tests: [{ projectName: 'chromium', expectedStatus: 'passed', status: 'unexpected', results: [
            { status: 'failed', errors: [{ message: '\u001b[31mMissing quantity\u001b[39m' }], error: { message: 'duplicate fallback should not appear' } }
          ] }] },
        { title: 'retry changes its result', file: 'specs/fixture.spec.ts', line: 31, tests: [
          { projectName: 'chromium', expectedStatus: 'passed', status: 'flaky', results: [
            { status: 'timedOut', error: { message: 'Wait timed out' } }, { status: 'passed' }
          ] }
        ] },
        { title: 'expected failure', tests: [{ expectedStatus: 'failed', status: 'expected', results: [{ status: 'failed', error: { message: 'intentional' } }] }] },
        { title: 'success', tests: [{ expectedStatus: 'passed', status: 'expected', results: [{ status: 'passed' }] }] }
      ]
    }] }],
    stats: { expected: 2, unexpected: 1, skipped: 0, flaky: 1 },
    errors: [{ message: '\u001b[33mServer stopped unexpectedly\u001b[0m', location: { file: 'playwright.config.js', line: 25 } }]
  };
  const summary = summarizeReport(report, 'a'.repeat(64));
  assert.equal(summary.collected, 4);
  assert.equal(summary.status, 'failed');
  assert.equal(summary.authorizesInstall, false);
  assert.deepEqual(summary.failures.map(item => item.status), ['unexpected', 'flaky']);
  assert.equal(summary.failures[0].file, 'specs/fixture.spec.ts');
  assert.equal(summary.failures[0].line, 21);
  assert.equal(summary.failures[0].title, 'fixture.spec.ts › portal › quantity remains visible');
  assert.equal(summary.failures[0].message, 'Missing quantity');
  assert.equal(summary.failures[1].message, 'Wait timed out');
  assert.deepEqual(summary.runnerErrors, [{ file: 'playwright.config.js', line: 25, message: 'Server stopped unexpectedly' }]);
});

test('diagnostic bounds each message and reports unexpected success and runner-only failures', () => {
  const report = { suites: [{ title: 'edge.spec.ts', specs: [
    { title: 'long failure', line: 7, tests: [{ expectedStatus: 'passed', status: 'unexpected', results: [
      { status: 'failed', errors: [{ message: '😀'.repeat(2000) }] }
    ] }] },
    { title: 'unexpected success', line: 11, tests: [{ expectedStatus: 'failed', status: 'unexpected', results: [{ status: 'passed' }] }] }
  ] }], errors: [{ message: 'Runner error' }], stats: { expected: 0, unexpected: 2, skipped: 0, flaky: 0 } };
  const summary = summarizeReport(report, 'b'.repeat(64));
  assert.equal(Array.from(summary.failures[0].message).length, 1500);
  assert.match(summary.failures[1].message, /Expected failed; actual passed/);
  assert.equal(summary.runnerErrors[0].message, 'Runner error');
  const runnerOnly = summarizeReport({ suites: [], errors: [{ message: 'Web server cannot start' }] }, 'c'.repeat(64));
  assert.equal(runnerOnly.collected, 0);
  assert.equal(runnerOnly.runnerErrors.length, 1);
  assert.equal(runnerOnly.authorizesInstall, false);
});

test('file summarization preserves original bytes/hash and never produces installation evidence, even for a passing report', () => {
  const folder = fs.mkdtempSync(path.join(os.tmpdir(), 'c1-summary-test-'));
  try {
    const reportPath = path.join(folder, 'playwright.json');
    const summaryPath = path.join(folder, 'diagnostico-resumen.json');
    const original = JSON.stringify({ suites: [], errors: [], stats: { expected: 240, unexpected: 0, skipped: 0, flaky: 0 } }) + '\n';
    fs.writeFileSync(reportPath, original);
    const summary = summarizeFile(reportPath, summaryPath);
    assert.equal(fs.readFileSync(reportPath, 'utf8'), original);
    assert.equal(summary.reportSha256, crypto.createHash('sha256').update(original).digest('hex'));
    assert.equal(summary.status, 'failed');
    assert.equal(summary.authorizesInstall, false);
    assert.notEqual(summary.schema, 'prodimt.c1.browser-gate.v1');
    assert.deepEqual(JSON.parse(fs.readFileSync(summaryPath, 'utf8')), summary);
  } finally {
    fs.rmSync(folder, { recursive: true, force: true });
  }
});
