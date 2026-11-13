---
name: arac-modelleme
description: >-
  Harvester yarış oyunu için Blender'da yeni bir araba modeli üretip Unity projesine teslim eder.
  Kullanıcı yeni bir araba modeli, araba varyantı, modifiye parçası ya da var olan bir arabada düzeltme
  istediğinde bu beceriyi kullan. Ölçek, eksen, adlandırma, malzeme, modifiye slotları, ışık/kamera
  noktaları, poligon bütçesi ve teslim kontrolü kurallarını içerir.
---

# Araba modelleme (Harvester)

Harvester, Unity 6 (URP) ile yapılan online bir arcade yarış oyunu. Oyuncular arabalarını hem performans
hem görünüş olarak modifiye ediyor. Arkadan gelen bir biçerdöver arabaları biçip parçalarına ayırıyor.
Oyunun kodu modeli **adlarından** tanır: tekerlekleri, modifiye slotlarını, boyanabilir yüzeyleri,
far ve kamera noktalarını kendisi bulur. Yani bu kurallar zevk meselesi değil, sözleşmedir. Bir ad ya da
eksen yanlışsa araba oyunda bozuk çalışır.

**Bütün kurallar:** [references/spesifikasyon.md](./references/spesifikasyon.md). Yeni bir arabaya başlamadan önce
baştan sona oku. Referans araba `Assets/Cars/RacerCoupe_01/`: kurallara uyduğu doğrulandı, şüphede ona bak.

## Akış

1. **Ad ve kimlik.** Kullanıcının verdiği adı `PascalCase_NN` biçimine getir (ör. `MuscleV8_01`).
   Gerçek marka, model ya da oyun adı kullanma (yasak kelime listesi spesifikasyonda). Sınıfı belirle
   (coupe, muscle, hatchback, supercar, rally, pickup…) ve özgün bir silüet tasarla.
2. **Modelle.** Spesifikasyondaki zorunlu hiyerarşi, adlar, pivotlar, ölçü aralıkları ve poligon bütçesiyle.
   Önce stok araba, sonra her slot için modifiye varyantları.
3. **Malzemeler.** Sadece spesifikasyondaki malzeme adları. Düz renkler; ışık ve neon için emisyon ayarı.
4. **Blender'da kontrol.** [scripts/blender_kontrol.py](./scripts/blender_kontrol.py) betiğini Blender'da çalıştır
   (araba kök objesi seçiliyken). Bütün maddeler GEÇTİ olmadan dışa aktarma.
5. **Dışa aktar ve teslim et.**
   - `Assets/Cars/<Ad>/<Ad>.fbx`, `Assets/Cars/<Ad>/Materials/`, `Assets/Cars/<Ad>/<Ad>_kimlik.json`.
   - Üretim betiğini `BlenderSources/cars/<Ad>.py` olarak projeye kaydet (aynı modeli yeniden üretebilmek için).
6. **Unity'de kontrol.** Spesifikasyondaki "Unity teslim kontrolü" listesini uygula ve raporla.
   - Unity'de sadece asset içe aktar ve oku.
   - **Sahne açma, play mode'a girme, build alma, kod dosyası yazma, başka klasörlere dokunma.**
     Aynı editörü başka bir ajan kullanıyor.
7. **Rapor.** Spesifikasyonun sonundaki rapor şablonuyla: ölçümler, slot ve varyant listesi, üçgen
   sayıları, bilinen sınırlamalar.

## Var olan bir arabayı düzeltirken

- Sadece istenen şeyi değiştir.
- Adları, pivotları, slot sıralarını ve malzeme adlarını koru; oyun bunlara bağlı.
- Bir varyantın adı değişirse raporda eski → yeni olarak yaz.
