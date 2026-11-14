import bpy
import bmesh
import math
import os
from mathutils import Vector, Matrix, Euler


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


def make_mat(name, color, metallic=0.0, roughness=0.45, emission=None, emission_strength=0.0, alpha=1.0, backface_culling=True):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    mat.use_backface_culling = backface_culling
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        if "Emission Color" in bsdf.inputs and emission:
            bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
            bsdf.inputs["Emission Strength"].default_value = emission_strength
        if alpha < 1.0:
            if "Alpha" in bsdf.inputs:
                bsdf.inputs["Alpha"].default_value = alpha
            if hasattr(mat, "blend_method"):
                mat.blend_method = 'BLEND'
    mat.diffuse_color = (*color, alpha)
    mat.metallic = metallic
    mat.roughness = roughness
    return mat


def u2b(v):
    """
    Authored car space: x = Right (+X), y = Forward (+Y), z = Up (+Z).
    Maps to Blender (-x, -y, z) (180 deg rotation around Z, det = +1).
    When exported with axis_forward='-Z', axis_up='Y', bake_space_transform=True,
    Unity receives: +X = Right, +Y = Up, +Z = Forward, root rotation = (0,0,0), scale = (1,1,1).
    """
    return (-float(v[0]), -float(v[1]), float(v[2]))


class PartBuilder:
    def __init__(self, name, materials):
        self.name = name
        self.materials = materials if isinstance(materials, list) else [materials]
        assert len(self.materials) <= 2, f"{name} has {len(self.materials)} materials (max allowed is 2)"
        self.verts = []
        self.faces = []

    def add_submesh(self, verts, faces, mat_idx=0, mirror_x=False):
        base = len(self.verts)
        self.verts.extend([u2b(v) for v in verts])
        for f in faces:
            self.faces.append((tuple(base + i for i in f), mat_idx))
        if mirror_x:
            sbase = len(self.verts)
            self.verts.extend([u2b((-v[0], v[1], v[2])) for v in verts])
            for f in faces:
                self.faces.append((tuple(sbase + i for i in reversed(f)), mat_idx))

    def add_box(self, p_min, p_max, mat_idx=0, mirror_x=False):
        x0, y0, z0 = min(p_min[0], p_max[0]), min(p_min[1], p_max[1]), min(p_min[2], p_max[2])
        x1, y1, z1 = max(p_min[0], p_max[0]), max(p_min[1], p_max[1]), max(p_min[2], p_max[2])
        v = [
            (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
            (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1),
        ]
        f = [
            (0, 3, 2, 1), (4, 5, 6, 7),
            (0, 1, 5, 4), (1, 2, 6, 5),
            (2, 3, 7, 6), (3, 0, 4, 7),
        ]
        self.add_submesh(v, f, mat_idx=mat_idx, mirror_x=mirror_x)

    def add_prism(self, quad_bottom, quad_top, mat_idx=0, mirror_x=False):
        v = list(quad_bottom) + list(quad_top)
        f = [
            (0, 3, 2, 1),
            (4, 5, 6, 7),
            (0, 1, 5, 4),
            (1, 2, 6, 5),
            (2, 3, 7, 6),
            (3, 0, 4, 7),
        ]
        self.add_submesh(v, f, mat_idx=mat_idx, mirror_x=mirror_x)

    def add_cylinder(self, p0, p1, r0, r1=None, segs=10, mat_idx=0, mirror_x=False, cap=True):
        if r1 is None:
            r1 = r0
        v0 = Vector(p0)
        v1 = Vector(p1)
        axis = v1 - v0
        if axis.length < 1e-5:
            return
        axis.normalize()
        up = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
        u = axis.cross(up).normalized()
        w = axis.cross(u).normalized()

        verts = []
        faces = []
        for i in range(segs):
            ang = 2.0 * math.pi * i / segs
            d = u * math.cos(ang) + w * math.sin(ang)
            verts.append(tuple(v0 + d * r0))
            verts.append(tuple(v1 + d * r1))
        for i in range(segs):
            ni = (i + 1) % segs
            faces.append((i * 2, ni * 2, ni * 2 + 1, i * 2 + 1))
        if cap:
            faces.append(tuple(range((segs - 1) * 2, -1, -2)))
            faces.append(tuple(range(1, segs * 2, 2)))
        self.add_submesh(verts, faces, mat_idx=mat_idx, mirror_x=mirror_x)

    def build(self, parent=None, pivot=None, visible=True, outward_normal=None):
        mesh = bpy.data.meshes.new(self.name + "_mesh")
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)

        bm = bmesh.new()
        bm_verts = [bm.verts.new(v) for v in self.verts]
        bm.verts.ensure_lookup_table()
        for f_idx, mat_idx in self.faces:
            try:
                face = bm.faces.new([bm_verts[i] for i in f_idx])
                face.material_index = mat_idx
            except ValueError:
                pass

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


