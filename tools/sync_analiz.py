#!/usr/bin/env python3
"""Senkron ölçümü: uzaktaki arabayı ekranda gerçek yerinden ne kadar uzakta/geride görüyoruz.

Host ve istemci aynı bilgisayarda çalışır; RaceAutoTest -autoTrace iki tarafta da bütün arabaların konumunu
duvar saatiyle (ms, iki süreçte ortak) kaydeder. Bir arabanın gerçek konumu, onu süren (local=1) tarafın kaydıdır.
Öbür taraftaki kayıt (local=0) ekranda gösterilen konumdur. Aynı anda ikisinin farkı = görüntü hatası.

Kullanım: python3 tools/sync_analiz.py <trace_race_host.csv> <trace_race_client.csv> [--json cikti.json]
Çıktı: araba ve izleyici başına hata (m) ve gecikme eşdeğeri (ms, hatanın hız yönündeki bileşeni / hız).
"""
import bisect, csv, json, math, sys


def load(path):
    rows = {}
    with open(path, newline='') as f:
        for r in csv.DictReader(f):
            try:
                key = (r['car'], int(r['local']))
                rows.setdefault(key, []).append((float(r['ms']), float(r['x']), float(r['z']), float(r['vx']), float(r['vz']), int(r['elim'])))
            except (KeyError, ValueError):
                continue
    for v in rows.values():
        v.sort()
    return rows


def interp(series, times, t):
    i = bisect.bisect_left(times, t)
    if i <= 0 or i >= len(series):
        return None
    a, b = series[i - 1], series[i]
    if b[0] - a[0] > 100:  # kayıtta boşluk
        return None
    f = (t - a[0]) / (b[0] - a[0]) if b[0] > a[0] else 0.0
    return tuple(a[k] + (b[k] - a[k]) * f for k in (1, 2, 3, 4))


def pct(values, p):
    if not values:
        return float('nan')
    s = sorted(values)
    return s[min(len(s) - 1, int(round(p / 100 * (len(s) - 1))))]


def hitches(*row_sets, gap_ms=150, pad_ms=1500):
    """Kare donmaları (iki taraftan birinde kayıt boşluğu): öncesi/sonrası pad_ms ölçümden çıkarılır.
    Aynı bilgisayardaki başka yük (editör, Blender) iki oyunu birden dondurabiliyor; bu ağ hatası değil."""
    spans = []
    for rows in row_sets:
        times = sorted({r[0] for series in rows.values() for r in series})
        for a, b in zip(times, times[1:]):
            if b - a > gap_ms:
                spans.append((a - 200, b + pad_ms))
    return spans


def analyse(truth_rows, view_rows, viewer, min_speed=5.0, skip=()):
    out = []
    for (car, local), truth in truth_rows.items():
        if local != 1:
            continue
        view = view_rows.get((car, 0))
        if not view:
            continue
        times = [r[0] for r in truth]
        errs, lags, ahead = [], [], []
        for t, x, z, vx, vz, elim in view:
            if elim or any(a <= t <= b for a, b in skip):
                continue
            real = interp(truth, times, t)
            if real is None:
                continue
            rx, rz, rvx, rvz = real
            speed = math.hypot(rvx, rvz)
            if speed < min_speed:
                continue
            dx, dz = x - rx, z - rz
            err = math.hypot(dx, dz)
            along = (dx * rvx + dz * rvz) / speed  # + önde, - geride
            errs.append(err)
            lags.append(-along / speed * 1000.0)
            ahead.append(along)
        if errs:
            out.append({
                'araba': car, 'izleyen': viewer, 'ornek': len(errs),
                'hata_m_medyan': round(pct(errs, 50), 2), 'hata_m_p95': round(pct(errs, 95), 2), 'hata_m_max': round(max(errs), 2),
                'gecikme_ms_medyan': round(pct(lags, 50), 1), 'gecikme_ms_p95': round(pct(lags, 95), 1),
                'gecikme_ms_p5': round(pct(lags, 5), 1),
            })
    return out


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    if len(args) < 2:
        print(__doc__)
        return 1
    host, client = load(args[0]), load(args[1])
    skip = hitches(host, client)
    if skip:
        print(f"{len(skip)} kare donması ölçüm dışı: " + ", ".join(f"{(b - a) / 1000:.1f} sn" for a, b in skip))
    results = analyse(host, client, 'istemci', skip=skip) + analyse(client, host, 'host', skip=skip)
    for r in results:
        print(f"{r['izleyen']:>8} ← {r['araba']:<10} n={r['ornek']:5}  hata medyan {r['hata_m_medyan']:5.2f} m  p95 {r['hata_m_p95']:5.2f} m  "
              f"max {r['hata_m_max']:5.2f} m | gecikme medyan {r['gecikme_ms_medyan']:6.1f} ms  p5..p95 {r['gecikme_ms_p5']:.0f}..{r['gecikme_ms_p95']:.0f} ms")
    if '--json' in sys.argv:
        path = sys.argv[sys.argv.index('--json') + 1]
        with open(path, 'w') as f:
            json.dump(results, f, ensure_ascii=False, indent=1)
    return 0


if __name__ == '__main__':
    sys.exit(main())
