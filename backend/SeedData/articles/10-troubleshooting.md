---
{
  "title": "Sorun Giderme (Troubleshooting)",
  "contentType": "faq",
  "tags": [
    "project-knowledge-portal",
    "troubleshooting"
  ],
  "excerpt": "Sık karşılaşılan sorunlar, hata mesajları ve çözüm yolları.",
  "status": "published"
}
---

## Giriş Sorunları

### "Invalid credentials" hatası

- E-posta adresinin doğru yazıldığından emin olun.
- Şifre en az 8 karakter olmalıdır.
- Azure AD ile oluşturulan hesaplarda, henüz yerel şifre belirlenmemişse e-posta/şifre ile giriş yapılamaz. Azure AD ile giriş yapın.

### Azure AD popup açılmıyor

- Tarayıcı popup engelleyicisinin devre dışı olduğundan emin olun.
- Azure Portal'da redirect URI'nin doğru yapılandırıldığını kontrol edin.
- MSAL environment değişkenlerinin (VITE_AZURE_CLIENT_ID, VITE_AZURE_TENANT_ID) doğru olduğunu doğrulayın.

### HTTP 429 (Too Many Requests)

Rate limit aşıldığında döner. Auth endpoint'leri için dakikada 10, search için dakikada 30 istek sınırı vardır. Bir dakika bekleyip tekrar deneyin.

## Arama Sorunları

### Semantic/Hybrid/RAG arama çalışmıyor

- Ollama'nın çalıştığından emin olun (varsayılan: http://localhost:11434).
- appsettings.json'da Ollama:Enabled = true olmalı.
- nomic-embed-text modelinin indirildiğini doğrulayın: ollama pull nomic-embed-text
- RAG için ek olarak llama3.2 modeli gerekir: ollama pull llama3.2

### Yeni eklenen makale aramada çıkmıyor

- Makalenin status'ünün 'published' olduğundan emin olun — sadece yayınlanmış makaleler indekslenir.
- İndeksleme arka planda her 5 saniyede çalışır. Birkaç saniye bekleyin.
- GET /api/search/embedding-status ile indeksleme durumunu kontrol edebilirsiniz.
- Sorun devam ederse POST /api/search/reindex ile indeksi yeniden oluşturun.

## Dosya Ekleri Sorunları

### Dosya yüklenemiyor

- Dosya boyutunun 20 MB'ı aşmadığını kontrol edin.
- Dosya uzantısının izin verilenler listesinde olduğunu doğrulayın (.png, .jpg, .pdf, .docx, .md, .txt, vb.).
- Makale başına en fazla 20 dosya sınırına ulaşılmamış olmalı.
- Uploads dizininin (data/uploads/) yazma izinlerine sahip olduğunu kontrol edin.

### Editöre yapıştırılan görsel kayboldu

Editöre yapıştırılan görseller geçici blob URL'ler kullanır. Makaleyi kaydetmeden sayfadan çıkarsanız bu görseller kaybolur. Makaleyi kaydettikten sonra görseller kalıcı URL'lere dönüşür.

## Yetkilendirme Sorunları

### HTTP 403 (Forbidden)

- İşlem için gerekli yetkiye sahip olduğunuzdan emin olun (RBAC ve Yetkilendirme makalesine bakın).
- API key ile session-only endpoint'lere erişmeye çalışıyor olabilirsiniz — JWT token kullanın.
- Başkasının makalesini düzenlemeye çalışıyorsanız articles:edit_any (sadece admin) yetkisi gerekir.

### HTTP 401 (Unauthorized)

- JWT token'ın süresi dolmuş olabilir (24 saat). Tekrar login yapın.
- Authorization header'ının 'Bearer {token}' formatında olduğunu kontrol edin.
- API key kullanıyorsanız X-API-Key header'ında 'kp_' prefix'i ile gönderildiğinden emin olun.

## Veritabanı Sorunları

### "database is locked" hatası

SQLite WAL modu ve busy timeout (5000ms) otomatik olarak etkindir. Bu hata genellikle çok sayıda eşzamanlı yazma işlemi olduğunda ortaya çıkar. Birkaç saniye bekleyip tekrar deneyin. Production ortamında yüksek eşzamanlılık gerekiyorsa PostgreSQL'e geçiş düşünülebilir.

## Destek

Yukarıdaki çözümler sorununuzu gidermediyse, lütfen hata mesajının tam metnini ve hangi endpoint'e istek yaptığınızı belirterek destek talep edin.
