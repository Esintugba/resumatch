import { defineConfig, devices } from '@playwright/test';

// Full-stack tests: a real API on localhost:5252 and a throwaway database,
// started by scripts/run-e2e-fullstack.sh. Run that script, not this config.
export default defineConfig({
  testDir: 'e2e-fullstack',
  // One API and one database are shared, so tests run one at a time.
  workers: 1,
  fullyParallel: false,
  reporter: 'list',
  use: {
    baseURL: 'http://localhost:4173',
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'webkit', use: { ...devices['Desktop Safari'] } },
  ],
  webServer: {
    command: 'npm run build && npm run preview',
    url: 'http://localhost:4173',
    reuseExistingServer: false,
    timeout: 120_000,
  },
});
