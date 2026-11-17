# Sürüm Geçmişi

Biçim: [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) · Sürümleme: [SemVer](https://semver.org/lang/tr/)

## [1.3.0] - 2026-11-18
### Eklendi
- **⚡ Doğrudan bağlantı (Wi-Fi Direct):** Modem gerekmeden cihazdan cihaza bağlantı. Huawei Share / Quick Share ile aynı yöntemdir. Mümkünse 5 GHz bandında açılır ve aynı modemden geçmeye göre yaklaşık 2 kat hızlıdır.
  - Android ⇄ Android: Bir cihaz *Bağlantıyı aç*, diğeri *Bağlantıya katıl*. Ağ adı ve şifre eşleşme kodundan türetildiği için onay gerekmez.
  - Bilgisayar ve diğer cihazlar ekranda gösterilen ağa normal Wi-Fi gibi bağlanır.
- README'ye "Hangi durumda hangi yol en hızlı?" tablosu eklendi. Bilgisayar rehberine doğrudan bağlantı notu eklendi.

## [1.2.0] - 2026-11-15
### Eklendi
- **Çift yönlü aktarım:** Tabletten/telefondan bilgisayara (ve cihazdan cihaza) gönderim. Bilgisayar artık alıcı olarak da çalışır ve gelen dosyaları `İndirilenler\HizliAktar` klasörüne kaydeder.
- Tablette **Dosya seç ve gönder** düğmesi ve diğer uygulamalarda **Paylaş → Hızlı Aktar** desteği.
- **Kalıcı cihaz hafızası (iki tarafta):** Bağlanan cihazlar hatırlanır; kapalıyken "çevrimdışı" olarak listede kalır.
- **Takma ad:** Bilgisayarda sağ tık menüsünden, tablette basılı tutarak verilir. Cihazın kodu ve cihazın kendisi de unutulabilir.
- Bilgisayarın da düzenlenebilir bir eşleşme kodu vardır. Tüm cihazlara aynı kod yazılırsa hiç kod sorulmaz.
- **Logo ve simgeler:** Uygulama simgeleri, pencere ve ekran arka planında logo filigranı.
- **Uygulama içi rehberler:** Bilgisayarda APK indirme QR kodu ve adımlar; tablette bilgisayar programını bulma rehberi (GitHub bağlantısı ve kopyalama düğmesi).
- Tablette **IP ile ekle** (`IP` veya `IP:port` kabul eder).
- README baştan yazıldı: ekran görüntüleri, adım adım kurulum, sorun giderme tablosu.

### Değişti
- Protokol `HA2`: Gönderen kimliği eklendi. Eski `HA1` biçimi okunmaya devam eder.
- 1.1 sürümünde kaydedilen PIN'ler yeni cihaz listesine otomatik taşınır.

### Düzeltildi
- Kayıtlı adreste başka bir cihaz (veya cihazın kendisi) yanıt verirse yanlış cihaza gönderim yapılmaz.
- Yüksek DPI ekranlarda bilgisayar penceresinin alt düğmeleri ekran dışında kalıyordu.

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
