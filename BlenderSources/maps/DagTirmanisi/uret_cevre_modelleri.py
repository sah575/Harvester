#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Harvester — A1 Dağ Tırmanışı Haritası: Çevre ve Süs Modelleri (2. Aşama)
Üretim Betiği (Blender 4.x arka plan kipinde çalışır: ~/.local/bin/blender -b -P uret_cevre_modelleri.py)

Üretilen Modeller (LOD0 <= 1.500, LOD1 <= 400 üçgen):
1. Agac_Cam_01.fbx         — Uzun dağ çamı (7 katmanlı gür taç + yan kök/dal detayları)
2. Agac_Ladin_01.fbx       — Sık dallı dağ ladini (8 katmanlı sivri taç)
3. Agac_KarliCam_01.fbx    — Karlı zirve çamı (6 katmanlı taç + kar şapkaları, Mat_YaprakCam + Mat_Kar)
4. Cimen_Obek_01.fbx       — Dağ çimeni / çalı öbeği (zengin hacimli yaprak kümeleri)
5. Kaya_Buyuk_01.fbx       — Büyük falez/yamaç kayası (köşeli low-poly kaya kütlesi + karlı üst yüzey)
6. Kaya_Orta_01.fbx        — Orta boy yol kenarı kayası (fasetli kaya kümesi)
7. Tabela_KeskinViraj.fbx  — Reflektörlü keskin viraj (şevron) uyarı tabelası (direk + levha + emisyonlu ok)
8. Tabela_KayaDusebilir.fbx— Reflektörlü kaya düşebilir / uçurum uyarı tabelası (üçgen levha + emisyon)
"""

import os
import sys
import json
import math
import random
import bpy
import bmesh
from mathutils import Vector, Euler


# ---------------------------------------------------------------------------
# Yardımcılar
# ---------------------------------------------------------------------------
def u2b(ux, uy, uz):
    """Unity (ux=Sağ, uy=Yukarı, uz=İleri) -> Blender (-ux, -uz, uy)"""
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


def make_mat(name, color, metallic=0.0, roughness=0.80, emission=None, emission_strength=0.0):
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
    return mat


class PropMeshBuilder:
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

    def add_tri(self, i0, i1, i2, mat_idx=0):
        self.faces.append(((i0, i2, i1), mat_idx))

    def add_quad(self, i0, i1, i2, i3, mat_idx=0):
        self.faces.append(((i0, i3, i2, i1), mat_idx))

    def add_frustum(self, y0, y1, r0, r1, n_sides, mat_idx=0, twist_deg=0.0, wave_amp=0.0, seed=0, cap_bottom=False, cap_top=False):
        rng = random.Random(seed)
        ring0 = []
        ring1 = []
        twist_rad = math.radians(twist_deg)
        for i in range(n_sides):
            ang0 = (2.0 * math.pi * i) / n_sides
            ang1 = ang0 + twist_rad
            w0 = 1.0 + wave_amp * math.sin(ang0 * 3.0 + seed) + (rng.uniform(-0.06, 0.06) if wave_amp > 0 else 0.0)
            w1 = 1.0 + wave_amp * 0.5 * math.cos(ang1 * 3.0 + seed)
            y_jit = rng.uniform(-0.08, 0.08) * wave_amp if wave_amp > 0 else 0.0
            ring0.append(self.add_vert_u(math.cos(ang0) * r0 * w0, y0 + y_jit, math.sin(ang0) * r0 * w0))
            ring1.append(self.add_vert_u(math.cos(ang1) * r1 * w1, y1, math.sin(ang1) * r1 * w1))

        for i in range(n_sides):
            ni = (i + 1) % n_sides
            self.add_quad(ring0[i], ring0[ni], ring1[ni], ring1[i], mat_idx=mat_idx)

        if cap_bottom:
            c0 = self.add_vert_u(0.0, y0, 0.0)
            for i in range(n_sides):
                ni = (i + 1) % n_sides
                self.add_tri(c0, ring0[ni], ring0[i], mat_idx=mat_idx)

        if cap_top:
            c1 = self.add_vert_u(0.0, y1, 0.0)
            for i in range(n_sides):
                ni = (i + 1) % n_sides
                self.add_tri(c1, ring1[i], ring1[ni], mat_idx=mat_idx)

    def add_cone_tier(self, y_base, y_tip, r_base, n_sides, mat_idx=0, droop=0.22, wave_amp=0.18, seed=1, sub_rings=2):
        """Alt kenarları hafif sarkık, yıldızvari doğal çam/ladin dal katmanı (kapalı hacim)"""
        rng = random.Random(seed)
        tip_x = rng.uniform(-0.06, 0.06)
        tip_z = rng.uniform(-0.06, 0.06)
        tip_v = self.add_vert_u(tip_x, y_tip, tip_z)
        under_center = self.add_vert_u(0.0, y_base + droop * 0.5, 0.0)

        rings = []
        for r_idx in range(sub_rings):
            u = r_idx / float(sub_rings)  # 0 = alt dış etek, >0 = ara halka
            ring = []
            for i in range(n_sides):
                ang = (2.0 * math.pi * i) / n_sides + u * 0.06
                is_ridge = (i % 2 == 0)
                rad_scale = (1.0 + wave_amp) if is_ridge else (1.0 - wave_amp * 0.68)
                rad_scale *= (1.0 + rng.uniform(-0.04, 0.04))
                r = r_base * (1.0 - u * 0.52) * rad_scale
                y_cur = y_base + (y_tip - y_base) * (u ** 0.85 * 0.56)
                if r_idx == 0:
                    y_cur -= (droop if is_ridge else -droop * 0.30) + rng.uniform(-0.03, 0.03)
                ring.append(self.add_vert_u(math.cos(ang) * r, y_cur, math.sin(ang) * r))
            rings.append(ring)

        for r_idx in range(sub_rings - 1):
            r0 = rings[r_idx]
            r1 = rings[r_idx + 1]
            for i in range(n_sides):
                ni = (i + 1) % n_sides
                self.add_quad(r0[i], r0[ni], r1[ni], r1[i], mat_idx=mat_idx)

        top_ring = rings[-1]
        base_ring = rings[0]
        for i in range(n_sides):
            ni = (i + 1) % n_sides
            self.add_tri(top_ring[i], top_ring[ni], tip_v, mat_idx=mat_idx)
            self.add_tri(under_center, base_ring[ni], base_ring[i], mat_idx=mat_idx)

    def add_snow_crowned_tier(self, y_base, y_tip, r_base, n_sides, droop=0.22, wave_amp=0.16, seed=1, is_lod1=False):
        """Alt yarısı koyu çam yaprağı (mat 0), üst yarısı beyaz kar şapkası (mat 1) olan tek parça kesişimsiz dal katmanı"""
        rng = random.Random(seed)
        tip_x = rng.uniform(-0.05, 0.05)
        tip_z = rng.uniform(-0.05, 0.05)
        tip_v = self.add_vert_u(tip_x, y_tip, tip_z)
        under_center = self.add_vert_u(0.0, y_base + droop * 0.45, 0.0)

        base_ring = []
        snow_ring = []
        for i in range(n_sides):
            ang = (2.0 * math.pi * i) / n_sides
            is_ridge = (i % 2 == 0)
            rad_scale = (1.0 + wave_amp) if is_ridge else (1.0 - wave_amp * 0.65)
            rad_scale *= (1.0 + rng.uniform(-0.04, 0.04))

            r0 = r_base * rad_scale
            y0 = y_base - (droop if is_ridge else -droop * 0.28) + rng.uniform(-0.03, 0.03)
            base_ring.append(self.add_vert_u(math.cos(ang) * r0, y0, math.sin(ang) * r0))

            # Kar sınırı halkası (dalın ortasında zikzaklı kar eteği)
            u_snow = 0.36 if is_ridge else 0.48
            r1 = r_base * (1.0 - u_snow * 0.88) * rad_scale
            y1 = y_base + (y_tip - y_base) * u_snow
            snow_ring.append(self.add_vert_u(math.cos(ang) * r1, y1, math.sin(ang) * r1))

        for i in range(n_sides):
            ni = (i + 1) % n_sides
            # Alt etek: yeşil çam yaprağı (mat_idx = 0)
            self.add_quad(base_ring[i], base_ring[ni], snow_ring[ni], snow_ring[i], mat_idx=0)
            # Üst koni: beyaz kar örtüsü (mat_idx = 1)
            self.add_tri(snow_ring[i], snow_ring[ni], tip_v, mat_idx=1)
            # Alt taban kapaması: yeşil çam yaprağı (mat_idx = 0)
            self.add_tri(under_center, base_ring[ni], base_ring[i], mat_idx=0)

    def add_box(self, cx, cy, cz, sx, sy, sz, mat_idx=0, rot_y_deg=0.0, tilt_x_deg=0.0):
        hx, hy, hz = sx * 0.5, sy * 0.5, sz * 0.5
        ry = math.radians(rot_y_deg)
        rx = math.radians(tilt_x_deg)
        cy_r, sy_r = math.cos(ry), math.sin(ry)
        cx_r, sx_r = math.cos(rx), math.sin(rx)

        local_pts = [
            (-hx, -hy, -hz), (+hx, -hy, -hz), (+hx, -hy, +hz), (-hx, -hy, +hz),
            (-hx, +hy, -hz), (+hx, +hy, -hz), (+hx, +hy, +hz), (-hx, +hy, +hz),
        ]
        v_ids = []
        for lx, ly, lz in local_pts:
            y1 = ly * cx_r - lz * sx_r
            z1 = ly * sx_r + lz * cx_r
            x2 = lx * cy_r + z1 * sy_r
            z2 = -lx * sy_r + z1 * cy_r
            v_ids.append(self.add_vert_u(cx + x2, cy + y1, cz + z2))

        v0, v1, v2, v3, v4, v5, v6, v7 = v_ids
        self.add_quad(v4, v5, v6, v7, mat_idx)
        self.add_quad(v1, v0, v3, v2, mat_idx)
        self.add_quad(v3, v2, v6, v7, mat_idx)
        self.add_quad(v1, v0, v4, v5, mat_idx)
        self.add_quad(v2, v1, v5, v6, mat_idx)
        self.add_quad(v0, v3, v7, v4, mat_idx)

    def build(self, recalc_normals=True):
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

        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.001)
        if recalc_normals:
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bmesh.ops.triangulate(bm, faces=bm.faces)
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
# 1. Ağaç Modelleri (Agac_Cam_01, Agac_Ladin_01, Agac_KarliCam_01)
# ---------------------------------------------------------------------------
def build_pine_tree(mats, is_lod1=False, name_prefix="Agac_Cam_01"):
    obj_name = f"{name_prefix}_{'LOD1' if is_lod1 else 'LOD0'}"
    mb = PropMeshBuilder(obj_name, [mats["Mat_AgacGovde"], mats["Mat_YaprakCam"]])

    n_trunk = 6 if is_lod1 else 10
    # Kök tabanı zeminin 0.65m altına kadar iner ki eğimli yamaçta asla boşluk kalmasın
    mb.add_frustum(-0.65, 0.40, 0.66, 0.44, n_trunk, mat_idx=0, wave_amp=0.12, seed=10, cap_bottom=True)
    mb.add_frustum( 0.40, 3.40, 0.44, 0.30, n_trunk, mat_idx=0, twist_deg=8.0, seed=11)
    mb.add_frustum( 3.40, 8.20, 0.30, 0.12, n_trunk, mat_idx=0, twist_deg=8.0, seed=12, cap_top=True)

    # 6 katmanlı gür çam tacı (toplam yükseklik ~11.6 m)
    tiers = [
        (2.00,  4.60, 2.95, 0.28, 21),
        (3.40,  6.00, 2.60, 0.25, 22),
        (4.80,  7.40, 2.20, 0.22, 23),
        (6.20,  8.70, 1.78, 0.18, 24),
        (7.50, 10.10, 1.35, 0.14, 25),
        (8.90, 11.60, 0.88, 0.10, 26),
    ]
    if is_lod1:
        tiers = [
            (2.00,  5.50, 2.90, 0.22, 21),
            (4.50,  8.20, 2.20, 0.18, 23),
            (7.20, 11.60, 1.35, 0.12, 25),
        ]

    n_crown = 8 if is_lod1 else 14
    sub_r = 1 if is_lod1 else 2
    for yb, yt, rb, droop, sd in tiers:
        mb.add_cone_tier(yb, yt, rb, n_crown, mat_idx=1, droop=droop, wave_amp=0.18, seed=sd, sub_rings=sub_r)

    return mb.build()


def build_spruce_tree(mats, is_lod1=False, name_prefix="Agac_Ladin_01"):
    obj_name = f"{name_prefix}_{'LOD1' if is_lod1 else 'LOD0'}"
    mb = PropMeshBuilder(obj_name, [mats["Mat_AgacGovde"], mats["Mat_YaprakCam"]])

    n_trunk = 6 if is_lod1 else 10
    mb.add_frustum(-0.65, 0.35, 0.58, 0.38, n_trunk, mat_idx=0, wave_amp=0.10, seed=30, cap_bottom=True)
    mb.add_frustum( 0.35, 9.50, 0.38, 0.10, n_trunk, mat_idx=0, twist_deg=10.0, seed=31, cap_top=True)

    # Ladin: 7 katmanlı ince uzun sivri silüet (~13.2 m)
    tiers = [
        (1.30,  3.70, 2.65, 0.30, 41),
        (2.60,  5.00, 2.38, 0.27, 42),
        (3.90,  6.40, 2.10, 0.24, 43),
        (5.30,  7.80, 1.80, 0.20, 44),
        (6.70,  9.30, 1.48, 0.17, 45),
        (8.20, 11.00, 1.12, 0.13, 46),
        (9.80, 13.20, 0.75, 0.09, 47),
    ]
    if is_lod1:
        tiers = [
            (1.30,  4.80, 2.60, 0.25, 41),
            (3.90,  7.60, 2.05, 0.20, 43),
            (6.60, 10.40, 1.45, 0.15, 45),
            (9.40, 13.20, 0.85, 0.10, 47),
        ]

    n_crown = 8 if is_lod1 else 14
    sub_r = 1 if is_lod1 else 2
    for yb, yt, rb, droop, sd in tiers:
        mb.add_cone_tier(yb, yt, rb, n_crown, mat_idx=1, droop=droop, wave_amp=0.20, seed=sd, sub_rings=sub_r)

    return mb.build()


def build_snow_pine_tree(mats, is_lod1=False, name_prefix="Agac_KarliCam_01"):
    """Zirve bölgesi için kar kaplı çam ağacı (Mat_YaprakCam + Mat_Kar)"""
    obj_name = f"{name_prefix}_{'LOD1' if is_lod1 else 'LOD0'}"
    mb = PropMeshBuilder(obj_name, [mats["Mat_YaprakCam"], mats["Mat_Kar"]])

    n_trunk = 6 if is_lod1 else 8
    mb.add_frustum(-0.65, 2.40, 0.50, 0.28, n_trunk, mat_idx=0, wave_amp=0.08, seed=50, cap_bottom=True, cap_top=True)

    tiers = [
        (1.60,  4.20, 2.75, 0.25, 61),
        (3.20,  5.90, 2.30, 0.22, 62),
        (4.80,  7.60, 1.82, 0.18, 63),
        (6.40,  9.30, 1.32, 0.14, 64),
        (8.00, 10.90, 0.86, 0.10, 65),
    ]
    if is_lod1:
        tiers = [
            (1.60,  5.20, 2.70, 0.22, 61),
            (4.40,  8.00, 2.00, 0.16, 63),
            (7.20, 10.90, 1.18, 0.10, 65),
        ]

    n_crown = 8 if is_lod1 else 14
    for yb, yt, rb, droop, sd in tiers:
        mb.add_snow_crowned_tier(yb, yt, rb, n_crown, droop=droop, wave_amp=0.16, seed=sd, is_lod1=is_lod1)

    return mb.build()


# ---------------------------------------------------------------------------
# 2. Çimen Öbeği Modeli (Cimen_Obek_01)
# ---------------------------------------------------------------------------
def build_grass_clump(mats, is_lod1=False, name_prefix="Cimen_Obek_01"):
    obj_name = f"{name_prefix}_{'LOD1' if is_lod1 else 'LOD0'}"
    mb = PropMeshBuilder(obj_name, [mats["Mat_YaprakCam"]])
    rng = random.Random(77)

    n_blades = 10 if is_lod1 else 24
    for i in range(n_blades):
        angle = (2.0 * math.pi * i) / n_blades + rng.uniform(-0.18, 0.18)
        dist = rng.uniform(0.08, 0.85)
        bx = math.cos(angle) * dist
        bz = math.sin(angle) * dist
        by = -0.15

        h = rng.uniform(0.60, 1.25)
        lean = rng.uniform(0.25, 0.62)
        w_base = rng.uniform(0.15, 0.26)
        w_mid = w_base * 0.75

        dir_x = math.cos(angle + rng.uniform(-0.3, 0.3))
        dir_z = math.sin(angle + rng.uniform(-0.3, 0.3))
        perp_x = -dir_z
        perp_z = dir_x

        v_bL = mb.add_vert_u(bx - perp_x * w_base, by, bz - perp_z * w_base)
        v_bR = mb.add_vert_u(bx + perp_x * w_base, by, bz + perp_z * w_base)
        v_bB = mb.add_vert_u(bx - dir_x * (w_base * 0.65), by, bz - dir_z * (w_base * 0.65))

        mx = bx + dir_x * (lean * 0.45)
        mz = bz + dir_z * (lean * 0.45)
        my = by + h * 0.55
        v_mL = mb.add_vert_u(mx - perp_x * w_mid, my, mz - perp_z * w_mid)
        v_mR = mb.add_vert_u(mx + perp_x * w_mid, my, mz + perp_z * w_mid)
        v_mB = mb.add_vert_u(mx - dir_x * (w_mid * 0.55), my, mz - dir_z * (w_mid * 0.55))

        tx = bx + dir_x * lean
        tz = bz + dir_z * lean
        ty = by + h
        v_tip = mb.add_vert_u(tx, ty, tz)

        if is_lod1:
            mb.add_tri(v_bL, v_bR, v_tip, 0)
            mb.add_tri(v_bR, v_bB, v_tip, 0)
            mb.add_tri(v_bB, v_bL, v_tip, 0)
        else:
            mb.add_quad(v_bL, v_bR, v_mR, v_mL, 0)
            mb.add_quad(v_bR, v_bB, v_mB, v_mR, 0)
            mb.add_quad(v_bB, v_bL, v_mL, v_mB, 0)
            mb.add_tri(v_mL, v_mR, v_tip, 0)
            mb.add_tri(v_mR, v_mB, v_tip, 0)
            mb.add_tri(v_mB, v_mL, v_tip, 0)

    return mb.build()


# ---------------------------------------------------------------------------
# 3. Kaya Modelleri (Kaya_Buyuk_01, Kaya_Orta_01)
# ---------------------------------------------------------------------------
def build_rock_mesh(name, mats, radius_x, radius_y, radius_z, subdiv, seed, snow_cap=False):
    mesh = bpy.data.meshes.new(name + "_mesh")
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)

    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    rng = random.Random(seed)

    planes = []
    for _ in range(9):
        px = rng.uniform(-1.0, 1.0)
        py = rng.uniform(-0.6, 1.0)
        pz = rng.uniform(-1.0, 1.0)
        l = math.sqrt(px * px + py * py + pz * pz) or 1.0
        planes.append((px / l, py / l, pz / l, rng.uniform(0.65, 0.90)))

    for v in bm.verts:
        vx, vy, vz = v.co.x, v.co.y, v.co.z
        for nx, ny, nz, d_lim in planes:
            dot = vx * nx + vy * ny + vz * nz
            if dot > d_lim:
                excess = dot - d_lim
                vx -= nx * excess * 0.78
                vy -= ny * excess * 0.78
                vz -= nz * excess * 0.78

        noise = 1.0 + 0.18 * math.sin(vx * 3.7 + seed) * math.cos(vz * 4.1 - seed)
        ux = vx * radius_x * noise
        uy = vy * radius_y * noise
        uz = vz * radius_z * noise
        if uy < -radius_y * 0.35:
            uy = -radius_y * 0.35
        uy += radius_y * 0.35 - 0.45
        v.co = Vector(u2b(ux, uy, uz))

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=bm.faces)

    for f in bm.faces:
        if snow_cap and f.normal.z > 0.50 and f.calc_center_median().z > radius_y * 0.38:
            f.material_index = 1
        else:
            f.material_index = 0

    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()

    for m in mats:
        mesh.materials.append(m)

    obj.location = Vector((0.0, 0.0, 0.0))
    obj.rotation_euler = Euler((0.0, 0.0, 0.0), 'XYZ')
    obj.scale = Vector((1.0, 1.0, 1.0))
    return obj


# ---------------------------------------------------------------------------
# 4. Tabela Modelleri (Tabela_KeskinViraj, Tabela_KayaDusebilir)
# ---------------------------------------------------------------------------
def build_chevron_sign(mats, is_lod1=False, name_prefix="Tabela_KeskinViraj"):
    obj_name = f"{name_prefix}_{'LOD1' if is_lod1 else 'LOD0'}"
    mb = PropMeshBuilder(obj_name, [mats["Mat_Bariyer"], mats["Mat_Reflektor"]])

    for sx in (-0.65, 0.65):
        mb.add_box(sx, 1.15, -0.06, 0.12, 3.20, 0.12, mat_idx=0)

    mb.add_box(0.0, 2.25, 0.0, 1.95, 1.18, 0.08, mat_idx=0)
    mb.add_box(0.0, 2.80, 0.048, 1.86, 0.08, 0.03, mat_idx=1)
    mb.add_box(0.0, 1.70, 0.048, 1.86, 0.08, 0.03, mat_idx=1)

    chevron_offsets = [-0.40, 0.32] if not is_lod1 else [0.0]
    for cx in chevron_offsets:
        v_top_L = mb.add_vert_u(cx - 0.28, 2.64, 0.055)
        v_top_R = mb.add_vert_u(cx - 0.04, 2.64, 0.055)
        v_mid_R = mb.add_vert_u(cx + 0.26, 2.25, 0.055)
        v_mid_L = mb.add_vert_u(cx + 0.02, 2.25, 0.055)
        v_bot_L = mb.add_vert_u(cx - 0.28, 1.86, 0.055)
        v_bot_R = mb.add_vert_u(cx - 0.04, 1.86, 0.055)

        mb.add_quad(v_top_L, v_top_R, v_mid_R, v_mid_L, mat_idx=1)
        mb.add_quad(v_mid_L, v_mid_R, v_bot_R, v_bot_L, mat_idx=1)

    return mb.build(recalc_normals=False)


def build_hazard_sign(mats, is_lod1=False, name_prefix="Tabela_KayaDusebilir"):
    obj_name = f"{name_prefix}_{'LOD1' if is_lod1 else 'LOD0'}"
    mb = PropMeshBuilder(obj_name, [mats["Mat_Bariyer"], mats["Mat_Reflektor"]])

    mb.add_box(0.0, 1.15, -0.06, 0.12, 3.20, 0.12, mat_idx=0)

    z_f = 0.04
    z_b = -0.04
    tri_pts = [
        (-0.84, 1.75),
        ( 0.84, 1.75),
        ( 0.00, 3.10),
    ]
    f_ids = [mb.add_vert_u(x, y, z_f) for x, y in tri_pts]
    b_ids = [mb.add_vert_u(x, y, z_b) for x, y in tri_pts]
    mb.add_tri(f_ids[0], f_ids[2], f_ids[1], mat_idx=0)
    mb.add_tri(b_ids[0], b_ids[1], b_ids[2], mat_idx=0)
    for i in range(3):
        ni = (i + 1) % 3
        mb.add_quad(f_ids[i], f_ids[ni], b_ids[ni], b_ids[i], mat_idx=0)

    in_pts = [
        (-0.56, 1.90),
        ( 0.56, 1.90),
        ( 0.00, 2.84),
    ]
    out_r = [mb.add_vert_u(x * 0.90, 1.75 + (y - 1.75) * 0.90, 0.052) for x, y in tri_pts]
    in_r = [mb.add_vert_u(x, y, 0.052) for x, y in in_pts]
    for i in range(3):
        ni = (i + 1) % 3
        mb.add_quad(out_r[i], in_r[i], in_r[ni], out_r[ni], mat_idx=1)

    mb.add_box(0.0, 2.34, 0.052, 0.12, 0.34, 0.02, mat_idx=1)
    mb.add_box(0.0, 2.04, 0.052, 0.12, 0.12, 0.02, mat_idx=1)

    return mb.build(recalc_normals=False)


# ---------------------------------------------------------------------------
# Dışa Aktarma ve Doğrulama
# ---------------------------------------------------------------------------
def count_tris(obj):
    return sum(max(0, len(p.vertices) - 2) for p in obj.data.polygons)


def export_prop_fbx(name, build_fn, out_dirs):
    clear_scene()
    mats = {
        "Mat_AgacGovde": make_mat("Mat_AgacGovde", (0.26, 0.17, 0.10), roughness=0.90),
        "Mat_YaprakCam": make_mat("Mat_YaprakCam", (0.11, 0.24, 0.14), roughness=0.85),
        "Mat_Kar":       make_mat("Mat_Kar",       (0.86, 0.90, 0.95), roughness=0.65),
        "Mat_Kaya":      make_mat("Mat_Kaya",      (0.32, 0.29, 0.26), roughness=0.90),
        "Mat_Bariyer":   make_mat("Mat_Bariyer",   (0.46, 0.47, 0.50), roughness=0.65),
        "Mat_Reflektor": make_mat("Mat_Reflektor", (0.95, 0.38, 0.08), roughness=0.35,
                                  emission=(1.2, 0.45, 0.08), emission_strength=1.5),
    }

    lod0_obj, lod1_obj = build_fn(mats)
    bpy.context.view_layer.update()

    tris0 = count_tris(lod0_obj)
    tris1 = count_tris(lod1_obj)
    assert tris0 <= 1500, f"{name} LOD0 üçgen sayısı > 1500: {tris0}"
    assert tris1 <= 400, f"{name} LOD1 üçgen sayısı > 400: {tris1}"

    for d in out_dirs:
        os.makedirs(d, exist_ok=True)
        fbx_path = os.path.join(d, f"{name}.fbx")
        bpy.ops.export_scene.fbx(
            filepath=fbx_path,
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

    return {
        "model": name,
        "lod0_tris": tris0,
        "lod1_tris": tris1,
        "toplam_tris": tris0 + tris1,
    }


def build_all_props(export_to_assets=False):
    src_dir = "/home/zoro/harvester/Harvester/BlenderSources/maps/DagTirmanisi"
    out_dirs = [src_dir]
    if export_to_assets:
        out_dirs.append("/home/zoro/harvester/Harvester/Assets/Maps/DagTirmanisi")

    prop_specs = [
        ("Agac_Cam_01", lambda m: (
            build_pine_tree(m, is_lod1=False, name_prefix="Agac_Cam_01"),
            build_pine_tree(m, is_lod1=True,  name_prefix="Agac_Cam_01"),
        )),
        ("Agac_Ladin_01", lambda m: (
            build_spruce_tree(m, is_lod1=False, name_prefix="Agac_Ladin_01"),
            build_spruce_tree(m, is_lod1=True,  name_prefix="Agac_Ladin_01"),
        )),
        ("Agac_KarliCam_01", lambda m: (
            build_snow_pine_tree(m, is_lod1=False, name_prefix="Agac_KarliCam_01"),
            build_snow_pine_tree(m, is_lod1=True,  name_prefix="Agac_KarliCam_01"),
        )),
        ("Cimen_Obek_01", lambda m: (
            build_grass_clump(m, is_lod1=False, name_prefix="Cimen_Obek_01"),
            build_grass_clump(m, is_lod1=True,  name_prefix="Cimen_Obek_01"),
        )),
        ("Kaya_Buyuk_01", lambda m: (
            build_rock_mesh("Kaya_Buyuk_01_LOD0", [m["Mat_Kaya"], m["Mat_Kar"]], 3.6, 2.8, 3.2, subdiv=3, seed=101, snow_cap=True),
            build_rock_mesh("Kaya_Buyuk_01_LOD1", [m["Mat_Kaya"], m["Mat_Kar"]], 3.6, 2.8, 3.2, subdiv=2, seed=101, snow_cap=True),
        )),
        ("Kaya_Orta_01", lambda m: (
            build_rock_mesh("Kaya_Orta_01_LOD0", [m["Mat_Kaya"]], 1.8, 1.35, 1.6, subdiv=3, seed=202, snow_cap=False),
            build_rock_mesh("Kaya_Orta_01_LOD1", [m["Mat_Kaya"]], 1.8, 1.35, 1.6, subdiv=2, seed=202, snow_cap=False),
        )),
        ("Tabela_KeskinViraj", lambda m: (
            build_chevron_sign(m, is_lod1=False, name_prefix="Tabela_KeskinViraj"),
            build_chevron_sign(m, is_lod1=True,  name_prefix="Tabela_KeskinViraj"),
        )),
        ("Tabela_KayaDusebilir", lambda m: (
            build_hazard_sign(m, is_lod1=False, name_prefix="Tabela_KayaDusebilir"),
            build_hazard_sign(m, is_lod1=True,  name_prefix="Tabela_KayaDusebilir"),
        )),
    ]

    report = []
    for name, fn in prop_specs:
        info = export_prop_fbx(name, fn, out_dirs)
        report.append(info)
        print(f"[Prop] {name}.fbx -> LOD0: {info['lod0_tris']} üçgen, LOD1: {info['lod1_tris']} üçgen")

    return report


if __name__ == "__main__":
    export_assets = "--export-assets" in sys.argv
    res = build_all_props(export_to_assets=export_assets)
    print(json.dumps(res, indent=2, ensure_ascii=False))
