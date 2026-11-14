#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Harvester — A1 Dağ Tırmanışı Haritası (1. Aşama: Yol Orta Çizgisi + Kaba Yol/Tünel/Arazi)
Üretim Betiği (Blender 4.x arka plan kipinde çalışır: ~/.local/bin/blender -b -P uret_dag_tirmanisi.py)

Kurallar (docs/GOREVLER.md):
- Toplam uzunluk: 2–3 km (~2.49 km)
- Yol genişliği: her yerde 14 m (ek olarak iki yanda 0.6 m banket = bariyerler arası 15.2 m)
- Firkete sayısı: 5 adet (en az 4 zorunlu), firkete iç yarıçapı >= 12 m (en dar iç yarıçap ~16.6 m, merkez ~23.6 m)
- Eğim: <= %12 (en dik eğim ~%10.6), yanal eğim (banking) <= 6° (en fazla ~3.5°)
- Tüneller: 2 adet (Tunel_01: 112.5 m kıvrımlı; Tunel_02: 189.9 m >= 150 m ve kıvrımlı), tavan >= 7 m (7.65–9.20 m)
- Başlangıç: ~95 m düz ve eğimsiz dizilme alanı (baslangic_m = 75)
- Bitiş: ~165 m düz ve eğimsiz zirve düzlüğü (bitis_m = 2453.8)
- Dag_Merkez.json nokta aralığı: ~2.5 m (<= 4 m)
"""

import os
import sys
import json
import math
import bpy
import bmesh
from mathutils import Vector, Matrix, Euler
from mathutils.bvhtree import BVHTree


# ---------------------------------------------------------------------------
# Sabitler ve Ölçüler
# ---------------------------------------------------------------------------
ROAD_WIDTH = 14.0           # Asfalt şerit genişliği (m)
HALF_WIDTH = ROAD_WIDTH * 0.5
SHOULDER = 0.60             # Banket payı (m); bariyer iç yüzü = ±7.60 m
BARRIER_THICK = 0.65        # Bariyer kalınlığı (m); dış yüz = ±8.25 m
BARRIER_HEIGHT = 0.92       # Bariyer yol üstü yüksekliği (m)
BARRIER_DEPTH = 1.25        # Bariyer yol altı etek derinliği (m)
LINE_HEIGHT = 0.018         # Yol çizgilerinin asfalttan yüksekliği (m)

START_M = 75.0              # Başlangıç dizilişi ön sırası (Harvester ~26 m'de, 0..95 m düz)
FINISH_MARGIN = 38.0        # Bitiş kemerinden son duvara kalan güvenli fren mesafesi (m)
SAMPLE_STEP = 2.5           # Dag_Merkez.json nokta aralığı (m, <= 4.0 m)


# ---------------------------------------------------------------------------
# Koordinat Dönüşümü (Unity <-> Blender)
# ---------------------------------------------------------------------------
def u2b(ux, uy, uz):
    """
    Unity sol-elli dünya koordinatını (ux=Sağ, uy=Yukarı, uz=İleri)
    Blender sağ-elli dünya koordinatına (-ux, -uz, uy) dönüştürür.
    axis_forward='-Z', axis_up='Y', bake_space_transform=True ile FBX dışa aktarıldığında
    Unity'de tam olarak (ux, uy, uz) koordinatını verir.
    """
    return (-float(ux), -float(uz), float(uy))


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
# 1. Yol Orta Çizgisi Tasarımı (2B Klotoid + 3B Eğim Profili)
# ---------------------------------------------------------------------------
SEGMENTS = [
    # (uzunluk_m, donus_derece, hedef_egim, ad, ucurum_tarafi: -1=sol, +1=sag, 0=iki_yan_yamaç)
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
    (165.0,    0.0, 0.000, "Bitis_Duzlugu",       0),
]


def generate_centerline():
    """
    Yüksek çözünürlükte (ds = 0.25 m) eğrilik ve eğim entegrasyonu yapar,
    ardından tam SAMPLE_STEP (2.5 m) yay uzunluğu adımlarıyla örnekler.
    """
    ds = 0.25
    fine_pts = []
    raw_grades = []
    seg_meta = []

    s_acc = 0.0
    for seg_len, turn_deg, grade, name, cliff_side in SEGMENTS:
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
    for seg_len, turn_deg, grade, name, cliff_side in SEGMENTS:
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
        "seg_name": "Bitis_Duzlugu",
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
        if sampled[i]["s_horiz"] < 95.0 or sampled[i]["s_horiz"] > (total_horiz_len - 165.0):
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

    return sampled, seg_meta


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
                pass

        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.002)
        if recalc_normals:
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
# 2. Yol, Çizgiler, Bariyerler, Tüneller, Kemerler ve Kaba Arazi Üretimi
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

    num_chunks = 8
    chunk_size = math.ceil(n / num_chunks)
    barrier_objs = []
    for c in range(num_chunks):
        k0 = c * chunk_size
        k1 = min(n, (c + 1) * chunk_size)
        if k0 >= k1:
            continue
        b_mb = MapMeshBuilder(f"Bariyer_{c + 1:02d}", [mats["Mat_Bariyer"], mats["Mat_Reflektor"]])
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


def build_rough_terrain(frames, tunnel_info, mats):
    """
    Kaba dağ arazisi:
    1) Arazi_Yamac_01..04: Yolun hemen dışındaki bariyer tabanından (±8.25 m) başlayan
       iç dağ sırtı ve dış uçurum şevleri; en dış etekte (±24.5 m) aşağı kapanarak
       alt araziyle kesintisiz birleşir.
    2) Arazi_TunelDagi_01..02: Tünellerin üstünü örten kaya dağ sırtı.
    3) Arazi_Dis_01..04: Geniş kaba dağ kütlesi (yola yakın her yerde yolun altında kalır).
    """
    n = len(frames) - 1
    xo = HALF_WIDTH + SHOULDER + BARRIER_THICK  # 8.25 m

    # cliff_side değerini yumuşat (yamaç/uçurum geçişlerinde keskin basamak oluşmasın)
    cs_raw = [float(f["cliff_side"]) for f in frames]
    cs_smooth = []
    win_cs = 10
    for i in range(len(frames)):
        lo = max(0, i - win_cs)
        hi = min(len(frames) - 1, i + win_cs)
        cs_smooth.append(sum(cs_raw[lo:hi + 1]) / (hi - lo + 1))

    num_chunks = 4
    chunk_size = math.ceil(n / num_chunks)
    terrain_objs = []

    for c in range(num_chunks):
        k0 = c * chunk_size
        k1 = min(n, (c + 1) * chunk_size)
        if k0 >= k1:
            continue

        t_mb = MapMeshBuilder(f"Arazi_Yamac_{c + 1:02d}", [mats["Mat_Kaya"], mats["Mat_ToprakKar"]])

        left_rings = []
        right_rings = []
        mat_choices = []

        for k in range(k0, k1 + 1):
            f = frames[k]
            px, py, pz = f["x"], f["y"], f["z"]
            rx, ry, rz = f["right_flat"]
            s_val = f["s"]
            cs = cs_smooth[k]  # -1.0 (sol uçurum) .. +1.0 (sağ uçurum)

            wobble1 = 1.2 * math.sin(s_val * 0.07 + 0.5)
            wobble2 = 1.4 * math.cos(s_val * 0.045 - 0.8)

            # Sol taraf: cs = +1 iken dağ sırtı (+11.5 m), cs = -1 iken uçurum (-13.5 m)
            l_ridge_t = 0.5 * (cs + 1.0)
            r_ridge_t = 0.5 * (1.0 - cs)

            l_h2 = -8.5 * (1.0 - l_ridge_t) + (11.0 + abs(wobble1)) * l_ridge_t
            l_h1 = -3.2 * (1.0 - l_ridge_t) + (5.5 + abs(wobble2)) * l_ridge_t

            r_h1 = -3.2 * (1.0 - r_ridge_t) + (5.5 + abs(wobble2)) * r_ridge_t
            r_h2 = -8.5 * (1.0 - r_ridge_t) + (11.0 + abs(wobble1)) * r_ridge_t

            l_heights = [-13.5 + wobble1 * 0.5, l_h2, l_h1, -0.45]
            r_heights = [-0.45, r_h1, r_h2, -13.5 + wobble2 * 0.5]

            l_xs = [-24.0, -18.0, -12.8, -xo]
            r_xs = [xo, 12.8, 18.0, 24.0]

            l_ring = []
            for lx, lh in zip(l_xs, l_heights):
                l_ring.append(t_mb.add_vert_u(px + rx * lx, py + lh, pz + rz * lx))
            r_ring = []
            for rx_off, rh in zip(r_xs, r_heights):
                r_ring.append(t_mb.add_vert_u(px + rx * rx_off, py + rh, pz + rz * rx_off))

            left_rings.append(l_ring)
            right_rings.append(r_ring)
            mat_choices.append(1 if py > 125.0 else 0)

        for idx in range(len(left_rings) - 1):
            m_idx = mat_choices[idx]
            lr0, lr1 = left_rings[idx], left_rings[idx + 1]
            rr0, rr1 = right_rings[idx], right_rings[idx + 1]
            for j in range(3):
                t_mb.add_quad(lr0[j], lr0[j + 1], lr1[j + 1], lr1[j], mat_idx=m_idx if j == 0 else 0)
                t_mb.add_quad(rr0[j], rr0[j + 1], rr1[j + 1], rr1[j], mat_idx=m_idx if j == 2 else 0)

        t_mb.extrude_profile(frames, [(xo, -BARRIER_DEPTH), (-xo, -BARRIER_DEPTH)], k0, k1)
        terrain_objs.append(t_mb.build())

    # 2) Tünellerin üstündeki dağ sırtı kütlesi (Arazi_TunelDagi_01, Arazi_TunelDagi_02)
    for idx_t, tinfo in enumerate(tunnel_info):
        k0, k1 = tinfo["k0"], tinfo["k1"]
        td_mb = MapMeshBuilder(f"Arazi_TunelDagi_{idx_t + 1:02d}", [mats["Mat_Kaya"], mats["Mat_ToprakKar"]])
        rings = []
        xs_cap = [-22.0, -14.5, -8.5, 0.0, 8.5, 14.5, 22.0]
        base_hs = [-4.0, 12.5, 17.5, 21.5, 17.5, 12.5, -4.0]
        portal_hs = [-4.0, 7.2, 10.2, 11.65, 10.2, 7.2, -4.0]

        for k in range(k0, k1 + 1):
            f = frames[k]
            px, py, pz = f["x"], f["y"], f["z"]
            rx, ry, rz = f["right_flat"]
            u = (k - k0) / max(1, (k1 - k0))
            arch_factor = math.sin(math.pi * u)
            ring = []
            for x_lat, bh, ph in zip(xs_cap, base_hs, portal_hs):
                h = ph + (bh - ph) * arch_factor
                ring.append(td_mb.add_vert_u(px + rx * x_lat, py + h, pz + rz * x_lat))
            rings.append(ring)

        for step_i in range(len(rings) - 1):
            r0, r1 = rings[step_i], rings[step_i + 1]
            for j in range(len(xs_cap) - 1):
                m_idx = 1 if (j in (2, 3) and idx_t == 1) else 0
                td_mb.add_quad(r0[j], r0[j + 1], r1[j + 1], r1[j], mat_idx=m_idx)

        terrain_objs.append(td_mb.build())

    # 3) Çevreleyen geniş kaba dağ arazisi (2x2 = 4 blok: Arazi_Dis_01..04)
    xs_all = [f["x"] for f in frames]
    zs_all = [f["z"] for f in frames]
    margin = 160.0
    x_min, x_max = min(xs_all) - margin, max(xs_all) + margin
    z_min, z_max = min(zs_all) - margin, max(zs_all) + margin

    nx_total, nz_total = 44, 52
    dx = (x_max - x_min) / nx_total
    dz = (z_max - z_min) / nz_total

    grid_y = [[0.0] * (nx_total + 1) for _ in range(nz_total + 1)]
    sampled_sub = frames[::4]  # 10 m'de bir örnek

    for iz in range(nz_total + 1):
        gz = z_min + iz * dz
        for ix in range(nx_total + 1):
            gx = x_min + ix * dx
            min_d2 = 1e18
            near_y = 0.0
            w_sum = 0.0
            wy_sum = 0.0
            for f in sampled_sub:
                d2 = (gx - f["x"]) ** 2 + (gz - f["z"]) ** 2
                if d2 < min_d2:
                    min_d2 = d2
                    near_y = f["y"]
                w = 1.0 / ((d2 + 95.0 ** 2) ** 2)
                w_sum += w
                wy_sum += w * f["y"]
            dist = math.sqrt(min_d2)
            smooth_base_y = wy_sum / w_sum if w_sum > 0 else near_y

            ridge = 12.0 * math.sin(gx * 0.011 + 0.7) * math.cos(gz * 0.009 - 0.3)
            peaks = 24.0 * max(0.0, math.sin(gx * 0.006 - 1.1) * math.sin(gz * 0.006 + 0.4))
            natural_h = smooth_base_y - 14.0 + ridge + peaks

            if dist < 36.0:
                safe_h = near_y - 9.0 - (36.0 - dist) * 0.18
                h = min(natural_h, safe_h)
            else:
                t_blend = min(1.0, (dist - 36.0) / 55.0)
                safe_h = near_y - 9.0
                h = min(natural_h, safe_h * (1.0 - t_blend) + natural_h * t_blend)

            grid_y[iz][ix] = h

    half_x = nx_total // 2
    half_z = nz_total // 2
    chunk_Idx = 1
    for cz in range(2):
        z0_i = cz * half_z
        z1_i = nz_total if cz == 1 else half_z
        for cx in range(2):
            x0_i = cx * half_x
            x1_i = nx_total if cx == 1 else half_x
            g_mb = MapMeshBuilder(f"Arazi_Dis_{chunk_Idx:02d}", [mats["Mat_ToprakKar"], mats["Mat_Kaya"]])
            chunk_Idx += 1

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
                    m_idx = 1 if gy_avg > 95.0 else 0
                    g_mb.add_quad(v00, v10, v11, v01, mat_idx=m_idx)

            terrain_objs.append(g_mb.build())

    return terrain_objs


# ---------------------------------------------------------------------------
# 3. Geometri, 3B Açıklık (Clearance) ve Kural Doğrulama
# ---------------------------------------------------------------------------
def verify_track(frames, seg_meta, tunnel_info):
    total_len = frames[-1]["s"]
    max_step = 0.0
    max_grade = 0.0
    max_grade_s = 0.0
    max_bank = max(abs(f["bank_deg"]) for f in frames)

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

    min_center_r = 1e9
    min_r_s = 0.0
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

    min_inner_r = min_center_r - HALF_WIDTH

    # 3B BVHTree ile 14m × 7m sürüş koridorunda (tüneller, kemerler, arazi, bariyerler) engel kontrolü!
    depsgraph = bpy.context.evaluated_depsgraph_get()
    obstacle_trees = []
    for o in bpy.data.objects:
        if o.type == 'MESH' and not o.name.startswith("Yol_") and "Duvar" not in o.name:
            obstacle_trees.append((o.name, BVHTree.FromObject(o, depsgraph)))

    min_overhead_clearance = 1e9
    for k in range(2, len(frames) - 2):
        f = frames[k]
        px, py, pz = f["x"], f["y"], f["z"]
        rx, ry, rz = f["right"]
        ux, uy, uz = f["up"]
        up_bl = Vector(u2b(ux, uy, uz)).normalized()
        for x_lat in (-6.5, 0.0, 6.5):
            orig_u = (
                px + rx * x_lat + ux * 0.25,
                py + ry * x_lat + uy * 0.25,
                pz + rz * x_lat + uz * 0.25,
            )
            orig_bl = Vector(u2b(*orig_u))
            for ob_name, tree in obstacle_trees:
                loc, norm, idx, dist = tree.ray_cast(orig_bl, up_bl, 15.0)
                if loc is not None:
                    clearance = dist + 0.25
                    min_overhead_clearance = min(min_overhead_clearance, clearance)
                    assert clearance >= 7.0, (
                        f"Sürüş koridorunda tavan/engel çakışması! {ob_name} s={f['s']:.1f}m "
                        f"x_lat={x_lat}m yükseklik={clearance:.2f}m (< 7.0m)"
                    )

    hairpins = [s for s in seg_meta if "Firkete" in s["name"]]
    total_tris = sum(
        sum(max(0, len(p.vertices) - 2) for p in o.data.polygons)
        for o in bpy.data.objects if o.type == 'MESH'
    )

    assert 2000.0 <= total_len <= 3000.0, f"Uzunluk hatası: {total_len}"
    assert max_step <= 4.0, f"Nokta aralığı > 4 m: {max_step}"
    assert max_grade <= 0.12, f"Eğim > %12: {max_grade * 100:.2f}%"
    assert min_inner_r >= 12.0, f"Firkete iç yarıçapı < 12 m: {min_inner_r:.2f}"
    assert len(hairpins) >= 4, f"Firkete sayısı < 4: {len(hairpins)}"
    assert len(tunnel_info) >= 2 and max(t["length_m"] for t in tunnel_info) >= 150.0, "Tünel kuralı hatası"

    stats = {
        "uzunluk_m": round(total_len, 1),
        "nokta_sayisi": len(frames),
        "max_nokta_araligi_m": round(max_step, 3),
        "yol_genisligi_m": ROAD_WIDTH,
        "baslangic_m": START_M,
        "bitis_m": round(total_len - FINISH_MARGIN, 1),
        "baslangic_duzlugu_m": 95.0,
        "bitis_duzlugu_m": 165.0,
        "en_dik_egim_yuzde": round(max_grade * 100.0, 2),
        "en_dik_egim_konum_m": round(max_grade_s, 1),
        "en_fazla_yanal_egim_derece": round(max_bank, 2),
        "en_dar_merkez_yaricap_m": round(min_center_r, 2),
        "en_dar_ic_yaricap_m": round(min_inner_r, 2),
        "en_dar_yaricap_konum_m": round(min_r_s, 1),
        "en_alcak_tavan_acikligi_m": round(min_overhead_clearance, 2),
        "irtifa_baslangic_m": round(frames[0]["y"], 2),
        "irtifa_bitis_m": round(frames[-1]["y"], 2),
        "firketeler": [
            {
                "ad": h["name"],
                "baslangic_m": round(h["start_m"], 1),
                "bitis_m": round(h["end_m"], 1),
                "aci_derece": h["turn_deg"],
            }
            for h in hairpins
        ],
        "tuneller": tunnel_info,
        "toplam_ucgen": total_tris,
        "mesh_sayisi": sum(1 for o in bpy.data.objects if o.type == 'MESH'),
    }
    print("=== DOĞRULAMA GEÇTİ ===")
    print(json.dumps(stats, indent=2, ensure_ascii=False))
    return stats


# ---------------------------------------------------------------------------
# 4. Önizleme Görseli Üretimi (Üstten + Perspektif Görünümler)
# ---------------------------------------------------------------------------
def render_previews(frames, preview_dir):
    os.makedirs(preview_dir, exist_ok=True)
    scene = bpy.context.scene

    world = bpy.data.worlds.new("MapPreviewWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.14, 0.17, 0.22, 1.0)
        bg.inputs[1].default_value = 1.2

    # Önizleme için geçici güneş ışığı (FBX dışa aktarmadan önce silinecek)
    sun_data = bpy.data.lights.new("PreviewSun", type='SUN')
    sun_data.energy = 4.0
    sun_data.color = (1.0, 0.96, 0.88)
    sun_obj = bpy.data.objects.new("PreviewSun", sun_data)
    scene.collection.objects.link(sun_obj)
    sun_obj.rotation_euler = Euler((math.radians(35.0), math.radians(-25.0), math.radians(20.0)), 'XYZ')

    # 1) Üstten (Top-Down) Ortografik Harita Görünümü
    xs_bl = [-f["x"] for f in frames]
    ys_bl = [-f["z"] for f in frames]
    zs_bl = [f["y"] for f in frames]

    cx = 0.5 * (min(xs_bl) + max(xs_bl))
    cy = 0.5 * (min(ys_bl) + max(ys_bl))
    span = max(max(xs_bl) - min(xs_bl), max(ys_bl) - min(ys_bl)) + 260.0

    cam_data = bpy.data.cameras.new("TopDownCam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = span
    cam_data.clip_start = 1.0
    cam_data.clip_end = 2000.0
    cam_obj = bpy.data.objects.new("TopDownCam", cam_data)
    scene.collection.objects.link(cam_obj)
    cam_obj.location = Vector((cx, cy, max(zs_bl) + 500.0))
    cam_obj.rotation_euler = Euler((0.0, 0.0, 0.0), 'XYZ')
    scene.camera = cam_obj

    scene.render.resolution_x = 1400
    scene.render.resolution_y = 1400
    top_path = os.path.join(preview_dir, "dag_tirmanisi_ustten.png")
    scene.render.filepath = top_path
    bpy.ops.render.render(write_still=True)

    # 2) Perspektif (Yol + Firkete + Tünel) Görünümü
    cam_data.type = 'PERSP'
    cam_data.lens = 28.0
    f_look = frames[int(len(frames) * 0.32)]
    f_cam = frames[int(len(frames) * 0.25)]
    cam_pos = Vector(u2b(f_cam["x"] - 45.0, f_cam["y"] + 42.0, f_cam["z"] - 35.0))
    target_pos = Vector(u2b(f_look["x"], f_look["y"] + 4.0, f_look["z"]))
    cam_obj.location = cam_pos
    direction = target_pos - cam_pos
    cam_obj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()

    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    persp_path = os.path.join(preview_dir, "dag_tirmanisi_perspektif.png")
    scene.render.filepath = persp_path
    bpy.ops.render.render(write_still=True)

    bpy.data.objects.remove(cam_obj, do_unlink=True)
    bpy.data.cameras.remove(cam_data)
    bpy.data.objects.remove(sun_obj, do_unlink=True)
    bpy.data.lights.remove(sun_data)
    return [top_path, persp_path]


# ---------------------------------------------------------------------------
# Unity URP Lit .mat Dosyası Üretici (.meta YAZMAZ, yalnız .mat yazar)
# ---------------------------------------------------------------------------
def write_urp_lit_mat(filepath, mat_name, base_rgb, smoothness=0.25, metallic=0.0, emission_rgb=None):
    r, g, b = base_rgb
    if emission_rgb is not None and max(emission_rgb) > 0.0:
        er, eg, eb = emission_rgb
        keywords_block = "  m_ValidKeywords:\n  - _EMISSION"
        lightmap_flags = 2
    else:
        er, eg, eb = 0.0, 0.0, 0.0
        keywords_block = "  m_ValidKeywords: []"
        lightmap_flags = 4

    content = f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {mat_name}
  m_Shader: {{fileID: 4800000, guid: 933532a4fcc9baf4fa0491de14d08ed7, type: 3}}
  m_Parent: {{fileID: 0}}
  m_ModifiedSerializedProperties: 0
{keywords_block}
  m_InvalidKeywords: []
  m_LightmapFlags: {lightmap_flags}
  m_EnableInstancingVariants: 0
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap:
    RenderType: Opaque
  disabledShaderPasses:
  - MOTIONVECTORS
  m_LockedProperties: 
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - _BaseMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _BumpMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _DetailAlbedoMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _DetailMask:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _DetailNormalMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _EmissionMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _MainTex:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _MetallicGlossMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _OcclusionMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _ParallaxMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _SpecGlossMap:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - unity_Lightmaps:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - unity_LightmapsInd:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - unity_ShadowMasks:
        m_Texture: {{fileID: 0}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    m_Ints: []
    m_Floats:
    - _AddPrecomputedVelocity: 0
    - _AlphaClip: 0
    - _AlphaToMask: 0
    - _Blend: 0
    - _BlendModePreserveSpecular: 1
    - _BumpScale: 1
    - _ClearCoatMask: 0
    - _ClearCoatSmoothness: 0
    - _Cull: 2
    - _Cutoff: 0.5
    - _DetailAlbedoMapScale: 1
    - _DetailNormalMapScale: 1
    - _DstBlend: 0
    - _DstBlendAlpha: 0
    - _EnvironmentReflections: 1
    - _GlossMapScale: 0
    - _Glossiness: 0
    - _GlossyReflections: 0
    - _Metallic: {metallic}
    - _OcclusionStrength: 1
    - _Parallax: 0.005
    - _QueueOffset: 0
    - _ReceiveShadows: 1
    - _Smoothness: {smoothness}
    - _SmoothnessTextureChannel: 0
    - _SpecularHighlights: 1
    - _SrcBlend: 1
    - _SrcBlendAlpha: 1
    - _Surface: 0
    - _WorkflowMode: 1
    - _ZWrite: 1
    m_Colors:
    - _BaseColor: {{r: {r}, g: {g}, b: {b}, a: 1}}
    - _Color: {{r: {r}, g: {g}, b: {b}, a: 1}}
    - _EmissionColor: {{r: {er}, g: {eg}, b: {eb}, a: 1}}
    - _SpecColor: {{r: 0.19999996, g: 0.19999996, b: 0.19999996, a: 1}}
  m_BuildTextureStacks: []
  m_AllowLocking: 1
--- !u!114 &7657601005869460982
MonoBehaviour:
  m_ObjectHideFlags: 11
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: d0353a89b1f911e48b9e16bdc9f2e058, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: 
  version: 10
"""
    with open(filepath, "w", encoding="utf-8") as f:
        f.write(content)


