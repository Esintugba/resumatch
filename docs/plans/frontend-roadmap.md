# ResuMatch frontend ve gerçek kullanıcı yol haritası

Durum: taslak, karar bekliyor. İnceleme tarihi: 2026-11-01, `main` commit'i `f459c56`.

Bu belge bir plandır. Kod, migration veya canlı veri değişikliği içermez. Belgedeki hukuk notları hukuki görüş değildir. Fiyatlar kaynak listesindeki tarihte okunmuştur ve değişebilir.

## Özet

- API'nin çekirdeği sağlam, ama bugün gerçek kullanıcıya açılamaz.
- **En büyük engel:** Kodda beceri kataloğunu dolduran hiçbir yol yok. Kullanıcı özgeçmişe beceri ekleyemez ve iş eşleştirmesi anlamlı sonuç üretmez.
- **Hesap yaşam döngüsü eksik:** Hesap silme, veri dışa aktarma, şifre değiştirme ve sıfırlama, e-posta doğrulama, rıza kaydı yok.
- **Özgeçmiş verisi OpenAI'ye gitmiyor.** Yalnızca iş ilanının başlığı ve açıklaması gidiyor. OpenAI şu an kapalı.
- **Önerilen sıra:** Önce API sözleşmesi ve hesap işleri, sonra frontend.
- **Önerilen kapalı beta maliyeti:** Aylık yaklaşık 12 ABD doları (bölüm 8).
- **Senin kararın:** On konu var, en acili kimlik doğrulama modeli ve alan adı (bölüm 8).

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
| Engel | Beceri kataloğu boş kalıyor. `Skill` ekleyen servis, seed veya migration verisi yok. `AddResumeSkillRequest` yalnızca `SkillId` kabul ediyor. | [AddResumeSkillRequest.cs](../../API/DTOs/Skill/AddResumeSkillRequest.cs), [SkillsController.cs](../../API/Controllers/SkillsController.cs) | Katalog verisi için ayrı karar ve PR. Canlıdaki tablo içeriğine bakmadım. |
| Yüksek | Enum'lar JSON'da sayı. `JsonStringEnumConverter` yok. Tipler sayı olarak üretilir, enum sırası değişirse istemci sessizce bozulur. | [Program.cs:650](../../API/Program.cs#L650) | Kullanıcı gelmeden string enum'a geç. |
| Yüksek | `Retry-After` tarayıcı kodundan okunamaz, CORS `WithExposedHeaders` tanımlamıyor. | [Program.cs:175](../../API/Program.cs#L175) | `Retry-After`, `ETag`, `Location` başlıklarını aç. |
| Yüksek | Hatalarda makine kodu yok, `detail` İngilizce. Doğrulama hataları hep "The supplied value is invalid." diyor. | [GlobalExceptionHandler.cs](../../API/Infrastructure/ExceptionHandling/GlobalExceptionHandler.cs), [Program.cs:656](../../API/Program.cs#L656) | Kararlı `code` uzantısı ekle. Alan kuralları istemcide de doğrulanır. |
| Yüksek | OpenAI kapalıyken parse isteği reddedilmiyor. Worker kapalı olduğundan draft `Pending` kalır. | [JobPostingParseDraftOptions.cs](../../API/Application/JobParsing/JobPostingParseDraftOptions.cs) | Özellik bayrağı ucu ekle veya isteği 503 ile reddet. Ekran bayrağa göre gizlenir. |
| Orta | Beklenmeyen 401 yanıtının gövdesi boş, ProblemDetails değil. | `AddJwtBearer` içinde `OnChallenge` yok | İstemci boş gövdeyi desteklesin. |
| Orta | Draft yanıtlarında `eTag` yok, yalnızca `draftVersion` (`uint`) var. | [EntityTagCodec.cs](../../API/Application/Common/Concurrency/EntityTagCodec.cs) | DTO'ya `eTag` ekle, biçim istemciye sızmasın. |
| Orta | Profil güncelleme, başvuru silme, öneri durumu güncelleme ucu yok. | Controller listesi | Kapsama göre ekle. |
| Orta | Özgeçmiş kopyalama ucu yok. `SourceResumeId` yalnızca bağlantı kuruyor; kopyalama kodu görmedim. | [ResumeService.cs](../../API/Application/Resumes/ResumeService.cs) | Sürümleme için `POST /resumes/{id}/duplicate`. |
| Orta | PDF dışa aktarma ve özgeçmiş yükleme yok. `ResumeExport` tablosu var, ucu yok. | [ResumeExport.cs](../../API/Entities/ResumeExport.cs) | Beta için kapsam dışı. Lansman kararı. |
| Orta | `POST /analyses` hız sınırı yok. Yalnızca login, register ve parse sınırlı. Analiz CPU harcıyor. | 3 adet `EnableRateLimiting` | Kullanıcı başına sınır ekle. |
| Düşük | `/api` yanıtlarında `Cache-Control: no-store` yok. | Yalnızca [Program.cs:751](../../API/Program.cs#L751) gönderiyor | Kişisel veri yanıtlarına ekle. |
| Düşük | Bölüm listeleri bölüme göre 20 ile 100 kayıtla sınırlı, sıralama elle `sortOrder`. | [ResumeSectionLimits.cs](../../API/Application/Common/ResumeSectionLimits.cs) | Formlarda sınır uyarısı. |

## 2. Gerçek kullanıcı eksikleri

Önem: **P0** kapalı betadan önce, **P1** açık lansmandan önce, **P2** lansman sonrası. Kapsam: S 1 PR, M 2 PR, L 3 veya daha fazla PR.

| Konu | Mevcut durum | Önem | Kapsam | Plan |
|---|---|---|---|---|
| Hesap silme | Yok. `User` üzerinden `Resume`, `JobPosting`, `JobApplication` ilişkileri `Restrict`. `AiUsageReservation` ve `AiUsageBudgetPeriod` içinde FK'siz `UserId` var. | P0 | M | Tek işlemde sil, AI defterlerini anonimleştir, parola ile onayla. |
| Veri dışa aktarma | Yok. | P0 | S | Tek JSON dosyası: `GET /account/export`. |
| Şifre değiştirme | Yok. | P0 | S | Mevcut parolayı iste, oturumları düşür. |
| Şifre sıfırlama | Yok. E-posta altyapısı da yok. | P0 | M | Tek kullanımlık, kısa ömürlü, özetlenmiş token. Hesabın varlığını açıklamayan yanıt. |
| Oturum iptali | Yok. Yalnızca `IsActive` kontrolü var. Parola değişse bile token en çok 60 dk geçerli kalır. | P0 | S | `SecurityStamp` claim'i. [`OnTokenValidated`](../../API/Program.cs#L495) zaten her istekte DB'ye gidiyor. |
| E-posta doğrulama | `IsEmailVerified` alanı var, hep `false` kalıyor. | Beta P1, lansman P0 | M | Doğrulama bağlantısı. Lansmanda zorunlu. |
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

1. Parse düğmesinin yanında "Bu metin OpenAI'ye gönderilir" uyarısı, ilk kullanımda onay.
2. Hizmet alan olarak aydınlatma metnine ekleme, yurt dışı aktarım değerlendirmesi.
3. `PurgeEnabled`, bütçe, sabit model sürümü ve OpenAI harcama limiti.

## 4. Kimlik doğrulama tasarımı

### Mevcut durum

- HS256 erişim token'ı, 60 dakika, [appsettings.json:12](../../API/appsettings.json#L12) (üst sınır 24 saat).
- Refresh token ve çıkış ucu yok.
- `AllowCredentials` ayarı var, cookie okuma kodu yok.

### onrender.com ve cookie

- `onrender.com` [Public Suffix List](https://publicsuffix.org/list/public_suffix_list.dat) içinde. Bu yüzden `a.onrender.com` ile `b.onrender.com` **farklı sitedir**.
- `Domain=.onrender.com` ile cookie ayarlanamaz.
- Frontend ve API iki ayrı `onrender.com` adresinde kalırsa cookie üçüncü taraf sayılır: Safari varsayılan olarak korur, Firefox site başına ayırır, Chrome varsayılanda engellemez. Güvenilir değildir.
- **Cookie tabanlı model için özel alan adı şart:** `app.ornek.com` ve `api.ornek.com` aynı sitedir.

### Seçenekler

| | A. Bellekte access, HttpOnly cookie'de refresh | B. HttpOnly cookie'de oturum | C. localStorage'da token | D. Yalnızca bellekte token |
|---|---|---|---|---|
| XSS ile uzun süreli hesap ele geçirme | Zor, refresh okunamaz | Zor | **Kolay, token okunur ve dışarı gönderilir** | Zor |
| XSS sırasında istek yaptırma | Mümkün | Mümkün | Mümkün | Mümkün |
| CSRF | Korunmalı, yalnızca refresh ucu | Korunmalı, tüm yazma uçları | Etkisiz | Etkisiz |
| Sayfa yenileyince | Sessiz yenileme | Oturum sürer | Oturum sürer | **Tekrar giriş** |
| Oturum iptali | Sunucu tarafı, anında | Sunucu tarafı | Token süresi kadar gecikme | Token süresi kadar gecikme |
| Özel alan adı gerekir mi | **Evet** | **Evet** | Hayır | Hayır |
| Migration | Evet, refresh tablosu | Olası | Hayır | Hayır |
| Backend işi | M | M | Yok | Yok |

**Öneri: A.** Özgeçmiş kişisel veri içerdiği için XSS etkisini en iyi sınırlayan modeldir. Refresh token'ı döndürülür, özeti saklanır, tekrar kullanımda oturum ailesi kapatılır. Access süresi 15 dakikaya iner. Cookie: `__Host-` önekli, `HttpOnly`, `Secure`, `SameSite=Lax`, yalnızca `api` alan adına ait. Refresh ucu `Origin` başlığını da doğrular.

**Hızlı başlangıç gerekirse: D.** Altyapı değiştirmez, daha sonra A'ya geçilebilir. Bedeli sayfa yenilemede tekrar giriş ve 60 dakikalık oturumdur.

### localStorage (C) seçilirse

Risk açıktır: Sayfada çalışan tek bir kötü niyetli betik token'ı okuyup dışarı yollayabilir. Bağımlılık saldırısı veya bir XSS yeterlidir. Token süresi dolana kadar hesap ele geçirilir ve kullanıcı bunu kapatamaz.

Zorunlu önlemler:

- Katı CSP: `script-src 'self'`, `connect-src` yalnızca API. Bu, token'ın dışarı gönderilmesini de zorlaştırır.
- `dangerouslySetInnerHTML` yasak, ESLint ile zorunlu.
- Üçüncü taraf betik yok. Bağımlılık sayısı düşük, lockfile zorunlu, CI'da `npm audit`.
- Token süresi en çok 15 dakika, bu yüzden yenileme sorunu ayrıca çözülmeli.
- `SecurityStamp` ile sunucu tarafı iptal.

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

**Soğuk açılış.** Ölçülen değerler: `/health/live` 14 sn, `/health/ready` 38 sn. Render yaklaşık bir dakikaya kadar söylüyor.

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
| Ortam | `VITE_API_BASE_URL`, `NODE_VERSION`. Gizli değer yok. |
| Otomatik deploy | Kapalı. CI, API hazır olduktan sonra tetikler. |

**SPA rewrite:** Kural `/*` → `/index.html`, Action `Rewrite`. Render, var olan dosyalara kuralı uygulamaz, bu yüzden `/assets` etkilenmez.

Rewrite ile API'yi aynı adrese vekillemeye güvenmiyorum: Doküman harici hedef için bunu açıkça doğrulamıyor. API ayrı adreste kalır.

**PR önizlemeleri:** İlk aşamada kapalı. Önizleme adresleri CORS listesinde olmaz.

### Başlıklar

`/*` için:

```text
Content-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src https://API_ADRESI; manifest-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'
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

- `HttpSecurity__AllowedOrigins__0=https://app.ornek.com`, tam eşleşme, joker yok.
- Kod değişikliği: `WithExposedHeaders("ETag", "Retry-After", "Location")`.
- `AllowCredentials` yalnızca A veya B seçilirse `true`.
- **`/openapi` canlıda kapalı kalır.**
- Özel alan adına geçince `AllowedHosts` ve `PRODUCTION_URL` güncellenir.
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

Her satır tek PR'dır. **M** migration içerir ve uygulanmadan önce onayın gerekir.

### Aşama 0: API sözleşmesi

| # | PR | Kapsam |
|---|---|---|
| 1 | String enum JSON | Beta |
| 2 | CORS açık başlıklar, `/api` için `no-store` | Beta |
| 3 | Hata `code` alanı, `ProblemDetails` yanıt tipleri | Beta |
| 4 | Draft `eTag` alanı | Beta |
| 5 | Özellik bayrakları ucu, OpenAI kapalıyken parse reddi | Beta |
| 6 | Analiz hız sınırı | Beta |
| 7 | Beceri kataloğu verisi **M** | Beta |

### Aşama 1: Hesap ve uyum

| # | PR | Kapsam |
|---|---|---|
| 8 | Saklama, yedek ve geri yükleme runbook'u, tatbikat | Beta |
| 9 | `SecurityStamp` ve oturum iptali **M** | Beta |
| 10 | Seçilen auth modeli, refresh akışı **M** | Beta |
| 11 | Şifre değiştirme, profil güncelleme | Beta |
| 12 | Rıza ve politika sürümü kaydı **M** | Beta |
| 13 | Davetli kayıt | Beta |
| 14 | E-posta gönderici soyutlaması | Beta |
| 15 | Şifre sıfırlama **M** | Beta |
| 16 | Veri dışa aktarma | Beta |
| 17 | Hesap silme **M** | Beta |
| 18 | Purge ve log seviyesi ayarları | Beta |

### Aşama 2: Frontend temeli

| # | PR | Kapsam |
|---|---|---|
| 19 | `web/` iskeleti, lint, test altyapısı | Beta |
| 20 | CI `web-checks`, `api-contract`, Dependabot | Beta |
| 21 | Tip üretimi, API istemcisi, hata dönüşümü, cold start | Beta |
| 22 | Düzen, i18n, tasarım token'ları, erişilebilirlik temeli | Beta |
| 23 | Static Site yayını, başlıklar, CORS ayarı | Beta |
| 24 | Giriş, kayıt, şifre ekranları | Beta |
| 25 | Gizlilik, şartlar, rıza ekranı | Beta |

### Aşama 3: Ürün ekranları

| # | PR | Kapsam |
|---|---|---|
| 26 | Özgeçmiş listesi ve ana bilgiler | Beta |
| 27 | Düzenleyici: iletişim, deneyim, eğitim | Beta |
| 28 | Düzenleyici: beceri, proje, sertifika, dil, bağlantı | Beta |
| 29 | ATS analiz sonucu | Beta |
| 30 | İş ilanları ve eşleştirme | Beta |
| 31 | Başvuru takibi | Beta |
| 32 | Ayarlar: dışa aktarma, güvenlik, hesap silme | Beta |
| 33 | Playwright kritik akışları ve axe, `web-e2e` | Beta |

### Kapalı beta çıkışı

Davetli kullanıcılar, OpenAI kapalı. Şartlar:

- 1 ile 33 arası tamam.
- Hukuki metinler yayında.
- Geri yükleme tatbikatı başarılı.
- CSP zorlayıcı modda.
- Canlı duman testi temiz.

### Açık lansman

| # | PR |
|---|---|
| 34 | E-posta doğrulama zorunluluğu **M** |
| 35 | Hesap başına giriş koruması |
| 36 | Parola politikası kararı |
| 37 | Davetli kaydı kapatma, kötüye kullanım önlemleri |
| 38 | OpenAI açma: uyarı, rıza, bütçe, canary |
| 39 | Parse draft ekranı ve onay akışı |
| 40 | PDF dışa aktarma (ayrı karar) |
| 41 | Özgeçmiş kopyalama |
| 42 | Hata izleme ve OTLP, kişisel veri denetimi |
| 43 | Yük testi, ücretli plan doğrulaması |

Özgeçmiş dosyası yükleme ve ayrıştırma ayrı ürün ve gizlilik kararıdır, bu listeye dahil değildir.

## 8. Maliyet ve kararlar

Tutarlar ABD dolarıdır, vergi hariçtir.

### Maliyet kalemleri

| Kalem | Seçenek | Aylık | Yıllık | Not |
|---|---|---|---|---|
| Alan adı | `.com`, Cloudflare | ~0,93 | ~11,17 | İkincil kaynak, satın alırken doğrula |
| Alan adı | `.com.tr` | Doğrulanmadı | Doğrulanmadı | Resmi güncel fiyat bulunamadı |
| E-posta | Resend Free | 0 | 0 | 3.000/ay, 100/gün |
| E-posta | Resend Pro | 20 | 240 | 50.000/ay |
| E-posta | Postmark Basic | 15 | 180 | 10.000/ay |
| Frontend | Render Static | 0 | 0 | Bant genişliği ve dakika kotasına dahil |
| API | Render Free | 0 | 0 | Render üretim için önermiyor, uyku var |
| API | Render Starter | 7 | 84 | 512 MB, 0,5 CPU |
| Veritabanı | Neon Free | 0 | 0 | 6 saat geçmiş, zamanlanmış yedek yok |
| Veritabanı | Neon Launch, düşük kullanım | ~4 | ~48 | Varsayım aşağıda |
| Veritabanı | Neon Launch, sürekli açık | ~20 | ~240 | Worker açılırsa bu tarafa kayar |
| OpenAI | Kapalı | 0 | 0 | Açılırsa bütçe kararı gerekir |

**Neon varsayımı:** 1 GB depolama (0,35), 1 GB geri yükleme geçmişi (0,20), 0,25 CU ile ayda 120 aktif saat (30 CU-saat × 0,106 = 3,18). Toplam ~3,73. Sürekli açık: 182,5 CU-saat = 19,35, toplam ~19,90. Bunlar tahmindir. Ücretli planlarda aylık asgari ücret yok.

### Senaryolar

| Senaryo | Aylık | Yıllık | Yorum |
|---|---|---|---|
| En düşük | ~1 | ~11 | Uyku ve zayıf yedek. Yalnızca arkadaş denemesi. |
| **Önerilen kapalı beta** | **~12** | **~140** | Alan adı, Starter, Neon Launch, Resend Free |
| Açık lansman başlangıcı | ~12–32 | ~140–380 | Üst sınır Resend Pro ile. OpenAI ve Neon kullanımı hariç. |

Avukat görüşü tek seferliktir ve burada fiyatlanmadı.

### Kararların

| # | Karar | Seçenekler | Öneri | Etkisi |
|---|---|---|---|---|
| 1 | Kimlik doğrulama | A, B, C, D (bölüm 4) | **A** | PR 10, alan adı |
| 2 | Alan adı | `.com`, `.com.tr`, sonra almak | **`.com` şimdi** | A ve B bunsuz güvenilir çalışmaz |
| 3 | Beceri kataloğu | Küratörlü liste, kullanıcı girişi serbest, ikisi birden | **Küratörlü liste ve "katalogda yok" notu** | PR 7. Migration onayı gerekir. |
| 4 | Hosting | Free, Starter | **Davetli kullanıcı gelmeden Starter** | Soğuk açılış, kullanım şartı |
| 5 | Yedekleme | Neon Launch, şifreli `pg_dump` ve depolama, ikisi | **Neon Launch 7 gün** | `pg_dump` için yeni hesap ve gizli değer gerekir |
| 6 | E-posta | Resend, Postmark | **Resend Free ile başla** | Gönderen alan adı gerekir |
| 7 | Hukuki yapı | Şahıs, şirket, avukat görüşü | **Beta öncesi avukat görüşü** | Veri sorumlusu kimliği, yurt dışı aktarım |
| 8 | OpenAI zamanı | Betada kapalı, betada açık | **Betada kapalı** | PR 38 ve 39 |
| 9 | Hesap silme | Hemen, 7 gün geri alma süresi | **7 gün** | PR 17 |
| 10 | Veri saklama | Bölüm 2'deki öneri, farklı süreler | **Öneri** | Gizlilik metni |

**KVKK notu:** Render, Neon, OpenAI ve e-posta sağlayıcıları yurt dışı aktarım sorusu doğurabilir. 7499 sayılı Kanun sonrası m.9'da açık rıza artık ana yol değil, sınırlı bir istisna olarak yorumlanıyor. Yeterlilik kararı ve standart sözleşme gibi yollar öne çıkıyor. Bu konuyu rıza kutusuyla çözülmüş sayma. Avukat değerlendirmesi gerekir.

## Kaynaklar

Render:

- [Static Sites](https://render.com/docs/static-sites)
- [Redirects and Rewrites](https://render.com/docs/redirects-rewrites)
- [Static Site Headers](https://render.com/docs/static-site-headers)
- [Custom Domains](https://render.com/docs/custom-domains)
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
- `.com.tr` güncel fiyatı bulunamadı.
