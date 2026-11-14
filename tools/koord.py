#!/usr/bin/env python3
"""Ajanlar arası koordinasyon: tek Unity editörü için kilit + mesaj panosu.

Ajanlar: claude (yönetici), codex, agy (Antigravity), kullanici.
Durum dosyaları .koordinasyon/ altında (git dışı).

Kullanım:
  python3 tools/koord.py durum                       # kilit kimde, okunmamış mesaj sayıları
  python3 tools/koord.py al    <ajan> "<iş>" [dk]     # editör kilidini al (doluysa hata, çıkış 2)
  python3 tools/koord.py bekle <ajan> "<iş>" [dk]     # boşalana kadar bekle, sonra al
  python3 tools/koord.py uzat  <ajan> [dk]            # kendi kilidinin süresini uzat
  python3 tools/koord.py birak <ajan> ["<not>"]       # kilidi bırak, herkese "editör boş" yazar
  python3 tools/koord.py yaz   <ajan> <kime|hepsi> "<mesaj>"
  python3 tools/koord.py oku   <ajan>                 # sana gelen okunmamış mesajlar
  python3 tools/koord.py teslim <ajan> "<ne bitti, hangi dosyalar>"   # claude'a teslim bildirimi
  python3 tools/koord.py gecmis [n]                   # panodaki son n mesaj

Kilit süresi varsayılan 30 dk. Süresi dolan kilit boş sayılır (çöken ajan editörü kilitli bırakmasın).
"""
import fcntl
import json
import os
import sys
import time
from contextlib import contextmanager
from datetime import datetime

AJANLAR = ("claude", "codex", "agy", "kullanici")
KOK = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".koordinasyon")
KILIT = os.path.join(KOK, "editor_kilidi.json")
PANO = os.path.join(KOK, "pano.jsonl")
OKUNAN = os.path.join(KOK, "okunan_{}.txt")
VARSAYILAN_DK = 30


def zaman(ts=None):
    return datetime.fromtimestamp(ts or time.time()).strftime("%H:%M:%S")


@contextmanager
def kilitli():
    """Dosya işlemlerini atomik yapar (iki ajan aynı anda 'al' derse biri kazanır)."""
    os.makedirs(KOK, exist_ok=True)
    with open(os.path.join(KOK, ".lock"), "w") as f:
        fcntl.flock(f, fcntl.LOCK_EX)
        try:
            yield
        finally:
            fcntl.flock(f, fcntl.LOCK_UN)


def kilit_oku():
    try:
        with open(KILIT) as f:
            k = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        return None
    if k.get("bitis", 0) < time.time():
        return None
    return k


def kilit_yaz(k):
    if k is None:
        if os.path.exists(KILIT):
            os.remove(KILIT)
        return
    tmp = KILIT + ".tmp"
    with open(tmp, "w") as f:
        json.dump(k, f, ensure_ascii=False)
    os.replace(tmp, KILIT)


def pano_ekle(kimden, kime, metin, tur="mesaj"):
    with open(PANO, "a") as f:
        f.write(json.dumps({"t": time.time(), "kimden": kimden, "kime": kime, "tur": tur, "metin": metin},
                           ensure_ascii=False) + "\n")


def pano_satirlari():
    try:
        with open(PANO) as f:
            return [json.loads(s) for s in f if s.strip()]
    except FileNotFoundError:
        return []


def ajan_kontrol(a):
    if a not in AJANLAR:
        sys.exit(f"Bilinmeyen ajan '{a}'. Geçerli: {', '.join(AJANLAR)}")
    return a


def kilit_metni(k):
    if k is None:
        return "Editör BOŞ."
    kalan = int((k["bitis"] - time.time()) / 60)
    return f"Editör DOLU: {k['ajan']} — {k['is']} (alındı {zaman(k['baslangic'])}, ~{kalan} dk kaldı)"


def okunmamis(ajan):
    yol = OKUNAN.format(ajan)
    try:
        with open(yol) as f:
            n = int(f.read().strip() or 0)
    except FileNotFoundError:
        n = 0
    satirlar = pano_satirlari()
    bana = [(i, m) for i, m in enumerate(satirlar)
            if i >= n and m["kimden"] != ajan and m["kime"] in (ajan, "hepsi")]
    return bana, len(satirlar)


