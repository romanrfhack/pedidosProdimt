const { defineConfig, devices } = require('@playwright/test');
const { resolve } = require('node:path');

const webAppDirectory = resolve(__dirname, '../../apps/prodimt-pedidos-web');

module.exports = defineConfig({
  testDir: './specs',
  timeout: 30000,
  expect: {
    timeout: 5000
  },
  use: {
    baseURL: 'http://127.0.0.1:4210',
    trace: 'on-first-retry'
  },
  webServer: [
    {
      command: 'node mock-api.js',
      cwd: __dirname,
      url: 'http://127.0.0.1:5088/__test/health',
      reuseExistingServer: false,
      timeout: 30000
    },
    {
      command: 'npm run start -- --host 127.0.0.1 --port 4210',
      cwd: webAppDirectory,
      url: 'http://127.0.0.1:4210',
      reuseExistingServer: !process.env.CI,
      timeout: 120000
    }
  ],
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] }
    }
  ]
});
