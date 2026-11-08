import { expect, test } from './fixtures';
import { screenshots, signIn } from './helpers';

// Runs through scripts/run-e2e-fullstack.sh against a real API. All data is made up.

const first = 'Deneme Yazılım Geliştirici';
const second = 'Deneme Proje Yöneticisi';

test('creates, edits and saves a resume, resolves a two-tab conflict, guards unsaved changes and archives', async ({
  page,
  context,
  request,
}, testInfo) => {
  test.setTimeout(90_000);
  await signIn(page, request, testInfo);
  await expect(page.getByText('Henüz bir özgeçmişiniz yok.', { exact: false })).toBeVisible();

  // Create.
  await page.getByLabel('Özgeçmiş adı').fill(first);
  await page.getByRole('button', { name: 'Oluştur' }).click();
  await expect(page.getByRole('heading', { level: 1, name: first })).toBeVisible();
  await expect(page).toHaveURL(/\/app\/resumes\/[0-9a-f-]{36}$/);
  const editorUrl = page.url();

  // Edit and save the main fields.
  await page.getByRole('button', { name: 'Ana bilgiler' }).click();
  await page.getByLabel('Unvan').fill('Kıdemli yazılım geliştirici');
  await page.getByLabel('Hedef pozisyon').fill('Ekip lideri');
  await page.getByLabel('Özet').fill('Uydurma özet: dağıtık sistemler ve test otomasyonu üzerine on yıl.');
  const resumeApiPath = `/api/resumes/${new URL(editorUrl).pathname.split('/').at(-1) ?? ''}`;
  const firstSave = page.waitForRequest(
    (sent) => sent.method() === 'PUT' && new URL(sent.url()).pathname === resumeApiPath,
  );
  await page.getByRole('button', { name: 'Kaydet' }).click();
  expect((await firstSave).headers()['if-match']).toMatch(/^"[0-9A-F]{8}"$/);
  await expect(page.getByRole('status').filter({ hasText: 'Ana bilgiler kaydedildi.' })).toBeVisible();

  // Contact starts from the signed-in user's own name.
  await page.getByRole('button', { name: 'İletişim' }).click();
  await expect(page.getByLabel('Ad', { exact: true })).toHaveValue('Deneme');
  await page.getByLabel('Telefon').fill('+90 555 000 00 00');
  await page.getByLabel('Şehir').fill('İstanbul');
  await page.getByLabel('Ülke').fill('Türkiye');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'İletişim kaydedildi.' })).toBeVisible();
  await page.getByRole('button', { name: 'Kapat' }).click();
  await screenshots(page, testInfo, 'editor');

  // Two tabs: the second one opens the main fields before the first saves again.
  const other = await context.newPage();
  await other.goto(editorUrl);
  await other.getByRole('button', { name: 'Ana bilgiler' }).click();
  await expect(other.getByLabel('Unvan')).toHaveValue('Kıdemli yazılım geliştirici');

  await page.getByRole('button', { name: 'Ana bilgiler' }).click();
  await page.getByLabel('Unvan').fill('Baş yazılım geliştirici');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Ana bilgiler kaydedildi.' })).toBeVisible();

  // The second tab's save is refused with 412; its text stays and nothing is overwritten.
  await other.getByLabel('Unvan').fill('Bu sekmede yazılan unvan');
  const conflicting = other.waitForResponse((response) => response.request().method() === 'PUT');
  await other.getByRole('button', { name: 'Kaydet' }).click();
  expect((await conflicting).status()).toBe(412);
  const conflict = other.getByRole('alert').filter({ hasText: 'Bu kayıt başka yerde değişti' });
  await expect(conflict).toBeVisible();
  await expect(other.getByLabel('Unvan')).toHaveValue('Bu sekmede yazılan unvan');
  await expect(other.getByRole('button', { name: 'Kaydet' })).toBeDisabled();
  await screenshots(other, testInfo, 'conflict');

  // Loading the saved version asks first, then replaces the text.
  await conflict.getByRole('button', { name: 'Yeni sürümü yükle' }).click();
  const confirm = other.getByRole('alertdialog', { name: 'Yeni sürüm yüklensin mi?' });
  await expect(confirm).toBeVisible();
  await confirm.getByRole('button', { name: 'Yazdıklarımı sil ve yükle' }).click();
  await expect(other.getByLabel('Unvan')).toHaveValue('Baş yazılım geliştirici');
  await expect(other.getByRole('status').filter({ hasText: 'Kayıtlı sürüm yüklendi.' })).toBeVisible();

  // Unsaved changes: another section, the browser tab, and leaving the page all ask first.
  await other.getByLabel('Özet').fill('Kaydedilmemiş bir değişiklik.');
  await other.getByRole('button', { name: 'İletişim' }).click();
  const unsaved = other.getByRole('alertdialog', { name: 'Kaydedilmemiş değişiklikler var' });
  await expect(unsaved).toBeVisible();
  await screenshots(other, testInfo, 'unsaved', false);
  await unsaved.getByRole('button', { name: 'Bu bölümde kal' }).click();
  await expect(other.getByLabel('Özet')).toHaveValue('Kaydedilmemiş bir değişiklik.');

  let beforeUnloadAsked = false;
  other.once('dialog', (dialog) => {
    beforeUnloadAsked = dialog.type() === 'beforeunload';
    void dialog.dismiss();
  });
  await other.close({ runBeforeUnload: true });
  await expect.poll(() => beforeUnloadAsked).toBe(true);
  expect(other.isClosed()).toBe(false);

  await other.getByRole('link', { name: 'Özgeçmişlerime dön' }).click();
  const leaving = other.getByRole('alertdialog', { name: 'Kaydedilmemiş değişiklikler var' });
  await leaving.getByRole('button', { name: 'Değişiklikleri at' }).click();
  await expect(other.getByRole('heading', { level: 1, name: 'Özgeçmişlerim' })).toBeVisible();
  await other.close();

  // A second resume, then the list with both. Wait for the list: the editor's title field has the same label.
  await page.getByRole('link', { name: 'Özgeçmişlerime dön' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Özgeçmişlerim' })).toBeVisible();
  await page.getByLabel('Özgeçmiş adı').fill(second);
  await page.getByRole('button', { name: 'Oluştur' }).click();
  await expect(page.getByRole('heading', { level: 1, name: second })).toBeVisible();
  await page.getByRole('link', { name: 'Özgeçmişlerime dön' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Özgeçmişlerim' })).toBeVisible();
  const list = page.getByRole('list', { name: 'Özgeçmiş listesi' });
  await expect(list.getByRole('listitem')).toHaveCount(2);

  // Archive asks first.
  await list.getByRole('button', { name: `Arşivle: ${second}` }).click();
  const archiveDialog = page.getByRole('alertdialog', { name: `“${second}” arşivlensin mi?` });
  await archiveDialog.getByRole('button', { name: 'Arşivle' }).click();
  await expect(page.getByRole('status').filter({ hasText: `“${second}” arşivlendi.` })).toBeVisible();
  const archived = list.getByRole('listitem').filter({ hasText: second });
  await expect(archived.getByText('Arşivlendi')).toBeVisible();
  await expect(archived.getByRole('button', { name: /Arşivle/ })).toHaveCount(0);
  await screenshots(page, testInfo, 'list');
});
