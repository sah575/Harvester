# Araba modeli spesifikasyonu (Harvester)

Sürüm 1.1 · 13 Kasım 2026 · Referans araba: `Assets/Cars/RacerCoupe_01/`

Bu belge oyun kodunun (`Assets/scripts/Race/Car/`: `CarDefinition`, `CarVisual`, `RaceCar`) bir arabadan
beklediği her şeyi tanımlar. "Zorunlu" yazan kurallara uyulmazsa araba oyunda bozuk çalışır.
"Önerilen" kurallar kaliteyi ve performansı etkiler.

---

## 1. Koordinatlar, ölçek ve köken (zorunlu)

Unity'de, FBX içe aktarıldıktan sonra:

| Kural | Değer |
|---|---|
| Birim | metre, gerçek ölçek (Unity'de ölçek 1, `useFileScale` açık, `globalScale` 1) |
| Ön | **+Z**: ön tampon ve ön tekerlekler pozitif Z'de |
| Üst / sağ | +Y / +X |
| Kök dönüşü ve ölçeği | (0, 0, 0) ve (1, 1, 1). Hiçbir alt objede düzeltme amaçlı 90°/180° dönüş kalmamalı. |
| Köken (kök objenin pivotu) | x = 0 (simetri ekseni), y = 0 (lastiklerin yere değdiği düzlem), z = ön ve arka aksın tam ortası (±5 cm) |
| Tekerlek altı | 4 tekerleğin en alt noktası y = 0.000 (±5 mm) |

Blender'da modelledikten sonra **bütün objelere Apply Transform** (Ctrl+A → All Transforms) uygula.
Ebeveyn bağlarken "Object (Keep Transform)" kullan. RacerCoupe_01'de çalışan dışa aktarma ayarlarını
değiştirme. Kontrol her zaman Unity'deki sonuca göre yapılır: kök (0,0,0) iken ön tampon +Z'de olmalı.

## 2. Ölçüler (zorunlu aralıklar)

| | Alt | Üst | Not |
|---|---|---|---|
| Boy | 3.8 m | 5.0 m | Tamponlar dahil |
| Gövde eni | 1.70 m | 2.05 m | Aynalar hariç |
| En, aynalar dahil | — | 2.25 m | |
| Yükseklik | 1.00 m | 1.70 m | Sınıfa göre: supercar alçak, pickup/rally yüksek |
| Yerden yükseklik (şasi altı) | 0.08 m | 0.30 m | |
| Tekerlek yarıçapı | 0.30 m | 0.45 m | Ön ve arka farklı olabilir |
| Tekerlek genişliği | 0.20 m | 0.40 m | |
| Dingil mesafesi | 2.2 m | 3.2 m | |

**Açık tekerlek yasak (F1/formula değil).** Tekerlekler gövdenin içinde ya da çamurluk altında kalmalı.
Tekerleğin dış yüzü gövde kenarını en fazla 3 cm geçebilir, çünkü araba–araba çarpışma kutusu gövde
genişliğinden hesaplanıyor.

## 3. Zorunlu hiyerarşi ve adlar

Bütün adlar küçük harf, `snake_case` ya da aşağıdaki tireli biçimde, **tam olarak** yazıldığı gibi. Hepsi
kök objenin doğrudan çocuğu, aksi belirtilmedikçe.

### 3.1 Tekerlekler
- `wheel-front-left`, `wheel-front-right`, `wheel-back-left`, `wheel-back-right`
- Pivot **tekerlek merkezinde**; tekerlek yerel **X ekseni** etrafında döner (lastik ekseni X).
  Mesh sınırlarının merkezi pivotla aynı (±2 mm).
- Lastik mesh'i tekerlek objesinin kendisi.
- Her tekerleğin altındaki alt objeler, yerel konumları ve dönüşleri **(0,0,0)**:
  - `wheel-<konum>_rim_<sıra>_<ad>`: jant varyantları. Sıra 0 stok. Dört tekerlekte aynı sıra = aynı jant.
    (ör. `wheel-front-left_rim_0_sport`)
  - `wheel-<konum>_tire_lettering`: lastik yazısı (isteğe bağlı, varsayılan kapalı).
- **Kaliperler tekerleğin çocuğu OLMAZ** (tekerlek döner, kaliper dönmemeli). Kökün çocuğu:
  `caliper-front-left`, `caliper-front-right`, `caliper-back-left`, `caliper-back-right`.
  Pivotları ilgili tekerleğin merkezinde, dönüş (0,0,0).

### 3.2 Gövde parçaları (parçalanma için ayrı objeler)
Araba biçilince bu parçalar ayrı ayrı uçar.

Zorunlu parçalar:
- `chassis_underbody`
- `door_left`, `door_right`
- `fender_front_left`, `fender_front_right`, `fender_rear_left`, `fender_rear_right`
- `roof_cabin`
- `engine_decklid` (arka kapak) ya da motor öndeyse yine bu ad
- tamponlar, kaput, rüzgârlık slot varyantı olarak gelir (bkz. 4)

Kurallar:
- Her parça **kapalı hacim** (içi görünmeyen; ince tek yüzlü levha değil), dışa bakan normaller.
- Pivot parçanın kendi merkezine yakın. Kapılarda menteşe hattı olabilir.
- Parçalar birbirinin içine fazla girmesin. Birleşim yerlerinde 1–2 cm bindirme yeterli.

### 3.3 Camlar ve iç mekân
- `glass_windshield`, `glass_rear`, `glass_side_left`, `glass_side_right`: normaller **dışa** bakar,
  boşluğu tam kapatır.
- İç mekân (kokpit kamerası kullanılıyor): `interior_cabin` (taban, kapı içleri, tavan döşemesi, sütunlar),
  `interior_dash` (torpido ve gösterge), `interior_driver_seat`.
  - Kabin içeriden bakınca **kapalı**: delik, eksik yüz, dışarıdan görünen boşluk olmasın.
  - Direksiyon `mod_steering_*` slotunda.

### 3.4 Boş noktalar (Empty; mesh yok)
| Ad | Konum | Yön (objenin yerel +Z'si) |
|---|---|---|
| `light_head_L`, `light_head_R` | Far lensinin 2–5 cm **önünde**, lens merkezi hizasında | +Z (ileri) |
| `light_tail_L`, `light_tail_R` | Stop lambasının 2–5 cm arkasında | −Z (geri) |
| `camera_cockpit` | Sürücü koltuğunun üstünde, göz hizası (koltuk oturma yüzeyinden ~0.55–0.65 m yukarı), direksiyonun ~0.45 m arkası | +Z |
| `exhaust_L`, `exhaust_R` *(önerilen)* | Egzoz ucunun ağzında | −Z |

Sürücü sol tarafta (x < 0).

## 4. Modifiye slotları ve varyantlar (zorunlu)

Ad biçimi: **`mod_<slot>_<sıra>_<ad>`** (ör. `mod_spoiler_4_swanneck`).
- `<slot>`: küçük harf, alt çizgili; aşağıdaki standart listeden.
- `<sıra>`: 0'dan başlar, aralıksız. **0 = stok parça.**
- `<ad>`: küçük harf, kısa, İngilizce ya da Türkçe; marka yok.
- Mesh adı obje adı + `_mesh` olabilir.

Varyant kuralları:
- Bir slotun **bütün varyantları aynı pivotta**: aynı yerel konum, dönüş 0.
- Dışa aktarılan dosyada **her slotta sadece sıra 0 görünür**, diğerleri gizli. Oyun görünürlüğü zaten
  kendisi ayarlar, ama dosya da bu halde olsun.
- "Yok" seçeneği olan slotlarda sıra 0 `..._0_none` adlı **mesh'siz boş obje**. Yer tutucu kutu koyma.
- Çift taraflı slotlar `_left` / `_right` ile biter ve birlikte değişir (ör. `mod_skirts_2_track_left`,
  `mod_skirts_2_track_right`).
- **Varyantlar tekerlek konumunu, aks yüksekliğini ve yerden yüksekliği değiştirmez.** Geniş kasa
  çamurlukları tekerleğin üstünü örter ama tekerlek yerinde kalır.
- **Ön tekerleklerin dönme alanını boş bırak:** ön tekerlekler ±35° döner. Çamurluk, tampon ve etek
  varyantlarının iç yüzleri dönen lastiğe 3 cm'den fazla yaklaşmasın.
- Her varyant tek başına makul görünmeli ve diğer slotların varsayılanlarıyla birleşince delik ya da
  çakışma bırakmamalı.

Standart slot listesi (yeni bir arabada hepsi zorunlu değil; olanlar bu adları kullanmalı):

| Slot | Ne | Tipik varyant sayısı |
|---|---|---|
| `bumper_front` | Ön tampon | 3–5 |
| `bumper_rear` | Arka tampon / difüzör | 3–4 |
| `skirts` | Marşpiyel (`_left`/`_right` çifti) | 3–4 |
| `fender` | Çamurluk kaplaması / geniş kasa | 2–3 |
| `hood` | Kaput | 3–5 |
| `spoiler` | Rüzgârlık (0 = stok ya da `0_none`) | 4–6 |
| `exhaust` | Egzoz uçları | 3–4 |
| `headlight` | Far görünümü | 2–4 |
| `taillight` | Stop görünümü | 2–3 |
| `mirror` | Yan aynalar | 2–3 |
| `roof_scoop` | Tavan hava alığı (0 = `0_none`) | 2–4 |
| `rollcage` | Rollbar (0 = `0_none`) | 2–3 |
| `neon` | Alt neon (0 = `0_none`) | 2–3 |
| `steering` | Direksiyon | 2–3 |
| `interior_passenger` | Yolcu koltuğu / nitro tüpü | 2 |
| `interior_pillar` | Sütun göstergeleri | 2 |
| `frunk` | Ön bagaj içi | 2 |

Yeni bir slot gerekirse önce kullanıcıya sor. Eklenirse adı bu biçimde olsun ve raporda belirtilsin.

**Çıkartmalar:** `decal_<sıra>_<ad>` (ör. `decal_2_side_rocker`). Sıra 1'den başlar, hepsi varsayılan gizli.
Gövde yüzeyinin 2–5 mm üstünde, `Decal_Graphic` malzemesiyle. Yazı, logo ve sponsor adı yok; soyut
şekiller ve numara serbest.

## 5. Malzemeler (zorunlu)

Oyuncu renkleri oyunda değiştiriyor. **Her renk seçeneği için ayrı malzeme YAPMA.** Sadece şu adlar
kullanılır (araba klasöründe `Materials/` altında, aynı adlarla):

**Boyanabilir (düz renk, doku yok; renk oyunda değiştirilir):**
- `Paint_Body`: gövde
- `Paint_Rim`: jant
- `Paint_Caliper`: kaliper
- `Paint_Accent`: şerit ve aksesuar vurgusu
- `Neon`: alt neon, emisyonlu
- `Glass_Tint`: cam, şeffaf

**Sabit:**
- `Trim_Dark`: plastik trim, lastik, ızgara
- `Carbon`
- `Metal_Chrome`
- `Interior_Dark`
- `Light_Head`: far lensi, emisyonlu
- `Light_Tail`: stop lambası, emisyonlu
- `Decal_Graphic`

Kurallar:
- Hepsi **URP Lit**. Doku kullanma; düz renk (decal için ileride bir atlas gelebilir, şimdilik düz renk).
- **Emisyonlu malzemeler** (`Neon`, `Light_Head`, `Light_Tail`):
  - `.mat` dosyasında `m_LightmapFlags: 2` (RealtimeEmissive; **4 olursa emisyon kapanır**);
  - `m_ValidKeywords` içinde `_EMISSION`;
  - `_EmissionColor` 0'dan büyük.
- **`Glass_Tint`:** Surface = Transparent, `_Cull = 0` (çift yönlü), alfa ~0.35–0.5, ZWrite kapalı.
  Cam üçgenlerinin normalleri yine de **dışa** baksın.
- Bir objede en fazla **2** malzeme yuvası (alt mesh). Mümkünse 1.
- **İsteğe bağlı sabit malzeme:** `Screen`, torpido ve gösterge ekranları için, emisyonlu.
  Ekranlarda `Neon` kullanma: oyuncu neon rengini değiştirince ekran da değişir ve kokpitte göz alır.
- Listede olmayan başka bir malzeme gerekirse önce kullanıcıya sor.

**`.mat` dosyalarını elle yazıyorsan (YAML):**
- **Emisyonlu malzemede `m_LightmapFlags` asla 4 olmaz:** 2 (RealtimeEmissive) ya da 1. URP malzemeyi yüklerken
  anahtar kelimeleri bu bayraktan yeniden hesaplar; 4 ise `_EMISSION`'ı sessizce siler, malzemeyi kirli bırakır
  ve editör kaydederse diskten de gider. Emisyonsuz malzemelerde 4 sorun değil.
- `m_ValidKeywords` içinde `_EMISSION`, `_EmissionColor` > 0.
- **Şeffaf malzeme:** `_Surface: 1`, `_ZWrite: 0`, `RenderType: Transparent`, kuyruk 3000. Çift yüzlüyse `_Cull: 0`.
- **Teslim testi:** malzeme Unity'de açılınca kirli (dirty) oluyorsa yazdığın değer URP'nin beklediğiyle çelişiyor;
  düzelt.

## 6. Poligon bütçesi (zorunlu üst sınırlar)

| | Hedef | Üst sınır |
|---|---|---|
| Stok araba (her slotta sıra 0 + jant 0, görünür olan her şey) | 2 000–3 000 üçgen | 5 000 |
| Tek bir varyant | ≤ 250 | 400 |
| Tekerlek başına (lastik + aktif jant) | ≤ 250 | 350 |
| Bütün dosya (bütün varyantlar dahil) | ≤ 9 000 | 12 000 |

Görünmeyen yüzleri (iç içe kalan, alttan hiç görülmeyen) sil.

## 7. Stil (önerilen, ama oyunun görünümü buna bağlı)

- Oyun düşük çözünürlükte (yarım çözünürlük) çiziliyor, üstüne posterize, dither ve kenar çizgisi
  uygulanıyor. Gece ve sisli.
- **Low-poly, düz renkli, tok ve okunur formlar.** 3 cm'den küçük detaylar ekranda kaybolur; onlara üçgen
  harcama.
- Silüet her arabada ayırt edici olsun: farklı burun, tavan hattı, arka.
- Işıklar (far, stop, neon) gece pistinde arabayı tanıtır. Far ve stop şekilleri belirgin ve arabaya özgü
  olsun.
- Gerçek bir arabanın birebir kopyası **yasak**. İlham alınabilir, ama oran, ızgara, far ve stop şekilleri
  belirgin şekilde farklı olmalı.

**Yasak kelimeler** (dosya, obje, mesh, malzeme adlarında ve çıkartmalarda). Marka ve model adları kadar
**yarış serisi ve donanım sınıfı adları** da yasak (Cup, GT3, RS, Turbo S, Type R, Nismo, AMG, M Sport, SVR,
Trackhawk, Hellcat…). Varyant adları sadece genel tanımlayıcılar olsun: stock, street, track, race, gtwing,
ducktail, timeattack, widebody… Marka ve model adları: (Porsche,
911, GT3, RS, Cup, Ferrari, Lamborghini, Nissan, GT-R, Skyline, Supra, Toyota, Honda, NSX, BMW, M3,
Mercedes, AMG, Audi, Ford, Mustang, Chevrolet, Camaro, Corvette, Dodge, Mazda, RX-7, Subaru, Mitsubishi,
Lancer…), parça markaları (BBS, Brembo, Recaro, Sparco, OZ, HKS, Pirelli, Michelin…) ve oyun adları
(NFS, NFSU, Forza, Gran Turismo…).

## 8. Kimlik kartı: `<Ad>_kimlik.json` (zorunlu)

Oyunun performans değerleri Unity'de ayarlanır. Modelci arabanın karakterini bu dosyayla önerir:

```json
{
  "id": "MuscleV8_01",
  "gorunenAd": "V8 Kas",
  "sinif": "muscle",
  "cekis": "RWD",
  "tahminiKutleKg": 1450,
  "karakter": { "sonHiz": 4, "ivme": 5, "yolTutus": 2, "fren": 3, "dayaniklilik": 4 },
  "aciklama": "Düzlükte çok hızlı, virajda ağır; drift'e yatkın.",
  "slotlar": { "spoiler": ["stok", "ördek kuyruğu", "yüksek kanat"], "...": [] },
  "jantlar": ["spor", "5 kol", "türbin", "file"]
}
```

- `karakter` değerleri 1–5.
- `slotlar` ve `jantlar`: her varyantın oyunda görünecek Türkçe adı, sıra numarasıyla aynı sırada.
- `sinif`: `coupe`, `muscle`, `hatchback`, `supercar`, `rally`, `pickup`, `sedan`, `van` ya da yeni bir tür
  (raporda açıkla).

## 9. Teslim

```
Assets/Cars/<Ad>/
├── <Ad>.fbx
├── <Ad>_kimlik.json
└── Materials/          (sadece bölüm 5'teki adlar)
BlenderSources/cars/<Ad>.py   (modeli baştan üreten Blender betiği)
```

- Prefab yapmak zorunlu değil. Oyun kendi prefab'ını kurar, senin prefab'ına güvenmez.
- Unity'de **sadece**:
  - asset'leri içe aktar;
  - FBX içe aktarma ayarlarında ölçek 1 ve `useFileScale` açık olsun;
  - kontrol için oku.
- Yasak: sahne açmak/kaydetmek, play mode, build, `.cs` dosyası yazmak, `Assets/Cars/<Ad>/` dışındaki bir
  şeyi değiştirmek. Proje editörünü aynı anda başka bir ajan kullanıyor.

## 10. Kontroller

### 10.1 Blender'da (dışa aktarmadan önce)
[`../scripts/blender_kontrol.py`](../scripts/blender_kontrol.py) betiğini çalıştır. Bütün satırlar GEÇTİ olmalı.

### 10.2 Unity teslim kontrolü
Bütün maddeleri ölçerek doğrula ve raporda göster:
1. Kök dönüşü (0,0,0), ölçek 1. Ön tampon ve ön tekerlekler +Z'de.
2. 4 tekerlek adı tam; tekerlek merkezleri, yarıçapları; tekerlek altı y = 0.000.
3. Jant ve lastik yazısı alt objelerinin yerel konum ve dönüşü (0,0,0). Kaliperler kökte, tekerlek merkezlerinde.
4. Dosyadaki görünür halde her slotta tek varyant (sıra 0) var; `_0_none`'lar mesh'siz.
5. Aynı slotta bütün varyantların pivotu aynı.
6. Malzeme listesi bölüm 5 ile aynı. Emisyonlu üç malzemede `m_LightmapFlags: 2` ve `_EMISSION`; Glass_Tint şeffaf ve `_Cull = 0`.
7. Obje başına en fazla 2 alt mesh. Üçgen sayıları bölüm 6'nın sınırları içinde.
8. Boş noktalar: adları, konumları, yönleri; far noktası lensin önünde.
9. Ölçüler bölüm 2'nin aralıklarında; tekerlekler gövde kenarını 3 cm'den fazla geçmiyor.
10. Adlarda yasak kelime yok.
11. Görsel kontrol: önden, arkadan, yandan, üstten, alttan ve `camera_cockpit`'ten görüntü al ve bak.
    - Havada asılı ya da yerin altında parça yok.
    - Delik ya da ters yüz yok.
    - Farlar ve stoplar önden ve arkadan görünüyor.
    - Kabin kapalı.

### 10.3 Kendi kendine testler (Blender'da, teslimden önce)
- **Dönme testi:** her tekerleği yerel X'te 90° döndür. Tekerlekle birlikte dönmemesi gereken bir şey (kaliper,
  fren kapağı, çamurluk içi) dönüyorsa hiyerarşi yanlış. Sonra dönüşü geri al.
- **Normaller:** bütün objelerde Recalculate Outside. Face Orientation görünümünde her yüz mavi (dışa) olmalı;
  özellikle tek yüzlü düzlemler (cam, çıkartma, neon şeridi). Cull kapalı malzeme ters normali gizler ama
  ışığı bozar.
- **Işık testi:** tam önden ve tam arkadan, göz hizasından ortografik render. Farlar ve stoplar açıkça
  görünmeli. Işık boş noktaları hiçbir mesh'in içinde olmamalı.
- **Render:** 6 açı (ön, arka, yan, üst, alt, kokpit) + tekerlek dönmüş halde bir render.
- Bütün ölçümleri teslim notuna sayılarla yaz: iddia ve ölçüm aynı tabloda olursa doğrulama hızlanır.

### 10.4 Unity'de render ile kontrol yapan için
- Prefab asset'inde `activeInHierarchy` güvenilmez; ölçümleri `EditorSceneManager.NewPreviewScene()` içindeki
  örnekte yap.
- Emisyonlu malzemeleri render ederken önce birkaç ısınma render'ı al. Yoksa ilk karede `_EMISSION` varyantı
  derlenmemiş olur ve malzeme hiç çizilmez.
- Malzemeyi hem diskteki YAML'da hem editördeki yüklü kopyada kontrol et (`IsKeywordEnabled("_EMISSION")`,
  `EditorUtility.IsDirty`).

