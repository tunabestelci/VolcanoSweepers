import bpy, bmesh, os, json, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree

OUT = r'C:\Users\User\vs_items\out'
os.makedirs(OUT, exist_ok=True)

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def meshes():
    return [o for o in bpy.data.objects if o.type == 'MESH']

def apply_all(objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    # parent iliskilerini kaldir (dunya donusumu korunur)
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def join(objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1: bpy.ops.object.join()
    return bpy.context.view_layer.objects.active

def bounds(o):
    mn = Vector((1e9,) * 3); mx = Vector((-1e9,) * 3)
    for v in o.data.vertices:
        w = o.matrix_world @ v.co
        mn = Vector((min(mn[i], w[i]) for i in range(3))); mx = Vector((max(mx[i], w[i]) for i in range(3)))
    return mn, mx

def set_origin_world(o, p):
    for v in o.data.vertices: v.co -= p
    o.data.update()

def scale_mesh(o, s):
    for v in o.data.vertices: v.co *= s
    o.data.update()

def decimate(o, tris):
    n = len(o.data.polygons)
    if n <= tris: return
    bpy.context.view_layer.objects.active = o
    m = o.modifiers.new('dec', 'DECIMATE'); m.ratio = tris / n
    bpy.ops.object.modifier_apply(modifier='dec')

def export_fbx(name, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT, name + '.fbx')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, add_leaf_bones=False, bake_anim=False,
                             axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL', path_mode='STRIP', embed_textures=False,
                             mesh_smooth_type='FACE')
    print('EXPORT', name, os.path.getsize(path), 'polys', [len(o.data.polygons) for o in objs])

def render_views(prefix, size=1.0, objs=None):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'MATERIAL'
    sc.render.resolution_x = 400; sc.render.resolution_y = 400
    objs = objs or meshes()
    mn = Vector((1e9,) * 3); mx = Vector((-1e9,) * 3)
    for o in objs:
        a, b = bounds(o)
        mn = Vector((min(mn[i], a[i]) for i in range(3))); mx = Vector((max(mx[i], b[i]) for i in range(3)))
    c = (mn + mx) / 2; ext = (mx - mn).length
    bpy.ops.object.camera_add(); cam = bpy.context.object; sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = ext * 1.05
    for nm, d in (('a', (0, -1, 0.2)), ('b', (1, 0, 0.2)), ('c', (0, 0, 1))):
        dv = Vector(d).normalized()
        cam.location = c + dv * ext * 3
        cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(OUT, f'prev_{prefix}_{nm}.png')
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)

