"""
Harvester — HotHatch_01 (Compact 2-Box Performance Hot-Hatch / Touring Hatchback)
Blender 4.x Python Generator Script (.agents/skills/arac-modelleme/SKILL.md)

Design Identity (HotHatch_01):
- Class: hatchback (Compact AWD/FWD Turbo 2-Box Performance Hot-Hatch)
- Silhouette & Proportions:
  - Crisp 2-box hatchback silhouette with a short sloping front hood, long flat roof extending to z = -1.62,
    signature wide boomerang C-pillar, and steep upright rear tailgate + rear glass
  - Slim upper front radiator slot with a full-width accent stripe connecting dual-U LED headlights,
    paired with a wide lower honeycomb intake and C-blade side air curtains
  - Roof-edge hatchback spoilers (stock lip, clubsport ducktail, touring-car wing, swan-neck, time-attack)
  - Wide horizontal L-block LED taillights and rear diffuser with twin/center/quad exhaust options
- Coordinate convention (Unity target):
  - Forward: +Z (Blender -Y)
  - Up:      +Y (Blender +Z)
  - Right:   +X (Blender -X before FBX axis conversion)
  - Root transform: pos=(0,0,0), rot=(0,0,0), scale=(1,1,1)
  - Origin (0,0,0): ground level (y=0), exact midpoint between front (+1.32) and rear (-1.32) axles.
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
                bsdf.inputs["Emission Strength"].default_value = 2.5
    if alpha < 1.0:
        mat.diffuse_color = (rgb[0], rgb[1], rgb[2], alpha)
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


def build_hot_hatch_01():
    clear_scene()

    root = bpy.data.objects.new("HotHatch_01", None)
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
        "Paint_Body":    make_mat("Paint_Body",    (0.10, 0.34, 0.86), metallic=0.35, roughness=0.26),
        "Paint_Rim":     make_mat("Paint_Rim",     (0.84, 0.86, 0.90), metallic=0.75, roughness=0.24),
        "Paint_Caliper": make_mat("Paint_Caliper", (0.88, 0.12, 0.14), metallic=0.30, roughness=0.25),
        "Paint_Accent":  make_mat("Paint_Accent",  (0.90, 0.14, 0.16), metallic=0.15, roughness=0.30),
        "Neon":          make_mat("Neon",          (0.05, 0.85, 1.00), metallic=0.0,  roughness=0.1, emission=(0.05, 0.85, 1.00)),
        "Glass_Tint":    make_mat("Glass_Tint",    (0.08, 0.11, 0.14), metallic=0.1,  roughness=0.08, alpha=0.42),
        "Trim_Dark":     make_mat("Trim_Dark",     (0.09, 0.09, 0.10), metallic=0.05, roughness=0.82),
        "Carbon":        make_mat("Carbon",        (0.13, 0.14, 0.16), metallic=0.25, roughness=0.38),
        "Metal_Chrome":  make_mat("Metal_Chrome",  (0.84, 0.86, 0.89), metallic=0.92, roughness=0.16),
        "Interior_Dark": make_mat("Interior_Dark", (0.14, 0.14, 0.16), metallic=0.05, roughness=0.88),
        "Light_Head":    make_mat("Light_Head",    (1.00, 0.98, 0.92), metallic=0.0,  roughness=0.1, emission=(1.00, 0.98, 0.92)),
        "Light_Tail":    make_mat("Light_Tail",    (1.00, 0.05, 0.05), metallic=0.0,  roughness=0.1, emission=(1.00, 0.05, 0.05)),
        "Decal_Graphic": make_mat("Decal_Graphic", (0.96, 0.96, 0.96), metallic=0.05, roughness=0.35),
        "Screen":        make_mat("Screen",        (0.08, 0.78, 0.98), metallic=0.0,  roughness=0.12, emission=(0.08, 0.78, 0.98)),
    }

    # =========================================================================
    # 2. CHASSIS & CLOSED SOLID BODY PANELS (2-Box Performance Hot-Hatch)
    #    Wheelbase: 2.64 m (front z = +1.32, rear z = -1.32, midpoint = 0.00)
    #    Length: 4.30 m (-2.15 .. +2.15), Body Width (no mirrors): 1.86 m, Height: 1.45 m
    # =========================================================================
    p_chassis = PartBuilder("chassis_underbody", [mats["Trim_Dark"], mats["Metal_Chrome"]])
    p_chassis.add_box((-0.78, 0.13, -1.96), (0.78, 0.24, 1.96), mat_idx=0)
    p_chassis.add_box((-0.60, 0.24,  1.78), (0.60, 0.42, 1.98), mat_idx=1)
    p_chassis.add_box((-0.62, 0.24, -1.98), (0.62, 0.44, -1.78), mat_idx=1)
    p_chassis.build(parent=root, pivot=(0.0, 0.19, 0.0), visible=True)

    for side in (-1, 1):
        s_name = "left" if side < 0 else "right"
        sx = float(side)

        def orient_quad(q):
            return q if side > 0 else [q[0], q[3], q[2], q[1]]

        # Front Fender (Crisp hot-hatch shoulder crease y = 0.70..0.88, wheel arch z = 0.88..1.76)
        p_ff = PartBuilder(f"fender_front_{s_name}", [mats["Paint_Body"], mats["Trim_Dark"]])
        # Forward section ahead of front wheel (z = 1.76 .. 2.02)
        p_ff.add_prism(
            orient_quad([(sx * 0.50, 0.26, 1.76), (sx * 0.88, 0.26, 1.76), (sx * 0.84, 0.24, 2.02), (sx * 0.48, 0.24, 2.02)]),
            orient_quad([(sx * 0.50, 0.76, 1.76), (sx * 0.88, 0.74, 1.76), (sx * 0.82, 0.70, 2.02), (sx * 0.48, 0.70, 2.02)]),
            mat_idx=0
        )
        # Upper arch bridge over front wheel (z = 0.88 .. 1.76, clearance y >= 0.70 > front wheel top 0.66)
        p_ff.add_prism(
            orient_quad([(sx * 0.52, 0.70, 0.88), (sx * 0.93, 0.70, 0.88), (sx * 0.90, 0.68, 1.76), (sx * 0.50, 0.68, 1.76)]),
            orient_quad([(sx * 0.52, 0.86, 0.88), (sx * 0.88, 0.84, 0.88), (sx * 0.88, 0.74, 1.76), (sx * 0.50, 0.76, 1.76)]),
            mat_idx=0
        )
        # Rear section of front fender between wheel and door (z = 0.64 .. 0.88) + side wing spear badge
        p_ff.add_prism(
            orient_quad([(sx * 0.70, 0.22, 0.64), (sx * 0.90, 0.22, 0.64), (sx * 0.93, 0.50, 0.88), (sx * 0.70, 0.50, 0.88)]),
            orient_quad([(sx * 0.68, 0.88, 0.64), (sx * 0.88, 0.86, 0.64), (sx * 0.88, 0.84, 0.88), (sx * 0.68, 0.86, 0.88)]),
            mat_idx=0
        )
        # Inner wheelwell wall (at |x|=0.50 so turning front wheel at x=0.785 has >12 cm clearance)
        p_ff.add_box((sx * 0.48, 0.24, 0.88), (sx * 0.54, 0.72, 1.76), mat_idx=1)
        p_ff.build(parent=root, pivot=(sx * 0.74, 0.56, 1.32), visible=True)

        # Door (Long hatchback cabin door z = -0.62 .. +0.64, crisp tornado line at y = 0.88; pivot at front hinge)
        p_door = PartBuilder(f"door_{s_name}", [mats["Paint_Body"], mats["Interior_Dark"]])
        p_door.add_prism(
            orient_quad([(sx * 0.70, 0.22, -0.62), (sx * 0.89, 0.22, -0.62), (sx * 0.89, 0.22, 0.64), (sx * 0.70, 0.22, 0.64)]),
            orient_quad([(sx * 0.70, 0.56, -0.62), (sx * 0.91, 0.56, -0.62), (sx * 0.91, 0.54, 0.64), (sx * 0.70, 0.54, 0.64)]),
            mat_idx=0
        )
        p_door.add_prism(
            orient_quad([(sx * 0.70, 0.56, -0.62), (sx * 0.91, 0.56, -0.62), (sx * 0.91, 0.54, 0.64), (sx * 0.70, 0.54, 0.64)]),
            orient_quad([(sx * 0.68, 0.90, -0.62), (sx * 0.88, 0.88, -0.62), (sx * 0.88, 0.86, 0.64), (sx * 0.68, 0.88, 0.64)]),
            mat_idx=0
        )
        p_door.add_box((sx * 0.66, 0.26, -0.58), (sx * 0.71, 0.84, 0.60), mat_idx=1)
        p_door.build(parent=root, pivot=(sx * 0.84, 0.56, 0.64), visible=True)

        # Rear Quarter Fender (Hatchback blister wheel arch + lower rear quarter flank z = -2.04 .. -0.62)
        p_rf = PartBuilder(f"fender_rear_{s_name}", [mats["Paint_Body"], mats["Trim_Dark"]])
        # Forward section ahead of rear wheel (z = -0.88 .. -0.62)
        p_rf.add_prism(
            orient_quad([(sx * 0.70, 0.22, -0.88), (sx * 0.92, 0.22, -0.88), (sx * 0.89, 0.22, -0.62), (sx * 0.70, 0.22, -0.62)]),
            orient_quad([(sx * 0.56, 0.92, -0.88), (sx * 0.90, 0.90, -0.88), (sx * 0.88, 0.88, -0.62), (sx * 0.68, 0.90, -0.62)]),
            mat_idx=0
        )
        # Sculpted blister arch over rear wheel (z = -1.76 .. -0.88, clearance y >= 0.70 > rear wheel top 0.66)
        p_rf.add_prism(
            orient_quad([(sx * 0.54, 0.70, -1.76), (sx * 0.93, 0.70, -1.76), (sx * 0.93, 0.70, -0.88), (sx * 0.54, 0.70, -0.88)]),
            orient_quad([(sx * 0.52, 0.92, -1.76), (sx * 0.89, 0.90, -1.76), (sx * 0.90, 0.90, -0.88), (sx * 0.56, 0.92, -0.88)]),
            mat_idx=0
        )
        # Rear corner quarter panel behind rear wheel up to taillight pocket (z = -2.04 .. -1.76)
        p_rf.add_prism(
            orient_quad([(sx * 0.52, 0.28, -2.04), (sx * 0.86, 0.28, -2.04), (sx * 0.92, 0.28, -1.76), (sx * 0.54, 0.28, -1.76)]),
            orient_quad([(sx * 0.48, 0.92, -2.04), (sx * 0.84, 0.90, -2.04), (sx * 0.89, 0.90, -1.76), (sx * 0.52, 0.92, -1.76)]),
            mat_idx=0
        )
        # Inner rear wheelwell liner
        p_rf.add_box((sx * 0.50, 0.24, -1.74), (sx * 0.56, 0.70, -0.88), mat_idx=1)
        p_rf.build(parent=root, pivot=(sx * 0.76, 0.62, -1.32), visible=True)

    # Roof & 2-Box Hatchback Greenhouse (Long flat roof z = -1.62 .. +0.16, y = 1.43 m + Wide Boomerang C-Pillar!)
    p_roof = PartBuilder("roof_cabin", [mats["Paint_Body"], mats["Interior_Dark"]])
    p_roof.add_prism(
        [(-0.56, 1.35, -1.62), (0.56, 1.35, -1.62), (0.54, 1.36, 0.16), (-0.54, 1.36, 0.16)],
        [(-0.52, 1.42, -1.60), (0.52, 1.42, -1.60), (0.50, 1.43, 0.14), (-0.50, 1.43, 0.14)],
        mat_idx=0
    )
    p_roof.add_box((-0.52, 1.32, -1.56), (0.52, 1.35, 0.12), mat_idx=1)
    # Hatchback A-Pillars (z = +0.66 base at cowl back to z = +0.14 roof header)
    p_roof.add_prism(
        [(0.58, 0.86, 0.64), (0.68, 0.86, 0.64), (0.54, 1.36, 0.14), (0.46, 1.36, 0.14)],
        [(0.56, 0.92, 0.68), (0.66, 0.92, 0.68), (0.52, 1.42, 0.18), (0.44, 1.42, 0.18)],
        mat_idx=0, mirror_x=True
    )
    # B-Pillars (tapered inward to match side glass tumblehome from x=0.85 at y=0.88 to x=0.54 at y=1.37)
    p_roof.add_prism(
        [(0.78, 0.88, -0.64), (0.85, 0.88, -0.64), (0.85, 0.88, -0.52), (0.78, 0.88, -0.52)],
        [(0.48, 1.37, -0.64), (0.54, 1.37, -0.64), (0.54, 1.37, -0.52), (0.48, 1.37, -0.52)],
        mat_idx=1, mirror_x=True
    )
    # Signature Wide Forward-Leaning "Boomerang" Hatchback C-Pillar (z = -1.96 waist up to z = -1.56 roof)
    p_roof.add_prism(
        [(0.48, 0.90, -1.96), (0.84, 0.90, -1.96), (0.88, 0.90, -1.36), (0.50, 0.90, -1.36)],
        [(0.46, 1.40, -1.62), (0.56, 1.40, -1.62), (0.58, 1.40, -1.24), (0.46, 1.40, -1.24)],
        mat_idx=0, mirror_x=True
    )
    p_roof.build(parent=root, pivot=(0.0, 1.24, -0.68), visible=True)

    # Upright Hatchback Lower Tailgate (engine_decklid slot name required by spec; z = -2.08 .. -1.84, y = 0.56 .. 0.94)
    p_deck = PartBuilder("engine_decklid", [mats["Paint_Body"], mats["Trim_Dark"]])
    p_deck.add_prism(
        [(-0.56, 0.56, -2.08), (0.56, 0.56, -2.08), (0.56, 0.56, -1.86), (-0.56, 0.56, -1.86)],
        [(-0.52, 0.94, -1.98), (0.52, 0.94, -1.98), (0.52, 0.94, -1.82), (-0.52, 0.94, -1.82)],
        mat_idx=0
    )
    # Recessed license-plate & hatch handle pocket
    p_deck.add_box((-0.32, 0.62, -2.09), (0.32, 0.76, -1.96), mat_idx=1)
    p_deck.build(parent=root, pivot=(0.0, 0.75, -1.96), visible=True)

    # =========================================================================
    # 3. WINDOWS (100% Outward Normals, Single Glass_Tint Submesh, _Cull=0)
    # =========================================================================
    p_ws = PartBuilder("glass_windshield", [mats["Glass_Tint"]])
    p_ws.add_submesh(
        [(0.00, 0.87, 0.66), (0.60, 0.86, 0.64), (0.00, 1.41, 0.15), (0.48, 1.40, 0.14)],
        [(0, 1, 3, 2)], mat_idx=0, mirror_x=True
    )
    p_ws.build(parent=root, pivot=(0.0, 1.14, 0.40), visible=True, outward_normal=(0.0, 1.0, 1.0))

    # Steep Upright Hatchback Rear Tailgate Window (z = -1.60 top down to z = -1.96 tailgate waist)
    p_rw = PartBuilder("glass_rear", [mats["Glass_Tint"]])
    p_rw.add_submesh(
        [(0.00, 1.40, -1.60), (0.46, 1.39, -1.60), (0.00, 0.93, -1.97), (0.48, 0.92, -1.97)],
        [(0, 2, 3, 1)], mat_idx=0, mirror_x=True
    )
    p_rw.build(parent=root, pivot=(0.0, 1.16, -1.78), visible=True, outward_normal=(0.0, 0.6, -1.0))

    for side in (-1, 1):
        s_name = "left" if side < 0 else "right"
        sx = float(side)
        p_sw = PartBuilder(f"glass_side_{s_name}", [mats["Glass_Tint"]])
        swv = [
            (sx * 0.86, 0.88,  0.58),
            (sx * 0.86, 0.90, -1.36),
            (sx * 0.56, 1.38, -1.26),
            (sx * 0.50, 1.40,  0.14),
        ]
        swf = [(0, 1, 2, 3)] if side > 0 else [(0, 3, 2, 1)]
        p_sw.add_submesh(swv, swf, mat_idx=0)
        p_sw.build(parent=root, pivot=(sx * 0.68, 1.14, -0.38), visible=True, outward_normal=(sx, 0.4, 0.0))

    # =========================================================================
    # 4. ENCLOSED INTERIOR CABIN & COCKPIT (Modern Hot-Hatch Digital Cockpit)
    # =========================================================================
    p_cab = PartBuilder("interior_cabin", [mats["Interior_Dark"], mats["Trim_Dark"]])
    p_cab.add_box((-0.72, 0.20, -1.56), (0.72, 0.26,  0.68), mat_idx=0)  # floor
    p_cab.add_box((-0.72, 0.24,  0.56), (0.72, 0.88,  0.70), mat_idx=0)  # front firewall/cowl
    p_cab.add_box((-0.72, 0.24, -1.94), (0.72, 0.94, -1.50), mat_idx=0)  # rear hatch trunk bay & parcel shelf
    p_cab.add_box((-0.14, 0.26, -0.96), (0.14, 0.52,  0.58), mat_idx=1)  # center console
    p_cab.add_cylinder((0.0, 0.50, 0.08), (0.0, 0.66, 0.10), 0.022, 0.032, segs=8, mat_idx=1)  # golf-ball shifter
    p_cab.build(parent=root, pivot=(0.0, 0.52, -0.36), visible=True)

    p_dash = PartBuilder("interior_dash", [mats["Interior_Dark"], mats["Screen"]])
    p_dash.add_box((-0.70, 0.62, 0.28), (0.70, 0.86, 0.64), mat_idx=0)
    p_dash.add_box((-0.54, 0.82, 0.26), (-0.16, 0.93, 0.50), mat_idx=0)
    p_dash.add_cylinder((-0.36, 0.75, 0.30), (-0.36, 0.78, 0.10), 0.044, 0.038, segs=8, mat_idx=0)
    # Dual digital gauge cluster + angled infotainment screen (Screen material)
    p_dash.add_box((-0.50, 0.78, 0.27), (0.16, 0.90, 0.31), mat_idx=1)
    p_dash.build(parent=root, pivot=(0.0, 0.76, 0.42), visible=True)

    p_seat = PartBuilder("interior_seat_driver", [mats["Interior_Dark"], mats["Paint_Accent"]])
    p_seat.add_box((-0.56, 0.26, -0.54), (-0.16, 0.38, -0.02), mat_idx=0)
    p_seat.add_box((-0.54, 0.36, -0.60), (-0.18, 0.96, -0.46), mat_idx=0)
    p_seat.add_box((-0.58, 0.38, -0.52), (-0.50, 0.84, -0.18), mat_idx=1)
    p_seat.add_box((-0.22, 0.38, -0.52), (-0.14, 0.84, -0.18), mat_idx=1)
    p_seat.add_box((-0.48, 0.96, -0.58), (-0.24, 1.14, -0.46), mat_idx=1)
    p_seat.build(parent=root, pivot=(-0.36, 0.64, -0.32), visible=True)

    # =========================================================================
    # 5. INTERIOR MOD SLOTS (steering, interior_passenger, interior_pillar, rollcage)
    # =========================================================================
    sw_pivot = (-0.36, 0.78, 0.10)
    for idx_s, (s_name, r_rim, is_yoke) in enumerate((
        ("mod_steering_0_stock",      0.165, False),
        ("mod_steering_1_deep_dish",  0.158, False),
        ("mod_steering_2_track_yoke", 0.162, True),
    )):
        ps = PartBuilder(s_name, [mats["Interior_Dark"], mats["Paint_Accent"]])
        ps.add_cylinder((-0.36, 0.77, 0.13), (-0.36, 0.78, 0.07), 0.046, 0.046, segs=10, mat_idx=0)
        if is_yoke:
            ps.add_box((-0.52, 0.70, 0.06), (-0.20, 0.74, 0.10), mat_idx=0)
            ps.add_box((-0.53, 0.70, 0.05), (-0.48, 0.87, 0.10), mat_idx=1)
            ps.add_box((-0.24, 0.70, 0.05), (-0.19, 0.87, 0.10), mat_idx=1)
            ps.add_box((-0.41, 0.77, 0.05), (-0.31, 0.81, 0.10), mat_idx=1)
        else:
            segs = 12
            for i in range(segs):
                a0 = 2.0 * math.pi * i / segs
                a1 = 2.0 * math.pi * (i + 1) / segs
                p0 = (-0.36 + math.cos(a0) * r_rim, 0.78 + math.sin(a0) * r_rim, 0.08)
                p1 = (-0.36 + math.cos(a1) * r_rim, 0.78 + math.sin(a1) * r_rim, 0.08)
                ps.add_cylinder(p0, p1, 0.017, 0.017, segs=6, mat_idx=(1 if i in (2, 3) else 0), cap=False)
            ps.add_box((-0.51, 0.76, 0.07), (-0.21, 0.80, 0.09), mat_idx=0)
            ps.add_box((-0.38, 0.62, 0.07), (-0.34, 0.78, 0.09), mat_idx=1)
        ps.build(parent=root, pivot=sw_pivot, visible=(idx_s == 0))

    pass_pivot = (0.36, 0.54, -0.32)
    pp0 = PartBuilder("mod_interior_passenger_0_seat", [mats["Interior_Dark"], mats["Paint_Accent"]])
    pp0.add_box((0.16, 0.26, -0.54), (0.56, 0.38, -0.02), mat_idx=0)
    pp0.add_box((0.18, 0.36, -0.60), (0.54, 0.96, -0.46), mat_idx=0)
    pp0.add_box((0.50, 0.38, -0.52), (0.58, 0.84, -0.18), mat_idx=1)
    pp0.add_box((0.14, 0.38, -0.52), (0.22, 0.84, -0.18), mat_idx=1)
    pp0.add_box((0.24, 0.96, -0.58), (0.48, 1.14, -0.46), mat_idx=1)
    pp0.build(parent=root, pivot=pass_pivot, visible=True)

    pp1 = PartBuilder("mod_interior_passenger_1_twin_nos", [mats["Paint_Accent"], mats["Metal_Chrome"]])
    pp1.add_box((0.16, 0.26, -0.52), (0.56, 0.32, -0.04), mat_idx=1)
    for bx in (0.26, 0.46):
        pp1.add_cylinder((bx, 0.38, -0.50), (bx, 0.46, -0.06), 0.072, 0.072, segs=10, mat_idx=0)
        pp1.add_cylinder((bx, 0.46, -0.06), (bx, 0.48,  0.02), 0.026, 0.020, segs=8, mat_idx=1)
    pp1.build(parent=root, pivot=pass_pivot, visible=False)

    pil_pivot = (-0.54, 1.10, 0.40)
    pil0 = PartBuilder("mod_interior_pillar_0_stock", [mats["Interior_Dark"]])
    pil0.add_prism(
        [(-0.66, 0.86, 0.62), (-0.58, 0.86, 0.62), (-0.52, 1.32, 0.16), (-0.56, 1.32, 0.16)],
        [(-0.64, 0.90, 0.64), (-0.56, 0.90, 0.64), (-0.50, 1.36, 0.18), (-0.54, 1.36, 0.18)],
        mat_idx=0
    )
    pil0.build(parent=root, pivot=pil_pivot, visible=True)

    pil1 = PartBuilder("mod_interior_pillar_1_triple_gauges", [mats["Interior_Dark"], mats["Screen"]])
    pil1.add_prism(
        [(-0.66, 0.86, 0.62), (-0.58, 0.86, 0.62), (-0.52, 1.32, 0.16), (-0.56, 1.32, 0.16)],
        [(-0.64, 0.90, 0.64), (-0.56, 0.90, 0.64), (-0.50, 1.36, 0.18), (-0.54, 1.36, 0.18)],
        mat_idx=0
    )
    for gi in range(3):
        t = 0.22 + gi * 0.22
        gx = -0.60 + t * 0.08
        gy = 0.90 + t * 0.38
        gz = 0.58 - t * 0.38
        pil1.add_cylinder((gx, gy, gz), (gx + 0.03, gy - 0.01, gz - 0.03), 0.024, 0.024, segs=8, mat_idx=0)
        pil1.add_cylinder((gx + 0.03, gy - 0.01, gz - 0.03), (gx + 0.035, gy - 0.012, gz - 0.035), 0.020, 0.020, segs=8, mat_idx=1)
    pil1.build(parent=root, pivot=pil_pivot, visible=False)

    rc_pivot = (0.0, 0.82, -0.80)
    add_empty("mod_rollcage_0_none", rc_pivot, parent=root, forward_plus_z=True, visible=True)

    prc1 = PartBuilder("mod_rollcage_1_street_half", [mats["Paint_Accent"]])
    prc1.add_cylinder((0.52, 0.28, -0.68), (0.48, 1.30, -0.66), 0.024, 0.024, segs=6, mat_idx=0, mirror_x=True)
    prc1.add_cylinder((-0.48, 1.30, -0.66), (0.48, 1.30, -0.66), 0.024, 0.024, segs=6, mat_idx=0)
    prc1.add_cylinder((-0.48, 1.30, -0.66), (0.52, 0.28, -0.68), 0.020, 0.020, segs=6, mat_idx=0)
    prc1.add_cylinder((0.48, 1.30, -0.66), (-0.52, 0.28, -0.68), 0.020, 0.020, segs=6, mat_idx=0)
    prc1.build(parent=root, pivot=rc_pivot, visible=False)

    prc2 = PartBuilder("mod_rollcage_2_full_fia", [mats["Paint_Accent"]])
    prc2.add_cylinder((0.52, 0.28, -0.68), (0.48, 1.30, -0.66), 0.024, 0.024, segs=6, mat_idx=0, mirror_x=True)
    prc2.add_cylinder((-0.48, 1.30, -0.66), (0.48, 1.30, -0.66), 0.024, 0.024, segs=6, mat_idx=0)
    prc2.add_cylinder((-0.48, 1.30, -0.66), (0.52, 0.28, -0.68), 0.020, 0.020, segs=6, mat_idx=0)
    prc2.add_cylinder((0.48, 1.30, -0.66), (0.46, 1.32,  0.12), 0.022, 0.022, segs=6, mat_idx=0, mirror_x=True)
    prc2.add_cylinder((0.48, 1.30, -0.66), (0.46, 0.90, -1.52), 0.020, 0.020, segs=6, mat_idx=0, mirror_x=True)
    prc2.build(parent=root, pivot=rc_pivot, visible=False)

    # =========================================================================
    # 6. FRUNK / UNDER-HOOD TRANSVERSE TURBO 4-CYL ENGINE BAY SLOT
    # =========================================================================
    frunk_pivot = (0.0, 0.50, 1.32)
    pfr0 = PartBuilder("mod_frunk_0_stock", [mats["Trim_Dark"], mats["Metal_Chrome"]])
    pfr0.add_box((-0.44, 0.28, 0.88), (0.44, 0.62, 1.66), mat_idx=0)
    pfr0.add_box((-0.34, 0.62, 1.04), (0.34, 0.72, 1.50), mat_idx=1)
    pfr0.build(parent=root, pivot=frunk_pivot, visible=True)

    pfr1 = PartBuilder("mod_frunk_1_custom_audio", [mats["Interior_Dark"], mats["Neon"]])
    pfr1.add_box((-0.46, 0.28, 0.88), (0.46, 0.62, 1.66), mat_idx=0)
    pfr1.add_cylinder((-0.22, 0.60, 1.26), (-0.22, 0.72, 1.28), 0.13, 0.10, segs=10, mat_idx=1)
    pfr1.add_cylinder(( 0.22, 0.60, 1.26), ( 0.22, 0.72, 1.28), 0.13, 0.10, segs=10, mat_idx=1)
    pfr1.build(parent=root, pivot=frunk_pivot, visible=False)

    # =========================================================================
    # 7. COMPACT SLOPING HOT-HATCH HOOD VARIANTS (mod_hood_0_stock .. 4_carbon)
    #    Spans z = +0.64 (windshield cowl) to z = +2.02 (front nose header)
    # =========================================================================
    hood_pivot = (0.0, 0.78, 1.34)
    for idx_h, (h_name, h_style) in enumerate((
        ("mod_hood_0_stock",  "stock"),
        ("mod_hood_1_street", "street"),
        ("mod_hood_2_vented", "vented"),
        ("mod_hood_3_track",  "track"),
        ("mod_hood_4_carbon", "carbon"),
    )):
        h_mat0 = mats["Carbon"] if h_style == "carbon" else mats["Paint_Body"]
        ph = PartBuilder(h_name, [h_mat0, mats["Trim_Dark"]])
        # Main sloping hot-hatch hood slab (z = 0.64 .. 2.02)
        ph.add_prism(
            [(-0.52, 0.74, 1.38), (0.52, 0.74, 1.38), (0.48, 0.64, 2.02), (-0.48, 0.64, 2.02)],
            [(-0.52, 0.80, 1.38), (0.52, 0.80, 1.38), (0.48, 0.70, 2.02), (-0.48, 0.70, 2.02)],
            mat_idx=0
        )
        ph.add_prism(
            [(-0.54, 0.82, 0.64), (0.54, 0.82, 0.64), (0.52, 0.74, 1.38), (-0.52, 0.74, 1.38)],
            [(-0.54, 0.88, 0.64), (0.54, 0.88, 0.64), (0.52, 0.80, 1.38), (-0.52, 0.80, 1.38)],
            mat_idx=0
        )
        # Crisp twin outer hood character ridges
        ph.add_prism(
            [(0.28, 0.78, 0.72), (0.42, 0.78, 0.72), (0.38, 0.69, 1.92), (0.26, 0.69, 1.92)],
            [(0.30, 0.84, 0.72), (0.40, 0.84, 0.72), (0.36, 0.73, 1.92), (0.28, 0.73, 1.92)],
            mat_idx=0, mirror_x=True
        )

        if h_style in ("street", "track", "carbon"):
            # Central turbo intercooler extractor duct
            ph.add_box((-0.24, 0.74, 1.42), (0.24, 0.81, 1.76), mat_idx=1)
        if h_style in ("vented", "track", "carbon"):
            for vz in (1.02, 1.18, 1.34, 1.50):
                ph.add_box((0.24, 0.79, vz), (0.44, 0.86, vz + 0.08), mat_idx=1, mirror_x=True)
        if h_style == "carbon":
            ph.add_cylinder((0.34, 0.70, 1.92), (0.34, 0.73, 1.92), 0.028, 0.028, segs=8, mat_idx=1, mirror_x=True)

        ph.build(parent=root, pivot=hood_pivot, visible=(idx_h == 0))

    # =========================================================================
    # 8. FENDER ADD-ON VARIANTS (mod_fender_0_stock .. 2_widebody)
    # =========================================================================
    fend_pivot = (0.0, 0.62, 0.0)
    pf0 = PartBuilder("mod_fender_0_stock", [mats["Paint_Accent"]])
    # Signature hot-hatch side fender spear badge at A-pillar base
    pf0.add_box((0.885, 0.74, 0.66), (0.910, 0.79, 0.86), mat_idx=0, mirror_x=True)
    pf0.build(parent=root, pivot=fend_pivot, visible=True)

    pf1 = PartBuilder("mod_fender_1_louvers", [mats["Carbon"]])
    for li in range(4):
        lz = 1.14 + li * 0.11
        pf1.add_box((0.62, 0.80, lz), (0.86, 0.87, lz + 0.055), mat_idx=0, mirror_x=True)
    for li in range(4):
        lz = -1.52 + li * 0.11
        pf1.add_box((0.62, 0.88, lz), (0.86, 0.94, lz + 0.055), mat_idx=0, mirror_x=True)
    pf1.build(parent=root, pivot=fend_pivot, visible=False)

    pf2 = PartBuilder("mod_fender_2_widebody", [mats["Paint_Body"], mats["Carbon"]])
    # Touring-car box-flare widebody arches
    pf2.add_box((0.86, 0.26,  0.88), (0.99, 0.84,  1.76), mat_idx=0, mirror_x=True)
    pf2.add_box((0.86, 0.28, -1.76), (1.00, 0.90, -0.88), mat_idx=0, mirror_x=True)
    for li in range(4):
        lz = 1.14 + li * 0.11
        pf2.add_box((0.64, 0.80, lz), (0.88, 0.88, lz + 0.055), mat_idx=1, mirror_x=True)
    pf2.build(parent=root, pivot=fend_pivot, visible=False)

    # =========================================================================
    # 9. FRONT BUMPERS (mod_bumper_front_0_stock .. 4_timeattack)
    #    Slim Upper Radiator Slot + Wide Lower Honeycomb Intake + Side C-Strake Gills
    # =========================================================================
    fb_pivot = (0.0, 0.38, 2.02)
    for idx_b, (b_name, lip_ext, canards, rods) in enumerate((
        ("mod_bumper_front_0_stock",      0.02, 0, False),
        ("mod_bumper_front_1_street",     0.07, 0, False),
        ("mod_bumper_front_2_race",       0.12, 1, False),
        ("mod_bumper_front_3_track",      0.16, 2, False),
        ("mod_bumper_front_4_timeattack", 0.22, 2, True),
    )):
        pfb = PartBuilder(b_name, [mats["Paint_Body"], mats["Trim_Dark"]])
        # Lower full-width bumper fascia (up to y = 0.55 so outer corners leave clean pocket for headlights)
        pfb.add_prism(
            [(-0.84, 0.16, 1.94), (0.84, 0.16, 1.94), (0.76, 0.16, 2.13), (-0.76, 0.16, 2.13)],
            [(-0.82, 0.55, 1.94), (0.82, 0.55, 1.94), (0.74, 0.55, 2.12), (-0.74, 0.55, 2.12)],
            mat_idx=0
        )
        # Central upper radiator grille bridge between headlights (x = -0.48 .. +0.48, y = 0.55 .. 0.68)
        pfb.add_prism(
            [(-0.48, 0.55, 1.94), (0.48, 0.55, 1.94), (0.46, 0.55, 2.12), (-0.46, 0.55, 2.12)],
            [(-0.48, 0.68, 1.94), (0.48, 0.68, 1.94), (0.46, 0.66, 2.10), (-0.46, 0.66, 2.10)],
            mat_idx=0
        )
        # Slim upper hot-hatch grille slot
        pfb.add_box((-0.46, 0.58, 2.02), (0.46, 0.65, 2.13), mat_idx=1)
        # Wide lower trapezoidal honeycomb-style central intake + side C-strake air curtains
        pfb.add_box((-0.52, 0.18, 2.00), (0.52, 0.44, 2.135), mat_idx=1)
        pfb.add_box(( 0.56, 0.20, 1.98), (0.78, 0.46, 2.125), mat_idx=1, mirror_x=True)
        # Body-colored horizontal C-strakes inside side intakes
        pfb.add_box(( 0.58, 0.31, 2.02), (0.77, 0.35, 2.132), mat_idx=0, mirror_x=True)
        # Front chin splitter
        pfb.add_prism(
            [(-0.86, 0.13, 1.90), (0.86, 0.13, 1.90), (0.80, 0.13, 2.13 + lip_ext), (-0.80, 0.13, 2.13 + lip_ext)],
            [(-0.86, 0.17, 1.90), (0.86, 0.17, 1.90), (0.80, 0.17, 2.13 + lip_ext), (-0.80, 0.17, 2.13 + lip_ext)],
            mat_idx=1
        )
        if canards >= 1:
            pfb.add_box((0.74, 0.24, 1.98), (0.91, 0.28, 2.12), mat_idx=1, mirror_x=True)
        if canards >= 2:
            pfb.add_box((0.72, 0.36, 1.96), (0.92, 0.40, 2.10), mat_idx=1, mirror_x=True)
        if rods:
            pfb.add_cylinder((0.34, 0.15, 2.13 + lip_ext * 0.8), (0.34, 0.38, 2.08), 0.010, 0.010, segs=6, mat_idx=1, mirror_x=True)
        pfb.build(parent=root, pivot=fb_pivot, visible=(idx_b == 0))

    # =========================================================================
    # 10. REAR BUMPERS (mod_bumper_rear_0_stock .. 3_extreme)
    # =========================================================================
    rb_pivot = (0.0, 0.36, -2.04)
    for idx_rb, (rb_name, diff_ext, n_fins) in enumerate((
        ("mod_bumper_rear_0_stock",   0.02, 2),
        ("mod_bumper_rear_1_street",  0.06, 3),
        ("mod_bumper_rear_2_track",   0.10, 4),
        ("mod_bumper_rear_3_extreme", 0.16, 5),
    )):
        prb = PartBuilder(rb_name, [mats["Paint_Body"], mats["Trim_Dark"]])
        prb.add_prism(
            [(-0.84, 0.18, -2.13), (0.84, 0.18, -2.13), (0.88, 0.18, -1.92), (-0.88, 0.18, -1.92)],
            [(-0.82, 0.60, -2.13), (0.82, 0.60, -2.13), (0.86, 0.60, -1.92), (-0.86, 0.60, -1.92)],
            mat_idx=0
        )
        # Lower hot-hatch diffuser valence
        prb.add_box((-0.70, 0.14, -2.13 - diff_ext), (0.70, 0.34, -1.92), mat_idx=1)
        for fi in range(n_fins):
            fx = 0.09 + fi * 0.12
            prb.add_box((fx, 0.12, -2.14 - diff_ext), (fx + 0.024, 0.32, -1.96), mat_idx=1, mirror_x=True)
        prb.build(parent=root, pivot=rb_pivot, visible=(idx_rb == 0))

    # =========================================================================
    # 11. SIDE SKIRTS (Left & Right Separate Slots: mod_skirts_0_stock_left/right .. 3_wide)
    # =========================================================================
    for side in (-1, 1):
        s_tag = "left" if side < 0 else "right"
        sx = float(side)
        sk_pivot = (sx * 0.86, 0.18, 0.0)
        for idx_sk, (sk_sub, w_ext, fin_h) in enumerate((
            ("0_stock",  0.00, 0.00),
            ("1_street", 0.03, 0.04),
            ("2_aero",   0.05, 0.09),
            ("3_wide",   0.08, 0.14),
        )):
            psk = PartBuilder(f"mod_skirts_{sk_sub}_{s_tag}", [mats["Paint_Body"], mats["Carbon"]])
            x_in = min(sx * 0.72, sx * (0.90 + w_ext))
            x_out = max(sx * 0.72, sx * (0.90 + w_ext))
            psk.add_box((x_in, 0.14, -0.88), (x_out, 0.24, 0.88), mat_idx=0)
            psk.add_box((min(sx * 0.84, sx * (0.92 + w_ext)), 0.11, -0.88), (max(sx * 0.84, sx * (0.92 + w_ext)), 0.15, 0.88), mat_idx=1)
            if fin_h > 0.0:
                fx0 = min(sx * (0.89 + w_ext), sx * (0.92 + w_ext))
                fx1 = max(sx * (0.89 + w_ext), sx * (0.92 + w_ext))
                psk.add_box((fx0, 0.12, -0.88), (fx1, 0.16 + fin_h, -0.66), mat_idx=1)
            psk.build(parent=root, pivot=sk_pivot, visible=(idx_sk == 0))

    # =========================================================================
    # 12. ROOF-MOUNTED HATCHBACK SPOILERS (mod_spoiler_0_stock .. 5_timeattack)
    #     Mounted at the rear edge of the 2-box hatchback roof (y = 1.40 .. 1.66, z = -1.62 .. -2.02)
    # =========================================================================
    sp_pivot = (0.0, 1.42, -1.72)

    # 0: Factory Hot-Hatch Roof-Edge Lip Spoiler
    ps0 = PartBuilder("mod_spoiler_0_stock", [mats["Paint_Body"], mats["Trim_Dark"]])
    ps0.add_prism(
        [(-0.56, 1.38, -1.82), (0.56, 1.38, -1.82), (0.54, 1.39, -1.58), (-0.54, 1.39, -1.58)],
        [(-0.54, 1.45, -1.84), (0.54, 1.45, -1.84), (0.52, 1.43, -1.58), (-0.52, 1.43, -1.58)],
        mat_idx=0
    )
    ps0.add_box((0.48, 1.18, -1.84), (0.54, 1.40, -1.68), mat_idx=1, mirror_x=True)
    ps0.build(parent=root, pivot=sp_pivot, visible=True)

    # 1: Clubsport Extended Roof Ducktail Lip
    ps1 = PartBuilder("mod_spoiler_1_ducktail", [mats["Carbon"], mats["Paint_Accent"]])
    ps1.add_prism(
        [(-0.60, 1.38, -1.90), (0.60, 1.38, -1.90), (0.54, 1.39, -1.58), (-0.54, 1.39, -1.58)],
        [(-0.58, 1.52, -1.94), (0.58, 1.52, -1.94), (0.52, 1.43, -1.58), (-0.52, 1.43, -1.58)],
        mat_idx=0
    )
    ps1.add_box((-0.58, 1.50, -1.95), (0.58, 1.53, -1.88), mat_idx=1)
    ps1.build(parent=root, pivot=sp_pivot, visible=False)

    # 2..5: Roof-Mounted Aero Wings (Street Tunnel, Touring Trackwing, Swan-Neck, Time-Attack Dual-Deck)
    for idx_w, (w_name, w_span, w_h, w_z, swan, dual) in enumerate((
        ("mod_spoiler_2_street",     0.66, 1.52, -1.82, False, False),
        ("mod_spoiler_3_trackwing",  0.78, 1.58, -1.88, False, False),
        ("mod_spoiler_4_swanneck",   0.82, 1.61, -1.92, True,  False),
        ("mod_spoiler_5_timeattack", 0.88, 1.63, -1.96, True,  True),
    ), start=2):
        pw = PartBuilder(w_name, [mats["Carbon"], mats["Paint_Accent"]])
        st_x = 0.32 if swan else 0.40
        pw.add_box((st_x - 0.02, 1.38, w_z - 0.06), (st_x + 0.02, w_h, w_z + 0.14), mat_idx=0, mirror_x=True)
        if swan:
            pw.add_box((st_x - 0.02, w_h - 0.01, w_z - 0.08), (st_x + 0.02, w_h + 0.04, w_z + 0.08), mat_idx=0, mirror_x=True)
        pw.add_prism(
            [(-w_span, w_h - 0.02, w_z - 0.12), (w_span, w_h - 0.02, w_z - 0.12), (w_span, w_h - 0.04, w_z + 0.10), (-w_span, w_h - 0.04, w_z + 0.10)],
            [(-w_span, w_h + 0.02, w_z - 0.12), (w_span, w_h + 0.02, w_z - 0.12), (w_span, w_h,        w_z + 0.10), (-w_span, w_h,        w_z + 0.10)],
            mat_idx=0
        )
        if dual:
            pw.add_box((-w_span, w_h + 0.05, w_z - 0.14), (w_span, w_h + 0.08, w_z + 0.02), mat_idx=0)
        pw.add_box((w_span - 0.02, w_h - 0.10, w_z - 0.16), (w_span + 0.01, w_h + (0.10 if dual else 0.06), w_z + 0.12), mat_idx=1, mirror_x=True)
        pw.build(parent=root, pivot=sp_pivot, visible=False)

    # =========================================================================
    # 13. ROOF SCOOPS & MIRRORS
    # =========================================================================
    rs_pivot = (0.0, 1.42, -0.32)
    add_empty("mod_roof_scoop_0_none", rs_pivot, parent=root, forward_plus_z=True, visible=True)

    prs1 = PartBuilder("mod_roof_scoop_1_single", [mats["Paint_Body"], mats["Trim_Dark"]])
    prs1.add_box((-0.14, 1.41, -0.52), (0.14, 1.50, -0.10), mat_idx=0)
    prs1.add_box((-0.11, 1.42, -0.12), (0.11, 1.48, -0.08), mat_idx=1)
    prs1.build(parent=root, pivot=rs_pivot, visible=False)

    prs2 = PartBuilder("mod_roof_scoop_2_dual", [mats["Carbon"], mats["Trim_Dark"]])
    prs2.add_box((0.03, 1.41, -0.54), (0.20, 1.50, -0.10), mat_idx=0, mirror_x=True)
    prs2.add_box((0.05, 1.42, -0.12), (0.18, 1.48, -0.08), mat_idx=1, mirror_x=True)
    prs2.build(parent=root, pivot=rs_pivot, visible=False)

    prs3 = PartBuilder("mod_roof_scoop_3_race", [mats["Carbon"], mats["Paint_Accent"]])
    prs3.add_box((-0.16, 1.41, -0.82), (0.16, 1.52, -0.08), mat_idx=0)
    prs3.add_box((-0.02, 1.50, -0.86), (0.02, 1.60, -0.22), mat_idx=1)
    prs3.build(parent=root, pivot=rs_pivot, visible=False)

    mir_pivot = (0.0, 0.92, 0.44)
    for idx_m, (m_name, m_mat, m_span) in enumerate((
        ("mod_mirror_0_stock",     mats["Paint_Body"], 1.00),
        ("mod_mirror_1_aero",      mats["Paint_Body"], 1.02),
        ("mod_mirror_2_carbon_f1", mats["Carbon"],     0.98),
    )):
        pm = PartBuilder(m_name, [m_mat, mats["Trim_Dark"]])
        pm.add_box((0.84, 0.86, 0.42), (0.91, 0.93, 0.50), mat_idx=1, mirror_x=True)
        pm.add_box((0.89, 0.90, 0.36), (m_span, 0.98, 0.52), mat_idx=0, mirror_x=True)
        pm.build(parent=root, pivot=mir_pivot, visible=(idx_m == 0))

    # =========================================================================
    # 14. HEADLIGHTS, TAILLIGHTS, EXHAUSTS & NEON
    #     CRITICAL RULE (Section 6.1):
    #     Headlight housing (Trim_Dark) front z = 2.110, top y = 0.700
    #     Headlight lens (Light_Head) front z = 2.118 (>= 2.110 + 0.005!), top y = 0.688 (<= 0.700!)
    # =========================================================================
    hl_pivot = (0.0, 0.63, 2.02)
    for idx_hl, hl_name in enumerate((
        "mod_headlight_0_stock",
        "mod_headlight_1_amber",
        "mod_headlight_2_halo",
        "mod_headlight_3_rally",
    )):
        phl = PartBuilder(hl_name, [mats["Trim_Dark"], mats["Light_Head"]])
        # Housing frame: front face at z = 2.110, top face at y = 0.700
        phl.add_box((0.48, 0.550, 1.900), (0.80, 0.700, 2.110), mat_idx=0, mirror_x=True)
        # Signature Dual-U / Twin-Hex LED DRL + full-width grille light bar: front face at z = 2.118, top face at y = 0.688
        for u_x in (0.51, 0.65):
            # Bottom bar of U
            phl.add_box((u_x, 0.570, 1.940), (u_x + 0.11, 0.600, 2.118), mat_idx=1, mirror_x=True)
            # Inner & outer vertical legs of U
            phl.add_box((u_x,        0.595, 1.940), (u_x + 0.028, 0.688, 2.118), mat_idx=1, mirror_x=True)
            phl.add_box((u_x + 0.082, 0.595, 1.940), (u_x + 0.11,  0.688, 2.118), mat_idx=1, mirror_x=True)
        if idx_hl in (1, 2):
            # Full-width illuminated grille light bar across upper radiator slot
            phl.add_box((-0.46, 0.625, 2.040), (0.46, 0.655, 2.118), mat_idx=1)
        if idx_hl == 3:
            phl.add_cylinder((0.32, 0.56, 2.08), (0.32, 0.56, 2.122), 0.062, 0.062, segs=10, mat_idx=1, mirror_x=True)
        phl.build(parent=root, pivot=hl_pivot, visible=(idx_hl == 0))

    # Signature Horizontal L-Block Hatchback LED Taillights
    tl_pivot = (0.0, 0.85, -2.04)
    for idx_tl, tl_name in enumerate((
        "mod_taillight_0_stock",
        "mod_taillight_1_led_bar",
        "mod_taillight_2_smoked_quad",
    )):
        ptl = PartBuilder(tl_name, [mats["Trim_Dark"], mats["Light_Tail"]])
        ptl.add_box((0.42, 0.78, -2.080), (0.82, 0.92, -1.940), mat_idx=0, mirror_x=True)
        if idx_tl == 0:
            # Twin horizontal L-block LED bars per side
            ptl.add_box((0.44, 0.855, -2.092), (0.80, 0.905, -1.960), mat_idx=1, mirror_x=True)
            ptl.add_box((0.44, 0.795, -2.092), (0.78, 0.835, -1.960), mat_idx=1, mirror_x=True)
        elif idx_tl == 1:
            ptl.add_box((-0.80, 0.82, -2.092), (0.80, 0.89, -1.960), mat_idx=1)
        else:
            for cx in (0.50, 0.70):
                ptl.add_cylinder((cx, 0.85, -1.98), (cx, 0.85, -2.095), 0.045, 0.045, segs=10, mat_idx=1, mirror_x=True)
        ptl.build(parent=root, pivot=tl_pivot, visible=(idx_tl == 0))

    ex_pivot = (0.0, 0.25, -2.12)
    for idx_ex, ex_name in enumerate((
        "mod_exhaust_0_stock",
        "mod_exhaust_1_dual_titanium",
        "mod_exhaust_2_center_race",
        "mod_exhaust_3_quad_extreme",
    )):
        pex = PartBuilder(ex_name, [mats["Metal_Chrome"], mats["Trim_Dark"]])
        if idx_ex == 0:
            # Classic Hot-Hatch Quad Tips (2 per side)
            for cx in (0.44, 0.56):
                pex.add_cylinder((cx, 0.25, -1.96), (cx, 0.25, -2.15), 0.044, 0.044, segs=10, mat_idx=0, mirror_x=True)
        elif idx_ex == 1:
            pex.add_cylinder((0.50, 0.25, -1.96), (0.50, 0.25, -2.17), 0.064, 0.064, segs=12, mat_idx=0, mirror_x=True)
        elif idx_ex == 2:
            # Center twin race pipes
            for cx in (0.08, 0.20):
                pex.add_cylinder((cx, 0.25, -1.96), (cx, 0.25, -2.17), 0.048, 0.048, segs=10, mat_idx=0, mirror_x=True)
        else:
            for cx in (0.42, 0.56):
                pex.add_cylinder((cx, 0.25, -1.96), (cx, 0.25, -2.19), 0.052, 0.052, segs=10, mat_idx=0, mirror_x=True)
        pex.build(parent=root, pivot=ex_pivot, visible=(idx_ex == 0))

    neon_pivot = (0.0, 0.10, 0.0)
    add_empty("mod_neon_0_none", neon_pivot, parent=root, forward_plus_z=True, visible=True)

    pn1 = PartBuilder("mod_neon_1_sides", [mats["Neon"]])
    pn1.add_box((0.80, 0.09, -0.80), (0.84, 0.11, 0.80), mat_idx=0, mirror_x=True)
    pn1.build(parent=root, pivot=neon_pivot, visible=False)

    pn2 = PartBuilder("mod_neon_2_full", [mats["Neon"]])
    pn2.add_box((0.80, 0.09, -0.80), (0.84, 0.11, 0.80), mat_idx=0, mirror_x=True)
    pn2.add_box((-0.54, 0.09,  1.92), (0.54, 0.11,  1.98), mat_idx=0)
    pn2.add_box((-0.54, 0.09, -2.02), (0.54, 0.11, -1.96), mat_idx=0)
    pn2.build(parent=root, pivot=neon_pivot, visible=False)

    # =========================================================================
    # 15. DECALS / STICKERS (decal_1 .. decal_5, All Hidden by Default, Outward Quads)
    # =========================================================================
    pdec1 = PartBuilder("decal_1_center_stripes", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec1.add_submesh(
        [(0.04, 0.884, 0.66), (0.14, 0.884, 0.66), (0.14, 0.704, 1.98), (0.04, 0.704, 1.98)],
        [(0, 1, 2, 3)], mat_idx=0, mirror_x=True
    )
    pdec1.add_submesh(
        [(0.04, 1.424, -1.58), (0.14, 1.424, -1.58), (0.14, 1.434, 0.12), (0.04, 1.434, 0.12)],
        [(0, 1, 2, 3)], mat_idx=0, mirror_x=True
    )
    pdec1.add_submesh(
        [(0.145, 0.884, 0.66), (0.165, 0.884, 0.66), (0.165, 0.704, 1.98), (0.145, 0.704, 1.98)],
        [(0, 1, 2, 3)], mat_idx=1, mirror_x=True
    )
    pdec1.build(parent=root, pivot=(0.0, 1.02, 0.30), visible=False, outward_normal=(0.0, 1.0, 0.2))

    pdec2 = PartBuilder("decal_2_side_rocker", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec2.add_submesh(
        [(0.914, 0.26, -0.58), (0.914, 0.26, 0.58), (0.914, 0.34, 0.58), (0.914, 0.34, -0.58)],
        [(0, 1, 2, 3)], mat_idx=0, mirror_x=True
    )
    pdec2.add_submesh(
        [(0.914, 0.345, -0.58), (0.914, 0.345, 0.58), (0.914, 0.37, 0.58), (0.914, 0.37, -0.58)],
        [(0, 1, 2, 3)], mat_idx=1, mirror_x=True
    )
    pdec2.build(parent=root, pivot=(0.0, 0.31, 0.0), visible=False, outward_normal="radial_x")

    pdec3 = PartBuilder("decal_3_number_plates", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec3.add_submesh(
        [(0.918, 0.42, -0.24), (0.918, 0.42, 0.28), (0.918, 0.74, 0.28), (0.918, 0.74, -0.24)],
        [(0, 1, 2, 3)], mat_idx=0, mirror_x=True
    )
    pdec3.add_submesh(
        [(0.922, 0.58, -0.14), (0.922, 0.58, 0.18), (0.922, 0.64, 0.18), (0.922, 0.64, -0.14)],
        [(0, 1, 2, 3)], mat_idx=1, mirror_x=True
    )
    pdec3.build(parent=root, pivot=(0.0, 0.58, 0.02), visible=False, outward_normal="radial_x")

    pdec4 = PartBuilder("decal_4_side_slashes", [mats["Paint_Accent"], mats["Decal_Graphic"]])
    pdec4.add_submesh(
        [(0.935, 0.60, -1.56), (0.935, 0.60, -0.96), (0.935, 0.68, -0.96), (0.935, 0.68, -1.56)],
        [(0, 1, 2, 3)], mat_idx=0, mirror_x=True
    )
    pdec4.add_submesh(
        [(0.935, 0.48, -1.66), (0.935, 0.48, -1.06), (0.935, 0.56, -1.06), (0.935, 0.56, -1.66)],
        [(0, 1, 2, 3)], mat_idx=1, mirror_x=True
    )
    pdec4.build(parent=root, pivot=(0.0, 0.58, -1.30), visible=False, outward_normal="radial_x")

    pdec5 = PartBuilder("decal_5_windshield_banner", [mats["Decal_Graphic"], mats["Paint_Accent"]])
    pdec5.add_submesh(
        [(0.00, 1.32, 0.26), (0.48, 1.31, 0.25), (0.00, 1.415, 0.15), (0.48, 1.405, 0.14)],
        [(0, 1, 3, 2)], mat_idx=0, mirror_x=True
    )
    pdec5.build(parent=root, pivot=(0.0, 1.36, 0.20), visible=False, outward_normal=(0.0, 1.0, 1.0))

    # =========================================================================
    # 16. WHEELS, CALIPERS (Under Root!), 4 RIM VARIANTS & TIRE LETTERING
    # =========================================================================
    wheel_specs = [
        ("wheel-front-left",  -0.785, 0.33,  1.32, 0.33, 0.25, -1),
        ("wheel-front-right",  0.785, 0.33,  1.32, 0.33, 0.25,  1),
        ("wheel-back-left",   -0.790, 0.33, -1.32, 0.33, 0.26, -1),
        ("wheel-back-right",   0.790, 0.33, -1.32, 0.33, 0.26,  1),
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
        cz_off = -0.12 if "front" in w_name else 0.12
        x_in = min(wx - sx * 0.04, wx + sx * 0.06)
        x_out = max(wx - sx * 0.04, wx + sx * 0.06)
        pcal.add_box((x_in, wy - 0.04, wz + cz_off - 0.05), (x_out, wy + 0.11, wz + cz_off + 0.05), mat_idx=0)
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

        # Rim 1: 5-Spoke Blade Style
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
                r_in_l, r_out_l = r_rim + 0.022, r_tire - 0.018
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
    add_empty("light_head_L",   (-0.65, 0.63,  2.15), parent=root, forward_plus_z=True)
    add_empty("light_head_R",   ( 0.65, 0.63,  2.15), parent=root, forward_plus_z=True)
    add_empty("light_tail_L",   (-0.62, 0.85, -2.13), parent=root, forward_plus_z=False)
    add_empty("light_tail_R",   ( 0.62, 0.85, -2.13), parent=root, forward_plus_z=False)
    add_empty("camera_cockpit", (-0.36, 1.04, -0.16), parent=root, forward_plus_z=True)
    add_empty("exhaust_L",      (-0.50, 0.25, -2.17), parent=root, forward_plus_z=False)
    add_empty("exhaust_R",      ( 0.50, 0.25, -2.17), parent=root, forward_plus_z=False)

    # 1. Update view_layer while all objects are visible so Blender depsgraph evaluates matrix_local=(0,0,0) for all child rims
    bpy.context.view_layer.update()

    # 2. Apply target visibility for both blender_kontrol.py (hide_get / hide_render) and FBX export (hide_viewport)
    for o in bpy.data.objects:
        if "_target_visible" in o:
            vis = bool(o["_target_visible"])
            del o["_target_visible"]
            o.hide_set(not vis)
            o.hide_viewport = not vis
            o.hide_render = not vis

    # Select root for blender_kontrol.py
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    bpy.context.view_layer.objects.active = root
    return root


def export_hot_hatch_01_fbx():
    """
    Exports HotHatch_01.fbx with top-level parts unparented from the temporary Blender root
    so that in Unity the FBX root itself is HotHatch_01 and Blender's 3-level bake_space_transform
    bug never affects wheel->rim children. Then restores root parenting in Blender.
    """
    root = bpy.data.objects.get("HotHatch_01")
    top_children = list(root.children) if root else []
    vis_states = {o.name: (o.hide_viewport, o.hide_get(), o.hide_render) for o in bpy.data.objects}

    for o in bpy.data.objects:
        o.hide_viewport = False
        o.hide_set(False)
    bpy.context.view_layer.update()

    if root:
        for ch in top_children:
            ch.parent = None
            ch.matrix_parent_inverse = Matrix.Identity(4)
        bpy.context.collection.objects.unlink(root)
    bpy.context.view_layer.update()

    for name, (hv, hg, hr) in vis_states.items():
        o = bpy.data.objects.get(name)
        if o and o != root:
            o.hide_viewport = hv
            o.hide_set(hg)
            o.hide_render = hr

    out_dir = "/home/zoro/harvester/Harvester/Assets/Cars/HotHatch_01"
    os.makedirs(out_dir, exist_ok=True)
    fbx_path = os.path.join(out_dir, "HotHatch_01.fbx")
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
        o.hide_viewport = False
        o.hide_set(False)
    if root:
        bpy.context.collection.objects.link(root)
        for ch in top_children:
            ch.parent = root
            ch.matrix_parent_inverse = Matrix.Identity(4)
    bpy.context.view_layer.update()
    for name, (hv, hg, hr) in vis_states.items():
        o = bpy.data.objects.get(name)
        if o:
            o.hide_viewport = hv
            o.hide_set(hg)
            o.hide_render = hr
    if root:
        bpy.ops.object.select_all(action='DESELECT')
        root.select_set(True)
        bpy.context.view_layer.objects.active = root

    return fbx_path


build_hot_hatch_01()
export_hot_hatch_01_fbx()

