# Ajan koordinasyonu: roller, sahiplik, editör kilidi

14 Kasım 2026. Üç ajan aynı proje klasöründe ve **tek Unity editöründe** çalışır. Bu belge bağlayıcıdır; buradaki kurala uymayan iş kabul edilmez ve geri alınır. Yönetici Claude'dur, son söz kullanıcınındır.

## 1. Roller

| Ajan | Kim | Görev alanı |
|---|---|---|
| `claude` | Claude Code | **Yönetici.** Görev dağıtır, teslimleri inceler, entegre eder, commit/push yapar, build alır. Geliştirme: ağ, senkronizasyon, sunucu (VDS relay), yarış akışı, araba fiziği, harvester yapay zekâsı, optimizasyon. |
| `codex` | Codex | **Arayüz.** Menüler, lobi/oda ekranları, garaj, dükkân, ayarlar, HUD görünümü, menü önizlemeleri. |
| `agy` | Antigravity | **Modeller.** Blender'da araba (ve ileride harvester/silah) modelleri, malzemeler, kimlik kartları. Kural kitabı: `.agents/skills/arac-modelleme/`. |
| `kullanici` | Kullanıcı | Tasarım ve görünüş kararları, oyun testleri. Kilidi her zaman zorla alabilir. |

## 2. Dosya sahipliği

Sadece kendi satırındaki yerlere yaz. Başkasının dosyasına ihtiyacın varsa **dokunma**, sahibine mesaj yaz (`koord.py yaz`). Tabloda olmayan bir yere yazman gerekiyorsa önce `claude`'a sor.

| Ajan | Yazabileceği yerler |
|---|---|
| `claude` | `Assets/scripts/Race/**`, `Assets/scripts/Net/**`, `Assets/scripts/*.cs` (lobby, MatchSettings, NetworkManagerGuard), `Assets/scripts/Editor/**` (aşağıda başkasına verilenler hariç), `Assets/Prefabs/race/**`, `Assets/Scenes/race.unity`, `Assets/Scenes/carTest.unity`, `Assets/Cars/ArabaKatalogu.asset`, `Assets/Models/Harvester/**` (Unity tarafı), `Assets/Settings/**`, `ProjectSettings/**`, `Packages/**`, `Server/**`, `tools/**`, `docs/**`, `AGENTS.md` |
| `codex` | `Assets/scripts/UI/**`, `Assets/UI/**` (TMP atlas gürültüsü hariç), `Assets/Scenes/Lobby.unity` (yalnız kurucu menüyle), `Assets/scripts/Editor/LobbyUIBuilder.cs`, `LobbyMenuBuilder.cs`, `LobbyUIValidation.cs`, `MenuCarRegistryBuilder.cs`, `SupercarGarageBuilder.cs`, `Assets/UI/TeslimNotlari.md` |
| `agy` | `BlenderSources/**`, `Assets/Cars/<AracAdi>/**` (FBX, Materials, Textures, `_kimlik.json`, `_Tanim.asset`), `.agents/skills/arac-modelleme/**` |

**Herkes için yasak:**
- `Assets/Cars/ArabaKatalogu.asset` sırası ağ indeksidir; sadece `claude` sona ekler.
- `RaceCar.prefab` ve `HarvesterAI.prefab` (ağ prefab'ları): sadece `claude`.
- LethalLook, FoggySkybox, NightSky, Volume profilleri, dikiz aynası renderer'ı, URP renderScale 0.5: kimse değiştirmez.
- `CarContactSolver`, harvester boyutu ve spot menzili, VDS ayarları: kullanıcı kararı olmadan kimse.
- Yerel `CLAUDE.md`, `.vscode/`, `Harvester.slnx`, `Assets/UserChoices.choices*`: dokunma, commit etme.

## 3. Git

- Tek dal: **`yaris-entegrasyon`**. Dal değiştirme yok.
- **Commit ve push'u sadece `claude` yapar.** `codex` ve `agy` git'e yazmaz: `commit`, `push`, `checkout`, `switch`, `reset`, `restore`, `stash`, `clean`, `rebase`, `merge` yasak. `status`, `diff`, `log` serbest.
- İş bitince `koord.py teslim` ile bildir: ne yaptın, hangi dosyalar, nasıl test ettin, bilinen sorunlar. `claude` inceler; uygunsa commit'ler, değilse mesajla geri gönderir.
- `main`'e merge sadece kullanıcı onayıyla.

## 4. Editör kilidi (en önemli kural)

Tek Unity editörü var. Aşağıdakileri yapmadan önce **kilidi al**, bitince **bırak**:

- play mode (oyunu çalıştırmak, otomatik testler),
- build,
- sahne açma/kaydetme, kurucu menüleri çalıştırma (`Harvester/...`),
- Unity'de render/ekran görüntüsü alma (önizleme, kamera),
- `Assets/` altına FBX/doku dışa aktarmak (Unity yeniden içe aktarır),
- `refresh_unity` (derleme). Kilit başkasındaysa derlemeyi tetikleme; kodunu yazıp bekle.

Kilit gerektirmeyenler: kendi dosyalarında kod yazmak, Blender'da çalışmak (Unity'ye dışa aktarmadan), belge okumak.

