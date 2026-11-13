# Yarış modu: mimari ve iş bölümü

12 Kasım 2026. Oyun saklambaçtan yarışa döndü: oyuncular yarış arabalarıyla yarışıyor, arkadan yapay zekânın sürdüğü harvester gelip arabaları biçiyor. Bu belge ilk temelin (pist, araba, ağ çarpışmaları, harvester yapay zekâsı) nasıl bölündüğünü ve parçaların birbirine nasıl bağlandığını anlatır. Parçalar ayrı ajanlarca yazılıyor; bir ajanın sorumluluğu dışındaki dosyaya ihtiyacı varsa yöneticiye bildirir.

## Kararlar

- Pist turlu da olabilir A'dan B'ye de (`RaceTrack.Loop`).
- Biçilen araba elenir. Elenen oyuncu harvester'ı izler. Bu kural geçici, ileride değişebilir.
- Harvester'ı kimse sürmüyor, host'ta çalışan yapay zekâ sürüyor. İleride silahlar gelecek (roket, yağ, makineli). Şimdilik takip, biçme ve çarpma.
- Sürüş arcade: hızlı kalkış, kolay dönüş, drift.
- Araba modeli Kenney Car Kit'ten (CC0); bütün kod bizim.
- Eski saklambaç oyunu (`world` sahnesi, robotlar) şimdilik duruyor, dokunulmaz. Yarış yeni `race` sahnesinde kurulur.

### Harvester kararları (13 Kasım)

- Harvester'ın arkasında kalmanın güvenli olup olmayacağı ayrıca düşünülecek; şimdilik olduğu gibi kalıyor.
- Harvester hata affetmez. Sürüdeki araba–araba çarpışmasından gelen yavaşlama da cezalandırılır.
- Kalkış: harvester yarış başladıktan en geç ~5 sn sonra sürünün dibinde olmalı. Arabaların kalkıştan kazandığı mesafeyi hemen kapatır.

## Klasörler ve sahiplik

| Ajan | Yazdığı yerler | Çıktı |
|---|---|---|
| **Pist** | `Assets/scripts/Race/Track/`, `Assets/scripts/Editor/TrackBuilder.cs`, `Assets/Scenes/race.unity` | `Harvester/Rebuild Race Track` menüsü: virajlı, hafif eğimli pist ağı, bariyerler, zemin, başlangıç/bitiş işaretleri. `RaceTrack` bileşenini doldurur. |
| **Araba** | `Assets/Models/Kenney/` (sadece modeller ve lisans), `Assets/scripts/Race/Car/`, `Assets/scripts/Editor/CarBuilder.cs`, `Assets/Prefabs/race/` | `RaceCar` (arcade sürüş, tek oyunculu test edilebilir), araba kamerası, `Harvester/Rebuild Race Car` menüsü. |
| **Harvester AI** | `Assets/scripts/Race/Harvester/`, `Assets/scripts/Editor/HarvesterAIBuilder.cs` | `HarvesterAI`: pisti takip eden, en yakın/en arkadaki arabaya saldıran, tablasıyla biçen sunucu taraflı sürücü. |
| **Ağ** | `Assets/scripts/Race/Net/`, `Assets/scripts/Race/RaceManager.cs` (ve spawner) | Yarış akışı (geri sayım, sıralama, eleme, bitiş), araba–araba ve harvester–araba çarpışma senkronu, lobiden `race` sahnesine geçiş. |
| **Yönetici** | `Assets/scripts/Race/RaceTrack.cs` (ortak sözleşme), `lobby.cs` / `GameManager.cs` entegrasyonu, bu belge | Parçaların birleşmesi, build testleri. |

## Ortak sözleşmeler

### `Harvester.Race.RaceTrack` (hazır)
Orta çizgi üzerinde mesafe (m) tabanlı sorgular: `GetPoint`, `GetForward`, `GetRight`, `GetNearestDistance(pos, hint, window)`, `GetLateralOffset`, `GetStartPose(slot)`, `GetHarvesterStartPose()`, `Length`, `Width`, `Loop`, `StartDistance`, `FinishDistance`. Pist kurucusu orta çizgiyi `SetCenterline` ile verir. `RaceTrack.Current` sahnedeki pisttir.

