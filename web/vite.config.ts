import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// The API's `http` launch profile. The browser talks only to the Vite origin,
// so the refresh cookie is stored for localhost:5173, which the API's
// Development settings allow (docs/api/authentication.md, Local development).
const apiTarget = 'http://localhost:5252';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // The API allows exactly this origin; never fall back to another port.
    strictPort: true,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
      // The cold start check calls /health/live on the API.
      '/health': { target: apiTarget, changeOrigin: true },
    },
  },
  preview: {
    port: 4173,
    strictPort: true,
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['src/test/setup.ts'],
    restoreMocks: true,
  },
});
