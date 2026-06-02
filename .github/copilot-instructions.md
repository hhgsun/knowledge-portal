# Copilot Instructions — Knowledge Portal

> Bu dosya VS Code Copilot agent'larına workspace açıldığında otomatik yüklenir.

## Primary Reference

**`AGENTS.md`** projenin tek doğruluk kaynağıdır (Single Source of Truth).
Her geliştirme öncesi bu dosyayı oku. Detaylı spec'ler `specs/` klasöründedir — çelişki durumunda `AGENTS.md` geçerlidir.

## Critical Rules

1. **Dokümantasyon senkronizasyonu zorunludur.** Her değişiklik sonrası `AGENTS.md` → "Documentation Sync Rules" bölümündeki Trigger → Action Matrix'i uygula.
2. Yeni dosya/endpoint/entity eklendiğinde ilgili tüm dokümantasyon güncellenir.
3. Bir özellik kaldırıldığında ilgili dokümantasyon satırları silinir — ölü referans bırakılmaz.
4. Backend DTO değişikliği → `frontend/src/types/api.ts` aynı anda güncellenir.
5. Feature Status tablosunda sadece tam çalışan özellikler ✅ olarak işaretlenir.

## Post-Change Validation (Zorunlu)

Her conversation'ın SONUNDA, yapılan değişiklikleri aşağıdaki checklist ile doğrula:

- [ ] Yeni/değişen endpoint → AGENTS.md Endpoint Matrix'te var mı?
- [ ] Yeni sayfa/component → specs/frontend-structure.md'de var mı?
- [ ] Feature tamamlandı → Feature Status ✅ olarak güncellendi mi?
- [ ] Known Gap kapatıldı → Satır silindi mi?
- [ ] DTO değişti → types/api.ts güncellendi mi?
- [ ] Validation kuralı değişti → Validation Rules tablosu güncellendi mi?

Uyumsuzluk varsa düzeltmeden conversation'ı kapatma.

## Do NOT

- Add Next.js, SSR, server components
- Add Redux, Zustand, MUI, Chakra
- Use ASP.NET Identity
- Use magic permission strings
- Skip documentation sync after changes
