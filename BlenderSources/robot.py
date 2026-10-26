# Parçalı robot modeli: iskelet, parçalar, idle/run animasyonları ve Unity'ye FBX dışa aktarma.
#
# Kullanım: Blender'da Scripting sekmesinde bu dosyayı açıp çalıştır (ya da Unity MCP blender_bridge run_python).
# Ayrı bir "RobotYeni" sahnesinde çalışır, açık sahnene dokunmaz. Sonra Unity'de
# Harvester > Rebuild Robot Prefabs menüsünü çalıştır.
import bpy, bmesh, math
from mathutils import Vector, Matrix

# Parçalı robot: her uzuv ayrı kapalı mesh, kendi kemiğine sabit bağlı (skin yok).
# Ölçüler metre; boy ~4.75 m (eski robotla aynı). Robot -Y yönüne bakar (Unity'de +Z).

scn = bpy.data.scenes.get("RobotYeni") or bpy.data.scenes.new("RobotYeni")
bpy.context.window.scene = scn
for o in list(scn.objects):
    bpy.data.objects.remove(o, do_unlink=True)
for a in list(bpy.data.actions):
    if a.name in ("idle", "run"):
        bpy.data.actions.remove(a)

def material(name, color, emission=None):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1)
        bsdf.inputs["Emission Strength"].default_value = 4.0
    m.diffuse_color = (*color, 1)
    return m

MATS = [material("Govde", (0.6, 0.6, 0.62)), material("Eklem", (0.08, 0.08, 0.09)), material("Goz", (1.0, 0.15, 0.05), (1.0, 0.15, 0.05))]
GOVDE, EKLEM, GOZ = 0, 1, 2

# --- Primitifler (bmesh'e eklenir) ---
def box(bm, center, size, mat, taper=1.0, bevel=0.04):
    """size: (x, y, z). taper: üst yüzün alt yüze oranı (konik kutu)."""
    res = bmesh.ops.create_cube(bm, size=1.0)
    verts = res["verts"]
    for v in verts:
        s = taper if v.co.z > 0 else 1.0
        v.co = Vector((v.co.x * size[0] * s, v.co.y * size[1] * s, v.co.z * size[2])) + Vector(center)
    faces = list({f for v in verts for f in v.link_faces})
    for f in faces:
        f.material_index = mat
    if bevel > 0:
        edges = list({e for v in verts for e in v.link_edges})
        bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=1, affect='EDGES', profile=0.5)
    return verts

def cylinder(bm, a, b, radius, mat, segments=10, radius2=None):
    """a→b ekseninde silindir/koni."""
    a, b = Vector(a), Vector(b)
    length = (b - a).length
    res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments,
                                radius1=radius, radius2=radius if radius2 is None else radius2, depth=length)
    rot = Vector((0, 0, 1)).rotation_difference((b - a).normalized()).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation((a + b) / 2) @ rot, verts=res["verts"])
    for f in {f for v in res["verts"] for f in v.link_faces}:
        f.material_index = mat
    return res["verts"]

def sphere(bm, center, radius, mat):
    res = bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=6, radius=radius)
    bmesh.ops.translate(bm, vec=Vector(center), verts=res["verts"])
    for f in {f for v in res["verts"] for f in v.link_faces}:
        f.material_index = mat
    return res["verts"]

def piece(name, build):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    build(bm)
    # Bevel sonrası malzeme indeksleri komşu yüzden kalıtılır; ayrıca garanti olsun diye normalleri düzelt
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    for m in MATS:
        me.materials.append(m)
    for p in me.polygons:
        p.use_smooth = False
    ob = bpy.data.objects.new(name, me)
    scn.collection.objects.link(ob)
    return ob

# --- İskelet ---
arm_data = bpy.data.armatures.new("RobotRig")
arm = bpy.data.objects.new("RobotRig", arm_data)
scn.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')

