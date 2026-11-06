const { defineConfig, devices } = require('@playwright/test');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');

const directory = process.env.PRODIMT_C2_DIRECTORY;
if (!directory) throw new Error('C2 orchestration directory required');
const scene = JSON.parse(readFileSync(resolve(directory, 'browser-private.json'), 'utf8'));
const url = new URL(scene.baseUrl);
if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1' || !url.port || url.port === '5088') {
  throw new Error('C2 requires its own loopback API');
}

module.exports = defineConfig({
  testDir: './real',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 45000,
  expect: { timeout: 10000 },
  outputDir: resolve(directory, 'browser-results'),
  reporter: [['json', { outputFile: resolve(directory, 'playwright-real.json') }]],
  use: { baseURL: scene.baseUrl, trace: 'off', screenshot: 'only-on-failure', video: 'off' },
  // The owned Kestrel process serves the production Angular build and the API
  // at one origin. No reusable webServer, mocks, interceptors or public URLs.
  projects: [{ name: 'chromium-real', use: { ...devices['Desktop Chrome'],
    ...(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH ? {
      launchOptions: { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH }
    } : {}) } }]
});
