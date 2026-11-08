import type { Page } from '@playwright/test';
import { expect, test } from './fixtures';
import { screenshots, signIn } from './helpers';

// Experience, education and skills against a real API. All data is made up.

async function createResume(page: Page, title: string) {
  await page.getByLabel('Özgeçmiş adı').fill(title);
  await page.getByRole('button', { name: 'Oluştur' }).click();
  await expect(page.getByRole('heading', { level: 1, name: title })).toBeVisible();
}

async function pickMonth(page: Page, legend: string, month: string, year: string) {
  const group = page.getByRole('group', { name: legend });
  await group.getByLabel('Ay').selectOption({ label: month });
  await group.getByLabel('Yıl').fill(year);
}

test('experience and education: add, edit, reorder and delete', async ({ page, request }, testInfo) => {
  test.setTimeout(120_000);
  await signIn(page, request, testInfo);
  await createResume(page, 'Deneme Bölümler');

  // Experience: two items.
  await page.getByRole('button', { name: 'Deneyim', exact: true }).click();
  for (const [position, company, month, year] of [
    ['Yazılım geliştirici', 'Uydurma Teknoloji', 'Mart', '2019'],
    ['Kıdemli geliştirici', 'Örnek Yazılım', 'Ocak', '2022'],
  ] as const) {
    await page.getByRole('button', { name: 'Deneyim ekle' }).click();
    await page.getByLabel('Pozisyon').fill(position);
    await page.getByLabel('Şirket').fill(company);
    await page.getByLabel('Çalışma şekli').selectOption({ label: 'Tam zamanlı' });
    await pickMonth(page, 'Başlangıç', month, year);
    if (position.startsWith('Kıdemli')) {
      await page.getByLabel('Halen burada çalışıyorum').check();
      await screenshots(page, testInfo, 'experience-form');
    } else {
      await pickMonth(page, 'Bitiş', 'Aralık', '2021');
    }
    await page.getByRole('button', { name: 'Kaydet' }).click();
    await expect(page.getByRole('status').filter({ hasText: `“${position} – ${company}” kaydedildi.` })).toBeVisible();
  }

  // The end date cannot be before the start, checked before sending.
  await page.getByRole('button', { name: 'Düzenle: Yazılım geliştirici – Uydurma Teknoloji' }).click();
  await pickMonth(page, 'Bitiş', 'Ocak', '2018');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('Bitiş tarihi başlangıç tarihinden önce olamaz.')).toBeVisible();
  await pickMonth(page, 'Bitiş', 'Aralık', '2021');
  await page.getByLabel('Konum').fill('İstanbul');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'kaydedildi.' })).toBeVisible();

  // Reorder: the second moves up; focus stays on it.
  const moveUp = page.getByRole('button', { name: 'Yukarı taşı: Kıdemli geliştirici – Örnek Yazılım' });
  await moveUp.click();
  await expect(page.getByRole('status').filter({ hasText: '1. sıraya taşındı' })).toBeVisible();
  const experiences = page.getByRole('listitem').filter({ hasText: /geliştirici – / });
  await expect(experiences.first()).toContainText('Kıdemli geliştirici');
  await expect(page.getByRole('button', { name: 'Aşağı taşı: Kıdemli geliştirici – Örnek Yazılım' })).toBeFocused();
  await screenshots(page, testInfo, 'experience-list');

  // Delete asks first.
  await page.getByRole('button', { name: 'Sil: Yazılım geliştirici – Uydurma Teknoloji' }).click();
  const confirm = page.getByRole('alertdialog', { name: '“Yazılım geliştirici – Uydurma Teknoloji” silinsin mi?' });
  await expect(confirm).toBeVisible();
  await screenshots(page, testInfo, 'delete-confirm', false);
  await confirm.getByRole('button', { name: 'Sil' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'silindi.' })).toBeVisible();
  await expect(experiences).toHaveCount(1);

  // Education: two items, one ongoing.
  await page.getByRole('button', { name: 'Eğitim', exact: true }).click();
  for (const [school, degree, ongoing] of [
    ['Uydurma Üniversitesi', 'Lisans', false],
    ['Örnek Enstitüsü', 'Yüksek lisans', true],
  ] as const) {
    await page.getByRole('button', { name: 'Eğitim ekle' }).click();
    await page.getByLabel('Okul').fill(school);
    await page.getByLabel('Derece').fill(degree);
    await page.getByLabel('Bölüm', { exact: true }).fill('Bilgisayar mühendisliği');
    await pickMonth(page, 'Başlangıç', 'Eylül', ongoing ? '2023' : '2013');
    if (ongoing) {
      await page.getByLabel('Halen devam ediyor').check();
      await page.getByLabel('Not ortalaması').fill('3,6');
      await screenshots(page, testInfo, 'education-form');
    } else {
      await pickMonth(page, 'Bitiş', 'Haziran', '2017');
    }
    await page.getByRole('button', { name: 'Kaydet' }).click();
    await expect(page.getByRole('status').filter({ hasText: `“${school} – ${degree}” kaydedildi.` })).toBeVisible();
  }
  await page.getByRole('button', { name: 'Düzenle: Uydurma Üniversitesi – Lisans' }).click();
  await page.getByLabel('Bölüm', { exact: true }).fill('Yazılım mühendisliği');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'kaydedildi.' })).toBeVisible();
  await page.getByRole('button', { name: 'Aşağı taşı: Uydurma Üniversitesi – Lisans' }).click();
  await expect(page.getByRole('status').filter({ hasText: '2. sıraya taşındı' })).toBeVisible();
  await screenshots(page, testInfo, 'education-list');
  await page.getByRole('button', { name: 'Sil: Uydurma Üniversitesi – Lisans' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Sil' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'silindi.' })).toBeVisible();

  // Everything survives a reload in the saved order.
  await page.reload();
  await page.getByRole('button', { name: 'Deneyim', exact: true }).click();
  await expect(page.getByText('Kıdemli geliştirici – Örnek Yazılım')).toBeVisible();
  await page.getByRole('button', { name: 'Eğitim', exact: true }).click();
  await expect(page.getByText('Örnek Enstitüsü – Yüksek lisans')).toBeVisible();
  await expect(page.getByText('Uydurma Üniversitesi – Lisans')).toHaveCount(0);
});

