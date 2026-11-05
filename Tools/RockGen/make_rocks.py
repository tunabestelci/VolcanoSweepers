import bpy, bmesh, math, random, sys, os
import numpy as np
from mathutils import Vector, noise

argv = sys.argv[sys.argv.index('--') + 1:]
outdir = argv[0]
base = r'C:\Users\User\vs_rocks'
os.makedirs(outdir, exist_ok=True)

TILE = 1.7   # dokunun dunyadaki boyutu (m): UV ve yer degistirme icin

def load_disp(name):
    return np.load(f'{base}/{name}_disp.npy')

def sample(img, u, v):
    h, w = img.shape
    x = (u % 1.0) * (w - 1); y = (v % 1.0) * (h - 1)
    x0 = int(x); y0 = int(y); x1 = min(x0 + 1, w - 1); y1 = min(y0 + 1, h - 1)
    fx = x - x0; fy = y - y0
    return (img[y0, x0] * (1 - fx) + img[y0, x1] * fx) * (1 - fy) + (img[y1, x0] * (1 - fx) + img[y1, x1] * fx) * fy

def triplanar(img, p, n, tile):
    wx, wy, wz = abs(n.x) ** 4, abs(n.y) ** 4, abs(n.z) ** 4
    s = wx + wy + wz
    wx /= s; wy /= s; wz /= s
    d = 0.0
    if wx > 0.01: d += wx * sample(img, p.y / tile, p.z / tile)
    if wy > 0.01: d += wy * sample(img, p.x / tile, p.z / tile)
    if wz > 0.01: d += wz * sample(img, p.x / tile, p.y / tile)
    return d

def make_rock(name, variant, seed, stretch, amp, disp_img, tris_l0, tris_l1):
    rnd = random.Random(seed)
    bpy.ops.object.select_all(action='DESELECT')
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=6, radius=1.0)   # ~40k ucgen
    ob = bpy.context.active_object
    me = ob.data
    ox, oy, oz = [rnd.uniform(0, 100) for _ in range(3)]
    # kirik duzlemler: kaya yuzeyleri
    planes = []
    for _ in range(rnd.randint(4, 6)):
        nv = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.6, 1))).normalized()
        planes.append((nv, rnd.uniform(0.66, 0.86)))
    bm = bmesh.new(); bm.from_mesh(me)
    for v in bm.verts:
        p = v.co.copy()
        n1 = noise.noise(p * 0.9 + Vector((ox, oy, oz)))
        n2 = noise.noise(p * 2.2 + Vector((oy, oz, ox)))
        rr = noise.noise(p * 4.5 + Vector((oz, ox, oy)))
        r = 1.0 + 0.26 * n1 + 0.12 * n2 + 0.05 * (1 - abs(rr) * 2)
        q = p * r
        q = Vector((q.x * stretch[0], q.y * stretch[1], q.z * stretch[2]))
        for (nv, d) in planes:
            ex = q.dot(nv) - d * max(stretch)
            if ex > 0: q = q - nv * ex * 0.92
        # tabani duz
        zmin = -0.42 * stretch[2]
        if q.z < zmin: q.z = zmin + (q.z - zmin) * 0.05
        v.co = q
    bm.to_mesh(me); bm.free()
    me.update()
    me.calc_normals_split() if hasattr(me, 'calc_normals_split') else None
    # yuzey ince kabartisi: triplanar yer degistirme
    bm = bmesh.new(); bm.from_mesh(me)
    bm.verts.ensure_lookup_table(); bm.normal_update()
    disp = []
    for v in bm.verts:
        d = triplanar(disp_img, v.co, v.normal, TILE)
        disp.append((d - 0.5) * amp)
    for v, dd in zip(bm.verts, disp):
        v.co = v.co + v.normal * dd
    bm.to_mesh(me); bm.free()
    me.update()
    # tabani origine otur: en alt nokta -%10 yukseklik (gomulu)
    zs = [v.co.z for v in me.vertices]
    zmin, zmax = min(zs), max(zs)
    shift = -(zmin + 0.10 * (zmax - zmin))
    for v in me.vertices: v.co.z += shift
    me.update()
    results = {}
    for lod, tris in (('L0', tris_l0), ('L1', tris_l1)):
        bpy.ops.object.select_all(action='DESELECT')
        copy = ob.copy(); copy.data = ob.data.copy(); bpy.context.collection.objects.link(copy)
        bpy.context.view_layer.objects.active = copy
        copy.select_set(True)
        mod = copy.modifiers.new('dec', 'DECIMATE')
        mod.ratio = min(1.0, tris / float(len(copy.data.polygons)))
        bpy.ops.object.modifier_apply(modifier='dec')
        m2 = copy.data
        # kutu UV (yuzey normalinin baskin eksenine gore)
        bm = bmesh.new(); bm.from_mesh(m2)
        uv = bm.loops.layers.uv.new('UVMap')
        for f in bm.faces:
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for l in f.loops:
                p = l.vert.co
                if ax == 0: l[uv].uv = (p.y / TILE, p.z / TILE)
                elif ax == 1: l[uv].uv = (p.x / TILE, p.z / TILE)
                else: l[uv].uv = (p.x / TILE, p.y / TILE)
            f.smooth = True
        bm.to_mesh(m2); bm.free()
        m2.update()
        results[lod] = (copy, len(m2.polygons))
        bpy.ops.object.select_all(action='DESELECT')
        copy.select_set(True)
        path = os.path.join(outdir, f'{name}_{variant}_{lod}.obj')
        bpy.ops.wm.obj_export(filepath=path, export_selected_objects=True, export_uv=True, export_normals=True,
                              export_materials=False, forward_axis='NEGATIVE_Z', up_axis='Y', global_scale=1.0,
                              apply_modifiers=True)
        print('EXPORT', path, len(m2.polygons), 'faces', tuple(round(x, 2) for x in copy.dimensions))

VARIANTS = [
    ('A', 11, (1.00, 0.92, 0.80)),
    ('B', 23, (0.80, 0.84, 1.10)),
    ('C', 37, (1.40, 1.05, 0.55)),
    ('D', 51, (1.55, 0.72, 0.72)),
]
for name, amp in (('tiger', 0.13), ('lichen', 0.11)):
    disp_img = load_disp(name)
    for (var, seed, stretch) in VARIANTS:
        make_rock(name, var, seed + (7 if name == 'lichen' else 0), stretch, amp, disp_img, 2400, 420)
print('DONE')