def yazdir(m):
    print(f"[{zaman(m['t'])}] {m['kimden']} → {m['kime']} ({m['tur']}): {m['metin']}")


def al(ajan, is_, dk):
    with kilitli():
        k = kilit_oku()
        if k is not None and k["ajan"] != ajan:
            return False, k
        simdi = time.time()
        kilit_yaz({"ajan": ajan, "is": is_, "baslangic": simdi, "bitis": simdi + dk * 60})
        pano_ekle(ajan, "hepsi", f"Editörü aldım: {is_} (~{dk} dk). Play/build/sahne açmayın.", "kilit")
        return True, None


def main(a):
    if not a:
        print(__doc__)
        return 0
    komut = a[0]

    if komut == "durum":
        with kilitli():
            print(kilit_metni(kilit_oku()))
            for ajan in AJANLAR:
                bana, _ = okunmamis(ajan)
                if bana:
                    print(f"  {ajan}: {len(bana)} okunmamış mesaj")
        return 0

    if komut in ("al", "bekle"):
        ajan = ajan_kontrol(a[1])
        is_ = a[2] if len(a) > 2 else "belirtilmedi"
        dk = int(a[3]) if len(a) > 3 else VARSAYILAN_DK
        duyuruldu = False
        while True:
            ok, k = al(ajan, is_, dk)
            if ok:
                print(f"Kilit sende ({dk} dk). İş bitince: python3 tools/koord.py birak {ajan}")
                return 0
            if komut == "al":
                print(kilit_metni(k))
                return 2
            if not duyuruldu:
                print(kilit_metni(k) + " — boşalmasını bekliyorum...", flush=True)
                with kilitli():
                    pano_ekle(ajan, k["ajan"], f"Editörü bekliyorum: {is_}", "bekleme")
                duyuruldu = True
            time.sleep(5)

    if komut == "uzat":
        ajan = ajan_kontrol(a[1])
        dk = int(a[2]) if len(a) > 2 else VARSAYILAN_DK
        with kilitli():
            k = kilit_oku()
            if k is None or k["ajan"] != ajan:
                print("Kilit sende değil. " + kilit_metni(k))
                return 2
            k["bitis"] = time.time() + dk * 60
            kilit_yaz(k)
        print(f"Uzatıldı: {dk} dk.")
        return 0

    if komut == "birak":
        ajan = ajan_kontrol(a[1])
        notu = a[2] if len(a) > 2 else ""
        with kilitli():
            k = kilit_oku()
            if k is None or k["ajan"] != ajan:
                # Yönetici ve kullanıcı takılan kilidi zorla bırakabilir.
                if k is not None and ajan in ("claude", "kullanici"):
                    pano_ekle(ajan, k["ajan"], f"Kilidin zorla bırakıldı ({k['is']}).", "kilit")
                else:
                    print("Kilit sende değil. " + kilit_metni(k))
                    return 2
            kilit_yaz(None)
            pano_ekle(ajan, "hepsi", "Editör boş." + (f" Not: {notu}" if notu else ""), "kilit")
        print("Bırakıldı.")
        return 0

    if komut == "yaz":
        ajan = ajan_kontrol(a[1])
        kime = a[2]
        if kime != "hepsi":
            ajan_kontrol(kime)
        with kilitli():
            pano_ekle(ajan, kime, a[3])
        print("Yazıldı.")
        return 0

    if komut == "teslim":
        ajan = ajan_kontrol(a[1])
        with kilitli():
            pano_ekle(ajan, "claude", a[2], "teslim")
        print("Teslim bildirildi. Commit/push'u claude yapar.")
        return 0

    if komut == "oku":
        ajan = ajan_kontrol(a[1])
        with kilitli():
            bana, toplam = okunmamis(ajan)
            for _, m in bana:
                yazdir(m)
            if not bana:
                print("Yeni mesaj yok.")
            with open(OKUNAN.format(ajan), "w") as f:
                f.write(str(toplam))
            print(kilit_metni(kilit_oku()))
        return 0

    if komut == "gecmis":
        n = int(a[1]) if len(a) > 1 else 20
        for m in pano_satirlari()[-n:]:
            yazdir(m)
        return 0

    print(__doc__)
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
