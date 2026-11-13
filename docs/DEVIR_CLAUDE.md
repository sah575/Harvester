# Codex → Claude devir notu — 14 Kasım 2026

Dal: `yaris-entegrasyon`. Main'e merge edilmedi. Önce bu belgeyi, ardından `DEVIR_CODEX.md` ve `YarisMimarisi.md` belgelerini oku. `DEVIR_CODEX.md` ilk devir anını anlatır; aşağıdaki durum daha günceldir.

## Tamamlanan ve push edilen işler

- `fc1d932`: eski saklambaç dosyalarının temizliği. GUID/eksik referans denetimi ve iki player ile Relay üzerinden Lobby → race → bağlantı açık Lobby dönüşü geçti. Harvester görseli ve ağ prefab hash'i korundu.
- `e84cca8`: P0 ölçüm altyapısı, iki GPU başlangıç ölçümleri.
- `b31cbb3`: P1 render ölçeği hatası düzeltildi. Kayıt yokken taban 0,5 ezilmiyor; kaydırıcı tabanın çarpanı, eski kayıtlar taşınıyor.
- `3124cdf`: P2 Düşük / Orta / Yüksek; GPU'ya göre ilk seçim, ay ışığı/farlar piksel ışığı, LethalLook ve ayna renderer'ı korunuyor. Düşük 60 FPS/gölgesiz/ayna ~15 Hz; Orta/Yüksek Forward+.
- `f46acd4`: P3 görüş mesafesi 200/250/300 m; Detay katmanı 9, küçük görseller 120 m'de kırpılır. Ayna 170 m, garaj önizlemesi değişmedi. İlk denemedeki GC artışı giderilip tekrar ölçüldü.

- `ef408a6`: P4 stop point ışıkları kaldırıldı, uzak farlar sönüyor; düşük profilde anlamlı süre kazancı ölçülmedi.
- `c2839f8`: P5 harvester görsel LOD 11.721 / 4.404 üçgen; fizik/hash aynı, üçgen p95 163 bin → 59 bin.
- `a2a0672`: P6 araç malzeme önbelleği ve tekerlek birleştirme; draw medyanı 135 → 123, SetPass 44 → 24, render p95 1,79 → 1,22 ms.
- `0463804`: P7 yalnız genel yerel efekt havuzu hazır. Enkaz kısmı kullanıcı isteğiyle bekliyor. Yarış tüketicisi olmadığı için FPS kazancı değildir.
- `4a9fb3d`: P8 HUD önbelleği; GC medyanı 8.202 → 2.692 B/kare (%67,2 azalma).

- `2bc8032`: P9 uzak zemin/lambalar; 42 yol/bariyer/collider mesh verisi aynı, zemin 73.728 → 27.264 üçgen.
- P10 son build ve iki player Relay testi geçti; Vulkan → OpenGLCore, Mono, post işlem varyant ayıklaması. P10 commit kimliği git geçmişindedir.

Ölçümler: `docs/performans/Olcumler.md`, ayrıntılar `P0.md`–`P10.md`, JSON özetleri `sonuclar/`. Ham CSV ve günlükler `Builds/reports/` altında git dışındadır. OpenGL GPU süresini desteklemiyor: -1/sıfır geçerli örnek, CPU'dan türetilmiş GPU süresi yok. P2'de vSync değiştiğinden RTX hızlanmasının tamamı Forward+ kazancı değildir; Intel'in 16,79 ms p95'i 60 FPS sınırını içerir.

## Kullanıcının son isteği: GEÇİCİ çarpışmasız enkaz testi

**KULLANICI DÜZELTMESİ: Kullanıcının isteğini yerine getiremedim; enkaz/collider konusu çözülmedi.** Kullanıcı oyunda denedi ve yaptığım değişikliğin istediği sonucu sağlamadığını bildirdi. Aşağıda anlatılan teknik uygulama ve bileşen testleri, kullanıcının istediği oyun davranışının sağlandığını kanıtlamaz. Kullanıcı açıkça “şimdilik bununla uğraşma” dedi. Bu konuyu yeniden çözmeye çalışma; P7'nin enkaz fiziği/katman/uyutma değişiklikleri de bekleyecek. Genel havuz altyapısı ve diğer optimizasyonlar ayrı devam edebilir. Test seçeneği kodda duruyor, nihai veya kullanıcı tarafından kabul edilmiş çözüm değildir.

Kullanıcı P3 sonrasında biçilen araçların collider'larının test amacıyla kapatılmasını istedi. Gerekçe: enkaz nedeniyle tekrarlayan kazalar aynı döngüye sokuyor; sürüş testini daha uzun tutmak istiyor. **Bu nihai oyun kuralı değil. Enkaz/çarpışma tasarımına daha sonra kullanıcıyla karar verilecek. Kalıcı çözüm diye yorumlama.**

