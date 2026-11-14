"""
Harvester — MuscleFastback_01 (American V8 Muscle Fastback / Track Coupe)
Blender 4.x Python Generator Script (.agents/skills/arac-modelleme/SKILL.md)

Design Identity (MuscleFastback_01):
- Class: muscle (Front-Engine V8 High-Beltline Muscle Fastback / Track Coupe)
- Silhouette & Proportions:
  - High, muscular horizontal hood with raised central power-dome & heat extractor vents
  - Deeply recessed wide trapezoidal upper grille with diagonal fang struts and front splitter
  - Signature tri-bar angled LED headlight blades and recessed rear Kammback panel with vertical tri-bar LED taillights
  - Sharp "hockey-stick" rear fender shoulder kick-up over the rear wheels and fastback roof-to-tail roofline
- Coordinate convention (Unity target):
  - Forward: +Z (Blender -Y)
  - Up:      +Y (Blender +Z)
  - Right:   +X (Blender -X before FBX axis conversion)
  - Root transform: pos=(0,0,0), rot=(0,0,0), scale=(1,1,1)
  - Origin (0,0,0): ground level (y=0), exact midpoint between front (+1.36) and rear (-1.36) axles.
"""

import os
import math
import bpy
import bmesh
from mathutils import Vector, Matrix, Euler


def u2b(p):
    """
    Unity (x_right, y_up, z_fwd) -> Blender (-x_right, -z_fwd, y_up).
    When exported with axis_forward='-Z', axis_up='Y', bake_space_transform=True,
    Unity receives exact (+X=Right, +Y=Up, +Z=Forward) with root rotation (0,0,0) and scale (1,1,1).
    """
    return (-float(p[0]), -float(p[2]), float(p[1]))


def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for block in list(bpy.data.meshes):
        bpy.data.meshes.remove(block, do_unlink=True)
    for block in list(bpy.data.materials):
        bpy.data.materials.remove(block, do_unlink=True)
    for block in list(bpy.data.cameras):
        bpy.data.cameras.remove(block, do_unlink=True)
    for block in list(bpy.data.lights):
        bpy.data.lights.remove(block, do_unlink=True)


def make_mat(name, rgb, metallic=0.1, roughness=0.4, alpha=1.0, emission=None):
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        if "Alpha" in bsdf.inputs:
            bsdf.inputs["Alpha"].default_value = alpha
        if emission is not None:
            if "Emission Color" in bsdf.inputs:
                bsdf.inputs["Emission Color"].default_value = (emission[0], emission[1], emission[2], 1.0)
            elif "Emission" in bsdf.inputs:
                bsdf.inputs["Emission"].default_value = (emission[0], emission[1], emission[2], 1.0)
            if "Emission Strength" in bsdf.inputs:
                bsdf.inputs["Emission Strength"].default_value = (0.6 if name == "Screen" else 2.5)
    if alpha < 1.0:
        mat.diffuse_color = (rgb[0], rgb[2], rgb[2], alpha)
    else:
        mat.diffuse_color = (rgb[0], rgb[1], rgb[2], 1.0)
    return mat


class PartBuilder:
    """Builds a closed low-poly mesh with max 1-2 materials and explicit pivot."""
    def __init__(self, name, materials):
        self.name = name
        self.materials = materials
        self.verts = []
        self.faces = []

    def add_submesh(self, verts_u, faces, mat_idx=0, mirror_x=False):
        base = len(self.verts)
        for v in verts_u:
            self.verts.append(u2b(v))
        for f in faces:
            self.faces.append((tuple(base + i for i in reversed(f)), mat_idx))
        if mirror_x:
            mbase = len(self.verts)
            for v in verts_u:
                self.verts.append(u2b((-v[0], v[1], v[2])))
            for f in faces:
                self.faces.append((tuple(mbase + i for i in f), mat_idx))

    def add_prism(self, bot_u, top_u, mat_idx=0, mirror_x=False):
        verts = list(bot_u) + list(top_u)
        faces = [
            (0, 3, 2, 1),  # bottom
            (4, 5, 6, 7),  # top
            (0, 1, 5, 4),  # back
            (1, 2, 6, 5),  # right
            (2, 3, 7, 6),  # front
            (3, 0, 4, 7),  # left
        ]
        self.add_submesh(verts, faces, mat_idx=mat_idx, mirror_x=mirror_x)

    def add_box(self, min_u, max_u, mat_idx=0, mirror_x=False):
        x0, y0, z0 = min(min_u[0], max_u[0]), min(min_u[1], max_u[1]), min(min_u[2], max_u[2])
        x1, y1, z1 = max(min_u[0], max_u[0]), max(min_u[1], max_u[1]), max(min_u[2], max_u[2])
        bot = [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)]
        top = [(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)]
        self.add_prism(bot, top, mat_idx=mat_idx, mirror_x=mirror_x)

    def add_cylinder(self, p0_u, p1_u, r0, r1=None, segs=12, mat_idx=0, mirror_x=False, cap=True):
        if r1 is None:
            r1 = r0
        v0 = Vector(p0_u)
        v1 = Vector(p1_u)
        axis = v1 - v0
        if axis.length < 1e-6:
            return
        axis_n = axis.normalized()
        up = Vector((0.0, 1.0, 0.0)) if abs(axis_n.y) < 0.9 else Vector((1.0, 0.0, 0.0))
        t1 = axis_n.cross(up).normalized()
        t2 = axis_n.cross(t1).normalized()
        verts = []
        for i in range(segs):
            ang = 2.0 * math.pi * i / segs
            ca, sa = math.cos(ang), math.sin(ang)
            verts.append(tuple(v0 + (t1 * ca + t2 * sa) * r0))
        for i in range(segs):
            ang = 2.0 * math.pi * i / segs
            ca, sa = math.cos(ang), math.sin(ang)
            verts.append(tuple(v1 + (t1 * ca + t2 * sa) * r1))
        faces = []
        for i in range(segs):
            ni = (i + 1) % segs
            faces.append((i, ni, segs + ni, segs + i))
        if cap:
            faces.append(tuple(reversed(range(segs))))
            faces.append(tuple(range(segs, 2 * segs)))
        self.add_submesh(verts, faces, mat_idx=mat_idx, mirror_x=mirror_x)

    def build(self, parent=None, pivot=None, visible=True, outward_normal=None):
        mesh = bpy.data.meshes.new(self.name)
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)

        bm = bmesh.new()
        bm_verts = [bm.verts.new(v) for v in self.verts]
        bm.verts.ensure_lookup_table()
        for f_idx, mat_idx in self.faces:
            try:
                f = bm.faces.new([bm_verts[i] for i in f_idx])
                f.material_index = mat_idx
            except ValueError:
                pass
        bm.faces.ensure_lookup_table()
        if outward_normal == "radial_x":
            bm.normal_update()
            for face in bm.faces:
                cx = face.calc_center_median().x
                if face.normal.x * cx < 0.0:
                    face.normal_flip()
            bm.normal_update()
        elif outward_normal is not None:
            bm.normal_update()
            target_n = Vector(u2b(outward_normal)).normalized()
            for face in bm.faces:
                if face.normal.dot(target_n) < 0.0:
                    face.normal_flip()
            bm.normal_update()
        else:
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(mesh)
        bm.free()

        for m in self.materials:
            mesh.materials.append(m)

        if pivot is None and self.verts:
            xs = [v[0] for v in self.verts]
            ys = [v[1] for v in self.verts]
            zs = [v[2] for v in self.verts]
            p_bl = Vector(((min(xs) + max(xs)) * 0.5, (min(ys) + max(ys)) * 0.5, (min(zs) + max(zs)) * 0.5))
        elif pivot is not None:
            p_bl = Vector(u2b(pivot))
        else:
            p_bl = Vector((0.0, 0.0, 0.0))

        mesh.transform(Matrix.Translation(-p_bl))

        if parent is not None:
            obj.parent = parent
            obj.matrix_parent_inverse = Matrix.Identity(4)
            obj.location = p_bl - parent.location
        else:
            obj.location = p_bl

        obj.rotation_euler = Euler((0.0, 0.0, 0.0), 'XYZ')
        obj.scale = Vector((1.0, 1.0, 1.0))
        obj["_target_visible"] = bool(visible)
        obj.hide_viewport = False
        obj.hide_render = False
        return obj


def add_empty(name, pos_u, parent=None, forward_plus_z=True, visible=True):
    emp = bpy.data.objects.new(name, None)
    emp.empty_display_type = 'SINGLE_ARROW'
    emp.empty_display_size = 0.20
    bpy.context.collection.objects.link(emp)
    p_bl = Vector(u2b(pos_u))
    if parent is not None:
        emp.parent = parent
        emp.matrix_parent_inverse = Matrix.Identity(4)
        emp.location = p_bl - parent.location
    else:
        emp.location = p_bl
    emp.rotation_euler = Euler((0.0, 0.0, 0.0 if forward_plus_z else math.pi), 'XYZ')
    emp.scale = Vector((1.0, 1.0, 1.0))
    emp["_target_visible"] = bool(visible)
    return emp



