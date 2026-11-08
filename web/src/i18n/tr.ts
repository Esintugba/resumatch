import type { ApiErrorCode } from '../api/problem';

// Every text the interface shows. English is added later as a second catalog
// of the same type (see Messages), so nothing here may be inlined elsewhere.
export const tr = {
  locale: 'tr',
  appName: 'ResuMatch',
  skipToContent: 'İçeriğe geç',
  pageTitle: (page: string) => `${page} · ResuMatch`,

  serverWake: {
    waking: 'Sunucu uyanıyor, en fazla bir dakika sürebilir.',
    elapsed: (seconds: number) => `Geçen süre: ${String(seconds)} sn`,
    unreachable: 'Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.',
    retry: 'Tekrar dene',
  },

  session: {
    checking: 'Oturum kontrol ediliyor…',
  },

  login: {
    title: 'Giriş yap',
    intro: 'Hesabınıza e-posta adresiniz ve parolanızla girin.',
    email: 'E-posta',
    password: 'Parola',
    submit: 'Giriş yap',
    submitting: 'Giriş yapılıyor…',
    errorSummary: 'Giriş yapılamadı',
    emailRequired: 'E-posta adresinizi girin.',
    emailInvalid: 'Geçerli bir e-posta adresi girin.',
    passwordRequired: 'Parolanızı girin.',
    fieldInvalid: 'Bu alanı kontrol edin.',
    rateLimited: (seconds: number) =>
      `Çok fazla deneme yapıldı. ${String(seconds)} saniye sonra tekrar deneyebilirsiniz.`,
    rateLimitOver: 'Tekrar deneyebilirsiniz.',
    signOutIncomplete:
      'Çıkış tamamlanamadı, bağlantınızı kontrol edin. Bu cihazda oturum kapatıldı; bağlantı gelince çıkış yeniden denenecek.',
    waitCountdown: (seconds: number) => `Bekleyin (${String(seconds)} sn)`,
  },

  nav: {
    label: 'Ana gezinme',
    resumes: 'Özgeçmişlerim',
    signedInAs: (name: string) => `${name} olarak giriş yapıldı`,
    signOut: 'Çıkış yap',
  },

  common: {
    loading: 'Yükleniyor…',
    save: 'Kaydet',
    saving: 'Kaydediliyor…',
    cancel: 'Vazgeç',
    edit: 'Düzenle',
    close: 'Kapat',
    empty: 'Girilmedi',
    retry: 'Tekrar dene',
    loadFailed: 'Bilgiler yüklenemedi.',
  },

  forms: {
    required: 'Bu alan zorunlu.',
    maxLength: (length: number) => `En fazla ${String(length)} karakter olabilir.`,
    emailInvalid: 'Geçerli bir e-posta adresi girin.',
    fieldInvalid: 'Bu alanı kontrol edin.',
    errorSummary: 'Kaydedilemedi',
    saved: (section: string) => `${section} kaydedildi.`,
  },

  unsaved: {
    title: 'Kaydedilmemiş değişiklikler var',
    description: 'Bu bölümde kaydetmediğiniz değişiklikler kaybolacak.',
    discard: 'Değişiklikleri at',
    stay: 'Bu bölümde kal',
    signOutDescription: 'Kaydetmediğiniz değişiklikler kaybolacak. Yine de çıkış yapılsın mı?',
    signOutConfirm: 'Değişiklikleri at ve çıkış yap',
    signOutStay: 'Sayfada kal',
  },

  conflict: {
    title: 'Bu kayıt başka yerde değişti',
    description:
      'Siz düzenlerken bu bilgiler başka bir sekmede ya da cihazda kaydedildi. Yazdıklarınız aşağıda duruyor; üzerine yazılmadı. Kaydetmek için önce yeni sürümü yükleyin.',
    reload: 'Yeni sürümü yükle',
    confirmTitle: 'Yeni sürüm yüklensin mi?',
    confirmDescription: 'Bu bölümde yazdıklarınız silinecek ve kayıtlı sürüm yüklenecek. Gerekirse önce yazdıklarınızı kopyalayın.',
    confirm: 'Yazdıklarımı sil ve yükle',
    reloaded: 'Kayıtlı sürüm yüklendi.',
  },

  resumes: {
    title: 'Özgeçmişlerim',
    createTitle: 'Yeni özgeçmiş',
    createLabel: 'Özgeçmiş adı',
    createHint: 'Örneğin: Yazılım geliştirici – 2026',
    create: 'Oluştur',
    creating: 'Oluşturuluyor…',
    empty: 'Henüz bir özgeçmişiniz yok. İlkini yukarıdan oluşturarak başlayın.',
    listLabel: 'Özgeçmiş listesi',
    noActive: 'Etkin özgeçmişiniz yok. Yeni bir tane oluşturabilirsiniz.',
    archiveSection: 'Arşiv',
    archivedListLabel: 'Arşivlenen özgeçmişler',
    updatedAt: (date: string) => `Son değişiklik: ${date}`,
    master: 'Ana özgeçmiş',
    archived: 'Arşivlendi',
    archive: 'Arşivle',
    archiving: 'Arşivleniyor…',
    archiveTitle: (title: string) => `“${title}” arşivlensin mi?`,
    archiveDescription: 'Arşivlenen özgeçmiş listede “Arşivlendi” olarak kalır. Arşivden çıkarma henüz yok.',
    archivedDone: (title: string) => `“${title}” arşivlendi.`,
    previous: 'Önceki sayfa',
    next: 'Sonraki sayfa',
    page: (page: number, total: number) => `Sayfa ${String(page)} / ${String(total)}`,
  },

  editor: {
    back: 'Özgeçmişlerime dön',
    archivedBanner: 'Bu özgeçmiş arşivlendi. Düzenlemeye devam edebilirsiniz.',
    notFound: 'Bu özgeçmiş bulunamadı.',
    sectionsLabel: 'Özgeçmiş bölümleri',
    main: {
      title: 'Ana bilgiler',
      resumeTitle: 'Özgeçmiş adı',
      professionalTitle: 'Unvan',
      targetRole: 'Hedef pozisyon',
      summary: 'Özet',
      summaryHint: 'Kendinizi birkaç cümleyle anlatın.',
    },
    contact: {
      title: 'İletişim',
      firstName: 'Ad',
      lastName: 'Soyad',
      email: 'E-posta',
      phoneNumber: 'Telefon',
      city: 'Şehir',
      country: 'Ülke',
      postalCode: 'Posta kodu',
    },
  },

  errors: {
    network: 'Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.',
    unexpected: 'Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.',
    supportCode: (traceId: string) => `Destek kodu: ${traceId}`,
    codes: {
      validation_failed: 'Girdiğiniz bilgileri kontrol edin.',
      malformed_request: 'İstek okunamadı. Sayfayı yenileyip tekrar deneyin.',
      unauthorized: 'E-posta adresi veya parola hatalı.',
      not_found: 'Aradığınız kayıt bulunamadı.',
      conflict: 'Bu işlem kaydın şu anki durumuyla çakışıyor.',
      precondition_failed: 'Bu kayıt başka bir yerde değişti. Sayfayı yenileyin.',
      precondition_required: 'İşlem tamamlanamadı. Sayfayı yenileyip tekrar deneyin.',
      payload_too_large: 'Gönderilen bilgi çok büyük.',
      unsupported_media_type: 'İstek biçimi desteklenmiyor.',
      rate_limited: 'Çok fazla deneme yapıldı. Biraz bekleyip tekrar deneyin.',
      internal_error: 'Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.',
      feature_disabled: 'Bu özellik şu anda kullanılamıyor.',
      csrf_rejected: 'Güvenlik denetimi başarısız oldu. Sayfayı yenileyin.',
      registration_rejected: 'Kayıt tamamlanamadı.',
      section_limit_reached: 'Bu bölüm için en fazla kayıt sayısına ulaşıldı.',
      duplicate_skill: 'Bu beceri zaten ekli.',
      duplicate_language: 'Bu dil zaten ekli.',
      duplicate_application: 'Bu ilan için zaten bir başvurunuz var.',
      reference_conflict: 'Bağlı kayıt artık geçerli değil.',
      resource_in_use: 'Bu kayıt başka kayıtlar tarafından kullanıldığı için silinemez.',
      invalid_status_transition: 'Başvuru bu duruma geçirilemez.',
      parse_draft_state_invalid: 'Bu taslak artık bu işleme uygun değil.',
      parse_draft_outdated: 'Taslak güncel değil. Yeni bir ayrıştırma isteyin.',
      skill_provenance_locked: 'Bu beceri elle değiştirilemez.',
      job_posting_skill_limit_reached: 'İlan en fazla beceri sayısına ulaştı.',
      parse_cooldown: 'Aynı içerik kısa süre önce ayrıştırıldı. Biraz bekleyin.',
      refresh_token_superseded: 'Oturum başka bir sekmede yenilendi. Tekrar deneyin.',
    } satisfies Record<ApiErrorCode, string>,
  },
};

export type Messages = typeof tr;
