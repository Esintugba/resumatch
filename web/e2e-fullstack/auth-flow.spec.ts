import { expect, test } from './fixtures';
import { readableStorage, refreshCookie, signIn, welcome } from './helpers';

// Runs through scripts/run-e2e-fullstack.sh: real API, throwaway database,
// web app over HTTPS (self-signed). Every user is made up for the run.


// Over plain HTTP, Playwright's WebKit stores Secure cookies but never sends
// them back, so these tests use HTTPS (see docs/api/authentication.md).
const signOutMarkerKey = 'resumatch.signOutPending';

test('signs in, and keeps the access token out of every script-readable store', async ({
  page,
  context,
  request,
}, testInfo) => {
  const login = await signIn(page, request, testInfo, true);
  const accessToken = ((await login.json()) as { accessToken: string }).accessToken;

  // The API sets the refresh token as a __Host- cookie: Secure, HttpOnly, SameSite=Strict, Path=/.
  const setCookie = (await login.headerValue('set-cookie')) ?? '';
  expect(setCookie).toContain(`${refreshCookie}=`);
  expect(setCookie).toMatch(/;\s*secure/i);
  expect(setCookie).toMatch(/;\s*httponly/i);
  expect(setCookie).toMatch(/;\s*samesite=strict/i);
  expect(setCookie).toMatch(/;\s*path=\//i);
  expect(setCookie).not.toMatch(/;\s*domain=/i);

  // The browser stored it.
  const cookie = (await context.cookies()).find((item) => item.name === refreshCookie);
  testInfo.annotations.push({
    type: 'refresh-cookie-stored',
    description: cookie
      ? JSON.stringify({ secure: cookie.secure, httpOnly: cookie.httpOnly, sameSite: cookie.sameSite, path: cookie.path })
      : 'not stored',
  });
  expect(cookie, `${refreshCookie} was not stored by ${testInfo.project.name}`).toBeDefined();
  expect(cookie).toMatchObject({ secure: true, httpOnly: true, path: '/' });
  // Playwright's WebKit on Windows and Linux reports SameSite as None for every
  // cookie, so the attribute is checked on the header above.
  if (testInfo.project.name === 'chromium') expect(cookie?.sameSite).toBe('Strict');

  // The access token is in no storage a script can read, and neither is the refresh token.
  const storage = await readableStorage(page);
  expect(storage.localStorage).toEqual([]);
  expect(storage.sessionStorage).toEqual([]);
  expect(storage.cookie).not.toContain(refreshCookie);
  expect(JSON.stringify(storage)).not.toContain(accessToken);
});

test('keeps the session across a reload and in a new tab, and signs out in every tab', async ({
  page,
  context,
  request,
}, testInfo) => {
  await signIn(page, request, testInfo);

  // A reload loses the in-memory token; the page refreshes silently with the cookie.
  const refreshOnReload = page.waitForResponse('**/api/auth/refresh');
  await page.reload();
  const refreshed = await refreshOnReload;
  // The API answers 200 only if the browser sent the refresh cookie back.
  expect(refreshed.status(), `${testInfo.project.name} did not send ${refreshCookie} back`).toBe(200);
  // Recorded for information only: Playwright's WebKit does not show cookies
  // its network stack adds in request headers, so this can read false there.
  const reportedCookie = ((await refreshed.request().headerValue('cookie')) ?? '').includes(`${refreshCookie}=`);
  testInfo.annotations.push({ type: 'cookie-header-visible-to-playwright', description: String(reportedCookie) });
  await expect(page.getByRole('heading', { level: 1, name: welcome })).toBeVisible();

  // A second tab gets its own token from the same cookie (one refresh at a time, Web Locks).
  const second = await context.newPage();
  await second.goto('/');
  await expect(second.getByRole('heading', { level: 1, name: welcome })).toBeVisible();

  // Signing out in one tab signs out the other through the BroadcastChannel.
  await page.getByRole('button', { name: 'Çıkış yap' }).click();
  await expect(page).toHaveURL(/\/login$/);
  await expect(second).toHaveURL(/\/login$/);
  await expect(second.getByRole('heading', { level: 1, name: 'Giriş yap' })).toBeVisible();

  // The API cleared the cookie, so a reload stays signed out.
  expect((await context.cookies()).some((item) => item.name === refreshCookie)).toBe(false);
  await page.reload();
  await expect(page).toHaveURL(/\/login$/);
});

test('shows the API error for wrong credentials in Turkish', async ({ page }) => {
  await page.goto('/login');

  await page.getByLabel('E-posta').fill('nobody@example.test');
  await page.getByLabel('Parola').fill('Yanlis-parola-1');
  await page.getByRole('button', { name: 'Giriş yap' }).click();

  await expect(page.getByRole('alert')).toContainText('E-posta adresi veya parola hatalı.');
  await expect(page.getByLabel('Parola')).toHaveValue('');
});

test('signs out locally when offline, and finishes the logout on the next page load', async ({
  page,
  context,
  request,
}, testInfo) => {
  await signIn(page, request, testInfo);

  await context.setOffline(true);
  await page.getByRole('button', { name: 'Çıkış yap' }).click();

  // The interface signs out and says the logout did not reach the server.
  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByRole('status')).toContainText('Çıkış tamamlanamadı, bağlantınızı kontrol edin.');
  // Only the flag is stored; the cookie is still there because the API never saw the logout.
  expect(await page.evaluate(() => Object.entries(localStorage))).toEqual([[signOutMarkerKey, '1']]);
  expect((await context.cookies()).some((item) => item.name === refreshCookie)).toBe(true);

  // Close the tab while offline, so the next load is what finishes the logout.
  await page.close();
  await context.setOffline(false);
  const next = await context.newPage();
  const authCalls: string[] = [];
  next.on('request', (sent) => {
    const path = new URL(sent.url()).pathname;
    if (path.startsWith('/api/auth/')) authCalls.push(path);
  });

  await next.goto('/');

  await expect(next).toHaveURL(/\/login$/);
  await expect.poll(() => authCalls).toContain('/api/auth/logout');
  expect(authCalls).not.toContain('/api/auth/refresh');
  await expect.poll(async () => next.evaluate(() => Object.entries(localStorage))).toEqual([]);
  await expect
    .poll(async () => (await context.cookies()).some((item) => item.name === refreshCookie))
    .toBe(false);
  await expect(next.getByRole('status')).toHaveCount(0);

  // The session is gone on the server too: a reload stays signed out.
  await next.reload();
  await expect(next).toHaveURL(/\/login$/);
});