def build_steering_variant(name, mats_list, pivot_u, root_obj=None, visible=True,
                           r_rim=0.148, is_yoke=False, spoke_mat_idx=0):
    """
    Direksiyon simidi varyantini kendi yerel XY duzleminde, merkezi (0, 0, 0) olacak sekilde uretir:
      - Yerel +X: Sag
      - Yerel +Y: Simit ust kenari (saat 12 yonu)
      - Yerel +Z: Direksiyon kolonu boyunca ileri (torpidoya dogru; mesh.bounds en ince ekseni yerel Z!)
      - Gobek (hub): cap 7.0 cm (r = 0.035 m), z = +0.008 .. +0.028 m (merkez +0.018 m, halkadan 3.4 cm geride)
      - Halka (rim): 16 segmentli ici bos simit, boru kesiti 2.8 cm (r_tube = 0.014 m), z = -0.028 .. -0.002 m (merkez -0.015 m)
      - Saat 12 isareti: ustte dar serit (genislik ~2.5 cm <= 3.0 cm), mat_idx = 1
      - 3 Kol (sol 195 deg, sag -15 deg, alt 270 deg): hem halkadan hem gobekten 6 mm (>= 5 mm) ayrik;
        alt kol her zaman Interior_Dark (mat_idx = 0). Hicbir yuz baska bir yuzle ayni duzlemde degil.
      - Kolon ekseninde 18 derece egik (yerel +Z model uzayinda (0, +0.309, +0.951)).
    """
    mesh = bpy.data.meshes.new(name + "_mesh")
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)

    bm = bmesh.new()
    verts = []
    faces = []

    def add_local_prism(quad_front, quad_back, m_idx):
        base = len(verts)
        verts.extend(quad_front)
        verts.extend(quad_back)
        faces.extend([
            ((base + 0, base + 3, base + 2, base + 1), m_idx),
            ((base + 4, base + 5, base + 6, base + 7), m_idx),
            ((base + 0, base + 1, base + 5, base + 4), m_idx),
            ((base + 1, base + 2, base + 6, base + 5), m_idx),
            ((base + 2, base + 3, base + 7, base + 6), m_idx),
            ((base + 3, base + 0, base + 4, base + 7), m_idx),
        ])

    def add_local_box(x0, y0, z0, x1, y1, z1, m_idx):
        qf = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)]
        qb = [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
        add_local_prism(qf, qb, m_idx)

    # 1. Gobek (Hub): cap = 7.0 cm (r_hub = 0.035 m), yerel z = +0.008 .. +0.028 m (merkez +0.018 m, halkadan 3.4 cm geride)
    r_hub = 0.035
    z_hub0, z_hub1 = 0.008, 0.028
    hub_segs = 12
    hbase = len(verts)
    for i in range(hub_segs):
        ang = 2.0 * math.pi * i / hub_segs
        verts.append((math.cos(ang) * r_hub, math.sin(ang) * r_hub, z_hub0))
    for i in range(hub_segs):
        ang = 2.0 * math.pi * i / hub_segs
        verts.append((math.cos(ang) * r_hub, math.sin(ang) * r_hub, z_hub1))
    for i in range(hub_segs):
        ni = (i + 1) % hub_segs
        faces.append(((hbase + i, hbase + ni, hbase + hub_segs + ni, hbase + hub_segs + i), 0))
    faces.append((tuple(hbase + i for i in reversed(range(hub_segs))), 0))
    faces.append((tuple(hbase + hub_segs + i for i in range(hub_segs)), 0))

    if is_yoke:
        # Sol ve sag yatay kollar (gobekten 6 mm ayrik: x_in=0.041 m, yan tutamaklardan 6 mm ayrik: x_out=0.124 m)
        for sx in (-1.0, 1.0):
            x_a = min(sx * 0.041, sx * 0.124)
            x_b = max(sx * 0.041, sx * 0.124)
            add_local_box(x_a, -0.014, -0.015, x_b, 0.014, -0.003, 0)
            # Yan kelebek/yoke tutamaklar (x = 0.130 .. 0.158, y = -0.125 .. +0.125 -> boy 0.250 m, z = -0.028 .. -0.002 -> kalinlik 0.056 m < boy/4!)
            gx0 = min(sx * 0.130, sx * 0.158)
            gx1 = max(sx * 0.130, sx * 0.158)
            add_local_box(gx0, -0.125, -0.028, gx1, 0.095, -0.002, 0)
            # Ust tutamak vurgu uclari (5 mm ayrik: y = 0.100 .. 0.125)
            add_local_box(gx0,  0.100, -0.028, gx1, 0.125, -0.002, 1)
    else:
        # 2. Ici Bos Simit Halka (16 segment, boru kesiti 2.8 cm -> r_tube = 0.014 m, z_merkez = -0.015 m)
        # Segment 0 saat 12'de [85.5 deg, 94.5 deg] araligindadir -> genislik = 2.54 cm <= 3.0 cm!
        r_tube = 0.0135
        z_rim = -0.0145
        tube_sides = 6
        deg_list = [
            85.5, 94.5,                                       # Seg 0: saat 12 ince isaret seridi (2.54 cm <= 3.0 cm)
            119.0, 143.5, 168.0, 192.5, 217.0, 241.5, 265.5,  # Sol yarim (7 segment)
            274.5,                                            # Seg 8: saat 6 alt simetrik segment
            298.5, 323.0, 347.5, 12.0, 36.5, 61.0             # Sag yarim (7 segment) -> toplam 16 segment!
        ]
        n_ring = len(deg_list)
        rbase = len(verts)
        for deg in deg_list:
            th = math.radians(deg)
            ct, st = math.cos(th), math.sin(th)
            for j in range(tube_sides):
                phi = 2.0 * math.pi * j / tube_sides
                cp, sp = math.cos(phi), math.sin(phi)
                rr = r_rim + r_tube * cp
                verts.append((rr * ct, rr * st, z_rim + r_tube * sp))
        for i in range(n_ring):
            ni = (i + 1) % n_ring
            seg_mat = 1 if i == 0 else 0  # Yalniz saat 12 ince seridi (2.54 cm) mat_idx=1 kullanir
            for j in range(tube_sides):
                nj = (j + 1) % tube_sides
                v00 = rbase + i * tube_sides + j
                v10 = rbase + ni * tube_sides + j
                v11 = rbase + ni * tube_sides + nj
                v01 = rbase + i * tube_sides + nj
                faces.append(((v00, v10, v11, v01), seg_mat))

        # 3. Uc Ayrik Kol (Sol 195 deg, Sag -15 deg, Alt 270 deg)
        #    r_in = 0.041 m (r_hub = 0.035 m gobekten 6 mm ayrik)
        #    r_out = r_rim - r_tube - 0.006 (halka ic yuzeyinden 6 mm ayrik)
        r_in = r_hub + 0.006
        r_out = (r_rim - r_tube) - 0.006
        half_w = 0.011  # 2.2 cm kol genisligi
        for sp_deg, s_mat in ((195.0, spoke_mat_idx), (-15.0, spoke_mat_idx), (270.0, 0)):
            ang = math.radians(sp_deg)
            ux, uy = math.cos(ang), math.sin(ang)
            px, py = -uy, ux
            qf = [
                (ux * r_in  - px * half_w, uy * r_in  - py * half_w,  0.010),
                (ux * r_in  + px * half_w, uy * r_in  + py * half_w,  0.010),
                (ux * r_out + px * half_w, uy * r_out + py * half_w, -0.020),
                (ux * r_out - px * half_w, uy * r_out - py * half_w, -0.020),
            ]
            qb = [
                (ux * r_in  - px * half_w, uy * r_in  - py * half_w,  0.019),
                (ux * r_in  + px * half_w, uy * r_in  + py * half_w,  0.019),
                (ux * r_out + px * half_w, uy * r_out + py * half_w, -0.010),
                (ux * r_out - px * half_w, uy * r_out - py * half_w, -0.010),
            ]
            add_local_prism(qf, qb, s_mat)

    bm_verts = [bm.verts.new(v) for v in verts]
    bm.verts.ensure_lookup_table()
    for f_idx, m_idx in faces:
        try:
            f = bm.faces.new([bm_verts[i] for i in f_idx])
            f.material_index = m_idx
        except ValueError:
            pass
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()

    for m in mats_list:
        mesh.materials.append(m)

    p_bl = Vector(u2b(pivot_u))
    if root_obj is not None:
        obj.parent = root_obj
        obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.location = p_bl - root_obj.location
    else:
        obj.location = p_bl

    # 18 derece kolon egimi: Blender'da +72 deg X donusu yerel +Z'yi kolon eksenine
    # (Unity'de (0.000, +0.309, +0.951)) hizalar, ust kenari (+Y) surucuye dogru 18 derece yatirir.
    obj.rotation_euler = Euler((math.radians(72.0), 0.0, 0.0), 'XYZ')
    obj.scale = Vector((1.0, 1.0, 1.0))
    obj["_target_visible"] = bool(visible)
    obj.hide_viewport = False
    obj.hide_render = False
    return obj


def build_muscle_fastback_01():
    clear_scene()

    root = bpy.data.objects.new("MuscleFastback_01", None)
    root.empty_display_type = 'PLAIN_AXES'
    root.empty_display_size = 0.5
    bpy.context.collection.objects.link(root)
    root.location = Vector((0.0, 0.0, 0.0))
    root.rotation_euler = Euler((0.0, 0.0, 0.0), 'XYZ')
    root.scale = Vector((1.0, 1.0, 1.0))

    # =========================================================================
    # 1. ALLOWED MATERIALS (14 total: 6 Paintable + 8 Fixed including Screen)
    # =========================================================================
    mats = {
        "Paint_Body":    make_mat("Paint_Body",    (0.85, 0.22, 0.12), metallic=0.30, roughness=0.28),
        "Paint_Rim":     make_mat("Paint_Rim",     (0.10, 0.11, 0.13), metallic=0.65, roughness=0.32),
        "Paint_Caliper": make_mat("Paint_Caliper", (0.85, 0.22, 0.12), metallic=0.30, roughness=0.25),
        "Paint_Accent":  make_mat("Paint_Accent",  (0.96, 0.97, 0.98), metallic=0.15, roughness=0.30),
        "Neon":          make_mat("Neon",          (1.00, 0.35, 0.05), metallic=0.0,  roughness=0.1, emission=(1.00, 0.35, 0.05)),
        "Glass_Tint":    make_mat("Glass_Tint",    (0.08, 0.11, 0.14), metallic=0.1,  roughness=0.08, alpha=0.42),
        "Trim_Dark":     make_mat("Trim_Dark",     (0.09, 0.09, 0.10), metallic=0.05, roughness=0.82),
        "Carbon":        make_mat("Carbon",        (0.13, 0.14, 0.16), metallic=0.25, roughness=0.38),
        "Metal_Chrome":  make_mat("Metal_Chrome",  (0.88, 0.90, 0.92), metallic=0.90, roughness=0.15),
        "Interior_Dark": make_mat("Interior_Dark", (0.07, 0.07, 0.075), metallic=0.0,  roughness=0.88),
        "Light_Head":    make_mat("Light_Head",    (1.00, 0.98, 0.92), metallic=0.0,  roughness=0.1, emission=(1.00, 0.98, 0.92)),
        "Light_Tail":    make_mat("Light_Tail",    (1.00, 0.05, 0.05), metallic=0.0,  roughness=0.1, emission=(1.00, 0.05, 0.05)),
        "Decal_Graphic": make_mat("Decal_Graphic", (0.96, 0.96, 0.96), metallic=0.05, roughness=0.35),
        "Screen":        make_mat("Screen",        (0.05, 0.38, 0.50), metallic=0.0,  roughness=0.15, emission=(0.05, 0.42, 0.55)),
    }

    # =========================================================================
    # 2. CHASSIS & CLOSED SOLID BODY PANELS (American V8 Muscle Fastback)
    #    Wheelbase: 2.72 m (front z = +1.36, rear z = -1.36, midpoint = 0.00)
    #    Length: ~4.72 m, Body Width (no mirrors): 1.98 m, Height: 1.36 m
    # =========================================================================
    p_chassis = PartBuilder("chassis_underbody", [mats["Trim_Dark"], mats["Metal_Chrome"]])
    p_chassis.add_box((-0.82, 0.13, -2.12), (0.82, 0.24, 2.12), mat_idx=0)
    p_chassis.add_box((-0.64, 0.24,  1.96), (0.64, 0.42, 2.14), mat_idx=1)
    p_chassis.add_box((-0.66, 0.24, -2.18), (0.66, 0.44, -1.98), mat_idx=1)
    p_chassis.build(parent=root, pivot=(0.0, 0.19, 0.0), visible=True)

    for side in (-1, 1):
        s_name = "left" if side < 0 else "right"
        sx = float(side)

        def orient_quad(q):
            return q if side > 0 else [q[0], q[3], q[2], q[1]]

        # Front Fender (High square-jawed muscle beltline y = 0.74..0.88, wheel arch z = 0.90..1.82)
        p_ff = PartBuilder(f"fender_front_{s_name}", [mats["Paint_Body"], mats["Trim_Dark"]])
        # Forward section ahead of front wheel (z = 1.80 .. 2.18)
        p_ff.add_prism(
            orient_quad([(sx * 0.56, 0.28, 1.80), (sx * 0.92, 0.28, 1.80), (sx * 0.88, 0.26, 2.18), (sx * 0.54, 0.26, 2.18)]),
            orient_quad([(sx * 0.54, 0.78, 1.80), (sx * 0.92, 0.76, 1.80), (sx * 0.84, 0.72, 2.18), (sx * 0.52, 0.74, 2.18)]),
            mat_idx=0
        )
        # Upper arch bridge over front wheel (z = 0.90 .. 1.80, clearance y >= 0.72 > front wheel top 0.70)
        p_ff.add_prism(
            orient_quad([(sx * 0.54, 0.72, 0.90), (sx * 0.98, 0.72, 0.90), (sx * 0.94, 0.68, 1.80), (sx * 0.54, 0.68, 1.80)]),
            orient_quad([(sx * 0.54, 0.86, 0.90), (sx * 0.93, 0.84, 0.90), (sx * 0.92, 0.76, 1.80), (sx * 0.54, 0.78, 1.80)]),
            mat_idx=0
        )
        # Rear section of front fender between wheel and door (z = 0.56 .. 0.90) + GT3-style behind-wheel extractor vent
        p_ff.add_prism(
            orient_quad([(sx * 0.74, 0.22, 0.56), (sx * 0.96, 0.22, 0.56), (sx * 0.98, 0.52, 0.90), (sx * 0.74, 0.52, 0.90)]),
            orient_quad([(sx * 0.72, 0.88, 0.56), (sx * 0.93, 0.86, 0.56), (sx * 0.93, 0.84, 0.90), (sx * 0.72, 0.86, 0.90)]),
            mat_idx=0
        )
        p_ff.add_box((sx * 0.90, 0.44, 0.60), (sx * 0.965, 0.78, 0.82), mat_idx=1)
        # Inner wheelwell wall (at |x|=0.54 so turning front wheel at x=0.84 has >12 cm clearance)
        p_ff.add_box((sx * 0.52, 0.24, 0.90), (sx * 0.58, 0.74, 1.80), mat_idx=1)
        p_ff.build(parent=root, pivot=(sx * 0.78, 0.58, 1.34), visible=True)

        # Door (High muscle beltline y = 0.88 m, sculpted lower character crease; pivot at front hinge)
        p_door = PartBuilder(f"door_{s_name}", [mats["Paint_Body"], mats["Interior_Dark"]])
        p_door.add_prism(
            orient_quad([(sx * 0.74, 0.22, -0.54), (sx * 0.95, 0.22, -0.54), (sx * 0.95, 0.22, 0.56), (sx * 0.74, 0.22, 0.56)]),
            orient_quad([(sx * 0.74, 0.56, -0.54), (sx * 0.97, 0.56, -0.54), (sx * 0.96, 0.54, 0.56), (sx * 0.74, 0.54, 0.56)]),
            mat_idx=0
        )
        p_door.add_prism(
            orient_quad([(sx * 0.74, 0.56, -0.54), (sx * 0.97, 0.56, -0.54), (sx * 0.96, 0.54, 0.56), (sx * 0.74, 0.54, 0.56)]),
            orient_quad([(sx * 0.72, 0.90, -0.54), (sx * 0.93, 0.88, -0.54), (sx * 0.93, 0.86, 0.56), (sx * 0.72, 0.88, 0.56)]),
            mat_idx=0
        )
        p_door.add_box((sx * 0.70, 0.26, -0.50), (sx * 0.75, 0.84, 0.52), mat_idx=1)
        p_door.build(parent=root, pivot=(sx * 0.88, 0.58, 0.56), visible=True)

        # Rear Quarter Fender (Signature Muscle "Hockey-Stick" Haunch Kick-Up over rear wheel z = -2.22 .. -0.54)
        p_rf = PartBuilder(f"fender_rear_{s_name}", [mats["Paint_Body"], mats["Trim_Dark"]])
        # Forward shoulder kick-up ahead of rear wheel (z = -0.90 .. -0.54)
        p_rf.add_prism(
            orient_quad([(sx * 0.74, 0.22, -0.90), (sx * 0.98, 0.22, -0.90), (sx * 0.95, 0.22, -0.54), (sx * 0.74, 0.22, -0.54)]),
            orient_quad([(sx * 0.56, 0.96, -0.90), (sx * 0.99, 0.94, -0.90), (sx * 0.93, 0.88, -0.54), (sx * 0.58, 0.90, -0.54)]),
            mat_idx=0
        )
        # High muscular arch over rear wheel (z = -1.82 .. -0.90, clearance y >= 0.76 > rear wheel top 0.74)
        p_rf.add_prism(
            orient_quad([(sx * 0.58, 0.76, -1.82), (sx * 0.99, 0.76, -1.82), (sx * 0.99, 0.76, -0.90), (sx * 0.58, 0.76, -0.90)]),
            orient_quad([(sx * 0.52, 0.94, -1.82), (sx * 0.95, 0.92, -1.82), (sx * 0.96, 0.96, -0.90), (sx * 0.56, 0.98, -0.90)]),
            mat_idx=0
        )
        # Fastback Kammback rear quarter flank behind rear wheel (z = -2.22 .. -1.82)
        p_rf.add_prism(
            orient_quad([(sx * 0.54, 0.30, -2.22), (sx * 0.90, 0.30, -2.22), (sx * 0.98, 0.30, -1.82), (sx * 0.58, 0.30, -1.82)]),
            orient_quad([(sx * 0.48, 0.90, -2.22), (sx * 0.86, 0.88, -2.22), (sx * 0.95, 0.92, -1.82), (sx * 0.52, 0.94, -1.82)]),
            mat_idx=0
        )
        # Inner rear wheelwell liner
        p_rf.add_box((sx * 0.56, 0.24, -1.80), (sx * 0.62, 0.76, -0.90), mat_idx=1)
        p_rf.build(parent=root, pivot=(sx * 0.80, 0.64, -1.36), visible=True)

    # Roof & Muscle Fastback Greenhouse (height = 1.36 m, double-bubble roof + long fastback C-pillars)
    p_roof = PartBuilder("roof_cabin", [mats["Paint_Body"], mats["Interior_Dark"]])
    p_roof.add_prism(
        [(-0.54, 1.28, -0.76), (0.54, 1.28, -0.76), (0.52, 1.30, 0.08), (-0.52, 1.30, 0.08)],
        [(-0.50, 1.34, -0.74), (0.50, 1.34, -0.74), (0.48, 1.36, 0.06), (-0.48, 1.36, 0.06)],
        mat_idx=0
    )
    p_roof.add_box((-0.50, 1.25, -0.72), (0.50, 1.28, 0.04), mat_idx=1)
    # Muscular A-Pillars (z = +0.58 base at cowl back to z = +0.06 roof header)
    p_roof.add_prism(
        [(0.60, 0.86, 0.56), (0.70, 0.86, 0.56), (0.52, 1.29, 0.06), (0.44, 1.29, 0.06)],
        [(0.58, 0.92, 0.60), (0.68, 0.92, 0.60), (0.50, 1.35, 0.10), (0.42, 1.35, 0.10)],
        mat_idx=0, mirror_x=True
    )
    # Long Fastback C-Pillars sweeping from z = -0.72 roof header all the way to z = -1.72 rear deck
    p_roof.add_prism(
        [(0.48, 0.92, -1.72), (0.60, 0.92, -1.72), (0.54, 1.28, -0.72), (0.44, 1.28, -0.72)],
        [(0.46, 0.98, -1.68), (0.58, 0.98, -1.68), (0.50, 1.34, -0.70), (0.40, 1.34, -0.70)],
        mat_idx=0, mirror_x=True
    )
    p_roof.build(parent=root, pivot=(0.0, 1.16, -0.34), visible=True)

    # Rear Fastback Trunk Decklid (engine_decklid slot name required by spec; z = -2.22 .. -1.44)
    p_deck = PartBuilder("engine_decklid", [mats["Paint_Body"], mats["Trim_Dark"]])
    p_deck.add_prism(
        [(-0.52, 0.86, -2.22), (0.52, 0.86, -2.22), (0.50, 0.92, -1.44), (-0.50, 0.92, -1.44)],
        [(-0.50, 0.92, -2.22), (0.50, 0.92, -2.22), (0.48, 0.99, -1.44), (-0.48, 0.99, -1.44)],
        mat_idx=0
    )
    p_deck.add_box((-0.44, 0.92, -2.14), (0.44, 0.95, -1.76), mat_idx=1)
    p_deck.build(parent=root, pivot=(0.0, 0.92, -1.82), visible=True)

    # =========================================================================
    # 3. WINDOWS (100% Outward Normals, Single Glass_Tint Submesh, _Cull=0)
    # =========================================================================
    p_ws = PartBuilder("glass_windshield", [mats["Glass_Tint"]])
    p_ws.add_submesh(
        [(0.00, 0.87, 0.58), (0.62, 0.86, 0.56), (0.00, 1.34, 0.07), (0.46, 1.33, 0.06)],
        [(0, 1, 3, 2)], mat_idx=0, mirror_x=True
    )
    p_ws.build(parent=root, pivot=(0.0, 1.10, 0.32), visible=True, outward_normal=(0.0, 1.0, 1.0))

    p_rw = PartBuilder("glass_rear", [mats["Glass_Tint"]])
    p_rw.add_submesh(
        [(0.00, 1.32, -0.74), (0.44, 1.31, -0.74), (0.00, 0.98, -1.66), (0.46, 0.97, -1.66)],
        [(0, 2, 3, 1)], mat_idx=0, mirror_x=True
    )
    p_rw.build(parent=root, pivot=(0.0, 1.14, -1.20), visible=True, outward_normal=(0.0, 1.0, -1.0))

    for side in (-1, 1):
        s_name = "left" if side < 0 else "right"
        sx = float(side)
        p_sw = PartBuilder(f"glass_side_{s_name}", [mats["Glass_Tint"]])
        swv = [
            (sx * 0.91, 0.88,  0.50),
            (sx * 0.91, 0.92, -0.72),
            (sx * 0.50, 1.32, -0.72),
            (sx * 0.48, 1.33,  0.06),
            (sx * 0.58, 0.96, -1.56),
        ]
        swf = [(0, 1, 2, 3), (1, 4, 2)] if side > 0 else [(0, 3, 2, 1), (1, 2, 4)]
        p_sw.add_submesh(swv, swf, mat_idx=0)
        p_sw.build(parent=root, pivot=(sx * 0.70, 1.10, -0.26), visible=True, outward_normal=(sx, 0.5, 0.0))

    # =========================================================================
    # 4. ENCLOSED INTERIOR CABIN & COCKPIT (Dual-Brow Muscle Dash + Screen)
    # =========================================================================
    p_cab = PartBuilder("interior_cabin", [mats["Interior_Dark"], mats["Trim_Dark"]])
    p_cab.add_box((-0.76, 0.20, -1.06), (0.76, 0.26,  0.60), mat_idx=0)  # floor
    p_cab.add_box((-0.76, 0.24,  0.48), (0.76, 0.88,  0.62), mat_idx=0)  # front firewall/cowl
    p_cab.add_box((-0.76, 0.24, -1.12), (0.76, 0.98, -0.92), mat_idx=0)  # rear bulkhead
    p_cab.add_box((-0.76, 0.90, -1.68), (0.76, 0.98, -0.92), mat_idx=0)  # fastback rear parcel shelf
    p_cab.add_box((-0.16, 0.26, -0.92), (0.16, 0.54,  0.52), mat_idx=1)  # V8 manual/seq transmission tunnel
    p_cab.add_cylinder((0.0, 0.52, 0.02), (0.0, 0.70, 0.04), 0.020, 0.028, segs=8, mat_idx=1)  # cue-ball shifter
    p_cab.build(parent=root, pivot=(0.0, 0.52, -0.20), visible=True)

    p_dash = PartBuilder("interior_dash", [mats["Interior_Dark"], mats["Screen"]])
    # Symmetrical "dual-brow" muscle dashboard (driver brow top at y = 0.900 m, open gauge cavity y = 0.80..0.88 m)
    p_dash.add_box((-0.74, 0.60, 0.22), (0.74, 0.80, 0.56), mat_idx=0)
    p_dash.add_box((-0.52, 0.88, 0.20), (-0.24, 0.90, 0.46), mat_idx=0)
    p_dash.add_box((-0.52, 0.80, 0.20), (-0.49, 0.88, 0.44), mat_idx=0)
    p_dash.add_box((-0.27, 0.80, 0.20), (-0.24, 0.88, 0.44), mat_idx=0)
    p_dash.add_box((-0.50, 0.80, 0.25), (-0.26, 0.88, 0.46), mat_idx=0)
    p_dash.add_box(( 0.22, 0.80, 0.20), ( 0.54, 0.88, 0.46), mat_idx=0)
    # Steering column sleeve entering lower dash below gauge screen
    p_dash.add_cylinder((-0.38, 0.72, 0.07), (-0.38, 0.69, 0.24), 0.032, 0.038, segs=10, mat_idx=0)
    # Compact digital gauge cluster display (width 0.22 m = 14.9% <= 25% of 1.48 m dash width)
    p_dash.add_box((-0.49, 0.805, 0.23), (-0.27, 0.875, 0.25), mat_idx=1)
    p_dash.build(parent=root, pivot=(0.0, 0.76, 0.36), visible=True)

    p_seat = PartBuilder("interior_seat_driver", [mats["Interior_Dark"], mats["Paint_Accent"]])
    p_seat.add_box((-0.58, 0.26, -0.60), (-0.18, 0.38, -0.08), mat_idx=0)
    p_seat.add_box((-0.56, 0.36, -0.66), (-0.20, 0.94, -0.52), mat_idx=0)
    p_seat.add_box((-0.60, 0.38, -0.58), (-0.52, 0.82, -0.24), mat_idx=1)
    p_seat.add_box((-0.24, 0.38, -0.58), (-0.16, 0.82, -0.24), mat_idx=1)
    p_seat.add_box((-0.50, 0.94, -0.64), (-0.26, 1.12, -0.52), mat_idx=1)
    p_seat.build(parent=root, pivot=(-0.38, 0.62, -0.38), visible=True)

    # =========================================================================
    # 5. INTERIOR MOD SLOTS (steering, interior_passenger, interior_pillar, rollcage)
    #    Pivot at hub center (-0.380, 0.720, 0.040).
    #    Top of wheel reaches y = 0.867 m (3.3 cm below 0.900 m dash top line!).
    # =========================================================================
    sw_pivot = (-0.38, 0.72, 0.04)
    build_steering_variant("mod_steering_0_stock",      [mats["Interior_Dark"], mats["Paint_Accent"]], sw_pivot, root_obj=root, visible=True,  r_rim=0.148, is_yoke=False, spoke_mat_idx=0)
    build_steering_variant("mod_steering_1_deep_dish",  [mats["Interior_Dark"], mats["Paint_Accent"]], sw_pivot, root_obj=root, visible=False, r_rim=0.145, is_yoke=False, spoke_mat_idx=0)
    build_steering_variant("mod_steering_2_track_yoke", [mats["Interior_Dark"], mats["Paint_Accent"]], sw_pivot, root_obj=root, visible=False, r_rim=0.145, is_yoke=True,  spoke_mat_idx=0)

    pass_pivot = (0.38, 0.54, -0.38)
    pp0 = PartBuilder("mod_interior_passenger_0_seat", [mats["Interior_Dark"], mats["Paint_Accent"]])
    pp0.add_box((0.18, 0.26, -0.60), (0.58, 0.38, -0.08), mat_idx=0)
    pp0.add_box((0.20, 0.36, -0.66), (0.56, 0.94, -0.52), mat_idx=0)
    pp0.add_box((0.52, 0.38, -0.58), (0.60, 0.82, -0.24), mat_idx=1)
    pp0.add_box((0.16, 0.38, -0.58), (0.24, 0.82, -0.24), mat_idx=1)
    pp0.add_box((0.26, 0.94, -0.64), (0.50, 1.12, -0.52), mat_idx=1)
    pp0.build(parent=root, pivot=pass_pivot, visible=True)

    pp1 = PartBuilder("mod_interior_passenger_1_twin_nos", [mats["Paint_Accent"], mats["Metal_Chrome"]])
    pp1.add_box((0.18, 0.26, -0.58), (0.58, 0.32, -0.10), mat_idx=1)
    for bx in (0.28, 0.48):
        pp1.add_cylinder((bx, 0.38, -0.56), (bx, 0.46, -0.12), 0.075, 0.075, segs=10, mat_idx=0)
        pp1.add_cylinder((bx, 0.46, -0.12), (bx, 0.48, -0.04), 0.028, 0.022, segs=8, mat_idx=1)
    pp1.build(parent=root, pivot=pass_pivot, visible=False)

    pil_pivot = (-0.56, 1.06, 0.32)
    pil0 = PartBuilder("mod_interior_pillar_0_stock", [mats["Interior_Dark"]])
    pil0.add_prism(
        [(-0.68, 0.86, 0.54), (-0.60, 0.86, 0.54), (-0.50, 1.26, 0.08), (-0.54, 1.26, 0.08)],
        [(-0.66, 0.90, 0.56), (-0.58, 0.90, 0.56), (-0.48, 1.30, 0.10), (-0.52, 1.30, 0.10)],
        mat_idx=0
    )
    pil0.build(parent=root, pivot=pil_pivot, visible=True)

    pil1 = PartBuilder("mod_interior_pillar_1_triple_gauges", [mats["Interior_Dark"], mats["Screen"]])
    pil1.add_prism(
        [(-0.68, 0.86, 0.54), (-0.60, 0.86, 0.54), (-0.50, 1.26, 0.08), (-0.54, 1.26, 0.08)],
        [(-0.66, 0.90, 0.56), (-0.58, 0.90, 0.56), (-0.48, 1.30, 0.10), (-0.52, 1.30, 0.10)],
        mat_idx=0
    )
    for gi in range(3):
        t = 0.22 + gi * 0.22
        gx = -0.62 + t * 0.10
        gy = 0.90 + t * 0.34
        gz = 0.50 - t * 0.40
        pil1.add_cylinder((gx, gy, gz), (gx + 0.03, gy - 0.01, gz - 0.03), 0.024, 0.024, segs=8, mat_idx=0)
        pil1.add_cylinder((gx + 0.03, gy - 0.01, gz - 0.03), (gx + 0.035, gy - 0.012, gz - 0.035), 0.020, 0.020, segs=8, mat_idx=1)
    pil1.build(parent=root, pivot=pil_pivot, visible=False)

    rc_pivot = (0.0, 0.78, -0.58)
    add_empty("mod_rollcage_0_none", rc_pivot, parent=root, forward_plus_z=True, visible=True)

    prc1 = PartBuilder("mod_rollcage_1_street_half", [mats["Paint_Accent"]])
    prc1.add_cylinder((0.54, 0.28, -0.72), (0.46, 1.24, -0.70), 0.024, 0.024, segs=6, mat_idx=0, mirror_x=True)
    prc1.add_cylinder((-0.46, 1.24, -0.70), (0.46, 1.24, -0.70), 0.024, 0.024, segs=6, mat_idx=0)
    prc1.add_cylinder((-0.46, 1.24, -0.70), (0.54, 0.28, -0.72), 0.020, 0.020, segs=6, mat_idx=0)
    prc1.add_cylinder((0.46, 1.24, -0.70), (-0.54, 0.28, -0.72), 0.020, 0.020, segs=6, mat_idx=0)
    prc1.build(parent=root, pivot=rc_pivot, visible=False)

    prc2 = PartBuilder("mod_rollcage_2_full_fia", [mats["Paint_Accent"]])
    prc2.add_cylinder((0.54, 0.28, -0.72), (0.46, 1.24, -0.70), 0.024, 0.024, segs=6, mat_idx=0, mirror_x=True)
    prc2.add_cylinder((-0.46, 1.24, -0.70), (0.46, 1.24, -0.70), 0.024, 0.024, segs=6, mat_idx=0)
    prc2.add_cylinder((-0.46, 1.24, -0.70), (0.54, 0.28, -0.72), 0.020, 0.020, segs=6, mat_idx=0)
    prc2.add_cylinder((0.46, 1.24, -0.70), (0.44, 1.26,  0.04), 0.022, 0.022, segs=6, mat_idx=0, mirror_x=True)
    prc2.add_cylinder((0.54, 0.28, -0.72), (0.58, 0.56,  0.46), 0.020, 0.020, segs=6, mat_idx=0, mirror_x=True)
    prc2.build(parent=root, pivot=rc_pivot, visible=False)

    # =========================================================================
    # 6. FRUNK / UNDER-HOOD V8 ENGINE BAY SLOT (mod_frunk_0_stock, mod_frunk_1_custom_audio)
    # =========================================================================
    frunk_pivot = (0.0, 0.50, 1.36)
    pfr0 = PartBuilder("mod_frunk_0_stock", [mats["Trim_Dark"], mats["Metal_Chrome"]])
    # Big naturally-aspirated / supercharged 5.0L V8 intake manifold & strut tower bar
    pfr0.add_box((-0.46, 0.28, 0.80), (0.46, 0.64, 1.72), mat_idx=0)
    pfr0.add_box((-0.30, 0.64, 0.92), (0.30, 0.76, 1.54), mat_idx=1)
    pfr0.build(parent=root, pivot=frunk_pivot, visible=True)

    pfr1 = PartBuilder("mod_frunk_1_custom_audio", [mats["Interior_Dark"], mats["Neon"]])
    pfr1.add_box((-0.48, 0.28, 0.80), (0.48, 0.64, 1.72), mat_idx=0)
    pfr1.add_cylinder((-0.24, 0.62, 1.26), (-0.24, 0.74, 1.28), 0.14, 0.11, segs=10, mat_idx=1)
    pfr1.add_cylinder(( 0.24, 0.62, 1.26), ( 0.24, 0.74, 1.28), 0.14, 0.11, segs=10, mat_idx=1)
    pfr1.build(parent=root, pivot=frunk_pivot, visible=False)

    # =========================================================================
    # 7. HIGH MUSCULAR COWL-INDUCTION V8 HOOD VARIANTS (mod_hood_0_stock .. 4_carbon)
    #    Spans z = +0.56 (windshield cowl) to z = +2.18 (high muscle nose)
    # =========================================================================
    hood_pivot = (0.0, 0.82, 1.36)
    for idx_h, (h_name, h_style) in enumerate((
        ("mod_hood_0_stock",  "stock"),
        ("mod_hood_1_street", "street"),
        ("mod_hood_2_vented", "vented"),
        ("mod_hood_3_track",  "track"),
        ("mod_hood_4_carbon", "carbon"),
    )):
        h_mat0 = mats["Carbon"] if h_style == "carbon" else mats["Paint_Body"]
        ph = PartBuilder(h_name, [h_mat0, mats["Trim_Dark"]])
        # Main muscular hood slab (z = 0.56 .. 2.18, high front nose y = 0.74)
        ph.add_prism(
            [(-0.54, 0.76, 1.40), (0.54, 0.76, 1.40), (0.52, 0.68, 2.18), (-0.52, 0.68, 2.18)],
            [(-0.54, 0.82, 1.40), (0.54, 0.82, 1.40), (0.52, 0.74, 2.18), (-0.52, 0.74, 2.18)],
            mat_idx=0
        )
        ph.add_prism(
            [(-0.56, 0.82, 0.56), (0.56, 0.82, 0.56), (0.54, 0.76, 1.40), (-0.54, 0.76, 1.40)],
            [(-0.56, 0.88, 0.56), (0.56, 0.88, 0.56), (0.54, 0.82, 1.40), (-0.54, 0.82, 1.40)],
            mat_idx=0
        )
        # Signature wide central Cowl-Induction Power-Dome bulge
        ph.add_prism(
            [(-0.30, 0.86, 0.62), (0.30, 0.86, 0.62), (0.24, 0.73, 2.02), (-0.24, 0.73, 2.02)],
            [(-0.26, 0.92, 0.62), (0.26, 0.92, 0.62), (0.20, 0.78, 2.02), (-0.20, 0.78, 2.02)],
            mat_idx=0
        )
        # Twin heat-extractor hood vents
        ph.add_box((0.14, 0.81, 1.44), (0.36, 0.86, 1.78), mat_idx=1, mirror_x=True)

        if h_style in ("street", "track", "carbon"):
            # Forward ram-air scoop opening
            ph.add_box((-0.22, 0.78, 1.66), (0.22, 0.86, 1.98), mat_idx=1)
        if h_style in ("vented", "track", "carbon"):
            for vz in (0.96, 1.14, 1.32, 1.50):
                ph.add_box((0.28, 0.82, vz), (0.48, 0.89, vz + 0.08), mat_idx=1, mirror_x=True)
        if h_style == "carbon":
            ph.add_cylinder((0.38, 0.74, 2.06), (0.38, 0.77, 2.06), 0.03, 0.03, segs=8, mat_idx=1, mirror_x=True)

        ph.build(parent=root, pivot=hood_pivot, visible=(idx_h == 0))

    # =========================================================================
    # 8. FENDER ADD-ON VARIANTS (mod_fender_0_stock .. 2_widebody)
    # =========================================================================
    fend_pivot = (0.0, 0.64, 0.0)
    pf0 = PartBuilder("mod_fender_0_stock", [mats["Trim_Dark"]])
    # Subtle side badge/vent trim behind front wheel arch
    pf0.add_box((0.955, 0.56, 0.64), (0.980, 0.66, 0.78), mat_idx=0, mirror_x=True)
    pf0.build(parent=root, pivot=fend_pivot, visible=True)

    pf1 = PartBuilder("mod_fender_1_louvers", [mats["Carbon"]])
    for li in range(4):
        lz = 1.18 + li * 0.12
        pf1.add_box((0.64, 0.82, lz), (0.90, 0.89, lz + 0.06), mat_idx=0, mirror_x=True)
    for li in range(4):
        lz = -1.56 + li * 0.12
        pf1.add_box((0.66, 0.92, lz), (0.92, 0.99, lz + 0.06), mat_idx=0, mirror_x=True)
    pf1.build(parent=root, pivot=fend_pivot, visible=False)

    pf2 = PartBuilder("mod_fender_2_widebody", [mats["Paint_Body"], mats["Carbon"]])
    pf2.add_box((0.90, 0.28,  0.90), (1.02, 0.84,  1.80), mat_idx=0, mirror_x=True)
    pf2.add_box((0.92, 0.30, -1.82), (1.03, 0.94, -0.90), mat_idx=0, mirror_x=True)
    for li in range(4):
        lz = 1.18 + li * 0.12
        pf2.add_box((0.66, 0.82, lz), (0.92, 0.90, lz + 0.06), mat_idx=1, mirror_x=True)
    pf2.build(parent=root, pivot=fend_pivot, visible=False)

    # =========================================================================
    # 9. FRONT BUMPERS (mod_bumper_front_0_stock .. 4_timeattack)
    #    Deeply Recessed Trapezoidal Muscle Grille + Diagonal Fang Struts
    # =========================================================================
    fb_pivot = (0.0, 0.40, 2.20)
    for idx_b, (b_name, lip_ext, canards, rods) in enumerate((
        ("mod_bumper_front_0_stock",      0.03, 0, False),
        ("mod_bumper_front_1_street",     0.08, 0, False),
        ("mod_bumper_front_2_race",       0.13, 1, False),
        ("mod_bumper_front_3_track",      0.17, 2, False),
        ("mod_bumper_front_4_timeattack", 0.24, 2, True),
    )):
        pfb = PartBuilder(b_name, [mats["Paint_Body"], mats["Trim_Dark"]])
        # Lower full-width muscle bumper fascia (up to y = 0.55 so outer corners leave pocket for headlights)
        pfb.add_prism(
            [(-0.88, 0.18, 2.10), (0.88, 0.18, 2.10), (0.80, 0.18, 2.34), (-0.80, 0.18, 2.34)],
            [(-0.86, 0.55, 2.10), (0.86, 0.55, 2.10), (0.78, 0.55, 2.32), (-0.78, 0.55, 2.32)],
            mat_idx=0
        )
        # Central raised grille surround between the headlights (x = -0.53 .. +0.53, y = 0.55 .. 0.70)
        pfb.add_prism(
            [(-0.53, 0.55, 2.10), (0.53, 0.55, 2.10), (0.52, 0.55, 2.33), (-0.52, 0.55, 2.33)],
            [(-0.53, 0.70, 2.10), (0.53, 0.70, 2.10), (0.52, 0.68, 2.32), (-0.52, 0.68, 2.32)],
            mat_idx=0
        )
        # Deeply recessed upper muscle grille cavity + diagonal fang struts
        pfb.add_box((-0.50, 0.44, 2.20), (0.50, 0.66, 2.35), mat_idx=1)
        pfb.add_prism(
            [(0.28, 0.44, 2.31), (0.33, 0.44, 2.31), (0.33, 0.44, 2.355), (0.28, 0.44, 2.355)],
            [(0.34, 0.66, 2.29), (0.39, 0.66, 2.29), (0.39, 0.66, 2.335), (0.34, 0.66, 2.335)],
            mat_idx=0, mirror_x=True
        )
        # Lower trapezoidal splitter intake + side brake ducts
        pfb.add_box((-0.56, 0.20, 2.22), (0.56, 0.38, 2.35), mat_idx=1)
        pfb.add_box(( 0.60, 0.22, 2.18), (0.82, 0.42, 2.33), mat_idx=1, mirror_x=True)
        # Front chin splitter
        pfb.add_prism(
            [(-0.90, 0.13, 2.06), (0.90, 0.13, 2.06), (0.84, 0.13, 2.34 + lip_ext), (-0.84, 0.13, 2.34 + lip_ext)],
            [(-0.90, 0.18, 2.06), (0.90, 0.18, 2.06), (0.84, 0.18, 2.34 + lip_ext), (-0.84, 0.18, 2.34 + lip_ext)],
            mat_idx=1
        )
        if canards >= 1:
            pfb.add_box((0.78, 0.26, 2.16), (0.96, 0.30, 2.32), mat_idx=1, mirror_x=True)
        if canards >= 2:
            pfb.add_box((0.76, 0.38, 2.14), (0.97, 0.42, 2.30), mat_idx=1, mirror_x=True)
        if rods:
            pfb.add_cylinder((0.36, 0.16, 2.34 + lip_ext * 0.8), (0.36, 0.38, 2.30), 0.010, 0.010, segs=6, mat_idx=1, mirror_x=True)
        pfb.build(parent=root, pivot=fb_pivot, visible=(idx_b == 0))

    # =========================================================================
    # 10. REAR BUMPERS (mod_bumper_rear_0_stock .. 3_extreme)
    # =========================================================================
    rb_pivot = (0.0, 0.38, -2.20)
    for idx_rb, (rb_name, diff_ext, n_fins) in enumerate((
        ("mod_bumper_rear_0_stock",   0.02, 2),
        ("mod_bumper_rear_1_street",  0.06, 3),
        ("mod_bumper_rear_2_track",   0.10, 4),
        ("mod_bumper_rear_3_extreme", 0.18, 5),
    )):
        prb = PartBuilder(rb_name, [mats["Paint_Body"], mats["Trim_Dark"]])
        prb.add_prism(
            [(-0.88, 0.20, -2.32), (0.88, 0.20, -2.32), (0.92, 0.20, -2.08), (-0.92, 0.20, -2.08)],
            [(-0.86, 0.74, -2.32), (0.86, 0.74, -2.32), (0.90, 0.74, -2.08), (-0.90, 0.74, -2.08)],
            mat_idx=0
        )
        # Lower quad-tip diffuser housing
        prb.add_box((-0.74, 0.14, -2.33 - diff_ext), (0.74, 0.36, -2.08), mat_idx=1)
        for fi in range(n_fins):
            fx = 0.10 + fi * 0.13
            prb.add_box((fx, 0.12, -2.34 - diff_ext), (fx + 0.025, 0.34, -2.12), mat_idx=1, mirror_x=True)
        prb.build(parent=root, pivot=rb_pivot, visible=(idx_rb == 0))

    # =========================================================================
    # 11. SIDE SKIRTS (Left & Right Separate Slots: mod_skirts_0_stock_left/right .. 3_wide)
    # =========================================================================
    for side in (-1, 1):
        s_tag = "left" if side < 0 else "right"
        sx = float(side)
        sk_pivot = (sx * 0.90, 0.18, 0.0)
        for idx_sk, (sk_sub, w_ext, fin_h) in enumerate((
            ("0_stock",  0.00, 0.00),
            ("1_street", 0.03, 0.04),
            ("2_aero",   0.05, 0.09),
            ("3_wide",   0.08, 0.14),
        )):
            psk = PartBuilder(f"mod_skirts_{sk_sub}_{s_tag}", [mats["Paint_Body"], mats["Carbon"]])
            x_in = min(sx * 0.76, sx * (0.95 + w_ext))
            x_out = max(sx * 0.76, sx * (0.95 + w_ext))
            psk.add_box((x_in, 0.14, -0.90), (x_out, 0.24, 0.90), mat_idx=0)
            psk.add_box((min(sx * 0.88, sx * (0.97 + w_ext)), 0.11, -0.90), (max(sx * 0.88, sx * (0.97 + w_ext)), 0.15, 0.90), mat_idx=1)
            if fin_h > 0.0:
                fx0 = min(sx * (0.94 + w_ext), sx * (0.97 + w_ext))
                fx1 = max(sx * (0.94 + w_ext), sx * (0.97 + w_ext))
                psk.add_box((fx0, 0.12, -0.90), (fx1, 0.16 + fin_h, -0.68), mat_idx=1)
            psk.build(parent=root, pivot=sk_pivot, visible=(idx_sk == 0))

    # =========================================================================
    # 12. SPOILERS (mod_spoiler_0_stock .. 5_timeattack)
    # =========================================================================
    sp_pivot = (0.0, 0.94, -2.16)

    ps0 = PartBuilder("mod_spoiler_0_stock", [mats["Paint_Body"]])
    ps0.add_box((-0.66, 0.90, -2.22), (0.66, 0.97, -2.06), mat_idx=0)
    ps0.build(parent=root, pivot=sp_pivot, visible=True)

    ps1 = PartBuilder("mod_spoiler_1_ducktail", [mats["Carbon"]])
    ps1.add_prism(
        [(-0.72, 0.90, -2.22), (0.72, 0.90, -2.22), (0.72, 0.90, -2.04), (-0.72, 0.90, -2.04)],
        [(-0.68, 1.08, -2.30), (0.68, 1.08, -2.30), (0.68, 0.96, -2.08), (-0.68, 0.96, -2.08)],
        mat_idx=0
    )
    ps1.build(parent=root, pivot=sp_pivot, visible=False)

    for idx_w, (w_name, w_span, w_h, w_z, swan, dual) in enumerate((
        ("mod_spoiler_2_street",     0.78, 1.12, -2.20, False, False),
        ("mod_spoiler_3_trackwing",  0.90, 1.24, -2.24, False, False),
        ("mod_spoiler_4_swanneck",   0.94, 1.30, -2.28, True,  False),
        ("mod_spoiler_5_timeattack", 0.98, 1.34, -2.32, True,  True),
    ), start=2):
        pw = PartBuilder(w_name, [mats["Carbon"], mats["Paint_Accent"]])
        st_x = 0.34 if swan else 0.42
        pw.add_box((st_x - 0.02, 0.90, w_z - 0.06), (st_x + 0.02, w_h, w_z + 0.06), mat_idx=0, mirror_x=True)
        if swan:
            pw.add_box((st_x - 0.02, w_h - 0.02, w_z - 0.08), (st_x + 0.02, w_h + 0.05, w_z + 0.08), mat_idx=0, mirror_x=True)
        pw.add_prism(
            [(-w_span, w_h - 0.02, w_z - 0.14), (w_span, w_h - 0.02, w_z - 0.14), (w_span, w_h - 0.04, w_z + 0.10), (-w_span, w_h - 0.04, w_z + 0.10)],
            [(-w_span, w_h + 0.02, w_z - 0.14), (w_span, w_h + 0.02, w_z - 0.14), (w_span, w_h,        w_z + 0.10), (-w_span, w_h,        w_z + 0.10)],
            mat_idx=0
        )
        if dual:
            pw.add_box((-w_span, w_h + 0.07, w_z - 0.16), (w_span, w_h + 0.10, w_z + 0.02), mat_idx=0)
        pw.add_box((w_span - 0.02, w_h - 0.12, w_z - 0.18), (w_span + 0.01, w_h + (0.14 if dual else 0.08), w_z + 0.12), mat_idx=1, mirror_x=True)
        pw.build(parent=root, pivot=sp_pivot, visible=False)

    # =========================================================================
    # 13. ROOF SCOOPS & MIRRORS
    # =========================================================================
    rs_pivot = (0.0, 1.35, -0.24)
    add_empty("mod_roof_scoop_0_none", rs_pivot, parent=root, forward_plus_z=True, visible=True)

    prs1 = PartBuilder("mod_roof_scoop_1_single", [mats["Paint_Body"], mats["Trim_Dark"]])
    prs1.add_box((-0.14, 1.34, -0.44), (0.14, 1.43, -0.02), mat_idx=0)
    prs1.add_box((-0.11, 1.35, -0.04), (0.11, 1.41,  0.00), mat_idx=1)
    prs1.build(parent=root, pivot=rs_pivot, visible=False)

    prs2 = PartBuilder("mod_roof_scoop_2_dual", [mats["Carbon"], mats["Trim_Dark"]])
    prs2.add_box((0.03, 1.34, -0.46), (0.20, 1.43, -0.02), mat_idx=0, mirror_x=True)
    prs2.add_box((0.05, 1.35, -0.04), (0.18, 1.41,  0.00), mat_idx=1, mirror_x=True)
    prs2.build(parent=root, pivot=rs_pivot, visible=False)

    prs3 = PartBuilder("mod_roof_scoop_3_race", [mats["Carbon"], mats["Paint_Accent"]])
    prs3.add_box((-0.16, 1.34, -0.70), (0.16, 1.46,  0.00), mat_idx=0)
    prs3.add_box((-0.02, 1.44, -0.74), (0.02, 1.54, -0.14), mat_idx=1)
    prs3.build(parent=root, pivot=rs_pivot, visible=False)

    mir_pivot = (0.0, 0.92, 0.38)
    for idx_m, (m_name, m_mat, m_span) in enumerate((
        ("mod_mirror_0_stock",     mats["Paint_Body"], 1.05),
        ("mod_mirror_1_aero",      mats["Paint_Body"], 1.07),
        ("mod_mirror_2_carbon_f1", mats["Carbon"],     1.03),
    )):
        pm = PartBuilder(m_name, [m_mat, mats["Trim_Dark"]])
        pm.add_box((0.88, 0.86, 0.36), (0.96, 0.93, 0.44), mat_idx=1, mirror_x=True)
        pm.add_box((0.94, 0.90, 0.30), (m_span, 0.98, 0.46), mat_idx=0, mirror_x=True)
        pm.build(parent=root, pivot=mir_pivot, visible=(idx_m == 0))

    # =========================================================================
    # 14. HEADLIGHTS, TAILLIGHTS, EXHAUSTS & NEON
    #     CRITICAL RULE (Section 6.1):
    #     Headlight housing (Trim_Dark) front z = 2.310, top y = 0.720
    #     Headlight lens (Light_Head) front z = 2.318 (>= 2.310 + 0.005!), top y = 0.705 (<= 0.720!)
    # =========================================================================
    hl_pivot = (0.0, 0.64, 2.20)
    for idx_hl, hl_name in enumerate((
        "mod_headlight_0_stock",
        "mod_headlight_1_amber",
        "mod_headlight_2_halo",
        "mod_headlight_3_rally",
    )):
        phl = PartBuilder(hl_name, [mats["Trim_Dark"], mats["Light_Head"]])
        # Housing frame: front face at z = 2.310, top face at y = 0.720
        phl.add_box((0.53, 0.550, 2.080), (0.84, 0.720, 2.310), mat_idx=0, mirror_x=True)
        # Signature Tri-Bar LED DRL blades + projector lens: front face at z = 2.318, top face at y = 0.705
        for bi in range(3):
            bx0 = 0.55 + bi * 0.055
            phl.add_box((bx0, 0.575, 2.120), (bx0 + 0.032, 0.705, 2.318), mat_idx=1, mirror_x=True)
        phl.add_box((0.72, 0.580, 2.120), (0.82, 0.695, 2.318), mat_idx=1, mirror_x=True)
        if idx_hl == 3:
            phl.add_cylinder((0.38, 0.58, 2.28), (0.38, 0.58, 2.322), 0.065, 0.065, segs=10, mat_idx=1, mirror_x=True)
        phl.build(parent=root, pivot=hl_pivot, visible=(idx_hl == 0))

    # Signature Recessed Kammback Rear Panel + Tri-Bar Vertical LED Taillights
    tl_pivot = (0.0, 0.82, -2.26)
    for idx_tl, tl_name in enumerate((
        "mod_taillight_0_stock",
        "mod_taillight_1_led_bar",
        "mod_taillight_2_smoked_quad",
    )):
        ptl = PartBuilder(tl_name, [mats["Trim_Dark"], mats["Light_Tail"]])
        # Full-width black Kammback deck panel
        ptl.add_box((-0.84, 0.74, -2.32), (0.84, 0.90, -2.20), mat_idx=0)
        if idx_tl == 0:
            # 3 vertical Tri-Bar LED bars per side
            for bi in range(3):
                bx0 = 0.38 + bi * 0.14
                ptl.add_box((bx0, 0.755, -2.332), (bx0 + 0.085, 0.885, -2.22), mat_idx=1, mirror_x=True)
        elif idx_tl == 1:
            ptl.add_box((-0.80, 0.78, -2.332), (0.80, 0.86, -2.22), mat_idx=1)
        else:
            for cx in (0.44, 0.68):
                ptl.add_cylinder((cx, 0.82, -2.26), (cx, 0.82, -2.335), 0.048, 0.048, segs=10, mat_idx=1, mirror_x=True)
        ptl.build(parent=root, pivot=tl_pivot, visible=(idx_tl == 0))

    ex_pivot = (0.0, 0.28, -2.32)
    for idx_ex, ex_name in enumerate((
        "mod_exhaust_0_stock",
        "mod_exhaust_1_dual_titanium",
        "mod_exhaust_2_center_race",
        "mod_exhaust_3_quad_extreme",
    )):
        pex = PartBuilder(ex_name, [mats["Metal_Chrome"], mats["Trim_Dark"]])
        if idx_ex == 0:
            for cx in (0.48, 0.60):
                pex.add_cylinder((cx, 0.28, -2.14), (cx, 0.28, -2.36), 0.048, 0.048, segs=10, mat_idx=0, mirror_x=True)
        elif idx_ex == 1:
            pex.add_cylinder((0.54, 0.28, -2.14), (0.54, 0.28, -2.38), 0.068, 0.068, segs=12, mat_idx=0, mirror_x=True)
        elif idx_ex == 2:
            for cx in (0.09, 0.22):
                pex.add_cylinder((cx, 0.28, -2.14), (cx, 0.28, -2.38), 0.052, 0.052, segs=10, mat_idx=0, mirror_x=True)
        else:
            for cx in (0.46, 0.60):
                pex.add_cylinder((cx, 0.28, -2.14), (cx, 0.28, -2.40), 0.056, 0.056, segs=10, mat_idx=0, mirror_x=True)
        pex.build(parent=root, pivot=ex_pivot, visible=(idx_ex == 0))

    neon_pivot = (0.0, 0.10, 0.0)
    add_empty("mod_neon_0_none", neon_pivot, parent=root, forward_plus_z=True, visible=True)

    pn1 = PartBuilder("mod_neon_1_sides", [mats["Neon"]])
    pn1.add_box((0.84, 0.09, -0.82), (0.88, 0.11, 0.82), mat_idx=0, mirror_x=True)
    pn1.build(parent=root, pivot=neon_pivot, visible=False)

    pn2 = PartBuilder("mod_neon_2_full", [mats["Neon"]])
    pn2.add_box((0.84, 0.09, -0.82), (0.88, 0.11, 0.82), mat_idx=0, mirror_x=True)
    pn2.add_box((-0.58, 0.09,  2.10), (0.58, 0.11,  2.16), mat_idx=0)
    pn2.add_box((-0.58, 0.09, -2.22), (0.58, 0.11, -2.16), mat_idx=0)
    pn2.build(parent=root, pivot=neon_pivot, visible=False)

    # =========================================================================
    # 15. DECALS / STICKERS (decal_1 .. decal_5, All Hidden by Default, Outward Quads)
    # =========================================================================
    pdec1 = PartBuilder("decal_1_center_stripes", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec1.add_submesh(
        [(0.04, 0.924, 0.62), (0.14, 0.924, 0.62), (0.14, 0.784, 2.02), (0.04, 0.784, 2.02)],
        [(0, 1, 2, 3)], mat_idx=0, mirror_x=True
    )
    pdec1.add_submesh(
        [(0.04, 1.344, -0.74), (0.14, 1.344, -0.74), (0.14, 1.364, 0.06), (0.04, 1.364, 0.06)],
        [(0, 1, 2, 3)], mat_idx=0, mirror_x=True
    )
    pdec1.add_submesh(
        [(0.145, 0.924, 0.62), (0.165, 0.924, 0.62), (0.165, 0.784, 2.02), (0.145, 0.784, 2.02)],
        [(0, 1, 2, 3)], mat_idx=1, mirror_x=True
    )
    pdec1.build(parent=root, pivot=(0.0, 0.96, 0.66), visible=False, outward_normal=(0.0, 1.0, 0.2))

    pdec2 = PartBuilder("decal_2_side_rocker", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec2.add_submesh(
        [(0.962, 0.26, -0.52), (0.962, 0.26, 0.52), (0.962, 0.34, 0.52), (0.962, 0.34, -0.52)],
        [(0, 1, 2, 3)], mat_idx=0, mirror_x=True
    )
    pdec2.add_submesh(
        [(0.962, 0.345, -0.52), (0.962, 0.345, 0.52), (0.962, 0.37, 0.52), (0.962, 0.37, -0.52)],
        [(0, 1, 2, 3)], mat_idx=1, mirror_x=True
    )
    pdec2.build(parent=root, pivot=(0.0, 0.31, 0.0), visible=False, outward_normal="radial_x")

    pdec3 = PartBuilder("decal_3_number_plates", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec3.add_submesh(
        [(0.974, 0.42, -0.24), (0.974, 0.42, 0.28), (0.974, 0.74, 0.28), (0.974, 0.74, -0.24)],
        [(0, 1, 2, 3)], mat_idx=0, mirror_x=True
    )
    pdec3.add_submesh(
        [(0.978, 0.58, -0.14), (0.978, 0.58, 0.18), (0.978, 0.64, 0.18), (0.978, 0.64, -0.14)],
        [(0, 1, 2, 3)], mat_idx=1, mirror_x=True
    )
    pdec3.build(parent=root, pivot=(0.0, 0.58, 0.02), visible=False, outward_normal="radial_x")

    pdec4 = PartBuilder("decal_4_side_slashes", [mats["Paint_Accent"], mats["Decal_Graphic"]])
    pdec4.add_submesh(
        [(0.995, 0.60, -1.62), (0.995, 0.60, -1.00), (0.995, 0.68, -1.00), (0.995, 0.68, -1.62)],
        [(0, 1, 2, 3)], mat_idx=0, mirror_x=True
    )
    pdec4.add_submesh(
        [(0.995, 0.48, -1.72), (0.995, 0.48, -1.10), (0.995, 0.56, -1.10), (0.995, 0.56, -1.72)],
        [(0, 1, 2, 3)], mat_idx=1, mirror_x=True
    )
    pdec4.build(parent=root, pivot=(0.0, 0.58, -1.34), visible=False, outward_normal="radial_x")

    pdec5 = PartBuilder("decal_5_windshield_banner", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec5.add_submesh(
        [(0.00, 1.25, 0.18), (0.48, 1.24, 0.17), (0.00, 1.345, 0.07), (0.46, 1.335, 0.06)],
        [(0, 1, 3, 2)], mat_idx=0, mirror_x=True
    )
    pdec5.build(parent=root, pivot=(0.0, 1.29, 0.12), visible=False, outward_normal=(0.0, 1.0, 1.0))

    # =========================================================================
    # 16. WHEELS, CALIPERS (Under Root!), 4 RIM VARIANTS & TIRE LETTERING
    # =========================================================================
    wheel_specs = [
        ("wheel-front-left",  -0.84, 0.35,  1.36, 0.35, 0.28, -1),
        ("wheel-front-right",  0.84, 0.35,  1.36, 0.35, 0.28,  1),
        ("wheel-back-left",   -0.84, 0.37, -1.36, 0.37, 0.31, -1),
        ("wheel-back-right",   0.84, 0.37, -1.36, 0.37, 0.31,  1),
    ]

    for w_name, wx, wy, wz, radius, width, side in wheel_specs:
        sx = float(side)
        half_w = width * 0.5
        r_tire = radius
        r_rim = radius * 0.70
        r_hub = radius * 0.22
        r_disc = radius * 0.52
        n_seg = 20
        w_center = (wx, wy, wz)

        # Tire + Brake Disc (2 materials: Trim_Dark, Metal_Chrome)
        pw = PartBuilder(w_name, [mats["Trim_Dark"], mats["Metal_Chrome"]])
        tv = []
        tf_tire = []
        for i in range(n_seg):
            ang = 2.0 * math.pi * i / n_seg
            cy = wy + math.sin(ang) * r_tire
            cz = wz + math.cos(ang) * r_tire
            cy_r = wy + math.sin(ang) * r_rim
            cz_r = wz + math.cos(ang) * r_rim
            tv.extend([
                (wx - sx * half_w, cy, cz),
                (wx + sx * half_w, cy, cz),
                (wx - sx * half_w * 0.85, cy_r, cz_r),
                (wx + sx * half_w * 0.88, cy_r, cz_r),
            ])
        for i in range(n_seg):
            ni = (i + 1) % n_seg
            b0, b1 = i * 4, ni * 4
            if side > 0:
                tf_tire.extend([
                    (b0, b1, b1 + 1, b0 + 1),
                    (b0 + 1, b1 + 1, b1 + 3, b0 + 3),
                    (b1 + 2, b0 + 2, b0, b1),
                    (b0 + 3, b1 + 3, b1 + 2, b0 + 2),
                ])
            else:
                tf_tire.extend([
                    (b0 + 1, b1 + 1, b1, b0),
                    (b0 + 3, b1 + 3, b1 + 1, b0 + 1),
                    (b1, b0, b0 + 2, b1 + 2),
                    (b0 + 2, b1 + 2, b1 + 3, b0 + 3),
                ])
        pw.add_submesh(tv, tf_tire, mat_idx=0)
        pw.add_cylinder((wx - sx * 0.02, wy, wz), (wx + sx * 0.03, wy, wz), r_disc, r_disc, segs=12, mat_idx=1)
        w_obj = pw.build(parent=root, pivot=w_center, visible=True)

        # Caliper: Direct Child of Root, Pivot at Exact Wheel Center!
        cal_name = w_name.replace("wheel-", "caliper-")
        pcal = PartBuilder(cal_name, [mats["Paint_Caliper"]])
        cz_off = -0.13 if "front" in w_name else 0.13
        x_in = min(wx - sx * 0.04, wx + sx * 0.06)
        x_out = max(wx - sx * 0.04, wx + sx * 0.06)
        pcal.add_box((x_in, wy - 0.04, wz + cz_off - 0.05), (x_out, wy + 0.12, wz + cz_off + 0.05), mat_idx=0)
        pcal.build(parent=root, pivot=w_center, visible=True)

        # Rim 0: 10-Spoke Sport Rim (Default Visible)
        pr0 = PartBuilder(f"{w_name}_rim_0_sport", [mats["Paint_Rim"], mats["Metal_Chrome"]])
        for i in range(10):
            ang = 2.0 * math.pi * i / 10.0
            a_l, a_r = ang - 0.09, ang + 0.09
            v_sp = [
                (wx + sx * half_w * 0.84, wy + math.sin(a_l) * r_rim, wz + math.cos(a_l) * r_rim),
                (wx + sx * half_w * 0.84, wy + math.sin(a_r) * r_rim, wz + math.cos(a_r) * r_rim),
                (wx + sx * half_w * 0.54, wy + math.sin(a_r) * r_hub, wz + math.cos(a_r) * r_hub),
                (wx + sx * half_w * 0.54, wy + math.sin(a_l) * r_hub, wz + math.cos(a_l) * r_hub),
            ]
            pr0.add_submesh(v_sp, [(0, 1, 2, 3) if side > 0 else (3, 2, 1, 0)], mat_idx=0)
        pr0.add_cylinder((wx + sx * half_w * 0.45, wy, wz), (wx + sx * half_w * 0.65, wy, wz), r_hub, r_hub * 0.7, segs=10, mat_idx=1)
        pr0.build(parent=w_obj, pivot=w_center, visible=True, outward_normal=(sx, 0.0, 0.0))

        # Rim 1: 5-Spoke Deep Dish Torq-Muscle Style
        pr1 = PartBuilder(f"{w_name}_rim_1_5spoke", [mats["Paint_Rim"], mats["Metal_Chrome"]])
        for i in range(5):
            ang = 2.0 * math.pi * i / 5.0
            a_l, a_r = ang - 0.15, ang + 0.15
            v_sp = [
                (wx + sx * half_w * 0.82, wy + math.sin(a_l) * r_rim, wz + math.cos(a_l) * r_rim),
                (wx + sx * half_w * 0.82, wy + math.sin(a_r) * r_rim, wz + math.cos(a_r) * r_rim),
                (wx + sx * half_w * 0.46, wy + math.sin(a_r) * r_hub, wz + math.cos(a_r) * r_hub),
                (wx + sx * half_w * 0.46, wy + math.sin(a_l) * r_hub, wz + math.cos(a_l) * r_hub),
            ]
            pr1.add_submesh(v_sp, [(0, 1, 2, 3) if side > 0 else (3, 2, 1, 0)], mat_idx=0)
        pr1.add_cylinder((wx + sx * half_w * 0.40, wy, wz), (wx + sx * half_w * 0.62, wy, wz), r_hub, r_hub * 0.8, segs=10, mat_idx=1)
        pr1.build(parent=w_obj, pivot=w_center, visible=False, outward_normal=(sx, 0.0, 0.0))

        # Rim 2: Aero Turbofan
        pr2 = PartBuilder(f"{w_name}_rim_2_turbofan", [mats["Carbon"], mats["Paint_Accent"]])
        pr2.add_cylinder((wx + sx * half_w * 0.72, wy, wz), (wx + sx * half_w * 0.86, wy, wz), r_rim * 0.96, r_rim * 0.92, segs=16, mat_idx=0)
        pr2.add_cylinder((wx + sx * half_w * 0.86, wy, wz), (wx + sx * half_w * 0.90, wy, wz), r_hub * 0.9, r_hub * 0.6, segs=10, mat_idx=1)
        pr2.build(parent=w_obj, pivot=w_center, visible=False)

        # Rim 3: Y-Spoke Forged Mesh
        pr3 = PartBuilder(f"{w_name}_rim_3_mesh", [mats["Paint_Rim"], mats["Metal_Chrome"]])
        for i in range(8):
            a_base = 2.0 * math.pi * i / 8.0
            for branch in (-1, 1):
                a_tip = a_base + branch * 0.24
                v_y = [
                    (wx + sx * half_w * 0.84, wy + math.sin(a_tip - 0.04) * r_rim, wz + math.cos(a_tip - 0.04) * r_rim),
                    (wx + sx * half_w * 0.84, wy + math.sin(a_tip + 0.04) * r_rim, wz + math.cos(a_tip + 0.04) * r_rim),
                    (wx + sx * half_w * 0.50, wy + math.sin(a_base + 0.04) * r_hub, wz + math.cos(a_base + 0.04) * r_hub),
                    (wx + sx * half_w * 0.50, wy + math.sin(a_base - 0.04) * r_hub, wz + math.cos(a_base - 0.04) * r_hub),
                ]
                pr3.add_submesh(v_y, [(0, 1, 2, 3) if side > 0 else (3, 2, 1, 0)], mat_idx=0)
        pr3.add_cylinder((wx + sx * half_w * 0.44, wy, wz), (wx + sx * half_w * 0.66, wy, wz), r_hub, r_hub * 0.7, segs=10, mat_idx=1)
        pr3.build(parent=w_obj, pivot=w_center, visible=False, outward_normal=(sx, 0.0, 0.0))

        # Sidewall Tire Lettering
        ptl_let = PartBuilder(f"{w_name}_tire_lettering", [mats["Decal_Graphic"]])
        for arc_offset in (0.0, math.pi):
            for s_i in range(4):
                a0 = arc_offset - 0.44 + s_i * 0.22
                a1 = a0 + 0.15
                r_in_l, r_out_l = r_rim + 0.025, r_tire - 0.020
                vl = [
                    (wx + sx * (half_w + 0.003), wy + math.sin(a0) * r_out_l, wz + math.cos(a0) * r_out_l),
                    (wx + sx * (half_w + 0.003), wy + math.sin(a1) * r_out_l, wz + math.cos(a1) * r_out_l),
                    (wx + sx * (half_w + 0.003), wy + math.sin(a1) * r_in_l,  wz + math.cos(a1) * r_in_l),
                    (wx + sx * (half_w + 0.003), wy + math.sin(a0) * r_in_l,  wz + math.cos(a0) * r_in_l),
                ]
                ptl_let.add_submesh(vl, [(0, 1, 2, 3) if side > 0 else (3, 2, 1, 0)], mat_idx=0)
        ptl_let.build(parent=w_obj, pivot=w_center, visible=False, outward_normal=(sx, 0.0, 0.0))

    # =========================================================================
    # 17. EMPTIES: LIGHTS, COCKPIT CAMERA & EXHAUST TIPS
    # =========================================================================
    add_empty("light_head_L",   (-0.68, 0.64,  2.35), parent=root, forward_plus_z=True)
    add_empty("light_head_R",   ( 0.68, 0.64,  2.35), parent=root, forward_plus_z=True)
    add_empty("light_tail_L",   (-0.64, 0.84, -2.36), parent=root, forward_plus_z=False)
    add_empty("light_tail_R",   ( 0.64, 0.84, -2.36), parent=root, forward_plus_z=False)
    add_empty("camera_cockpit", (-0.38, 0.98, -0.38), parent=root, forward_plus_z=True)
    add_empty("exhaust_L",      (-0.54, 0.28, -2.38), parent=root, forward_plus_z=False)
    add_empty("exhaust_R",      ( 0.54, 0.28, -2.38), parent=root, forward_plus_z=False)

    # 1. Update view_layer while all objects are visible
    bpy.context.view_layer.update()

    # 2. Gizlemeyi yalniz gorunurlukle (hide_set) yap; hide_render ve hide_viewport KULLANMA!
    for o in bpy.data.objects:
        if "_target_visible" in o:
            vis = bool(o["_target_visible"])
            del o["_target_visible"]
            o.hide_set(not vis)
            o.hide_viewport = False
            o.hide_render = False

    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    bpy.context.view_layer.objects.active = root
    return root


def export_muscle_fastback_01_fbx():
    root = bpy.data.objects.get("MuscleFastback_01")
    top_children = list(root.children) if root else []
    vis_states = {o.name: o.hide_get() for o in bpy.data.objects}

    for o in bpy.data.objects:
        o.hide_viewport = False
        o.hide_render = False
        o.hide_set(False)
    bpy.context.view_layer.update()

    if root:
        for ch in top_children:
            ch.parent = None
            ch.matrix_parent_inverse = Matrix.Identity(4)
        bpy.context.collection.objects.unlink(root)
    bpy.context.view_layer.update()

    sw_objs = [o for o in bpy.data.objects if o.name.startswith("mod_steering_") and o.type == 'MESH']
    for sw in sw_objs:
        sw.data.transform(Matrix.Rotation(math.radians(90.0), 4, 'X'))
        sw.data.update()
        sw.rotation_euler = Euler((-math.radians(18.0), 0.0, 0.0), 'XYZ')
    bpy.context.view_layer.update()

    for name, hg in vis_states.items():
        o = bpy.data.objects.get(name)
        if o and o != root:
            o.hide_set(hg)
            o.hide_viewport = False
            o.hide_render = False

    out_dir = "/home/zoro/harvester/Harvester/Assets/Cars/MuscleFastback_01"
    os.makedirs(out_dir, exist_ok=True)
    fbx_path = os.path.join(out_dir, "MuscleFastback_01.fbx")
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

    for o in bpy.data.objects:
        o.hide_set(False)
    for sw in sw_objs:
        sw.data.transform(Matrix.Rotation(-math.radians(90.0), 4, 'X'))
        sw.data.update()
        sw.rotation_euler = Euler((math.radians(72.0), 0.0, 0.0), 'XYZ')
    if root:
        bpy.context.collection.objects.link(root)
        for ch in top_children:
            ch.parent = root
            ch.matrix_parent_inverse = Matrix.Identity(4)
    bpy.context.view_layer.update()
    for name, hg in vis_states.items():
        o = bpy.data.objects.get(name)
        if o:
            o.hide_set(hg)
            o.hide_viewport = False
            o.hide_render = False
    if root:
        bpy.ops.object.select_all(action='DESELECT')
        root.select_set(True)
        bpy.context.view_layer.objects.active = root

    return fbx_path


if __name__ == "__main__":
    build_muscle_fastback_01()
    export_muscle_fastback_01_fbx()