def bone(name, head, tail, parent=None, roll=0.0):
    eb = arm_data.edit_bones.new(name)
    eb.head, eb.tail, eb.roll = Vector(head), Vector(tail), roll
    if parent:
        eb.parent = arm_data.edit_bones[parent]
        eb.use_connect = False
    return eb

bone("hips", (0, 0, 2.45), (0, 0, 2.75))
bone("spine", (0, 0, 2.75), (0, 0, 3.05), "hips")
bone("chest", (0, 0, 3.05), (0, 0, 3.85), "spine")
bone("neck", (0, 0, 3.85), (0, 0, 4.0), "chest")
bone("head", (0, 0, 4.0), (0, 0, 4.55), "neck")
for side, sx in (("L", 1), ("R", -1)):
    bone(f"shoulder.{side}", (0.35 * sx, 0, 3.7), (0.82 * sx, 0, 3.7), "chest")
    bone(f"upper_arm.{side}", (0.85 * sx, 0, 3.62), (0.85 * sx, 0, 2.92), f"shoulder.{side}")
    bone(f"forearm.{side}", (0.85 * sx, 0, 2.92), (0.85 * sx, 0, 2.22), f"upper_arm.{side}")
    bone(f"hand.{side}", (0.85 * sx, 0, 2.22), (0.85 * sx, 0, 1.92), f"forearm.{side}")
    bone(f"thigh.{side}", (0.3 * sx, 0, 2.45), (0.3 * sx, 0, 1.42), "hips")
    bone(f"shin.{side}", (0.3 * sx, 0, 1.42), (0.3 * sx, 0, 0.3), f"thigh.{side}")
    bone(f"foot.{side}", (0.3 * sx, 0, 0.3), (0.3 * sx, -0.45, 0.12), f"shin.{side}")

bpy.ops.object.mode_set(mode='OBJECT')

# --- Parçalar (dünya koordinatında modellenir, sonra kemiğe bağlanır) ---
pieces = {}

def hips(bm):
    box(bm, (0, 0, 2.6), (1.0, 0.6, 0.32), GOVDE, taper=0.9)
    cylinder(bm, (-0.42, 0, 2.48), (0.42, 0, 2.48), 0.14, EKLEM)  # kalça ekseni
pieces["Kalca"] = ("hips", hips)

def spine(bm):
    cylinder(bm, (0, 0.05, 2.76), (0, 0.05, 3.06), 0.16, EKLEM, radius2=0.2)
    box(bm, (0, 0.12, 2.9), (0.5, 0.3, 0.22), GOVDE)
pieces["Bel"] = ("spine", spine)

def chest(bm):
    box(bm, (0, 0, 3.47), (1.25, 0.8, 0.78), GOVDE, taper=1.12, bevel=0.06)
    box(bm, (0, -0.42, 3.5), (0.6, 0.08, 0.35), EKLEM)                 # göğüs plakası
    box(bm, (0, 0.45, 3.45), (0.7, 0.18, 0.5), EKLEM)                  # sırt motoru
    cylinder(bm, (0.2, 0.55, 3.7), (0.2, 0.55, 4.05), 0.06, EKLEM)    # egzoz
    cylinder(bm, (-0.2, 0.55, 3.7), (-0.2, 0.55, 4.05), 0.06, EKLEM)
pieces["Gogus"] = ("chest", chest)

def neck(bm):
    cylinder(bm, (0, 0, 3.84), (0, 0, 4.02), 0.14, EKLEM)
pieces["Boyun"] = ("neck", neck)

def head(bm):
    box(bm, (0, 0, 4.28), (0.72, 0.66, 0.52), GOVDE, bevel=0.05)
    box(bm, (0, -0.33, 4.32), (0.56, 0.06, 0.14), GOZ, bevel=0.0)      # vizör
    box(bm, (0, -0.31, 4.12), (0.4, 0.06, 0.08), EKLEM, bevel=0.0)     # ağız ızgarası
    cylinder(bm, (0.22, 0.1, 4.54), (0.22, 0.1, 4.78), 0.025, EKLEM)  # anten
    sphere(bm, (0.22, 0.1, 4.8), 0.05, GOZ)
