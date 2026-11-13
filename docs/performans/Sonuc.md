# Optimizasyon sonucu — 14 Kasım 2026

Dal `yaris-entegrasyon`; main'e merge edilmedi. Temizlik ve P0–P6/P8–P10 tamamlandı. **P7'de genel havuz hazır; enkaz/collider kısmı kullanıcının son talimatıyla bekliyor. Önceki collider isteği karşılanmadı.** Harvester tehdit modeli/boyutu, CarContactSolver, ertelenen frustum konusu, Windows ve VDS ayarlarına dokunulmadı.

P4 öncesi P3 ile son Intel OpenGL koşusu aynı Düşük/1080p/0,5/ayna senaryosunda:

| Ölçüm | Önce | Sonra |
|---|---:|---:|
| Draw medyanı | 135 | 121 |
| SetPass medyanı | 44 | 24 |
| Üçgen p95 | 162.874 | 54.057 |
| Yönetilen tahsis medyanı | 8.202 B/kare | 2.692 B/kare |
| Render iş parçacığı p95 | 1,78 ms | 1,33 ms |
| CPU ana p95 | 5,62 ms | 5,66 ms |
| Kare p95 | 16,79 ms | 16,78 ms |

Bu sonuç GPU çizim yükü ve tahsis azaltımıdır; Intel'de genel FPS/ana CPU hızlanması iddia edilmiyor. Düşük 60 FPS ile sınırlı. Harvester tek başına ~116,7 bin yerine 11.721 / 4.404 üçgen LOD kullanıyor. P6 sırasında render p95 1,79 → 1,22 ms ölçüldü; son koşudaki farklı değer tabloya aynen yansıtıldı. RTX'in son Vulkan Yüksek kare p95'i 2,31 ms; GPU p95'i 0,39 ms. Başlangıç RTX P2 OpenGL p95'i 2,18 ms idi; bütün optimizasyonlar her koşuda toplam kareyi hızlandırdı gibi bir iddia yoktur. Karşılaştırma sınırları [P10](P10.md) ve [bütün ham özetlerde](Olcumler.md) açıklanır.

Doğrulamalar: beş araçta renk/jant/fren, kaynak malzeme değişmezliği, boya önbelleği, tekerlek birleştirme; gerçek garaj RenderTexture görüntüsü; LOD altı hareketli parça eşlemesi; harvester fizik ölçüleri/hash eşitliği; 42 yol/bariyer/collider mesh'inin tam serileştirilmiş veri eşitliği; eksik script/referans denetimi; havuz yaşam döngüsü/mikro ölçümü; iki GPU × iki API 60 aktif saniye; son Linux build 0 hata/0 uyarı; iki player Relay yarış → bağlı Lobby dönüşü.

Görüntüler: [garaj](gorseller/p6-garaj.png), [yarış ve ayna](gorseller/p10-host-hud.png), [kaput](gorseller/p10-host-kaput.png), [sonuçlar ve harvester](gorseller/p10-host-izleyici.png). Geometri inceleme görüntüleri P5 belgesinde ayrı belirtilmiştir.

Kurucular: `Harvester/Apply Quality Levels`, `Harvester/Apply Visibility Budget`, `Harvester/LOD/Harvester Kaynağını Dışa Aktar`, `Harvester/LOD/Harvester LOD Meshlerini Üret`, `Harvester/Rebuild Harvester AI`, `Harvester/Rebuild Race Track`, `Harvester/Apply Build Optimizations`, `Harvester/Build/Linux Vulkan Öncelikli`, `Harvester/Build/Linux OpenGL Öncelikli`, `Harvester/Doğrulama/Yarış Projesi`.

Temel değişen dosyalar: CarVisual/RaceCar görselleri; Race/Perf ölçüm, kalite, görüş, LOD ve kaynak ömrü sınıfları; Race/Pooling; RaceHUD/NetStatsHUD; GaragePreview/MenuSettings; QualityBuilder/VisibilityBuilder/HarvesterLodBuilder/HarvesterAIBuilder/TrackBuilder/BuildOptimizationBuilder; üç kalite pipeline/renderer çifti; harvester LOD FBX ve mesh asset'leri; HarvesterAI.prefab; yarış sahnesindeki üretilmiş zemin/lambalar; QualitySettings/TagManager/ProjectSettings ve URP global shader ayıklama ayarı. [Tam P4–P10 dosya listesi](DegisenDosyalar.txt).

Eksik bir mevcut API gereksinimi yok. Genel havuz gelecekteki yerel efekt tüketicilerine bağlanmayı bekliyor; ağ objesi havuzu değildir. Enkaz/collider davranışı ve harvester avlama tasarımı için kullanıcı kararı bekleniyor. [Manuel kabul testi listesi](KullaniciTesti.md) hazır; bu manuel maddelerin tamamı otomatik test edilmiş değildir. Linux player: `Builds/LinuxTest/Harvester.x86_64`. Sonraki adım kullanıcı testi ve geri bildirimdir.
