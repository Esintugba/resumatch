# Fluent API configuration yaklaşımı

Bu klasör, `Api.Entities` altındaki somut entity'lerin EF Core eşlemeleri için ayrıldı.
Bu aşamada yalnızca klasör yapısı ve bu yaklaşım belgesi hazırlandı; aşağıda listelenen
configuration sınıfları henüz oluşturulmadı.

## Sınıf düzeni

Her somut entity için aynı isim köküne sahip tek bir configuration sınıfı kullanılacak.
Namespace `Api.Data.Configurations` olacak. Her sınıf doğrudan
`IEntityTypeConfiguration<TEntity>` implement edecek.

Aşağıdaki kod yalnızca gelecek dosyalar için şablondur:

```csharp
using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // Anahtar, alanlar, index'ler ve ilişkiler burada tanımlanacak.
    }
}
```

Entity dosyalarına DataAnnotation veya configuration attribute eklenmeyecek.
İleride DbContext oluşturulduğunda sınıflar `ApplyConfigurationsFromAssembly` ile
kaydedilecek. Configuration'lar çalışma sırasına bağımlı olmayacak; aynı ilişki iki
farklı sınıfta tekrar tanımlanmayacak. Bu aşamada DbContext oluşturulmadı.
Bu düzen [EF Core model configuration yaklaşımını](https://learn.microsoft.com/en-us/ef/core/modeling/) izler.

## Anahtarlar ve alanlar

- Her somut entity'nin anahtarı `HasKey(x => x.Id)` ile belirtilecek.
- Mevcut `Guid.NewGuid()` üretimi korunacak; `Id` için `ValueGeneratedNever()`
  kullanılarak uygulamanın sağladığı değer saklanacak.
- `BaseEntity` yalnızca ortak CLR tabanı olarak kalacak; ayrı entity, tablo veya
  `IEntityTypeConfiguration<BaseEntity>` olarak kaydedilmeyecek. `Id`, `CreatedAt`
  ve `UpdatedAt` kuralları somut entity configuration'larında uygulanacak.
- `CreatedAt` için mevcut uygulama tarafındaki UTC başlangıcı korunacak;
  `UpdatedAt` nullable kalacak. Bu katmanda tarih güncelleyen iş mantığı yazılmayacak.
- Zorunlu alanlar `IsRequired()`, opsiyonel alanlar `IsRequired(false)` ile mevcut
  nullable tiplerle uyumlu tanımlanacak.
- String uzunlukları alan bazında `HasMaxLength(n)` ile açık seçilecek. Uzun içerik
  alanları PostgreSQL `text` olarak değerlendirilecek; uzunluk sınırı gerekmeyen
  alanların bu tercihi ilgili configuration'da açık belirtilecek.
- Unique index'ler `HasIndex(...).IsUnique()` ile tanımlanacak; `User.Email`
  benzersizliği buna dahil. Harf büyüklüğü/normalizasyon politikası ayrıca
  değerlendirilecek; unique index tek başına normalizasyon yapmaz.
- Her decimal alan için `HasPrecision(precision, scale)` açık kullanılacak:
  `ResumeAnalysis` puanları, `KeywordMatch.Weight` ve `ResumeEducation.Grade`.
  Değerler ilgili configuration hazırlanırken alanın aralık ve hassasiyet ihtiyacına
  göre seçilecek. Precision, 0-100 gibi iş kurallarını tek başına doğrulamaz.

Alan yapılandırmaları için [EF Core property belgeleri](https://learn.microsoft.com/en-us/ef/core/modeling/entity-properties)
esas alınacak; veritabanı kısıtları uygulama katmanındaki girdi doğrulamasının yerini almaz.

## Enum ve PostgreSQL türleri

- Enum'lar `HasConversion<int>()` ile PostgreSQL `integer` olarak saklanacak;
  nullable enum'larda null korunacak. PostgreSQL native enum veya string dönüşümü
  kullanılmayacak. Veritabanı kullanılmaya başlandıktan sonra mevcut enum sayısal
  karşılıkları korunacak.
- `Guid` alanlar `uuid`, `DateOnly` alanlar `date`, decimal alanlar `numeric(p,s)`
  olarak eşlenecek.
- `DateTimeOffset` alanlar `timestamp with time zone` olarak eşlenecek. Yazılan
  değerler UTC (`Offset = 0`) olacak; PostgreSQL özgün timezone/offset bilgisini
  bu sütunda saklamaz. UTC dönüşümünün yapılması gerektiğinde sorumluluk daha sonra
  uygulama katmanında ele alınacak; entity dosyaları değiştirilmeyecek.

Kaynaklar: [EF Core value conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions),
[Npgsql type mapping](https://www.npgsql.org/efcore/mapping/general.html),
[Npgsql tarih ve saat eşlemeleri](https://www.npgsql.org/doc/types/datetime.html).

## İlişkiler ve silme davranışı

- Her ilişki tek yerde, FK'yi taşıyan entity'nin configuration'ında tanımlanacak.
  `HasOne`, `WithMany`/`WithOne`, `HasForeignKey` ve `OnDelete` açık kullanılacak.
- `ResumeContact` configuration'ında `Resume.Contact` ile One-to-One ilişki ve
  bağımlı tarafın `ResumeContact.ResumeId` FK'si açık belirtilecek; FK benzersiz olacak.
  Bir CV'nin iletişim kaydı olmaması mevcut nullable navigation ile uyumlu kalacak.
- `Resume.SourceResume` / `DerivedResumes` self-reference ilişkisi
  `ResumeConfiguration` içinde nullable `SourceResumeId` ile tanımlanacak.
- One-to-Many ilişkiler mevcut koleksiyonlarla eşlenecek. Ters navigation olmayan
  `Skill` ilişkilerinde ve `JobApplication.Resume` ilişkisinde `WithMany()` kullanılacak.
  `ProjectSkill.Project` için FK açıkça `ResumeProjectId` olacak.
- Başlangıç politikası tüm FK'lerde açık `DeleteBehavior.Restrict` olacak;
  otomatik cascade delete'e dayanılmayacak. `Cascade` yalnızca yaşam döngüsü
  bağımlılığı ve tüm silme yolları incelendikten sonra ilişkiye özel seçilebilecek.
  `SetNull` yalnızca nullable FK ve kaydın bağımsız korunması kararıyla kullanılabilecek.
- Özellikle kullanıcı, CV, iş ilanı, analiz ve başvuru arasındaki çoklu bağlantılar;
  paylaşılan `Skill` kayıtları ve CV self-reference ilişkisi otomatik silme açısından
  birlikte değerlendirilecek. PostgreSQL'e SQL Server'ın multiple cascade path
  kısıtı varsayılmayacak; hedef istenmeyen zincirleme silmeyi önlemek.
- `Restrict` veritabanında cascade silmeyi engeller; EF Core izlenen opsiyonel
  ilişkilerde FK'yi null yapabilir. Bu nedenle geçmiş bağlantıların korunması da
  gerekiyorsa silme politikası izlenen/izlenmeyen kayıtlarla ayrıca doğrulanacak.

Silme davranışlarının ayrıntıları: [EF Core cascade delete](https://learn.microsoft.com/en-us/ef/core/saving/cascade-delete).

## Sonraki aşamalarda oluşturulacak dosyalar

| Entity | Configuration dosyası |
| --- | --- |
| User | UserConfiguration.cs |
| Resume | ResumeConfiguration.cs |
| ResumeContact | ResumeContactConfiguration.cs |
| ResumeLink | ResumeLinkConfiguration.cs |
| ResumeExperience | ResumeExperienceConfiguration.cs |
| ExperienceBullet | ExperienceBulletConfiguration.cs |
| ResumeEducation | ResumeEducationConfiguration.cs |
| Skill | SkillConfiguration.cs |
| ResumeSkill | ResumeSkillConfiguration.cs |
| ResumeProject | ResumeProjectConfiguration.cs |
| ProjectSkill | ProjectSkillConfiguration.cs |
| ResumeCertification | ResumeCertificationConfiguration.cs |
| ResumeLanguage | ResumeLanguageConfiguration.cs |
| JobPosting | JobPostingConfiguration.cs |
| JobPostingSkill | JobPostingSkillConfiguration.cs |
| ResumeAnalysis | ResumeAnalysisConfiguration.cs |
| AnalysisIssue | AnalysisIssueConfiguration.cs |
| KeywordMatch | KeywordMatchConfiguration.cs |
| AnalysisRecommendation | AnalysisRecommendationConfiguration.cs |
| JobApplication | JobApplicationConfiguration.cs |
| ResumeExport | ResumeExportConfiguration.cs |

Toplam 21 somut entity configuration'ı planlanıyor. `BaseEntity` ve enum'lar için
ayrı configuration dosyası oluşturulmayacak.

Bu hazırlık sırasında `API.csproj` içinde EF Core/Npgsql paket referansları bulunmuyor.
Configuration kodu yazılmaya başlandığında EF Core ve Npgsql sağlayıcısının uyumlu
10.x sürümleri eklenecek; bu aşamada paket veya proje ayarı değiştirilmedi.
Entity, DTO, Repository, Service, Controller, DbContext veya Migration oluşturulmadı.