Ana RaceCar collider'ları elenince zaten kapanıyordu. Açık kalanlar CarVisual.Shatter'ın kopan parçalar ve kasaya, ayrıca eski model parçalanma yolunun sonradan eklediği collider'lardı. Yeni `TestDebrisCollision.Configure` yalnız bu enkaz gövdelerinde collider.enabled=false ve Rigidbody.detectCollisions=false uygular. Görsel parçalanma, yerçekimi ve mevcut temizlenme süresi kalır; parçalar zeminden de geçebilir. Canlı araçların collider'ları değişmez.

- Editör menüsü: **Harvester/Test/Enkaz çarpışmasını kapat (geçici)**. İşaretli = yeni enkazlar çarpışmasız. Kullanıcının testine hazır olması için BU MAKİNEDE editör tercihi açık bırakıldı. Karşılaştırırken yarışı yeniden başlat.
- Tercih anahtarı: `Harvester.Test.EnkazCarpismaYok` (1 açık / 0 kapalı). PlayerPrefs yereldir, git ile diğer makineye taşınmaz.
- Development player: `-testEnkazCarpismaYok`. Online testte her iki player'a da ver; enkaz efektleri yereldir, bu bayrak ağdan dağıtılmaz.
- Runner: `python3 tools/run_race_checks.py --mode online --label enkaz-gecici-online --no-debris-collision`.
- Uzun performans/test koşusu: `python3 tools/run_race_checks.py --mode perf --label enkaz-uzun-intel --gpu intel --seconds 60 --no-debris-collision`.
- Normal runner koşularında varsayılan kapalıdır. Release build'de seçenek daima etkisizdir. Editörde menüyü kapatmak, player'da bayrağı kaldırmak ve varsa test tercihini 0 yapmak normal enkaz çarpışmalarını geri getirir.
- `PerfProbe` JSON'unda `debrisCollisionDisabled` ve runner sonucunda istenen/gerçek durum kayıtlıdır. Bu seçenek açıkken alınmış ölçümleri P0–P3 normal çarpışmalı koşularla eşdeğer sayma.
- Kod: `Assets/scripts/Race/Perf/TestDebrisCollision.cs`, `Assets/scripts/Editor/TestDebrisCollisionMenu.cs`; CarVisual'da iki, RaceCar eski model yolunda bir çağrı. Nihai karar sonrası bu kancalar ve test menüsü kolayca kaldırılabilir.

Doğrulama: Play mode'da katalogdaki beş aracın her biri için 17 enkaz Rigidbody/collider'ı hem açık hem kapalı test seçeneğiyle denetlendi; hepsi geçti. Eski model parçalanma yolu da iki durumda geçti. Ana elenmiş araç collider'ı iki durumda da kapalı. Unity Console hata yok; Linux development build 0 hata / 0 uyarı. İki player Relay testi geçti: seçenek iki tarafta açık, yarış tamamlandı, iki player bağlantı açıkken Lobby'ye döndü; NetworkConfig mismatch yok. Sonuç: `docs/performans/enkaz-gecici-online.json`.

## Kalan işler ve korunacak kararlar

Kullanıcı daha sonra P4–P10'a devam edilmesini istedi. **P4/P5/P6/P8/P9/P10 tamamlandı; P7'nin yalnız genel havuz kısmı tamamlandı. Enkaz/collider kısmı son kullanıcı talimatıyla bekliyor ve önceki istek karşılanmadı.** Sonuç/kanıtlar: `docs/performans/Sonuc.md`, manuel liste: `docs/performans/KullaniciTesti.md`. Son build 0 hata/0 uyarı, son Relay testi geçti. Değişiklikler ayrı büyük adım commit'leriyle push edildi; main'e merge edilmez. Bundan sonraki adım kullanıcı testi ve geri bildirimdir.

`DEVIR_CODEX.md` 6. bölümündeki konulara dokunulmadı: harvester davranışı/boyutu, CarContactSolver, ertelenen frustum uyarısı, Windows build, VDS ayarları. Harvester spot menzili 130 m kaldı. Geçici enkaz isteği, harvester avlama modeli veya canlı araç–araç çarpışmasını değiştirme izni değildir.

Yeni LOD/build kurucuları ve tam dosya listesi Sonuc.md içindedir. Kurucular: `Harvester/Apply Quality Levels`, `Harvester/Apply Visibility Budget`, `Harvester/Doğrulama/Yarış Projesi`. Tek editör kuralı geçerli; build/play/sahne değişimi aynı anda yürütülmez. Script refresh sonrası domain reload'un tamamlanmasını bekle.

TMP TerminalFont/fallback/Roboto atlas gürültülerini commit'leme. Yerel `.vscode/`, `CLAUDE.md`, `Harvester.slnx`, `Assets/UserChoices.choices*`, `agent.txt` ve önceden var olan `Assets/Resources.meta` bu işe dahil edilmedi. Katalog sırası değişmedi. `Assets/Cars/` yeniden üretilmedi.
