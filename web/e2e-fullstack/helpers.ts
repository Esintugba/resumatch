import type { APIRequestContext, Page, TestInfo } from '@playwright/test';
import { expect } from './fixtures';

export const api = 'http://localhost:5252';
export const refreshCookie = '__Host-resumatch_refresh';
export const password = 'E2e-Parola-2026!';
/** The page a signed-in user lands on. */
export const welcome = 'Özgeçmişlerim';

const viewports = {
  desktop: { width: 1280, height: 800 },
  mobile: { width: 360, height: 740 },
} as const;

/** Full-page shots, or the viewport only for modal dialogs, whose backdrop covers just the viewport. */
export async function screenshots(page: Page, testInfo: TestInfo, name: string, fullPage = true) {
  for (const [size, viewport] of Object.entries(viewports)) {
    await page.setViewportSize(viewport);
    const file = testInfo.outputPath(`${name}-${size}-${testInfo.project.name}.png`);
    await page.screenshot({ path: file, fullPage });
    await testInfo.attach(`${name}-${size}`, { path: file, contentType: 'image/png' });
  }
  await page.setViewportSize(viewports.desktop);
}

/** Everything a script on the page could read, to prove the access token is in none of it. */
export async function readableStorage(page: Page) {
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
export async function signIn(page: Page, request: APIRequestContext, testInfo: TestInfo, shoot = false) {
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
  if (shoot) await screenshots(page, testInfo, 'list-empty');
  return login;
}

