import { expect, test } from '@playwright/test';

test('shows the placeholder once the API is live', async ({ page }) => {
  await page.route('**/health/live', (route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: '{"status":"Healthy"}' }),
  );

  await page.goto('/');

  await expect(page.getByRole('heading', { level: 1, name: 'ResuMatch' })).toBeVisible();
  await expect(page.getByText('Sunucu hazır.', { exact: false })).toBeVisible();
  await expect(page.getByRole('status')).toHaveCount(0);
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