### Sık yapılan hatalar (önceki teslimlerden)
- Jant/yazı alt objelerinde uygulanmamış eksen dönüşümü: parçalar tekerlekten metrelerce uzakta çıkar.
  Çözüm: Apply Transform + Keep Transform.
- Kökte kalan 180° dönüş: araba ters gider.
- Kaliperi tekerleğin çocuğu yapmak: kaliper tekerlekle döner.
- Emisyon malzemesinde `m_LightmapFlags: 4`: ışıklar parlamaz.
- Far lensinin gövdenin içinde kalması: önden görünmez.
- Cam normallerinin içe bakması.
- "none" varyantına yer tutucu kutu koymak (z-fighting).
- "Cup" gibi seri ya da sınıf adlarını varyant adında kullanmak.
- Torpido ekranında `Neon` malzemesi kullanmak (neon rengiyle birlikte değişir).

## 11. Rapor şablonu

```
Araba: <Ad>  (sınıf, kısa tasarım açıklaması)
Dosyalar: ...
Ölçüler: boy × en (aynasız/aynalı) × yükseklik, yerden yükseklik, dingil, iz (ön/arka)
Tekerlekler: merkezler, yarıçaplar, genişlikler
Üçgen: stok N, en büyük varyant N, tekerlek N, toplam N
Slotlar: slot → [sıra_ad, ...]  (dosyada görünür olan: 0)
Jantlar: [...]
Boş noktalar: ad → konum, yön
Malzemeler: liste + emisyon/cam ayarları
Kontrol: 10.2'deki 11 madde → GEÇTİ/KALDI
Bilinen sınırlamalar / açık sorular: ...
```
