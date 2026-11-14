"""
Harvester araba modeli kontrolü (Blender 4.x).

Kullanım: Blender'da araba kök objesini seç ve bu betiği çalıştır (Text Editor > Run Script,
ya da `exec(open(yol).read())`). Sonuç konsola ve `KONTROL_SONUCU` metin bloğuna yazılır.
Her satır GEÇTİ / KALDI / UYARI. KALDI varken dışa aktarma.

Not: Blender Z-yukarı çalışır. Ön yön, ön ve arka tekerleklerin konumundan bulunur; dışa aktarma
eksen ayarından bağımsız olarak arabanın kendi içindeki tutarlılığı kontrol edilir. Unity'deki
son yön (+Z) ayrıca Unity'de kontrol edilmeli (spesifikasyon 10.2).
"""
import re
import bpy
from mathutils import Vector

WHEELS = ["wheel-front-left", "wheel-front-right", "wheel-back-left", "wheel-back-right"]
CALIPERS = ["caliper-front-left", "caliper-front-right", "caliper-back-left", "caliper-back-right"]
EMPTIES = ["light_head_L", "light_head_R", "light_tail_L", "light_tail_R", "camera_cockpit"]
PANELS = ["chassis_underbody", "door_left", "door_right", "fender_front_left", "fender_front_right",
          "fender_rear_left", "fender_rear_right", "roof_cabin", "engine_decklid"]
GLASS = ["glass_windshield", "glass_rear", "glass_side_left", "glass_side_right"]
MATERIALS = {"Paint_Body", "Paint_Rim", "Paint_Caliper", "Paint_Accent", "Neon", "Glass_Tint",
             "Trim_Dark", "Carbon", "Metal_Chrome", "Interior_Dark", "Light_Head", "Light_Tail",
             "Decal_Graphic", "Screen"}
FORBIDDEN = ["porsche", "911", "gt3", "cup", "ferrari", "lamborghini", "nissan", "gtr", "gt-r", "skyline",
             "supra", "toyota", "honda", "nsx", "bmw", "mercedes", "amg", "audi", "ford", "mustang",
             "chevrolet", "camaro", "corvette", "dodge", "mazda", "rx7", "rx-7", "subaru", "mitsubishi",
             "lancer", "bbs", "brembo", "recaro", "sparco", "hks", "pirelli", "michelin", "nfs", "nfsu",
             "forza", "granturismo", "turbos", "typer", "nismo", "msport", "svr", "trackhawk", "hellcat"]
MOD_RE = re.compile(r"^mod_([a-z0-9_]+?)_(\d+)_([a-z0-9_]+?)(_left|_right)?$")
RIM_RE = re.compile(r"^(wheel-(?:front|back)-(?:left|right))_rim_(\d+)_([a-z0-9_]+)$")

results = []


def report(status, item, detail=""):
    results.append((status, item, detail))


def check(cond, item, detail=""):
    report("GEÇTİ" if cond else "KALDI", item, detail)
    return cond


def tri_count(obj):
    if obj.type != "MESH" or obj.data is None:
        return 0
    return sum(max(0, len(p.vertices) - 2) for p in obj.data.polygons)


