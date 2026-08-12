---
{
  "title": "Azure AD ile Kurumsal Giriş",
  "contentType": "how-to",
  "tags": [
    "project-knowledge-portal",
    "security",
    "deployment"
  ],
  "excerpt": "Azure Active Directory ile SSO entegrasyonu, MSAL yapılandırması ve kullanıcı eşleme mekanizması.",
  "status": "published"
}
---

## Genel Bakış

Knowledge Portal, Microsoft Azure Active Directory (Azure AD / Entra ID) ile tek oturum açma (SSO) desteği sunar. Kullanıcılar kurumsal Microsoft hesaplarıyla doğrudan giriş yapabilir.

## Giriş Akışı

Azure AD girişi MSAL.js v5 redirect-bridge popup akışı ile çalışır:

1. Kullanıcı 'Microsoft ile Giriş Yap' butonuna tıklar.
2. Popup penceresi açılır ve Azure AD kimlik doğrulama sayfasına yönlendirir.
3. Kullanıcı Microsoft hesabıyla giriş yapar.
4. Popup, BroadcastChannel üzerinden ana pencereye auth kodunu iletir.
5. PKCE exchange ile access token alınır.
6. Access token POST /api/auth/azure-login endpoint'ine gönderilir.
7. Backend Microsoft Graph /me ile token'ı doğrular.
8. Yerel kullanıcı bulunur veya oluşturulur, JWT token döner.

## Kullanıcı Eşleme

Azure AD ile giriş yapıldığında kullanıcı eşleme şu şekilde çalışır:

- **İlk giriş (yeni kullanıcı):** Azure'dan gelen e-posta ile mevcut kullanıcı aranır. Bulunursa AzureObjectId ile bağlanır. Bulunamazsa viewer rolüyle yeni kullanıcı oluşturulur.
- **Sonraki girişler:** AzureObjectId ile doğrudan eşleştirilir. Profil adı Azure'dan güncellenir.

## Sessiz Giriş (Auto-Login)

Kullanıcının aktif Azure oturumu varsa, login sayfası otomatik olarak sessiz giriş dener. Başarılı olursa kullanıcı giriş formunu bile görmeden sisteme yönlendirilir.

## Çıkış

Çıkış yapıldığında msalInstance.clearCache() çağrılarak MSAL cache'i temizlenir. Bu, bir sonraki girişte otomatik sessiz login'in tekrar yapılmamasını sağlar.

## Şifre Belirleme

Azure AD ile oluşturulan kullanıcıların başlangıçta yerel şifresi yoktur. Profil sayfasından (PUT /api/auth/profile) ilk kez şifre belirlenebilir — bu durumda mevcut şifre (currentPassword) gerekmez. Şifre belirlendikten sonra hem Azure AD hem de e-posta + şifre ile giriş yapılabilir.

## Yapılandırma

Azure AD entegrasyonu için gerekli yapılandırma:

### Azure Portal

1. Azure Portal'da bir App Registration oluşturun.
2. Redirect URI olarak http://localhost:5173/auth-popup-callback.html ekleyin (SPA tipi).
3. Application (client) ID ve Directory (tenant) ID'yi not edin.

### Frontend Ayarları

Frontend environment değişkenlerinde MSAL yapılandırmasını belirtin:

- `VITE_AZURE_CLIENT_ID` — Application (client) ID
- `VITE_AZURE_TENANT_ID` — Directory (tenant) ID

## Güvenlik Notları

- Access token doğrulaması Microsoft Graph API üzerinden yapılır (backend).
- PKCE (Proof Key for Code Exchange) kullanılarak token güvenliği sağlanır.
- Popup callback sayfası (auth-popup-callback.html) Vite multi-page entry olarak build edilir.
