# ResuMatch frontend ve gerçek kullanıcı yol haritası

Durum: Kararlar verildi, uygulama PR 1 ile başlıyor. İnceleme tarihi: 2026-11-01, `main` commit'i `f459c56`. `alanadi.com`, seçilecek alan adı için yer tutucudur.

Bu belge bir plandır. Kod, migration veya canlı veri değişikliği içermez. Belgedeki hukuk notları hukuki görüş değildir. Fiyatlar kaynak listesindeki tarihte okunmuştur ve değişebilir.

## Özet

- API'nin çekirdeği sağlam, ama bugün gerçek kullanıcıya açılamaz.
- **En büyük engel:** Kodda beceri kataloğunu dolduran hiçbir yol yok. Kullanıcı özgeçmişe beceri ekleyemez ve iş eşleştirmesi anlamlı sonuç üretmez.
- **Hesap yaşam döngüsü eksik:** Hesap silme, veri dışa aktarma, şifre değiştirme ve sıfırlama, e-posta doğrulama, rıza kaydı yok.
- **Özgeçmiş verisi OpenAI'ye gitmiyor.** Yalnızca iş ilanının başlığı ve açıklaması gidiyor. OpenAI şu an kapalı.
- **Sıra:** Önce zorunlu backend temeli, sonra giriş → özgeçmiş → ATS analizi akışı, ardından kalan backend işleri. Beta kapısı değişmez.
- **Maliyet:** Geliştirmede yalnızca alan adı (yılda yaklaşık 11 dolar), beta öncesi aylık yaklaşık 12 dolar (bölüm 8).
- **Kapalı beta planı:** 21 PR, migration içeren 7 PR için ayrı onay gerekir (bölüm 7).
- **Kararlar:** Tümü verildi (bölüm 8). Veri sorumlusu yapısı avukatla netleşene kadar davet gönderilmez.

## 1. API envanteri

Tüm yollar `/api` altındadır. `register` ve `login` dışındaki `/api` uçları JWT ister. Liste uçları `page` ve `pageSize` alır.

### Ekran ve akış eşlemesi

| Ekran veya akış | Uç noktalar | Durum |
|---|---|---|
| Kayıt | `POST /auth/register` | Var. Yanıtta token döner, doğrulama yok. |
| Giriş | `POST /auth/login` | Var. |
| Oturum geri yükleme | `GET /auth/me` | Var. |
| Özgeçmiş listesi | `GET`, `POST /resumes` | Var. |
| Özgeçmiş düzenleyici | `GET /resumes/{id}/editor`, `PUT /resumes/{id}` | Var. Tek çağrıda tüm bölümleri getirir. |
| Arşivleme, silme | `PATCH /resumes/{id}/archive`, `DELETE /resumes/{id}` | Var. Silme 409 dönebilir. |
| İletişim | `GET`, `PUT`, `DELETE /resumes/{id}/contact` | Var. |
| Deneyim, proje | `/resumes/{id}/experiences`, `/projects` için CRUD | Var. `PUT` işlemi `If-Match` ister. |
| Eğitim, sertifika, dil, bağlantı | `/educations`, `/certifications`, `/languages`, `/links` için CRUD | Var. `If-Match` yok, son yazan kazanır. |
| Özgeçmiş becerileri | `/resumes/{id}/skills`, `GET /skills` | Var, ama katalog boş (aşağıda). |
| ATS analizi | `POST /analyses`, `GET /analyses/{id}`, `GET /resumes/{id}/analyses` | Var. Eşzamanlı çalışır, 201 döner. |
| İş ilanı | `/job-postings` için CRUD, `POST /{id}/skills/{skillId}/adopt` | Var. |
| İş eşleştirme | `POST /analyses` ve gövdede `jobPostingId` | Var. |
| Parse draft isteği | `POST /job-postings/{id}/parse` | Var. Asenkron, 200 veya 202 döner. |
| Parse draft izleme | `GET /job-postings/{id}/parse-drafts`, `/parse-drafts/{draftId}` | Var. Yoklama gerekir. |
| Parse draft onayı | `POST .../parse-drafts/{draftId}/confirm`, `/cancel` | Var. `If-Match` ister. |
| Başvuru takibi | `GET`, `POST /job-applications`, `GET`, `PUT /{id}` | Var. Durum ve ilana göre filtrelenir. |
| Sağlık, sürüm | `GET /health/live`, `/health/ready`, `/health/version` | Var, anonim. |

### Eksik veya frontend için kullanışsız olanlar

