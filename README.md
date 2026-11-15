# Hızlı Aktar

**Aynı Wi-Fi ağındaki bilgisayardan tablet ve telefonlara sürükle-bırak ile hızlı, sınırsız dosya aktarımı.**

Yazar: **Muhammed Hüseyin KILIÇ** · Sürüm: **1.1.0** · Lisans: [MIT](LICENSE)

Bilgisayardaki pencereye dosya veya klasör bırakırsınız, dosyalar birkaç saniye içinde tablete ya da telefona gelir. İnternet, bulut, hesap ya da kablo gerekmez. Dosyalar yalnızca ev veya okul ağınızın içinde, iki cihaz arasında doğrudan gider.

Huawei MatePad Pro için yazıldı. Android uyumlu her tablet ve telefonda çalışır (Android 7.0 ve üzeri, HarmonyOS 2/3/4 dahil).

---

## İndir

[**Releases**](../../releases/latest) sayfasından iki dosyayı indirin:

| Dosya | Cihaz | Boyut |
|---|---|---|
| `HizliAktar.exe` | Windows bilgisayar (Windows 7, 8, 10, 11; kurulum gerektirmez) | ~26 KB |
| `HizliAktar.apk` | Android tablet veya telefon | ~20 KB |

## Özellikler

- **Sürükle-bırak:** Dosya veya klasörü pencereye bırakın, hemen gönderilir. Klasör yapısı korunur.
- **Otomatik bulma:** Aynı Wi-Fi'deki tablet ve telefonlar, cihaz adlarıyla birlikte listede kendiliğinden görünür.
- **Birden çok cihaz:** Listeden birkaç cihaz seçip aynı dosyaları hepsine gönderebilirsiniz. Birden çok bilgisayar aynı tablete aynı anda gönderebilir.
- **Tek seferlik eşleşme:** Kod yalnızca ilk gönderimde sorulur ve bilgisayarda kalıcı olarak saklanır. Tüm cihazlarınıza aynı kodu yazarsanız yeni bir cihaz eklendiğinde de kod sorulmaz.
- **Boyut sınırı yok:** 1 KB da olsa 100 GB da olsa gönderebilirsiniz. Tek koşul, alıcı cihazda yeterli boş alan olması; alan yoksa aktarım başlamadan uyarı verilir.
- **Hız sınırı yok:** Uygulama hızı kısmaz, ağınız ne kadar hızlıysa o hızla gönderir (5 GHz Wi-Fi'de genellikle 30–80 MB/sn).
- **Ekran kararmaz:** Tablet uygulaması açıkken ekran kilitlenmez. Uygulama arka planda da alım yapmaya devam eder.
- **Uyku engeli:** Uzun aktarımlar sürerken bilgisayar uykuya geçmez.
- **Güvenli kayıt:** Eşleşme kodunu bilmeyen biri dosya gönderemez. Gönderilen dosyalar yalnızca `İndirilenler/HizliAktar` klasörüne yazılabilir.
- **Küçük ve bağımlılıksız:** Ek kütüphane, kurulum, hesap ya da internet bağlantısı gerektirmez.

## Kurulum ve kullanım

### 1. Tablet veya telefon
1. `HizliAktar.apk` dosyasını cihaza atın (USB kablo, e-posta vb. ile, yalnızca bir kez gerekir) ve dokunarak kurun. Sistem sorarsa *bilinmeyen kaynaklardan yüklemeye* izin verin.
2. **Hızlı Aktar** uygulamasını açın. Ekranda şunlar görünür:
   - **Cihaz adı:** Bilgisayardaki listede bu ad görünür. İsterseniz değiştirip *Kaydet*'e basın.
   - **Eşleşme kodu:** 4–9 rakamlı bir kod. Kendi kodunuzu yazıp *Kodu kaydet*'e basabilirsiniz. Tüm cihazlara aynı kodu yazmanız önerilir.

### 2. Bilgisayar
1. `HizliAktar.exe`'yi çalıştırın.
2. Windows Güvenlik Duvarı uyarı verirse **Özel ağlar** için izin verin. Bu izin olmadan cihazlar listede görünmez.
3. Birkaç saniye içinde tablet soldaki listede görünür. Dosyaları sağdaki mavi alana sürükleyin veya alana tıklayıp dosya seçin.
4. İlk gönderimde eşleşme kodu sorulur. Kod bir kez girildikten sonra bilgisayar onu unutmaz.

Gelen dosyalar tablette **Dosyalar → İndirilenler → HizliAktar** klasörüne kaydedilir.

### Cihaz listede görünmüyorsa
- İki cihazın **aynı Wi-Fi ağında** olduğundan emin olun. Misafir ağlar cihazların birbirini görmesini çoğu zaman engeller.
- Güvenlik duvarında Hızlı Aktar'a izin verildiğini kontrol edin.
- Yine de görünmüyorsa **IP ile ekle** düğmesine basın ve tablet ekranında yazan IP adresini girin.

### Huawei / Honor cihazlarda
- Sistemin uygulamayı arka planda kapatmaması için: *Ayarlar → Pil → Uygulama başlatma → Hızlı Aktar → Manuel yönet* yolunu izleyin ve üç seçeneği de açın.
- **HarmonyOS NEXT / 5.x** Android uygulamalarını (APK) çalıştırmaz. Bu sürümlerde uygulama kurulamaz.

---

## Nasıl çalışır? (Teknik ayrıntılar)

```
 Bilgisayar (HizliAktar.exe)                    Tablet / Telefon (HizliAktar.apk)
 ┌─────────────────────────┐   UDP 47801     ┌──────────────────────────────┐
 │ Keşif dinleyicisi       │ ◄────────────── │ Her 1,5 sn yayın:            │
 │ (cihaz listesi)         │   (yayın)       │ "HA1|id|47800|cihaz adı"     │
 │                         │                 │                              │
 │ Gönderici               │   TCP 47800     │ Arka plan alıcı servisi      │
 │ 4 paralel bağlantı,     │ ──────────────► │ → İndirilenler/HizliAktar/   │
 │ 1 MB tampon             │  dosya başına   │   (MediaStore)               │
 └─────────────────────────┘  bir bağlantı   └──────────────────────────────┘
```

### Protokol (sürüm `HA1`)

**Keşif:** Tablet her 1,5 saniyede bir UDP 47801 portuna, hem `255.255.255.255` adresine hem de alt ağ yayın adresine şu UTF-8 metni gönderir:

```
HA1|<cihaz-id (12 hex)>|<tcp-port>|<cihaz adı>
```

Bilgisayar, 6 saniye boyunca haber alamadığı cihazı listeden çıkarır.

**Aktarım:** Her dosya için ayrı bir TCP bağlantısı açılır. Sayılar big-endian'dır.

| Alan | Boyut | Açıklama |
|---|---|---|
| sihirli | 3 bayt | `HA1` (ASCII) |
| kod | int32 | Eşleşme kodu |
| yol uzunluğu | uint16 | Ardından gelen UTF-8 yolun bayt sayısı |
| yol | n bayt | Göreli yol, `/` ile ayrılmış (ör. `Ödev/şiir.txt`) |
| boyut | int64 | Dosya boyutu (bayt). `-1` ise *ping* |

Alıcının tek baytlık yanıtı:

| Yanıt | Anlamı |
|---|---|
| `K` | Kabul edildi. Gönderici ardından tam `boyut` bayt veri gönderir. |
| `P` | Eşleşme kodu yanlış. |
| `S` | Cihazda yeterli boş alan yok. |
| `D` | (Veri bittikten sonra) Dosya diske tamamen yazıldı. |

*Ping* (`boyut = -1`) yanıtı: durum baytı (`K` veya `P`), ardından cihaz id'si ve cihaz adı (her biri uint16 uzunluk + UTF-8). Bilgisayar bunu eşleşme kodunu doğrulamak ve *IP ile ekle* için kullanır.

### Hız için yapılanlar
- Araya HTTP gibi bir katman konmadı. Veri doğrudan ham TCP üzerinden akar ve dosya başına yalnızca ~20 baytlık bir başlık gönderilir.
- 1 MB okuma/yazma tamponları ve 4 MB soket tamponları kullanılır.
- 4 paralel bağlantı sayesinde çok sayıda küçük dosya da hızlı gider.
- Aktarım sırasında tabletin Wi-Fi güç tasarrufu kapatılır (`WIFI_MODE_FULL_HIGH_PERF`). Bu ayar Wi-Fi hızını en çok etkileyen etkendir.
- Bilgisayar tarafında, işletim sisteminin dosyayı sırayla okuduğunu bilmesi için `FileOptions.SequentialScan` kullanılır.

### Güvenlik
- Eşleşme kodu olmadan dosya kabul edilmez.
- Gelen yollardaki `..`, sürücü harfi ve geçersiz karakterler temizlenir. Hiçbir dosya `İndirilenler/HizliAktar` klasörünün dışına yazılamaz.
- Yarıda kalan aktarımlarda yarım dosya silinir.
- ⚠️ Trafik **şifrelenmez.** Uygulamayı yalnızca güvendiğiniz ağlarda (ev, okul) kullanın.

---

## Kaynak koddan derleme

Gereksinimler: Windows, JDK 17 veya üzeri ve Android SDK (`build-tools;35.0.0` ile `platforms;android-35`). Gradle veya Android Studio projesi gerekmez.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Çıktılar `cikti/` klasörüne yazılır:
- **PC:** Windows'la birlikte gelen .NET Framework derleyicisi (`csc.exe`) kullanılır.
- **APK:** `aapt2 → javac → d8 → zipalign → apksigner` adımlarıyla üretilir.

İmza anahtarı (`android/hizliaktar.keystore`) ilk derlemede otomatik oluşturulur ve depoya eklenmez. Yeni bir sürümün eskisinin üzerine kurulabilmesi için hep aynı anahtarla imzalanması gerekir.

### Proje yapısı

```
HizliAktar/
├── pc/
│   ├── Proto.cs          Protokol, keşif, kod saklama, paralel gönderim
│   └── MainForm.cs       Pencere, sürükle-bırak, ilerleme, sürüm bilgisi
├── android/
│   ├── AndroidManifest.xml
│   └── src/com/hizliaktar/app/
│       ├── MainActivity.java     Ekran (ad, kod, durum, alınan dosyalar)
│       ├── ReceiverService.java  Arka plan alıcı: UDP yayın + TCP sunucu
│       ├── Prefs.java            Cihaz id'si, adı, eşleşme kodu
│       └── BuildInfo.java        Sürüm ve yazar
├── build.ps1             Tek komutla derleme
├── CHANGELOG.md
└── LICENSE
```

Bilgisayar, eşleşme kodlarını `%APPDATA%\HizliAktar\pins.txt` dosyasında saklar.

## Sürüm geçmişi

Ayrıntılar için [CHANGELOG.md](CHANGELOG.md) dosyasına bakın.

## Lisans

MIT © 2026 Muhammed Hüseyin KILIÇ. Ayrıntılar için [LICENSE](LICENSE) dosyasına bakın.
