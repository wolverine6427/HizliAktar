# Sürüm Geçmişi

Biçim: [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) · Sürümleme: [SemVer](https://semver.org/lang/tr/)

## [1.1.0] - 2026-11-15
### Eklendi
- Tablet uygulaması açıkken ekran kilitlenmez ve kararmaz.
- Eşleşme kodu tablette düzenlenebilir (4–9 rakam). Tüm cihazlara aynı kod yazılabilir.
- Bilgisayar, yeni bir cihazda daha önce girilmiş kodları otomatik dener. Aynı kodu kullanan cihazlarda kod tekrar sorulmaz.
- Boş alan kontrolü: Dosya sığmayacaksa aktarım başlamadan uyarı verilir (`S` yanıtı).
- Uzun aktarımlarda bilgisayarın uykuya geçmesi engellenir.
- Sürüm ve yazar bilgisi her iki uygulamada da gösterilir.

## [1.0.0] - 2026-11-15
### Eklendi
- İlk sürüm: Wi-Fi üzerinden otomatik cihaz bulma, sürükle-bırak gönderim, klasör desteği.
- PIN ile eşleşme. Kod bilgisayarda kalıcı olarak saklanır.
- 4 paralel TCP bağlantısı, 1 MB tamponlar, Wi-Fi yüksek performans kilidi.
- Arka planda çalışan alıcı servis. Dosyalar `İndirilenler/HizliAktar` klasörüne kaydedilir.
- *IP ile ekle* seçeneği (yayının engellendiği ağlar için).
