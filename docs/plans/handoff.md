# Devir notu (5 Kasım 2026)

Yeni oturum buradan başlar. Ayrıntılı plan [frontend-roadmap.md](frontend-roadmap.md) içinde.

> **Alan adı ve yayın ertelendi, PR 19'da karar verilecek.** PR 19'a kadar her şey yerelde geliştirilip test edilir. Ücretli plan önerileri (Render Starter, Neon Launch) de PR 19'a kalır. Render'daki ücretsiz API deploy hattı test için çalışmaya devam eder: merge politikası ve canlı doğrulama aynen uygulanır.

## Neredeyiz

- **Son canlı commit (bu not yazılırken):** `37629b7` (PR #24, refresh cookie ve CSRF). `/health/ready` Healthy, smoke testi geçiyor.
- **Canlı adresler:** API `https://resumatch-api-da3z.onrender.com` (Render Free), veritabanı Neon (pooler, PgBouncer transaction modu).
- **Aşama A tamamlandı:** PR 1–5, 3b ve 3c merge edildi. Son migration `20261103173237_AddAuthSessions`.
- **PR #25:** Referer şema düzeltmesi ve bu not. Merge edildiğinde son canlı commit onun merge commit'i olur; `/health/version` ile doğrula. Açık PR kalmaz.
- **Sıradaki PR:** PR 6, web iskeleti ve sözleşme CI'ı (Aşama B). `web/`, lint, test, tip üretimi, API istemcisi, `web-checks` ve `api-contract` job'ları, `npm` için Dependabot. Migration yok.
- **Kimlik doğrulama:** Frontend akışı [authentication.md](../api/authentication.md) içinde: access token bellekte, 401'de tek refresh, Web Locks, `BroadcastChannel`, yerelde Vite proxy.

## Bekleyen kararlar

- **Alan adı ve yayın: ertelendi, PR 19'da karar verilecek.** Ad seçilmedi, `alanadi.com` yer tutucudur. Frontend `app.alanadi.com` mı yoksa kök `alanadi.com` mı olacak, o zaman belirlenir; ikisi de cookie tasarımıyla çalışır. Alan adı gelince değişecek ayarlar authentication.md'deki "When the domain is ready" bölümünde, kod değişikliği gerekmez.
- **Ücretli planlar: ertelendi, PR 19'da karar verilecek.** Öneriler (Render Starter, Neon Launch) yol haritasının 8. bölümünde duruyor. O zamana kadar hiçbir şey satın alınmaz.
- **E-posta:** Gerçek gönderim (`mail.alanadi.com`) alan adına bağlı, PR 19'a kalır. PR 11 yerelde sahte göndericiyle geliştirilip test edilir.
- **Canlıda kimlik bilgisi:** `HttpSecurity__AllowCredentials` alan adı gelene kadar kapalı. Bu sürede refresh ve logout canlıda 403 döner, bu beklenen durumdur. Cookie akışı yerelde Vite proxy ile test edilir.
- **Hukuk:** Veri sorumlusu yapısı ve metinler avukat görüşüne bağlı (beta çıkış şartı).
- **WebKit ve yerel HTTP (karar verildi):** Playwright WebKit `http://localhost` üzerinde `Secure` cookie'yi saklıyor ama geri göndermiyor; HTTPS'te gönderiyor. Tam akış E2E testleri bu yüzden kendinden imzalı sertifikayla HTTPS'te çalışır (`vite --mode e2e`); günlük `npm run dev` HTTP'de kalır. PR 9'daki CI `web-e2e` job'unda WebKit normal test olarak çalışır, beklenen hata işareti yoktur. Ayrıntı: [authentication.md](../api/authentication.md#local-development).
- **Token yenileme (karar verildi):** Yalnızca 401 ile; süre dolmadan önceden yenileme yok.
- **Stil (karar verildi):** CSS Modules'a PR 8'de geçilir.

## Merge politikası

Tam metin: [change-and-merge-policy.md](../operations/change-and-merge-policy.md). Özet:

1. Yalnızca sahibinin **numara ve başlıkla** onayladığı PR merge edilir. Migration içeren PR'da onay migration'ı da kapsamalıdır.
2. Merge'den önce `gh pr view <n> --json number,title,headRefName,author,mergeStateStatus` çıktısı onayla karşılaştırılır. Biri tutmazsa merge yapılmaz.
3. Dependabot PR'ları ayrıca onaysız asla merge edilmez.
4. Aynı anda tek PR merge edilir. Sonraki PR'a ancak canlı doğrulama bittikten sonra geçilir: `main` çalışması (migration dahil) başarılı, `/health/version` merge commit'ini gösteriyor, `/health/ready` 200, smoke testi geçiyor.
5. Merge öncesi branch güncel `main` ile birleştirilir ve yeşil CI beklenir.
6. Migration veya deploy düşerse durulur. Tekrar denenmez, `Down` çalıştırılmaz, rapor edilir.


Politika dosyasında yazmayan ama PR 4 ve 5'te istenen bir adım daha var: PR merge onayına sunulmadan önce `/security-review` çalıştırılır, bulgular düzeltilir ya da gerekçesiyle raporlanır.

**Merge betiği:** [`scripts/merge-pr.sh`](../../scripts/merge-pr.sh). Örnek: `bash scripts/merge-pr.sh 26 "<onaylanan başlık>" <branch>`. Önce `--dry-run` ile yalnızca kontroller çalıştırılabilir; merge edilmiş bir PR için canlı doğrulama `--verify-only` ile tekrarlanır. Migration içeren PR'da `--migration-approved` olmadan durur.

- **Merge öncesi:** numara, başlık, branch, `main` hedefi, `OPEN` ve taslak değil, `mergeStateStatus` `CLEAN`, branch `main`'in gerisinde değil, head commit'te tüm check'ler yeşil ve `main`'deki `ci.yml` job'larının (deploy hariç) hepsi başarılı. Okuma çağrıları tekrar denenir.
- **Merge:** tek `gh pr merge --merge --match-head-commit <sha>` çağrısı, asla tekrar denenmez.
- **Merge sonrası:** merge commit'inin `main` çalışmasını bekler, job ve deploy adımlarını yazar, `Apply migrations` ve `Smoke test` adımlarının başarısını, `/health/version` commit'ini ve `/health/ready` 200'ü doğrular. Yerelde `jq` olmadığı için smoke testi CI adımından okunur; `jq` varsa yerelde de çalışır. Bir adım tutmazsa durur.

## Dokunulmaması gerekenler

- **`secrets.json`:** Hiçbir yere yüklenmez, commit edilmez, içeriği bir hizmete gönderilmez.
- **DakaProje:** Aynı hesaplardaki DakaProje servislerine (Render, Neon) dokunulmaz.
- **Canlı kullanıcı:** Canlıda kullanıcı oluşturulmaz, giriş yapılmaz. Smoke testi bu yüzden salt okunurdur.
- **Neon verisi:** Neon'a doğrudan bağlanılmaz ve veri değiştirilmez. Migration'ları yalnızca CI'daki deploy job'u uygular.

## Neon kontrol sorgusu düzeni

Migration içeren her merge'den sonra:

1. Claude bir salt okunur `SELECT` sorgusu ve beklenen sonucu verir. Sorgu yalnızca sayım ve şema kontrolü yapar; kişisel veri döndürmez.
2. Sahibi sorguyu Neon konsolunda çalıştırır ve sonucun beklenenle eşleşip eşleşmediğini söyler.
3. Eşleşmezse sonraki işe geçilmez.

Örnek (PR 4 için, beklenen `1, 2, 3, 0, 0, 0, 400`):

```sql
SELECT
  (SELECT count(*) FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103173237_AddAuthSessions') AS migration_applied,
  (SELECT count(*) FROM information_schema.tables WHERE table_name IN ('AuthSessions','RefreshTokens')) AS session_tables,
  (SELECT count(*) FROM pg_constraint WHERE conname IN ('CK_AuthSessions_Lifetime','CK_AuthSessions_Revocation','CK_RefreshTokens_TokenHashLength')) AS check_constraints,
  (SELECT count(*) FROM "Users" WHERE "SecurityStamp" IS NULL) AS users_without_stamp,
  (SELECT count(*) FROM "AuthSessions") AS sessions,
  (SELECT count(*) FROM "RefreshTokens") AS refresh_tokens,
  (SELECT count(*) FROM "Skills") AS skills;
```

Migration'lar merge'den önce geçici bir PostgreSQL'de `Up → Down → Up` olarak denenir. Betik `dotnet-ef` 10.0.14 ile, güvenli `Staging` ortam değişkenleriyle üretilir.

## Bilinen riskler

- **`minimatch` (GHSA-g3hv-75wg-m249, orta):** `web/` geliştirme bağımlılıklarında ESLint ve typescript-eslint üzerinden geliyor; tam `npm audit` 10 uyarı gösterir. Açık, saldırganın kontrol ettiği glob desenleri gerektirir; bizde desenler yalnızca ESLint yapılandırmasından gelir ve paket üretim build'ine girmez. Düzeltme yalnızca `minimatch` 11'de, ESLint 10 ise `^10.2.4` istiyor. Karar: override yok, CI'da `npm audit --omit=dev` yeterli. **ESLint `minimatch` 11'i desteklediğinde güncelle** ve tam `npm audit`'in temiz olduğunu doğrula.

## Lansman kontrol listesi

Beta davetinden önce elle yapılacaklar:

- [ ] Gerçek iPhone Safari'de giriş → sayfa yenileme → çıkış akışını dene (özel alan adı ve HTTPS ile). Playwright WebKit gerçek Safari değildir.

## Ortam notları

- **Testler:** Testcontainers ile PostgreSQL, Docker Desktop açık olmalı. `dotnet test` tam paketi yaklaşık 2,5 dakika sürer (son sayım 1421 test).
- **Test ortam kilidi:** `ResuMatchWebApplicationFactory` ortam değişkenlerini kilitler. Uygulama örneği tutan bir test sınıfında (`ApiIntegrationTestBase`) ikinci bir örnek açan test sonsuza kadar bekler. Başlangıç hatası testleri `IntegrationTestBase` sınıfından türeyen sınıflara yazılmalıdır.
- **Yerel araçlar:** Python ve `jq` kurulu değil. Kabuk betikleri Git Bash'te çalışır.
- **OneDrive:** `.git/worktrees` altındaki eski klasörleri OneDrive kilitlediği için silinemiyordu. Proje taşındıktan sonra `git worktree prune` ile temizlenebilir.
