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

### C8. Menü akışı ve pist oylaması (C7 kabul edildi; kullanıcı kararı 14 Kasım)
Kullanıcının istediği akış:
- **Ana ekran:** TEK OYUNCULU / ÇOK OYUNCULU (+ ayarlar, çıkış). Bu kısım iyi, kalsın.
- **Tek oyunculu** → oyuncu menüsü: **GARAJ / ARAÇ SEÇ**, **MAĞAZA**, **SÜR**, **AYARLAR**, **ANA MENÜ**. SÜR → **pist seçimi** ekranı (pist kartları: ad, tür turlu/sprint, uzunluk) → seçince yarış başlar (`lobby.SetTrack` + `SetReady(true)`).
- **Çok oyunculu** → bağlantı sayfası (mevcut, iyi) → odaya girince aynı oyuncu menüsü (GARAJ / ARAÇ SEÇ, MAĞAZA, SÜR, AYARLAR, ANA MENÜ=odadan çık). SÜR → **pist oylaması** ekranı: her pist kartında oy sayısı ve kimin oy verdiği; oyuncu bir karta tıklayınca oy verir (tekrar tıklarsa geri alır), sonra HAZIR. Herkes hazırsa yarış başlar; **çoğunluğun seçtiği pist, eşitlikte rastgele** (sunucu karar verir). Oyuncu listesinde kim hazır/kim oy verdi görünsün. Karar anında kısa bir "Seçilen pist: …" bildirimi (eşitlikte "Eşitlik → rastgele: …").
- Oylamanın ağ tarafı hazır (lobby.cs): `VoteTrack(int index)` (-1 ya da aynı pist = geri al), `LocalVote`, `TrackVotes(int)`, `LeadingTrack` (eşitlik/oy yok = -1), `GetPlayer(i).VotedTrack`, karar sonrası `TrackIndex` değişir ve `RoomChanged` tetiklenir. Kurucunun pist seçme düğmesi (C7) çok oyunculuda kaldırılır; tek oyunculuda `SetTrack` kalır.
- Test bayrakları (`-autoHost`, `-autoJoin`, `-autoStart`, `-track`) bozulmamalı.

### C9. Yarışta mini harita
Yarış sırasında köşede **dairesel mini harita** (örnek: yarış oyunlarındaki yuvarlak radar): pist çizgisi (`RaceTrack.Current.Centerline`), oyuncu ortada, harita oyuncunun gidiş yönüne göre döner (yukarı = ileri).
- İşaretler **üçgen** ve yöne bakar: **kendin yeşil**, diğer oyuncular **sarı**, **harvester kırmızı** (`RaceCar.All`, yerel araba için mevcut HUD'un kullandığı yol, `HarvesterAI.Instance`). Elenen arabalar gösterilmez. Daire dışına düşen işaretler kenara sabitlenir (harvester'ın yönü hep görülsün).
- Ölçek: daire yarıçapı ~150 m (sprint pistinde de çalışmalı; Dağ Tırmanışı 2,5 km, ileride 5 km). Yükseklik farkı yok sayılır (yukarıdan görünüm).
- **Performans:** ek kamera yok. Pist çizgisini yarış başında bir kez bir dokuya ya da UI mesh'ine çiz, her karede yalnız döndür/kaydır; işaretler UI Image. GC tahsisi yok.
- Mevcut HUD (`Assets/scripts/UI/Yaris/`) ile çakışmasın; izleyici modunda izlenen arabayı merkez al. Ayarlardan aç/kapat (varsayılan açık).

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

### A2. Dağ Tırmanışı: zirveye uzatma (kullanıcı kararı, 14 Kasım)
Kullanıcı mevcut haritayı beğendi ("agy'nin eline sağlık"). Şimdi yol **toplam ~5 km** olacak ve **zirvede karlı bir bitişle** bitecek.

**Kurallar:**
- Mevcut yolun eski bitiş düzlüğüne kadar olan kısmı (yaklaşık ilk 2.340 m: yüzey, bariyer, tüneller, `Dag_Merkez.json` noktaları) **aynen kalır**; test edilmiş sürüş hattı bu. Eski bitiş düzlüğü ve kemeri kaldırılır, yol oradan devam eder.
- Yeni bölüm (~2,5 km): ağırlık **saç tokası virajlarda**. Dağ yamacına üst üste dizilmiş en az 6 firkete (alttaki yol yukarıdan görünsün), aralarda kısa düzlükler. Ölçüler A1'deki gibi: genişlik 14 m, firkete iç yarıçapı ≥ 12 m, eğim ≤ %12, yanal eğim ≤ 6°, tünel varsa tavan ≥ 7 m.
- Son ~1 km kar bölgesi: karlı arazi, karlı ağaçlar ve kayalar, yol kenarlarında kar birikintisi (yolun sürüş yüzeyi temiz kalabilir). Kar çizgisi şimdiki gibi yükseklikle başlar.
- **Zirvede bitiş:** ~150 m düzlük + bitiş kemeri, karlı zirve düzlüğü.
- **Manzara noktası (isteğe bağlı):** bitişten sonra küçük bir seyir terası (vadi ve alttaki firketeler görünsün). Performansı zorlarsa koyma. Ölçü: yeni bölüm sınırları içinde, ek model ≤ 3, üçgen bütçesine dahil.
- Bütçe (bütün harita): arazi ≤ 350k üçgen, ağaç ≤ 6.000, çimen öbeği ≤ 8.000, malzeme ≤ 14. Mevcut modelleri tekrar kullan; yeni model gerekiyorsa (karlı kaya, kar yığını) A1 sınırları geçerli.
- Dosya adları ve formatlar A1'deki gibi (`Dag_Yol.fbx`, `Dag_Arazi.fbx`, `Dag_Merkez.json`, `Dag_Yerlesim.json`); dosyalar güncellenir, yeni harita açılmaz.

**Aşamalar (A1'deki gibi):** önce **yalnız yol + kaba arazi** (`Dag_Merkez.json` + `Dag_Yol.fbx` + kaba `Dag_Arazi.fbx`) teslim et ve dur; ben botlarla ve harvester'la test ederim. Onaydan sonra 2. aşama: yeni bölümün çevre detayı + kar + manzara.

---

## CLAUDE (yönetici)

- **K1. Senkronizasyon ve gecikme** (ana iş): ölçüm → istemci tahmini, interpolasyon tamponu, gönderim sıklığı, relay yolu, saat senkronu; arabalar ve harvester için. Hedef: aynı bölgedeki oyuncularda uzaktaki araba ve harvester 100 ms altında güncel görünsün, çarpışmalar tutarlı olsun.
- **K2.** Codex'in HUD'u gelince eski `RaceHUD` çizimlerini kapatmak; tek oyunculu ve hazır akışının uçtan uca testi.
- **K3.** Dağ haritası entegrasyonu: `Dag_Merkez.json` → `RaceTrack`, çarpışma, başlangıç dizilişi, harvester testi, yerleşim instancing, harita seçimi.
- Codex için verilen API'ler hazır ve derleniyor (14 Kasım): `lobby` (tek oyuncu, hazır, otomatik başlatma), `RaceManager` (F1 ışıkları sunucu saatine bağlı, aralar), `RaceCar` (gösterge vitesi/devri), `RaceSpectatorCamera` (hedefler arası geçiş).
