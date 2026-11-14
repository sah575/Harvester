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
    Convert authored right-handed car coordinates (x=Right, y=Forward, z=Up)
    into Blender world coordinates (-x, -y, z) so that exporting with
    axis_forward='-Z', axis_up='Y', bake_space_transform=True produces in Unity:
      +X = Right, +Y = Up, +Z = Forward, root rotation = (0,0,0), scale = (1,1,1).
    Notice (-x, -y, z) is a pure 180-deg rotation around Z (det = +1), preserving face winding.
    """
    return (-float(v[0]), -float(v[1]), float(v[2]))


class PartBuilder:
    def __init__(self, name, materials):
        self.name = name
        self.materials = materials if isinstance(materials, list) else [materials]
        assert len(self.materials) <= 2, f"{name} has {len(self.materials)} materials (max allowed is 2)"
        self.verts = []
        self.faces = []

    def add_submesh(self, verts, faces, mat_idx=0, mirror_x=False, double_sided=False):
        base = len(self.verts)
        self.verts.extend([u2b(v) for v in verts])
        for f in faces:
            self.faces.append((tuple(base + i for i in f), mat_idx))
            if double_sided:
                self.faces.append((tuple(base + i for i in reversed(f)), mat_idx))
        if mirror_x:
            sbase = len(self.verts)
            self.verts.extend([u2b((-v[0], v[1], v[2])) for v in verts])
            for f in faces:
                self.faces.append((tuple(sbase + i for i in reversed(f)), mat_idx))
                if double_sided:
                    self.faces.append((tuple(sbase + i for i in f), mat_idx))

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
        """
        Adds a closed 6-face hexahedron (solid slab/wedge) from 4 bottom points and 4 top points
        ordered counter-clockwise when viewed from above (+Z).
        Ensures every body panel is a closed watertight volume for destruction.
        """
        v = list(quad_bottom) + list(quad_top)
        f = [
            (0, 3, 2, 1),  # bottom
            (4, 5, 6, 7),  # top
            (0, 1, 5, 4),  # side 0-1
            (1, 2, 6, 5),  # side 1-2
            (2, 3, 7, 6),  # side 2-3
            (3, 0, 4, 7),  # side 3-0
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

        if outward_normal is not None:
            bm.normal_update()
            target_n = Vector(u2b(outward_normal)).normalized()
            for face in bm.faces:
                if face.material_index == 0 and face.normal.dot(target_n) < 0.0:
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

        # Shift mesh vertices so local origin is at p_bl (Apply Transform equivalent)
        mesh.transform(Matrix.Translation(-p_bl))

        if parent is not None:
            # Parent is a top-level object (e.g. wheel-front-left) at parent.location.
            # Set parent with Object (Keep Transform) so local pos is p_bl - parent.location (= 0,0,0 for rims!)
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



def build_racer_coupe_01():
    clear_scene()

    root = bpy.data.objects.new("RacerCoupe_01", None)
    root.empty_display_type = 'PLAIN_AXES'
    root.empty_display_size = 0.5
    bpy.context.collection.objects.link(root)
    root.location = Vector((0.0, 0.0, 0.0))
    root.rotation_euler = Euler((0.0, 0.0, 0.0), 'XYZ')
    root.scale = Vector((1.0, 1.0, 1.0))

    mats = {
        "Paint_Body":    make_mat("Paint_Body",    (0.12, 0.56, 0.92), metallic=0.20, roughness=0.25),
        "Paint_Rim":     make_mat("Paint_Rim",     (0.86, 0.72, 0.26), metallic=0.85, roughness=0.22),
        "Paint_Caliper": make_mat("Paint_Caliper", (0.96, 0.78, 0.04), metallic=0.35, roughness=0.25),
        "Paint_Accent":  make_mat("Paint_Accent",  (0.00, 0.90, 0.82), metallic=0.20, roughness=0.28),
        "Neon":          make_mat("Neon",          (0.00, 0.95, 1.00), roughness=0.10, emission=(0.00, 0.95, 1.00), emission_strength=5.0),
        "Glass_Tint":    make_mat("Glass_Tint",    (0.07, 0.12, 0.18), metallic=0.85, roughness=0.08, alpha=0.38, backface_culling=False),

        "Trim_Dark":     make_mat("Trim_Dark",     (0.03, 0.03, 0.035), metallic=0.05, roughness=0.85),
        "Carbon":        make_mat("Carbon",        (0.075, 0.08, 0.09), metallic=0.35, roughness=0.40),
        "Metal_Chrome":  make_mat("Metal_Chrome",  (0.88, 0.90, 0.92), metallic=0.90, roughness=0.15),
        "Interior_Dark": make_mat("Interior_Dark", (0.07, 0.07, 0.075), metallic=0.0,  roughness=0.85),
        "Light_Head":    make_mat("Light_Head",    (0.96, 0.98, 1.00), roughness=0.10, emission=(0.92, 0.97, 1.00), emission_strength=5.0),
        "Light_Tail":    make_mat("Light_Tail",    (1.00, 0.06, 0.04), roughness=0.15, emission=(1.00, 0.05, 0.03), emission_strength=4.5),
        "Decal_Graphic": make_mat("Decal_Graphic", (0.95, 0.96, 0.98), metallic=0.05, roughness=0.30),
        "Screen":        make_mat("Screen",        (0.05, 0.38, 0.50), roughness=0.15, emission=(0.05, 0.42, 0.55), emission_strength=0.6),
    }

    # =========================================================================
    # 2. CHASSIS & CLOSED SOLID BODY PANELS (For Shatter Destruction)
    #    Dimensions: Length ~4.42 m (y from -2.18 to +2.24), Width ~1.98 m (x from -0.99 to +0.99)
    # =========================================================================
    p_chassis = PartBuilder("chassis_underbody", [mats["Trim_Dark"], mats["Metal_Chrome"]])
    p_chassis.add_box((-0.78, -1.96, 0.12), (0.78, 1.96, 0.22), mat_idx=0)
    # Front and rear crash beams
    p_chassis.add_box((-0.62,  1.88, 0.22), (0.62,  2.00, 0.36), mat_idx=1)
    p_chassis.add_box((-0.62, -2.02, 0.22), (0.62, -1.90, 0.38), mat_idx=1)
    p_chassis.build(pivot=(0.0, 0.0, 0.18), visible=True)

    # Front & Rear Fenders + Doors (Left & Right separate closed 3D meshes!)
    for side in (-1, 1):
        s_name = "left" if side < 0 else "right"
        sx = float(side)

        def orient_quad(q):
            return q if side > 0 else [q[0], q[3], q[2], q[1]]

        # Front Fender (closed 3D shell with wheel arch & sharp angular crest)
        p_ff = PartBuilder(f"fender_front_{s_name}", [mats["Paint_Body"], mats["Trim_Dark"]])
        # Front nose section ahead of wheel arch
        p_ff.add_prism(
            orient_quad([(sx * 0.56, 1.62, 0.34), (sx * 0.88, 1.62, 0.34), (sx * 0.82, 1.98, 0.32), (sx * 0.52, 2.02, 0.32)]),
            orient_quad([(sx * 0.52, 1.62, 0.64), (sx * 0.84, 1.62, 0.62), (sx * 0.76, 1.98, 0.48), (sx * 0.48, 2.02, 0.48)]),
            mat_idx=0
        )
        # Upper arch bridge over front wheel
        p_ff.add_prism(
            orient_quad([(sx * 0.58, 0.88, 0.66), (sx * 0.92, 0.88, 0.66), (sx * 0.90, 1.62, 0.62), (sx * 0.54, 1.62, 0.64)]),
            orient_quad([(sx * 0.56, 0.88, 0.78), (sx * 0.86, 0.88, 0.76), (sx * 0.84, 1.62, 0.68), (sx * 0.52, 1.62, 0.70)]),
            mat_idx=0
        )
        # Rearward fender pillar behind front wheel (A-pillar base)
        p_ff.add_prism(
            orient_quad([(sx * 0.72, 0.72, 0.22), (sx * 0.90, 0.72, 0.22), (sx * 0.92, 0.92, 0.54), (sx * 0.72, 0.92, 0.54)]),
            orient_quad([(sx * 0.68, 0.72, 0.78), (sx * 0.86, 0.72, 0.76), (sx * 0.86, 0.92, 0.76), (sx * 0.68, 0.92, 0.78)]),
            mat_idx=0
        )
        # Inner wheelwell liner (solid dark trim)
        p_ff.add_box((sx * 0.54, 0.90, 0.24), (sx * 0.64, 1.60, 0.66), mat_idx=1)
        p_ff.build(pivot=(sx * 0.74, 1.35, 0.54), visible=True)

        # Door (closed 3D panel with outer sculpted blade & inner cabin door card; pivot at front hinge)
        p_door = PartBuilder(f"door_{s_name}", [mats["Paint_Body"], mats["Interior_Dark"]])
        # Lower door slab
        p_door.add_prism(
            orient_quad([(sx * 0.74, -0.44, 0.22), (sx * 0.91, -0.44, 0.22), (sx * 0.90, 0.72, 0.22), (sx * 0.74, 0.72, 0.22)]),
            orient_quad([(sx * 0.74, -0.44, 0.54), (sx * 0.94, -0.44, 0.54), (sx * 0.91, 0.72, 0.52), (sx * 0.74, 0.72, 0.52)]),
            mat_idx=0
        )
        # Upper door beltline slab
        p_door.add_prism(
            orient_quad([(sx * 0.74, -0.44, 0.54), (sx * 0.94, -0.44, 0.54), (sx * 0.91, 0.72, 0.52), (sx * 0.74, 0.72, 0.52)]),
            orient_quad([(sx * 0.74, -0.44, 0.82), (sx * 0.88, -0.44, 0.82), (sx * 0.86, 0.72, 0.78), (sx * 0.74, 0.72, 0.78)]),
            mat_idx=0
        )
        # Inner door trim panel + door handle pocket
        p_door.add_box((sx * 0.70, -0.42, 0.26), (sx * 0.75, 0.68, 0.78), mat_idx=1)
        p_door.add_box((sx * 0.87, -0.32, 0.68), (sx * 0.92, -0.14, 0.73), mat_idx=1)
        p_door.build(pivot=(sx * 0.84, 0.70, 0.65), visible=True)

        # Rear Quarter Fender (closed 3D wide haunch with side air intake channel)
        p_rf = PartBuilder(f"fender_rear_{s_name}", [mats["Paint_Body"], mats["Trim_Dark"]])
        # Forward intake scoop pillar (between door and rear wheel)
        p_rf.add_prism(
            orient_quad([(sx * 0.74, -0.88, 0.22), (sx * 0.96, -0.88, 0.22), (sx * 0.92, -0.44, 0.22), (sx * 0.74, -0.44, 0.22)]),
            orient_quad([(sx * 0.70, -0.88, 0.86), (sx * 0.95, -0.88, 0.84), (sx * 0.88, -0.44, 0.82), (sx * 0.70, -0.44, 0.82)]),
            mat_idx=0
        )
        # Upper arch bridge over rear wheel
        p_rf.add_prism(
            orient_quad([(sx * 0.60, -1.64, 0.68), (sx * 0.96, -1.64, 0.68), (sx * 0.98, -0.88, 0.68), (sx * 0.60, -0.88, 0.68)]),
            orient_quad([(sx * 0.56, -1.64, 0.88), (sx * 0.90, -1.64, 0.86), (sx * 0.95, -0.88, 0.84), (sx * 0.60, -0.88, 0.86)]),
            mat_idx=0
        )
        # Rearward quarter tail section behind rear wheel
        p_rf.add_prism(
            orient_quad([(sx * 0.56, -2.04, 0.36), (sx * 0.84, -2.04, 0.36), (sx * 0.94, -1.64, 0.36), (sx * 0.56, -1.64, 0.36)]),
            orient_quad([(sx * 0.52, -2.04, 0.82), (sx * 0.80, -2.04, 0.80), (sx * 0.90, -1.64, 0.86), (sx * 0.56, -1.64, 0.88)]),
            mat_idx=0
        )
        # Side intake cavity & rear wheelwell liner (dark trim)
        p_rf.add_box((sx * 0.82, -0.84, 0.44), (sx * 0.96, -0.46, 0.76), mat_idx=1)
        p_rf.add_box((sx * 0.56, -1.62, 0.24), (sx * 0.66, -0.90, 0.68), mat_idx=1)
        p_rf.build(pivot=(sx * 0.78, -1.24, 0.58), visible=True)

    # Roof & Pillars (closed 3D cabin crown with outer paint + inner headliner so cockpit has zero holes!)
    p_roof = PartBuilder("roof_cabin", [mats["Paint_Body"], mats["Interior_Dark"]])
    # Outer roof slab + inner headliner
    p_roof.add_prism(
        [(-0.52, -0.62, 1.18), (0.52, -0.62, 1.18), (0.50, 0.22, 1.22), (-0.50, 0.22, 1.22)],
        [(-0.48, -0.60, 1.25), (0.48, -0.60, 1.25), (0.46, 0.20, 1.27), (-0.46, 0.20, 1.27)],
        mat_idx=0
    )
    p_roof.add_box((-0.48, -0.58, 1.15), (0.48, 0.18, 1.18), mat_idx=1)
    # A-Pillars (solid 3D bars framing the windshield cleanly from cockpit camera)
    p_roof.add_prism(
        [(0.58, 0.70, 0.78), (0.68, 0.70, 0.78), (0.50, 0.18, 1.20), (0.42, 0.18, 1.20)],
        [(0.56, 0.74, 0.84), (0.66, 0.74, 0.84), (0.48, 0.22, 1.26), (0.40, 0.22, 1.26)],
        mat_idx=0, mirror_x=True
    )
    # B-Pillars & C-Pillars (fastback buttresses)
    p_roof.add_prism(
        [(0.72, -0.48, 0.80), (0.86, -0.48, 0.80), (0.52, -0.46, 1.20), (0.44, -0.46, 1.20)],
        [(0.72, -0.40, 0.82), (0.86, -0.40, 0.82), (0.52, -0.38, 1.22), (0.44, -0.38, 1.22)],
        mat_idx=1, mirror_x=True
    )
    p_roof.add_prism(
        [(0.52, -1.32, 0.86), (0.68, -1.32, 0.86), (0.52, -0.60, 1.18), (0.42, -0.60, 1.18)],
        [(0.50, -1.28, 0.92), (0.64, -1.28, 0.92), (0.48, -0.58, 1.25), (0.38, -0.58, 1.25)],
        mat_idx=0, mirror_x=True
    )
    p_roof.build(pivot=(0.0, -0.20, 1.08), visible=True)

    # Rear Engine Decklid (closed 3D cover with slatted vents)
    p_deck = PartBuilder("engine_decklid", [mats["Paint_Body"], mats["Carbon"]])
    p_deck.add_prism(
        [(-0.54, -1.98, 0.74), (0.54, -1.98, 0.74), (0.54, -1.26, 0.82), (-0.54, -1.26, 0.82)],
        [(-0.52, -1.98, 0.82), (0.52, -1.98, 0.82), (0.52, -1.26, 0.90), (-0.52, -1.26, 0.90)],
        mat_idx=0
    )
    for slat_y in (-1.42, -1.56, -1.70, -1.84):
        p_deck.add_box((-0.38, slat_y - 0.04, 0.83), (0.38, slat_y + 0.04, 0.91), mat_idx=1)
    p_deck.build(pivot=(0.0, -1.62, 0.84), visible=True)

    # =========================================================================
    # 3. WINDOWS (Outward Normals, Glass_Tint _Cull = 0 for Inside/Outside View)
    # =========================================================================
    p_ws = PartBuilder("glass_windshield", [mats["Glass_Tint"], mats["Trim_Dark"]])
    p_ws.add_submesh(
        [(0.00, 0.74, 0.80), (0.60, 0.72, 0.80), (0.00, 0.20, 1.24), (0.44, 0.18, 1.23)],
        [(0, 1, 3, 2)], mat_idx=0, mirror_x=True, double_sided=False
    )
    # Top & bottom windshield seal frame
    p_ws.add_box((-0.58, 0.70, 0.76), (0.58, 0.76, 0.81), mat_idx=1)
    p_ws.build(pivot=(0.0, 0.46, 1.02), visible=True, outward_normal=(0.0, 1.0, 1.0))

    p_rw = PartBuilder("glass_rear", [mats["Glass_Tint"]])
    p_rw.add_submesh(
        [(0.00, -0.60, 1.22), (0.42, -0.60, 1.20), (0.00, -1.28, 0.90), (0.50, -1.28, 0.89)],
        [(0, 2, 3, 1)], mat_idx=0, mirror_x=True, double_sided=False
    )
    p_rw.build(pivot=(0.0, -0.94, 1.05), visible=True, outward_normal=(0.0, -1.0, 1.0))

    for side in (-1, 1):
        s_name = "left" if side < 0 else "right"
        sx = float(side)
        p_sw = PartBuilder(f"glass_side_{s_name}", [mats["Glass_Tint"]])
        swv = [
            (sx * 0.84,  0.68, 0.80), (sx * 0.86, -0.42, 0.82), (sx * 0.48, -0.40, 1.22), (sx * 0.46,  0.18, 1.23),
            (sx * 0.86, -0.46, 0.82), (sx * 0.62, -1.22, 0.88), (sx * 0.44, -0.60, 1.20), (sx * 0.48, -0.44, 1.22),
        ]
        swf = [(0, 1, 2, 3), (4, 5, 6, 7)] if side > 0 else [(0, 3, 2, 1), (4, 7, 6, 5)]
        p_sw.add_submesh(swv, swf, mat_idx=0, double_sided=False)
        p_sw.build(pivot=(sx * 0.68, -0.20, 1.00), visible=True, outward_normal=(sx, 0.0, 0.5))

    # =========================================================================
    # 4. ENCLOSED INTERIOR CABIN & COCKPIT (Max 2 Materials per Object)
    # =========================================================================
    p_cab = PartBuilder("interior_cabin", [mats["Interior_Dark"], mats["Trim_Dark"]])
    # Solid floor, front firewall, rear bulkhead, side sills, and transmission tunnel (no light leaks!)
    p_cab.add_box((-0.74, -0.82, 0.20), (0.74,  0.80, 0.26), mat_idx=0)  # floor
    p_cab.add_box((-0.74,  0.70, 0.24), (0.74,  0.84, 0.80), mat_idx=0)  # front firewall
    p_cab.add_box((-0.74, -0.88, 0.24), (0.74, -0.74, 0.88), mat_idx=0)  # rear firewall
    p_cab.add_box((-0.74, -1.30, 0.78), (0.74, -0.74, 0.88), mat_idx=0)  # rear parcel shelf
    p_cab.add_box((-0.14, -0.76, 0.26), (0.14,  0.72, 0.46), mat_idx=1)  # center console tunnel
    # Shifter & Handbrake lever on center tunnel
    p_cab.add_cylinder((0.0, 0.16, 0.44), (0.0, 0.18, 0.64), 0.018, 0.024, segs=8, mat_idx=1)
    p_cab.add_cylinder((-0.06, 0.02, 0.44), (-0.06, 0.16, 0.58), 0.014, 0.016, segs=8, mat_idx=1)
    p_cab.build(pivot=(0.0, 0.0, 0.50), visible=True)

    # Dashboard & Digital Cluster (Uses Screen material, width = 0.22 m = 15.3% <= 25% of 1.44 m dash width)
    p_dash = PartBuilder("interior_dash", [mats["Interior_Dark"], mats["Screen"]])
    # Lower main dashboard body (top at z_up = 0.72 m so gauge cavity above 0.72 m is open and visible through steering wheel!)
    p_dash.add_box((-0.72, 0.44, 0.54), (0.72, 0.78, 0.72), mat_idx=0)
    # Recessed driver binnacle arch: top visor (0.80..0.82), left/right cheeks, and back wall (y_fwd = 0.48..0.68)
    p_dash.add_box((-0.50, 0.43, 0.80), (-0.22, 0.68, 0.82), mat_idx=0)
    p_dash.add_box((-0.50, 0.43, 0.72), (-0.47, 0.66, 0.80), mat_idx=0)
    p_dash.add_box((-0.25, 0.43, 0.72), (-0.22, 0.66, 0.80), mat_idx=0)
    p_dash.add_box((-0.48, 0.47, 0.72), (-0.24, 0.68, 0.80), mat_idx=0)
    # Steering column sleeve entering lower dashboard below the gauge screen (does NOT block upper wheel opening!)
    p_dash.add_cylinder((-0.36, 0.28, 0.64), (-0.36, 0.46, 0.61), 0.032, 0.038, segs=10, mat_idx=0)
    # Compact digital gauge cluster screen (width 0.22 m <= 25% of dash width, clearly visible through top of steering wheel)
    p_dash.add_box((-0.47, 0.45, 0.725), (-0.25, 0.47, 0.795), mat_idx=1)
    p_dash.build(pivot=(0.0, 0.56, 0.70), visible=True)

    # Driver Bucket Seat (Always present at x = -0.36)
    p_dseat = PartBuilder("interior_driver_seat", [mats["Interior_Dark"], mats["Paint_Accent"]])
    p_dseat.add_box((-0.54, -0.44, 0.26), (-0.18,  0.00, 0.36), mat_idx=0)
    p_dseat.add_box((-0.56, -0.44, 0.34), (-0.50, -0.02, 0.46), mat_idx=0)
    p_dseat.add_box((-0.22, -0.44, 0.34), (-0.16, -0.02, 0.46), mat_idx=0)
    p_dseat.add_box((-0.52, -0.50, 0.36), (-0.20, -0.38, 0.90), mat_idx=0)
    p_dseat.add_box((-0.58, -0.46, 0.58), (-0.14, -0.36, 0.76), mat_idx=1)
    p_dseat.add_box((-0.48, -0.48, 0.88), (-0.24, -0.38, 1.04), mat_idx=1)
    p_dseat.build(pivot=(-0.36, -0.28, 0.62), visible=True)

    # =========================================================================
    # 5. STEERING WHEEL VARIANTS (mod_steering_0_stock .. 2_drift)
    #    Pivot at hub center (-0.360, 0.250, 0.640) -> Unity (-0.360, 0.640, 0.250).
    #    Top of wheel reaches y = 0.788 m in Unity (3.2 cm below 0.820 m dash top line!).
    # =========================================================================
    sw_pivot = (-0.36, 0.25, 0.64)
    build_steering_variant("mod_steering_0_stock",   [mats["Interior_Dark"], mats["Metal_Chrome"]], sw_pivot, root_obj=None, visible=True,  r_rim=0.148, is_yoke=False, spoke_mat_idx=1)
    build_steering_variant("mod_steering_1_formula", [mats["Carbon"],        mats["Paint_Accent"]], sw_pivot, root_obj=None, visible=False, r_rim=0.145, is_yoke=True,  spoke_mat_idx=0)
    build_steering_variant("mod_steering_2_drift",   [mats["Interior_Dark"], mats["Paint_Rim"]],    sw_pivot, root_obj=None, visible=False, r_rim=0.145, is_yoke=False, spoke_mat_idx=1)

    # =========================================================================
    # 6. INTERIOR PASSENGER, PILLAR & ROLLCAGE SLOTS
    # =========================================================================
    pass_pivot = (0.36, -0.28, 0.56)
    # 0: Stock Passenger Seat (Visible by default)
    ppass0 = PartBuilder("mod_interior_passenger_0_seat", [mats["Interior_Dark"], mats["Paint_Accent"]])
    ppass0.add_box((0.18, -0.44, 0.26), (0.54,  0.00, 0.36), mat_idx=0)
    ppass0.add_box((0.20, -0.50, 0.36), (0.52, -0.38, 0.90), mat_idx=0)
    ppass0.add_box((0.14, -0.46, 0.58), (0.58, -0.36, 0.76), mat_idx=1)
    ppass0.add_box((0.24, -0.48, 0.88), (0.48, -0.38, 1.04), mat_idx=1)
    ppass0.build(pivot=pass_pivot, visible=True)

    # 1: Passenger Twin Nitrous Bottles
    ppass1 = PartBuilder("mod_interior_passenger_1_nos", [mats["Paint_Accent"], mats["Metal_Chrome"]])
    ppass1.add_box((0.18, -0.42, 0.26), (0.54, 0.04, 0.32), mat_idx=1)
    for bx in (0.28, 0.44):
        ppass1.add_cylinder((bx, -0.38, 0.38), (bx, 0.02, 0.48), 0.065, 0.065, segs=12, mat_idx=0)
        ppass1.add_cylinder((bx, 0.02, 0.48), (bx, 0.09, 0.50), 0.065, 0.022, segs=12, mat_idx=1)
    ppass1.build(pivot=pass_pivot, visible=False)

    # A-Pillar Gauge Pods
    pil_pivot = (-0.54, 0.50, 0.96)
    ppil0 = PartBuilder("mod_interior_pillar_0_stock", [mats["Interior_Dark"]])
    ppil0.add_box((-0.60, 0.46, 0.86), (-0.54, 0.54, 0.90), mat_idx=0)
    ppil0.build(pivot=pil_pivot, visible=True)

    ppil1 = PartBuilder("mod_interior_pillar_1_gauges", [mats["Interior_Dark"], mats["Screen"]])
    for gi, (gy, gz) in enumerate([(0.58, 0.88), (0.50, 0.96), (0.42, 1.04)]):
        ppil1.add_cylinder((-0.55, gy, gz), (-0.50, gy - 0.04, gz - 0.01), 0.028, 0.028, segs=10, mat_idx=0)
        ppil1.add_cylinder((-0.50, gy - 0.04, gz - 0.01), (-0.495, gy - 0.045, gz - 0.01), 0.022, 0.022, segs=10, mat_idx=1)
    ppil1.build(pivot=pil_pivot, visible=False)

    # Rollcage Variants (0_none, 1_half, 2_full)
    rc_pivot = (0.0, -0.30, 0.82)
    add_empty("mod_rollcage_0_none", rc_pivot, forward_plus_z=True)

    prc1 = PartBuilder("mod_rollcage_1_half", [mats["Paint_Accent"]])
    prc1.add_cylinder((0.52, -0.52, 0.30), (0.44, -0.52, 1.12), 0.024, 0.024, segs=8, mat_idx=0, mirror_x=True)
    prc1.add_cylinder((-0.44, -0.52, 1.12), (0.44, -0.52, 1.12), 0.024, 0.024, segs=8, mat_idx=0)
    prc1.add_cylinder((0.44, -0.52, 1.10), (-0.48, -0.52, 0.36), 0.020, 0.020, segs=8, mat_idx=0, mirror_x=True)
    prc1.build(pivot=rc_pivot, visible=False)

    prc2 = PartBuilder("mod_rollcage_2_full", [mats["Paint_Accent"]])
    prc2.add_cylinder((0.52, -0.52, 0.30), (0.44, -0.52, 1.12), 0.024, 0.024, segs=8, mat_idx=0, mirror_x=True)
    prc2.add_cylinder((-0.44, -0.52, 1.12), (0.44, -0.52, 1.12), 0.024, 0.024, segs=8, mat_idx=0)
    prc2.add_cylinder((0.44, -0.52, 1.10), (-0.48, -0.52, 0.36), 0.020, 0.020, segs=8, mat_idx=0, mirror_x=True)
    prc2.add_cylinder((0.44, -0.52, 1.12), (0.42,  0.18, 1.16), 0.022, 0.022, segs=8, mat_idx=0, mirror_x=True)
    prc2.add_cylinder((0.42,  0.18, 1.16), (0.56,  0.68, 0.78), 0.022, 0.022, segs=8, mat_idx=0, mirror_x=True)
    prc2.add_cylinder((0.50, -0.48, 0.72), (0.66,  0.64, 0.34), 0.020, 0.020, segs=8, mat_idx=0, mirror_x=True)
    prc2.build(pivot=rc_pivot, visible=False)

    # =========================================================================
    # 7. FRUNK BAY & HOOD VARIANTS (mod_hood_0_stock .. 4_carbon)
    # =========================================================================
    frunk_pivot = (0.0, 1.36, 0.44)
    pfr0 = PartBuilder("mod_frunk_0_stock", [mats["Trim_Dark"]])
    pfr0.add_box((-0.52, 0.88, 0.24), (0.52, 1.88, 0.56), mat_idx=0)
    pfr0.build(pivot=frunk_pivot, visible=True)

    pfr1 = PartBuilder("mod_frunk_1_nos_bay", [mats["Trim_Dark"], mats["Paint_Accent"]])
    pfr1.add_box((-0.52, 0.88, 0.24), (0.52, 1.88, 0.32), mat_idx=0)
    pfr1.add_box((-0.48, 0.92, 0.32), (0.48, 1.32, 0.52), mat_idx=0)
    pfr1.add_box((-0.50, 1.18, 0.54), (0.50, 1.24, 0.58), mat_idx=1)
    pfr1.add_cylinder((0.18, 1.36, 0.40), (0.18, 1.76, 0.42), 0.065, 0.065, segs=10, mat_idx=1, mirror_x=True)
    pfr1.build(pivot=frunk_pivot, visible=False)

    hood_pivot = (0.0, 0.82, 0.78)
    for idx_h, (h_name, h_style) in enumerate((
        ("mod_hood_0_stock",  "stock"),
        ("mod_hood_1_street", "street"),
        ("mod_hood_2_track",  "track"),
        ("mod_hood_3_blower", "blower"),
        ("mod_hood_4_carbon", "carbon"),
    )):
        h_main_mat = mats["Carbon"] if h_style == "carbon" else mats["Paint_Body"]
        h_sec_mat = mats["Metal_Chrome"] if h_style in ("blower", "carbon") else mats["Carbon"]
        ph = PartBuilder(h_name, [h_main_mat, h_sec_mat])
        # Solid closed wedge hood slab (front section + rear section)
        ph.add_prism(
            [(-0.52, 1.42, 0.58), (0.52, 1.42, 0.58), (0.48, 2.02, 0.44), (-0.48, 2.02, 0.44)],
            [(-0.52, 1.42, 0.64), (0.52, 1.42, 0.64), (0.48, 2.02, 0.48), (-0.48, 2.02, 0.48)],
            mat_idx=0
        )
        ph.add_prism(
            [(-0.56, 0.76, 0.74), (0.56, 0.76, 0.74), (0.52, 1.42, 0.58), (-0.52, 1.42, 0.58)],
            [(-0.56, 0.76, 0.80), (0.56, 0.76, 0.80), (0.52, 1.42, 0.64), (-0.52, 1.42, 0.64)],
            mat_idx=0
        )
        # Distinct angular power-dome ribs on all hoods for original styling
        ph.add_box((0.22, 0.84, 0.64), (0.28, 1.84, 0.76), mat_idx=0, mirror_x=True)

        if h_style in ("street", "track", "carbon"):
            ph.add_box((-0.34, 1.82, 0.46), (0.34, 1.94, 0.52), mat_idx=1)
        if h_style in ("track", "carbon"):
            ph.add_box((0.12, 1.34, 0.60), (0.34, 1.68, 0.68), mat_idx=1, mirror_x=True)
            ph.add_box((0.10, 0.86, 0.76), (0.14, 1.32, 0.83), mat_idx=1, mirror_x=True)
        if h_style == "blower":
            ph.add_box((-0.24, 1.18, 0.62), (0.24, 1.66, 0.84), mat_idx=1)
            ph.add_cylinder((0.09, 1.66, 0.76), (0.09, 1.72, 0.76), 0.055, 0.055, segs=10, mat_idx=1, mirror_x=True)
        if h_style == "carbon":
            ph.add_cylinder((0.36, 1.88, 0.50), (0.36, 1.88, 0.53), 0.032, 0.032, segs=8, mat_idx=1, mirror_x=True)

        ph.build(pivot=hood_pivot, visible=(idx_h == 0))

    # =========================================================================
    # 8. FENDER ADD-ON VARIANTS (mod_fender_0_stock .. 2_widebody)
    # =========================================================================
    fend_pivot = (0.0, 0.0, 0.62)
    pf0 = PartBuilder("mod_fender_0_stock", [mats["Paint_Body"]])
    pf0.add_box((0.84, 0.92, 0.56), (0.88, 1.02, 0.62), mat_idx=0, mirror_x=True)
    pf0.build(pivot=fend_pivot, visible=True)

    pf1 = PartBuilder("mod_fender_1_louvers", [mats["Carbon"]])
    for li in range(4):
        ly = 1.14 + li * 0.10
        pf1.add_box((0.62, ly, 0.68), (0.82, ly + 0.05, 0.76), mat_idx=0, mirror_x=True)
    for li in range(3):
        ly = -1.36 + li * 0.11
        pf1.add_box((0.66, ly, 0.84), (0.86, ly + 0.05, 0.91), mat_idx=0, mirror_x=True)
    pf1.build(pivot=fend_pivot, visible=False)

    pf2 = PartBuilder("mod_fender_2_widebody", [mats["Paint_Body"], mats["Carbon"]])
    # Front and rear widebody overfender flares
    pf2.add_box((0.84,  0.86, 0.30), (0.98,  1.66, 0.74), mat_idx=0, mirror_x=True)
    pf2.add_box((0.88, -1.68, 0.32), (1.02, -0.84, 0.84), mat_idx=0, mirror_x=True)
    for li in range(4):
        ly = 1.14 + li * 0.10
        pf2.add_box((0.64, ly, 0.68), (0.88, ly + 0.05, 0.76), mat_idx=1, mirror_x=True)
    pf2.build(pivot=fend_pivot, visible=False)

    # =========================================================================
    # 9. FRONT BUMPERS (mod_bumper_front_0_stock .. 4_timeattack)
    # =========================================================================
    fb_pivot = (0.0, 2.02, 0.34)
    for idx_b, (b_name, lip_ext, canards, rods) in enumerate((
        ("mod_bumper_front_0_stock",      0.02, 0, False),
        ("mod_bumper_front_1_street",     0.07, 0, False),
        ("mod_bumper_front_2_race",       0.11, 1, False),
        ("mod_bumper_front_3_track",      0.14, 2, False),
        ("mod_bumper_front_4_timeattack", 0.24, 2, True),
    )):
        pfb = PartBuilder(b_name, [mats["Paint_Body"], mats["Carbon"]])
        # Main angular front fascia block (closed 3D volumes)
        pfb.add_prism(
            [(-0.82, 1.92, 0.20), (0.82, 1.92, 0.20), (0.74, 2.12, 0.20), (-0.74, 2.12, 0.20)],
            [(-0.78, 1.92, 0.48), (0.78, 1.92, 0.48), (0.68, 2.08, 0.46), (-0.68, 2.08, 0.46)],
            mat_idx=0
        )
        # Hexagonal center grille recess + side brake duct blocks (Carbon/Dark)
        pfb.add_box((-0.44, 1.96, 0.22), (0.44, 2.13, 0.40), mat_idx=1)
        pfb.add_box(( 0.50, 1.95, 0.22), (0.74, 2.10, 0.38), mat_idx=1, mirror_x=True)
        # Lower splitter plate
        pfb.add_box((-0.86 - lip_ext * 0.3, 1.88, 0.14), (0.86 + lip_ext * 0.3, 2.12 + lip_ext, 0.20), mat_idx=1)

        if canards >= 1:
            pfb.add_box((0.74, 1.94, 0.28), (0.92, 2.10, 0.32), mat_idx=1, mirror_x=True)
        if canards >= 2:
            pfb.add_box((0.72, 1.98, 0.36), (0.90, 2.12, 0.40), mat_idx=1, mirror_x=True)
        if rods:
            # Side endplates + splitter struts
            pfb.add_box((0.88, 1.88, 0.14), (0.94, 2.34, 0.42), mat_idx=1, mirror_x=True)
            pfb.add_cylinder((0.26, 2.10, 0.38), (0.26, 2.30, 0.18), 0.012, 0.012, segs=6, mat_idx=1, mirror_x=True)

        pfb.build(pivot=fb_pivot, visible=(idx_b == 0))

    # =========================================================================
    # 10. REAR BUMPERS (mod_bumper_rear_0_stock .. 3_timeattack)
    # =========================================================================
    rb_pivot = (0.0, -2.06, 0.36)
    for idx_rb, (rb_name, n_fins, d_ext) in enumerate((
        ("mod_bumper_rear_0_stock",      0, 0.00),
        ("mod_bumper_rear_1_street",     2, 0.05),
        ("mod_bumper_rear_2_track",      4, 0.10),
        ("mod_bumper_rear_3_timeattack", 6, 0.20),
    )):
        prb = PartBuilder(rb_name, [mats["Paint_Body"], mats["Carbon"]])
        prb.add_prism(
            [(-0.84, -2.14, 0.22), (0.84, -2.14, 0.22), (0.86, -1.94, 0.22), (-0.86, -1.94, 0.22)],
            [(-0.80, -2.12, 0.68), (0.80, -2.12, 0.68), (0.82, -1.94, 0.68), (-0.82, -1.94, 0.68)],
            mat_idx=0
        )
        # Lower diffuser housing
        prb.add_box((-0.68, -2.16 - d_ext, 0.16), (0.68, -1.92, 0.36), mat_idx=1)
        for fi in range(n_fins // 2):
            fx = 0.18 + fi * 0.20
            prb.add_box((fx - 0.015, -2.18 - d_ext, 0.12), (fx + 0.015, -1.94, 0.36), mat_idx=1, mirror_x=True)
        prb.build(pivot=rb_pivot, visible=(idx_rb == 0))

    # =========================================================================
    # 11. SIDE SKIRTS (mod_skirts_0_stock_left/right .. 3_wide_left/right)
    # =========================================================================
    for side in (-1, 1):
        s_name = "left" if side < 0 else "right"
        sx = float(side)
        sk_pivot = (sx * 0.88, 0.14, 0.20)
        for idx_sk, (sk_base, w_out, fin_h) in enumerate((
            ("mod_skirts_0_stock",  0.90, 0.00),
            ("mod_skirts_1_street", 0.95, 0.08),
            ("mod_skirts_2_track",  0.99, 0.16),
            ("mod_skirts_3_wide",   1.05, 0.24),
        )):
            psk = PartBuilder(f"{sk_base}_{s_name}", [mats["Carbon"], mats["Paint_Body"]])
            psk.add_box((sx * 0.78, -0.84, 0.14), (sx * w_out, 0.84, 0.24), mat_idx=0)
            if fin_h > 0.0:
                psk.add_box((sx * (w_out - 0.03), -0.84, 0.14), (sx * w_out, -0.64, 0.24 + fin_h), mat_idx=1)
            psk.build(pivot=sk_pivot, visible=(idx_sk == 0))

    # =========================================================================
    # 12. SPOILERS / REAR WINGS (mod_spoiler_0_stock .. 5_timeattack)
    # =========================================================================
    sp_pivot = (0.0, -1.96, 1.05)

    # 0: Stock Integrated Deck Lip (Visible by default)
    psp0 = PartBuilder("mod_spoiler_0_stock", [mats["Paint_Body"]])
    psp0.add_box((-0.68, -2.10, 0.80), (0.68, -1.96, 0.86), mat_idx=0)
    psp0.build(pivot=sp_pivot, visible=True)

    # 1: Classic Ducktail
    psp1 = PartBuilder("mod_spoiler_1_ducktail", [mats["Paint_Body"], mats["Carbon"]])
    psp1.add_prism(
        [(-0.68, -2.12, 0.80), (0.68, -2.12, 0.80), (0.68, -1.86, 0.80), (-0.68, -1.86, 0.80)],
        [(-0.64, -2.18, 0.98), (0.64, -2.18, 0.98), (0.64, -1.96, 0.86), (-0.64, -1.96, 0.86)],
        mat_idx=0
    )
    psp1.add_box((-0.62, -2.19, 0.96), (0.62, -2.14, 1.00), mat_idx=1)
    psp1.build(pivot=sp_pivot, visible=False)

    # 2: Low Street Wing
    psp2 = PartBuilder("mod_spoiler_2_street", [mats["Paint_Body"], mats["Carbon"]])
    psp2.add_box((0.42, -2.06, 0.80), (0.46, -1.94, 0.98), mat_idx=1, mirror_x=True)
    psp2.add_box((-0.78, -2.14, 0.96), (0.78, -1.90, 1.01), mat_idx=0)
    psp2.add_box((0.76, -2.16, 0.90), (0.80, -1.88, 1.06), mat_idx=1, mirror_x=True)
    psp2.build(pivot=sp_pivot, visible=False)

    # 3: GT Pedestal Wing
    psp3 = PartBuilder("mod_spoiler_3_gtwing", [mats["Carbon"], mats["Paint_Accent"]])
    psp3.add_box((0.32, -2.04, 0.80), (0.36, -1.88, 1.16), mat_idx=0, mirror_x=True)
    psp3.add_box((-0.86, -2.16, 1.14), (0.86, -1.88, 1.19), mat_idx=0)
    psp3.add_box((0.84, -2.20, 1.04), (0.88, -1.84, 1.26), mat_idx=0, mirror_x=True)
    psp3.add_box((0.87, -2.18, 1.18), (0.89, -1.86, 1.24), mat_idx=1, mirror_x=True)
    psp3.build(pivot=sp_pivot, visible=False)

    # 4: Swan-Neck Active Track Wing
    psp4 = PartBuilder("mod_spoiler_4_swanneck", [mats["Carbon"], mats["Paint_Accent"]])
    psp4.add_box((0.28, -2.00, 0.80), (0.32, -1.84, 1.30), mat_idx=0, mirror_x=True)
    psp4.add_box((0.28, -2.08, 1.24), (0.32, -1.84, 1.32), mat_idx=0, mirror_x=True)
    psp4.add_box((-0.92, -2.22, 1.22), (0.92, -1.90, 1.27), mat_idx=0)
    psp4.add_box((-0.90, -2.24, 1.27), (0.90, -2.08, 1.32), mat_idx=1)
    psp4.add_box((0.90, -2.28, 1.04), (0.94, -1.86, 1.36), mat_idx=0, mirror_x=True)
    psp4.build(pivot=sp_pivot, visible=False)

    # 5: Time-Attack Chassis-Mount Dual-Deck Wing
    psp5 = PartBuilder("mod_spoiler_5_timeattack", [mats["Carbon"], mats["Paint_Accent"]])
    psp5.add_box((0.36, -2.24, 0.34), (0.40, -2.04, 1.38), mat_idx=0, mirror_x=True)
    psp5.add_box((-1.02, -2.32, 1.26), (1.02, -1.96, 1.31), mat_idx=0)
    psp5.add_box((-0.98, -2.36, 1.36), (0.98, -2.08, 1.41), mat_idx=1)
    psp5.add_box((1.00, -2.40, 0.96), (1.04, -1.92, 1.48), mat_idx=0, mirror_x=True)
    psp5.build(pivot=sp_pivot, visible=False)

    # =========================================================================
    # 13. ROOF SCOOPS (mod_roof_scoop_0_none .. 3_sharkfin)
    # =========================================================================
    rs_pivot = (0.0, -0.10, 1.26)
    add_empty("mod_roof_scoop_0_none", rs_pivot, forward_plus_z=True)

    prs1 = PartBuilder("mod_roof_scoop_1_single", [mats["Carbon"], mats["Paint_Accent"]])
    prs1.add_box((-0.14, -0.34, 1.24), (0.14, 0.08, 1.34), mat_idx=0)
    prs1.add_box((-0.11,  0.04, 1.26), (0.11, 0.09, 1.32), mat_idx=1)
    prs1.build(pivot=rs_pivot, visible=False)

    prs2 = PartBuilder("mod_roof_scoop_2_twin", [mats["Paint_Body"], mats["Carbon"]])
    prs2.add_box((0.06, -0.36, 1.24), (0.24, 0.10, 1.33), mat_idx=0, mirror_x=True)
    prs2.add_box((0.08,  0.06, 1.26), (0.22, 0.11, 1.31), mat_idx=1, mirror_x=True)
    prs2.build(pivot=rs_pivot, visible=False)

    prs3 = PartBuilder("mod_roof_scoop_3_sharkfin", [mats["Carbon"], mats["Paint_Accent"]])
    prs3.add_box((-0.14, -0.34, 1.24), (0.14, 0.08, 1.34), mat_idx=0)
    prs3.add_box((-0.02, -1.68, 0.90), (0.02, -0.26, 1.34), mat_idx=0)
    prs3.add_box((-0.025, -1.68, 1.26), (0.025, -0.30, 1.34), mat_idx=1)
    prs3.build(pivot=rs_pivot, visible=False)

    # =========================================================================
    # 14. HEADLIGHTS, TAILLIGHTS, EXHAUSTS, MIRRORS, NEON
    # =========================================================================
    hl_pivot = (0.0, 1.76, 0.56)
    for idx_hl, (hl_name, hl_emit_mat) in enumerate((
        ("mod_headlight_0_stock", mats["Light_Head"]),
        ("mod_headlight_1_amber", mats["Paint_Caliper"]),
        ("mod_headlight_2_halo",  mats["Neon"]),
        ("mod_headlight_3_rally", mats["Decal_Graphic"]),
    )):
        phl = PartBuilder(hl_name, [mats["Trim_Dark"], hl_emit_mat])
        # Angular twin-hex headlight housing (front face at z=1.92, top at y=0.60 in Unity)
        phl.add_box((0.54, 1.62, 0.50), (0.78, 1.92, 0.60), mat_idx=0, mirror_x=True)
        # Dual glowing LED lens bars protruding 5 mm in front of housing (z=1.925 in Unity) and framed inside housing (y <= 0.60)
        phl.add_box((0.56, 1.66, 0.555), (0.76, 1.925, 0.590), mat_idx=1, mirror_x=True)
        phl.add_box((0.56, 1.80, 0.515), (0.76, 1.925, 0.545), mat_idx=1, mirror_x=True)
        phl.build(pivot=hl_pivot, visible=(idx_hl == 0))

    tl_pivot = (0.0, -2.06, 0.74)
    for idx_tl, (tl_name, tl_mat, segmented) in enumerate((
        ("mod_taillight_0_stock", mats["Light_Tail"],    False),
        ("mod_taillight_1_clear", mats["Decal_Graphic"], False),
        ("mod_taillight_2_cyber", mats["Light_Tail"],    True),
    )):
        ptl = PartBuilder(tl_name, [mats["Trim_Dark"], tl_mat])
        ptl.add_box((-0.80, -2.13, 0.70), (0.80, -2.04, 0.78), mat_idx=0)
        if not segmented:
            ptl.add_box((-0.78, -2.14, 0.72), (0.78, -2.08, 0.76), mat_idx=1)
        else:
            for bx in (0.14, 0.36, 0.58):
                ptl.add_box((bx, -2.15, 0.71), (bx + 0.14, -2.08, 0.77), mat_idx=1, mirror_x=True)
        ptl.build(pivot=tl_pivot, visible=(idx_tl == 0))

    ex_pivot = (0.0, -2.16, 0.24)
    for idx_ex, (ex_name, tips, z_tilt) in enumerate((
        ("mod_exhaust_0_stock",   [(-0.46, 0.048), (0.46, 0.048)], 0.00),
        ("mod_exhaust_1_center",  [(-0.08, 0.054), (0.08, 0.054)], 0.00),
        ("mod_exhaust_2_quad",    [(-0.52, 0.046), (-0.40, 0.046), (0.40, 0.046), (0.52, 0.046)], 0.00),
        ("mod_exhaust_3_upswept", [(-0.09, 0.058), (0.09, 0.058)], 0.12),
    )):
        pex = PartBuilder(ex_name, [mats["Metal_Chrome"], mats["Trim_Dark"]])
        for tx, tr in tips:
            pex.add_cylinder((tx, -1.96, 0.24), (tx, -2.26, 0.24 + z_tilt), tr, tr, segs=12, mat_idx=0)
            pex.add_cylinder((tx, -2.24, 0.24 + z_tilt * 0.9), (tx, -2.265, 0.24 + z_tilt), tr * 0.76, tr * 0.76, segs=12, mat_idx=1)
        pex.build(pivot=ex_pivot, visible=(idx_ex == 0))

    mir_pivot = (0.0, 0.68, 0.84)
    for idx_m, (m_name, cap_mat, h_scale) in enumerate((
        ("mod_mirror_0_stock",  mats["Paint_Body"],   1.00),
        ("mod_mirror_1_carbon", mats["Carbon"],       0.82),
        ("mod_mirror_2_aero",   mats["Paint_Accent"], 0.65),
    )):
        pm = PartBuilder(m_name, [mats["Trim_Dark"], cap_mat])
        pm.add_box((0.82, 0.64, 0.78), (0.94, 0.72, 0.84), mat_idx=0, mirror_x=True)
        pm.add_box((0.90, 0.62, 0.82), (1.05, 0.74, 0.82 + 0.08 * h_scale), mat_idx=1, mirror_x=True)
        pm.build(pivot=mir_pivot, visible=(idx_m == 0))

    neon_pivot = (0.0, 0.0, 0.10)
    add_empty("mod_neon_0_none", neon_pivot, forward_plus_z=True)

    pn1 = PartBuilder("mod_neon_1_sides", [mats["Neon"]])
    pn1.add_box((0.82, -0.74, 0.09), (0.86, 0.74, 0.11), mat_idx=0, mirror_x=True)
    pn1.build(pivot=neon_pivot, visible=False)

    pn2 = PartBuilder("mod_neon_2_full", [mats["Neon"]])
    pn2.add_box((0.82, -0.74, 0.09), (0.86, 0.74, 0.11), mat_idx=0, mirror_x=True)
    pn2.add_box((-0.56,  1.96, 0.09), (0.56,  2.02, 0.11), mat_idx=0)
    pn2.add_box((-0.56, -2.06, 0.09), (0.56, -2.00, 0.11), mat_idx=0)
    pn2.build(pivot=neon_pivot, visible=False)

    # =========================================================================
    # 15. DECALS / STICKERS (Separate Objects, Hidden on Stock Default)
    # =========================================================================
    pdec1 = PartBuilder("decal_1_center_stripes", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec1.add_box((0.03,  0.80, 0.642), (0.11,  1.98, 0.648), mat_idx=0, mirror_x=True)
    pdec1.add_box((0.03, -0.58, 1.252), (0.11,  0.18, 1.272), mat_idx=0, mirror_x=True)
    pdec1.add_box((0.115, 0.80, 0.642), (0.13,  1.98, 0.648), mat_idx=1, mirror_x=True)
    pdec1.build(pivot=(0.0, 0.60, 0.90), visible=False)

    pdec2 = PartBuilder("decal_2_side_rocker", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec2.add_box((0.912, -0.40, 0.25), (0.918, 0.68, 0.32), mat_idx=0, mirror_x=True)
    pdec2.add_box((0.912, -0.40, 0.33), (0.918, 0.68, 0.35), mat_idx=1, mirror_x=True)
    pdec2.build(pivot=(0.0, 0.14, 0.30), visible=False)

    pdec3 = PartBuilder("decal_3_number_plates", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec3.add_box((0.922, -0.04, 0.40), (0.928, 0.42, 0.72), mat_idx=0, mirror_x=True)
    pdec3.add_box((0.928,  0.06, 0.58), (0.932, 0.32, 0.64), mat_idx=1, mirror_x=True)
    pdec3.build(pivot=(0.0, 0.18, 0.56), visible=False)

    pdec4 = PartBuilder("decal_4_side_slashes", [mats["Paint_Accent"], mats["Decal_Graphic"]])
    pdec4.add_box((0.962, -1.04, 0.56), (0.968, -0.56, 0.64), mat_idx=0, mirror_x=True)
    pdec4.add_box((0.962, -1.12, 0.44), (0.968, -0.64, 0.52), mat_idx=1, mirror_x=True)
    pdec4.build(pivot=(0.0, -0.84, 0.56), visible=False)

    pdec5 = PartBuilder("decal_5_windshield_banner", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec5.add_submesh(
        [(0.00, 0.32, 1.16), (0.46, 0.30, 1.15), (0.00, 0.20, 1.245), (0.44, 0.18, 1.235)],
        [(0, 1, 3, 2)], mat_idx=0, mirror_x=True
    )
    pdec5.build(pivot=(0.0, 0.26, 1.20), visible=False, outward_normal=(0.0, 1.0, 1.0))

    # =========================================================================
    # 16. WHEELS + CALIPERS + 4 RIM VARIANTS + TIRE LETTERING
    #     - Wheel bottoms are strictly at y = 0.000!
    #     - Every object has <= 2 materials!
    #     - Every child rim & tire lettering has localPosition=(0,0,0), localRotation=(0,0,0)!
    # =========================================================================
    wheel_specs = [
        ("wheel-front-left",  -0.82,  1.28, 0.35, 0.35, 0.28, -1),
        ("wheel-front-right",  0.82,  1.28, 0.35, 0.35, 0.28,  1),
        ("wheel-back-left",   -0.84, -1.24, 0.37, 0.37, 0.32, -1),
        ("wheel-back-right",   0.84, -1.24, 0.37, 0.37, 0.32,  1),
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

        # Base Wheel Mesh: Tire (Trim_Dark) + Brake Disc (Metal_Chrome) -> strictly 2 materials!
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
        # Brake disc inside wheel barrel
        pw.add_cylinder((wx - sx * 0.02, wy, wz), (wx + sx * 0.02, wy, wz), r_disc, r_disc, segs=14, mat_idx=1)
        w_obj = pw.build(parent=None, pivot=w_center, visible=True)

        # Brake Caliper as direct child of root (does not spin with wheel; pivot at wheel center)
        cal_name = w_name.replace("wheel-", "caliper-")
        pcal = PartBuilder(cal_name, [mats["Paint_Caliper"]])
        pcal.add_box(
            (wx - sx * 0.01, wy - r_disc * 0.85, wz - 0.08),
            (wx + sx * 0.04, wy - r_disc * 0.35, wz + 0.10),
            mat_idx=0
        )
        pcal.build(parent=None, pivot=w_center, visible=True)

        # Rim 0: Sport 10-Spoke (Visible by default, localPos = 0,0,0)
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

        # Rim 1: 5-Spoke Deep-Dish (Hidden by default, localPos = 0,0,0)
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

        # Rim 2: Aero Turbofan Disc (Hidden by default, localPos = 0,0,0)
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

        # Rim 3: Multi-Spoke Y-Mesh (Hidden by default, localPos = 0,0,0)
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

        # Sidewall Tire Lettering (Hidden by default on stock, localPos = 0,0,0)
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
    # 17. LIGHT & COCKPIT CAMERA EMPTIES
    #     - light_head_L, light_head_R: front headlight centers, facing +Z
    #     - light_tail_L, light_tail_R: rear taillight centers, facing -Z
    #     - camera_cockpit: driver eye height above driver seat, facing +Z
    # =========================================================================
    add_empty("light_head_L",   (-0.66,  1.95, 0.58), forward_plus_z=True)
    add_empty("light_head_R",   ( 0.66,  1.95, 0.58), forward_plus_z=True)
    add_empty("light_tail_L",   (-0.64, -2.14, 0.74), forward_plus_z=False)
    add_empty("light_tail_R",   ( 0.64, -2.14, 0.74), forward_plus_z=False)
    add_empty("camera_cockpit", (-0.36, -0.18, 0.90), forward_plus_z=True)

    for o in list(bpy.data.objects):
        if o != root and o.parent is None:
            loc = o.location.copy()
            rot = o.rotation_euler.copy()
            o.parent = root
            o.matrix_parent_inverse = Matrix.Identity(4)
            o.location = loc
            o.rotation_euler = rot

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
    return root


def export_racer_coupe_01_fbx():
    root = bpy.data.objects.get("RacerCoupe_01")
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

    out_dir = "/home/zoro/harvester/Harvester/Assets/Cars/RacerCoupe_01"
    os.makedirs(out_dir, exist_ok=True)
    fbx_path = os.path.join(out_dir, "RacerCoupe_01.fbx")
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
    return fbx_path


if __name__ == "__main__":
    build_racer_coupe_01()
    export_racer_coupe_01_fbx()

