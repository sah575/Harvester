"""Unity'nin dışa aktardığı yerel mesh verisinden, pivotlara dokunmadan iki LOD üretir.

Blender --background --python bu_dosya -- girdi.json cikti.fbx
Girdi HarvesterLodBuilder.ExportSource menüsünden gelir. Koordinat kalibrasyonu
FBX eksen/birim dönüşümünü Unity tarafında kesin olarak tersine çevirmek içindir.
"""
import bpy, bmesh, json, sys
from pathlib import Path

source, target = sys.argv[sys.argv.index('--') + 1:]
data = json.loads(Path(source).read_text())
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
report = []
for level, ratio in [(0, .085), (1, .020)]:
    triangles = 0
    for index, item in enumerate(data['meshes']):
        name = f'L{level}_{index:03}'
        faces, slots = [], []
        for slot, submesh in enumerate(item['submeshes']):
            indices = submesh['values']
            faces += [indices[i:i+3] for i in range(0, len(indices), 3)]
            slots += [slot] * (len(indices)//3)
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata([(v['x'], v['y'], v['z']) for v in item['vertices']], [], faces)
        mesh.update()
        uv = mesh.uv_layers.new(name='UVMap')
        for loop in mesh.loops:
            if loop.vertex_index < len(item['uv']):
                v = item['uv'][loop.vertex_index]
                uv.data[loop.index].uv = (v['x'], v['y'])
        for slot in range(len(item['submeshes'])):
            mat = bpy.data.materials.get(f'Yuva_{slot:02}') or bpy.data.materials.new(f'Yuva_{slot:02}')
            mesh.materials.append(mat)
        for polygon, slot in zip(mesh.polygons, slots):
            polygon.material_index = slot
            polygon.use_smooth = True
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        # Unity UV/normal sınırlarında aynı köşeyi çoğaltır. Ayrık üçgenleri
        # sadeleştirmek yüzeyde aralık açar; önce topolojiyi tekrar birleştir.
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.remove_doubles(threshold=0.000001)
        topology = bmesh.from_edit_mesh(mesh)
        bmesh.ops.recalc_face_normals(topology, faces=list(topology.faces))
        bmesh.update_edit_mesh(mesh)
        bpy.ops.object.mode_set(mode='OBJECT')
        if len(faces) > 24:
            modifier = obj.modifiers.new('Üçgen bütçesi', 'DECIMATE')
            modifier.ratio = max(ratio, 12 / len(faces))
            modifier.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        modifier = obj.modifiers.new('Üçgenle', 'TRIANGULATE')
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        topology = bmesh.new()
        topology.from_mesh(obj.data)
        bmesh.ops.recalc_face_normals(topology, faces=list(topology.faces))
        topology.to_mesh(obj.data)
        topology.free()
        obj.data.set_sharp_from_angle(angle=0.785398)
        triangles += len(obj.data.polygons)
        obj.select_set(False)
    report.append({'level': level, 'triangles': triangles})

mesh = bpy.data.meshes.new('CoordinateCalibration')
mesh.from_pydata([(0,0,0),(1,0,0),(0,2,0),(0,0,3)], [], [(0,1,2),(0,3,1),(0,2,3),(1,3,2)])
obj = bpy.data.objects.new(mesh.name, mesh)
bpy.context.collection.objects.link(obj)
bpy.ops.export_scene.fbx(filepath=str(Path(target).resolve()), use_selection=False,
    object_types={'MESH'}, add_leaf_bones=False, bake_anim=False,
    axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True)
Path(target + '.json').write_text(json.dumps(report, indent=2))
print('LOD_SONUCU=' + json.dumps(report))