def world_bbox(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        if o.type != "MESH":
            continue
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector((min(lo.x, w.x), min(lo.y, w.y), min(lo.z, w.z)))
            hi = Vector((max(hi.x, w.x), max(hi.y, w.y), max(hi.z, w.z)))
    return lo, hi


def visible(o):
    return not (o.hide_get() or o.hide_render)


def is_identity(o, tol=1e-4):
    return (o.location.length < tol and Vector(o.rotation_euler).length < tol
            and (o.scale - Vector((1, 1, 1))).length < tol)


def main():
    root = bpy.context.active_object
    if root is None:
        print("Araba kök objesini seç.")
        return
    allobj = [root] + list(root.children_recursive)
    names = {o.name: o for o in allobj}

    # --- Kök
    check(Vector(root.rotation_euler).length < 1e-4 and (root.scale - Vector((1, 1, 1))).length < 1e-4,
          "Kök dönüşü 0, ölçek 1", f"rot={tuple(round(a, 4) for a in root.rotation_euler)} ölçek={tuple(root.scale)}")
    applied = [o.name for o in allobj if o.type == "MESH" and (o.scale - Vector((1, 1, 1))).length > 1e-4]
    check(not applied, "Uygulanmamış ölçek yok", ", ".join(applied[:8]))

    # --- Tekerlekler ve yön
    wheels = [names.get(w) for w in WHEELS]
    if not check(all(wheels), "4 tekerlek adı tam", ", ".join(w for w in WHEELS if w not in names)):
        finish()
        return
    centers = {w.name: w.matrix_world.translation.copy() for w in wheels}
    front = (centers["wheel-front-left"] + centers["wheel-front-right"]) / 2
    back = (centers["wheel-back-left"] + centers["wheel-back-right"]) / 2
    fwd = (front - back)
    wheelbase = fwd.length
    fwd.normalize()
    right = (centers["wheel-front-right"] - centers["wheel-front-left"]).normalized()
    mid = (front + back) / 2
    report("BİLGİ", "Ön yön (Blender dünyası)", f"{tuple(round(a, 3) for a in fwd)}, dingil {wheelbase:.3f} m")
    check(2.2 <= wheelbase <= 3.2, "Dingil mesafesi 2.2–3.2 m", f"{wheelbase:.3f}")
    check(abs((mid - root.matrix_world.translation).dot(fwd)) <= 0.05,
          "Köken iki aksın ortasında (±5 cm)", f"fark {(mid - root.matrix_world.translation).dot(fwd):.3f} m")

    up = Vector((0, 0, 1))
    for w in wheels:
        lo, hi = world_bbox([w])
        center = (lo + hi) / 2
        radius = (hi.z - lo.z) / 2
        width = abs((hi - lo).dot(right))
        check(abs(lo.z) <= 0.005, f"{w.name}: tekerlek altı z=0", f"{lo.z:.4f}")
        check((center - w.matrix_world.translation).length <= 0.002, f"{w.name}: pivot merkezde",
              f"fark {(center - w.matrix_world.translation).length:.4f}")
        check(0.30 <= radius <= 0.45 and 0.20 <= width <= 0.40, f"{w.name}: yarıçap/genişlik",
              f"r={radius:.3f} g={width:.3f}")
        for c in w.children:
            ok = is_identity(c)
            if RIM_RE.match(c.name) or c.name.endswith("_tire_lettering"):
                check(ok, f"{c.name}: yerel dönüşüm (0,0,0)",
                      f"loc={tuple(round(a, 3) for a in c.location)} rot={tuple(round(a, 3) for a in c.rotation_euler)}")
            elif "caliper" in c.name:
                report("KALDI", f"{c.name}: kaliper tekerleğin çocuğu olmamalı", "kökün çocuğu yap")
            else:
                report("UYARI", f"{w.name} altında beklenmeyen obje", c.name)
    rims = {}
    for o in allobj:
        m = RIM_RE.match(o.name)
        if m:
            rims.setdefault(m.group(2), set()).add(m.group(1))
    check(rims and all(len(v) == 4 for v in rims.values()), "Her jant sırası 4 tekerlekte var",
          str({k: len(v) for k, v in sorted(rims.items())}))
    check("0" in rims, "Stok jant (rim_0) var")

    for c in CALIPERS:
        o = names.get(c)
        if check(o is not None and o.parent == root, f"{c}: var ve kökün çocuğu"):
            wheel = names[c.replace("caliper", "wheel")]
            check((o.matrix_world.translation - wheel.matrix_world.translation).length <= 0.005,
                  f"{c}: pivot tekerlek merkezinde")

    # --- Zorunlu objeler
    for n in PANELS + GLASS:
        check(n in names, f"Gövde/cam objesi: {n}")
    for n in EMPTIES:
        o = names.get(n)
        check(o is not None and o.type == "EMPTY", f"Boş nokta: {n}")
    for side in ("L", "R"):
        o = names.get("light_head_" + side)
        if o:
            d = (o.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized()
            report("BİLGİ", f"light_head_{side} yönü", f"{tuple(round(a, 2) for a in d)} (Unity'de +Z olmalı)")

    # --- Modifiye slotları
    slots = {}
    for o in allobj:
        m = MOD_RE.match(o.name)
        if o.name.startswith("mod_") and not m:
            report("KALDI", "Slot adı biçimi mod_<slot>_<sıra>_<ad>", o.name)
            continue
        if m:
            key = m.group(1) + (m.group(4) or "")
            slots.setdefault(key, []).append((int(m.group(2)), o))
    for key, items in sorted(slots.items()):
        idx = sorted(i for i, _ in items)
        check(idx == list(range(len(idx))), f"Slot {key}: sıra 0'dan aralıksız", str(idx))
        vis = [o.name for i, o in items if visible(o)]
        check(len(vis) == 1 and vis[0] == next(o.name for i, o in items if i == 0),
              f"Slot {key}: sadece sıra 0 görünür", ", ".join(vis))
        locs = [o.matrix_world.translation for _, o in items]
        check(all((l - locs[0]).length < 1e-3 for l in locs), f"Slot {key}: ortak pivot")
        for i, o in items:
            if o.name.endswith("_none") or "_none_" in o.name:
                check(o.type == "EMPTY", f"{o.name}: mesh'siz boş obje")
            tris = tri_count(o)
            if tris > 400:
                report("KALDI", f"{o.name}: varyant üçgeni ≤ 400", str(tris))
    for o in allobj:
        if o.name.startswith("decal_"):
            check(not visible(o), f"{o.name}: varsayılan gizli")

    # --- Malzemeler
    used = set()
    for o in allobj:
        if o.type != "MESH":
            continue
        slots_used = [s.material.name for s in o.material_slots if s.material]
        used.update(slots_used)
        if len(o.material_slots) > 2:
            report("KALDI", f"{o.name}: en fazla 2 malzeme yuvası", str(len(o.material_slots)))
    extra = sorted(m for m in used if m.split(".")[0] not in MATERIALS)
    check(not extra, "Sadece izinli malzeme adları", ", ".join(extra))

    # --- Üçgen bütçesi
    stock = 0
    for o in allobj:
        if o.type == "MESH" and visible(o):
            stock += tri_count(o)
    total = sum(tri_count(o) for o in allobj)
    check(stock <= 5000, "Stok üçgen ≤ 5000 (hedef 2000–3000)", str(stock))
    check(total <= 12000, "Toplam üçgen ≤ 12000", str(total))

    # --- Ölçüler
    vis_mesh = [o for o in allobj if o.type == "MESH" and visible(o)]
    body = [o for o in vis_mesh if not o.name.startswith("mod_mirror")]
    lo, hi = world_bbox(body)
    size = hi - lo
    length = abs(size.dot(fwd))
    width = abs(size.dot(right))
    height = size.z
    check(3.8 <= length <= 5.0, "Boy 3.8–5.0 m", f"{length:.3f}")
    check(1.70 <= width <= 2.05, "Gövde eni (aynasız) 1.70–2.05 m", f"{width:.3f}")
    check(1.0 <= height <= 1.7, "Yükseklik 1.0–1.7 m", f"{height:.3f}")
    wl, wh = world_bbox(wheels)
    body_half = width / 2
    wheel_half = max(abs((wl - mid).dot(right)), abs((wh - mid).dot(right)))
    check(wheel_half <= body_half + 0.03, "Tekerlekler gövdeyi 3 cm'den fazla geçmiyor",
          f"tekerlek {wheel_half:.3f} gövde {body_half:.3f}")

    # --- Yasak kelimeler
    bad = []
    for o in allobj:
        for txt in (o.name, getattr(o.data, "name", "") or ""):
            low = txt.lower()
            for word in FORBIDDEN:
                if re.search(r"(^|[^a-z0-9])" + re.escape(word) + r"([^a-z0-9]|$)", low):
                    bad.append(f"{txt} ({word})")
    for m in bpy.data.materials:
        low = m.name.lower()
        for word in FORBIDDEN:
            if re.search(r"(^|[^a-z0-9])" + re.escape(word) + r"([^a-z0-9]|$)", low):
                bad.append(f"{m.name} ({word})")
    check(not bad, "Yasak kelime yok", ", ".join(sorted(set(bad))[:10]))

    finish()


def finish():
    lines = [f"[{s}] {i}" + (f" — {d}" if d else "") for s, i, d in results]
    failed = sum(1 for s, _, _ in results if s == "KALDI")
    lines.append(f"\nSONUÇ: {'HAZIR' if failed == 0 else str(failed) + ' madde KALDI'}")
    text = "\n".join(lines)
    print(text)
    block = bpy.data.texts.get("KONTROL_SONUCU") or bpy.data.texts.new("KONTROL_SONUCU")
    block.clear()
    block.write(text)


main()
