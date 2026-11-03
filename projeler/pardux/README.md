# Pardux

Klavye kısayoluyla açılan, Türkçe komut anlayan masaüstü asistanı. Ekranın üstünde yaşayan siyah bir **ada** (dynamic island) olarak çalışır.
Kurulum gerektirmez: Windows'un içindeki .NET Framework ile çalışır. Windows 11'de buzlu cam (akrilik) görünüm kullanır.

## Kullanım

1. Masaüstündeki **Pardux** kısayolunu aç (uygulama `%LOCALAPPDATA%\Programs\Pardux` klasöründe kuruludur; derleme bunu otomatik günceller). Uygulama sağ alttaki simge tepsisine yerleşir, Windows ile başlarken adada kısa bir "Pardux hazır" bildirimi görünür.
2. **Alt+Space** ile ada yukarıdan aşağıya açılır (kısayol ayarlardan değiştirilebilir).
3. Ne istediğini yaz ve **Enter**'a bas.

| Tuş | Ne yapar |
|---|---|
| Enter | Seçili sonucu çalıştırır |
| ↑ ↓ / Tab | Sonuçlar arasında gezinir / öneriyi tamamlar |
| Ctrl+, | Ayarları açar |
| Esc | Geri döner / kapatır |

## Pardux Ada

Ekranın üst ortasındaki siyah kapsül; duruma göre şekil değiştirir:

| Durum | Ne görünür |
|---|---|
| Müzik çalarken | Kapak resmi, şarkı adı ve kapağın rengine bürünen ses dalgası. Tıklayınca medya kartı açılır (ilerleme çubuğu, önceki / oynat-duraklat / sonraki, sağda döngü: kapalı → tüm liste → bu şarkı); ekranda başka bir yere tıklayınca küçülür, ✕ ile müzik duraklayana kadar gizlenir. Üzerine gelince sağda beliren düğmeyle ada küçük bir **noktaya** dönüşür; noktaya tıklayınca geri açılır. Spotify, YouTube, tarayıcı — Windows'a medya bildiren her uygulamada çalışır. |
| Hatırlatıcı kuruluyken | Son 60 dakikaya girince geri sayım (müzikle yan yana da görünür) |
| Bildirim | Hatırlatma (Tamam / 5 dk ertele), şarj takıldı/çıkarıldı, not kaydedildi… |
| Alt+Space | Ada yaylı bir animasyonla büyüyüp arama paneline dönüşür |
| Boşta | İsteğe bağlı: göz kırpan küçük Pardux hapı |

Oyun ve tam ekran uygulamalarda ada otomatik gizlenir (ayarlardan kapatılabilir).

## Gündem haberleri

Türkiye gündeminde öne çıkan önemli haberler (Google Haberler) sağ üstte macOS tarzı bir bildirimle gelir; tıklayınca görsel, özet, haber metni ve diğer kaynakları içeren ayrıntı penceresi açılır. Bildirimler seyrek tutulur: en az 45 dakika arayla, günde en fazla 6, gece 00:00–08:00 arası hiç gelmez, aynı olay iki kez bildirilmez. Arama kutusuna "haberler" yazınca güncel başlıklar listelenir. Ayarlar → Ada ve bildirimler bölümünden kapatılabilir.


## Komutlar

