# Atölye ve araç galerisi — yeni tasarım

13 Kasım 2026. Önceki test arayüzünün yerine, aracın ekranda merkezde kaldığı ve işlemlerin çevresine yerleştiği yeni düzen kuruldu. Referanslar: kullanıcının paylaştığı Underground 2 garaj görüntüsü ve [oyunun kullanım kılavuzundaki özelleştirme bölümü](https://www.videogamemanual.com/PS2/Need%20for%20Speed-%20Underground%202%20%28USA%29.pdf). Oyunun görselleri, logoları veya arayüz asset'leri kopyalanmadı; ortam ve arayüz kodla üretildi.

## Kullanım

**Araç galerisi:** Yalnızca araçlar sergilenir. Ekrandaki sol/sağ oklar veya klavyedeki sol/sağ oklarla araç değişir. Araç yuvarlak platformda, performans çubukları altında ve fiyatı üstünde görünür. Satın alma ayrı düğmedir. Sahip olunan araçta aynı düğme garajda düzenlemeye götürür.

**Garaj:** Sahip olduğun araçlar arasında üstteki oklarla veya Aracım bölümünden geçebilirsin. Görünüm, Boya ve Performans bölümleri ayrıdır. Soldan modül (ör. Farlar, Jantlar, Rüzgârlık), sağdan o modülün seçeneği seçilir. Jant/far/tampon gibi modüller seçildiğinde kamera ilgili bölgeye yaklaşır. Araç ve dört gerçek performans değeri ekranda kalır.

**Önce dene, sonra tak:** Parça veya performans seviyesi seçmek yalnızca önizlemedir; para kesmez, kayıtlı modifiyeyi değiştirmez. Tak / Satın al ve tak ya da Uygula düğmesi seçimi taslağa geçirir. Kaydet, takılı taslağı CarLoadoutStore'a yazar. Onaylanmamış önizleme Kaydet ile kalıcı hâle gelmez. Boya ücretsizdir, anında taslağa/önizlemeye uygulanır; kalıcı olması için yine Kaydet gerekir.

**Kamera:** Araç görüntüsü üzerinde sol fare tuşunu tutmak otomatik dönüşü durdurur. Sürüklemek yatay/dikey açı değiştirir. Fare tekerleği yakınlaştırır. Menü düğmelerine basmak kamerayı döndürmez. Modül yakın planında otomatik dönüş kapalıdır; elle çevrilebilir. Tüm aracı gör genel açıya ve kısa beklemeden sonra otomatik sergi dönüşüne döner. Fare bırakıldığında veya uygulama odağı kaybolduğunda tutma durumu temizlenir.

**Araç başına kayıt:** Her aracın kaydedilmiş parça, renk ve performans seçimi kimliklerle ayrı tutulur. Araç değiştirip başka aracı kaydetmek önceki aracın modifiyesini silmez. Yarışın okuduğu aktif seçim API'si CarLoadoutStore olarak kalır. Eski tek araç kaydı ilk açılışta araç profiline alınır.

## Ortam ve kurucu

Atölye; dairesel çelik platform, emniyet dilimleri, beton zemin/derzler, servis kepenkleri, çelik kolonlar, liftler, takım dolapları, yedek lastikler ve tavan ışıklarından oluşur. Kamera dolaşım alanı serbest bırakılmıştır. Ortam yalnızca önizleme katmanında, ayrı RenderTexture kamerasında çizilir. Özel malzemeler çalışma zamanında oluşur ve kapanırken temizlenir; yarış ortamı/ayarları değiştirilmez.

- `Harvester/Rebuild Lobby UI`: yeni galeri ve atölye panellerini Lobby içinde yeniden kurar. Uygulandı ve sahne kaydedildi.
- `Harvester/Arayüz/Grand Tourer V8'i Garaja Ekle`: yeni aracı prefab ve CarDefinition ile hazırlar. Uygulandı.
- `Harvester/Arayüz/Muscle Fastback'i Garaja Ekle`: çalışma sırasında gelen ikinci yeni aracı hazırlar. Uygulandı.
- `Harvester/Arayüz/Supercar V10'u Garaja Ekle`: önceki V10 kurucusu korunur.
- `Harvester/Arayüz/Kayıt ve Ekonomi Testi`: önceki ekonomi test menüsü korunur.

## Yeni araçlar

GrandTourerV8_01: görünen ad **V8 Uzun Kaput**, başlangıç fiyatı **10.000 Hurda**. Yeni tanımda 20 modifiye yuvası (17 gövde/iç mekân + jant/çıkartma/lastik yazısı), 6 boya yuvası ve 6 performans kategorisi bulunur. Performans mevcut varsayılanlarla başlar; özel dengeleme yapılmadı.

Çalışma sırasında gelen ikinci araç **MuscleFastback_01**, görünen adı **V8 Kaslı Fastback**, fiyatı **9.000 Hurda** olarak eklendi. Onda da 20 modifiye/6 boya yuvası var. Malzeme bağlantıları, model önizlemesi ve CarLoadoutStore kayıt döngüsü doğrulandı.

Katalog sırası korunmuştur: RacerCoupe_01 → SupercarV10_01 → GrandTourerV8_01 → MuscleFastback_01 → HotHatch_01. UI envanteri artık mevcut sırasını koruyup yeni tanımları sona ekler. FBX'in kendisi değiştirilmedi; Read/Write açıldı, stok dışı varyantları kapatan model prefab'ı ve modelden ölçülen tanım üretildi. Rebuild Race Car çalıştırılmadı.

## Değişen dosyalar

- `Assets/scripts/UI/GaragePreview.cs`: atölye ortamı, dairesel platform, kamera odağı, dönüş ve temizlik.
- `Assets/scripts/UI/GaragePreviewDrag.cs`: fare basma/bırakma, sürükleme, tekerlek ve odak kaybı.
- `Assets/scripts/UI/LobbyMenus.cs`: galeri gezinmesi, modül akışı, önizleme/onay ayrımı, performans karşılaştırması ve kayıt bağlantısı.
- `Assets/scripts/UI/GarageLoadouts.cs`: araç başına kalıcı modifiye.
- `Assets/scripts/UI/MenuWidgets.cs`: yeni vurgu rengi ve daha hafif paneller.
- `Assets/scripts/Editor/LobbyMenuBuilder.cs`: yeni panel yerleşimleri ve kontroller.
- `Assets/scripts/Editor/MenuCarRegistryBuilder.cs`: mevcut araç sırasını koruma.
- `Assets/scripts/Editor/SupercarGarageBuilder.cs`: ortak entegrasyon yordamı ve V8 kurucu menüsü.
- `Assets/Cars/GrandTourerV8_01/` ve `Assets/Cars/MuscleFastback_01/`: verilen model, kimlik/malzemeler, yeni prefab ve tanım.
- `Assets/UI/MenuCarRegistry.asset`, `TerminalFont.asset`, `Assets/Scenes/Lobby.unity`: kurucu çıktıları.
- `Assets/UI/Captures/Atolye/Teslim/`: yedi güncel ekran görüntüsü. Görüntüler test kayıtlarıyla alınmıştır; test sonunda kullanıcının kayıtları geri yüklenmiştir.

## Kontroller

Unity 6000.0.85f1 derlemesi ve son konsol kontrolü hatasızdır.

- Jant önizlemesi: dört jant değişti, Hurda ve kayıtlı jant aynı kaldı.
- Gerçek UGUI pointer down/drag/up olayları: tutma otomatik dönüşü durdurdu, sürükleme açıyı değiştirdi, bırakma durumu temizledi.
- Parçayı onaylama ve Kaydet: aktif CarLoadoutStore seçiminde doğrulandı.
- Araç başına kayıt: başka araca geçilip kaydedildikten sonra önceki jant seçimi geri yüklendi.
- Performans önizlemesi ücret/kayıt değiştirmedi; onaydan sonra Motor seviye 1 için 1.000 Hurda kesildi ve kayıt doğru okundu.
- Serbest renk: B25C29FF, gerçek UI düğmesiyle uygulandı ve RGB(178,92,41) olarak kayıt/yükleme doğrulandı.
- Galeri sol/sağ düğmeleri doğru araca geçti. Klavye okları eski Input Manager üzerinden bağlandı; fiziksel klavye testi yapılmadı.
- Yeni V8: yetersiz bakiye düğmeyi engelledi; test bakiyesiyle satın alma, garajda açma ve seçimin kaydı geçti.
- LAN host, bağlıyken garaj/mağaza engeli, bağlantı açıkken Lobby yeniden yükleme, tek NetworkManager ve Ayrıl sonrası ana menü/önizleme kontrolü geçti.
- Test sonrasında kullanıcının para/sahiplik, aktif araç ve araç profili kayıtları geri yüklendi.

Gerçek iki istemcili yarış/tur testi yapılmadı. Mevcut yarış CarCatalog eksikliği sürüyor; arayüz tanım envanterini kullanıyor. Race/Net kodu, race.unity, yarış HUD'ı ve ağ prefab'ları değiştirilmedi. Dayanıklılık API'si olmadığı için sahte bir stat gösterilmez; dört gerçek değer kullanılır.

## Görseller

[Galeri](Captures/Atolye/Teslim/01-arac-galerisi.png) · [Atölye](Captures/Atolye/Teslim/02-atolye.png) · [Jant yakın plan](Captures/Atolye/Teslim/03-jant-yakin-plan.png) · [Performans](Captures/Atolye/Teslim/04-performans.png) · [Boya](Captures/Atolye/Teslim/05-boya.png)

[Muscle Fastback galerisi](Captures/Atolye/Teslim/06-muscle-galeri.png)


## Turbo Hatch entegrasyonu

HotHatch_01, **Turbo Hatch** adıyla **6.000 Hurda** fiyatından beşinci araç olarak eklendi. 20 modifiye yuvası, 6 boya yuvası ve 6 performans kategorisi var. Ölçülen stok gövde 4,31 × 1,86 × 1,45 m. Temel performans mevcut varsayılanlarla başlar; kimlik JSON'undaki AWD ve karakter puanları yalnızca öneridir, sürüş API'si değiştirilmedi.

`Harvester/Arayüz/Hot Hatch'i Garaja Ekle` menüsü stok prefab'ı ve tanımı üretir; görünen parça adlarını teslim edilen kimlik JSON'undaki Türkçe adlardan alır. Model nesne adları ve kayıt anahtarları korunur. Katalog sırası değiştirilmeden sona eklenir.

Dosyalar: `Assets/Cars/HotHatch_01/` (verilen model, malzemeler ve kimlik + üretilen prefab/tanım), `Assets/scripts/Editor/SupercarGarageBuilder.cs`, UI envanteri ve ekran görüntüsü.

Kontrol: galeri 05/05, doğru CarVisual modeli, eksik malzeme olmaması, gerçek UI üzerinden satın alma/garaja geçiş, dört jantın Türbin varyantına geçmesi ve CarLoadoutStore'da jant 2 olarak kayıt/yükleme geçti. Unity konsolunda hata yok. Oyuncunun para, sahiplik, aktif araç ve araç başına modifiye kayıtları testten sonra geri yüklendi. Yarış testi yapılmadı; önceki CarCatalog sınırlaması sürüyor.

[Turbo Hatch ekran görüntüsü](Captures/Atolye/Teslim/07-turbo-hatch.png)