pieces["Kafa"] = ("head", head)

for side, sx in (("L", 1), ("R", -1)):
    def shoulder(bm, sx=sx):
        box(bm, (0.82 * sx, 0, 3.78), (0.5, 0.62, 0.3), GOVDE, taper=0.85, bevel=0.05)
        sphere(bm, (0.85 * sx, 0, 3.62), 0.17, EKLEM)
    pieces[f"Omuz.{side}"] = (f"shoulder.{side}", shoulder)

    def upper_arm(bm, sx=sx):
        box(bm, (0.85 * sx, 0, 3.27), (0.26, 0.28, 0.6), GOVDE, taper=0.85)
    pieces[f"UstKol.{side}"] = (f"upper_arm.{side}", upper_arm)

    def forearm(bm, sx=sx):
        sphere(bm, (0.85 * sx, 0, 2.92), 0.14, EKLEM)                 # dirsek
        box(bm, (0.85 * sx, 0, 2.55), (0.32, 0.34, 0.58), GOVDE, taper=1.15)
        box(bm, (0.85 * sx + 0.17 * sx, 0, 2.55), (0.06, 0.2, 0.4), EKLEM, bevel=0.0)
    pieces[f"OnKol.{side}"] = (f"forearm.{side}", forearm)

    def hand(bm, sx=sx):
        box(bm, (0.85 * sx, 0, 2.14), (0.24, 0.26, 0.18), EKLEM)
        for fy in (-0.08, 0.08):                                      # iki pençe
            box(bm, (0.85 * sx, fy, 1.98), (0.18, 0.06, 0.2), EKLEM, taper=0.6, bevel=0.0)
    pieces[f"El.{side}"] = (f"hand.{side}", hand)

    def thigh(bm, sx=sx):
        box(bm, (0.3 * sx, 0, 1.95), (0.36, 0.42, 0.85), GOVDE, taper=1.2)
    pieces[f"Uyluk.{side}"] = (f"thigh.{side}", thigh)

    def shin(bm, sx=sx):
        sphere(bm, (0.3 * sx, -0.03, 1.42), 0.17, EKLEM)             # diz
        box(bm, (0.3 * sx, 0, 0.85), (0.32, 0.38, 0.95), GOVDE, taper=1.25)
        box(bm, (0.3 * sx, -0.21, 1.0), (0.2, 0.06, 0.45), EKLEM, bevel=0.0)  # kaval plakası
    pieces[f"Baldir.{side}"] = (f"shin.{side}", shin)

    def foot(bm, sx=sx):
        cylinder(bm, (0.2 * sx + 0.1 * sx - 0.12, 0, 0.3), (0.3 * sx + 0.12, 0, 0.3), 0.1, EKLEM)  # bilek
        box(bm, (0.3 * sx, -0.12, 0.12), (0.42, 0.78, 0.24), GOVDE, taper=0.85)
    pieces[f"Ayak.{side}"] = (f"foot.{side}", foot)

for name, (bone_name, build) in pieces.items():
    ob = piece(name, build)
    ob.parent = arm
    ob.parent_type = 'BONE'
    ob.parent_bone = bone_name
    bpy.context.view_layer.update()
    ob.matrix_world = Matrix.Identity(4)

bpy.context.view_layer.update()
print("parça sayısı:", len(pieces), "kemik:", len(arm_data.bones))

# ---------------------------------------------------------------- Animasyonlar

# Aşağı bakan kemiklerde (bacak/kol) +X dönüş ucu geriye götürür; yukarı bakanlarda (gövde/kafa) öne eğer.
arm = bpy.data.objects["RobotRig"]
scn = bpy.context.window.scene
scn.render.fps = 30
for pb in arm.pose.bones:
    pb.rotation_mode = 'XYZ'

def reset():
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)

def key_all(frame):
    for pb in arm.pose.bones:
        pb.keyframe_insert("rotation_euler", frame=frame)
        pb.keyframe_insert("location", frame=frame)

