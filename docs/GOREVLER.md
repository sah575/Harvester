# Görevler — 14 Kasım 2026

Görevleri yönetici (`claude`) dağıtır. Kurallar: `docs/KOORDINASYON.md`. Her görev bitince `koord.py teslim`; bir görev bitmeden sıradakine geçebilirsin ama her teslim ayrı olsun. Tasarım/görünüş sorusu varsa `claude`'a yaz, kullanıcıya o iletir.

---

## CODEX (arayüz)

Alan: `Assets/scripts/UI/**`, `Assets/UI/**`, lobi kurucuları. **`lobby.cs`, `RaceManager.cs`, `RaceHUD.cs`, `RaceSpectatorCamera.cs`, `RaceCar.cs`'e yazma**: hepsi `claude`'un. Bir veri eksikse iste, kendin ekleme.

### C1. Para birimi: Hurda → Dolar
- Bütün metinlerde "Hurda/HURDA" yerine dolar: `$12.500` biçimi (binlik ayırıcı nokta).
- `HurdaEkonomisi` sınıfının adını değiştirmek serbest (ör. `Para`), ama **kayıtlı bakiye kaybolmasın**: PlayerPrefs anahtarı aynı kalsın ya da eskisinden bir kez taşı.
- Fiyatları dolara uygun ölçekle (yarış ödülleri aşağıda: 1. 10.000 $). Araba ve parça fiyatlarını buna göre öner, teslimde tablo olarak yaz.

