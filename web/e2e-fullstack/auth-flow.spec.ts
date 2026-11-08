import { expect, test, type APIRequestContext, type Page, type TestInfo } from '@playwright/test';

// Runs through scripts/run-e2e-fullstack.sh: real API, throwaway database.
// Every user is made up for the run; no real data is shown or stored.

const api = 'http://localhost:5252';
const refreshCookie = '__Host-resumatch_refresh';
const password = 'E2e-Parola-2026!';
const welcome = 'Hoş geldiniz, Deneme';

// Playwright's WebKit stores Secure cookies set over http://localhost but never
// sends them back (checked with plain, SameSite, Secure and __Host- cookies:
// only the ones without Secure are sent). The refresh cookie is __Host-, so it
// must be Secure, and every step that needs it fails in WebKit over HTTP.
const webkitSecureCookieGap =
  'WebKit (Playwright build) does not send Secure cookies over http://localhost, so the refresh cookie never returns.';

const viewports = {
  desktop: { width: 1280, height: 800 },
  mobile: { width: 360, height: 740 },
} as const;

async function screenshots(page: Page, testInfo: TestInfo, name: string) {
  for (const [size, viewport] of Object.entries(viewports)) {
    await page.setViewportSize(viewport);
    const file = testInfo.outputPath(`${name}-${size}-${testInfo.project.name}.png`);
    await page.screenshot({ path: file, fullPage: true });
    await testInfo.attach(`${name}-${size}`, { path: file, contentType: 'image/png' });
  }
  await page.setViewportSize(viewports.desktop);
}

/** Everything a script on the page could read, to prove the access token is in none of it. */
async function readableStorage(page: Page) {
  return page.evaluate(async () => {
    const dump = (storage: Storage) =>
      Array.from({ length: storage.length }, (_, index) => {
        const key = storage.key(index) ?? '';
        return `${key}=${storage.getItem(key) ?? ''}`;
      });
    let databases: string[];
    try {
      databases = (await indexedDB.databases()).map((database) => database.name ?? '');
    } catch {
      databases = ['(indexedDB.databases() not supported)'];
    }
    return {
      localStorage: dump(localStorage),
      sessionStorage: dump(sessionStorage),
      cookie: document.cookie,
      databases,
    };
  });
}

/** Registers a made-up user through the API (the registration screen comes in PR 10) and signs in through the form. */
async function signIn(page: Page, request: APIRequestContext, testInfo: TestInfo, shoot = false) {
  const email = `e2e-${testInfo.project.name}-${String(Date.now())}@example.test`;
  const registered = await request.post(`${api}/api/auth/register`, {
    data: { firstName: 'Deneme', lastName: 'Kullanıcı', email, password },
  });
  expect(registered.status()).toBe(201);

  await page.goto('/');
  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Giriş yap' })).toBeVisible();
  if (shoot) await screenshots(page, testInfo, 'login');

  await page.getByLabel('E-posta').fill(email);
  await page.getByLabel('Parola').fill(password);
  const loginResponse = page.waitForResponse('**/api/auth/login');
  await page.getByRole('button', { name: 'Giriş yap' }).click();
  const login = await loginResponse;
  expect(login.status()).toBe(200);
  await expect(page.getByRole('heading', { level: 1, name: welcome })).toBeVisible();
  if (shoot) await screenshots(page, testInfo, 'home');
  return login;
}

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

  // The browser stored it over http://localhost even though it is Secure.
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
  test.fail(testInfo.project.name === 'webkit', webkitSecureCookieGap);
  await signIn(page, request, testInfo);

  // A reload loses the in-memory token; the page refreshes silently with the cookie.
  const refreshOnReload = page.waitForResponse('**/api/auth/refresh');
  await page.reload();
  const refreshed = await refreshOnReload;
  // Only whether the browser sent the cookie, never its value.
  const sentCookie = ((await refreshed.request().headerValue('cookie')) ?? '').includes(`${refreshCookie}=`);
  testInfo.annotations.push({ type: 'refresh-cookie-sent', description: String(sentCookie) });
  expect(sentCookie, `${testInfo.project.name} did not send ${refreshCookie} over http://localhost`).toBe(true);
  expect(refreshed.status()).toBe(200);
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
