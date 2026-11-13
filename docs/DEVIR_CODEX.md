# Devir notu: Claude → Codex (13 Kasım 2026, gece)

Claude'un kredisi bitmek üzere. Bu belge yapılanları, yarım kalanı ve sıradaki işleri anlatır. Proje
sahibinin sırası: **temizlik → optimizasyon → kullanıcı testi**. Sonra kullanıcı harvester tehdit modelini
anlatacak, en son arkadaşlarla online çarpışma testi.

Önce oku: `docs/YarisMimarisi.md` (mimari ve kurallar), `Assets/UI/TeslimNotlari.md` (senin arayüz notların),
`.agents/skills/arac-modelleme/` (araba modelleme kuralları). `CLAUDE.md` yerel ve git dışında, kısmen eski
(bkz. 4).

## 1. Git durumu

- Dal: **`yaris-entegrasyon`**. Senin `ui-menus` dalının üstünde. GitHub'a push edildi.
- `main`'e merge edilmedi. Açık PR: #5 (`unity6-upgrade`, eski). Bütün iş bitince `yaris-entegrasyon` için
  yeni bir PR açılmalı; merge'ü kullanıcı onaylar.
- Son commit'ler (eskiden yeniye):
  - `4023234`: Antigravity'nin Blender kaynakları ve son model düzeltmeleri.
  - `df150d0`: araçların performansı ve yarış entegrasyonu.
  - `6db3fab`: dikiz aynası.
  - (bu belge)
- **Commit'lenmemiş, yarım:** temizlik (bkz. 2). Yerel çalışma kopyasında duruyor, GitHub'da yok.

## 2. Yarım kalan: eski saklambaç dosyalarının temizliği

Bir alt ajan bu işi yaparken durdu. Çalışma kopyasında ~170 değişiklik var (silmeler ve kod değişiklikleri).
Önce `git status` ile neyin yapıldığını gör.

**Amaç:** saklambaç oyunundan kalan her şeyi silmek. **Lethal Company görünümü** (LookBuilder, `LethalLook`
shader ve malzemesi, URP renderer feature'ları, `FoggySkybox`, `NightSkyFog`/`NightSky` ve HDRI, Volume
profilleri) ve **dikiz aynası** (`RearViewMirror_Renderer`) kalacak.

**Silinecekler:**
- sahneler `world`, `mpdeneme`, `deneme`, `testFrank`;
- robotlar (`Assets/Prefabs/chars/`, `Assets/anims/`, `RobotBuilder`, `BlenderSources/robot.py`);
- `Assets/Prefabs/modifiyeli/` (FRANKV2*);
- `ilkelHal`'deki harvesterv2/v3 FBX'leri;
- `Assets/Prefabs/v2mirrors/`, `Assets/Backgrounds/`, `Assets/UI Toolkit/`;
- robot/arazi malzemeleri;
- kod: `charMovement`, `frankMovement`, `BoundaryGuard`, `GameHUD`, `SpectatorCamera`, `spamChars`,
  `OwnerNetworkAnimator`, `GameManager`, `AutoTestReporter`, `WorldBuilder`, `NetSyncBuilder`;
- **mısır: bütün `Assets/scripts/Crops/`**, Corn shader ve malzemeleri, `FieldBounds` (karar: sil, git'te
  duruyor).

**Değiştirilecekler:**
1. **Harvester modeli:** `ilkelHal/harvesterv4.1.fbx` ve harvester malzemeleri `AssetDatabase.MoveAsset` ile
   `Assets/Models/Harvester/` altına taşınır (GUID korunur).
   - FRANKV2'den yalnız görsel bir `HarvesterGorsel.prefab` üretilir: hiyerarşi adları aynı, sadece
     Transform/MeshFilter/MeshRenderer.
   - `HarvesterAIBuilder.SourcePath` bu prefab'ı gösterir.
   - `Harvester/Rebuild Harvester AI` sonrası `HarvesterAI.prefab` aynı görünmeli ve `GlobalObjectIdHash`
     değişmemeli.
2. **`GameManager.InputAllowed`:** `RaceCar.cs` ve `CarTrackFollower.cs`'deki ifadeler silinir. Yarışta
   GameManager zaten yoktu, yani davranış aynı kalır. Girdi kilidini `RaceCar.InputEnabled` + `RaceManager`
   sağlıyor.
