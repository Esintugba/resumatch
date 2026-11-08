import { test as base } from '@playwright/test';

/** The only hosts these tests may reach; certificate errors are ignored, so nothing else is allowed. */
export function isLocalhost(url: URL): boolean {
  return url.hostname === 'localhost' || url.hostname === '127.0.0.1' || url.hostname === '[::1]';
}

export const test = base.extend({
  context: async ({ context }, use) => {
    await context.route((url) => !isLocalhost(url), (route) => route.abort('blockedbyclient'));
    await use(context);
  },
});

export { expect } from '@playwright/test';