| Ne yazılır | Ne olur |
|---|---|
| `discord aç`, `chrome`, `hesap makinesi` | Uygulamayı açar |
| `müzik aç`, `şarkı çal`, `çal` | Spotify açılır, kaldığın şarkı devam eder |
| `tarkan şımarık çal` | Spotify'da arar ve çalar |
| `durdur`, `sonraki`, `önceki`, `ses 40`, `sessiz` | Müzik ve ses kontrolü |
| `hava`, `ankara hava durumu` | Anlık hava ve 3 günlük tahmin (Open-Meteo) |
| `dolar`, `100 euro kaç tl` | Güncel döviz kuru (open.er-api.com) |
| `pano` | Pano geçmişi; seçtiğin metin önceki pencereye yapıştırılır |
| `10 dk sonra çayı hatırlat`, `saat 18:30'da ilacı hatırlat` | Hatırlatıcı |
| `hatırlatıcılar` | Kurulu hatırlatıcıları listeler, Enter ile iptal eder |
| `not al süt yumurta`, `notlarım` | Hızlı notlar |
| `bul rapor` | Bilgisayarındaki dosyalarda arar |
| `pil` | Pil ve disk durumu |
| `mail yaz`, `annem'e mail at` | Mail paneli (Ctrl+Enter ile Gmail/Outlook taslağı açılır) |
| `ahmet'e whatsapp'tan akşam geliyorum yaz`, `wp annem: ekmek al`, `whatsapp 0555 111 22 33 selam` | WhatsApp sohbetini mesaj yazılı açar; ayar açıksa otomatik gönderir. Numarası kayıtlı değilse kişiyi WhatsApp'ta seçersin |
| `ayşe'ye instagramdan selam yaz` | Kişiler'de kayıtlı Instagram sohbet linkini açar, mesajı yazar ve gönderir |
| `5 dakikalık zamanlayıcı`, `10 dk geri sayım` | Adada geri sayan zamanlayıcı, bitince alarm |
| `kronometre` | Adada ileri sayan kronometre (tıklayınca duraklat / sıfırla) |
| `(1250*12)/7` | Hesap makinesi (Enter sonucu kopyalar) |
| `youtube lofi`, `merhaba çevir`, `kadıköy harita`, `github.com` | Web |
| `karanlık mod`, `açık tema` | Temayı değiştirir |
| `kilitle`, `uyku`, `kapat`, `chrome kapat` | Sistem ve uygulama kapatma (riskli olanlar onay ister) |

## Sesli komut: "Hey Pardux"