| Öncelik | Bulgu | Kanıt | Öneri |
|---|---|---|---|
| Engel | Beceri kataloğu boş kalıyor. `Skill` ekleyen servis, seed veya migration verisi yok. `AddResumeSkillRequest` yalnızca `SkillId` kabul ediyor. | [AddResumeSkillRequest.cs](../../API/DTOs/Skill/AddResumeSkillRequest.cs), [SkillsController.cs](../../API/Controllers/SkillsController.cs) | PR 3: Küratörlü liste ve "katalogda yok" notu. Canlıdaki tablo içeriğine bakmadım. |
| Yüksek | Enum'lar JSON'da sayı. `JsonStringEnumConverter` yok. Tipler sayı olarak üretilir, enum sırası değişirse istemci sessizce bozulur. | [Program.cs:650](../../API/Program.cs#L650) | Kullanıcı gelmeden string enum'a geç. |
| Yüksek | `Retry-After` tarayıcı kodundan okunamaz, CORS `WithExposedHeaders` tanımlamıyor. | [Program.cs:175](../../API/Program.cs#L175) | `Retry-After`, `ETag`, `Location` başlıklarını aç. |
| Yüksek | Hatalarda makine kodu yok, `detail` İngilizce. Doğrulama hataları hep "The supplied value is invalid." diyor. | [GlobalExceptionHandler.cs](../../API/Infrastructure/ExceptionHandling/GlobalExceptionHandler.cs), [Program.cs:656](../../API/Program.cs#L656) | Kararlı `code` uzantısı ekle. Alan kuralları istemcide de doğrulanır. |
| Yüksek | OpenAI kapalıyken parse isteği reddedilmiyor. Worker kapalı olduğundan draft `Pending` kalır. | [JobPostingParseDraftOptions.cs](../../API/Application/JobParsing/JobPostingParseDraftOptions.cs) | PR 2: 503 ve `feature_disabled`, draft oluşmaz. Ekran `GET /capabilities` ile gizlenir. |
| Orta | Beklenmeyen 401 yanıtının gövdesi boş, ProblemDetails değil. | `AddJwtBearer` içinde `OnChallenge` yok | İstemci boş gövdeyi desteklesin. |
| Orta | Draft yanıtlarında `eTag` yok, yalnızca `draftVersion` (`uint`) var. | [EntityTagCodec.cs](../../API/Application/Common/Concurrency/EntityTagCodec.cs) | DTO'ya `eTag` ekle, biçim istemciye sızmasın. |
| Orta | Profil güncelleme, başvuru silme, öneri durumu güncelleme ucu yok. | Controller listesi | Kapsama göre ekle. |
| Orta | Özgeçmiş kopyalama ucu yok. `SourceResumeId` yalnızca bağlantı kuruyor; kopyalama kodu görmedim. | [ResumeService.cs](../../API/Application/Resumes/ResumeService.cs) | Sürümleme için `POST /resumes/{id}/duplicate`. |
| Orta | PDF dışa aktarma ve özgeçmiş yükleme yok. `ResumeExport` tablosu var, ucu yok. | [ResumeExport.cs](../../API/Entities/ResumeExport.cs) | Beta için kapsam dışı. Lansman kararı. |
| Orta | `POST /analyses` hız sınırı yok. Yalnızca login, register ve parse sınırlı. Analiz CPU harcıyor. | 3 adet `EnableRateLimiting` | Kullanıcı başına sınır ekle. |
| Düşük | `/api` yanıtlarında `Cache-Control: no-store` yok. | Yalnızca [Program.cs:751](../../API/Program.cs#L751) gönderiyor | Kişisel veri yanıtlarına ekle. |
| Düşük | Bölüm listeleri bölüme göre 20 ile 100 kayıtla sınırlı, sıralama elle `sortOrder`. | [ResumeSectionLimits.cs](../../API/Application/Common/ResumeSectionLimits.cs) | Formlarda sınır uyarısı. |

## 2. Gerçek kullanıcı eksikleri

Önem: **P0** kapalı beta çıkış şartı, **P1** açık lansmandan önce, **P2** lansman sonrası. Kapsam: S 1 PR, M 2 PR, L 3 veya daha fazla PR.

| Konu | Mevcut durum | Önem | Kapsam | Plan |
|---|---|---|---|---|
| Hesap silme | Yok. `User` üzerinden `Resume`, `JobPosting`, `JobApplication` ilişkileri `Restrict`. `AiUsageReservation` ve `AiUsageBudgetPeriod` içinde FK'siz `UserId` var. | P0 | M | Tek işlemde sil, AI defterlerini anonimleştir, parola ile onayla. |
| Veri dışa aktarma | Yok. | P0 | S | Tek JSON dosyası: `GET /account/export`. |
| Şifre değiştirme | Yok. | P0 | S | Mevcut parolayı iste, oturumları düşür. |
| Şifre sıfırlama | Yok. E-posta altyapısı da yok. | P0 | M | Tek kullanımlık, kısa ömürlü, özetlenmiş token. Hesabın varlığını açıklamayan yanıt. |
| Oturum iptali | Yok. Yalnızca `IsActive` kontrolü var. Parola değişse bile token en çok 60 dk geçerli kalır. | P0 | M | Refresh oturumları ve `SecurityStamp` (bölüm 4). |
| E-posta doğrulama | `IsEmailVerified` alanı var, hep `false` kalıyor. | P0 | M | Doğrulama bağlantısı, doğrulanmamış hesaba kısıt. |
| Rıza ve gizlilik kaydı | Yok. | P0 | S | Kullanım şartı ve aydınlatma metni için sürüm, zaman ve kayıt. Pazarlama izni ayrı. |
| Log'larda kişisel veri | Log şablonlarında e-posta, ad, telefon veya IP bulamadım. `Path` içinde GUID'ler var. Hassas EF loglama açık değil. | P2 | S | `Microsoft.EntityFrameworkCore` log seviyesini `Warning` yap. |
| OpenTelemetry | Varsayılanda kapalı (`Observability:Enabled=false`). ASP.NET ve HttpClient izleme etiketleri bu incelemede doğrulanmadı. | P1 | S | OTLP açılmadan önce etiketleri test et, IP ve sorgu dizesini dışla. |
| Saklama süresi | Politika yok. Draft 30 gün, AI defteri 90 gün ayarlı ama ikisinin purge bayrağı varsayılanda kapalı. Kullanıcı verisi süresiz. | P0 | M | Süreleri yaz, gizlilik metnine ekle, hareketsiz hesap politikası belirle. |
| Yedekleme ve geri yükleme | Kodda yok. Neon Free yalnızca 6 saatlik geçmiş, 1 manuel snapshot veriyor. Geri yükleme tatbikatı yapılmadı. | P0 | M | Neon Launch veya şifreli `pg_dump`. Runbook ve tatbikat. |
| Hesap başına giriş koruması | Yalnızca IP sınırı var (dakikada 5). Kilit yok. | P1 | S | Hesap başına kademeli gecikme. |
| Parola politikası | En az 8 karakter. | P1 | S | Sızmış parola kontrolü kararı. |
| Kayıt kısıtı | Herkes kayıt olabilir. | P0, yalnızca beta | S | Davet kodu veya izinli e-posta listesi. |
| Hukuki metinler | Yok. | P0 | Hukuk desteği | Aydınlatma metni, kullanım şartları, çerez bildirimi, yurt dışı aktarım değerlendirmesi. |

**Saklama önerisi (karar gerektirir):**

| Veri | Süre |
|---|---|
| Hesap ve özgeçmiş | Hesap silinene kadar. 24 ay hareketsizlikte uyarı, sonra silme. |
| Parse draft kaynak metni | 30 gün (`PurgeEnabled=true`). |
| AI kullanım defteri | 90 gün, sonrasında purge. |
| Silinen veri yedeklerde | Geri yükleme penceresi kadar. |
| Rıza kaydı | Hukuk görüşüne göre. |

## 3. OpenAI veri akışı

**Sonuç: Özgeçmiş verisi OpenAI'ye gitmiyor. Yalnızca iş ilanının başlığı ve açıklaması gidiyor. OpenAI canlıda kapalı.**

### Kod kanıtı

| Adım | Kanıt |
|---|---|
| Sağlayıcıya verilen tek girdi başlık, açıklama ve parser sürümü | [JobDescriptionParserV1.cs:60](../../API/Application/JobParsing/Engine/JobDescriptionParserV1.cs#L60), [ProviderExtractionRequest.cs](../../API/Application/JobParsing/Models/ProviderExtractionRequest.cs) |
| İstek gövdesindeki kullanıcı verisi yalnızca `SourceData(Title, Description)` | [OpenAiJobDescriptionExtractionProvider.cs:304](../../API/Infrastructure/JobParsing/OpenAI/OpenAiJobDescriptionExtractionProvider.cs#L304), [:687](../../API/Infrastructure/JobParsing/OpenAI/OpenAiJobDescriptionExtractionProvider.cs#L687) |
| `Store=false`, araç listesi boş | [:307](../../API/Infrastructure/JobParsing/OpenAI/OpenAiJobDescriptionExtractionProvider.cs#L307), [:694](../../API/Infrastructure/JobParsing/OpenAI/OpenAiJobDescriptionExtractionProvider.cs#L694) |
| Sabit sistem talimatı dışında metin yok | [OpenAiJobDescriptionExtractionPrompt.cs](../../API/Infrastructure/JobParsing/OpenAI/OpenAiJobDescriptionExtractionPrompt.cs) |
| Analiz motoru yerel | `IJobDescriptionExtractionProvider` ve `HttpClient` yalnızca iş ilanı parser'ı ve sağlayıcıda kullanılıyor |
| Canlıda kapalı | Varsayılan `Unavailable`. Render'da anahtar ve worker ayarı olmadığı 2026-10-31'de elle doğrulandı. |

Kullanıcı kimliği, e-posta, ad ve özgeçmiş alanı istek gövdesinde yok.

### Kalan riskler

- **İlan metni serbest metindir.** İçinde işe alımcı adı veya e-postası olabilir. Kullanıcı ilan alanına yanlışlıkla özgeçmiş de yapıştırabilir. Kodda maskeleme yok. Üst sınır 50.250 karakter.
- **OpenAI tarafı:** API verisi varsayılan olarak eğitimde kullanılmaz, kötüye kullanım log'ları 30 güne kadar tutulur. `store=false` uygulama durumunu kapatır, kötüye kullanım log'unu kapatmaz.
- **AB veri konumu** ek onay ve sözleşme gerektirir.
- **Veritabanı:** Draft, `SourceDescription` kopyasını tutar. Purge açılmazsa silinmez.

### OpenAI açılmadan önce

Bu adımlar betada uygulanmaz, OpenAI betada kapalıdır.

1. Parse düğmesinin yanında "Bu metin OpenAI'ye gönderilir" uyarısı, ilk kullanımda onay.
2. Hizmet alan olarak aydınlatma metnine ekleme, yurt dışı aktarım değerlendirmesi.
3. `PurgeEnabled`, bütçe, sabit model sürümü ve OpenAI harcama limiti.

## 4. Kimlik doğrulama tasarımı

**Karar: Seçenek A.** Access token bellekte, refresh token HttpOnly cookie'de. Yapı `alanadi.com` ve `api.alanadi.com` üzerine kuruludur. `alanadi.com` seçilecek alan adı için yer tutucudur.

### Mevcut durum

- HS256 erişim token'ı, 60 dakika, [appsettings.json:12](../../API/appsettings.json#L12) (üst sınır 24 saat).
- Refresh token ve çıkış ucu yok.
- `AllowCredentials` ayarı var, cookie okuma kodu yok.

### Neden özel alan adı gerekli

- `onrender.com` [Public Suffix List](https://publicsuffix.org/list/public_suffix_list.dat) içinde, bu yüzden iki ayrı `onrender.com` adresi farklı sitedir ve cookie üçüncü taraf sayılır.
- `alanadi.com` ile `api.alanadi.com` aynı sitedir, cookie birinci taraf kalır.

### Tasarım

| Konu | Karar |
|---|---|
| Access token | JWT, 15 dakika, yalnızca bellekte. Disk veya `localStorage` kullanılmaz. |
| Refresh token | Rastgele 256 bit. Veritabanında yalnızca SHA-256 özeti tutulur. Mutlak ömür 30 gün, boşta kalma sınırı 14 gün. |
| Cookie | `__Host-resumatch_refresh`, `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`, `Domain` yok. Yalnızca `api.alanadi.com` görür. |
| Rotasyon | Her `POST /auth/refresh` eski token'ı tüketir, aynı oturum ailesinde yenisini verir. Satır kilidiyle tek kazanan sağlanır. |
| Tekrar kullanım tespiti | Tüketilmiş token tekrar gelirse aile iptal edilir, kullanıcı yeniden giriş yapar. Güvenlik olayı log'lanır, IP yazılmaz. |
| Yarış durumu | İki sekme aynı anda yenilerse yanlış alarm olmasın diye kısa kabul penceresi (10 sn) ve sekmeler arası Web Locks kullanılır. |
| Oturum iptali | `POST /auth/logout`, `POST /auth/logout-all`, `GET /auth/sessions`, `DELETE /auth/sessions/{id}`. |
| Anında iptal | JWT'de `SecurityStamp` claim'i. Parola değişimi, sıfırlama, tüm oturumlardan çıkış ve hesap silme bunu değiştirir. Token [`OnTokenValidated`](../../API/Program.cs#L495) içinde karşılaştırılır. |
| CSRF | Üç katman: `SameSite=Strict`, cookie okuyan uçlarda `Origin` izin listesi ve preflight zorlayan `X-ResuMatch-CSRF: 1` başlığı. Cookie yalnızca refresh ve logout uçlarında okunur. |
| CORS | `AllowCredentials=true`, tek origin `https://alanadi.com`. İzinli başlıklara CSRF başlığı eklenir. |
| Sayfa açılışı | Uygulama `POST /auth/refresh` çağırır. 401 dönerse oturum yoktur. |
| Çoklu sekme | `BroadcastChannel` ile çıkış ve oturum değişimi yayılır. |
| Yerel geliştirme | Vite proxy `/api` isteğini yerel API'ye yönlendirir, istek aynı origin olur. Cookie adı ayarlanabilir, `Secure` zorunluluğu yalnızca Production dışında gevşer. |

**Tehdit sınırı:** A, XSS'i ortadan kaldırmaz. Saldırgan sayfa açıkken istek yaptırabilir, ama refresh token'ı çalıp oturumu dışarı taşıyamaz. CSP ve bağımlılık kuralları bu yüzden yine zorunludur.

### Değerlendirilen seçenekler

| | A. Bellek ve HttpOnly refresh (seçildi) | B. HttpOnly oturum cookie'si | C. localStorage | D. Yalnızca bellek |
|---|---|---|---|---|
| XSS ile token çalma | Zor | Zor | **Kolay** | Zor |
| CSRF | Refresh ve logout korunur | Tüm yazma uçları korunur | Etkisiz | Etkisiz |
| Sayfa yenileyince | Sessiz yenileme | Oturum sürer | Oturum sürer | **Tekrar giriş** |
| Oturum iptali | Anında | Anında | Stamp ile | Stamp ile |
| Özel alan adı | Gerekli | Gerekli | Gerekmez | Gerekmez |

## 5. Frontend mimarisi

### Klasör yapısı

```text
web/
  index.html
  package.json
  openapi/v1.json              üretilen sözleşme, commit edilir
  src/
    app/                       router, sağlayıcılar, hata sınırı
    api/                       schema.d.ts, client, hata dönüşümü, cold start
    features/
      auth/ account/ resumes/ analyses/ job-postings/ parse-drafts/ applications/
    components/                düğme, alan, diyalog, bildirim
    i18n/tr/ i18n/en/
    styles/                    token'lar, temel stil
  e2e/                         Playwright
```

Özellikler birbirini doğrudan içe aktarmaz, ortak kod `components` veya `api` katmanına iner.

### Bağımlılıklar

| Paket | Neden gerekli | Eklenmeyen alternatif |
|---|---|---|
| `react`, `react-dom`, `typescript`, `vite` | Çerçevede belirlendi | |
| `react-router` | İç içe rotalar, korumalı sayfalar, derin bağlantı | |
| `@tanstack/react-query` | Önbellek, yeniden doğrulama, geçersiz kılma ve parse draft yoklaması | Redux, Zustand: sunucu dışı durum çok az |
| `openapi-fetch` | Üretilen tiplerle ince `fetch` sarmalayıcı | Axios: `fetch` yeterli |
| `react-hook-form` | Uzun özgeçmiş formları, tekrar eden alanlar, az yeniden çizim | Formik |
| `zod`, `@hookform/resolvers` | API alan hatası vermediği için kurallar istemcide de gerekli | |
| `i18next`, `react-i18next` | Çoğul, değişken ve dil yükleme | Tarih ve sayı için dahili `Intl`, ek kütüphane yok |
| `@radix-ui/react-*` (yalnızca dialog, select, tabs) | Klavye ve ekran okuyucu davranışı doğru hazır gelir | Tam UI kiti: paket boyutu ve tema bağımlılığı |

**Geliştirme:** `openapi-typescript`, `vitest`, `@testing-library/react`, `@testing-library/user-event`, `msw` (sözleşmeye uygun API sahtesi), `@playwright/test`, `@axe-core/playwright`, `eslint`, `typescript-eslint`, `eslint-plugin-jsx-a11y`.

**Eklemiyorum:** Tailwind, moment/date-fns, lodash, sürükle bırak kütüphanesi. Sıralama erişilebilir yukarı ve aşağı düğmeleriyle yapılır.

**UI yaklaşımı:** CSS Modules ve CSS değişkenli tasarım token'ları. Çıktı katı CSP ile uyumlu tek bir CSS dosyasıdır.

### Rotalar

```text
/                       tanıtım
/login /register /forgot-password /reset-password /verify-email
/privacy /terms
/app                    özet
/app/resumes            /app/resumes/:id    /app/resumes/:id/analyses/:analysisId
/app/jobs               /app/jobs/:id       /app/jobs/:id/drafts/:draftId
/app/applications       /app/applications/:id
/app/settings           profil, güvenlik, veri, hesap silme
```

Ekranlar rota bazında tembel yüklenir. Dil rotada değil, kullanıcı tercihinde tutulur. Varsayılan `tr`, ikinci dil `en`.

### Davranışlar

**Soğuk açılış.** Geliştirmede ve Free planda geçerlidir. Ölçülen değerler: `/health/live` 14 sn, `/health/ready` 38 sn. Render yaklaşık bir dakikaya kadar söylüyor.

- Uygulama açılışta `/health/live` çağırır. 3 sn içinde yanıt yoksa "Sunucu uyanıyor, en fazla bir dakika sürebilir" ekranı ve geçen süre gösterilir.
- Ağ hataları ve 502/503/504 için yalnızca `GET` üstel gecikmeyle en çok 90 sn yeniden denenir.
- **`POST`, `PUT`, `DELETE` otomatik tekrarlanmaz.** Form verisi korunur, kullanıcı "Tekrar dene" der.
- Ücretli Render planına geçilince bu ekran kod değişikliği olmadan görünmez olur.

**ETag ve If-Match.**

- `eTag` gövdeden okunur ve React Query verisiyle birlikte tutulur.
- Her `PUT` ve korumalı `POST` son `eTag` ile gider.
- **412:** Form kullanıcının yazdıklarıyla açık kalır. "Bu kayıt başka yerde değişti" uyarısı, yeni sürümü yükleme ve iki sürümü karşılaştırma seçenekleri sunulur. Sessiz üzerine yazma yoktur.
- **428:** İstemci hatasıdır, Sentry benzeri araç olmadığı için `traceId` ile genel hata gösterilir.
- `If-Match` kullanmayan bölümlerde (eğitim, sertifika, dil, bağlantı) son yazan kazanır. Bu bilinen sınırdır.
- Parse draft için `eTag` alanı eklenene kadar bu akış başlamaz.

**ProblemDetails.** Tek bir `toApiError` işlevi şu biçimlere dönüştürür:

| Girdi | Sonuç |
|---|---|
| `application/problem+json` | `status`, `code`, `traceId` |
| `ValidationProblemDetails` | Alan bazlı hata, `setError` ile forma |
| Boş gövdeli 401 | Oturum sonu |
| JSON olmayan yanıt, ağ hatası, CORS'suz platform hatası | Bağlantı hatası |

Kullanıcıya `detail` değil, `code` ile çevrilmiş metin gösterilir. Destek için `traceId` kopyalanabilir.

**429 ve Retry-After.**

- `Retry-After` süresi kadar geri sayım, düğme kapalı, form verisi korunur.
- Başlık okunamazsa 60 sn kullanılır. Otomatik yeniden deneme yoktur.
- Parse bekleme süresi (10 dk) kullanıcıya açıkça söylenir.
- Sınır IP başınadır. Aynı ağdaki kullanıcılar birbirini etkileyebilir.

**Erişilebilirlik.** Hedef WCAG 2.2 AA.

- Anlamlı HTML, klavye ile tam kullanım, görünür odak.
- Rota değişiminde başlığa odak, sayfaya atlama bağlantısı.
- Hata metni alana `aria-describedby` ile bağlı, hata özetine odak.
- Parse durumu ve kaydetme sonucu `aria-live` ile duyurulur.
- `prefers-reduced-motion` desteği, `lang` özniteliği dille eşleşir.
- Playwright'ta axe kontrolü, CI'da ihlal olursa düşer. Elle NVDA turu yapılır.

**Mobil görünüm.**

- Önce 320 px genişlik. Yatay kaydırma yok.
- Tablolar dar ekranda kart listesi olur.
- Özgeçmiş düzenleyici bölümleri akordeon. Tek seferde tek form.
- Dokunma hedefi en az 44 px. Doğru `inputmode` ve `autocomplete`.
- Playwright 360 px ve 1280 px'de çalışır.

## 6. Yayın ve güvenlik

### Render Static Site

| Ayar | Değer |
|---|---|
| Kök dizin | `web` |
| Derleme | `npm ci && npm run build` |
| Yayın dizini | `web/dist` (Blueprint yolu kuruluşta doğrulanmalı) |
| Ortam | `VITE_API_BASE_URL=https://api.alanadi.com`, `NODE_VERSION`. Gizli değer yok. |
| Alan adı | Static Site'ta `alanadi.com`, web service'te `api.alanadi.com`. Render kök alan adı eklenince `www` adresini de ekler ve köke yönlendirir. |
| Otomatik deploy | Kapalı. CI, API hazır olduktan sonra tetikler. |

**SPA rewrite:** Kural `/*` → `/index.html`, Action `Rewrite`. Render, var olan dosyalara kuralı uygulamaz, bu yüzden `/assets` etkilenmez.

Rewrite ile API'yi aynı adrese vekillemeye güvenmiyorum: Doküman harici hedef için bunu açıkça doğrulamıyor. API ayrı adreste kalır.

**PR önizlemeleri:** İlk aşamada kapalı. Önizleme adresleri CORS listesinde olmaz.

### Başlıklar

`/*` için:

```text
Content-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src https://api.alanadi.com; manifest-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'
Strict-Transport-Security: max-age=31536000
X-Content-Type-Options: nosniff
Referrer-Policy: strict-origin-when-cross-origin
Permissions-Policy: camera=(), microphone=(), geolocation=()
```

`/assets/*` için `Cache-Control: public, max-age=31536000, immutable`. `/index.html` için `Cache-Control: no-cache`.

- Önce `Content-Security-Policy-Report-Only` ile denenir.
- Satır içi betik ve Google Fonts yoktur, yazı tipleri yerelden sunulur.
- HSTS'te `preload` yok, alan adı kararından sonra değerlendirilir.

### API ayarları

- `HttpSecurity__AllowedOrigins__0=https://alanadi.com`, tam eşleşme, joker yok.
- Kod değişikliği: `WithExposedHeaders("ETag", "Retry-After", "Location")`.
- `HttpSecurity__AllowCredentials=true`.
- **`/openapi` canlıda kapalı kalır.**
- `AllowedHosts=api.alanadi.com`, `PRODUCTION_URL=https://api.alanadi.com`.
- Özel alan adından sonra iki istemci ve sahte başlık testleri **tekrarlanır**. Render dokümanı Cloudflare vekillemesinin ayrıca dikkat istediğini söylüyor. Kendi Cloudflare hesabında vekilleme açılırsa `CF-Connecting-IP` güveni yeniden değerlendirilmelidir.

### CI değişiklikleri

| İş | İçerik |
|---|---|
| `web-checks` | `npm ci`, lint, tip denetimi, Vitest, build, `npm audit --omit=dev` |
| `api-contract` | API'den `v1.json` üret, tipleri üret, `git diff --exit-code`. Fark varsa düş. |
| `web-e2e` | PostgreSQL servisi ve API'ye karşı Playwright. Sahte sağlayıcı, gizli değer yok. |
| `deploy` | Yeni job'lara bağlı. API doğrulandıktan sonra statik site tetiklenir. |
| Dependabot | `npm` için `/web` eklenir, gruplanır. |

- **Üretim yöntemi:** API `Development` ortamında geçici ayarlarla başlatılıp `/openapi/v1.json` çekilir. Alternatif olan `Microsoft.Extensions.ApiDescription.Server` yeni bir paket ve tasarım zamanı ayarları ister.
- **Şema önkoşulu:** `ProducesResponseType` çağrılarının çoğunda hata tipi yok. `ProblemDetails` tipi eklenmeden hata şeması üretilmez.
- **Gizli değer kuralı:** Statik site deploy hook'u da yalnızca `deploy` job'unda kullanılır. Pull request job'ları gizli değer görmez.
- Statik site için deploy hook desteği kurulumda doğrulanmalıdır. Destek yoksa `web/**` için build filter kullanılan otomatik deploy devreye girer. Bu durumda API önce yayınlanacak biçimde geriye uyumlu değişiklik kuralı geçerlidir.

## 7. Yol haritası

Her satır tek PR'dır. Kapalı beta için 21 PR vardır. **Migration içeren her PR, migration uygulanmadan önce ayrıca onayını ister.** Alan adı yayına alınana kadar ekranlar yerelde geliştirilir, ama backend PR'ları merge edilince canlı API'ye deploy olur.

### Aşama A: Zorunlu backend temeli

| # | PR | Migration | Beta şartı | Neden ayrı | İçerik |
|---|---|---|---|---|---|
| 1 | API sözleşmesi | Hayır | Evet | Sonraki her PR buna dayanır, davranış değil biçim değiştirir | String enum, hata `code`, `ProblemDetails` tipleri, `Retry-After` ve `ETag` için CORS |
| 2 | Kapalı özellik yanıtı | Hayır | Evet | Parse davranışını değiştirir, kendi testleri olur | `GET /api/capabilities`, sağlayıcı veya worker yokken `POST .../parse` 503 ve `feature_disabled` kodu, draft oluşturmaz |
| 3 | Beceri kataloğu | **Evet** | Evet | Veri ve şema değişikliği, ayrı onay ister | Küratörlü liste, "katalogda yok" notu |
| 4 | Oturum temeli | **Evet** | Evet | Tablo ve token kuralları, cookie'den bağımsız test edilir | Refresh oturumları, rotasyon, tekrar kullanım, `SecurityStamp`, logout, oturum listesi, 15 dk access |
| 5 | Cookie ve CSRF | Hayır | Evet | Tarayıcı güvenlik yüzeyi, ayrı inceleme gerekir | HttpOnly cookie, `Origin` ve başlık kontrolü, CORS kimlik bilgisi |

### Aşama B: İnce dikey akış

Hedef: Yerelde giriş → özgeçmiş → ATS analizi uçtan uca çalışır.

| # | PR | Migration | Beta şartı | Neden ayrı | İçerik |
|---|---|---|---|---|---|
| 6 | Web iskeleti ve sözleşme CI'ı | Hayır | Evet | Ürün kodu yok, araç zinciri tek başına doğrulanır | `web/`, lint, test, tip üretimi, API istemcisi, `web-checks`, `api-contract`, Dependabot |
| 7 | Uygulama kabuğu ve giriş | Hayır | Evet | Tüm ekranların ortak temeli | Router, i18n, tasarım token'ları, hata ve 429 işleme, cold start, giriş, kayıt, sessiz yenileme |
| 8 | Özgeçmiş çekirdeği | Hayır | Evet | Dikey akışın veri kısmı | Liste, ana bilgiler, iletişim, deneyim, eğitim, beceri, 412 akışı |
| 9 | ATS analizi ve ilk E2E | Hayır | Evet | Dikey akışı kapatır, kanıtı Playwright üretir | Analiz sonucu, `web-e2e`, axe, giriş → özgeçmiş → analiz testi |

### Aşama C: Beta kapısı backend işleri

Akış çalıştıktan sonra her PR kendi API'si ve ekranıyla birlikte gelir.

| # | PR | Migration | Beta şartı | Neden ayrı | İçerik |
|---|---|---|---|---|---|
| 10 | Rıza ve davetli kayıt | **Evet** | Evet | Kayıt kapısını tek yerde değiştirir | Politika sürümü, kabul kaydı, yeni sürümde yeniden onay, davet kodu, gizlilik ve şartlar sayfaları |
| 11 | E-posta ve doğrulama | **Evet** | Evet | Gönderici altyapısı, PR 12 buna dayanır | Gönderici soyutlaması, yerelde sahte gönderici, doğrulama bağlantısı, doğrulanmamış hesaba kısıt |
| 12 | Parola ve profil | **Evet** | Evet | Hesap güvenliği, token tablosu ekler | Değiştirme, sıfırlama, tüm oturumları düşürme, profil güncelleme |
| 13 | Dışa aktarma ve hesap silme | **Evet** | Evet | Aynı veri haritasını kullanır, birlikte incelenmeli | JSON dışa aktarma, 7 gün geri alma, kalıcı silme işi, AI defteri anonimleştirme, ayarlar ekranı |
| 14 | Saklama ve işletme ayarları | **Evet** | Evet | Davranışı yapılandırma ile değiştirir, uyarı kaydı ekler | Purge açma, analiz hız sınırı, EF log seviyesi, `/api` için `no-store`, draft `eTag` alanı, hareketsizlik uyarısı ve silme |

### Aşama D: Kalan ürün ekranları

| # | PR | Migration | Beta şartı | Neden ayrı | İçerik |
|---|---|---|---|---|---|
| 15 | Kalan özgeçmiş bölümleri | Hayır | Evet | Tekrar eden formlar, PR 8'den sonra küçük | Proje, sertifika, dil, bağlantı, sıralama |
| 16 | İş ilanı ve eşleştirme | Hayır | Evet | Ayrı alan, yalnızca veriye bağlı | İlan CRUD, eşleştirme sonucu. Parse düğmesi gizli. |
| 17 | Başvuru takibi | Hayır | Evet | Durum geçişleri kendi akışıdır | Liste, filtre, detay, durum geçişleri |
| 18 | Erişilebilirlik ve mobil turu | Hayır | Evet | Tüm ekranları kesen kalite kapısı | Playwright kritik akışlar, axe, 360 px, NVDA turu |

### Aşama E: Yayın ve beta hazırlığı

| # | PR | Migration | Beta şartı | Neden ayrı | İçerik |
|---|---|---|---|---|---|
| 19 | Üretim yayını | Hayır | Evet | Alan adına bağlı tek değişiklik | Static Site, `alanadi.com`, `api.alanadi.com`, CSP, CORS, deploy sırası, iki istemci testini yenileme |
| 20 | Yedek ve geri yükleme runbook'u | Hayır | Evet | İşletme kanıtı, kod değil | Neon Launch ayarı, geri yükleme adımları, tatbikat sonucu |
| 21 | Hukuki metinlerin yayını | Hayır | Evet | Avukat görüşüne bağlı, zamanı dışarıdan belirlenir | Onaylı metinler ve sürüm kaydı |

### Hesap silme ve hareketsizlik (PR 13 ve 14)

**Hesap silme:**

- Silme isteği parola ile onaylanır, hesap `DeletionRequested` durumuna geçer, tüm oturumlar anında iptal edilir.
- 7 gün boyunca giriş kapalıdır. Geri alma yalnızca e-postadaki tek kullanımlık bağlantıyla yapılır.
- Kalıcı silme tek seferlik ve yeniden denenebilir bir iştir. Lease ile aynı anda tek çalıştırıcı olur, adımlar idempotent yazılır, yarıda kalan iş sonraki turda kaldığı yerden devam eder, başarısızlık metrik üretir.
- Bitiş kaydında kişisel veri tutulmaz.
- **Gizlilik metni notu:** Silinen verinin yedeklerde ne kadar kaldığı yazılacak. Süre, Neon geri yükleme penceresine eşittir (Launch ile 7 gün). Harici yedek eklenirse metin güncellenir.

**Hareketsizlik:**

- Süre yapılandırılabilir. Varsayılan 24 ay, avukat onayına bağlıdır ve bu belgeye işlenir.
- Silmeden önce e-posta uyarısı gider. Uyarı gönderilmeden silme başlamaz. Uyarı ile silme arası süre de ayarlanabilir, öneri 30 gün.
- Kullanıcı bu sürede giriş yaparsa silme iptal olur.
- E-posta teslim edilemezse hesap silinmez, kayıt incelemeye düşer.

### Beceri kataloğu tasarımı (PR 3)

- **Birincil kaynak: Kendi küratörlü listemiz.** Yaklaşık 300 ile 500 teknik ve mesleki beceri, repoda sürümlenen tek dosya. Lisans riski yoktur.
- **"Katalogda yok" notu:** `Resume` üzerinde kısa serbest metin alanı. Özgeçmişte görünür, analiz ve eşleştirmeye girmez.
- **Kullanıcı girdisi kataloğa otomatik eklenmez.** Katalog yalnızca PR ile değişir.
- Analiz motoru değişmez.

| Dış kaynak | Lisans | Kullanım önerisi |
|---|---|---|
| [ESCO](https://esco.ec.europa.eu/en/use-esco/download) | Karar 2011/833/EU: Ücretsiz yeniden kullanım, kaynak belirtilerek | Listeyi denetlemek için referans. Türkçe desteği doğrulanamadı. |
| [O\*NET](https://www.onetcenter.org/license_db.html) | CC BY 4.0: Atıf, lisans bağlantısı, değişiklik bildirimi, marka kuralı | ABD odaklı referans |

Dış kaynaktan satır alınırsa `THIRD_PARTY_NOTICES.md` ile atıf eklenir. İlk sürümde önerim satır almamaktır.

### Kapalı beta çıkış şartları

Tümü sağlanmadan davet gönderilmez:

- [ ] 1 ile 21 arası PR'lar merge edildi.
- [ ] Hesap silme, veri dışa aktarma, şifre sıfırlama, e-posta doğrulama ve rıza canlıda çalışıyor.
- [ ] Render Starter ve Neon Launch aktif.
- [ ] **Yedek alma ve geri yükleme denemesi başarılı, süresi kayıtlı.**
- [ ] **Veri sorumlusu yapısı avukatla netleşti. Bu tamamlanmadan davet gönderilmez.**
- [ ] **Avukat görüşü alındı, metinler yayında.**
- [ ] Yedeklerdeki verinin silinme süresi ve hareketsizlik süresi gizlilik metninde yazılı.
- [ ] CSP zorlayıcı modda, kritik E2E ve axe temiz.
- [ ] Özel alan adında iki istemci ve sahte başlık testleri geçti.
- [ ] OpenAI kapalı, parse isteği `feature_disabled` dönüyor.

### Açık lansman

| # | PR | Migration |
|---|---|---|
| 22 | Hesap başına giriş koruması, parola politikası | Olası |
| 23 | Davetli kaydı kapatma, kötüye kullanım önlemleri | Hayır |
| 24 | OpenAI açma: uyarı, rıza, bütçe, canary, parse draft ekranı | Olası |
| 25 | Özgeçmiş kopyalama | Hayır |
| 26 | PDF dışa aktarma (ayrı karar) | Olası |
| 27 | Hata izleme ve OTLP, kişisel veri denetimi | Hayır |
| 28 | Yük testi ve plan doğrulaması | Hayır |

Özgeçmiş dosyası yükleme ve ayrıştırma ayrı ürün ve gizlilik kararıdır, bu listeye dahil değildir.

## 8. Maliyet ve kararlar

Tutarlar ABD dolarıdır, vergi hariçtir.

### Maliyet aşamaları

| Aşama | Kalemler | Aylık | Yıllık eşdeğer |
|---|---|---|---|
| **Geliştirme** | Yalnızca `.com` alan adı. Render ve Neon ücretsiz plan. | ~0,93 | ~11,17 |
| **Beta öncesi** | Alan adı, Render Starter (7), Neon Launch (~4), Static Site (0), Resend Free (0) | ~12 | ~140 |
| Açık lansman başlangıcı | Beta öncesi ve gerekirse Resend Pro (20) | ~12–32 | ~140–380 |

Avukat görüşü tek seferlik gider ve beta çıkış şartıdır. Burada fiyatlanmadı, teklif alınmalı. OpenAI kapalı olduğu için maliyet üretmez.

### Maliyet kalemleri

| Kalem | Seçenek | Aylık | Yıllık | Not |
|---|---|---|---|---|
| Alan adı | `.com`, Cloudflare | ~0,93 | ~11,17 | İkincil kaynak, satın alırken doğrula |
| E-posta | Resend Free | 0 | 0 | 3.000/ay, 100/gün |
| E-posta | Resend Pro | 20 | 240 | 50.000/ay |
| E-posta | Postmark Basic | 15 | 180 | 10.000/ay |
| Frontend | Render Static | 0 | 0 | Bant genişliği ve dakika kotasına dahil |
| API | Render Free | 0 | 0 | Render üretim için önermiyor, uyku var |
| API | Render Starter | 7 | 84 | 512 MB, 0,5 CPU |
| Veritabanı | Neon Free | 0 | 0 | 6 saat geçmiş, zamanlanmış yedek yok |
| Veritabanı | Neon Launch, düşük kullanım | ~4 | ~48 | Varsayım aşağıda |
| Veritabanı | Neon Launch, sürekli açık | ~20 | ~240 | Worker açılırsa bu tarafa kayar |

**Neon varsayımı:** 1 GB depolama (0,35), 1 GB geri yükleme geçmişi (0,20), 0,25 CU ile ayda 120 aktif saat (30 CU-saat × 0,106 = 3,18). Toplam ~3,73. Sürekli açık: 182,5 CU-saat = 19,35, toplam ~19,90. Bunlar tahmindir. Ücretli planlarda aylık asgari ücret yok.

### Verilmiş kararlar

| Konu | Karar |
|---|---|
| Kimlik doğrulama | Seçenek A: Rotasyon, tekrar kullanım tespiti, oturum iptali, CSRF dahil |
| Alan adı | `.com`, `alanadi.com` ve `api.alanadi.com`. Ad sonra seçilecek. |
| Beceri kataloğu | Küratörlü liste ve "katalogda yok" notu, otomatik ekleme yok |
| Hosting zamanı | Beta öncesi Render Starter |
| Yedekleme | Beta öncesi Neon Launch, geri yükleme denemesi beta çıkış şartı |
| Hukuk | Avukat görüşü beta çıkış şartı |
| OpenAI | Betada kapalı, parse isteği açık hata döner |

### Onaylanan öneriler

| Karar | Onaylanan | Neden | Maliyet | Sonra değiştirmesi |
|---|---|---|---|---|
| Hosting: Starter ne zaman açılacak | PR 19 ile, davetten en az 1 hafta önce | Geliştirmede para harcamaz, yayını gerçek koşulda dener | 7/ay | **Kolay.** Plan değişir, kod değişmez. |
| Yedekleme: Neon Launch yeterli mi | 7 gün geçmiş yeterli. Harici `pg_dump` lansman öncesi yeniden değerlendirilir. | Harici yedek ek hesap ve gizli değer ister | ~4/ay, harici yedek ek | **Kolay**, ama kaçırılan geçmiş geri gelmez. |
| E-posta sağlayıcısı | Resend Free, `mail.alanadi.com` gönderen alan adı, SPF ve DKIM | Beta hacmine yeter, alt alan ana alan adının itibarını korur | 0. 3.000/ay üstü 20/ay. | **Kolay.** Soyutlama arkasında, DNS ve itibar yeniden kurulur. |
| Hukuki yapı: Veri sorumlusu kim | Avukatla şahıs veya şirket yapısını netleştir | Metinler, aktarım ve sorumluluk buna bağlı | Teklif alınmalı | **Zor.** Değişirse metinler ve rıza yenilenir. |
| OpenAI: Lansmanda açılma ölçütü | Beta geri bildirimi parse ihtiyacını gösterirse aç | Kapalı beta bu veri riskini taşımaz, ürün değeri önce ölçülür | Betada 0. Sonrası model ve bütçeye bağlı. | **Kolay** teknik olarak. Hukuki metin önceden hazır olmalı. |
| Hesap silme biçimi | 7 gün geri alma, bu sürede giriş kapalı, sonra kalıcı silme | Yanlışlıkla silmeyi kurtarır, süre gizlilik metninde yazılır | 0 | **Orta.** Şema ve metin değişir. Kalıcı silinen veri geri gelmez. |
| Veri saklama süreleri | Bölüm 2'deki tablo, 24 ay hareketsizlik (avukat onayına bağlı, ayarlanabilir), silmeden önce e-posta uyarısı | Süresiz saklamayı bitirir, kullanıcı tarafı basit kalır | 0 | **Orta.** Kısaltmak kolay, uzatmak yeni bilgilendirme ister. |

**KVKK notu:** Render, Neon, OpenAI ve e-posta sağlayıcıları yurt dışı aktarım sorusu doğurabilir. 7499 sayılı Kanun sonrası m.9'da açık rıza artık ana yol değil, sınırlı bir istisna olarak yorumlanıyor. Yeterlilik kararı ve standart sözleşme gibi yollar öne çıkıyor. Bu konuyu rıza kutusuyla çözülmüş sayma. Avukat değerlendirmesi gerekir.

## Kaynaklar

Render:

- [Static Sites](https://render.com/docs/static-sites)
- [Redirects and Rewrites](https://render.com/docs/redirects-rewrites)
- [Static Site Headers](https://render.com/docs/static-site-headers)
- [Custom Domains](https://render.com/docs/custom-domains): Kök alan adı DNS kaydı sağlayıcı rehberinden kurulumda alınacak.
- [Free instances](https://render.com/docs/free)
- [Blueprint spec](https://render.com/docs/blueprint-spec)
- [Pricing](https://render.com/pricing): Web service fiyatları sayfanın ham metninden okundu.

Neon:

- [Plans](https://neon.com/docs/introduction/plans)
- [Pricing](https://neon.com/pricing)
- [Backups](https://neon.com/docs/manage/backups)

Tarayıcı ve alan adı:

- [Public Suffix List](https://publicsuffix.org/list/public_suffix_list.dat): `onrender.com` satırı doğrudan dosyada doğrulandı.
- [MDN, Third-party cookies](https://developer.mozilla.org/en-US/docs/Web/Privacy/Guides/Third-party_cookies)
- [CostGoat, .com fiyat karşılaştırması](https://costgoat.com/compare/domains/com): İkincil kaynak.

OpenAI:

- [Data controls](https://developers.openai.com/api/docs/guides/your-data)

Beceri verisi:

- [ESCO download](https://esco.ec.europa.eu/en/use-esco/download)
- [ESCO FAQ](https://esco.ec.europa.eu/en/about-esco/faq)
- [Karar 2011/833/EU](https://eur-lex.europa.eu/eli/dec/2011/833/oj/eng)
- [O\*NET lisansı](https://www.onetcenter.org/license_db.html)

E-posta:

- [Resend pricing](https://resend.com/pricing)
- [Postmark pricing](https://postmarkapp.com/pricing)

KVKK:

- [Kurum duyurusu, standart sözleşmeler](https://www.kvkk.gov.tr/Icerik/7938/Standart-Sozlesmeler-ve-Baglayici-Sirket-Kurallarina-Iliskin-Dokumanlar-Hakkinda-Kamuoyu-Duyurusu)
- [EY, yurt dışı aktarım değişikliği](https://www.ey.com/tr_tr/insights/law/kvkk-degisikliginin-yurt-disi-veri-aktarimina-etkisi): İkincil kaynak.

### Doğrulanamayanlar

- Canlı `Skills` tablosu okunmadı, sonuç kod incelemesine dayanıyor.
- Render Hobby çalışma alanı ücreti ve dahil bant genişliği sayılarını okuyamadım.
- Render'da harici rewrite ve statik site deploy hook davranışı denenmedi.
- `store=false` varsayılanının davranışı OpenAI referansından değil, data controls sayfasından yorumlandı.
- OpenAI model fiyatı hesaplanmadı, model seçilmedi.
- ESCO'nun Türkçe sunulup sunulmadığı doğrulanamadı.
