const { defineConfig } = require('@playwright/test');
const base = require('./playwright.config.js');

module.exports = defineConfig({
  ...base,
  testDir: './machine-workspace',
  outputDir: './test-results/machine',
  workers: 1
});