"Hey Pardux" de ("parduks" diye okunur); Pardux "Dinliyorum efendim" der, isteğini söyle ("müzik aç", "15 dakika sonra çayı hatırlat", "ankara hava durumu", "kronometre başlat"). Tek nefeste "Hey Pardux, müzik aç" demek de olur. Pardux yaptığı işi sesle söyler ("Kronometreyi başlatıyorum"); hava ya da internetten gelen cevapları okur. Tanıma tamamen çevrimdışıdır: "Hey Pardux" hafif Vosk tanıyıcısıyla yakalanır, komut Whisper (whisper.cpp, Türkçede çok daha isabetli large-v3-turbo modeli, `dist\whisper`) ile yazıya dökülür; ses internete gönderilmez. Model "Hey Pardux" duyulunca arka planda yüklenir ve birkaç dakika açık kalır (komut ~3 sn'de yazılır). "Hey Pardux"tan önce söylenenler komuta karışmaz, cümle içindeki kısa duraklamalarda dinleme kesilmez, kısık mikrofonlarda ses yükseltilir, kişi adları Whisper'a ipucu olarak verilir. Sesli yanıt çevrimdışı yapay zekâ sesiyle (Piper, Türkçe `dfki` modeli, `dist\piper`) yapılır; sayılar, saatler, yüzdeler ve birimler yazıyla okunur ("8.848,86 m" → "sekiz bin sekiz yüz kırk sekiz virgül seksen altı metre"). Ayarlar → Ses'ten Windows'un Tolga sesine geçilebilir ya da sesli yanıt kapatılabilir.

## Yeni komutlar

| Ne yazılır / söylenir | Ne olur |
|---|---|
| (boş arama, Alt+Space) | Uygulama listesi yerine "bugün" paneli: takvim, pil, hava, gündem (hatırlatıcılar, sayaçlar, müzik, kaçırılan bildirimler) |
| `rocket league oyna`, `subliminal aç` | Yüklü oyunu açar (Steam kütüphanesi ve masaüstü kısayolları) |
| `steam'den counter strike indir` | Steam mağazasında bulur, kurulum penceresini açar |
| `kuzey'e discord'dan akşam geliyorum yaz`, `discord ahmet: naber` | Discord'da kişiyi bulup (Ctrl+K) mesajı yazar; ayara göre gönderir |
| `geminiye sor yapay zeka nedir`, `… diye geminiye sor` | Gemini'de yeni sohbet açar ve soruyu gönderir |
| `wifi'yi kapat`, `bluetooth aç`, `uçak modunu aç`, `parlaklık 40`, `ekranı karart`, `ekranı kapat` | Basit sistem ayarları |
| `bildirimler`, `kaçırdığım bildirimler` | Bildirim merkezinde açılmadan bekleyen bildirimler; Pardux bunları 10 dk sonra kendisi de hatırlatır |
| `rapor dosyasını aç`, `ödev pdf'ini aç`, `projeler klasörünü aç` | Dosyayı bulur (eksiz/aksansız yazım da olur) ve açar |

Aynı iş için çok sayıda söyleyiş anlaşılır: "açar mısın", "açabilir misin", "açsana", "lütfen … başlatır mısın", "müziği kes", "başka şarkı", "sesi biraz kıs"…

## Diğer

- **Birden çok canlı etkinlik:** ana kapsülde gösterilmeyen zamanlayıcılar ve kronometre yanında ayrı yuvarlak baloncuklarda durur (en çok iki; fazlası "+N" baloncuğunda). Fare üzerine gelince baloncuklar açılıp süreyi gösterir, tıklanan etkinliğin kartı açılır; kartın başlığına tıklayınca sıradaki sayaca geçilir.
- **Sorular:** "everest kaç metre", "nikola tesla kimdir", "google'da istanbulun nüfusu kaç ara" gibi sorular internette araştırılır: "kaç yaşında", "nüfusu", "ne zaman kuruldu", "yüksekliği", "başkenti" gibi kesin bilgiler Wikidata'dan doğrudan okunur; diğer sorularda Bing'in en iyi Türkçe sonuç sayfaları ve Türkçe Vikipedi açılır, soruyu en iyi cevaplayan Türkçe cümle seçilip Pardux'un içinde gösterilir (sesle sorulduysa okunur). Google bağlantısı altta kalır. YouTube, Instagram gibi sitelerde tarayıcı açılır.
- **Buzlu cam:** arka plan Gauss benzeri pürüzsüz bulanıklıkla işlenir, saniyelik tazelemede yeni görüntü yumuşakça geçer; baloncuklar da camdan pay alır.
- **Ada stili:** yüzen ya da ekranın üst kenarına yapışık (çentik gibi). Ada her zaman diğer "üstte" pencerelerin de önünde durur.
- **Gözler:** boştaki adanın göz rengi, boyutu ve şekli Ayarlar → Ada'dan değiştirilir.

## Ayarlar

Ayrı bir pencere yerine adanın içinde açılır (⚙ düğmesi, `Ctrl+,`, tepsi menüsü). Her değişiklik anında uygulanır ve kaydedilir; kaydet düğmesi yoktur. Esc arama paneline döner.

- **Genel:** kısayol, mail sağlayıcısı, Windows ile başlama, pano geçmişi, Spotify otomatik çalma, WhatsApp otomatik gönderme
- **Görünüm:** tema, vurgu rengi, buzlu cam (adanın kendisi ve arama/ayar paneli için ayrı ayrı), haber penceresi arka planı (bulanık / mika / düz), animasyonlar
- **Ada:** boyut (küçük / orta / büyük), müzik, Spotify/tarayıcı öndeyken gizle, boşta hap, tam ekranda gizle, şarj bildirimi
- **Ses:** "Hey Pardux", Pardux sesleri ve ses düzeyi. Uyanma sesi `assets\sounds\uyanis.mp3` (dosyayı değiştirip derleyince ses de değişir); bildirim, haber, alarm ve onay sesleri Pardux'un kendi yumuşak çan tınısıyla üretilir
- **Bildirimler:** gündem haberleri, "İyi misin?" (uzun süre hareketsiz kalınca sorar; 30 dk / 1 saat / 2 saat)
- **Kişiler:** ad, mail, telefon (WhatsApp) ve Instagram sohbet linki. Kişiler ayar dosyasından ayrı olarak da (`kisiler.json`, Roaming ve Local klasörlerinde birer kopya, `kisi-yedekleri` altında 30 günlük yedek) saklanır: ayar dosyası bozulsa ya da bilgisayar açılırken okunamasa bile kişiler geri gelir
- **Komutlar:** bir ad yazınca açılacak klasör, dosya, program veya site
- **Hakkında:** masaüstü kısayolu (her zaman güncel simgeyle), uygulama listesini yenileme, geçmişleri temizleme

Ayarlar `%APPDATA%\Pardux\ayarlar.json` dosyasında saklanır; her kayıtta bir önceki sağlam sürüm `ayarlar.yedek.json` olarak tutulur, dosya bozulursa kişiler ve komutlar yedekten geri gelir. Pano geçmişi yalnızca bellekte tutulur.

## Derleme

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Çıktı: `dist\Pardux.exe`. Simgeyi yeniden çizmek için `assets\pardux.ico` dosyasını silip derlemeyi tekrar çalıştır.
