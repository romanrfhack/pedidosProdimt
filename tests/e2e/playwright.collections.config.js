const { defineConfig } = require('@playwright/test');
const base = require('./playwright.config.js');
module.exports = defineConfig({
  ...base,
  testDir: './collections',
  outputDir: './test-results/collections',
  workers: 1
});