3. **`AutoTestReporter` silinir.** `-ntInterp`/`-ntBuffer` bayrakları `Race/Net/RaceAutoTest.cs`'e taşınır.
   `lobby.cs`'deki `AutoTestReporter.Create()` kaldırılır. Yarış testleri: `-autoDrive`, `-autoTrace`,
   `-autoShot`, `-autoElim S`, `-autoBots N`, `-autoLaps N`.
4. **`lobby.cs`:**
   - `raceMode`, `world` yolu, Harvester/Robot rolü, `RequestRoleRpc`, `HarvesterIndex`,
     `LobbyPlayer.IsHarvester`, `MatchSettings.PlayerInfo.IsHarvester` ve `-autoHarvester` kalkar.
   - StartGame her zaman `race` yükler.
   - **Ağ davranışı aynı kalır.**
5. **`LobbyUIBuilder`'dan rol satırı (`roleRow`) çıkar.** Lobby sahnesini açıp `Harvester/Rebuild Lobby UI`
   çalıştır ve kaydet. Senin panellerin (`LobbyMenuBuilder`) yeniden üretilir; hepsinin durduğunu kontrol et.
6. **`DefaultNetworkPrefabs.asset`:** eski 5 kayıt (FRANKV2, FRANKV2.1, robo1, robo2, punisher) çıkar. Sadece
   `RaceCar` ve `HarvesterAI` kalır. Kayıtlar prefab'lar silinmeden **önce** çıkarılmalı.
7. **Build Settings:** sadece `Lobby` ve `race`.
8. **Paketler:** `com.unity.timeline`, `com.unity.visualscripting`, `com.unity.collab-proxy` kaldırılır (son
   adım, ayrı derleme).
9. **Belgeler:** `docs/OyunTasarimi.md` başına "eski tasarım (saklambaç), güncel: `docs/YarisMimarisi.md`"
   notu; `YarisMimarisi.md`'deki "eski oyun duruyor" satırları güncellenir.

**Doğrulama:**
- derleme hatasız;
- silinen GUID'lere kalan bir referans yok (`AssetDatabase.GetDependencies` + `.meta` GUID taraması);
- `Missing` script/referans yok;
- Lobby → race → Lobby dönüşü;
- harvester prefab;
- iki build ile online test (bkz. 5);
- Lethal görünüm ekran görüntüsü, önce ve sonra.

Bitince **ayrı bir commit**: "Remove the old hide-and-seek game".

## 3. Sıradaki: optimizasyon (bir alt ajan analiz etti, uygulanmadı)

