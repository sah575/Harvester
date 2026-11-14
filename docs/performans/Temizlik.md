# Eski oyun temizliği — doğrulama

13 Kasım 2026; yaris-entegrasyon. Yarım bırakılmış silmeler ve yarışa özgü lobi/test entegrasyonu tamamlandı.

- Eski saklambaç sahneleri, robotlar, mısır/arazi kodu ve eski kurucular kaldırıldı.
- Harvester görseli Models/Harvester altında; taşınan 8 asset GUID'si korundu. Silinmiş 76 GUID için kalan seri dosyalarda referans taraması: 0 eşleşme.
- HarvesterGorsel: yalnız Transform/MeshFilter/MeshRenderer. Yeniden kurulan HarvesterAI'nın 19 renderer'ı için mesh/malzeme/bounds imzası öncekiyle birebir aynı. Disk ve editör PrefabIdHash: 2234473262.
- DefaultNetworkPrefabs yalnız RaceCar ve HarvesterAI içeriyor; build sahneleri yalnız Lobby/race.
- Lobi rol satırı kaldırıldı, bütün menü panelleri LobbyUIBuilder ile yeniden üretildi. Approval, Relay/LAN, isim, oda parolası ve tur dönüşü korunuyor. Development log'u oda dönüşünü otomatik doğrulayabiliyor.
- GameManager.InputAllowed bağı kaldırıldı; yarışın kendi InputEnabled kilidi sürüyor. -ntInterp ve -ntBuffer RaceAutoTest'e taşındı.
- Timeline, Visual Scripting ve Collab paketleri kaldırılmış manifest ile tam Unity derlemesi hatasız.
- Harvester/Rebuild Harvester AI ve Rebuild Lobby UI uygulandı. Harvester/Doğrulama/Yarış Projesi menüsü prefab bağımlılıklarını ve Lobby/race/carTest sahnelerinin kayıp bileşen/referanslarını doğruladı.
- Yeni Linux development Mono build: 258,38 MB; 15,12 sn; 0 hata/0 uyarı.
- İki gerçek player: Relay davet kodu, iki yarışçı + harvester, yarış başlatma, test elemesi, yarış bitişi ve her iki tarafta bağlantı açıkken Lobby/oda dönüşü geçti. Gerçek GPU: Mesa Intel Iris Xe (ADL GT2). NetworkConfig uyuşmazlığı yok. [Makine sonucu](temizlik-online.json).
- LethalLook, FoggySkybox, NightSky/NightSkyFog/HDRI, Volume profilleri ve ayna renderer'ı korundu. [Önce](gorseller/temizlik-once.png) / [sonra](gorseller/temizlik-sonra.png) ekran görüntüleri ayrı koşu anlarıdır; piksel farkı testi değildir. Render ölçeği hatası henüz bu commit'te düzeltilmedi; P1'de ölçülecek.

6. bölümde ertelenen Harvester davranışı/boyutu, CarContactSolver ve kamera frustum uyarısı değiştirilmedi. Race sahnesini kaydetmenin eklediği çarpışma varsayılanlarının seri yazımı kapsam dışında olduğu için geri alındı.

Tekrar: `python3 tools/run_race_checks.py --mode online --label temizlik-online`. Player günlükleri Builds/reports altında, git dışındadır. Yerel CLAUDE.md güncellendi ve git dışında bırakıldı. TMP atlas gürültüsü geri alındı.
