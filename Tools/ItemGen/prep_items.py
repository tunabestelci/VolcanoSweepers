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

def vessel(name, path, target_h, tris):
    reset()
    bpy.ops.import_scene.fbx(filepath=path)
    objs = meshes()
    apply_all(objs)
    o = join(objs)
    mn, mx = bounds(o)
    H = mx.z - mn.z
    sc = target_h / H
    # taban merkezini origine al: once olc (eksen tahmini asagida), sonra olcekle
    # 1) eksen tahmini: yatayda orta yukseklikte ic bosluk merkezi
    depsgraph = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(o, depsgraph)
    zmid = mn.z + H * 0.55
    cx = (mn.x + mx.x) / 2; cy = (mn.y + mx.y) / 2
    for it in range(8):
        pts = []
        for a in range(24):
            ang = a / 24 * math.tau
            d = Vector((math.cos(ang), math.sin(ang), 0))
            loc, nrm, idx, dist = bvh.ray_cast(Vector((cx, cy, zmid)), d)
            if loc is not None: pts.append(loc)
        if len(pts) > 12:
            cx = sum(p.x for p in pts) / len(pts); cy = sum(p.y for p in pts) / len(pts)
    # 2) profil
    prof = []
    zs = [mn.z + H * k / 120 for k in range(121)]
    for z in zs:
        rs = []
        for a in range(24):
            ang = a / 24 * math.tau
            d = Vector((math.cos(ang), math.sin(ang), 0))
            loc, nrm, idx, dist = bvh.ray_cast(Vector((cx, cy, z)), d)
            rs.append(dist if loc is not None else None)
        valid = sorted([r for r in rs if r is not None])
        prof.append((z, len(valid), valid[len(valid) // 2] if valid else None))
    # ic taban: eksenden asagi isin (agizdan icerisi)
    loc, nrm, idx, dist = bvh.ray_cast(Vector((cx, cy, mx.z + 0.5)), Vector((0, 0, -1)))
    floor_z = loc.z if loc is not None else mn.z
    inner = []
    for z, n, r in prof:
        if z < floor_z - 1e-6: continue
        if n >= 20 and r is not None: inner.append((z, r))
    if not inner:
        print('PROFILE EMPTY', name); return
    # ust agiz: ardisik gecerli son nokta
    rim = inner[-1][0]
    print('PROFILE', name, 'axis', round(cx, 3), round(cy, 3), 'floor', round(floor_z, 3), 'rim', round(rim, 3), 'H', round(H, 3), 'pts', len(inner),
          'r_floor', round(inner[0][1], 3), 'r_rim', round(inner[-1][1], 3))
    # eksen origine, taban z=0, olcek
    base = mn.z
    prof_out = [[round((z - base) * sc, 5), round(r * sc, 5)] for z, r in inner]
    # dekimasyon sonrasi mesh
    decimate(o, tris)
    for v in o.data.vertices:
        v.co.x = (v.co.x - cx) * sc; v.co.y = (v.co.y - cy) * sc; v.co.z = (v.co.z - base) * sc
    o.data.update()
    for p in o.data.polygons: p.use_smooth = True
    o.name = name
    json.dump({'profile': prof_out, 'height': target_h}, open(os.path.join(OUT, name + '_profile.json'), 'w'))
    render_views(name.lower(), objs=[o])
    export_fbx(name, [o])

def torch():
    reset()
    bpy.ops.import_scene.fbx(filepath=r'C:\Users\User\vs_items\non-textured-burning-torch-4\source\9993.fbx')
    objs = meshes()
    for o in objs: print('TORCH OBJ', o.name, len(o.data.polygons), [m.name for m in o.data.materials])
    apply_all(objs)
    flame = [o for o in meshes() if 'flame' in o.name.lower()]
    body = [o for o in meshes() if 'flame' not in o.name.lower()]
    b = join(body); b.name = 'TorchBody'
    f = join(flame) if flame else None
    if f: f.name = 'TorchFlame'
    mn, mx = bounds(b)
    print('TORCH BODY BOUNDS', tuple(mn), tuple(mx))
    # en uzun eksen = govde ekseni
    ext = mx - mn
    axis = max(range(3), key=lambda i: ext[i])
    print('TORCH long axis', axis, tuple(ext))
    # eksen Z olmali: gerekirse dondur
    import mathutils
    if axis != 2:
        rot = mathutils.Matrix.Rotation(math.radians(90), 4, 'Y' if axis == 0 else 'X')
        for o in [b] + ([f] if f else []):
            for v in o.data.vertices: v.co = rot @ v.co
            o.data.update()
        mn, mx = bounds(b)
    # alev yukari bakiyor mu: alev merkezi govdenin hangi ucunda
    if f:
        fm, fM = bounds(f)
        fc = (fm.z + fM.z) / 2
        if fc < (mn.z + mx.z) / 2:
            rot = mathutils.Matrix.Rotation(math.radians(180), 4, 'X')
            for o in (b, f):
                for v in o.data.vertices: v.co = rot @ v.co
                o.data.update()
            mn, mx = bounds(b)
    H = max(bounds(b)[1].z, bounds(f)[1].z if f else -1e9) - mn.z
    s = 1.7 / H
    cx = (mn.x + mx.x) / 2; cy = (mn.y + mx.y) / 2
    for o in [b] + ([f] if f else []):
        for v in o.data.vertices: v.co = Vector(((v.co.x - cx) * s, (v.co.y - cy) * s, (v.co.z - mn.z) * s))
        o.data.update()
        for p in o.data.polygons: p.use_smooth = True
    bm2, bM2 = bounds(b)
    print('TORCH final body', tuple(bm2), tuple(bM2))
    if f:
        a, c = bounds(f); print('TORCH final flame', tuple(a), tuple(c))
    render_views('torch', objs=[b] + ([f] if f else []))
    export_fbx('Torch', [b] + ([f] if f else []))

def spoon():
    reset()
    bpy.ops.import_scene.fbx(filepath=r'C:\Users\User\vs_items\umamusume-spoon\source\strawberry.fbx')
    objs = meshes(); apply_all(objs); o = join(objs); o.name = 'Spoon'
    mn, mx = bounds(o); ext = mx - mn
    print('SPOON ext', tuple(ext))
    L = max(ext)
    s = 0.34 / L
    c = (mn + mx) / 2
    for v in o.data.vertices: v.co = (v.co - c) * s
    o.data.update()
    for p in o.data.polygons: p.use_smooth = True
    render_views('spoon', objs=[o])
    export_fbx('Spoon', [o])

def spatula():
    reset()
    bpy.ops.import_scene.gltf(filepath=r'D:\simple_spatula.glb')
    objs = meshes(); apply_all(objs); o = join(objs); o.name = 'Spatula'
    # dokulari cikar (1024)
    roles = {}
    for m in o.data.materials:
        nt = m.node_tree
        bsdf = [n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'][0]
        def tex_for(sock):
            if not bsdf.inputs[sock].is_linked: return None
            n = bsdf.inputs[sock].links[0].from_node
            while n.type not in ('TEX_IMAGE',):
                ins = [i for i in n.inputs if i.is_linked]
                if not ins: return None
                n = ins[0].links[0].from_node
            return n.image
        for sock, nm in (('Base Color', 'base'), ('Normal', 'normal'), ('Roughness', 'rough'), ('Metallic', 'metal')):
            im = tex_for(sock)
            print('SPATULA tex', sock, im.name if im else None, im.size[:] if im else None, im.colorspace_settings.name if im else None)
            if im and nm not in roles:
                im2 = im.copy(); im2.scale(1024, 1024)
                p = os.path.join(OUT, f'Spatula_{nm}.png')
                im2.filepath_raw = p; im2.file_format = 'PNG'; im2.save()
                roles[nm] = p
    mn, mx = bounds(o); ext = mx - mn
    print('SPATULA ext', tuple(ext))
    L = max(ext); s = 0.5 / L
    c = (mn + mx) / 2
    for v in o.data.vertices: v.co = (v.co - c) * s
    o.data.update()
    render_views('spatula', objs=[o])
    export_fbx('Spatula', [o])

vessel('Mug', r'C:\Users\User\vs_items\mug-glass\source\gelas.fbx', 0.088, 3200)
vessel('Jug', r'C:\Users\User\vs_items\glass-water-jug-high-poly\source\Jug.fbx', 0.24, 4200)
torch()
spoon()
spatula()
print('ALLDONE')
