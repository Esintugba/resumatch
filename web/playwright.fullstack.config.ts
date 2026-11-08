import { defineConfig, devices } from '@playwright/test';

// Full-stack tests: a real API on localhost:5252 and a throwaway database,
// started by scripts/run-e2e-fullstack.sh. Run that script, not this config.
//
// The web app is served over HTTPS (`vite preview --mode e2e`, self-signed
// certificate), because WebKit does not send Secure cookies over plain HTTP.
// Certificate errors are ignored for this profile only, and e2e-fullstack/
// fixtures.ts aborts every request that is not to localhost, so the setting
// cannot cover any other host.
export default defineConfig({
  testDir: 'e2e-fullstack',
  // One API and one database are shared, so tests run one at a time.
  workers: 1,
  fullyParallel: false,
  reporter: 'list',
  use: {
    baseURL: 'https://localhost:4173',
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'webkit', use: { ...devices['Desktop Safari'] } },
  ],
  webServer: {
    command: 'npm run build && npx vite preview --mode e2e',
    url: 'https://localhost:4173',
    ignoreHTTPSErrors: true,
    reuseExistingServer: false,
    timeout: 120_000,
  },
});
