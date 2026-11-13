# Yarış dışı arayüz teslimi

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