Önce ölç, sonra uygula, tekrar ölç. Zayıf PC vekili: bu makinedeki **Intel Iris Xe**. Build'i NVIDIA ortam
değişkenleri olmadan açınca Intel'de çalışır. Güçlü: RTX 3060, `__NV_PRIME_RENDER_OFFLOAD=1
__GLX_VENDOR_LIBRARY_NAME=nvidia`. Frame Timing Stats açık.

Öncelik sırası:
- **P0, ölçüm altyapısı:** `Assets/scripts/Race/Perf/PerfProbe.cs` (`-perfProbe <sn> -perfLabel <ad>`).
  - `FrameTimingManager`: CPU ana/render ve GPU ms.
  - `ProfilerRecorder`: draw call, SetPass, batches, triangles, shadow casters, GC/kare.
  - Çıktı: CSV ve `[Perf]` özeti (p50/p95/p99), gerçek `renderScale` ve kalite seviyesi.
  - Senaryo: 4 araba (`-autoHost -autoBots 3 -autoDrive`), harvester ve ayna açık, 60 sn, 1080p pencere.
- **P1, KRİTİK HATA (senin kodun):** `Assets/scripts/UI/MenuSettings.cs` `ApplySaved()` her açılışta URP
  `renderScale`'i 1.0 yapıyor (`PlayerPrefs` varsayılanı 1). Lethal görünümün temeli olan **0.5 ezilmiş**:
  oyun 4 kat piksel çiziyor, pikselli görünüm kayboluyor.
  - Düzeltme: kayıtlı değer yoksa hiçbir şey uygulama.
  - Kaydırıcı mutlak değer değil, taban ölçeğin (0.5) çarpanı olsun (ör. 0.75–1.25×), ya da kaldır.
  - Çıkışta/oyundan dönüşte asset bellekte 1'de kalmasın.
- **P2, kalite seviyeleri:** 6 seviye 3'e insin: **Düşük / Orta / Yüksek** (Türkçe adlar,
  `QualitySettings.names`).
  - Kurucu menü: `Editor/QualityBuilder.cs`, `Harvester/Apply Quality Levels`. Kurucu LethalLook özelliğini ve
    ayna renderer'ını korumalı.
  - Şu anki "Low/Very Low" ay ışığını kapatıyor ve ek ışıkları vertex'e alıyor, yani farlar yolu
    aydınlatamıyor.
  - Herkes ilk açılışta "Ultra" ile başlıyor. İlk açılışta GPU'ya göre otomatik seçim olsun: entegre ya da
    <4 GB → Düşük.
  - `MenuSettings` seviye adlarını göstersin.
  - Öneri tablosu:
    - **Düşük:** gölge yok, far clip 200, ayna kapalı ya da 15 Hz, `targetFrameRate` 60.
    - **Orta:** hard gölge 40 m, Forward+.
    - **Yüksek:** soft gölge 80 m, 2 kaskad.
- **P3, görüş mesafesi:** sis ~250 m'de her şeyi yutuyor, ama kamera far clip'i 600.
  - Seviyeye göre 200/250/300 olsun.
  - Bir "Detay" katmanı (lamba parçaları, panolar, küçük araba parçaları) `layerCullDistances` ile ~120 m'de
    kesilsin.
- **P4, ışık bütçesi (~29 ek ışık):**
  - Arabaların stop lambası point ışıkları kaldırılsın; emisyon zaten var.
  - Uzak arabaların farları mesafeyle sönsün; yerel araba hep tam.
  - Orta/Yüksek'te Forward+ denensin: nesne başı ışık sınırı kalkar, ışık yanıp sönmeleri biter.
  - Harvester spot menzili (130 m) tasarım kararı; kullanıcıya sor.
- **P5, harvester LOD:** model 116.7k üçgen; bütün pistten fazla.
  - Decimate ile yeni bir FBX: LOD0 ~10–12k, LOD1 ~4k.
  - `LODGroup`, dithered crossfade.
  - Prefab ağ objesi: hash kontrolü.
- **P6, araba draw call'ları** (araba başı ~30 draw; ayna da bunlardan pay alıyor):
  - Boya için `MaterialPropertyBlock` yerine araba başına önbellekli malzeme kopyaları (SRP Batcher uyumu).
  - Tekerlek, jant ve yazıyı göbekte tek mesh'e birleştir.
  - Uzak arabada gölge ve tekerlek animasyonunu kapat.
  - Garaj önizlemesi (`GaragePreview`) MPB'ye güveniyorsa uyarla.
- **P7, enkaz ve object pooling:**
  - Parçalanma enkazı için "Enkaz" katmanı (sadece zemin ve bariyerle çarpışır), uyutma ve süre sonunda
    temizleme.
  - Genel havuz sistemi: `Assets/scripts/Race/Pooling/`: `Pool.Spawn/Release/Prewarm`, `IPoolable`,
    `PooledObject`. Gelecekteki roket, mermi, yağ ve kıvılcım efektleri için.
  - Ağda: efektler yerel, gerekirse NGO `INetworkPrefabInstanceHandler`.
- **P8, GC:** `RaceHUD.OnGUI`'de her karede `new GUIStyle` (önbelleğe al); `NetStatsHUD` metnini 0.25 sn'de
  bir yenile.
- **P9, pist:** `TrackBuilder` zemin payı 320 → ~220 m, uzakta daha seyrek hücre; lambaların 3 primitive'i tek
  mesh.
- **P10, build:**
  - Mono kalsın (IL2CPP Windows'u Linux'tan derleyemiyor).
  - `StripUnusedPostProcessingVariants` açık.
  - Linux'ta Vulkan ve OpenGL ölçülsün, iyisi öne alınsın.
  - **Occlusion culling bake ÖNERİLMİYOR:** pist açık, perdeleyen bir şey yok.

Her adımda ölç, tabloya yaz. Hedefler (öneri): Iris Xe + Düşük + 1080p p95 < 16.7 ms; 3060 + Yüksek p95 < 7 ms.

## 4. CLAUDE.md'de güncellenecekler (yerel dosya, git dışı)

Eski olan kısımlar:
- world, robot ve "Phase 4" anlatımı;
- `Rebuild Field Fence`, `Rebuild Game HUD`, `Apply Network Sync Settings` ve `Rebuild Robot Prefabs` menüleri;
- örnek test bayrakları (`-autoHarvester -autoKill`);
- "karakter prefab'ı değişince 4'ünü güncelle" kuralı.

Eklenmesi gerekenler: yarış mimarisi (kısaca `docs/YarisMimarisi.md`'ye yönlendir), araç sistemi (`CarDefinition` /
`CarCatalog` / `CarLoadout` / `CarVisual`, katalog sırası sabit), yeni kurucu menüler (`Rebuild Race Track`, `Rebuild
Race Car`, `Rebuild Harvester AI`, `Rebuild Race Network`, `Araç/Kimlik Kartından Performans Üret`, `Setup Rear View
Mirror`, `Rebuild Lobby UI`).

## 5. Test yöntemleri

- **Tek editör kuralı:** sahne açma, play mode ve build aynı anda tek işte yapılır. Network prefab değişince
  `ForceReserializeAssets` ve diskteki `GlobalObjectIdHash` = editördeki (yoksa "NetworkConfig mismatch").
- **Bilinen gürültü** (commit'leme, geri al): TMP `LiberationSans SDF - Fallback.asset`, `Roboto-Bold SDF.asset`,
  `Assets/UI/TerminalFont.asset` atlas değişiklikleri.
- **Build ve online test:**
  - `Builds/LinuxTest/`, development, Lobby + race.
  - Host: `-autoHost -autoOnline -autoStart 2 -autoRounds 1 -autoDrive -autoTrace -autoShot`.
  - İstemci: `-autoJoin kod -autoOnline -autoDrive`.
  - Davet kodu dosyası: `~/.config/unity3d/DefaultCompany/Harvester3D/autotest-code.txt`.
  - Pencereli başlat: `-screen-fullscreen 0 -screen-width 1280 -screen-height 720`.
  - Relay sunucusu VDS'te (`188.132.198.43:7778`, `Server/README.md`).
- **Araç testleri:**
  - `carTest` sahnesinde `CarAutoTest` (Shift+F8 ya da `-carTestAll`) 5 arabayı ölçer.
  - `Harvester/Araç/Kimlik Kartından Performans Üret` kimlik kartlarından stat üretir.
  - Katalog `Assets/Cars/ArabaKatalogu.asset`. Sıra = ağ indeksi; **asla değiştirme**, yeni araba sona eklenir.
- **Build'lerde PlayerPrefs** `~/.config/unity3d/unknown/unknown/prefs`'e, editör `DefaultCompany/Harvester3D`'e
  yazıyor. Build kendi içinde tutarlı (garaj ve yarış aynı yeri okur), ama editörde yapılan seçim build'e gitmez.
  Sebebi araştırılmadı; düşük öncelik.

## 6. Kullanıcıyla konuşulmadan yapılmayacaklar (bekleyen kararlar)

- **Harvester tehdit/avlama modeli:** kullanıcı test ettikten sonra nasıl olması gerektiğini anlatacak.
  **`Assets/scripts/Race/Harvester/` davranışına dokunma.** Bilinen sorunlar (bu konuşmaya kaldı):
  - kalkış atağı ilk ~6 sn'de öndeki arabayı biçebiliyor;
  - yarış bitince harvester durup arabalar ona çarpıyor;
  - gazı köklüyen oyuncu kaçabiliyordu (yarım kalan "zaman açığı bandı" modeli).
  - Harvester'ın boyutu (gövde ~8 m) **böyle kalacak** (kullanıcı kararı).
- **Araba–araba çarpışması:** kullanıcı arkadaşlarıyla oynayıp geri bildirim verecek. `CarContactSolver`'a
  dokunma.
- Kamera uyarısı "Screen position out of view frustum" (ertelendi).
- Windows build şimdilik gerekmiyor.
- VDS root şifresinin değiştirilmesi kullanıcıya önerildi.

## 7. Kullanıcı testi için hazırlanacak liste (optimizasyondan sonra)

1. Ana menü → Garaj: araba satın al, parça ve renk değiştir, performans geliştir, kaydet.
2. Oyna → Çevrimiçi → Oda kur, ikinci kopya kodla katılsın, yarışı başlat.
3. Yarışta:
   - seçilen araba, parça ve renk iki tarafta da doğru mu;
   - C ile 4 kamera, kokpit;
   - M ile ayna;
   - F3 ile ağ bilgisi.
4. Görünüm pikselli (Lethal) mi; Ayarlar'da kalite seviyeleri.
5. Intel GPU ile (NVIDIA değişkenleri olmadan) bir yarış: akıcılık.
