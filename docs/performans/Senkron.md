# Senkron ölçümü ve düzeltmesi — 14 Kasım 2026

İki development build aynı bilgisayarda, gerçek VDS relay'i üzerinden, tam gaz 2 turluk yarış (`tools/run_race_checks.py --mode harvester`). İki oyun da her karede bütün arabaların ve harvester'ın konumunu duvar saatiyle kaydeder (`-autoTrace`). Bir nesnenin gerçek konumu onu yöneten tarafın kaydıdır; öbür taraftaki kayıt ekranda gösterilendir. `tools/sync_analiz.py` aynı andaki farkı ölçer. Gecikme eşdeğeri = hatanın hız yönündeki bileşeni / hız (+ geride, − önde). Bilgisayarın kendisinin donduğu anlar (iki oyun birden; editör/Blender yükü) ölçüm dışıdır.

Yapay gecikme `--sim gecikme,oynama,kayıp` her iki oyunun gönderimine eklenir (100 → gidiş-dönüş ~270 ms).

## Sonuç (istemcinin gördüğü host arabası / harvester)

| Ağ | Önce: gecikme ortanca | Önce: hata ortanca / p95 | Sonra: gecikme ortanca | Sonra: hata ortanca / p95 |
|---|---|---|---|---|
| VDS, ek yok (RTT ~30 ms) | 14–16 ms | 0,93 / 1,7 m | **0,7 ms** | **0,72 / 1,1 m** |
| +50 ±10 ms | 25 ms | 1,43 / 2,2 m | **0,9 ms** | **1,26 / 3,0 m** |
| +100 ±20 ms, %1 kayıp | **183 ms** | **7,2 / 8,2 m** | **5,5 ms** | **1,7 / 3,0 m** |

Harvester aynı tabloyu izler (100 ms'de 182 → 2 ms). Host'un istemci arabasını görüşü zaten iyiydi (~0–3 ms) ve öyle kaldı.

## Bulunan sorunlar

1. **İleri tahmin 12 m ile sınırlıydı** (`RemoteExtrapolator.MaxOffset`). 37 m/sn'de bu ~320 ms demek; yüksek pingde gereken ~0,5–0,7 sn tahmin kesiliyordu. Sınır 45 m (en hızlı araba × 1 sn), süre sınırı 0,6 → 1 sn.
2. **Gecikme formülle kestiriliyordu** (RTT/2 + NGO tick gecikmesi). NGO'nun tamponu RTT, jitter ve sahibin kim olduğuna göre değişiyor; formül 0 ms'de iyi, 100 ms'de ~120–180 ms yanlış. Artık **ölçülüyor**: sahibi saniyede 5 kez "şu gerçek sunucu saatinde şuradaydım" damgası yollar (`hv_probe`, 28 bayt); alıcı ekranda gösterdiği konum geçmişinde o noktaya ne zaman geldiğini bulur. Son 7 ölçümün ortancası kullanılır; 3 ölçüm gelene kadar eski formül.
3. **İstemcinin ağ saati gerçek sunucu saatinin gerisinde** (0 ms'de ~60, 100 ms'de ~190 ms). Ping cevabına sunucu saati eklendi; `NetStatsHUD.RealServerTime` her makinede aynı anı verir (ölçülen hata ±10 ms).
4. Ölçüm sırasında görülen 0,7 sn'lik "donmalar" ağdan değil: iki oyun aynı anda ~2 sn donuyordu (bilgisayarın yükü). Gerçek oyunda tek oyun çalışır.

## Açık

- `CarContactSolver` araba–araba çarpışma zamanlamasında hâlâ eski formülü kullanıyor (`ViewDelay`). Ölçülen gecikmeyi kullanması tek satır (`RemoteExtrapolator.TryGetMeasuredDelay`), ama dosya kullanıcı onayı olmadan değiştirilmiyor.
- Yüksek pingde (≥100 ms) ani fren/kazada uzaktaki araba bir an ileri taşar (1 sn × hız'a kadar); tahminin doğal bedeli.
- Gerçek internet (farklı şehir/ülke) ve 3–4 oyunculu ölçüm yapılmadı.
