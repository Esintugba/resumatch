import basicSsl from '@vitejs/plugin-basic-ssl';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// The API's `http` launch profile. The browser talks only to the Vite origin,
// so the refresh cookie is stored for that host, which the API's settings
// allow (docs/api/authentication.md, Local development).
const apiTarget = 'http://localhost:5252';
const proxy = {
  '/api': { target: apiTarget, changeOrigin: true },
  // The cold start check calls /health/live on the API.
  '/health': { target: apiTarget, changeOrigin: true },
};

export default defineConfig(({ mode }) => ({
  // `--mode e2e` serves over HTTPS with a throwaway self-signed certificate,
  // because WebKit does not send Secure cookies over plain HTTP. Day-to-day
  // `npm run dev` stays on HTTP and needs no certificate.
  plugins: mode === 'e2e' ? [react(), basicSsl()] : [react()],
  server: {
    port: 5173,
    // The API allows exactly this origin; never fall back to another port.
    strictPort: true,
    proxy,
  },
  preview: {
    port: 4173,
    strictPort: true,
    proxy,
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['src/test/setup.ts'],
    restoreMocks: true,
  },
}));