test('skills: catalog search, add, level, delete, and the unlisted note', async ({ page, request }, testInfo) => {
  test.setTimeout(90_000);
  await signIn(page, request, testInfo);
  await createResume(page, 'Deneme Beceriler');
  await page.getByRole('button', { name: 'Beceriler', exact: true }).click();
  const search = page.getByLabel('Katalogda beceri ara');

  // A search with results from the real catalog.
  await search.fill('react');
  const add = page.getByRole('button', { name: 'Ekle: React', exact: true });
  await expect(add).toBeVisible();
  await screenshots(page, testInfo, 'skills-search');
  await add.click();
  await expect(page.getByRole('status').filter({ hasText: '“React” eklendi.' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Eklendi: React' })).toBeDisabled();

  await search.fill('docker');
  await page.getByRole('button', { name: 'Ekle: Docker', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: '“Docker” eklendi.' })).toBeVisible();

  // One character finds exact matches only.
  await search.fill('r');
  await expect(page.getByRole('button', { name: /^Ekle: R$|^Eklendi: R$/ })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Ekle: Redux' })).toHaveCount(0);

  // Level, then delete with confirmation.
  await page.getByLabel('Seviye: React').selectOption({ label: 'İleri' });
  await expect(page.getByRole('status').filter({ hasText: '“React” seviyesi kaydedildi.' })).toBeVisible();
  await page.getByRole('button', { name: 'Sil: Docker' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Sil' }).click();
  await expect(page.getByRole('status').filter({ hasText: '“Docker” silindi.' })).toBeVisible();

  // Not in the catalog: offered for the note, saved with the resume ETag.
  await search.fill('Uydurmaaracı');
  await expect(page.getByText('“Uydurmaaracı” katalogda yok.')).toBeVisible();
  await screenshots(page, testInfo, 'skills-not-found');
  await page.getByRole('button', { name: '“Uydurmaaracı” notuna ekle' }).click();
  await expect(page.getByLabel('Katalogda olmayan beceriler')).toHaveValue('Uydurmaaracı');
  const saveNote = page.waitForRequest((sent) => sent.method() === 'PUT' && /\/api\/resumes\/[0-9a-f-]{36}$/.test(sent.url()));
  await page.getByRole('button', { name: 'Kaydet' }).click();
  expect((await saveNote).headers()['if-match']).toMatch(/^"[0-9A-F]{8}"$/);
  await expect(page.getByRole('status').filter({ hasText: 'Not kaydedildi.' })).toBeVisible();

  // The main fields open with the new resume ETag: saving them is not a 412.
  await page.getByRole('button', { name: 'Ana bilgiler' }).click();
  await page.getByLabel('Unvan').fill('Uydurma unvan');
  const mainSave = page.waitForResponse((response) => response.request().method() === 'PUT');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  expect((await mainSave).status()).toBe(200);

  await page.reload();
  await page.getByRole('button', { name: 'Beceriler', exact: true }).click();
  await expect(page.getByRole('list', { name: 'Eklenen beceriler' }).getByText('React')).toBeVisible();
  await expect(page.getByLabel('Seviye: React')).toHaveValue('Advanced');
  await expect(page.getByLabel('Katalogda olmayan beceriler')).toHaveValue('Uydurmaaracı');
});
