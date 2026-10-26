# DTO yaklaşımı

DTO'lar API sözleşmesidir; Api.Entities altındaki sınıflar doğrudan request
veya response modeli olarak kullanılmaz.

## Klasörler ve planlanan DTO'lar

### Auth

- RegisterRequest
- LoginRequest
- UpdateProfileRequest
- AuthResponse
- CurrentUserResponse

### Resume

- CreateResumeRequest
- UpdateResumeRequest
- ResumeResponse
- ResumeListItemResponse
- UpsertResumeContactRequest
- ResumeContactResponse
- CreateResumeLinkRequest
- UpdateResumeLinkRequest
- ResumeLinkResponse
- CreateResumeExportRequest
- ResumeExportResponse

### Experience

- CreateResumeExperienceRequest
- UpdateResumeExperienceRequest
- ResumeExperienceResponse
- CreateExperienceBulletRequest
- UpdateExperienceBulletRequest
- ExperienceBulletResponse

### Education

- CreateResumeEducationRequest
- UpdateResumeEducationRequest
- ResumeEducationResponse

### Skill

- SkillResponse
- SkillListItemResponse
- CreateResumeSkillRequest
- UpdateResumeSkillRequest
- ResumeSkillResponse
- CreateSkillRequest yalnızca merkezi sözlük yönetimi endpoint'i gerekirse

### Project

- CreateResumeProjectRequest
- UpdateResumeProjectRequest
- ResumeProjectResponse
- AddProjectSkillRequest
- ProjectSkillResponse

### Certification

- CreateResumeCertificationRequest
- UpdateResumeCertificationRequest
- ResumeCertificationResponse

### Language

- CreateResumeLanguageRequest
- UpdateResumeLanguageRequest
- ResumeLanguageResponse

### JobPosting

- CreateJobPostingRequest
- UpdateJobPostingRequest
- JobPostingResponse
- JobPostingListItemResponse
- CreateJobPostingSkillRequest
- UpdateJobPostingSkillRequest
- JobPostingSkillResponse

### Analysis

- AnalyzeResumeRequest
- ResumeAnalysisResponse
- ResumeAnalysisListItemResponse
- AnalysisIssueResponse
- KeywordMatchResponse
- AnalysisRecommendationResponse
- UpdateRecommendationStatusRequest

Analiz skorları ve AI sonuçları client tarafından oluşturulan request alanları
değildir; server tarafından hesaplanır.

### JobApplication

- CreateJobApplicationRequest
- UpdateJobApplicationRequest
- JobApplicationResponse
- JobApplicationListItemResponse

## İsimlendirme ve namespace

- Namespace biçimi Api.DTOs.Domain olur.
- Oluşturma modelleri Create...Request, güncelleme modelleri
  Update...Request, cevap modelleri ...Response adını kullanır.
- Liste ekranları için gereken alanlarla sınırlı ...ListItemResponse
  modelleri kullanılır.
- Request ve response modelleri aynı sınıf üzerinden paylaşılmaz.

## Güvenlik ve sahiplik

Aşağıdaki alanlar client request'lerinden kesinlikle alınmaz:

- UserId
- ResumeUserId
- JobPostingUserId
- ResumeAnalysis.UserId
- SourceResumeIsMaster
- CreatedAt
- UpdatedAt
- Create request'lerinde Id
- PasswordHash

UserId ve yardımcı sahiplik alanları authenticated kullanıcının identity/claim
bilgisinden Application katmanında atanır. PasswordHash response modellerine de
konulmaz; kayıt/giriş request'i yalnızca plain-text parolayı taşıyabilir ve
parola server tarafında hashlenir.

ResumeId, JobPostingId, SkillId gibi kaynak kimlikleri işlem için gerekliyse
request'te bulunabilir. Application katmanı bu kaynakların authenticated
kullanıcıya ait olduğunu doğrular.

Aşağıdaki alanlar da ilgili işlem server tarafından yönetildiği sürece request
modellerine eklenmez:

- Normalize edilmiş email, skill ve dil değerleri
- ATS ve job-match skorları
- AnalyzedAt ve AnalyzerVersion
- AI üretim işaretleri
- ParsedSummary
- LastExportedAt ve ExportedAt
- Email doğrulama ve aktiflik durumu

Update işlemlerinde kaynak Id değeri request body yerine route üzerinden alınır.

## Validation

- DataAnnotations yalnızca request DTO'larında kullanılır.
- Temel doğrulamalar için Required, StringLength, EmailAddress, Url ve
  gerektiğinde Range tercih edilir.
- Entity sınıflarına DataAnnotation eklenmez.
- Veritabanı constraint'leri son güvenlik katmanı olarak kalır.
- İş kuralları ve ownership doğrulaması Application katmanında yapılır.

## Nullable ve update semantiği

İlk update sözleşmeleri PUT tabanlı tam güncelleme olarak tasarlanır. Required
alanlar non-nullable, temizlenmesine izin verilen alanlar nullable olur.

PUT modelinde gönderilmeyen nullable alan ile açıkça null gönderilen alan ayırt
edilemez. PATCH eklendiğinde "gönderilmedi" ve "null yap" ayrımı için ayrı bir
optional-value yaklaşımı veya JSON Patch sözleşmesi tasarlanır.

## Mapping

Bu aşamada AutoMapper veya başka mapping paketi kullanılmaz. Mapping stratejisi
Application katmanı tasarlanırken belirlenecek; entity navigation property'leri
DTO'lara doğrudan taşınmayacaktır.
