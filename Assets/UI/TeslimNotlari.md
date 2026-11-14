# Yarış dışı arayüz teslimi

**Güncel garaj ve galeri tasarımı:** [Atölye tasarımı ve doğrulamalar](AtolyeTasarimi.md). Aşağıdaki ekran görüntüleri ilk teslimi belgeler.

13 Kasım 2026. Dal: `ui-menus`. Lobby sahnesi `Harvester/Rebuild Lobby UI` ile yeniden üretildi ve kaydedildi. Ana menü, bağlantı, oda, garaj, mağaza ve ayarlar aynı sahnede panel olarak çalışır.

## Kullanım

Ana menüden Oyna → Çevrimiçi / LAN → Oda akışı kullanılır. Çevrimiçi oda Relay davet kodunu gösterir ve kopyalar; LAN mevcut IP ve şifre akışını kullanır. Oda kurucusu oyunu başlatır. Bağlantı açıkken garaj ve mağaza açılmaz. Odada kayıtlı aracın adı ve ayarlara erişim bulunur. Araç seçimini değiştirmek için odadan ayrılmak gerekir; oda içi araç seçimi eklenmedi.

Garajda sahip olunan araçlar, parça yuvası seçimi, boya yuvası seçimi, hazır palet, RRGGBB/RRGGBBAA renk girişi, geliştirme kategorileri ve önce/sonra stat çubukları vardır. Araç CarVisual üzerinden döner ve yakınlaştırılabilir. Parça/geliştirme düğmesi satın alır ve taslağa uygular; sabit Kaydet düğmesi CarLoadoutStore'a yazar. Ana menüye dönmek kaydedilmemiş taslağı bırakır. Araç satın alma mağazada, parça ve performans satın alma garajdadır.

HurdaEkonomisi başlangıçta 12.000 Hurda ve RacerCoupe_01 sahipliği verir; stok parçalar ücretsiz ve sahip olunan kabul edilir. Satın almalar kimliklerle ayrı PlayerPrefs JSON kaydında tutulur. `HurdaEkonomisi.ParaEkle(miktar, sebep)` gelecekte yarış ödüllerine bağlanabilir. Sunucu doğrulaması henüz yoktur; bu, istenen yerel ekonomi uygulamasıdır.

Ses, çözünürlük, tam ekran, kalite ve render ölçeği tercihleri kaydedilir. Ana ses AudioListener'a uygulanır. Müzik/efekt kaynakları için MenuAudioChannel bileşeni bulunur; ses kaynağının başlangıç sesini ilgili tercihle çarpar. Projede mevcut AudioSource/müzik içeriği bulunmadığından ses içeriği eklenmedi. Dikiz aynası mevcut RearViewMirror API'sini, kamera varsayılanı `Harvester.CarCameraMode` anahtarını kullanır. Kontroller mevcut eski Input Manager tuşlarını gösterir.

## Kurucu ve test menüleri

- `Harvester/Rebuild Lobby UI`: bütün Lobby arayüzünü üretir. Lobby sahnesi açık olmalıdır. Yeniden üretimden sonra sahneyi kaydet.
- `Harvester/Arayüz/Kayıt ve Ekonomi Testi`: başlangıç, satın alma, tekrar satın alma, yetersiz bakiye, negatif fiyat, ödül, kayıt yükleme, tamsayı taşması ve CarLoadoutStore kayıt döngüsünü sınar. Test sonunda oyuncunun kayıtlarını geri yükler.

MenuCarRegistryBuilder, Assets/Cars altındaki tanım asset'leri içeri alındığında/değiştiğinde/silindiğinde yalnızca Assets/UI/MenuCarRegistry.asset envanterini günceller. CarCatalog varsa onun sırası kullanılır; ağ indeksleri değiştirilmez. Yeni araç için yalnızca FBX yeterli değildir, bir CarDefinition tanımı gerekir.

## Dosyalar