### C2. Ana menü ve akış
- Ana menü: **TEK OYUNCU · ÇOK OYUNCU · AYARLAR · ÇIKIŞ**.
- **Tek oyuncu:** `lobby.Instance.StartSinglePlayer()`. Yerel bir oda açılır (dışarıya kapalı). Ekran: garaj + mağaza + büyük **SÜR** düğmesi → `lobby.Instance.SetReady(true)` → yarış hemen başlar. Yarış bitince aynı ekrana döner. "Ana menü" → `LeaveRoom()`.
- **Çok oyuncu:** Online/LAN seçimi (`SetOnlineMode`), **ODA KUR** (`HostRoom`) / **KATIL** (`JoinRoom`; kod ya da IP + şifre lobinin kendi input'larından okunur). Odada: oyuncu listesi + her oyuncunun hazır durumu, davet kodu (`RoomCode`), garaj/araç seçimi, **HAZIR** düğmesi (`SetReady(true/false)`). Herkes hazırsa 3 sn geri sayım (`AutoStartRemaining`) ve yarış kendiliğinden başlar. Kurucunun "başlat" düğmesi kalkabilir.
- **Garaj bağlıyken de çalışmalı:** `LobbyMenus`'teki `if(Connected)return;` engellerini kaldır. Seçim yarışa girerken arabanın sahibi tarafından `CarLoadoutStore`'dan okunuyor; ağ tarafında ek iş yok. Hazır olan oyuncu garajı değiştirirse `SetReady(false)` yap.
- Hikâye modu ileride gelecek: tek oyuncu menüsünde şimdilik tek pist, doğrudan garaja gir. Pist seçimi için yer bırakmana gerek yok.
- API (hepsi `lobby.Instance` üzerinde): `StartSinglePlayer()`, `HostRoom()`, `JoinRoom()`, `LeaveRoom()`, `SetOnlineMode(bool)`, `IsOnlineMode`, `SetReady(bool)`, `LocalReady`, `InRoom`, `PlayerCount`, `GetPlayer(i)` (`.Name`, `.Ready`, `.ClientId`), `IsLocal(p)`, `IsRoomOwner(p)`, `AutoStartRemaining` (-1 = yok), `RoomCode`, `lobby.SinglePlayer`, olaylar `RoomChanged`, `MenuReturned(string mesaj)`.

### C3. Yarış arayüzü (HUD)
Yeni HUD'u `Assets/scripts/UI/Yaris/` altında yaz (uGUI/TMP, LethalLook görünümüne uygun). Hazır olunca `claude` eski `RaceHUD` çizimlerini kapatır; o zamana kadar ikisi üst üste görünebilir, sorun değil.
- **Başlangıç ışıkları (F1):** 5 kırmızı ışık sırayla yanar, hepsi sönünce yarış başlar. `RaceManager.Instance.StartLightsLit` (0..5), `StartLightsOut`, `RaceManager.StartLightCount`. Kalan süre/sayı gösterme (bekleme rastgele).
- **Hız göstergesi, sol alt (NFS tarzı):** kadranda 0-10-20-…-max km/sa çentikleri, ibre ya da dolan yay; içinde devir yayı ve vites. `car.SpeedKmh`, `car.DisplayRpm` (`RaceCar.IdleRpm`..`RaceCar.RedlineRpm`), `car.DisplayGear` (-1 geri "R", 0 "N", 1..6), azami hız için `car.TopSpeed * 3.6f`. Kırmızı bölge 6.500 devirden sonra.
- **Sıralama tablosu (yarış sırasında):** sıra, isim, öndekine/arkadakine **saniye** cinsinden ara. `RaceManager.Instance.Standings` (Position, Name, Status), `GapToAhead(clientId)`, `GapToBehind(clientId)` (NaN = yok). Biçim: `+1.4 sn` / `-0.8 sn`. Yerel oyuncu vurgulu, elenenler soluk ve "ELENDİ".
- **Harvester:** `HarvesterGapMeters(clientId)` ve `HarvesterGapSeconds(clientId)`. Tabloda harvester da bir satır gibi görünsün ("HARVESTER — 2.1 sn"); 30 m altında kırmızı yanıp sönen uyarı.
- Sol üst: sıra (2/4), tur, süre.
- Yerel oyuncunun clientId'si: `NetworkManager.Singleton.LocalClientId`. Araba: `RaceCar.Local`.

### C4. Yarış sonu ekranı ve ödüller
- `RaceManager.Instance.Phase == RacePhase.Finished` olunca sonuç tablosu: sıra, isim, süre/ELENDİ, kazanılan para.
- Ödüller (sıraya göre): **1. 10.000 $, 2. 5.000 $, 3. 3.000 $, 4. 2.000 $**. Elenen oyuncu da son sıralamadaki yerinin ödülünü alır (kullanıcı aksini söylerse değişir). Ödül her oyuncunun kendi makinesinde, bir yarışta **bir kez** eklenir.
- Ekran 8 sn sonra kendiliğinden kapanıp odaya döner (bunu `RaceManager` yapar); "lobiye dönülüyor 5" sayacı göster.

### C5. Elenme / izleyici ekranı
- Biçilince "ELENDİN" + izleyici bilgisi. `RaceSpectatorCamera.Active` null değilse: `TargetName` ("Harvester" ya da oyuncu adı), `WatchingHarvester`, `Next()`, `Previous()`, `WatchHarvester()`. Kamera klavyeyle de değişiyor (← → / A D, H harvester); ekranda bu ipuçlarını ve tıklanabilir ok düğmelerini göster.

### C6. Bağlantı hataları
- `lobby.Instance.MenuReturned` mesajını (ör. "Bu kodla bir oda bulunamadı.", "Oda dolu.", "Bağlantı koptu.") ana menüde görünür bir uyarı kutusunda göster; "Tamam" ile kapansın. Bağlanırken "Bağlanıyor…" ve iptal düğmesi (`LeaveRoom`).

### C7. Pist seçimi (C1–C6 kabul edildi)
- Tek oyuncu ekranında ve çok oyunculu odada **pist seçici**: `lobby.TrackNames` (görünen ad), `lobby.Instance.TrackIndex` (seçili), `lobby.Instance.SetTrack(i)`. Çok oyunculuda yalnız kurucu değiştirebilir (`IsRoomOwner`), diğerleri seçili pisti görür; değişince `RoomChanged` gelir.
- Pistin küçük bir önizleme görseli olabilir (Dağ Tırmanışı için `BlenderSources/maps/DagTirmanisi/onizleme/dag_tirmanisi_ustten.png`'yi `Assets/UI/` altına kopyalayıp kullan). Sonuç ekranında ve HUD'da pist adı gösterilebilir.
- Dağ Tırmanışı A'dan B'ye: tur yok. HUD'daki "TUR" satırı `RaceManager.Instance.LoopTrack` false iken gizlenmeli; yerine kalan mesafe (`RaceTrack.Current.FinishDistance - Progress`) gösterilebilir. Uçtan uca test: tek oyuncu → Dağ Tırmanışı seç → SÜR → yarış → dönüş.

**Sıra:** C1 → C2 → C3 → C4 → C5 → C6 → C7. Her biri ayrı teslim. Play testlerini kilitle yap; tek oyunculu akış (SÜR → yarış → dönüş) teslimden önce bir kez uçtan uca denenmeli.

---

## AGY (Antigravity — modeller)

### A1. Yeni harita: Dağ Tırmanışı (sprint, A'dan B'ye)
Dağın eteğinden zirveye tek yönlü, **2–3 km** bir yol: keskin virajlar (en az 4 firkete), en az **2 tünel** (biri ≥150 m ve kıvrımlı), uçurum kenarı bölümler, kısa düzlükler. Çevre: çam/ladin ormanı, çimen, kayalar, kar çizgisi zirveye yakın, bariyerler (korkuluk), tünel ağızları, birkaç tabela. Gece ve sis olacak (oyunun LethalLook görünümü), yani uzak detay sis içinde kaybolur: uzağa değil yola yakın detaya yatırım yap.

**Ölçüler ve kurallar:**
- Yol genişliği **14 m** her yerde (harvester 8 m genişliğinde, virajlardan geçmeli). Firketenin iç yarıçapı ≥ 12 m.
- Eğim en fazla %12, yolun yanal eğimi (banking) en fazla 6°.
- Tünel içi tavan ≥ 7 m (harvester 5.5 m yüksek).
- Başlangıçta ~80 m düz ve geniş bir dizilme alanı (4 araba + harvester arkada), bitişte ~150 m düzlük + bitiş kemeri.
- Birimler metre, Unity'de +Y yukarı. FBX dışa aktarma ayarları araba kurallarındakiyle aynı (`references/spesifikasyon.md`).

**Teslim paketi** (`Assets/Maps/DagTirmanisi/`, kaynak `BlenderSources/maps/DagTirmanisi/`):
1. `Dag_Arazi.fbx`: arazi parçaları (yol dışı), ~200×200 m bloklar halinde (uzaktakiler kapatılabilsin). Toplam ≤ 250k üçgen.
2. `Dag_Yol.fbx`: yol yüzeyi ayrı mesh (çarpışma için), bariyerler ayrı mesh (`Bariyer_*`), tüneller ayrı (`Tunel_*`).
3. `Dag_Merkez.json`: yolun orta çizgisi, başlangıçtan bitişe sıralı noktalar, en fazla 4 m aralıkla: `{"genislik":14,"noktalar":[[x,y,z],...],"baslangic_m":40,"bitis_m":<m>}`. Oyun pisti, sıralamayı, harvester'ın yolunu buradan okur; **en önemli dosya bu**.
4. Ağaç/kaya/çimen **modelleri ayrı FBX** (`Agac_Cam_01.fbx` gibi; her biri LOD0 ≤ 1.500, LOD1 ≤ 400 üçgen) ve konumları `Dag_Yerlesim.json`: `[{"model":"Agac_Cam_01","p":[x,y,z],"r":ry,"s":olcek},...]`. Unity'de ben instancing ile yerleştireceğim; ağaçları arazinin içine gömme.
5. Malzemeler az olsun (≤ 12), dokular ≤ 1024 px. Mevcut araba malzeme kurallarındaki gibi `Materials/` altında.
6. Bir önizleme görseli (yukarıdan harita + birkaç yol seviyesi görüntüsü) `BlenderSources/maps/DagTirmanisi/onizleme/`.

**1. aşama kabul edildi (7305f9a):** iki bot pisti 73 sn'de bariyere çarpmadan bitirdi, harvester bütün firkete ve tünellerden geçti. **2. aşama şimdi:** çevre detayı (madde 4–6). Kurallar: `Dag_Yol.fbx`'teki yol yüzeyi, bariyer ve tünel geometrisini ve `Dag_Merkez.json`'u **değiştirme** (test edilmiş sürüş hattı). Arazi mesh'lerini (`Arazi_*`) daha detaylı yapabilirsin; isimleri koru. Ağaçlar yola/bariyere 3 m'den yakın, tünel ağzının önüne ve yolun üstüne gelmesin. Yerleşimi Unity'ye ben kuracağım; sen `Dag_Yerlesim.json` + model FBX'lerini teslim et. Ağaç sayısı ≤ 4.000, çimen öbekleri ≤ 6.000.

Önce **yalnız yol çizgisi ve kaba araziyi** (çevre detayı olmadan) teslim et: `Dag_Merkez.json` + kaba `Dag_Yol.fbx`. Ben sürüşü ve harvester'ı test ederim, onay verince çevre detayına geç. `Assets/`'e aktarırken kilit kuralı geçerli; `.meta` yazma.

---

## CLAUDE (yönetici)

- **K1. Senkronizasyon ve gecikme** (ana iş): ölçüm → istemci tahmini, interpolasyon tamponu, gönderim sıklığı, relay yolu, saat senkronu; arabalar ve harvester için. Hedef: aynı bölgedeki oyuncularda uzaktaki araba ve harvester 100 ms altında güncel görünsün, çarpışmalar tutarlı olsun.
- **K2.** Codex'in HUD'u gelince eski `RaceHUD` çizimlerini kapatmak; tek oyunculu ve hazır akışının uçtan uca testi.
- **K3.** Dağ haritası entegrasyonu: `Dag_Merkez.json` → `RaceTrack`, çarpışma, başlangıç dizilişi, harvester testi, yerleşim instancing, harita seçimi.
- Codex için verilen API'ler hazır ve derleniyor (14 Kasım): `lobby` (tek oyuncu, hazır, otomatik başlatma), `RaceManager` (F1 ışıkları sunucu saatine bağlı, aralar), `RaceCar` (gösterge vitesi/devri), `RaceSpectatorCamera` (hedefler arası geçiş).
