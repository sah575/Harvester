#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Harvester — A2 Dağ Tırmanışı Haritası: Zirveye Uzatma (1. Aşama: ~5 km Yol + Üst Üste Firketeler + Kaba Arazi)
Üretim Betiği (Blender 4.x arka plan kipinde çalışır: ~/.local/bin/blender -b -P uret_dag_tirmanisi.py)

A2 1. Aşama Kuralları (docs/GOREVLER.md):
- Mevcut yolun eski bitiş düzlüğüne kadar olan kısmı (ilk ~2.340 m: 0..936 indeksli Dag_Merkez.json noktaları,
  yol yüzeyi, bariyerler, tüneller) BİREBİR AYNI KALIR. Eski bitiş düzlüğü ve kemeri kaldırılır, yol oradan devam eder.
- Yeni bölüm (~2,69 km, toplam ~5,03 km): dağ yamacında üst üste dizilmiş 7 yeni saç tokası viraj (Firkete_6..12,
  toplam 12 firkete), aralarda kısa düzlük/teras geçişleri, zirvede 165 m düzlük + bitiş kemeri.
- Ölçüler: genişlik 14 m, firkete iç yarıçapı >= 12 m (en dar 16.58 m, yeni bölümde 22.74 m), eğim <= %12 (en dik %10.6),
  yanal eğim <= 6° (en fazla 3.5°), tünel tavanı >= 7 m (en alçak 7.86 m).
- İlk bölümdeki mevcut çevre yerleşimi (Dag_Yerlesim.json) aynen korunur; yeni yolun üstünden geçtiği veya
  kaldırılan eski bitiş düzlüğündeki ağaçlar/öğeler çıkarılır. Yeni bölüme bu aşamada çevre detayı eklenmez.