### `Harvester.Race.RaceCar` (Araba ajanı yazar, diğerleri buna güvenir)
```csharp
public class RaceCar : NetworkBehaviour
{
    public static readonly List<RaceCar> All;      // sahnedeki bütün arabalar
    public Rigidbody Body { get; }
    public float ForwardSpeed { get; }             // m/sn, ileri artı
    public bool IsEliminated { get; }              // NetworkVariable, sunucu yazar
    public float TrackDistance { get; }            // pist üzerindeki ilerleme (sahibi hesaplar, herkes okuyabilir)
    public static bool InputEnabled;               // yarış yöneticisi geri sayımda kapatır (varsayılan açık)
    public void Eliminate();                       // sadece sunucu: eler, herkeste parçalanma efekti
    public void ApplyHit(Vector3 velocityChange, Vector3 worldPoint); // her yerden çağrılabilir; sahibine iletilir
}
```
- Hareket sahibinde (owner-authoritative): `NetworkTransform` + `NetworkRigidbody`, robotlardaki ayarlarla (güvenilmez half-float delta), ama tam dönüş.
- Uzaktaki arabalar `RemoteExtrapolator` ile gösterilir. Bu bileşen zaten genel; harvester için yazıldı, arabalarda da çalışır.
- `ApplyHit`'in sahibine ulaşması Ağ ajanının işi. Araba ajanı sadece yerel uygulamayı yapar (`Body.AddForce(velocityChange, ForceMode.VelocityChange)` gibi) ve bir RPC kancası bırakır.

### `Harvester.Race.HarvesterAI` (Harvester AI ajanı)
- Sadece sunucuda karar verir ve sürer. Harvester `NetworkObject` sunucuya ait, istemciler `NetworkTransform` + `RemoteExtrapolator` ile görür.
- Pisti `RaceTrack`'ten takip eder; hedefi `RaceCar.All` içinden seçer.
- Biçme: tablanın önündeki kutuya giren araba için sunucuda `car.Eliminate()`. Çarpma için `car.ApplyHit(...)`.
- Zorluk tek bir yerden ayarlanabilmeli (hız, ivme, lastik bant mesafesi). Silahlar ileride "seviye" ile açılacak.

## Editörü paylaşma kuralları

Tek bir Unity editörü var, ajanlar aynı anda çalışıyor:

1. **Sahne açma/kaydetme, play mode ve build** sadece o an editör sırası kendisinde olan ajanın işidir. Sırayı yönetici verir; ajana verilen görevde yazar.
2. **Kod derleme herkese ortak.** `refresh_unity` herkesin kodunu derler. Kendi klasörün dışındaki derleme hatalarını düzeltme, raporla. Kendi kodunu her zaman derlenir halde bırak (yarım dosya bırakma).
3. Eski sahnelere (`world`, `Lobby`, `mpdeneme`, `deneme`, `testFrank`) ve eski karakter prefab'larına dokunma. `Lobby` entegrasyonu yöneticinin işi.
4. Network prefab'ı değiştirdikten sonra `GlobalObjectIdHash`'in diskte ve editörde aynı olduğunu kontrol et. Prefab'ı `EditPrefabContentsScope` ile düzenlediysen `AssetDatabase.ForceReserializeAssets` çalıştır (`NetSyncBuilder`'daki gibi).
5. Commit atma. Git'e yönetici kullanıcı isteyince atar.

## Kurallar

- Kod yorumları ve oyundaki bütün metinler Türkçe.
- Ad alanı `Harvester.Race`. Girdi eski Input Manager (`Input.GetAxis`).
- Sahne objelerini elle değil kurucu menülerle üret (CLAUDE.md'deki gibi).
- `execute_code` CodeDom derleyicisiyle C# 6 kabul eder: `out var` yok, `UnityEngine.Object` yazılmalı.