# ---------------------------------------------------------------------------
# Ana Akış
# ---------------------------------------------------------------------------
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
    }

    frames, seg_meta = generate_centerline()
    build_road_and_markings(frames, mats)
    build_barriers(frames, mats)
    _, tunnel_info = build_tunnels(frames, mats)
    build_arches(frames, mats)
    build_rough_terrain(frames, tunnel_info, mats)

    bpy.context.view_layer.update()
    stats = verify_track(frames, seg_meta, tunnel_info)

    base_src_dir = "/home/zoro/harvester/Harvester/BlenderSources/maps/DagTirmanisi"
    os.makedirs(base_src_dir, exist_ok=True)
    preview_dir = os.path.join(base_src_dir, "onizleme")
    render_previews(frames, preview_dir)

    merkez_data = {
        "genislik": int(ROAD_WIDTH),
        "noktalar": [
            [round(f["x"], 3), round(f["y"], 3), round(f["z"], 3)]
            for f in frames
        ],
        "baslangic_m": int(START_M),
        "bitis_m": round(frames[-1]["s"] - FINISH_MARGIN, 1),
    }

    src_json_path = os.path.join(base_src_dir, "Dag_Merkez.json")
    with open(src_json_path, "w", encoding="utf-8") as fp:
        json.dump(merkez_data, fp, ensure_ascii=False, separators=(",", ":"))

    stats_path = os.path.join(base_src_dir, "ozet_istatistik.json")
    with open(stats_path, "w", encoding="utf-8") as fp:
        json.dump(stats, fp, ensure_ascii=False, indent=2)

    src_fbx_path = os.path.join(base_src_dir, "Dag_Yol.fbx")
    bpy.ops.export_scene.fbx(
        filepath=src_fbx_path,
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

    if export_to_assets:
        assets_dir = "/home/zoro/harvester/Harvester/Assets/Maps/DagTirmanisi"
        mats_dir = os.path.join(assets_dir, "Materials")
        os.makedirs(mats_dir, exist_ok=True)

        mat_defs = [
            ("Mat_Asfalt",     (0.18, 0.19, 0.21), 0.28, 0.0, None),
            ("Mat_YolCizgi",   (0.88, 0.86, 0.76), 0.35, 0.0, None),
            ("Mat_Bariyer",    (0.46, 0.47, 0.50), 0.25, 0.0, None),
            ("Mat_Reflektor",  (0.95, 0.38, 0.08), 0.60, 0.0, (1.2, 0.45, 0.08)),
            ("Mat_TunelBeton", (0.28, 0.29, 0.32), 0.20, 0.0, None),
            ("Mat_TunelIsik",  (1.00, 0.92, 0.75), 0.80, 0.0, (2.2, 1.9, 1.4)),
            ("Mat_Kaya",       (0.32, 0.29, 0.26), 0.12, 0.0, None),
            ("Mat_ToprakKar",  (0.22, 0.28, 0.20), 0.14, 0.0, None),
        ]
        for m_name, rgb, smooth, metal, emis in mat_defs:
            write_urp_lit_mat(os.path.join(mats_dir, f"{m_name}.mat"), m_name, rgb, smooth, metal, emis)

        assets_json_path = os.path.join(assets_dir, "Dag_Merkez.json")
        with open(assets_json_path, "w", encoding="utf-8") as fp:
            json.dump(merkez_data, fp, ensure_ascii=False, separators=(",", ":"))

        assets_fbx_path = os.path.join(assets_dir, "Dag_Yol.fbx")
        bpy.ops.export_scene.fbx(
            filepath=assets_fbx_path,
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
        print(f"Assets altına aktarıldı: {assets_json_path}, {assets_fbx_path}")


if __name__ == "__main__":
    export_assets = "--export-assets" in sys.argv
    build_all(export_to_assets=export_assets)