def make_action(name, frames, pose_at):
    action = bpy.data.actions.get(name)
    if action:
        bpy.data.actions.remove(action)
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = action
    for f in range(0, frames + 1):
        reset()
        pose_at(f / frames)  # 0..1, son kare ilkiyle aynı → kesintisiz döngü
        key_all(f)
    for fc in action.fcurves:
        for kp in fc.keyframe_points:
            kp.interpolation = 'LINEAR'
    return action

P = arm.pose.bones
TAU = 2 * math.pi

def run_pose(p):
    s, c = math.sin(TAU * p), math.cos(TAU * p)
    swing = 0.7 * s                      # sol bacağın öne salınımı (radyan)
    P["thigh.L"].rotation_euler.x = -swing
    P["thigh.R"].rotation_euler.x = swing
    # Bacak öne giderken diz bükülür
    P["shin.L"].rotation_euler.x = 0.15 + 0.95 * max(0.0, c)
    P["shin.R"].rotation_euler.x = 0.15 + 0.95 * max(0.0, -c)
    P["foot.L"].rotation_euler.x = -0.25 * max(0.0, c)
    P["foot.R"].rotation_euler.x = -0.25 * max(0.0, -c)
    # Kollar bacakların tersine
    P["upper_arm.L"].rotation_euler.x = 0.55 * s
    P["upper_arm.R"].rotation_euler.x = -0.55 * s
    P["forearm.L"].rotation_euler.x = -0.7 - 0.2 * max(0.0, -s)
    P["forearm.R"].rotation_euler.x = -0.7 - 0.2 * max(0.0, s)
    # Gövde: öne eğik, kalça ile göğüs ters döner, adım başına iki kez zıplar
    P["hips"].location.y = 0.07 * math.cos(2 * TAU * p) - 0.06
    P["hips"].rotation_euler.y = -0.12 * s
    P["chest"].rotation_euler.x = 0.18
    P["chest"].rotation_euler.y = 0.2 * s
    P["head"].rotation_euler.x = -0.12

def idle_pose(p):
    s = math.sin(TAU * p)
    P["hips"].location.y = 0.025 * s
    P["chest"].rotation_euler.x = 0.03 * s
    P["head"].rotation_euler.y = 0.25 * math.sin(TAU * p)  # etrafa bakınır
    P["head"].rotation_euler.x = 0.04 * math.cos(TAU * p)
    P["upper_arm.L"].rotation_euler.z = 0.06 + 0.03 * s
    P["upper_arm.R"].rotation_euler.z = -0.06 - 0.03 * s
    P["forearm.L"].rotation_euler.x = -0.25 - 0.05 * s
    P["forearm.R"].rotation_euler.x = -0.25 - 0.05 * s
    P["shin.L"].rotation_euler.x = 0.08
    P["shin.R"].rotation_euler.x = 0.08
    P["thigh.L"].rotation_euler.x = -0.04
    P["thigh.R"].rotation_euler.x = -0.04

run = make_action("run", 16, run_pose)   # 0.53 sn
idle = make_action("idle", 60, idle_pose)  # 2 sn
arm.animation_data.action = run
scn.frame_start, scn.frame_end = 0, 16
scn.frame_set(4)
print("actions:", [(a.name, tuple(a.frame_range)) for a in (run, idle)])

# ---------------------------------------------------------------- Dışa aktarma
arm = bpy.data.objects["RobotRig"]
arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.rotation_euler = (0, 0, 0); pb.location = (0, 0, 0)
bpy.context.view_layer.update()
for o in bpy.context.view_layer.objects:
    o.select_set(o.name in scn.objects)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath="/home/zoro/harvester/Harvester/Assets/Prefabs/chars/roboParcali.fbx",
    use_selection=True, object_types={'ARMATURE', 'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
    axis_forward='-Z', axis_up='Y',
    add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
    mesh_smooth_type='FACE', use_mesh_modifiers=True)
print("exported")