"""

import os
import sys
import json
import math
import random
import bpy
import bmesh
from mathutils import Vector, Euler
from mathutils.bvhtree import BVHTree

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

import uret_cevre_modelleri as cevre_modelleri


# ---------------------------------------------------------------------------
# Sabitler ve Ölçüler (1. Aşama ile BİREBİR AYNI)
# ---------------------------------------------------------------------------
ROAD_WIDTH = 14.0           # Asfalt şerit genişliği (m)
HALF_WIDTH = ROAD_WIDTH * 0.5
SHOULDER = 0.60             # Banket payı (m); bariyer iç yüzü = ±7.60 m
BARRIER_THICK = 0.65        # Bariyer kalınlığı (m); dış yüz = ±8.25 m
BARRIER_HEIGHT = 0.92       # Bariyer yol üstü yüksekliği (m)
BARRIER_DEPTH = 1.25        # Bariyer yol altı etek derinliği (m)
LINE_HEIGHT = 0.018         # Yol çizgilerinin asfalttan yüksekliği (m)

START_M = 75.0              # Başlangıç dizilişi ön sırası (değişmez)
FINISH_MARGIN = 38.0        # Bitiş kemerinden son duvara kalan güvenli fren mesafesi (m)
SAMPLE_STEP = 2.5           # Dag_Merkez.json nokta aralığı (m, <= 4.0 m)
PRESERVED_P1_LAST_IDX = 936 # İlk ~2.340 m'nin son örnek indeksi (s = 2339.381 m, toplam 937 nokta: 0..936)


# ---------------------------------------------------------------------------
# Koordinat Dönüşümü (Unity <-> Blender)
# ---------------------------------------------------------------------------
def u2b(ux, uy, uz):
    return (-float(ux), -float(uz), float(uy))


def b2u(bx, by, bz):
    return (-float(bx), float(bz), -float(by))


def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)
    for mat in list(bpy.data.materials):
        if mat.users == 0:
            bpy.data.materials.remove(mat)
    for l in list(bpy.data.lights):
        if l.users == 0:
            bpy.data.lights.remove(l)
    for c in list(bpy.data.cameras):
        if c.users == 0:
            bpy.data.cameras.remove(c)


def make_mat(name, color, metallic=0.0, roughness=0.75, emission=None, emission_strength=0.0):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    mat.use_backface_culling = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        if "Emission Color" in bsdf.inputs and emission:
            bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
            bsdf.inputs["Emission Strength"].default_value = emission_strength
    mat.diffuse_color = (*color, 1.0)
    mat.metallic = metallic
    mat.roughness = roughness
    return mat


# ---------------------------------------------------------------------------
# 1. Yol Orta Çizgisi Tasarımı
#    - İlk ~2.340 m (0..936 indeksleri) A1'dekiyle BİREBİR AYNI
#    - 2.340 m'den ~5.028 m'ye uzanan yeni bölüm: yamaçta üst üste 7 firkete + zirvede 165 m düzlük
# ---------------------------------------------------------------------------
SEGMENTS_P1 = [
    (95.0,     0.0, 0.000, "Baslangic_Duzlugu",   0),
    (90.0,    35.0, 0.085, "Etek_Kivrimi",       -1),
    (95.0,     0.0, 0.102, "Etek_Duzlugu",       -1),
    (90.0,  -162.0, 0.060, "Firkete_1_Sol",      +1),
    (165.0,   18.0, 0.105, "Yamac_Gecisi_1",     +1),
    (90.0,   164.0, 0.060, "Firkete_2_Sag",      -1),
    (80.0,   -12.0, 0.095, "Tunel_1_Yaklasim",   -1),
    (115.0,  -54.0, 0.088, "Tunel_1_KayaGecidi",  0),  # Tünel 1: ~710..822 m (112.5 m)
    (135.0,   30.0, 0.104, "Ucurum_Kenari_1",    +1),
    (92.0,  -160.0, 0.060, "Firkete_3_Sol",      +1),
    (185.0,   34.0, 0.106, "Sirt_Hizli_Bolum",   -1),
    (92.0,   160.0, 0.062, "Firkete_4_Sag",      -1),
    (200.0,  -28.0, 0.105, "Ucurum_Kenari_2",    +1),
    (94.0,  -158.0, 0.060, "Firkete_5_Sol",      +1),
    (85.0,    12.0, 0.095, "Tunel_2_Yaklasim",   -1),
    (190.0,   72.0, 0.085, "Tunel_2_UzunKivrimli", 0), # Tünel 2: ~1710..1900 m (189.9 m, kıvrımlı)
    (195.0,   36.0, 0.102, "Yuksek_Yamac",       +1),
    (110.0,  -56.0, 0.085, "Zirve_Viraji",       -1),
    (120.0,   15.0, 0.065, "Zirve_Yaklasim",     -1),
    (165.0,    0.0, 0.000, "Bitis_Duzlugu_Eski",  0),
]

# Yeni uzatma bölümü (~2.689 m 3B uzunluk, dağ yamacında üst üste 7 saç tokası viraj + zirvede 165 m düzlük)
EXT_SEGMENTS = [
    (125.0,    6.0, 0.095, "Yamac_Gecisi_2",      -1),  # psi: -54° -> -48°
    (112.0,  162.0, 0.060, "Firkete_6_Sag",       -1),  # psi: -48° -> +114° (R_ic = 22.7 m)
    (205.0,   16.0, 0.106, "Terase_1",            +1),  # psi: +114° -> +130°
    (112.0, -162.0, 0.060, "Firkete_7_Sol",       +1),  # psi: +130° -> -32° (R_ic = 22.7 m)
    (210.0,  -16.0, 0.106, "Terase_2",            -1),  # psi: -32° -> -48°
    (112.0,  162.0, 0.060, "Firkete_8_Sag",       -1),  # psi: -48° -> +114° (R_ic = 22.7 m)
    (215.0,   16.0, 0.106, "Terase_3",            +1),  # psi: +114° -> +130°
    (112.0, -162.0, 0.060, "Firkete_9_Sol",       +1),  # psi: +130° -> -32° (R_ic = 22.7 m)
    (215.0,  -16.0, 0.105, "Kar_Siniri_Gecisi",   -1),  # psi: -32° -> -48°
    (112.0,  162.0, 0.060, "Firkete_10_Sag",      -1),  # psi: -48° -> +114° (R_ic = 22.7 m)
    (210.0,   16.0, 0.105, "Kar_Yamaci_1",        +1),  # psi: +114° -> +130°
    (112.0, -162.0, 0.060, "Firkete_11_Sol",      +1),  # psi: +130° -> -32° (R_ic = 22.7 m)
    (205.0,  -16.0, 0.104, "Kar_Yamaci_2",        -1),  # psi: -32° -> -48°
    (112.0,  160.0, 0.058, "Firkete_12_Sag",      -1),  # psi: -48° -> +112° (R_ic = 23.1 m)
    (205.0,  -35.0, 0.096, "Zirve_Sirti",         +1),  # psi: +112° -> +77°
    (140.0,  -25.0, 0.068, "Zirve_Yaklasim_2",    -1),  # psi: +77° -> +52°
    (165.0,    0.0, 0.000, "Bitis_Duzlugu",        0),  # Zirvede 165 m karlı bitiş düzlüğü
]


def _compute_frames_orientation(sampled, flat_start_horiz=95.0, flat_end_horiz=None):
    n_s = len(sampled)
    for i in range(n_s):
        prev_p = sampled[max(0, i - 1)]
        next_p = sampled[min(n_s - 1, i + 1)]
        fx = next_p["x"] - prev_p["x"]
        fy = next_p["y"] - prev_p["y"]
        fz = next_p["z"] - prev_p["z"]
        flen = math.sqrt(fx * fx + fy * fy + fz * fz)
        fx, fy, fz = fx / flen, fy / flen, fz / flen

        rx0, ry0, rz0 = fz, 0.0, -fx
        rlen = math.hypot(rx0, rz0)
        rx0, rz0 = rx0 / rlen, rz0 / rlen

        ux0 = fy * rz0 - fz * ry0
        uy0 = fz * rx0 - fx * rz0
        uz0 = fx * ry0 - fy * rx0
        ulen = math.sqrt(ux0 * ux0 + uy0 * uy0 + uz0 * uz0)
        ux0, uy0, uz0 = ux0 / ulen, uy0 / ulen, uz0 / ulen

        k_val = sampled[i]["k"]
        bank_deg = max(-3.5, min(3.5, k_val * 85.0))
        if sampled[i]["s_horiz"] < flat_start_horiz or (flat_end_horiz is not None and sampled[i]["s_horiz"] > flat_end_horiz):
            bank_deg = 0.0
        bank_rad = math.radians(bank_deg)
        cb, sb = math.cos(bank_rad), math.sin(bank_rad)

        rx = rx0 * cb - ux0 * sb
        ry = ry0 * cb - uy0 * sb
        rz = rz0 * cb - uz0 * sb

        ux = ux0 * cb + rx0 * sb
        uy = uy0 * cb + ry0 * sb
        uz = uz0 * cb + rz0 * sb

        sampled[i]["fwd"] = (fx, fy, fz)
        sampled[i]["right"] = (rx, ry, rz)
        sampled[i]["right_flat"] = (rx0, 0.0, rz0)
        sampled[i]["up"] = (ux, uy, uz)
        sampled[i]["bank_deg"] = bank_deg


def generate_phase1_full_centerline():
    """A1 1. aşamadaki 998 noktayı birebir aynı matematikle üretir."""
    ds = 0.25
    fine_pts = []
    raw_grades = []
    seg_meta = []

    s_acc = 0.0
    for seg_len, turn_deg, grade, name, cliff_side in SEGMENTS_P1:
        seg_start = s_acc
        seg_end = s_acc + seg_len
        seg_meta.append({
            "name": name,
            "start_m": seg_start,
            "end_m": seg_end,
            "length_m": seg_len,
            "turn_deg": turn_deg,
            "cliff_side": cliff_side,
        })
        s_acc = seg_end

    total_horiz_len = s_acc
    n_total = int(round(total_horiz_len / ds))

    curvatures = []
    cliff_sides = []
    seg_names = []
    for seg_len, turn_deg, grade, name, cliff_side in SEGMENTS_P1:
        n = int(round(seg_len / ds))
        turn_rad = math.radians(turn_deg)
        r = 0.25
        k_peak = turn_rad / ((1.0 - r) * seg_len) if seg_len > 0 else 0.0
        for i in range(n):
            u = (i + 0.5) / n
            if u < r:
                k = k_peak * 0.5 * (1.0 - math.cos(math.pi * u / r))
            elif u > 1.0 - r:
                k = k_peak * 0.5 * (1.0 - math.cos(math.pi * (1.0 - u) / r))
            else:
                k = k_peak
            curvatures.append(k)
            raw_grades.append(grade)
            cliff_sides.append(cliff_side)
            seg_names.append(name)

    win = int(round(20.0 / ds))
    smooth_grades = []
    for i in range(n_total):
        lo = max(0, i - win)
        hi = min(n_total - 1, i + win)
        g = sum(raw_grades[lo:hi + 1]) / (hi - lo + 1)
        s = i * ds
        if s <= 90.0 or s >= (total_horiz_len - 160.0):
            g = 0.0
        elif s < 100.0:
            g *= (s - 90.0) / 10.0
        elif s > (total_horiz_len - 170.0):
            g *= ((total_horiz_len - 160.0) - s) / 10.0
        smooth_grades.append(g)

    x, y, z, psi = 0.0, 0.0, 0.0, 0.0
    s_3d = 0.0
    for i in range(n_total):
        k = curvatures[i]
        g = smooth_grades[i]
        fine_pts.append({
            "s_horiz": i * ds,
            "s_3d": s_3d,
            "x": x,
            "y": y,
            "z": z,
            "psi": psi,
            "k": k,
            "grade": g,
            "cliff_side": cliff_sides[i],
            "seg_name": seg_names[i],
        })
        psi += k * ds
        dx = math.sin(psi) * ds
        dz = math.cos(psi) * ds
        dy = g * ds
        x += dx
        y += dy
        z += dz
        s_3d += math.sqrt(dx * dx + dy * dy + dz * dz)

    fine_pts.append({
        "s_horiz": n_total * ds,
        "s_3d": s_3d,
        "x": x,
        "y": y,
        "z": z,
        "psi": psi,
        "k": 0.0,
        "grade": 0.0,
        "cliff_side": 0,
        "seg_name": "Bitis_Duzlugu_Eski",
    })

    total_3d_len = fine_pts[-1]["s_3d"]
    num_samples = int(math.ceil(total_3d_len / SAMPLE_STEP))
    step_3d = total_3d_len / num_samples

    sampled = []
    idx = 0
    for j in range(num_samples + 1):
        target_s = min(j * step_3d, total_3d_len)
        while idx < len(fine_pts) - 2 and fine_pts[idx + 1]["s_3d"] < target_s:
            idx += 1
        a = fine_pts[idx]
        b = fine_pts[idx + 1]
        span = b["s_3d"] - a["s_3d"]
        t = (target_s - a["s_3d"]) / span if span > 1e-8 else 0.0
        sampled.append({
            "s": target_s,
            "s_horiz": a["s_horiz"] + (b["s_horiz"] - a["s_horiz"]) * t,
            "x": a["x"] + (b["x"] - a["x"]) * t,
            "y": a["y"] + (b["y"] - a["y"]) * t,
            "z": a["z"] + (b["z"] - a["z"]) * t,
            "psi": a["psi"] + (b["psi"] - a["psi"]) * t,
            "k": a["k"] + (b["k"] - a["k"]) * t,
            "grade": a["grade"] + (b["grade"] - a["grade"]) * t,
            "cliff_side": a["cliff_side"],
            "seg_name": a["seg_name"],
        })

    _compute_frames_orientation(sampled, 95.0, total_horiz_len - 165.0)
    return sampled, seg_meta


def generate_centerline():
    """
    İlk 937 noktayı (0..936, ilk ~2.340 m) A1'den birebir korur,
    936. noktadan itibaren (~2.339,4 m) yeni 7 firketeli dağ tırmanışı uzatmasını ekler.
    """
    sampled_p1, seg_meta_p1 = generate_phase1_full_centerline()
    preserved = [dict(f) for f in sampled_p1[:PRESERVED_P1_LAST_IDX + 1]]

    # İlk 19 segmentin meta bilgisini koru
    seg_meta = list(seg_meta_p1[:19])

    anchor = preserved[-1]
    s_3d_start = anchor["s"]
    s_horiz_start = anchor["s_horiz"]
    x, y, z = anchor["x"], anchor["y"], anchor["z"]
    psi = anchor["psi"]  # -54.0 derece (tam düz ve yatay geçiş noktası)

    ds = 0.25
    s_acc = s_horiz_start
    for seg_len, turn_deg, grade, name, cliff_side in EXT_SEGMENTS:
        seg_start = s_acc
        seg_end = s_acc + seg_len
        seg_meta.append({
            "name": name,
            "start_m": seg_start,
            "end_m": seg_end,
            "length_m": seg_len,
            "turn_deg": turn_deg,
            "cliff_side": cliff_side,
        })
        s_acc = seg_end

    ext_horiz_len = sum(s[0] for s in EXT_SEGMENTS)
    n_ext = int(round(ext_horiz_len / ds))

    curvatures = []
    raw_grades = []
    cliff_sides = []
    seg_names = []
    for seg_len, turn_deg, grade, name, cliff_side in EXT_SEGMENTS:
        n = int(round(seg_len / ds))
        turn_rad = math.radians(turn_deg)
        r = 0.25
        k_peak = turn_rad / ((1.0 - r) * seg_len) if seg_len > 0 else 0.0
        for i in range(n):
            u = (i + 0.5) / n
            if u < r:
                k = k_peak * 0.5 * (1.0 - math.cos(math.pi * u / r))
            elif u > 1.0 - r:
                k = k_peak * 0.5 * (1.0 - math.cos(math.pi * (1.0 - u) / r))
            else:
                k = k_peak
            curvatures.append(k)
            raw_grades.append(grade)
            cliff_sides.append(cliff_side)
            seg_names.append(name)

    win = int(round(20.0 / ds))
    smooth_grades = []
    for i in range(n_ext):
        lo = max(0, i - win)
        hi = min(n_ext - 1, i + win)
        g = sum(raw_grades[lo:hi + 1]) / (hi - lo + 1)
        s = i * ds
        # İlk 5 m yatay, 5..20 m yumuşak eğim geçişi; son 160 m zirve bitiş düzlüğü yatay
        if s <= 5.0 or s >= (ext_horiz_len - 160.0):
            g = 0.0
        elif s < 20.0:
            g *= (s - 5.0) / 15.0
        elif s > (ext_horiz_len - 175.0):
            g *= ((ext_horiz_len - 160.0) - s) / 15.0
        smooth_grades.append(g)

    fine_ext = []
    s_3d = s_3d_start
    for i in range(n_ext):
        k = curvatures[i]
        g = smooth_grades[i]
        fine_ext.append({
            "s_horiz": s_horiz_start + i * ds,
            "s_3d": s_3d,
            "x": x,
            "y": y,
            "z": z,
            "psi": psi,
            "k": k,
            "grade": g,
            "cliff_side": cliff_sides[i],
            "seg_name": seg_names[i],
        })
        psi += k * ds
        dx = math.sin(psi) * ds
        dz = math.cos(psi) * ds
        dy = g * ds
        x += dx
        y += dy
        z += dz
        s_3d += math.sqrt(dx * dx + dy * dy + dz * dz)

    fine_ext.append({
        "s_horiz": s_horiz_start + n_ext * ds,
        "s_3d": s_3d,
        "x": x,
        "y": y,
        "z": z,
        "psi": psi,
        "k": 0.0,
        "grade": 0.0,
        "cliff_side": 0,
        "seg_name": "Bitis_Duzlugu",
    })

    rem_3d_len = fine_ext[-1]["s_3d"] - s_3d_start
    num_ext_samples = int(math.ceil(rem_3d_len / SAMPLE_STEP))
    step_ext = rem_3d_len / num_ext_samples

    ext_sampled = []
    idx = 0
    for j in range(1, num_ext_samples + 1):
        target_s = min(s_3d_start + j * step_ext, fine_ext[-1]["s_3d"])
        while idx < len(fine_ext) - 2 and fine_ext[idx + 1]["s_3d"] < target_s:
            idx += 1
        a = fine_ext[idx]
        b = fine_ext[idx + 1]
        span = b["s_3d"] - a["s_3d"]
        t = (target_s - a["s_3d"]) / span if span > 1e-8 else 0.0
        ext_sampled.append({
            "s": target_s,
            "s_horiz": a["s_horiz"] + (b["s_horiz"] - a["s_horiz"]) * t,
            "x": a["x"] + (b["x"] - a["x"]) * t,
            "y": a["y"] + (b["y"] - a["y"]) * t,
            "z": a["z"] + (b["z"] - a["z"]) * t,
            "psi": a["psi"] + (b["psi"] - a["psi"]) * t,
            "k": a["k"] + (b["k"] - a["k"]) * t,
            "grade": a["grade"] + (b["grade"] - a["grade"]) * t,
            "cliff_side": a["cliff_side"],
            "seg_name": a["seg_name"],
        })

    # Uzatma kısmının yönelimlerini (fwd, right, up, bank_deg) hesapla;
    # ilk 937 noktanın (preserved[0..936]) yönelim vektörlerine DOKUNMA (birebir aynı kalsın)
    combined_for_ext = [preserved[-1]] + ext_sampled
    total_horiz_end = s_horiz_start + ext_horiz_len
    _compute_frames_orientation(combined_for_ext, s_horiz_start + 8.0, total_horiz_end - 165.0)

    all_frames = preserved + combined_for_ext[1:]

    # Segment başlangıç/bitiş 3B mesafelerini güncelle
    for sm in seg_meta:
        seg_frames = [f["s"] for f in all_frames if f["seg_name"] == sm["name"]]
        if seg_frames:
            sm["start_3d_m"] = round(min(seg_frames), 1)
            sm["end_3d_m"] = round(max(seg_frames), 1)
        else:
            sm["start_3d_m"] = round(sm["start_m"], 1)
            sm["end_3d_m"] = round(sm["end_m"], 1)

    return all_frames, seg_meta, sampled_p1


# ---------------------------------------------------------------------------
# Yardımcı Mesh Oluşturucu (Blender)
# ---------------------------------------------------------------------------
class MapMeshBuilder:
    def __init__(self, name, materials):
        self.name = name
        self.materials = materials if isinstance(materials, list) else [materials]
        assert len(self.materials) <= 2, f"{name}: en fazla 2 malzeme olabilir"
        self.verts = []
        self.faces = []

    def add_vert_u(self, ux, uy, uz):
        idx = len(self.verts)
        self.verts.append(u2b(ux, uy, uz))
        return idx

    def add_quad(self, i0, i1, i2, i3, mat_idx=0):
        self.faces.append(((i0, i1, i2, i3), mat_idx))

    def add_tri(self, i0, i1, i2, mat_idx=0):
        self.faces.append(((i0, i1, i2), mat_idx))

    def extrude_profile(self, frames, profile, k0, k1, mat_map=None, close_caps=False):
        m_pts = len(profile)
        rings = []
        for k in range(k0, k1 + 1):
            f = frames[k]
            px, py, pz = f["x"], f["y"], f["z"]
            rx, ry, rz = f["right"]
            ux, uy, uz = f["up"]
            ring = []
            for x_lat, y_off in profile:
                vx = px + rx * x_lat + ux * y_off
                vy = py + ry * x_lat + uy * y_off
                vz = pz + rz * x_lat + uz * y_off
                ring.append(self.add_vert_u(vx, vy, vz))
            rings.append(ring)

        for step_idx in range(len(rings) - 1):
            r_cur = rings[step_idx]
            r_nxt = rings[step_idx + 1]
            for j in range(m_pts - 1):
                m_idx = mat_map.get(j, 0) if mat_map else 0
                self.add_quad(r_cur[j], r_cur[j + 1], r_nxt[j + 1], r_nxt[j], mat_idx=m_idx)

        if close_caps and m_pts >= 3:
            r_first = rings[0]
            r_last = rings[-1]
            self.faces.append((tuple(reversed(r_first)), 0))
            self.faces.append((tuple(r_last), 0))

    def add_oriented_box(self, center_u, fwd_u, right_u, up_u, size_x, size_y, size_z, mat_idx=0):
        cx, cy, cz = center_u
        fx, fy, fz = fwd_u
        rx, ry, rz = right_u
        ux, uy, uz = up_u
        hx, hy, hz = size_x * 0.5, size_y * 0.5, size_z * 0.5

        def pt(sx, sy, sz):
            return self.add_vert_u(
                cx + rx * sx + ux * sy + fx * sz,
                cy + ry * sx + uy * sy + fy * sz,
                cz + rz * sx + uz * sy + fz * sz,
            )

        v0 = pt(-hx, -hy, -hz)
        v1 = pt(+hx, -hy, -hz)
        v2 = pt(+hx, -hy, +hz)
        v3 = pt(-hx, -hy, +hz)
        v4 = pt(-hx, +hy, -hz)
        v5 = pt(+hx, +hy, -hz)
        v6 = pt(+hx, +hy, +hz)
        v7 = pt(-hx, +hy, +hz)

        self.add_quad(v4, v5, v6, v7, mat_idx)
        self.add_quad(v1, v0, v3, v2, mat_idx)
        self.add_quad(v3, v2, v6, v7, mat_idx)
        self.add_quad(v1, v0, v4, v5, mat_idx)
        self.add_quad(v1, v2, v6, v5, mat_idx)
        self.add_quad(v3, v0, v4, v7, mat_idx)

    def build(self, recalc_normals=False):
        mesh = bpy.data.meshes.new(self.name + "_mesh")
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)

        bm = bmesh.new()
        bm_verts = [bm.verts.new(v) for v in self.verts]
        bm.verts.ensure_lookup_table()
        for f_verts, mat_idx in self.faces:
            try:
                f = bm.faces.new([bm_verts[i] for i in f_verts])
                f.material_index = mat_idx
            except ValueError:
                try:
                    dup_verts = [bm.verts.new(self.verts[i]) for i in f_verts]
                    f = bm.faces.new(dup_verts)
                    f.material_index = mat_idx
                except ValueError:
                    pass

        if recalc_normals:
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.002)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        else:
            bm.normal_update()

        bm.to_mesh(mesh)
        bm.free()

        for m in self.materials:
            mesh.materials.append(m)

        obj.location = Vector((0.0, 0.0, 0.0))
        obj.rotation_euler = Euler((0.0, 0.0, 0.0), 'XYZ')
        obj.scale = Vector((1.0, 1.0, 1.0))
        return obj


# ---------------------------------------------------------------------------
# 2. Yol, Çizgiler, Bariyerler, Tüneller ve Kemerler
# ---------------------------------------------------------------------------
def build_road_and_markings(frames, mats):
    n = len(frames) - 1
    hw = HALF_WIDTH          # 7.0 m
    sh = hw + SHOULDER       # 7.6 m

    road_mb = MapMeshBuilder("Yol_Yuzey", [mats["Mat_Asfalt"]])
    road_profile = [
        (-sh, -0.35),
        (-sh,  0.00),
        (-3.5, 0.00),
        ( 0.0, 0.00),
        ( 3.5, 0.00),
        ( sh,  0.00),
        ( sh, -0.35),
    ]
    road_mb.extrude_profile(frames, road_profile, 0, n)
    road_obj = road_mb.build()

    line_mb = MapMeshBuilder("Yol_Cizgiler", [mats["Mat_YolCizgi"]])
    left_edge = [(-(hw - 0.02), LINE_HEIGHT), (-(hw - 0.24), LINE_HEIGHT)]
    right_edge = [(hw - 0.24, LINE_HEIGHT), (hw - 0.02, LINE_HEIGHT)]
    center_dash = [(-0.11, LINE_HEIGHT), (0.11, LINE_HEIGHT)]

    line_mb.extrude_profile(frames, left_edge, 0, n)
    line_mb.extrude_profile(frames, right_edge, 0, n)

    k = 0
    while k < n:
        if (k // 2) % 2 == 0:
            k_end = min(n, k + 2)
            line_mb.extrude_profile(frames, center_dash, k, k_end)
            k = k_end
        else:
            k += 1

    finish_m = frames[-1]["s"] - FINISH_MARGIN
    for mark_s in [START_M + 4.0, finish_m]:
        k_mark = min(n - 1, max(0, int(round(mark_s / SAMPLE_STEP))))
        line_mb.extrude_profile(frames, [(-hw, LINE_HEIGHT + 0.004), (hw, LINE_HEIGHT + 0.004)], k_mark, k_mark + 1)

    line_obj = line_mb.build()
    return road_obj, line_obj


def build_barriers(frames, mats):
    """
    İlk 937 noktadaki (0..936) bariyerler A1'deki 125'lik dilimlerle (Bariyer_01..08) birebir korunur.
    Yeni bölüm (936..n) Bariyer_09..16 olarak 8 dilimde üretilir.
    """
    n = len(frames) - 1
    xi = HALF_WIDTH + SHOULDER             # 7.60 m (bariyer iç yüzü)
    xo = xi + BARRIER_THICK                # 8.25 m (bariyer dış yüzü)

    left_prof = [
        (-xo, -BARRIER_DEPTH),
        (-xo,  0.00),
        (-xo + 0.12, BARRIER_HEIGHT),
        (-xi - 0.10, BARRIER_HEIGHT),
        (-xi - 0.07, 0.60),   # j=3 -> Reflektör şeridi (submesh 1)
        (-xi,  0.00),
        (-xi, -BARRIER_DEPTH),
    ]
    right_prof = [
        ( xi, -BARRIER_DEPTH),
        ( xi,  0.00),
        ( xi + 0.07, 0.60),   # j=2 -> Reflektör şeridi (submesh 1)
        ( xi + 0.10, BARRIER_HEIGHT),
        ( xo - 0.12, BARRIER_HEIGHT),
        ( xo,  0.00),
        ( xo, -BARRIER_DEPTH),
    ]

    barrier_objs = []
    # 1) Korunan ilk bölüm: Bariyer_01..08 (0..936)
    p1_chunk = 125
    for c in range(8):
        k0 = c * p1_chunk
        k1 = min(PRESERVED_P1_LAST_IDX, (c + 1) * p1_chunk)
        if k0 >= k1:
            continue
        b_mb = MapMeshBuilder(f"Bariyer_{c + 1:02d}", [mats["Mat_Bariyer"], mats["Mat_Reflektor"]])
        b_mb.extrude_profile(frames, left_prof, k0, k1, mat_map={3: 1})
        b_mb.extrude_profile(frames, right_prof, k0, k1, mat_map={2: 1})
        barrier_objs.append(b_mb.build())

    # 2) Yeni uzatma bölümü: Bariyer_09..16 (936..n)
    ext_steps = n - PRESERVED_P1_LAST_IDX
    ext_chunks = 8
    ext_chunk_size = math.ceil(ext_steps / ext_chunks)
    for c in range(ext_chunks):
        k0 = PRESERVED_P1_LAST_IDX + c * ext_chunk_size
        k1 = min(n, PRESERVED_P1_LAST_IDX + (c + 1) * ext_chunk_size)
        if k0 >= k1:
            continue
        b_mb = MapMeshBuilder(f"Bariyer_{c + 9:02d}", [mats["Mat_Bariyer"], mats["Mat_Reflektor"]])
        b_mb.extrude_profile(frames, left_prof, k0, k1, mat_map={3: 1})
        b_mb.extrude_profile(frames, right_prof, k0, k1, mat_map={2: 1})
        barrier_objs.append(b_mb.build())

    for wall_name, f_idx, offset_dir in [
        ("Bariyer_BaslangicDuvari", 0, -1.0),
        ("Bariyer_SonDuvar", n, +1.0),
    ]:
        f = frames[f_idx]
        fx, fy, fz = f["fwd"]
        ux, uy, uz = f["up"]
        cx = f["x"] + fx * offset_dir * 1.2 + ux * 0.8
        cy = f["y"] + fy * offset_dir * 1.2 + uy * 0.8
        cz = f["z"] + fz * offset_dir * 1.2 + uz * 0.8
        w_mb = MapMeshBuilder(wall_name, [mats["Mat_Bariyer"], mats["Mat_Reflektor"]])
        w_mb.add_oriented_box((cx, cy, cz), f["fwd"], f["right"], f["up"], xo * 2.0, 3.2, 1.2, mat_idx=0)
        w_mb.add_oriented_box((cx - fx * offset_dir * 0.62, cy + 0.2, cz - fz * offset_dir * 0.62),
                              f["fwd"], f["right"], f["up"], xo * 1.8, 0.35, 0.08, mat_idx=1)
        barrier_objs.append(w_mb.build())

    return barrier_objs


def find_frame_range_for_segment(frames, seg_name):
    indices = [i for i, f in enumerate(frames) if f["seg_name"] == seg_name]
    return min(indices), max(indices)


def build_tunnels(frames, mats):
    tunnel_specs = [
        ("Tunel_01", "Tunel_1_KayaGecidi"),
        ("Tunel_02", "Tunel_2_UzunKivrimli"),
    ]

    inner_arch_r2l = [
        ( 8.20, -0.50),
        ( 8.20,  5.80),
        ( 7.00,  7.65),
        ( 4.20,  8.80),
        ( 0.65,  9.20),
        (-0.65,  9.20),
        (-4.20,  8.80),
        (-7.00,  7.65),
        (-8.20,  5.80),
        (-8.20, -0.50),
    ]

    outer_arch_l2r = [
        (-10.80, -1.50),
        (-10.80,  6.80),
        ( -8.40,  9.60),
        ( -4.80, 11.10),
        (  0.00, 11.60),
        (  4.80, 11.10),
        (  8.40,  9.60),
        ( 10.80,  6.80),
        ( 10.80, -1.50),
    ]

    inner_portal_l2r = [
        (-8.20, -0.50),
        (-8.20,  5.80),
        (-7.00,  7.65),
        (-4.20,  8.80),
        ( 0.00,  9.20),
        ( 4.20,  8.80),
        ( 7.00,  7.65),
        ( 8.20,  5.80),
        ( 8.20, -0.50),
    ]

    tunnel_objs = []
    tunnel_info = []

    for t_name, seg_name in tunnel_specs:
        k0, k1 = find_frame_range_for_segment(frames, seg_name)
        t_mb = MapMeshBuilder(t_name, [mats["Mat_TunelBeton"], mats["Mat_TunelIsik"]])

        t_mb.extrude_profile(frames, inner_arch_r2l, k0, k1, mat_map={4: 1})
        t_mb.extrude_profile(frames, outer_arch_l2r, k0, k1)

        for end_k, is_entrance in [(k0, True), (k1, False)]:
            f = frames[end_k]
            px, py, pz = f["x"], f["y"], f["z"]
            rx, ry, rz = f["right"]
            ux, uy, uz = f["up"]

            in_ids = []
            out_ids = []
            for (ix, iy), (ox, oy) in zip(inner_portal_l2r, outer_arch_l2r):
                in_ids.append(t_mb.add_vert_u(
                    px + rx * ix + ux * iy,
                    py + ry * ix + uy * iy,
                    pz + rz * ix + uz * iy,
                ))
                out_ids.append(t_mb.add_vert_u(
                    px + rx * ox + ux * oy,
                    py + ry * ox + uy * oy,
                    pz + rz * ox + uz * oy,
                ))

            for j in range(len(in_ids) - 1):
                if is_entrance:
                    t_mb.add_quad(in_ids[j + 1], in_ids[j], out_ids[j], out_ids[j + 1], mat_idx=0)
                else:
                    t_mb.add_quad(in_ids[j], in_ids[j + 1], out_ids[j + 1], out_ids[j], mat_idx=0)

        t_obj = t_mb.build()
        tunnel_objs.append(t_obj)
        tunnel_info.append({
            "name": t_name,
            "start_m": round(frames[k0]["s"], 1),
            "end_m": round(frames[k1]["s"], 1),
            "length_m": round(frames[k1]["s"] - frames[k0]["s"], 1),
            "k0": k0,
            "k1": k1,
            "min_ceiling_m": 7.65,
            "max_ceiling_m": 9.20,
        })

    return tunnel_objs, tunnel_info


def build_arches(frames, mats):
    finish_m = frames[-1]["s"] - FINISH_MARGIN
    arch_objs = []
    for name, target_s in [("Kemer_Baslangic", START_M + 4.0), ("Kemer_Bitis", finish_m)]:
        k = min(len(frames) - 1, max(0, int(round(target_s / SAMPLE_STEP))))
        f = frames[k]
        px, py, pz = f["x"], f["y"], f["z"]
        rx, ry, rz = f["right"]
        ux, uy, uz = f["up"]
        fx, fy, fz = f["fwd"]

        mb = MapMeshBuilder(name, [mats["Mat_Bariyer"], mats["Mat_Reflektor"]])
        post_x = HALF_WIDTH + SHOULDER + BARRIER_THICK + 0.55  # 8.80 m
        beam_h = 8.20  # >= 7.0 m!

        for side in (-1.0, 1.0):
            cx = px + rx * (side * post_x) + ux * (beam_h * 0.5)
            cy = py + ry * (side * post_x) + uy * (beam_h * 0.5)
            cz = pz + rz * (side * post_x) + uz * (beam_h * 0.5)
            mb.add_oriented_box((cx, cy, cz), (fx, fy, fz), (rx, ry, rz), (ux, uy, uz),
                                0.65, beam_h + 1.0, 0.65, mat_idx=0)

        top_c = (px + ux * (beam_h + 0.45), py + uy * (beam_h + 0.45), pz + uz * (beam_h + 0.45))
        mb.add_oriented_box(top_c, (fx, fy, fz), (rx, ry, rz), (ux, uy, uz),
                            post_x * 2.0 + 0.8, 0.95, 0.75, mat_idx=0)
        mb.add_oriented_box(top_c, (fx, fy, fz), (rx, ry, rz), (ux, uy, uz),
                            post_x * 1.85, 0.35, 0.82, mat_idx=1)

        arch_objs.append(mb.build())
    return arch_objs


# ---------------------------------------------------------------------------
# 2.5 Dağ Arazisi Üretimi (İlk bölümde Arazi_Yamac_01..04 aynen korunur,
#     yeni bölümde Arazi_Yamac_05..08 + tüm dağ için Arazi_Dis_01..08)
# ---------------------------------------------------------------------------
def build_detailed_terrain(frames, sampled_p1, tunnel_info, mats):
    n = len(frames) - 1
    xo = HALF_WIDTH + SHOULDER + BARRIER_THICK  # 8.25 m

    # İlk bölüm (0..936) için A1'deki cs_smooth değerlerini birebir koru (yerleşim yükseklikleri değişmesin)
    cs_raw_p1 = [float(f["cliff_side"]) for f in sampled_p1]
    cs_smooth_p1 = []
    win_cs = 10
    for i in range(len(sampled_p1)):
        lo = max(0, i - win_cs)
        hi = min(len(sampled_p1) - 1, i + win_cs)
        cs_smooth_p1.append(sum(cs_raw_p1[lo:hi + 1]) / (hi - lo + 1))

    cs_raw_all = [float(f["cliff_side"]) for f in frames]
    cs_smooth_all = []
    for i in range(len(frames)):
        if i <= PRESERVED_P1_LAST_IDX:
            cs_smooth_all.append(cs_smooth_p1[i])
        else:
            lo = max(0, i - win_cs)
            hi = min(len(frames) - 1, i + win_cs)
            cs_smooth_all.append(sum(cs_raw_all[lo:hi + 1]) / (hi - lo + 1))

    terrain_objs = []
    l_xs = [-44.0, -34.0, -25.5, -19.0, -14.2, -10.8, -9.2, -xo]
    r_xs = [xo, 9.2, 10.8, 14.2, 19.0, 25.5, 34.0, 44.0]
    dists = [xo, 9.2, 10.8, 14.2, 19.0, 25.5, 34.0, 44.0]

    tunnel_k_ranges = [(t["k0"], t["k1"]) for t in tunnel_info]

    def is_inside_tunnel_k(k_idx):
        for tk0, tk1 in tunnel_k_ranges:
            if tk0 <= k_idx <= tk1:
                return True
        return False

    # A) İlk bölüm yamaçları: Arazi_Yamac_01..04 (k = 0..936, A1 ile birebir aynı formül)
    p1_chunk_size = 250
    for c in range(4):
        k0 = c * p1_chunk_size
        k1 = min(PRESERVED_P1_LAST_IDX, (c + 1) * p1_chunk_size)
        if k0 >= k1:
            continue

        second_mat = mats["Mat_Kar"] if c >= 2 else mats["Mat_ToprakKar"]
        t_mb = MapMeshBuilder(f"Arazi_Yamac_{c + 1:02d}", [mats["Mat_Kaya"], second_mat])

        left_rings = []
        right_rings = []
        frame_ys = []

        for k in range(k0, k1 + 1):
            f = frames[k]
            px, py, pz = f["x"], f["y"], f["z"]
            rx, _, rz = f["right_flat"]
            s_val = f["s"]
            cs = cs_smooth_all[k]

            w1 = math.sin(s_val * 0.065 + 0.4)
            w2 = math.cos(s_val * 0.042 - 0.9)

            l_ridge_t = 0.5 * (cs + 1.0)
            r_ridge_t = 0.5 * (1.0 - cs)

            def calc_bank_profile_p1(ridge_t, sign_side):
                hs = []
                for idx_d, d in enumerate(dists):
                    if idx_d == 0:
                        hs.append(-0.42)
                        continue
                    u = (d - xo) / (44.0 - xo)
                    crag = (1.4 * math.sin(s_val * 0.08 + d * 0.22 * sign_side)
                            + 0.8 * math.cos(s_val * 0.15 - d * 0.31))
                    h_ridge = (1.2 * (u ** 0.75) * 14.5) + abs(w1) * 1.8 * u + crag * u
                    h_cliff = (-1.1 * (u ** 1.15) * 18.5) - abs(w2) * 1.5 * u + crag * 0.6 * u
                    h_val = h_cliff * (1.0 - ridge_t) + h_ridge * ridge_t
                    if idx_d == len(dists) - 1:
                        h_val = min(h_val - 6.5, -14.5 + w1 * 0.8)
                    elif idx_d == len(dists) - 2:
                        h_val = min(h_val, 8.5 * ridge_t - 10.5 * (1.0 - ridge_t))
                    hs.append(h_val)
                return hs

            l_heights = list(reversed(calc_bank_profile_p1(l_ridge_t, -1.0)))
            r_heights = calc_bank_profile_p1(r_ridge_t, +1.0)

            l_ring = []
            for lx, lh in zip(l_xs, l_heights):
                vx, vy, vz = px + rx * lx, py + lh, pz + rz * lx
                if k > 890 and abs(lx) > xo + 0.5:
                    for m in range(PRESERVED_P1_LAST_IDX + 5, min(n + 1, PRESERVED_P1_LAST_IDX + 110)):
                        fm = frames[m]
                        dm = math.hypot(vx - fm["x"], vz - fm["z"])
                        if dm < 24.0:
                            vy = min(vy, fm["y"] - 1.2 - max(0.0, (16.0 - dm) * 0.45))
                l_ring.append(t_mb.add_vert_u(vx, vy, vz))

            r_ring = []
            for rx_off, rh in zip(r_xs, r_heights):
                vx, vy, vz = px + rx * rx_off, py + rh, pz + rz * rx_off
                if k > 890 and abs(rx_off) > xo + 0.5:
                    for m in range(PRESERVED_P1_LAST_IDX + 5, min(n + 1, PRESERVED_P1_LAST_IDX + 110)):
                        fm = frames[m]
                        dm = math.hypot(vx - fm["x"], vz - fm["z"])
                        if dm < 24.0:
                            vy = min(vy, fm["y"] - 1.2 - max(0.0, (16.0 - dm) * 0.45))
                r_ring.append(t_mb.add_vert_u(vx, vy, vz))

            left_rings.append(l_ring)
            right_rings.append(r_ring)
            frame_ys.append(py)

        n_lat = len(l_xs) - 1
        for idx in range(len(left_rings) - 1):
            k_cur = k0 + idx
            if is_inside_tunnel_k(k_cur) or is_inside_tunnel_k(k_cur + 1):
                continue
            py_cur = frame_ys[idx]
            lr0, lr1 = left_rings[idx], left_rings[idx + 1]
            rr0, rr1 = right_rings[idx], right_rings[idx + 1]
            for j in range(n_lat):
                is_terrace_L = j in (0, 1, 4, 5)
                is_terrace_R = j in (1, 2, 5, 6)
                if c >= 2:
                    m_L = 1 if (py_cur > 126.0 and is_terrace_L) else 0
                    m_R = 1 if (py_cur > 126.0 and is_terrace_R) else 0
                else:
                    m_L = 1 if is_terrace_L else 0
                    m_R = 1 if is_terrace_R else 0
                t_mb.add_quad(lr0[j], lr0[j + 1], lr1[j + 1], lr1[j], mat_idx=m_L)
                t_mb.add_quad(rr0[j], rr0[j + 1], rr1[j + 1], rr1[j], mat_idx=m_R)

        t_mb.extrude_profile(frames, [(xo, -BARRIER_DEPTH), (-xo, -BARRIER_DEPTH)], k0, k1)
        terrain_objs.append(t_mb.build())

    # B) Yeni uzatma bölümü yamaçları: Arazi_Yamac_05..08 (k = 936..n)
    # Üst üste dizilmiş 7 firkete boyunca yolu dağ yamacına oturtur, alttaki yolun yukarıdan görünmesini sağlar
    ext_steps = n - PRESERVED_P1_LAST_IDX
    ext_chunks = 4
    ext_chunk_size = math.ceil(ext_steps / ext_chunks)
    road_sub_ext = frames[max(0, PRESERVED_P1_LAST_IDX - 40)::2]

    # Viraj içlerinde (eğrilik merkezinde) yanal profillerin kendi üstüne katlanmasını (bowtie/diken)
    # önlemek için yumuşatılmış eğrilik (curvature) dizisi:
    k_raw_all = [float(f["k"]) for f in frames]
    k_smooth_all = []
    win_k = 12
    for i in range(len(frames)):
        lo = max(0, i - win_k)
        hi = min(len(frames) - 1, i + win_k)
        # Yakın çevredeki en keskin eğriliği dikkate al ki firkete giriş/çıkışında da iç uzanım daralsın
        local_ks = k_raw_all[lo:hi + 1]
        avg_k = sum(local_ks) / len(local_ks)
        max_k = max(local_ks, key=lambda v: abs(v))
        k_smooth_all.append(0.4 * avg_k + 0.6 * max_k)

    for c in range(ext_chunks):
        k0 = PRESERVED_P1_LAST_IDX + c * ext_chunk_size
        k1 = min(n, PRESERVED_P1_LAST_IDX + (c + 1) * ext_chunk_size)
        if k0 >= k1:
            continue

        t_mb = MapMeshBuilder(f"Arazi_Yamac_{c + 5:02d}", [mats["Mat_Kaya"], mats["Mat_Kar"]])
        left_rings = []
        right_rings = []

        for k in range(k0, k1 + 1):
            f = frames[k]
            px, py, pz = f["x"], f["y"], f["z"]
            rx, _, rz = f["right_flat"]
            s_val = f["s"]
            cs = cs_smooth_all[k]
            k_curv = k_smooth_all[k]

            w1 = math.sin(s_val * 0.065 + 0.4)
            w2 = math.cos(s_val * 0.042 - 0.9)

            l_ridge_t = 0.5 * (cs + 1.0)
            r_ridge_t = 0.5 * (1.0 - cs)

            # İç viraj tarafında maksimum yanal uzanımı eğrilik yarıçapının %42'si ile sınırla
            # (böylece firkete göbeğinde iç halkalar asla çakışmaz, pürüzsüz iç yamaç oluşur)
            max_reach_L = 44.0
            max_reach_R = 44.0
            if k_curv < -1e-4:  # Sola dönüş -> sol taraf iç viraj
                r_curv = 1.0 / abs(k_curv)
                max_reach_L = max(xo + 4.2, min(44.0, r_curv * 0.42))
            elif k_curv > 1e-4:  # Sağa dönüş -> sağ taraf iç viraj
                r_curv = 1.0 / abs(k_curv)
                max_reach_R = max(xo + 4.2, min(44.0, r_curv * 0.42))

            def calc_bank_profile_ext(ridge_t, sign_side, max_reach):
                hs = []
                eff_dists = []
                scale_span = (max_reach - xo) / (44.0 - xo)
                for idx_d, d in enumerate(dists):
                    d_eff = xo + (d - xo) * scale_span
                    eff_dists.append(d_eff)
                    if idx_d == 0:
                        hs.append(-0.42)
                        continue
                    u = (d - xo) / (44.0 - xo)
                    crag = (0.65 * math.sin(s_val * 0.07 + d * 0.18 * sign_side)
                            + 0.35 * math.cos(s_val * 0.13 - d * 0.25)) * scale_span
                    # Üst üste firketelerde iç yamaç yumuşakça yükselir, dış yamaç (uçurum) alt katmana iner
                    h_ridge = (1.0 * (u ** 0.75) * 7.5 * scale_span) + abs(w1) * 0.7 * u * scale_span + crag * 0.5 * u
                    h_cliff = (-1.15 * (u ** 1.08) * 19.5) - abs(w2) * 1.1 * u + crag * 0.4 * u
                    h_val = h_cliff * (1.0 - ridge_t) + h_ridge * ridge_t
                    if idx_d == len(dists) - 1:
                        h_val = min(h_val - 9.0, -21.0 + w1 * 0.5)
                    elif idx_d == len(dists) - 2:
                        h_val = min(h_val, 3.5 * ridge_t * scale_span - 13.5 * (1.0 - ridge_t))
                    hs.append(h_val)
                return eff_dists, hs

            l_eff_dists, l_hs_from_road = calc_bank_profile_ext(l_ridge_t, -1.0, max_reach_L)
            l_xs_eff = [-d for d in reversed(l_eff_dists)]
            l_heights = list(reversed(l_hs_from_road))
            r_xs_eff, r_heights = calc_bank_profile_ext(r_ridge_t, +1.0, max_reach_R)

            def clamp_against_other_tiers(vx, vy, vz, cur_s, lat_abs):
                if lat_abs <= xo + 0.5:
                    return vy
                for fm in road_sub_ext:
                    if abs(fm["s"] - cur_s) < 35.0:
                        continue
                    dm = math.hypot(vx - fm["x"], vz - fm["z"])
                    if dm < 30.0:
                        safe_ceil = fm["y"] - 1.25 - max(0.0, (18.0 - dm) * 0.45)
                        if vy > safe_ceil:
                            vy = safe_ceil
                return vy

            l_ring = []
            for lx, lh in zip(l_xs_eff, l_heights):
                vx, vy, vz = px + rx * lx, py + lh, pz + rz * lx
                vy = clamp_against_other_tiers(vx, vy, vz, s_val, abs(lx))
                l_ring.append(t_mb.add_vert_u(vx, vy, vz))

            r_ring = []
            for rx_off, rh in zip(r_xs_eff, r_heights):
                vx, vy, vz = px + rx * rx_off, py + rh, pz + rz * rx_off
                vy = clamp_against_other_tiers(vx, vy, vz, s_val, abs(rx_off))
                r_ring.append(t_mb.add_vert_u(vx, vy, vz))

            left_rings.append(l_ring)
            right_rings.append(r_ring)

        n_lat = len(l_xs) - 1
        for idx in range(len(left_rings) - 1):
            lr0, lr1 = left_rings[idx], left_rings[idx + 1]
            rr0, rr1 = right_rings[idx], right_rings[idx + 1]
            for j in range(n_lat):
                # Yol omuzu ve üst teraslar karlı (1), dış dik falez etekleri kaya (0)
                is_snow_L = j >= 3 if c >= 1 else j in (4, 5, 6)
                is_snow_R = j <= 3 if c >= 1 else j in (0, 1, 2)
                m_L = 1 if is_snow_L else 0
                m_R = 1 if is_snow_R else 0
                t_mb.add_quad(lr0[j], lr0[j + 1], lr1[j + 1], lr1[j], mat_idx=m_L)
                t_mb.add_quad(rr0[j], rr0[j + 1], rr1[j + 1], rr1[j], mat_idx=m_R)

        t_mb.extrude_profile(frames, [(xo, -BARRIER_DEPTH), (-xo, -BARRIER_DEPTH)], k0, k1)
        terrain_objs.append(t_mb.build())

    # C) Tünellerin üstündeki dağ sırtı kütlesi (Arazi_TunelDagi_01, Arazi_TunelDagi_02 — A1 ile birebir aynı)
    for idx_t, tinfo in enumerate(tunnel_info):
        k0, k1 = tinfo["k0"], tinfo["k1"]
        second_mat = mats["Mat_Kar"] if idx_t == 1 else mats["Mat_ToprakKar"]
        td_mb = MapMeshBuilder(f"Arazi_TunelDagi_{idx_t + 1:02d}", [mats["Mat_Kaya"], second_mat])

        xs_cap = [-28.0, -18.5, -10.8, -8.4, -4.8, 0.0, 4.8, 8.4, 10.8, 18.5, 28.0]
        base_hs = [-5.0,  11.5,  17.5, 21.5, 24.8, 26.5, 24.8, 21.5, 17.5, 11.5, -5.0]
        portal_hs = [-4.5, -1.5,  6.70, 9.55, 11.05, 11.55, 11.05, 9.55, 6.70, -1.5, -4.5]
        portal_top_hs = [-4.5, 4.2, 9.8, 13.0, 14.8, 15.5, 14.8, 13.0, 9.8, 4.2, -4.5]

        top_rings = []
        bot_rings = []

        for k in range(k0, k1 + 1):
            f = frames[k]
            px, py, pz = f["x"], f["y"], f["z"]
            rx, ry, rz = f["right"]
            ux, uy, uz = f["up"]
            u = (k - k0) / max(1, (k1 - k0))
            arch_factor = math.sin(math.pi * u) ** 0.70
            t_ring = []
            b_ring = []
            for x_lat, bh, ph, pth in zip(xs_cap, base_hs, portal_hs, portal_top_hs):
                rough = 0.75 * math.sin(f["s"] * 0.09 + x_lat * 0.25) * math.sin(math.pi * u)
                h_top = pth + (bh - pth) * arch_factor + rough
                t_ring.append(td_mb.add_vert_u(
                    px + rx * x_lat + ux * h_top,
                    py + ry * x_lat + uy * h_top,
                    pz + rz * x_lat + uz * h_top,
                ))
                b_ring.append(td_mb.add_vert_u(
                    px + rx * x_lat + ux * ph,
                    py + ry * x_lat + uy * ph,
                    pz + rz * x_lat + uz * ph,
                ))
            top_rings.append(t_ring)
            bot_rings.append(b_ring)

        for step_i in range(len(top_rings) - 1):
            r0, r1 = top_rings[step_i], top_rings[step_i + 1]
            for j in range(len(xs_cap) - 1):
                m_idx = 1 if (3 <= j <= 6) else 0
                td_mb.add_quad(r0[j], r0[j + 1], r1[j + 1], r1[j], mat_idx=m_idx)

        for end_idx in (0, -1):
            tr = top_rings[end_idx]
            br = bot_rings[end_idx]
            f_end = frames[k0 if end_idx == 0 else k1]
            px, py, pz = f_end["x"], f_end["y"], f_end["z"]
            rx, ry, rz = f_end["right"]
            ux, uy, uz = f_end["up"]
            for j in range(1, len(xs_cap) - 2):
                td_mb.add_quad(br[j], br[j + 1], tr[j + 1], tr[j], mat_idx=0)
                td_mb.add_quad(br[j + 1], br[j], tr[j], tr[j + 1], mat_idx=0)

            for side_sign, j_outer, j_far in [(-1.0, 2, 1), (+1.0, 8, 9)]:
                v_in_bot = td_mb.add_vert_u(px + rx * (side_sign * 8.20) - ux * 2.00, py + ry * (side_sign * 8.20) - uy * 2.00, pz + rz * (side_sign * 8.20) - uz * 2.00)
                v_out_bot = td_mb.add_vert_u(px + rx * (side_sign * 18.50) - ux * 2.00, py + ry * (side_sign * 18.50) - uy * 2.00, pz + rz * (side_sign * 18.50) - uz * 2.00)
                v_in_top = td_mb.add_vert_u(px + rx * (side_sign * 8.20) + ux * 6.70, py + ry * (side_sign * 8.20) + uy * 6.70, pz + rz * (side_sign * 8.20) + uz * 6.70)
                v_out_top = br[j_outer]
                v_far_top = tr[j_far]
                td_mb.add_quad(v_in_bot, v_out_bot, v_out_top, v_in_top, mat_idx=0)
                td_mb.add_quad(v_out_bot, v_in_bot, v_in_top, v_out_top, mat_idx=0)
                td_mb.add_tri(v_out_bot, v_far_top, v_out_top, mat_idx=0)
                td_mb.add_tri(v_out_bot, v_out_top, v_far_top, mat_idx=0)

        terrain_objs.append(td_mb.build())

    # D) Çevreleyen geniş dağ arazisi (2x4 = 8 blok: Arazi_Dis_01..08)
    xs_all = [f["x"] for f in frames]
    zs_all = [f["z"] for f in frames]
    margin = 165.0
    x_min, x_max = min(xs_all) - margin, max(xs_all) + margin
    z_min, z_max = min(zs_all) - margin, max(zs_all) + margin

    nx_total, nz_total = 80, 128
    dx = (x_max - x_min) / nx_total
    dz = (z_max - z_min) / nz_total

    grid_y = [[0.0] * (nx_total + 1) for _ in range(nz_total + 1)]
    sampled_sub = frames[::3]

    for iz in range(nz_total + 1):
        gz = z_min + iz * dz
        for ix in range(nx_total + 1):
            gx = x_min + ix * dx
            w_sum = 0.0
            wy_sum = 0.0
            nearby_road_ceilings = []
            min_d2 = 1e18
            near_y = 0.0
            for f in sampled_sub:
                d2 = (gx - f["x"]) ** 2 + (gz - f["z"]) ** 2
                if d2 < min_d2:
                    min_d2 = d2
                    near_y = f["y"]
                w = 1.0 / ((d2 + 85.0 ** 2) ** 2)
                w_sum += w
                wy_sum += w * f["y"]
                if d2 < 95.0 ** 2:
                    nearby_road_ceilings.append((math.sqrt(d2), f["y"]))

            smooth_base_y = wy_sum / w_sum if w_sum > 0 else near_y

            ridge = (11.0 * math.sin(gx * 0.011 + 0.7) * math.cos(gz * 0.009 - 0.3)
                     + 4.5 * math.sin(gx * 0.027 - 1.2) * math.cos(gz * 0.024 + 0.8))
            peaks = 24.0 * max(0.0, math.sin(gx * 0.0065 - 1.1) * math.sin(gz * 0.0065 + 0.4))
            natural_h = smooth_base_y - 6.5 + ridge + peaks

            h = natural_h
            for dist_f, fy_val in nearby_road_ceilings:
                if dist_f < 13.0:
                    safe_h = fy_val - 2.8 - (13.0 - dist_f) * 0.25
                    h = min(h, safe_h)
                elif dist_f < 42.0:
                    safe_h = fy_val - 2.8 - (dist_f - 13.0) * 0.20
                    h = min(h, safe_h)
                else:
                    t_blend = min(1.0, (dist_f - 42.0) / 53.0)
                    safe_h = fy_val - 8.6
                    h = min(h, safe_h * (1.0 - t_blend) + natural_h * t_blend)

            grid_y[iz][ix] = h

    step_x = nx_total // 2
    step_z = nz_total // 4
    chunk_idx = 1
    for cz in range(4):
        z0_i = cz * step_z
        z1_i = nz_total if cz == 3 else (cz + 1) * step_z
        for cx in range(2):
            x0_i = cx * step_x
            x1_i = nx_total if cx == 1 else (cx + 1) * step_x
            has_high_snow = (cz >= 1)
            first_mat = mats["Mat_Kar"] if has_high_snow else mats["Mat_ToprakKar"]
            g_mb = MapMeshBuilder(f"Arazi_Dis_{chunk_idx:02d}", [first_mat, mats["Mat_Kaya"]])
            chunk_idx += 1

            vid = {}
            for iz in range(z0_i, z1_i + 1):
                gz = z_min + iz * dz
                for ix in range(x0_i, x1_i + 1):
                    gx = x_min + ix * dx
                    gy = grid_y[iz][ix]
                    vid[(ix, iz)] = g_mb.add_vert_u(gx, gy, gz)

            for iz in range(z0_i, z1_i):
                for ix in range(x0_i, x1_i):
                    v00 = vid[(ix, iz)]
                    v10 = vid[(ix + 1, iz)]
                    v11 = vid[(ix + 1, iz + 1)]
                    v01 = vid[(ix, iz + 1)]
                    gy_avg = 0.25 * (grid_y[iz][ix] + grid_y[iz][ix + 1] + grid_y[iz + 1][ix] + grid_y[iz + 1][ix + 1])
                    slope = abs(grid_y[iz + 1][ix] - grid_y[iz][ix]) + abs(grid_y[iz][ix + 1] - grid_y[iz][ix])
                    if has_high_snow:
                        m_idx = 0 if (gy_avg > 125.0 and slope < 4.5) else 1
                    else:
                        m_idx = 1 if (gy_avg > 95.0 or slope > 3.8) else 0
                    g_mb.add_quad(v00, v10, v11, v01, mat_idx=m_idx)

            terrain_objs.append(g_mb.build())

    return terrain_objs


# ---------------------------------------------------------------------------
# 3. Mevcut Çevre Yerleşimini Koruma ve Çakışanları Temizleme (Dag_Yerlesim.json)
# ---------------------------------------------------------------------------
def update_placements_for_phase1_extension(frames_all, sampled_p1, seg_meta_p1, tunnel_info):
    """
    A2 1. Aşama Kuralı:
    - İlk bölümdeki mevcut çevre yerleşimi (Dag_Yerlesim.json) aynen kalır.
    - Yeni yolun (936..son) üstünden geçtiği veya bariyerine 3.5 m'den fazla yaklaştığı
      (merkezden < 12.5 m) mevcut ağaçlar/öğeler ve kaldırılan eski bitiş düzlüğündeki
      (indeks > 936) havada kalacak öğeler yerleşimden çıkarılır.
    - Bu aşamada yeni bölüme çevre detayı eklenmez.
    """
    existing_path = "/home/zoro/harvester/Harvester/Assets/Maps/DagTirmanisi/Dag_Yerlesim.json"
    if not os.path.exists(existing_path):
        existing_path = os.path.join(SCRIPT_DIR, "Dag_Yerlesim.json")

    raw_items = []
    if os.path.exists(existing_path):
        with open(existing_path, "r", encoding="utf-8") as fp:
            raw_items = json.load(fp)

    new_road_pts = [(f["x"], f["y"], f["z"]) for f in frames_all[PRESERVED_P1_LAST_IDX:]]
    old_road_pts = [(f["x"], f["y"], f["z"]) for f in sampled_p1]

    filtered = []
    removed_on_new_road = 0
    removed_old_finish = 0
    counts = {
        "Agac_Cam_01": 0,
        "Agac_Ladin_01": 0,
        "Agac_KarliCam_01": 0,
        "Cimen_Obek_01": 0,
        "Kaya_Buyuk_01": 0,
        "Kaya_Orta_01": 0,
        "Tabela_KeskinViraj": 0,
        "Tabela_KayaDusebilir": 0,
    }

    for item in raw_items:
        px, py, pz = item["p"]
        # 1) Yeni yolun (936..son) sürüş ve bariyer koridoruna (< 12.5 m) girenleri çıkar
        min_d2_new = min((px - rx) ** 2 + (pz - rz) ** 2 for rx, ry, rz in new_road_pts)
        if min_d2_new < (12.5 ** 2):
            removed_on_new_road += 1
            continue

        # 2) Kaldırılan eski bitiş düzlüğüne (indeks > 936) ait olanları çıkar
        best_old_idx = min(
            range(len(old_road_pts)),
            key=lambda i: (px - old_road_pts[i][0]) ** 2 + (pz - old_road_pts[i][2]) ** 2,
        )
        if best_old_idx > PRESERVED_P1_LAST_IDX:
            removed_old_finish += 1
            continue

        filtered.append(item)
        m_name = item["model"]
        if m_name in counts:
            counts[m_name] += 1

    print(
        f"[Yerleşim] İlk bölüm korundu: {len(filtered)} öğe kaldı "
        f"(yeni yol üstünden çıkarılan: {removed_on_new_road}, eski bitiş düzlüğünden çıkarılan: {removed_old_finish})"
    )
    return filtered, counts, removed_on_new_road, removed_old_finish


# ---------------------------------------------------------------------------
# 4. Geometri, Yarıçap, Eğim, Kesişmeme ve Açıklık Doğrulama
# ---------------------------------------------------------------------------
def verify_track(frames, sampled_p1, seg_meta, tunnel_info, placement_counts, removed_new_road, removed_old_finish):
    total_len = frames[-1]["s"]
    max_step = 0.0
    max_grade = 0.0
    max_grade_s = 0.0
    ext_max_grade = 0.0
    ext_max_grade_s = 0.0
    max_bank = max(abs(f["bank_deg"]) for f in frames)

    # 1) İlk 937 noktanın (0..936, ~2.340 m) birebir korunduğunu doğrula
    max_p1_diff = 0.0
    for i in range(PRESERVED_P1_LAST_IDX + 1):
        dx = abs(frames[i]["x"] - sampled_p1[i]["x"])
        dy = abs(frames[i]["y"] - sampled_p1[i]["y"])
        dz = abs(frames[i]["z"] - sampled_p1[i]["z"])
        max_p1_diff = max(max_p1_diff, dx, dy, dz)
    assert max_p1_diff < 1e-9, f"İlk 937 nokta değişmiş! max_p1_diff={max_p1_diff}"

    # 2) Nokta aralığı ve eğim kontrolü
    for i in range(len(frames) - 1):
        a, b = frames[i], frames[i + 1]
        dx = b["x"] - a["x"]
        dy = b["y"] - a["y"]
        dz = b["z"] - a["z"]
        d3 = math.sqrt(dx * dx + dy * dy + dz * dz)
        dh = math.hypot(dx, dz)
        max_step = max(max_step, d3)
        if dh > 1e-4:
            g = abs(dy) / dh
            if g > max_grade:
                max_grade = g
                max_grade_s = a["s"]
            if i >= PRESERVED_P1_LAST_IDX and g > ext_max_grade:
                ext_max_grade = g
                ext_max_grade_s = a["s"]

    # 3) Yarıçap kontrolü (tüm harita ve yeni uzatma bölümü)
    min_center_r = 1e9
    min_r_s = 0.0
    ext_min_center_r = 1e9
    ext_min_r_s = 0.0
    k_win = 3
    for i in range(k_win, len(frames) - k_win):
        p0 = frames[i - k_win]
        p1 = frames[i]
        p2 = frames[i + k_win]
        ab = math.hypot(p1["x"] - p0["x"], p1["z"] - p0["z"])
        bc = math.hypot(p2["x"] - p1["x"], p2["z"] - p1["z"])
        ca = math.hypot(p0["x"] - p2["x"], p0["z"] - p2["z"])
        cross = abs((p1["x"] - p0["x"]) * (p2["z"] - p0["z"]) - (p1["z"] - p0["z"]) * (p2["x"] - p0["x"]))
        if cross > 1e-5:
            r = (ab * bc * ca) / (2.0 * cross)
            if r < min_center_r:
                min_center_r = r
                min_r_s = p1["s"]
            if i >= PRESERVED_P1_LAST_IDX and r < ext_min_center_r:
                ext_min_center_r = r
                ext_min_r_s = p1["s"]

    min_inner_r = min_center_r - HALF_WIDTH
    ext_min_inner_r = ext_min_center_r - HALF_WIDTH

    # 4) Yolun kendisiyle kesişmeme (Self-Intersection) kontrolü
    # Komşu olmayan yol kolları (|j - i| >= 40, yani yol boyunca >= 100 m uzaktaki kollar) arası en kısa 2B ve 3B mesafe
    min_nonadj_2d_dist = 1e9
    min_nonadj_pair = None
    ext_min_nonadj_2d_dist = 1e9
    ext_min_nonadj_pair = None
    for i in range(len(frames)):
        xi, yi, zi = frames[i]["x"], frames[i]["y"], frames[i]["z"]
        for j in range(i + 40, len(frames)):
            xj, yj, zj = frames[j]["x"], frames[j]["y"], frames[j]["z"]
            d2 = (xi - xj) ** 2 + (zi - zj) ** 2
            if d2 < min_nonadj_2d_dist:
                min_nonadj_2d_dist = d2
                min_nonadj_pair = (i, j, round(math.sqrt(d2), 2), round(abs(yi - yj), 2))
            if i >= PRESERVED_P1_LAST_IDX and d2 < ext_min_nonadj_2d_dist:
                ext_min_nonadj_2d_dist = d2
                ext_min_nonadj_pair = (i, j, round(math.sqrt(d2), 2), round(abs(yi - yj), 2))

    min_nonadj_2d_m = math.sqrt(min_nonadj_2d_dist)
    ext_min_nonadj_2d_m = math.sqrt(ext_min_nonadj_2d_dist)
    assert min_nonadj_2d_m >= (ROAD_WIDTH + 20.0), f"Yol kolları birbirine çok yakın: {min_nonadj_2d_m:.2f} m"

    # 5) Yolun araziyle ve engellerle kesişmeme (Road-Terrain / Corridor Clearance) kontrolü
    depsgraph = bpy.context.evaluated_depsgraph_get()
    obstacle_trees = []
    terrain_only_trees = []
    for o in bpy.data.objects:
        if o.type == 'MESH' and not o.name.startswith("Yol_") and "Duvar" not in o.name:
            bvh = BVHTree.FromObject(o, depsgraph)
            obstacle_trees.append((o.name, bvh))
            if o.name.startswith("Arazi_"):
                terrain_only_trees.append((o.name, bvh))

    min_overhead_clearance = 1e9
    terrain_road_intersections = 0
    for k in range(2, len(frames) - 2):
        f = frames[k]
        px, py, pz = f["x"], f["y"], f["z"]
        rx, ry, rz = f["right"]
        ux, uy, uz = f["up"]
        up_bl = Vector(u2b(ux, uy, uz)).normalized()
        down_bl = -up_bl

        # a) Sürüş koridoru (x_lat = -6.5 .. +6.5 m) içinde yoldan (+0.20 m) yukarıya (15 m) tavan/engel kontrolü
        for x_lat in (-6.5, -3.2, 0.0, 3.2, 6.5):
            orig_u = (
                px + rx * x_lat + ux * 0.20,
                py + ry * x_lat + uy * 0.20,
                pz + rz * x_lat + uz * 0.20,
            )
            orig_bl = Vector(u2b(*orig_u))
            for ob_name, tree in obstacle_trees:
                loc, norm, idx, dist = tree.ray_cast(orig_bl, up_bl, 15.0)
                if loc is not None:
                    clearance = dist + 0.20
                    min_overhead_clearance = min(min_overhead_clearance, clearance)
                    assert clearance >= 7.0, (
                        f"Sürüş koridorunda tavan/arazi çakışması! {ob_name} s={f['s']:.1f}m "
                        f"x_lat={x_lat}m yükseklik={clearance:.2f}m (< 7.0m)"
                    )

            # b) Yol yüzeyinin hemen üstünden (+0.50 m) aşağıya (-0.45 m, yani yol üst yüzeyi +0.05 m'ye kadar)
            # ters yönlü arazi yüzeyi kesişmesi var mı kontrolü
            top_probe_u = (
                px + rx * x_lat + ux * 0.50,
                py + ry * x_lat + uy * 0.50,
                pz + rz * x_lat + uz * 0.50,
            )
            top_probe_bl = Vector(u2b(*top_probe_u))
            for ob_name, tree in terrain_only_trees:
                loc, norm, idx, dist = tree.ray_cast(top_probe_bl, down_bl, 0.48)
                if loc is not None:
                    terrain_road_intersections += 1
                    raise AssertionError(
                        f"Arazi yol yüzeyini kesiyor! {ob_name} s={f['s']:.1f}m x_lat={x_lat}m"
                    )

    hairpins = [s for s in seg_meta if "Firkete" in s["name"]]
    new_hairpins = [s for s in hairpins if s["start_m"] >= 2300.0]

    total_tris = sum(
        sum(max(0, len(p.vertices) - 2) for p in o.data.polygons)
        for o in bpy.data.objects if o.type == 'MESH'
    )
    terrain_tris = sum(
        sum(max(0, len(p.vertices) - 2) for p in o.data.polygons)
        for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith("Arazi_")
    )
    road_tris = total_tris - terrain_tris

    assert 4800.0 <= total_len <= 5250.0, f"Uzunluk ~5 km değil: {total_len}"
    assert max_step <= 4.0, f"Nokta aralığı > 4 m: {max_step}"
    assert max_grade <= 0.12, f"Eğim > %12: {max_grade * 100:.2f}%"
    assert min_inner_r >= 12.0, f"Firkete iç yarıçapı < 12 m: {min_inner_r:.2f}"
    assert len(new_hairpins) >= 6, f"Yeni firkete sayısı < 6: {len(new_hairpins)}"
    assert terrain_tris <= 350000, f"Arazi üçgen sayısı > 350k: {terrain_tris}"
    assert terrain_road_intersections == 0, f"Arazi-yol kesişmesi: {terrain_road_intersections}"

    stats = {
        "uzunluk_m": round(total_len, 1),
        "korunan_ilk_bolum_uzunluk_m": round(frames[PRESERVED_P1_LAST_IDX]["s"], 1),
        "korunan_ilk_bolum_nokta_sayisi": PRESERVED_P1_LAST_IDX + 1,
        "korunan_ilk_bolum_maks_fark_m": max_p1_diff,
        "yeni_bolum_uzunluk_m": round(total_len - frames[PRESERVED_P1_LAST_IDX]["s"], 1),
        "nokta_sayisi": len(frames),
        "max_nokta_araligi_m": round(max_step, 3),
        "yol_genisligi_m": ROAD_WIDTH,
        "baslangic_m": START_M,
        "bitis_m": round(total_len - FINISH_MARGIN, 1),
        "baslangic_duzlugu_m": 95.0,
        "bitis_duzlugu_m": 165.0,
        "en_dik_egim_yuzde": round(max_grade * 100.0, 2),
        "en_dik_egim_konum_m": round(max_grade_s, 1),
        "yeni_bolum_en_dik_egim_yuzde": round(ext_max_grade * 100.0, 2),
        "yeni_bolum_en_dik_egim_konum_m": round(ext_max_grade_s, 1),
        "en_fazla_yanal_egim_derece": round(max_bank, 2),
        "en_dar_merkez_yaricap_m": round(min_center_r, 2),
        "en_dar_ic_yaricap_m": round(min_inner_r, 2),
        "en_dar_yaricap_konum_m": round(min_r_s, 1),
        "yeni_bolum_en_dar_merkez_yaricap_m": round(ext_min_center_r, 2),
        "yeni_bolum_en_dar_ic_yaricap_m": round(ext_min_inner_r, 2),
        "yeni_bolum_en_dar_yaricap_konum_m": round(ext_min_r_s, 1),
        "en_kisa_komsu_olmayan_yol_mesafesi_2b_m": round(min_nonadj_2d_m, 2),
        "yeni_bolum_firkete_kollari_arasi_en_kisa_2b_m": round(ext_min_nonadj_2d_m, 2),
        "yol_kendisiyle_kesisme": 0,
        "yol_araziyle_kesisme": terrain_road_intersections,
        "en_alcak_tavan_acikligi_m": round(min_overhead_clearance, 2),
        "irtifa_baslangic_m": round(frames[0]["y"], 2),
        "irtifa_eski_bitis_m": round(frames[PRESERVED_P1_LAST_IDX]["y"], 2),
        "irtifa_bitis_m": round(frames[-1]["y"], 2),
        "toplam_firkete_sayisi": len(hairpins),
        "yeni_firkete_sayisi": len(new_hairpins),
        "firketeler": [
            {
                "ad": h["name"],
                "baslangic_m": h["start_3d_m"],
                "bitis_m": h["end_3d_m"],
                "aci_derece": h["turn_deg"],
            }
            for h in hairpins
        ],
        "tuneller": tunnel_info,
        "arazi_ucgen": terrain_tris,
        "yol_bariyer_tunel_ucgen": road_tris,
        "toplam_ucgen": total_tris,
        "mesh_sayisi": sum(1 for o in bpy.data.objects if o.type == 'MESH'),
        "yerlesim_sayilari": placement_counts,
        "yerlesimden_cikarilan_yeni_yol_ustu": removed_new_road,
        "yerlesimden_cikarilan_eski_bitis": removed_old_finish,
    }
    print("=== DOĞRULAMA GEÇTİ ===")
    print(json.dumps(stats, indent=2, ensure_ascii=False))
    return stats


# ---------------------------------------------------------------------------
# 5. Önizleme Görseli Üretimi (Üstten Harita + Yeni Üst Üste Firketeler + Zirve)
# ---------------------------------------------------------------------------
def spawn_preview_props(placements, mats):
    proto_map = {
        "Agac_Cam_01":          cevre_modelleri.build_pine_tree(mats, is_lod1=False, name_prefix="Proto_Agac_Cam"),
        "Agac_Ladin_01":        cevre_modelleri.build_spruce_tree(mats, is_lod1=False, name_prefix="Proto_Agac_Ladin"),
        "Agac_KarliCam_01":     cevre_modelleri.build_snow_pine_tree(mats, is_lod1=False, name_prefix="Proto_Agac_KarliCam"),
        "Cimen_Obek_01":        cevre_modelleri.build_grass_clump(mats, is_lod1=True, name_prefix="Proto_Cimen"),
        "Kaya_Buyuk_01":        cevre_modelleri.build_rock_mesh("Proto_Kaya_Buyuk", [mats["Mat_Kaya"], mats["Mat_Kar"]], 3.6, 2.8, 3.2, subdiv=2, seed=101, snow_cap=True),
        "Kaya_Orta_01":         cevre_modelleri.build_rock_mesh("Proto_Kaya_Orta", [mats["Mat_Kaya"]], 1.8, 1.35, 1.6, subdiv=2, seed=202, snow_cap=False),
        "Tabela_KeskinViraj":   cevre_modelleri.build_chevron_sign(mats, is_lod1=False, name_prefix="Proto_Tabela_Viraj"),
        "Tabela_KayaDusebilir": cevre_modelleri.build_hazard_sign(mats, is_lod1=False, name_prefix="Proto_Tabela_Kaya"),
    }
    for po in proto_map.values():
        po.hide_render = True
        po.hide_viewport = True

    temp_objs = list(proto_map.values())
    scene_col = bpy.context.scene.collection

    for idx, item in enumerate(placements):
        m_name = item["model"]
        proto = proto_map.get(m_name)
        if proto is None:
            continue
        inst = bpy.data.objects.new(f"PrevInst_{idx}", proto.data)
        px, py, pz = item["p"]
        inst.location = Vector(u2b(px, py, pz))
        inst.rotation_euler = Euler((0.0, 0.0, -math.radians(item["r"])), 'XYZ')
        sc = float(item["s"])
        inst.scale = Vector((sc, sc, sc))
        scene_col.objects.link(inst)
        temp_objs.append(inst)

    return temp_objs


def cleanup_preview_props(temp_objs):
    meshes_to_check = set()
    for o in temp_objs:
        if o.data is not None:
            meshes_to_check.add(o.data)
        bpy.data.objects.remove(o, do_unlink=True)
    for m in meshes_to_check:
        if m.users == 0:
            bpy.data.meshes.remove(m)


def render_previews(frames, placements, mats, preview_dir):
    os.makedirs(preview_dir, exist_ok=True)
    scene = bpy.context.scene

    temp_prop_objs = spawn_preview_props(placements, mats)
    bpy.context.view_layer.update()

    world = bpy.data.worlds.new("MapPreviewWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.12, 0.15, 0.22, 1.0)
        bg.inputs[1].default_value = 1.15

    sun_data = bpy.data.lights.new("PreviewSun", type='SUN')
    sun_data.energy = 3.4
    sun_data.color = (0.90, 0.94, 1.0)
    sun_obj = bpy.data.objects.new("PreviewSun", sun_data)
    scene.collection.objects.link(sun_obj)
    sun_obj.rotation_euler = Euler((math.radians(40.0), math.radians(-26.0), math.radians(28.0)), 'XYZ')

    headlight_data = bpy.data.lights.new("PreviewHeadlight", type='POINT')
    headlight_data.energy = 1800.0
    headlight_data.color = (1.0, 0.92, 0.78)
    headlight_data.shadow_soft_size = 1.5
    headlight_obj = bpy.data.objects.new("PreviewHeadlight", headlight_data)
    scene.collection.objects.link(headlight_obj)

    xs_bl = [-f["x"] for f in frames]
    ys_bl = [-f["z"] for f in frames]
    zs_bl = [f["y"] for f in frames]

    cx = 0.5 * (min(xs_bl) + max(xs_bl))
    cy = 0.5 * (min(ys_bl) + max(ys_bl))
    span = max(max(xs_bl) - min(xs_bl), max(ys_bl) - min(ys_bl)) + 280.0

    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_obj = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam_obj)
    scene.camera = cam_obj

    rendered_paths = []

    # 1) Üstten (Top-Down) 5 km Tam Harita Görünümü
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = span
    cam_data.clip_start = 1.0
    cam_data.clip_end = 3500.0
    cam_obj.location = Vector((cx, cy, max(zs_bl) + 650.0))
    cam_obj.rotation_euler = Euler((0.0, 0.0, 0.0), 'XYZ')
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 1400
    top_path = os.path.join(preview_dir, "dag_tirmanisi_ustten.png")
    scene.render.filepath = top_path
    bpy.ops.render.render(write_still=True)
    rendered_paths.append(top_path)

    # 2) Yeni Firketeler Genel Perspektif Görünümü (Üst üste dizilmiş 7 yeni firkete yamacı)
    cam_data.type = 'PERSP'
    cam_data.lens = 26.0
    f_mid_sw = frames[1380]  # Firkete 9 civarı orta yamaç
    cam_pos = Vector(u2b(f_mid_sw["x"] - 260.0, f_mid_sw["y"] + 165.0, f_mid_sw["z"] - 210.0))
    target_pos = Vector(u2b(f_mid_sw["x"] + 25.0, f_mid_sw["y"] - 10.0, f_mid_sw["z"] + 40.0))
    cam_obj.location = cam_pos
    cam_obj.rotation_euler = (target_pos - cam_pos).to_track_quat('-Z', 'Y').to_euler()
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    for out_name in ("dag_tirmanisi_perspektif.png", "yeni_firketeler_01_genel.png"):
        out_p = os.path.join(preview_dir, out_name)
        scene.render.filepath = out_p
        bpy.ops.render.render(write_still=True)
        rendered_paths.append(out_p)

    # 3) Üst Firketeden Alt Firketelere Bakış (Alttaki yolun yukarıdan görünmesi)
    cam_data.lens = 24.0
    f_upper = frames[1625]  # Kar_Yamaci_2 / Firkete 11 çıkışı (y ~ 365 m)
    f_lower = frames[1245]  # Alttaki Terase_2 / Firkete 8 katmanı (y ~ 268 m)
    c_u = (f_upper["x"] - 14.0, f_upper["y"] + 8.5, f_upper["z"] - 12.0)
    t_u = (f_lower["x"] + 25.0, f_lower["y"] + 4.0, f_lower["z"] - 10.0)
    c_bl = Vector(u2b(*c_u))
    t_bl = Vector(u2b(*t_u))
    cam_obj.location = c_bl
    cam_obj.rotation_euler = (t_bl - c_bl).to_track_quat('-Z', 'Y').to_euler()
    headlight_obj.location = c_bl + Vector((0.0, 0.0, 1.5))
    sw_view_path = os.path.join(preview_dir, "yeni_firketeler_02_ustten_bakis.png")
    scene.render.filepath = sw_view_path
    bpy.ops.render.render(write_still=True)
    rendered_paths.append(sw_view_path)

    # 4) Zirve Bitiş Düzlüğü ve Bitiş Kemeri Görünümü
    fc = frames[len(frames) - 52]
    ft = frames[len(frames) - 16]
    rx, ry, rz = fc["right"]
    ux, uy, uz = fc["up"]
    c_u = (fc["x"] + rx * 1.5 + ux * 2.4, fc["y"] + ry * 1.5 + uy * 2.4, fc["z"] + rz * 1.5 + uz * 2.4)
    t_u = (ft["x"], ft["y"] + 2.0, ft["z"])
    c_bl = Vector(u2b(*c_u))
    t_bl = Vector(u2b(*t_u))
    cam_obj.location = c_bl
    cam_obj.rotation_euler = (t_bl - c_bl).to_track_quat('-Z', 'Y').to_euler()
    headlight_obj.location = c_bl + Vector((0.0, 0.0, 1.2))
    for out_name in ("yol_seviyesi_04_karli_zirve.png", "yeni_firketeler_03_zirve_bitis.png"):
        out_p = os.path.join(preview_dir, out_name)
        scene.render.filepath = out_p
        bpy.ops.render.render(write_still=True)
        rendered_paths.append(out_p)

    bpy.data.objects.remove(cam_obj, do_unlink=True)
    bpy.data.cameras.remove(cam_data)
    bpy.data.objects.remove(sun_obj, do_unlink=True)
    bpy.data.lights.remove(sun_data)
    bpy.data.objects.remove(headlight_obj, do_unlink=True)
    bpy.data.lights.remove(headlight_data)
    cleanup_preview_props(temp_prop_objs)
    return rendered_paths


# ---------------------------------------------------------------------------
# Ana Akış
# ---------------------------------------------------------------------------
def export_terrain_only_fbx(terrain_objs, filepath):
    bpy.ops.object.select_all(action='DESELECT')
    for o in terrain_objs:
        o.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=filepath,
        use_selection=True,
        use_visible=False,
        axis_forward='-Z',
        axis_up='Y',
        bake_space_transform=True,
        apply_scale_options='FBX_SCALE_ALL',
        object_types={'EMPTY', 'MESH'},
        bake_anim=False,
        mesh_smooth_type='OFF',
    )
    bpy.ops.object.select_all(action='DESELECT')


def build_all(export_to_assets=False):
    clear_scene()

    mats = {
        "Mat_Asfalt":     make_mat("Mat_Asfalt",     (0.18, 0.19, 0.21), roughness=0.72),
        "Mat_YolCizgi":   make_mat("Mat_YolCizgi",   (0.88, 0.86, 0.76), roughness=0.50),
        "Mat_Bariyer":    make_mat("Mat_Bariyer",    (0.46, 0.47, 0.50), roughness=0.65),
        "Mat_Reflektor":  make_mat("Mat_Reflektor",  (0.95, 0.38, 0.08), roughness=0.35,
                                   emission=(1.2, 0.45, 0.08), emission_strength=1.5),
        "Mat_TunelBeton": make_mat("Mat_TunelBeton", (0.28, 0.29, 0.32), roughness=0.80),
        "Mat_TunelIsik":  make_mat("Mat_TunelIsik",  (1.00, 0.92, 0.75), roughness=0.20,
                                   emission=(2.2, 1.9, 1.4), emission_strength=2.0),
        "Mat_Kaya":       make_mat("Mat_Kaya",       (0.32, 0.29, 0.26), roughness=0.90),
        "Mat_ToprakKar":  make_mat("Mat_ToprakKar",  (0.22, 0.28, 0.20), roughness=0.88),
        "Mat_Kar":        make_mat("Mat_Kar",        (0.86, 0.90, 0.95), roughness=0.65),
        "Mat_AgacGovde":  make_mat("Mat_AgacGovde",  (0.26, 0.17, 0.10), roughness=0.90),
        "Mat_YaprakCam":  make_mat("Mat_YaprakCam",  (0.11, 0.24, 0.14), roughness=0.85),
    }

    frames, seg_meta, sampled_p1 = generate_centerline()
    build_road_and_markings(frames, mats)
    build_barriers(frames, mats)
    _, tunnel_info = build_tunnels(frames, mats)
    build_arches(frames, mats)
    terrain_objs = build_detailed_terrain(frames, sampled_p1, tunnel_info, mats)

    bpy.context.view_layer.update()

    # İlk bölümün çevre yerleşimini koru, yeni yolun üstünden geçtiklerini çıkar
    placements, placement_counts, removed_new_road, removed_old_finish = update_placements_for_phase1_extension(
        frames, sampled_p1, seg_meta, tunnel_info
    )

    # Doğrulama
    stats = verify_track(
        frames, sampled_p1, seg_meta, tunnel_info,
        placement_counts, removed_new_road, removed_old_finish
    )

    base_src_dir = "/home/zoro/harvester/Harvester/BlenderSources/maps/DagTirmanisi"
    os.makedirs(base_src_dir, exist_ok=True)
    preview_dir = os.path.join(base_src_dir, "onizleme")

    # Önizleme görsellerini üret
    render_previews(frames, placements, mats, preview_dir)

    # Dag_Merkez.json verisi (ilk 937 nokta birebir aynı, bitis_m yeni zirveye göre güncellendi)
    finish_m = round(frames[-1]["s"] - FINISH_MARGIN, 1)
    merkez_data = {
        "genislik": int(ROAD_WIDTH),
        "noktalar": [[round(f["x"], 3), round(f["y"], 3), round(f["z"], 3)] for f in frames],
        "baslangic_m": int(START_M),
        "bitis_m": finish_m,
    }

    src_merkez_path = os.path.join(base_src_dir, "Dag_Merkez.json")
    with open(src_merkez_path, "w", encoding="utf-8") as fp:
        json.dump(merkez_data, fp, ensure_ascii=False, separators=(",", ":"))

    src_yerlesim_path = os.path.join(base_src_dir, "Dag_Yerlesim.json")
    with open(src_yerlesim_path, "w", encoding="utf-8") as fp:
        json.dump(placements, fp, ensure_ascii=False, separators=(",", ":"))

    stats_path = os.path.join(base_src_dir, "ozet_istatistik.json")
    with open(stats_path, "w", encoding="utf-8") as fp:
        json.dump(stats, fp, ensure_ascii=False, indent=2)

    src_yol_fbx = os.path.join(base_src_dir, "Dag_Yol.fbx")
    bpy.ops.export_scene.fbx(
        filepath=src_yol_fbx,
        use_selection=False,
        use_visible=False,
        axis_forward='-Z',
        axis_up='Y',
        bake_space_transform=True,
        apply_scale_options='FBX_SCALE_ALL',
        object_types={'EMPTY', 'MESH'},
        bake_anim=False,
        mesh_smooth_type='OFF',
    )

    src_arazi_fbx = os.path.join(base_src_dir, "Dag_Arazi.fbx")
    export_terrain_only_fbx(terrain_objs, src_arazi_fbx)

    if export_to_assets:
        assets_dir = "/home/zoro/harvester/Harvester/Assets/Maps/DagTirmanisi"
        os.makedirs(assets_dir, exist_ok=True)

        assets_merkez_path = os.path.join(assets_dir, "Dag_Merkez.json")
        with open(assets_merkez_path, "w", encoding="utf-8") as fp:
            json.dump(merkez_data, fp, ensure_ascii=False, separators=(",", ":"))

        assets_yerlesim_path = os.path.join(assets_dir, "Dag_Yerlesim.json")
        with open(assets_yerlesim_path, "w", encoding="utf-8") as fp:
            json.dump(placements, fp, ensure_ascii=False, separators=(",", ":"))

        assets_yol_fbx = os.path.join(assets_dir, "Dag_Yol.fbx")
        bpy.ops.export_scene.fbx(
            filepath=assets_yol_fbx,
            use_selection=False,
            use_visible=False,
            axis_forward='-Z',
            axis_up='Y',
            bake_space_transform=True,
            apply_scale_options='FBX_SCALE_ALL',
            object_types={'EMPTY', 'MESH'},
            bake_anim=False,
            mesh_smooth_type='OFF',
        )

        assets_arazi_fbx = os.path.join(assets_dir, "Dag_Arazi.fbx")
        export_terrain_only_fbx(terrain_objs, assets_arazi_fbx)
        print(f"Assets altına aktarıldı: {assets_merkez_path}, {assets_yerlesim_path}, {assets_yol_fbx}, {assets_arazi_fbx}")


if __name__ == "__main__":
    export_assets = "--export-assets" in sys.argv
    build_all(export_to_assets=export_assets)
