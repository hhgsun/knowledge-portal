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

- `Ollama:BaseUrl` ile yapılandırılan Ollama/uyumlu servisin erişilebilir olduğundan emin olun.
- appsettings.json'da Ollama:Enabled = true olmalı.
- `bge-m3` embedding modelinin erişilebilir olduğunu doğrulayın: `ollama pull bge-m3`
- RAG için `qwen2.5vl:7b` chat modelinin erişilebilir olduğunu doğrulayın: `ollama pull qwen2.5vl:7b`
- Yapılandırmadaki `EmbeddingDimensions` değerinin veritabanındaki `vector(1024)` kolonuyla uyumlu olduğunu kontrol edin.
- Model beklenmeyen embedding boyutu döndürürse health kontrolü Ollama'yı `unavailable` gösterir; semantic sorgular PostgreSQL'e ulaşmadan açıklayıcı bir boyut uyuşmazlığı hatasıyla durdurulur.

### Yeni eklenen makale aramada çıkmıyor

- Makalenin status'ünün 'published' olduğundan emin olun — sadece yayınlanmış makaleler indekslenir.
- İndeksleme PostgreSQL-backed `index_jobs` kuyruğunda asenkron çalışır. Varsayılan polling aralığı 2 saniyedir; yoğunluk veya retry/backoff nedeniyle daha uzun sürebilir. İndekssiz makale olduğu hâlde bekleyen/hatalı iş sayısı sıfırsa periyodik uzlaştırma eksik kuyruk satırını varsayılan olarak 60 saniye içinde yeniden oluşturur; oluşmuyorsa worker günlükleri ve PostgreSQL erişimi kontrol edilmelidir.
- GET /api/search/embedding-status ile indeksleme durumunu kontrol edebilirsiniz.
- GET /api/search/diagnostics ile model/boyut, kuyruk ve indeks uyarılarını inceleyebilirsiniz.
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

### PostgreSQL bağlantısı kurulamıyor

- `ConnectionStrings:DefaultConnection` değerini ve ağ erişimini doğrulayın.
- PostgreSQL servisinin çalıştığını ve hedef veritabanının mevcut olduğunu kontrol edin.
- Uygulama readiness kontrolü için `GET /api/health` çağrısını inceleyin; DB erişilemiyorsa 503 `unhealthy` döner.
- Migration'ları `cd backend && dotnet ef database update` ile uygulayın.

### "extension vector does not exist" veya vektör sorgusu hatası

- PostgreSQL sunucusunda `pgvector` eklentisinin kurulu olduğundan emin olun.
- Migration çalıştıran kullanıcının `CREATE EXTENSION vector` işlemi için gerekli yetkiye sahip olduğunu doğrulayın.
- `/api/search/diagnostics` ve `/api/search/storage-status` yönetici endpoint'lerindeki uyarıları kontrol edin.

## Destek

Yukarıdaki çözümler sorununuzu gidermediyse, lütfen hata mesajının tam metnini ve hangi endpoint'e istek yaptığınızı belirterek destek talep edin.