def build_supercar_v10_01():
    clear_scene()

    root = bpy.data.objects.new("SupercarV10_01", None)
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
        # Paintable
        "Paint_Body":    make_mat("Paint_Body",    (0.78, 0.80, 0.84), metallic=0.35, roughness=0.22),
        "Paint_Rim":     make_mat("Paint_Rim",     (0.14, 0.15, 0.17), metallic=0.80, roughness=0.25),
        "Paint_Caliper": make_mat("Paint_Caliper", (0.92, 0.12, 0.08), metallic=0.35, roughness=0.24),
        "Paint_Accent":  make_mat("Paint_Accent",  (0.95, 0.18, 0.08), metallic=0.25, roughness=0.28),
        "Neon":          make_mat("Neon",          (0.00, 0.92, 1.00), roughness=0.10, emission=(0.00, 0.92, 1.00), emission_strength=5.0),
        "Glass_Tint":    make_mat("Glass_Tint",    (0.06, 0.10, 0.15), metallic=0.85, roughness=0.08, alpha=0.40, backface_culling=False),

        # Fixed
        "Trim_Dark":     make_mat("Trim_Dark",     (0.03, 0.03, 0.035), metallic=0.05, roughness=0.85),
        "Carbon":        make_mat("Carbon",        (0.07, 0.075, 0.085), metallic=0.35, roughness=0.38),
        "Metal_Chrome":  make_mat("Metal_Chrome",  (0.88, 0.90, 0.92), metallic=0.90, roughness=0.15),
        "Interior_Dark": make_mat("Interior_Dark", (0.07, 0.07, 0.075), metallic=0.0,  roughness=0.85),
        "Light_Head":    make_mat("Light_Head",    (0.96, 0.98, 1.00), roughness=0.10, emission=(0.94, 0.98, 1.00), emission_strength=5.0),
        "Light_Tail":    make_mat("Light_Tail",    (1.00, 0.05, 0.03), roughness=0.15, emission=(1.00, 0.05, 0.03), emission_strength=4.5),
        "Decal_Graphic": make_mat("Decal_Graphic", (0.95, 0.96, 0.98), metallic=0.05, roughness=0.30),
        "Screen":        make_mat("Screen",        (0.05, 0.38, 0.50), roughness=0.15, emission=(0.05, 0.42, 0.55), emission_strength=0.6),
    }

    # =========================================================================
    # 2. CHASSIS & CLOSED SOLID BODY PANELS (Cab-Forward Mid-Engine V10 Supercar)
    #    Wheelbase: 2.66 m (front y = +1.33, rear y = -1.33, midpoint = 0.00)
    #    Length: ~4.52 m, Body Width (no mirrors): ~1.98 m, Height: ~1.21 m
    # =========================================================================
    p_chassis = PartBuilder("chassis_underbody", [mats["Trim_Dark"], mats["Metal_Chrome"]])
    p_chassis.add_box((-0.80, -2.02, 0.12), (0.80, 2.02, 0.22), mat_idx=0)
    p_chassis.add_box((-0.62,  1.92, 0.22), (0.62, 2.06, 0.35), mat_idx=1)
    p_chassis.add_box((-0.64, -2.08, 0.22), (0.64, -1.94, 0.36), mat_idx=1)
    p_chassis.build(parent=root, pivot=(0.0, 0.0, 0.18), visible=True)

    for side in (-1, 1):
        s_name = "left" if side < 0 else "right"
        sx = float(side)

        def orient_quad(q):
            return q if side > 0 else [q[0], q[3], q[2], q[1]]

        # Front Fender (low cab-forward supercar wedge, wheel arch y=0.90..1.76 for +-35 deg steering clearance)
        p_ff = PartBuilder(f"fender_front_{s_name}", [mats["Paint_Body"], mats["Trim_Dark"]])
        p_ff.add_prism(
            orient_quad([(sx * 0.54, 1.72, 0.28), (sx * 0.90, 1.72, 0.28), (sx * 0.84, 2.08, 0.26), (sx * 0.50, 2.12, 0.26)]),
            orient_quad([(sx * 0.50, 1.72, 0.58), (sx * 0.88, 1.72, 0.56), (sx * 0.78, 2.08, 0.44), (sx * 0.46, 2.12, 0.44)]),
            mat_idx=0
        )
        p_ff.add_prism(
            orient_quad([(sx * 0.58, 0.90, 0.62), (sx * 0.97, 0.90, 0.62), (sx * 0.95, 1.72, 0.56), (sx * 0.52, 1.72, 0.58)]),
            orient_quad([(sx * 0.56, 0.90, 0.74), (sx * 0.90, 0.90, 0.72), (sx * 0.88, 1.72, 0.64), (sx * 0.50, 1.72, 0.66)]),
            mat_idx=0
        )
        p_ff.add_prism(
            orient_quad([(sx * 0.74, 0.72, 0.20), (sx * 0.94, 0.72, 0.20), (sx * 0.97, 0.92, 0.52), (sx * 0.74, 0.92, 0.52)]),
            orient_quad([(sx * 0.70, 0.72, 0.74), (sx * 0.90, 0.72, 0.72), (sx * 0.90, 0.92, 0.72), (sx * 0.70, 0.92, 0.74)]),
            mat_idx=0
        )
        # Inner wheel arch liner (kept at x=0.56 so turning front wheel at x=0.83+-0.14 has >8 cm clearance)
        p_ff.add_box((sx * 0.52, 0.92, 0.22), (sx * 0.60, 1.70, 0.62), mat_idx=1)
        p_ff.build(parent=root, pivot=(sx * 0.76, 1.40, 0.50), visible=True)

        # Door (sculpted diagonal channel leading into the rear sideblade; pivot at front hinge)
        p_door = PartBuilder(f"door_{s_name}", [mats["Paint_Body"], mats["Interior_Dark"]])
        p_door.add_prism(
            orient_quad([(sx * 0.74, -0.36, 0.20), (sx * 0.94, -0.36, 0.20), (sx * 0.94, 0.74, 0.20), (sx * 0.74, 0.74, 0.20)]),
            orient_quad([(sx * 0.74, -0.36, 0.50), (sx * 0.96, -0.36, 0.50), (sx * 0.95, 0.74, 0.48), (sx * 0.74, 0.74, 0.48)]),
            mat_idx=0
        )
        p_door.add_prism(
            orient_quad([(sx * 0.74, -0.36, 0.50), (sx * 0.96, -0.36, 0.50), (sx * 0.95, 0.74, 0.48), (sx * 0.74, 0.74, 0.48)]),
            orient_quad([(sx * 0.74, -0.36, 0.78), (sx * 0.90, -0.36, 0.78), (sx * 0.90, 0.74, 0.74), (sx * 0.74, 0.74, 0.74)]),
            mat_idx=0
        )
        p_door.add_box((sx * 0.70, -0.34, 0.24), (sx * 0.75, 0.70, 0.74), mat_idx=1)
        p_door.build(parent=root, pivot=(sx * 0.86, 0.72, 0.58), visible=True)

        # Rear Quarter Fender + Signature Vertical Carbon Sideblade (y = -0.88 .. -0.34)
        p_rf = PartBuilder(f"fender_rear_{s_name}", [mats["Paint_Body"], mats["Carbon"]])
        # Vertical contrasting Carbon Sideblade panel behind door (sloped to match C-pillar roofline)
        p_rf.add_prism(
            orient_quad([(sx * 0.76, -0.88, 0.22), (sx * 0.97, -0.88, 0.22), (sx * 0.95, -0.36, 0.22), (sx * 0.76, -0.36, 0.22)]),
            orient_quad([(sx * 0.52, -0.88, 0.94), (sx * 0.88, -0.88, 0.90), (sx * 0.84, -0.36, 1.08), (sx * 0.50, -0.36, 1.12)]),
            mat_idx=1
        )
        # Wide muscular rear haunch over rear wheel (y = -1.74 .. -0.88, body half-width = 0.99 m)
        p_rf.add_prism(
            orient_quad([(sx * 0.60, -1.74, 0.66), (sx * 0.99, -1.74, 0.66), (sx * 0.99, -0.88, 0.66), (sx * 0.60, -0.88, 0.66)]),
            orient_quad([(sx * 0.54, -1.74, 0.86), (sx * 0.93, -1.74, 0.84), (sx * 0.95, -0.88, 0.86), (sx * 0.56, -0.88, 0.88)]),
            mat_idx=0
        )
        # Rear quarter tail section behind rear wheel (y = -2.12 .. -1.74)
        p_rf.add_prism(
            orient_quad([(sx * 0.56, -2.12, 0.34), (sx * 0.88, -2.12, 0.34), (sx * 0.96, -1.74, 0.34), (sx * 0.56, -1.74, 0.34)]),
            orient_quad([(sx * 0.52, -2.12, 0.82), (sx * 0.84, -2.12, 0.80), (sx * 0.93, -1.74, 0.84), (sx * 0.54, -1.74, 0.86)]),
            mat_idx=0
        )
        # Inner wheelwell wall
        p_rf.add_box((sx * 0.56, -1.72, 0.24), (sx * 0.64, -0.90, 0.66), mat_idx=1)
        p_rf.build(parent=root, pivot=(sx * 0.80, -1.28, 0.58), visible=True)

    # Roof & Cab-Forward Canopy Pillars (height ~1.20 m)
    p_roof = PartBuilder("roof_cabin", [mats["Paint_Body"], mats["Interior_Dark"]])
    p_roof.add_prism(
        [(-0.52, -0.46, 1.13), (0.52, -0.46, 1.13), (0.48, 0.36, 1.16), (-0.48, 0.36, 1.16)],
        [(-0.48, -0.44, 1.19), (0.48, -0.44, 1.19), (0.44, 0.34, 1.21), (-0.44, 0.34, 1.21)],
        mat_idx=0
    )
    p_roof.add_box((-0.48, -0.44, 1.10), (0.48, 0.32, 1.13), mat_idx=1)
    # Steep cab-forward A-Pillars
    p_roof.add_prism(
        [(0.60, 0.84, 0.74), (0.70, 0.84, 0.74), (0.48, 0.32, 1.15), (0.40, 0.32, 1.15)],
        [(0.58, 0.88, 0.80), (0.68, 0.88, 0.80), (0.46, 0.36, 1.20), (0.38, 0.36, 1.20)],
        mat_idx=0, mirror_x=True
    )
    # Flying Buttress C-Pillars extending back to -1.56
    p_roof.add_prism(
        [(0.48, -1.56, 0.84), (0.62, -1.56, 0.84), (0.52, -0.44, 1.13), (0.42, -0.44, 1.13)],
        [(0.46, -1.52, 0.90), (0.58, -1.52, 0.90), (0.48, -0.42, 1.19), (0.38, -0.42, 1.19)],
        mat_idx=0, mirror_x=True
    )
    p_roof.build(parent=root, pivot=(0.0, -0.10, 1.04), visible=True)

    # Mid-Engine V10 Louvered Decklid + Twin V10 Intake Plenum Blocks
    p_deck = PartBuilder("engine_decklid", [mats["Carbon"], mats["Metal_Chrome"]])
    p_deck.add_prism(
        [(-0.48, -2.06, 0.78), (0.48, -2.06, 0.78), (0.48, -0.86, 0.86), (-0.48, -0.86, 0.86)],
        [(-0.46, -2.06, 0.84), (0.46, -2.06, 0.84), (0.46, -0.86, 0.92), (-0.46, -0.86, 0.92)],
        mat_idx=0
    )
    # V10 Twin-Bank Intake Manifolds visible in the central engine bay spine
    p_deck.add_box((0.06, -1.66, 0.84), (0.24, -1.04, 0.95), mat_idx=1, mirror_x=True)
    for slat_y in (-1.02, -1.20, -1.38, -1.56, -1.74, -1.90):
        p_deck.add_box((0.26, slat_y - 0.03, 0.84), (0.44, slat_y + 0.03, 0.93), mat_idx=0, mirror_x=True)
    p_deck.build(parent=root, pivot=(0.0, -1.46, 0.86), visible=True)

    # =========================================================================
    # 3. WINDOWS (Outward Normals, Glass_Tint _Cull=0)
    # =========================================================================
    p_ws = PartBuilder("glass_windshield", [mats["Glass_Tint"]])
    p_ws.add_submesh(
        [(0.00, 0.88, 0.75), (0.62, 0.86, 0.75), (0.00, 0.34, 1.19), (0.44, 0.32, 1.18)],
        [(0, 1, 3, 2)], mat_idx=0, mirror_x=True
    )
    p_ws.build(parent=root, pivot=(0.0, 0.60, 0.96), visible=True, outward_normal=(0.0, 1.0, 1.0))

    p_rw = PartBuilder("glass_rear", [mats["Glass_Tint"]])
    p_rw.add_submesh(
        [(0.00, -0.44, 1.17), (0.42, -0.44, 1.16), (0.00, -1.08, 0.92), (0.44, -1.08, 0.91)],
        [(0, 2, 3, 1)], mat_idx=0, mirror_x=True
    )
    p_rw.build(parent=root, pivot=(0.0, -0.76, 1.04), visible=True, outward_normal=(0.0, -1.0, 1.0))

    for side in (-1, 1):
        s_name = "left" if side < 0 else "right"
        sx = float(side)
        p_sw = PartBuilder(f"glass_side_{s_name}", [mats["Glass_Tint"]])
        swv = [
            (sx * 0.88,  0.76, 0.75), (sx * 0.88, -0.36, 0.78), (sx * 0.48, -0.36, 1.17), (sx * 0.46,  0.32, 1.18),
        ]
        swf = [(0, 1, 2, 3)] if side > 0 else [(0, 3, 2, 1)]
        p_sw.add_submesh(swv, swf, mat_idx=0)
        p_sw.build(parent=root, pivot=(sx * 0.68, 0.18, 0.96), visible=True, outward_normal=(sx, 0.0, 0.5))

    # =========================================================================
    # 4. ENCLOSED INTERIOR CABIN & COCKPIT (Uses Screen material for displays!)
    # =========================================================================
    p_cab = PartBuilder("interior_cabin", [mats["Interior_Dark"], mats["Trim_Dark"]])
    p_cab.add_box((-0.74, -0.68, 0.18), (0.74,  0.90, 0.24), mat_idx=0)  # floor
    p_cab.add_box((-0.74,  0.80, 0.22), (0.74,  0.92, 0.76), mat_idx=0)  # front firewall
    p_cab.add_box((-0.74, -0.76, 0.22), (0.74, -0.58, 0.94), mat_idx=0)  # rear mid-engine bulkhead
    p_cab.add_box((-0.74, -1.10, 0.82), (0.74, -0.58, 0.92), mat_idx=0)  # rear upper shelf
    p_cab.add_box((-0.14, -0.62, 0.24), (0.14,  0.82, 0.46), mat_idx=1)  # high carbon center tunnel
    p_cab.add_cylinder((0.0, 0.26, 0.44), (0.0, 0.28, 0.62), 0.018, 0.024, segs=8, mat_idx=1)
    p_cab.build(parent=root, pivot=(0.0, 0.10, 0.48), visible=True)

    p_dash = PartBuilder("interior_dash", [mats["Interior_Dark"], mats["Screen"]])
    p_dash.add_box((-0.72, 0.52, 0.52), (0.72, 0.88, 0.69), mat_idx=0)
    # Recessed driver monoposto gauge arch (top visor 0.77..0.79 m, open cavity 0.69..0.77 m)
    p_dash.add_box((-0.51, 0.50, 0.77), (-0.23, 0.76, 0.79), mat_idx=0)
    p_dash.add_box((-0.51, 0.50, 0.69), (-0.48, 0.74, 0.77), mat_idx=0)
    p_dash.add_box((-0.26, 0.50, 0.69), (-0.23, 0.74, 0.77), mat_idx=0)
    p_dash.add_box((-0.49, 0.55, 0.69), (-0.25, 0.76, 0.77), mat_idx=0)
    # Steering column sleeve entering lower dash below gauge screen
    p_dash.add_cylinder((-0.37, 0.36, 0.61), (-0.37, 0.54, 0.58), 0.032, 0.038, segs=10, mat_idx=0)
    # Compact digital cockpit display (width = 0.22 m = 15.3% <= 25% of dash width)
    p_dash.add_box((-0.48, 0.53, 0.695), (-0.26, 0.55, 0.765), mat_idx=1)
    p_dash.build(parent=root, pivot=(0.0, 0.68, 0.68), visible=True)

    p_dseat = PartBuilder("interior_driver_seat", [mats["Interior_Dark"], mats["Paint_Accent"]])
    p_dseat.add_box((-0.55, -0.34, 0.24), (-0.19,  0.10, 0.34), mat_idx=0)
    p_dseat.add_box((-0.57, -0.34, 0.32), (-0.51,  0.08, 0.44), mat_idx=0)
    p_dseat.add_box((-0.23, -0.34, 0.32), (-0.17,  0.08, 0.44), mat_idx=0)
    p_dseat.add_box((-0.53, -0.42, 0.34), (-0.21, -0.28, 0.86), mat_idx=0)
    p_dseat.add_box((-0.59, -0.38, 0.56), (-0.15, -0.26, 0.72), mat_idx=1)
    p_dseat.add_box((-0.49, -0.40, 0.84), (-0.25, -0.28, 0.98), mat_idx=1)
    p_dseat.build(parent=root, pivot=(-0.37, -0.18, 0.58), visible=True)

    # =========================================================================
    # 5. STEERING WHEEL SLOT (mod_steering_0_stock .. 2_drift)
    #    Pivot at hub center (-0.370, 0.330, 0.610) -> Unity (-0.370, 0.610, 0.330).
    #    Top of wheel reaches y = 0.757 m in Unity (3.3 cm below 0.790 m dash top line!).
    # =========================================================================
    sw_pivot = (-0.37, 0.33, 0.61)
    build_steering_variant("mod_steering_0_stock",   [mats["Interior_Dark"], mats["Metal_Chrome"]], sw_pivot, root_obj=root, visible=True,  r_rim=0.148, is_yoke=False, spoke_mat_idx=1)
    build_steering_variant("mod_steering_1_formula", [mats["Carbon"],        mats["Paint_Accent"]], sw_pivot, root_obj=root, visible=False, r_rim=0.145, is_yoke=True,  spoke_mat_idx=0)
    build_steering_variant("mod_steering_2_drift",   [mats["Interior_Dark"], mats["Paint_Rim"]],    sw_pivot, root_obj=root, visible=False, r_rim=0.145, is_yoke=False, spoke_mat_idx=1)

    # =========================================================================
    # 6. INTERIOR PASSENGER, PILLAR & ROLLCAGE SLOTS
    # =========================================================================
    pass_pivot = (0.37, -0.18, 0.54)
    ppass0 = PartBuilder("mod_interior_passenger_0_seat", [mats["Interior_Dark"], mats["Paint_Accent"]])
    ppass0.add_box((0.19, -0.34, 0.24), (0.55,  0.10, 0.34), mat_idx=0)
    ppass0.add_box((0.21, -0.42, 0.34), (0.53, -0.28, 0.86), mat_idx=0)
    ppass0.add_box((0.15, -0.38, 0.56), (0.59, -0.26, 0.72), mat_idx=1)
    ppass0.add_box((0.25, -0.40, 0.84), (0.49, -0.28, 0.98), mat_idx=1)
    ppass0.build(parent=root, pivot=pass_pivot, visible=True)

    ppass1 = PartBuilder("mod_interior_passenger_1_nos", [mats["Paint_Accent"], mats["Metal_Chrome"]])
    ppass1.add_box((0.19, -0.32, 0.24), (0.55, 0.12, 0.30), mat_idx=1)
    for bx in (0.29, 0.45):
        ppass1.add_cylinder((bx, -0.28, 0.36), (bx, 0.10, 0.46), 0.065, 0.065, segs=12, mat_idx=0)
        ppass1.add_cylinder((bx, 0.10, 0.46), (bx, 0.16, 0.48), 0.065, 0.022, segs=12, mat_idx=1)
    ppass1.build(parent=root, pivot=pass_pivot, visible=False)

    pil_pivot = (-0.54, 0.62, 0.92)
    ppil0 = PartBuilder("mod_interior_pillar_0_stock", [mats["Interior_Dark"]])
    ppil0.add_box((-0.60, 0.58, 0.82), (-0.54, 0.66, 0.86), mat_idx=0)
    ppil0.build(parent=root, pivot=pil_pivot, visible=True)

    ppil1 = PartBuilder("mod_interior_pillar_1_gauges", [mats["Interior_Dark"], mats["Screen"]])
    for gy, gz in [(0.70, 0.84), (0.62, 0.92), (0.54, 1.00)]:
        ppil1.add_cylinder((-0.55, gy, gz), (-0.50, gy - 0.04, gz - 0.01), 0.028, 0.028, segs=10, mat_idx=0)
        ppil1.add_cylinder((-0.50, gy - 0.04, gz - 0.01), (-0.495, gy - 0.045, gz - 0.01), 0.022, 0.022, segs=10, mat_idx=1)
    ppil1.build(parent=root, pivot=pil_pivot, visible=False)

    rc_pivot = (0.0, -0.24, 0.78)
    add_empty("mod_rollcage_0_none", rc_pivot, parent=root, forward_plus_z=True, visible=True)

    prc1 = PartBuilder("mod_rollcage_1_half", [mats["Paint_Accent"]])
    prc1.add_cylinder((0.52, -0.40, 0.28), (0.44, -0.40, 1.08), 0.024, 0.024, segs=8, mat_idx=0, mirror_x=True)
    prc1.add_cylinder((-0.44, -0.40, 1.08), (0.44, -0.40, 1.08), 0.024, 0.024, segs=8, mat_idx=0)
    prc1.add_cylinder((0.44, -0.40, 1.06), (-0.48, -0.40, 0.34), 0.020, 0.020, segs=8, mat_idx=0, mirror_x=True)
    prc1.build(parent=root, pivot=rc_pivot, visible=False)

    prc2 = PartBuilder("mod_rollcage_2_full", [mats["Paint_Accent"]])
    prc2.add_cylinder((0.52, -0.40, 0.28), (0.44, -0.40, 1.08), 0.024, 0.024, segs=8, mat_idx=0, mirror_x=True)
    prc2.add_cylinder((-0.44, -0.40, 1.08), (0.44, -0.40, 1.08), 0.024, 0.024, segs=8, mat_idx=0)
    prc2.add_cylinder((0.44, -0.40, 1.06), (-0.48, -0.40, 0.34), 0.020, 0.020, segs=8, mat_idx=0, mirror_x=True)
    prc2.add_cylinder((0.44, -0.40, 1.08), (0.42,  0.30, 1.12), 0.022, 0.022, segs=8, mat_idx=0, mirror_x=True)
    prc2.add_cylinder((0.42,  0.30, 1.12), (0.58,  0.80, 0.74), 0.022, 0.022, segs=8, mat_idx=0, mirror_x=True)
    prc2.add_cylinder((0.50, -0.36, 0.68), (0.68,  0.74, 0.32), 0.020, 0.020, segs=8, mat_idx=0, mirror_x=True)
    prc2.build(parent=root, pivot=rc_pivot, visible=False)

    # =========================================================================
    # 7. FRUNK BAY & HOOD VARIANTS (mod_hood_0_stock .. 4_carbon)
    # =========================================================================
    frunk_pivot = (0.0, 1.48, 0.40)
    pfr0 = PartBuilder("mod_frunk_0_stock", [mats["Trim_Dark"]])
    pfr0.add_box((-0.50, 0.96, 0.22), (0.50, 1.96, 0.50), mat_idx=0)
    pfr0.build(parent=root, pivot=frunk_pivot, visible=True)

    pfr1 = PartBuilder("mod_frunk_1_nos_bay", [mats["Trim_Dark"], mats["Paint_Accent"]])
    pfr1.add_box((-0.50, 0.96, 0.22), (0.50, 1.96, 0.30), mat_idx=0)
    pfr1.add_box((-0.46, 1.00, 0.30), (0.46, 1.40, 0.48), mat_idx=0)
    pfr1.add_box((-0.48, 1.26, 0.50), (0.48, 1.32, 0.54), mat_idx=1)
    pfr1.add_cylinder((0.18, 1.44, 0.38), (0.18, 1.84, 0.40), 0.06, 0.06, segs=10, mat_idx=1, mirror_x=True)
    pfr1.build(parent=root, pivot=frunk_pivot, visible=False)

    hood_pivot = (0.0, 0.92, 0.74)
    for idx_h, (h_name, h_style) in enumerate((
        ("mod_hood_0_stock",  "stock"),
        ("mod_hood_1_street", "street"),
        ("mod_hood_2_track",  "track"),
        ("mod_hood_3_vented", "vented"),
        ("mod_hood_4_carbon", "carbon"),
    )):
        h_main_mat = mats["Carbon"] if h_style == "carbon" else mats["Paint_Body"]
        h_sec_mat = mats["Metal_Chrome"] if h_style == "carbon" else mats["Carbon"]
        ph = PartBuilder(h_name, [h_main_mat, h_sec_mat])
        # Low cab-forward V-creased front hood
        ph.add_prism(
            [(-0.50, 1.52, 0.52), (0.50, 1.52, 0.52), (0.46, 2.12, 0.38), (-0.46, 2.12, 0.38)],
            [(-0.50, 1.52, 0.58), (0.50, 1.52, 0.58), (0.46, 2.12, 0.44), (-0.46, 2.12, 0.44)],
            mat_idx=0
        )
        ph.add_prism(
            [(-0.54, 0.88, 0.70), (0.54, 0.88, 0.70), (0.50, 1.52, 0.52), (-0.50, 1.52, 0.52)],
            [(-0.54, 0.88, 0.76), (0.54, 0.88, 0.76), (0.50, 1.52, 0.58), (-0.50, 1.52, 0.58)],
            mat_idx=0
        )
        # Sloped V-crease strakes following the hood contour
        ph.add_prism(
            [(0.24, 0.92, 0.72), (0.30, 0.92, 0.72), (0.22, 2.02, 0.43), (0.16, 2.02, 0.43)],
            [(0.24, 0.92, 0.765), (0.30, 0.92, 0.765), (0.22, 2.02, 0.465), (0.16, 2.02, 0.465)],
            mat_idx=0, mirror_x=True
        )

        if h_style in ("street", "track", "carbon"):
            ph.add_box((-0.32, 1.88, 0.42), (0.32, 2.02, 0.48), mat_idx=1)
        if h_style in ("track", "carbon"):
            # Deep center radiator extractor duct
            ph.add_box((-0.26, 1.38, 0.54), (0.26, 1.78, 0.62), mat_idx=1)
        if h_style == "vented":
            for vy in (1.24, 1.40, 1.56, 1.72):
                ph.add_box((0.12, vy, 0.56), (0.38, vy + 0.06, 0.68), mat_idx=1, mirror_x=True)
        if h_style == "carbon":
            ph.add_cylinder((0.34, 1.96, 0.44), (0.34, 1.96, 0.47), 0.03, 0.03, segs=8, mat_idx=1, mirror_x=True)

        ph.build(parent=root, pivot=hood_pivot, visible=(idx_h == 0))

    # =========================================================================
    # 8. FENDER ADD-ON VARIANTS (mod_fender_0_stock .. 2_widebody)
    # =========================================================================
    fend_pivot = (0.0, 0.0, 0.60)
    pf0 = PartBuilder("mod_fender_0_stock", [mats["Paint_Body"]])
    pf0.add_box((0.88, 0.94, 0.52), (0.94, 1.04, 0.58), mat_idx=0, mirror_x=True)
    pf0.build(parent=root, pivot=fend_pivot, visible=True)

    pf1 = PartBuilder("mod_fender_1_louvers", [mats["Carbon"]])
    for li in range(4):
        ly = 1.18 + li * 0.11
        pf1.add_box((0.64, ly, 0.64), (0.86, ly + 0.05, 0.72), mat_idx=0, mirror_x=True)
    for li in range(4):
        ly = -1.52 + li * 0.11
        pf1.add_box((0.66, ly, 0.82), (0.88, ly + 0.05, 0.89), mat_idx=0, mirror_x=True)
    pf1.build(parent=root, pivot=fend_pivot, visible=False)

    pf2 = PartBuilder("mod_fender_2_widebody", [mats["Paint_Body"], mats["Carbon"]])
    pf2.add_box((0.88,  0.90, 0.28), (1.01,  1.72, 0.70), mat_idx=0, mirror_x=True)
    pf2.add_box((0.90, -1.76, 0.30), (1.02, -0.88, 0.82), mat_idx=0, mirror_x=True)
    for li in range(4):
        ly = 1.18 + li * 0.11
        pf2.add_box((0.66, ly, 0.64), (0.90, ly + 0.05, 0.72), mat_idx=1, mirror_x=True)
    pf2.build(parent=root, pivot=fend_pivot, visible=False)

    # =========================================================================
    # 9. FRONT BUMPERS (mod_bumper_front_0_stock .. 4_timeattack)
    # =========================================================================
    fb_pivot = (0.0, 2.10, 0.30)
    for idx_b, (b_name, lip_ext, canards, rods) in enumerate((
        ("mod_bumper_front_0_stock",      0.02, 0, False),
        ("mod_bumper_front_1_street",     0.07, 0, False),
        ("mod_bumper_front_2_race",       0.12, 1, False),
        ("mod_bumper_front_3_track",      0.16, 2, False),
        ("mod_bumper_front_4_timeattack", 0.24, 2, True),
    )):
        pfb = PartBuilder(b_name, [mats["Paint_Body"], mats["Trim_Dark"]])
        # Wide supercar nose with hexagonal single-frame grille & side fang intakes
        pfb.add_prism(
            [(-0.86, 2.00, 0.18), (0.86, 2.00, 0.18), (0.76, 2.22, 0.18), (-0.76, 2.22, 0.18)],
            [(-0.80, 2.00, 0.44), (0.80, 2.00, 0.44), (0.70, 2.18, 0.42), (-0.70, 2.18, 0.42)],
            mat_idx=0
        )
        # Wide hexagonal single-frame center grille + side radiator intakes
        pfb.add_box((-0.42, 2.04, 0.19), (0.42, 2.23, 0.40), mat_idx=1)
        pfb.add_box(( 0.48, 2.02, 0.20), (0.76, 2.20, 0.36), mat_idx=1, mirror_x=True)
        # Lower front splitter
        pfb.add_box((-0.88 - lip_ext * 0.25, 1.96, 0.12), (0.88 + lip_ext * 0.25, 2.22 + lip_ext, 0.18), mat_idx=1)

        if canards >= 1:
            pfb.add_box((0.76, 2.02, 0.24), (0.95, 2.18, 0.28), mat_idx=1, mirror_x=True)
        if canards >= 2:
            pfb.add_box((0.74, 2.06, 0.32), (0.93, 2.20, 0.36), mat_idx=1, mirror_x=True)
        if rods:
            pfb.add_box((0.90, 1.96, 0.12), (0.96, 2.44, 0.38), mat_idx=1, mirror_x=True)
            pfb.add_cylinder((0.28, 2.20, 0.34), (0.28, 2.40, 0.16), 0.012, 0.012, segs=6, mat_idx=1, mirror_x=True)

        pfb.build(parent=root, pivot=fb_pivot, visible=(idx_b == 0))

    # =========================================================================
    # 10. REAR BUMPERS (mod_bumper_rear_0_stock .. 3_timeattack)
    # =========================================================================
    rb_pivot = (0.0, -2.12, 0.36)
    for idx_rb, (rb_name, n_fins, d_ext) in enumerate((
        ("mod_bumper_rear_0_stock",      2, 0.00),
        ("mod_bumper_rear_1_street",     4, 0.05),
        ("mod_bumper_rear_2_track",      6, 0.10),
        ("mod_bumper_rear_3_timeattack", 6, 0.20),
    )):
        prb = PartBuilder(rb_name, [mats["Paint_Body"], mats["Trim_Dark"]])
        prb.add_prism(
            [(-0.88, -2.22, 0.20), (0.88, -2.22, 0.20), (0.90, -2.02, 0.20), (-0.90, -2.02, 0.20)],
            [(-0.84, -2.20, 0.68), (0.84, -2.20, 0.68), (0.86, -2.02, 0.68), (-0.86, -2.02, 0.68)],
            mat_idx=0
        )
        # Rear mesh extractor vents + central GT diffuser tunnel
        prb.add_box((0.42, -2.23, 0.44), (0.80, -2.06, 0.64), mat_idx=1, mirror_x=True)
        prb.add_box((-0.72, -2.24 - d_ext, 0.14), (0.72, -2.00, 0.38), mat_idx=1)
        for fi in range(n_fins // 2):
            fx = 0.16 + fi * 0.20
            prb.add_box((fx - 0.015, -2.26 - d_ext, 0.12), (fx + 0.015, -2.02, 0.38), mat_idx=1, mirror_x=True)
        prb.build(parent=root, pivot=rb_pivot, visible=(idx_rb == 0))

    # =========================================================================
    # 11. SIDE SKIRTS (mod_skirts_0_stock_left/right .. 3_wide_left/right)
    # =========================================================================
    for side in (-1, 1):
        s_name = "left" if side < 0 else "right"
        sx = float(side)
        sk_pivot = (sx * 0.92, 0.0, 0.18)
        for idx_sk, (sk_base, w_out, fin_h) in enumerate((
            ("mod_skirts_0_stock",  0.95, 0.00),
            ("mod_skirts_1_street", 0.98, 0.08),
            ("mod_skirts_2_track",  1.01, 0.16),
            ("mod_skirts_3_wide",   1.04, 0.24),
        )):
            psk = PartBuilder(f"{sk_base}_{s_name}", [mats["Carbon"], mats["Paint_Body"]])
            psk.add_box((sx * 0.80, -0.86, 0.12), (sx * w_out, 0.86, 0.22), mat_idx=0)
            if fin_h > 0.0:
                psk.add_box((sx * (w_out - 0.03), -0.86, 0.12), (sx * w_out, -0.62, 0.22 + fin_h), mat_idx=1)
            psk.build(parent=root, pivot=sk_pivot, visible=(idx_sk == 0))

    # =========================================================================
    # 12. SPOILERS / REAR WINGS (mod_spoiler_0_stock .. 5_timeattack)
    # =========================================================================
    sp_pivot = (0.0, -2.04, 1.02)

    psp0 = PartBuilder("mod_spoiler_0_stock", [mats["Paint_Body"]])
    psp0.add_box((-0.72, -2.18, 0.80), (0.72, -2.04, 0.86), mat_idx=0)
    psp0.build(parent=root, pivot=sp_pivot, visible=True)

    psp1 = PartBuilder("mod_spoiler_1_ducktail", [mats["Paint_Body"], mats["Carbon"]])
    psp1.add_prism(
        [(-0.72, -2.20, 0.80), (0.72, -2.20, 0.80), (0.72, -1.96, 0.80), (-0.72, -1.96, 0.80)],
        [(-0.68, -2.26, 0.96), (0.68, -2.26, 0.96), (0.68, -2.02, 0.86), (-0.68, -2.02, 0.86)],
        mat_idx=0
    )
    psp1.add_box((-0.66, -2.27, 0.94), (0.66, -2.22, 0.98), mat_idx=1)
    psp1.build(parent=root, pivot=sp_pivot, visible=False)

    psp2 = PartBuilder("mod_spoiler_2_street", [mats["Paint_Body"], mats["Carbon"]])
    psp2.add_box((0.44, -2.14, 0.80), (0.48, -2.00, 0.98), mat_idx=1, mirror_x=True)
    psp2.add_box((-0.82, -2.22, 0.96), (0.82, -1.98, 1.01), mat_idx=0)
    psp2.add_box((0.80, -2.24, 0.90), (0.84, -1.96, 1.06), mat_idx=1, mirror_x=True)
    psp2.build(parent=root, pivot=sp_pivot, visible=False)

    psp3 = PartBuilder("mod_spoiler_3_gtwing", [mats["Carbon"], mats["Paint_Accent"]])
    psp3.add_box((0.34, -2.12, 0.80), (0.38, -1.96, 1.14), mat_idx=0, mirror_x=True)
    psp3.add_box((-0.88, -2.24, 1.12), (0.88, -1.96, 1.17), mat_idx=0)
    psp3.add_box((0.86, -2.28, 1.02), (0.90, -1.92, 1.24), mat_idx=0, mirror_x=True)
    psp3.add_box((0.89, -2.26, 1.16), (0.91, -1.94, 1.22), mat_idx=1, mirror_x=True)
    psp3.build(parent=root, pivot=sp_pivot, visible=False)

    # Signature Gooseneck / Swan-Neck GT Wing (mounted from top of wing)
    psp4 = PartBuilder("mod_spoiler_4_swanneck", [mats["Carbon"], mats["Paint_Accent"]])
    psp4.add_box((0.26, -2.06, 0.80), (0.30, -1.88, 1.26), mat_idx=0, mirror_x=True)
    psp4.add_box((0.26, -2.16, 1.20), (0.30, -1.88, 1.28), mat_idx=0, mirror_x=True)
    psp4.add_box((-0.94, -2.28, 1.18), (0.94, -1.96, 1.23), mat_idx=0)
    psp4.add_box((-0.92, -2.30, 1.23), (0.92, -2.14, 1.28), mat_idx=1)
    psp4.add_box((0.92, -2.34, 1.00), (0.96, -1.92, 1.32), mat_idx=0, mirror_x=True)
    psp4.build(parent=root, pivot=sp_pivot, visible=False)

    psp5 = PartBuilder("mod_spoiler_5_timeattack", [mats["Carbon"], mats["Paint_Accent"]])
    psp5.add_box((0.38, -2.30, 0.34), (0.42, -2.10, 1.34), mat_idx=0, mirror_x=True)
    psp5.add_box((-0.98, -2.38, 1.22), (0.98, -2.02, 1.27), mat_idx=0)
    psp5.add_box((-0.94, -2.42, 1.32), (0.94, -2.14, 1.37), mat_idx=1)
    psp5.add_box((0.96, -2.46, 0.94), (1.00, -1.98, 1.44), mat_idx=0, mirror_x=True)
    psp5.build(parent=root, pivot=sp_pivot, visible=False)

    # =========================================================================
    # 13. ROOF SCOOPS (mod_roof_scoop_0_none .. 3_sharkfin)
    # =========================================================================
    rs_pivot = (0.0, -0.06, 1.20)
    add_empty("mod_roof_scoop_0_none", rs_pivot, parent=root, forward_plus_z=True, visible=True)

    prs1 = PartBuilder("mod_roof_scoop_1_single", [mats["Carbon"], mats["Paint_Accent"]])
    prs1.add_box((-0.14, -0.34, 1.18), (0.14, 0.10, 1.28), mat_idx=0)
    prs1.add_box((-0.11,  0.06, 1.20), (0.11, 0.11, 1.26), mat_idx=1)
    prs1.build(parent=root, pivot=rs_pivot, visible=False)

    prs2 = PartBuilder("mod_roof_scoop_2_twin", [mats["Paint_Body"], mats["Carbon"]])
    prs2.add_box((0.06, -0.36, 1.18), (0.24, 0.12, 1.27), mat_idx=0, mirror_x=True)
    prs2.add_box((0.08,  0.08, 1.20), (0.22, 0.13, 1.25), mat_idx=1, mirror_x=True)
    prs2.build(parent=root, pivot=rs_pivot, visible=False)

    prs3 = PartBuilder("mod_roof_scoop_3_sharkfin", [mats["Carbon"], mats["Paint_Accent"]])
    prs3.add_box((-0.14, -0.34, 1.18), (0.14, 0.10, 1.28), mat_idx=0)
    prs3.add_box((-0.02, -1.78, 0.86), (0.02, -0.26, 1.28), mat_idx=0)
    prs3.add_box((-0.025, -1.78, 1.20), (0.025, -0.30, 1.28), mat_idx=1)
    prs3.build(parent=root, pivot=rs_pivot, visible=False)

    # =========================================================================
    # 14. HEADLIGHTS, TAILLIGHTS, EXHAUSTS, MIRRORS, NEON
    # =========================================================================
    hl_pivot = (0.0, 1.88, 0.50)
    for idx_hl, (hl_name, hl_emit_mat) in enumerate((
        ("mod_headlight_0_stock", mats["Light_Head"]),
        ("mod_headlight_1_amber", mats["Paint_Caliper"]),
        ("mod_headlight_2_halo",  mats["Neon"]),
        ("mod_headlight_3_rally", mats["Decal_Graphic"]),
    )):
        phl = PartBuilder(hl_name, [mats["Trim_Dark"], hl_emit_mat])
        # Sharp horizontal L-blade supercar headlight housing (front face at z=2.04, top at y=0.54 in Unity)
        phl.add_box((0.48, 1.76, 0.43), (0.80, 2.04, 0.54), mat_idx=0, mirror_x=True)
        # Dual L-blade LED lenses protruding 5 mm proud of housing (z=2.045 >= 2.04, top y=0.525 <= 0.54)
        phl.add_box((0.50, 1.80, 0.490), (0.78, 2.045, 0.525), mat_idx=1, mirror_x=True)
        phl.add_box((0.50, 1.90, 0.445), (0.78, 2.045, 0.475), mat_idx=1, mirror_x=True)
        phl.build(parent=root, pivot=hl_pivot, visible=(idx_hl == 0))

    tl_pivot = (0.0, -2.14, 0.72)
    for idx_tl, (tl_name, tl_mat, segmented) in enumerate((
        ("mod_taillight_0_stock", mats["Light_Tail"],    False),
        ("mod_taillight_1_clear", mats["Decal_Graphic"], False),
        ("mod_taillight_2_cyber", mats["Light_Tail"],    True),
    )):
        ptl = PartBuilder(tl_name, [mats["Trim_Dark"], tl_mat])
        # Wide rear taillight bezel (rear face at z=-2.21 in Unity)
        ptl.add_box((-0.84, -2.21, 0.67), (0.84, -2.10, 0.77), mat_idx=0)
        if not segmented:
            # Twin horizontal LED light bars protruding 5 mm behind bezel (z=-2.215)
            ptl.add_box((0.16, -2.215, 0.72), (0.82, -2.14, 0.755), mat_idx=1, mirror_x=True)
            ptl.add_box((0.36, -2.215, 0.68), (0.82, -2.14, 0.710), mat_idx=1, mirror_x=True)
        else:
            for bx in (0.16, 0.38, 0.60):
                ptl.add_box((bx, -2.215, 0.685), (bx + 0.16, -2.14, 0.755), mat_idx=1, mirror_x=True)
        ptl.build(parent=root, pivot=tl_pivot, visible=(idx_tl == 0))

    ex_pivot = (0.0, -2.22, 0.30)
    for idx_ex, (ex_name, tips, z_tilt) in enumerate((
        ("mod_exhaust_0_stock",   [(-0.46, 0.052), (0.46, 0.052)], 0.00),
        ("mod_exhaust_1_center",  [(-0.10, 0.056), (0.10, 0.056)], 0.08),
        ("mod_exhaust_2_quad",    [(-0.54, 0.046), (-0.42, 0.046), (0.42, 0.046), (0.54, 0.046)], 0.00),
        ("mod_exhaust_3_upswept", [(-0.10, 0.060), (0.10, 0.060)], 0.16),
    )):
        pex = PartBuilder(ex_name, [mats["Metal_Chrome"], mats["Trim_Dark"]])
        for tx, tr in tips:
            pex.add_cylinder((tx, -2.02, 0.30), (tx, -2.30, 0.30 + z_tilt), tr, tr, segs=12, mat_idx=0)
            pex.add_cylinder((tx, -2.28, 0.30 + z_tilt * 0.9), (tx, -2.305, 0.30 + z_tilt), tr * 0.76, tr * 0.76, segs=12, mat_idx=1)
        pex.build(parent=root, pivot=ex_pivot, visible=(idx_ex == 0))

    mir_pivot = (0.0, 0.66, 0.78)
    for idx_m, (m_name, cap_mat, h_scale) in enumerate((
        ("mod_mirror_0_stock",  mats["Paint_Body"],   1.00),
        ("mod_mirror_1_carbon", mats["Carbon"],       0.82),
        ("mod_mirror_2_aero",   mats["Paint_Accent"], 0.65),
    )):
        pm = PartBuilder(m_name, [mats["Trim_Dark"], cap_mat])
        pm.add_box((0.86, 0.62, 0.73), (0.96, 0.70, 0.79), mat_idx=0, mirror_x=True)
        pm.add_box((0.92, 0.60, 0.77), (1.07, 0.72, 0.77 + 0.08 * h_scale), mat_idx=1, mirror_x=True)
        pm.build(parent=root, pivot=mir_pivot, visible=(idx_m == 0))

    neon_pivot = (0.0, 0.0, 0.10)
    add_empty("mod_neon_0_none", neon_pivot, parent=root, forward_plus_z=True, visible=True)

    pn1 = PartBuilder("mod_neon_1_sides", [mats["Neon"]])
    pn1.add_box((0.84, -0.78, 0.09), (0.88, 0.78, 0.11), mat_idx=0, mirror_x=True)
    pn1.build(parent=root, pivot=neon_pivot, visible=False)

    pn2 = PartBuilder("mod_neon_2_full", [mats["Neon"]])
    pn2.add_box((0.84, -0.78, 0.09), (0.88, 0.78, 0.11), mat_idx=0, mirror_x=True)
    pn2.add_box((-0.58,  2.02, 0.09), (0.58,  2.08, 0.11), mat_idx=0)
    pn2.add_box((-0.58, -2.14, 0.09), (0.58, -2.08, 0.11), mat_idx=0)
    pn2.build(parent=root, pivot=neon_pivot, visible=False)

    # =========================================================================
    # 15. DECALS / STICKERS (decal_1 .. decal_5, All Hidden by Default, Outward Quads)
    # =========================================================================
    pdec1 = PartBuilder("decal_1_center_stripes", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec1.add_submesh(
        [(0.03, 0.90, 0.584), (0.11, 0.90, 0.584), (0.11, 2.08, 0.444), (0.03, 2.08, 0.444)],
        [(0, 1, 2, 3)], mat_idx=0, mirror_x=True
    )
    pdec1.add_submesh(
        [(0.03, -0.42, 1.194), (0.11, -0.42, 1.194), (0.11, 0.32, 1.214), (0.03, 0.32, 1.214)],
        [(0, 1, 2, 3)], mat_idx=0, mirror_x=True
    )
    pdec1.add_submesh(
        [(0.115, 0.90, 0.584), (0.135, 0.90, 0.584), (0.135, 2.08, 0.444), (0.115, 2.08, 0.444)],
        [(0, 1, 2, 3)], mat_idx=1, mirror_x=True
    )
    pdec1.build(parent=root, pivot=(0.0, 0.70, 0.86), visible=False, outward_normal=(0.0, 0.2, 1.0))

    pdec2 = PartBuilder("decal_2_side_rocker", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec2.add_submesh(
        [(0.955, -0.32, 0.24), (0.955, 0.70, 0.24), (0.955, 0.70, 0.31), (0.955, -0.32, 0.31)],
        [(0, 1, 2, 3)], mat_idx=0, mirror_x=True
    )
    pdec2.add_submesh(
        [(0.955, -0.32, 0.315), (0.955, 0.70, 0.315), (0.955, 0.70, 0.34), (0.955, -0.32, 0.34)],
        [(0, 1, 2, 3)], mat_idx=1, mirror_x=True
    )
    pdec2.build(parent=root, pivot=(0.0, 0.18, 0.28), visible=False, outward_normal="radial_x")

    pdec3 = PartBuilder("decal_3_number_plates", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec3.add_submesh(
        [(0.965, -0.02, 0.38), (0.965, 0.44, 0.38), (0.965, 0.44, 0.68), (0.965, -0.02, 0.68)],
        [(0, 1, 2, 3)], mat_idx=0, mirror_x=True
    )
    pdec3.add_submesh(
        [(0.969, 0.08, 0.54), (0.969, 0.34, 0.54), (0.969, 0.34, 0.60), (0.969, 0.08, 0.60)],
        [(0, 1, 2, 3)], mat_idx=1, mirror_x=True
    )
    pdec3.build(parent=root, pivot=(0.0, 0.20, 0.53), visible=False, outward_normal="radial_x")

    pdec4 = PartBuilder("decal_4_side_slashes", [mats["Paint_Accent"], mats["Decal_Graphic"]])
    pdec4.add_submesh(
        [(0.995, -1.56, 0.54), (0.995, -0.96, 0.54), (0.995, -0.96, 0.62), (0.995, -1.56, 0.62)],
        [(0, 1, 2, 3)], mat_idx=0, mirror_x=True
    )
    pdec4.add_submesh(
        [(0.995, -1.66, 0.42), (0.995, -1.06, 0.42), (0.995, -1.06, 0.50), (0.995, -1.66, 0.50)],
        [(0, 1, 2, 3)], mat_idx=1, mirror_x=True
    )
    pdec4.build(parent=root, pivot=(0.0, -1.28, 0.52), visible=False, outward_normal="radial_x")

    pdec5 = PartBuilder("decal_5_windshield_banner", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec5.add_submesh(
        [(0.00, 0.46, 1.12), (0.46, 0.44, 1.11), (0.00, 0.34, 1.195), (0.44, 0.32, 1.185)],
        [(0, 1, 3, 2)], mat_idx=0, mirror_x=True
    )
    pdec5.build(parent=root, pivot=(0.0, 0.40, 1.15), visible=False, outward_normal=(0.0, 1.0, 1.0))

    # =========================================================================
    # 16. WHEELS, CALIPERS (Under Root!), 4 RIM VARIANTS & TIRE LETTERING
    # =========================================================================
    wheel_specs = [
        ("wheel-front-left",  -0.83,  1.33, 0.34, 0.34, 0.28, -1),
        ("wheel-front-right",  0.83,  1.33, 0.34, 0.34, 0.28,  1),
        ("wheel-back-left",   -0.84, -1.33, 0.36, 0.36, 0.32, -1),
        ("wheel-back-right",   0.84, -1.33, 0.36, 0.36, 0.32,  1),
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
            cy = wy + math.cos(ang) * r_tire
            cz = wz + math.sin(ang) * r_tire
            cy_r = wy + math.cos(ang) * r_rim
            cz_r = wz + math.sin(ang) * r_rim
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
                    (b0 + 2, b1 + 2, b1, b0),
                    (b0 + 2, b0 + 3, b1 + 3, b1 + 2),
                ])
            else:
                tf_tire.extend([
                    (b0 + 1, b1 + 1, b1, b0),
                    (b0 + 3, b1 + 3, b1 + 1, b0 + 1),
                    (b0, b1, b1 + 2, b0 + 2),
                    (b1 + 2, b1 + 3, b0 + 3, b0 + 2),
                ])
        pw.add_submesh(tv, tf_tire, mat_idx=0)
        pw.add_cylinder((wx - sx * 0.02, wy, wz), (wx + sx * 0.02, wy, wz), r_disc, r_disc, segs=14, mat_idx=1)
        w_obj = pw.build(parent=root, pivot=w_center, visible=True)

        # Caliper: child of ROOT (not wheel!), pivot at wheel center
        cal_name = w_name.replace("wheel-", "caliper-")
        pcal = PartBuilder(cal_name, [mats["Paint_Caliper"]])
        pcal.add_box(
            (wx - sx * 0.01, wy - r_disc * 0.85, wz - 0.08),
            (wx + sx * 0.04, wy - r_disc * 0.35, wz + 0.10),
            mat_idx=0
        )
        pcal.build(parent=root, pivot=w_center, visible=True)

        # Rim 0: Sport 10-Spoke (Stock, Visible)
        pr0 = PartBuilder(f"{w_name}_rim_0_sport", [mats["Paint_Rim"], mats["Metal_Chrome"]])
        for i in range(0, n_seg, 2):
            a0 = 2.0 * math.pi * i / n_seg
            a1 = 2.0 * math.pi * (i + 0.65) / n_seg
            v_sp = [
                (wx + sx * half_w * 0.84, wy + math.cos(a0) * r_rim, wz + math.sin(a0) * r_rim),
                (wx + sx * half_w * 0.84, wy + math.cos(a1) * r_rim, wz + math.sin(a1) * r_rim),
                (wx + sx * half_w * 0.46, wy + math.cos(a1) * r_hub, wz + math.sin(a1) * r_hub),
                (wx + sx * half_w * 0.46, wy + math.cos(a0) * r_hub, wz + math.sin(a0) * r_hub),
            ]
            pr0.add_submesh(v_sp, [(0, 1, 2, 3) if side > 0 else (3, 2, 1, 0)], mat_idx=0)
        pr0.add_cylinder((wx + sx * half_w * 0.40, wy, wz), (wx + sx * half_w * 0.64, wy, wz), r_hub, r_hub * 0.7, segs=10, mat_idx=1)
        pr0.build(parent=w_obj, pivot=w_center, visible=True, outward_normal=(sx, 0.0, 0.0))

        # Rim 1: 5-Spoke Twin-Blade
        pr1 = PartBuilder(f"{w_name}_rim_1_5spoke", [mats["Paint_Rim"], mats["Metal_Chrome"]])
        for i in range(5):
            ang = 2.0 * math.pi * i / 5.0
            a0, a1 = ang - 0.16, ang + 0.16
            v_sp = [
                (wx + sx * half_w * 0.76, wy + math.cos(a0) * r_rim, wz + math.sin(a0) * r_rim),
                (wx + sx * half_w * 0.76, wy + math.cos(a1) * r_rim, wz + math.sin(a1) * r_rim),
                (wx + sx * half_w * 0.32, wy + math.cos(a1) * r_hub, wz + math.sin(a1) * r_hub),
                (wx + sx * half_w * 0.32, wy + math.cos(a0) * r_hub, wz + math.sin(a0) * r_hub),
            ]
            pr1.add_submesh(v_sp, [(0, 1, 2, 3) if side > 0 else (3, 2, 1, 0)], mat_idx=0)
        pr1.add_cylinder((wx + sx * half_w * 0.28, wy, wz), (wx + sx * half_w * 0.54, wy, wz), r_hub, r_hub * 0.75, segs=10, mat_idx=1)
        pr1.build(parent=w_obj, pivot=w_center, visible=False, outward_normal=(sx, 0.0, 0.0))

        # Rim 2: Aero Turbofan Disc
        pr2 = PartBuilder(f"{w_name}_rim_2_turbofan", [mats["Carbon"], mats["Decal_Graphic"]])
        pr2.add_cylinder((wx + sx * half_w * 0.78, wy, wz), (wx + sx * half_w * 0.92, wy, wz), r_rim * 0.96, r_hub * 0.9, segs=18, mat_idx=0)
        for i in range(0, 18, 2):
            a0 = 2.0 * math.pi * i / 18.0
            a1 = 2.0 * math.pi * (i + 0.5) / 18.0
            v_fn = [
                (wx + sx * half_w * 0.93, wy + math.cos(a0) * r_rim * 0.93, wz + math.sin(a0) * r_rim * 0.93),
                (wx + sx * half_w * 0.93, wy + math.cos(a1) * r_rim * 0.93, wz + math.sin(a1) * r_rim * 0.93),
                (wx + sx * half_w * 0.93, wy + math.cos(a1) * r_rim * 0.65, wz + math.sin(a1) * r_rim * 0.65),
                (wx + sx * half_w * 0.93, wy + math.cos(a0) * r_rim * 0.65, wz + math.sin(a0) * r_rim * 0.65),
            ]
            pr2.add_submesh(v_fn, [(0, 1, 2, 3) if side > 0 else (3, 2, 1, 0)], mat_idx=1)
        pr2.build(parent=w_obj, pivot=w_center, visible=False, outward_normal=(sx, 0.0, 0.0))

        # Rim 3: Multi-Spoke Y-Mesh
        pr3 = PartBuilder(f"{w_name}_rim_3_mesh", [mats["Paint_Rim"], mats["Metal_Chrome"]])
        for i in range(8):
            a_base = 2.0 * math.pi * i / 8.0
            for branch in (-1, 1):
                a_tip = a_base + branch * 0.24
                v_y = [
                    (wx + sx * half_w * 0.84, wy + math.cos(a_tip - 0.04) * r_rim, wz + math.sin(a_tip - 0.04) * r_rim),
                    (wx + sx * half_w * 0.84, wy + math.cos(a_tip + 0.04) * r_rim, wz + math.sin(a_tip + 0.04) * r_rim),
                    (wx + sx * half_w * 0.50, wy + math.cos(a_base + 0.04) * r_hub, wz + math.sin(a_base + 0.04) * r_hub),
                    (wx + sx * half_w * 0.50, wy + math.cos(a_base - 0.04) * r_hub, wz + math.sin(a_base - 0.04) * r_hub),
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
                    (wx + sx * (half_w + 0.003), wy + math.cos(a0) * r_out_l, wz + math.sin(a0) * r_out_l),
                    (wx + sx * (half_w + 0.003), wy + math.cos(a1) * r_out_l, wz + math.sin(a1) * r_out_l),
                    (wx + sx * (half_w + 0.003), wy + math.cos(a1) * r_in_l,  wz + math.sin(a1) * r_in_l),
                    (wx + sx * (half_w + 0.003), wy + math.cos(a0) * r_in_l,  wz + math.sin(a0) * r_in_l),
                ]
                ptl_let.add_submesh(vl, [(0, 1, 2, 3) if side > 0 else (3, 2, 1, 0)], mat_idx=0)
        ptl_let.build(parent=w_obj, pivot=w_center, visible=False, outward_normal=(sx, 0.0, 0.0))

    # =========================================================================
    # 17. EMPTIES: LIGHTS, COCKPIT CAMERA & EXHAUST TIPS
    # =========================================================================
    add_empty("light_head_L",   (-0.64,  2.08, 0.50), parent=root, forward_plus_z=True)
    add_empty("light_head_R",   ( 0.64,  2.08, 0.50), parent=root, forward_plus_z=True)
    add_empty("light_tail_L",   (-0.62, -2.25, 0.72), parent=root, forward_plus_z=False)
    add_empty("light_tail_R",   ( 0.62, -2.25, 0.72), parent=root, forward_plus_z=False)
    add_empty("camera_cockpit", (-0.37, -0.10, 0.87), parent=root, forward_plus_z=True)
    add_empty("exhaust_L",      (-0.46, -2.32, 0.30), parent=root, forward_plus_z=False)
    add_empty("exhaust_R",      ( 0.46, -2.32, 0.30), parent=root, forward_plus_z=False)

    bpy.context.view_layer.update()

    # Gizlemeyi yalniz gorunurlukle (hide_set) yap; hide_render ve hide_viewport KULLANMA!
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


def export_supercar_v10_01_fbx():
    root = bpy.data.objects.get("SupercarV10_01")
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

    out_dir = "/home/zoro/harvester/Harvester/Assets/Cars/SupercarV10_01"
    os.makedirs(out_dir, exist_ok=True)
    fbx_path = os.path.join(out_dir, "SupercarV10_01.fbx")
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
        mesh_smooth_type='OFF'
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


if __name__ == "__main__":
    build_supercar_v10_01()
    export_supercar_v10_01_fbx()
