import { expect, test } from '@playwright/test';

// API calls are stubbed here; e2e-fullstack/ runs against a real API.

test('shows the sign-in page when there is no session', async ({ page }) => {
  await page.route('**/health/live', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: '{"status":"Healthy"}' }),
  );
  await page.route('**/api/auth/refresh', (route) =>
    route.fulfill({
      status: 401,
      contentType: 'application/problem+json',
      body: '{"status":401,"code":"unauthorized"}',
    }),
  );

  await page.goto('/');

  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Giriş yap' })).toBeVisible();
  await expect(page.getByLabel('E-posta')).toBeVisible();
  await expect(page.getByLabel('Parola')).toBeVisible();
});

test('tells the user the server is waking while the API does not answer', async ({ page }) => {
  // Leave the request pending, as a cold start does.
  await page.route('**/health/live', () => undefined);

  await page.goto('/');

  await expect(page.getByRole('status')).toHaveText('Sunucu uyanıyor, en fazla bir dakika sürebilir.', {
    timeout: 6_000,
  });
  await expect(page.getByText(/Geçen süre: \d+ sn/)).toBeVisible();
});