| Dosya | Görev |
|---|---|
| Assets/scripts/lobby.cs | Mevcut ağ mantığına ana menü/oda panel bildirimleri ve Türkçe oda metinleri |
| Assets/scripts/Editor/LobbyUIBuilder.cs | Mevcut bağlantı/oda arayüzünü üretme ve yeni kurucuya geçiş |
| Assets/scripts/Editor/LobbyMenuBuilder.cs | Ana menü, garaj, mağaza, ayarlar, ortak çerçeve ve font üretimi |
| Assets/scripts/Editor/MenuCarRegistryBuilder.cs | Araç tanımlarını izleyen UI envanteri |
| Assets/scripts/Editor/LobbyUIValidation.cs | Kayıt/ekonomi test menüsü |
| Assets/scripts/UI/LobbyMenus.cs | Panel geçişleri, garaj, mağaza, kayıt bağlantısı |
| Assets/scripts/UI/MenuWidgets.cs | Ortak UGUI/TMP denetimleri, kaydırma ve tema |
| Assets/scripts/UI/GaragePreview.cs | İzole katman, önizleme kamerası, RenderTexture, ışıklar ve CarVisual |
| Assets/scripts/UI/HurdaEkonomisi.cs | Yerel para/sahiplik kayıtları ve ödül API'si |
| Assets/scripts/UI/MenuSettings.cs | Ayar ekranı, PlayerPrefs ve ayar uygulaması |
| Assets/scripts/UI/MenuAudioChannel.cs | Müzik/efekt tercihlerini AudioSource'a uygulayan adaptör |
| Assets/scripts/UI/MenuCarRegistry.cs | UI araç envanteri veri tipi |
| Assets/UI/MenuCarRegistry.asset | Otomatik taranan mevcut araç tanımları |
| Assets/UI/TerminalFont.asset | Arayüze ait Türkçe karakter destekli TMP font ve atlası |
| Assets/Scenes/Lobby.unity | Kurucu tarafından yeniden üretilen sahne |
| Assets/UI/Captures/Teslim/*.png | Dokuz ekran görüntüsü |

Unity tarafından üretilen eşlik eden `.meta` dosyaları da teslimin parçasıdır.

## Mevcut veri/API ihtiyaçları

- **CarCatalog asset'i yok.** Garaj, tanımları UI envanterinden listeler ve bunu ekranda belirtir. Şu anda iki CarDefinition var: RacerCoupe_01 ve SupercarV10_01. Araç kurucusu çalıştırılmadı. Yarış tarafındaki kataloğun hazırlanıp bağlanması ve UI ile aynı sıralamayı kullanması gerekir; yarışta kayıtlı seçim uygulaması bu eksik katalog nedeniyle uçtan uca sınanmadı.
- **CarStats dayanıklılık alanı sunmuyor.** Dört gerçek stat çizilir; dayanıklılık için veri olmadığı yazılır. Yarış tarafının ileride dayanıklılık/can değerini sunması gerekir; kütleden sahte dayanıklılık hesaplanmadı.
- RacerCoupe tanımındaki görsel parça fiyatları şu anda 0. Arayüz bu gerçek fiyatları kullanır; araç verileri değiştirilmedi.
- Model mesh'lerinde Read/Write kapalı olduğundan CarVisual birleştirme uyarısı verir ve desteklediği birleştirmesiz çizime döner. Önizleme çalışır; model import ayarları değiştirilmedi.
- SupercarV10_01 artık tanımı ve stok görünüm prefab'ıyla entegre. Modelin kendisi ve malzemeleri entegrasyon teslimine dahil edildi; Blender kaynakları bu değişikliğin dışında.

## Doğrulama

- Unity 6000.0.85f1 içinde derleme: hata yok.
- Kayıt/ekonomi test menüsü: geçti; asıl kayıtlar geri yüklendi.
- Gerçek UI parça düğmesi → satın alma → sabit Kaydet → CarLoadoutStore yükleme: geçti.
- LAN host açma, oyuncu satırı ve ayrılma: geçti.
- Bağlıyken garaj/mağaza engeli: geçti.
- Canlı Relay üzerinden oda kurma ve davet kodu alma: geçti.
- Kopyala düğmesi: kod panoya doğru yazıldı; testten sonra eski pano geri yüklendi.
- Connection approval yanlış şifre reddi: geçti.
- Bağlantı açıkken NetworkManager.SceneManager üzerinden Lobby yeniden yükleme: geçti; host bağlı kaldı, tek NetworkManager bulundu, oda açık ve ana menü kapalıydı.
- Ana menü, bağlantı, garaj/parça/boya/performans, mağaza, ayarlar ve oda görsel olarak kontrol edildi.
- Komut satırı otomatik test yordamları, approval, NetworkList, MatchSettings, transport seçimi ve yarış başlatma mantığı değiştirilmedi. **İki ayrı istemci, -autoHost/-autoJoin ile build testi ve gerçek yarıştan tur sonu dönüşü çalıştırılmadı.** Bağlı Lobby yeniden yükleme testi bu dönüş sözleşmesini ayrıca kontrol etti.
- race.unity, RaceHUD.cs, Assets/scripts/Race, Assets/scripts/Net, yarış prefab'ları, araç asset'leri, DefaultNetworkPrefabs ve Server üzerinde teslim değişikliği yok.

## Ekran görüntüleri

- [Ana menü](Captures/Teslim/01-ana-menu.png)
- [Bağlantı](Captures/Teslim/02-baglanti.png)
- [Garaj / parçalar](Captures/Teslim/03-garaj-parcalar.png)
- [Garaj / performans](Captures/Teslim/04-garaj-performans.png)
- [Garaj / boya](Captures/Teslim/05-garaj-boya.png)
- [Mağaza](Captures/Teslim/06-magaza.png)
- [Ayarlar](Captures/Teslim/07-ayarlar.png)
- [Kamera ve kontroller](Captures/Teslim/08-ayarlar-kontroller.png)
- [Çevrimiçi oda](Captures/Teslim/09-oda-online.png)


## Supercar V10 entegrasyonu

`Harvester/Arayüz/Supercar V10'u Garaja Ekle` menüsü `SupercarGarageBuilder.cs` üzerinden verilen FBX'i garaja hazırlar. FBX değiştirilmez; import ayarında Read/Write açılır. Stok dışı parçalar/jantlar ile çıkartma ve lastik yazısı prefab'da kapatılır. Mevcut CarBuilder.EnsureDefinition API'si bu prefab'dan tanımı ölçer. Rebuild Race Car çalıştırılmaz; yarış kodu/prefab'ı ve sahneler değiştirilmez.

- Görünen ad: V10 Orta Motor Süper Spor.
- Başlangıç mağaza fiyatı: 8.000 Hurda; satın alınca garajdaki sahip olunan araçlar arasında seçilebilir.
- 20 yuva: modelin 17 parça yuvası + jant, çıkartma ve lastik yazısı.
- 6 boya yuvası, 6 performans kategorisi; temel performans mevcut varsayılanlarla başlar.
- Ölçülen gövde: 4,55 × 2,00 × 1,21 m. Dingil: 2,66 m. Tekerlek yarıçapı ön 0,34 m / arka 0,36 m.
- Dosyalar: `Assets/scripts/Editor/SupercarGarageBuilder.cs`, `Assets/Cars/SupercarV10_01/` içindeki model, malzemeler, kimlik, yeni prefab ve `_Tanim.asset`; güncellenen UI envanteri.
- Doğrulama: envanterde iki araç; UI üzerinden 8.000 Hurda satın alma, garajdan seçme, GT kanadı takma, serbest gövde rengi uygulama ve CarLoadoutStore kayıt/yükleme geçti. Önizleme görsel olarak kontrol edildi, konsolda hata yok. Test sonrası oyuncunun para/sahiplik ve araç kayıtları geri yüklendi.
- Ortak yarış CarCatalog eksikliği sürüyor; yarışta V10 sürüş testi bu kapsamda yapılmadı.

[Ekran: V10 garaj](Captures/Teslim/10-supercar-v10-garaj.png) · [Ekran: V10 boya ve modifiye](Captures/Teslim/11-supercar-v10-modifiye.png)

## C1 — Dolar, 14 Kasım 2026
Kayıt anahtarı Harvester.Ekonomi.v1 ve hurda alanı korundu; mevcut $1.800 ve sahiplikler Unity MCP ile okundu. Dolar(12500) = $12.500, Fiyat(6000) = 30000 doğrulandı. Derleme hatası yok. Fiyatlar gösterim ve tahsilatta x5; tanımlar değiştirilmedi.

| Araç | Dolar |
|---|---:|
| GrandTourerV8_01 | $50.000 |
| HotHatch_01 | $30.000 |
| MuscleFastback_01 | $45.000 |
| RacerCoupe_01 | $0 |
| SupercarV10_01 | $40.000 |

| Kalem | Fiyat politikası |
|---|---|
| Görsel parçalar | Tanımdaki fiyat × 5 |
| Performans seviyeleri | Tanımdaki fiyat × 5; önceki seviye şartı korunur |
| Boya | Ücretsiz |

Kullanıcıya sorulacak: fiyat dengesi (araçlar 3–5 birincilik ödülü), eski bakiyenin nominal korunması. Bilinen sorun: sınıf adı ve JSON alanı uyumluluk için Hurda olarak kaldı.

## C2 — Ana menü ve oda akışı
Dosyalar: LobbyMenus.cs, LobbyMenuBuilder.cs, LobbyUIBuilder.cs, Scenes/Lobby.unity (Rebuild Lobby UI ile).
Ana menü dört seçenek; tek oyuncu yerel oda, garaj/mağaza, SÜR; çok oyuncu mevcut Online/LAN alanları ve oda kur/katıl. Oda listesinde hazır durumları, yerel/kurucu işaretleri, davet kodu ve geri sayım var. Hazır düğmesi iki yönlüdür. Bağlı garaj engelleri kalktı; garaj açmak ve modifiye uygulamak hazır durumunu kaldırır. Eski ağ alanları korunur, eski oda çizimleri gizlenir.
Unity MCP play testi: TEK OYUNCU düğmesi → oda=True, tek=True, oyuncu=1 → bağlı garaj=True → SÜR → race/Racing, yerel araba mevcut → sunucu Eliminate API ile eleme → sonuç süresinden sonra Lobby, oda=True, tek=True. Ekonomi/kayıt testi geçti. Derleme hatası yok; editör temiz ve play kapalı.
Kullanıcıya sorulacak: tek oyuncuda ayrı oturum panelinden garaj/mağazaya geçiş ve garaj açarken hazır durumunu kaldırma tercihi. Bilinen sorunlar: ikinci makineyle çok oyunculu test yapılmadı; TMP otomatik atlas gürültüsü claude'a bildirildi.

### C1 fiyat aralıkları (ek tablo)

| Araç | Ücretli görsel parçalar | Performans seviyesi |
|---|---:|---:|
| GrandTourerV8_01 | $750–$10.500 | $3.500–$28.750 |
| HotHatch_01 | $750–$8.250 | $2.750–$22.500 |
| MuscleFastback_01 | $750–$10.000 | $3.250–$27.500 |
| RacerCoupe_01 | $750–$9.000 | $3.000–$25.000 |
| SupercarV10_01 | $1.000–$11.000 | $3.500–$30.000 |

## C3 — Yarış HUD'u
Dosyalar: Assets/scripts/UI/Yaris/YarisArayuzu.cs, YarisKadrani.cs, YarisIsigi.cs ve Unity tarafından üretilen meta dosyaları. Görseller: Captures/Teslim/C3-isiklar-son.png ve C3-hiz-son.png.
uGUI/TMP arayüzü yarış sahnesi yüklenince bir kez oluşturulur; yarış sahnesi değiştirilmedi. Beş yuvarlak kırmızı ışık, sunucunun ışık verisini izler ve söndüklerinde gizlenir; süre/sayı gösterilmez. Sol alt kadran 10 km/sa aralıkları ve gerçek azami hızı, hız yayı/ibresi, iç devir yayı, vites ve 6.500 sonrası kırmızı bölgeyi gösterir. Sol üst sıra/tur/süre; sağda yerel vurgulu sıralama, ön/arka saniye araları, ELENDİ/AYRILDI, harvester saniye satırı. 30 m altında kırmızı yanıp sönen uyarı. Tablo aynanın altına, ışıklar üstüne yerleştirildi; ayna/render ayarlarına dokunulmadı.
Unity MCP testleri: temiz derleme; 5 lamba/2 yanık görüntüsü; Racing/StartLightsOut=True; gerçek araba verisi 149,9 km/sa, 6600 dev/dk, vites 6 ve kırmızı devir görüntüsü; +1.4 sn / -0.8 sn / NaN→— biçim testi; yerel satır ve harvester yakın uyarısı görsel kontrolü. LAN hazır=True → garaj açma → hazır=False ayrıca doğrulandı. Test gaz girdileri sıfırlandı. Son durum Lobby, kirli=False, play=False, derleniyor=False; konsolda hata yok. C# diff boşluk kontrolü temiz. Unity sahne YAML'ının otomatik boş alanlarında git diff --check boşluk uyarıları var; sahne elle düzenlenmedi.
Kullanıcıya sorulacak: zeytin yeşili terminal paleti, kadran ve aynaya göre HUD yerleşimi. Bilinen sorunlar: eski RaceHUD aynı anda çiziliyor (claude K2 ile kapatacak); iki gerçek istemciyle araların doğruluğu ve dört kişilik canlı sıralama ayrıca denenmeli. TMP atlas gürültüsü claude'a bildirildi. C4–C6 yapılmadı.

## C8 — Menü akışı ve pist oylaması (14 Kasım 2026)
- Oyuncu menüsü: garaj/araç seç, mağaza, SÜR, ayarlar, ana menü. SÜR ayrı pist kartları sayfasını açar. Tek oyuncuda kart SetTrack + SetReady ile başlatır; çok oyuncuda oy verir, aynı kart oyu geri alır, ayrı HAZIR düğmesi vardır. Oy değişince hazır durumu kaldırılır.
- Kartlarda tür, sabit yaklaşık uzunluk, oy sayısı ve adlar; oyuncu listesinde hazır/oy bilgisi. Sunucu API'si korunur. Karar yarış açılışında dört saniye gösterilir; eşitlik özeti önceki geri sayımdan korunur.
- Dosyalar: LobbyMenus.cs, Yaris/YarisArayuzu.cs, Yaris/PistKarariBildirimi.cs.
- Test: Unity MCP derleme temiz. Normal 7777'de tek oyuncu → SÜR → Dağ Tırmanışı → yarış → Eliminate API ile sonuç → otomatik Lobby dönüşü ve beş düğmeli oyuncu menüsü doğrulandı. LAN hostta oy 1/1 → aynı karta tekrar basınca -1/0 doğrulandı. İkinci oyuncu test verisiyle 1–1 eşitlik, LeadingTrack=-1 ve hazır sonrası sunucunun dag seçmesi doğrulandı. Oylama ekran görüntüsü /tmp/c8-oylama.png. Gerçek ikinci ağ istemcisi çalıştırılmadı. Test bayrakları ve ağ kodu değiştirilmedi.
- Kullanıcıya sorulacak: LethalLook renkleriyle metin kartları; Gece Pisti yaklaşık 1,4 km, Dağ Tırmanışı uzatma hedefiyle yaklaşık 5 km sabit yazıldı. Karar bildirimi sahne açılışında gösteriliyor; sunucu sahneyi karar anında doğrudan yüklediğinden yüklemeden önce bekleme eklenmedi.
- Bilinen sınır: oy yoksa mevcut pist kalır; eşitlik yazısı yalnız pozitif oylardaki eşitlikte kullanılır. C9 testi sırasında otomatik sahne geçişinde “Eşitlik → rastgele: Dağ Tırmanışı (sprint)” bildirimi yakalandı; dikiz aynasıyla çakışmayan son konum ayrıca /tmp/c8-karar.png ile doğrulandı.


## C9 — Dairesel mini harita (14 Kasım 2026)
- Sağ üstte 150 m yarıçaplı, yükseklikten bağımsız, ileri yönü yukarı olan radar. Pist çizgisi RaceTrack.Centerline'dan bir kez UI mesh'ine hazırlanır; yalnız RectTransform döner/kayar. Turlu pistin son noktası ilkine bağlanır, sprint bağlanmaz.
- Önceden oluşturulan UI Image üçgenleri: yerel yeşil, diğer araçlar sarı, harvester kırmızı. Üçgenler araç yönüne bakar; daire dışındakiler kenara sabitlenir; elenenler gizlenir. RaceSpectatorCamera.Target doğrudan izleyici merkezidir.
- Ayarlar/Kamera/Mini harita: varsayılan açık, PlayerPrefs Harvester.Ayarlar.MiniHarita ile kalıcı. Testte kapalı tercihi play yeniden başlatmada korundu; sonunda açık geri yüklendi.
- Dosyalar: YarisMiniHarita.cs, YarisPistCizgisi.cs ve Unity'nin ürettiği meta dosyaları; YarisArayuzu.cs kurulum, MenuSettings.cs tercih. C8 PistKarariBildirimi.cs konumu dikiz aynasından uzağa taşındı.
- Unity MCP testleri: derleme/konsol temiz; dag sprint (3988 çizgi köşesi) ve race turlu pistte radar görseli; 90° araç yönünde 90° harita dönüşü; 150 m kuzey/doğu örneklerinde ileri-yukarı, 7000 m yükseklik etkisiz; 9999 m uzaklıkta işaret 120 UI biriminde kenara sabit. Test aracıyla yeşil merkez, sarı yön/rengi, elenen işaretin gizlenmesi, izlenen araçta (0,0) merkez ve harvester izlerken kırmızı merkez doğrulandı. Görseller /tmp/c9-baslangic.png, /tmp/c9-turlu.png, /tmp/c9-izleyici.png.
- Performans: ısınma sonrası doğrudan 1000 LateUpdate çağrısı GC.GetAllocatedBytesForCurrentThread ile 0 bayt. Bu ölçüm mini harita güncellemesini kapsar, bütün HUD/kareyi kapsamaz. Sahnede mevcut ana kamera + dikiz kamerası dışında kamera eklenmedi (toplam 2). Doku ve mesh verileri yalnız kurulumda ayrılır, kapanışta şekil dokuları/sprite'lar temizlenir.
- Kullanıcıya sorulacak: sağ üstte 280×280 UI birimlik koyu yeşil radar, 150 m ölçek ve üçgen boyutları. Bilinen sınır: gerçek ikinci ağ istemcisiyle görsel test yapılmadı; ikinci araç runtime test nesnesiydi. İşaret havuzu 16 araçtır (mevcut oda sınırı 4). Yakın üst üste geçen dağ yolları yükseklik yok sayıldığı için beraber görünür; görevde istenen davranıştır.
- Editör play kapalı, zaman ölçeği 1, Lobby sahnesi temiz bırakıldı. Başka ajanın model dosyaları ve TMP atlas değişiklikleri teslim kapsamına dahil değildir.