```bash
python3 tools/koord.py oku    <ajan>                    # işe başlarken ve her adımda: mesajların + kilit durumu
python3 tools/koord.py bekle  <ajan> "garaj render" 20  # boşalana kadar bekler, sonra alır (20 dk)
python3 tools/koord.py uzat   <ajan> 15                 # süre yetmiyorsa
python3 tools/koord.py birak  <ajan> "render bitti, editör boş"
python3 tools/koord.py yaz    <ajan> claude "RaceHUD'da X alanı lazım"
python3 tools/koord.py teslim <ajan> "Garaj ekranı: dosyalar ..., test: ..."
python3 tools/koord.py durum
```

- Kilit süresi dolunca (varsayılan 30 dk) kendiliğinden boşalır. Uzun işte `uzat` kullan.
- İşin bitince **hemen** bırak. Kilidi tutup başka işe geçme.
- Kilitteyken play mode'dan çıkmadan, açık sahneyi kaydetmeden bırakma. Editörü bulduğun gibi (play kapalı, `race` ya da `Lobby` sahnesi açık, kaydedilmemiş değişiklik yok) bırak.
- Unity pencere başlığında `[EDİTÖR: ajan — iş]` görünür. Kilit başkasındayken play mode'a girilirse konsolda `[Koordinasyon]` uyarısı çıkar: hemen çık.
- `claude` ve `kullanici` takılan kilidi zorla bırakabilir.
- **Assets/'e dosya yazdıysan kilidi bırakmadan önce Unity'nin içe aktardığını doğrula.** Unity penceresi odakta değilken otomatik yenilemez. `refresh_unity` (ya da Unity'de Assets → Refresh) çalıştır, konsolda hata olmadığına bak. 14 Kasım'da agy'nin FBX'leri içe aktarılmadan kalmıştı; kontrol eden eski modeli gördü.
- **`.meta` dosyası ve GUID'yi elle yazma.** Unity üretsin. Elle yazılmış GUID'ler (`a1b2c3d4…` gibi) çakışma riski taşır; 14 Kasım'da birini değiştirmek gerekti.

## 5. Çalışma döngüsü

1. Başlarken: `koord.py oku <ajan>`. `claude`'dan gelen görevi oku. Görev yoksa iş uydurma, `claude`'a sor.
2. Kod/model işi: kendi alanında, kilitsiz.
3. Unity'de bir şey denemek gerektiğinde: `bekle` → işi yap → `birak`.
4. Bitince: derlemenin temiz olduğunu doğrula (kilitle), `teslim` yaz.
5. Başka ajanın işine bağlıysan: `yaz` ile iste, cevabı `oku` ile takip et; beklerken kendi alanında başka iş yoksa dur.

## 6. Ortak kurallar

- Kod yorumları ve oyundaki bütün metinler Türkçe. Eski Input Manager.
- Sahne objeleri elle değil kurucu menülerle üretilir.
- Tasarım/görünüş kararı kullanıcınındır. Emin değilsen sor, kendi zevkine göre değiştirme.
- Kendi kodunu her zaman derlenir halde bırak. Başkasının alanındaki derleme hatasını düzeltme, sahibine ve `claude`'a yaz.
