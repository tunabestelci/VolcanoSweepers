import bpy, os, math
from mathutils import Vector
exec(open(r'C:\Users\User\vs_items\common.py').read())

reset()
bpy.ops.import_scene.fbx(filepath=r'C:\Users\User\vs_items\non-textured-burning-torch-4\source\9993.fbx')
objs = meshes()
apply_all(objs)
flame = [o for o in meshes() if 'flame' in o.name.lower()]
body = [o for o in meshes() if 'flame' not in o.name.lower()]
b = join(body); b.name = 'TorchBody'
f = join(flame); f.name = 'TorchFlame'
mn, mx = bounds(b)
print('TORCH raw body', tuple(mn), tuple(mx), 'flame', tuple(bounds(f)[0]), tuple(bounds(f)[1]))
zs = sorted(v.co.z for v in b.data.vertices)
lo = zs[int(len(zs) * 0.15)]; hi = zs[int(len(zs) * 0.85)]
def spread(sel):
    pts = [v.co for v in b.data.vertices if sel(v.co.z)]
    cxm = sum(p.x for p in pts) / len(pts); cym = sum(p.y for p in pts) / len(pts)
    return sum(math.hypot(p.x - cxm, p.y - cym) for p in pts) / len(pts)
sl, sh = spread(lambda z: z <= lo), spread(lambda z: z >= hi)
print('TORCH spread low/high', sl, sh)
flip = not (sl > sh)
H0 = mx.z - mn.z
s = 1.35 / H0
cx = (mn.x + mx.x) / 2; cy = (mn.y + mx.y) / 2
for v in b.data.vertices:
    x, y, z = (v.co.x - cx) * s, (v.co.y - cy) * s, (v.co.z - mn.z) * s
    if flip: z = 1.35 - z; x = -x
    v.co = Vector((x, y, z))
b.data.update()
for p in b.data.polygons: p.use_smooth = True
fm, fM = bounds(f)
fh = fM.z - fm.z; fc = (fm + fM) / 2
fs = 0.34 / max(fh, 1e-6)
for v in f.data.vertices:
    v.co = Vector(((v.co.x - fc.x) * fs, (v.co.y - fc.y) * fs, (v.co.z - fm.z) * fs + 1.35 - 0.06))
f.data.update()
for p in f.data.polygons: p.use_smooth = True
bm2, bM2 = bounds(b); fm2, fM2 = bounds(f)
print('TORCH final body', tuple(bm2), tuple(bM2), 'flame', tuple(fm2), tuple(fM2), 'flip', flip)
render_views('torch', objs=[b, f])
export_fbx('Torch', [b, f])
print('ALLDONE')
