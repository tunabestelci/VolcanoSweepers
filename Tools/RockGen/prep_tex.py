import sys, os
import numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS = None
import OpenEXR

base = r'C:\Users\User\vs_rocks'
out = r'C:\Users\User\VolcanoSweepers\Assets\_Project\Art\Textures\Rocks'
os.makedirs(out, exist_ok=True)

def read_exr(path):
    f = OpenEXR.File(path)
    ch = f.channels()
    key = 'RGB' if 'RGB' in ch else list(ch.keys())[0]
    px = np.asarray(ch[key].pixels).astype(np.float32)
    return px

def resize_arr(a, size):
    # a: HxWxC float 0..1
    c = a.shape[2] if a.ndim == 3 else 1
    chans = []
    for i in range(c):
        im = Image.fromarray(a[..., i] if c > 1 else a, mode='F')
        im = im.resize((size, size), Image.LANCZOS)
        chans.append(np.asarray(im))
    return np.stack(chans, axis=-1) if c > 1 else chans[0]

def prep(name, diff_size, norm_size, exposure=1.0, gamma=1.0):
    d = Image.open(f'{base}/{name}/textures/{name}_rock_diff_4k.jpg').convert('RGB').resize((diff_size, diff_size), Image.LANCZOS)
    arr = np.asarray(d).astype(np.float32) / 255.0
    if exposure != 1.0 or gamma != 1.0:
        arr = np.clip(arr * exposure, 0, 1) ** gamma
    Image.fromarray((arr * 255 + 0.5).astype(np.uint8)).save(f'{out}/{name.capitalize()}Rock_Diff.jpg', quality=93, subsampling=0)
    print(name, 'diff mean', (arr.reshape(-1, 3).mean(0) * 255).round(1))

    n = read_exr(f'{base}/{name}/textures/{name}_rock_nor_gl_4k.exr')
    print(name, 'normal exr', n.shape, n.min(), n.max(), n.reshape(-1, n.shape[2]).mean(0))
    n = np.clip(n[..., :3], 0, 1)
    n = resize_arr(n, norm_size)
    # yeniden normalize
    v = n * 2 - 1
    ln = np.maximum(np.linalg.norm(v, axis=2, keepdims=True), 1e-6)
    v = v / ln
    n = np.clip((v * 0.5 + 0.5) * 255 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(n).save(f'{out}/{name.capitalize()}Rock_Normal.png', optimize=True)

    r = read_exr(f'{base}/{name}/textures/{name}_rock_rough_4k.exr')
    print(name, 'rough mean', float(r[..., 0].mean()))

prep('dark', 2048, 2048, exposure=2.3, gamma=0.92)
prep('tiger', 2048, 2048)
prep('lichen', 2048, 2048, exposure=1.25)
for f in sorted(os.listdir(out)):
    print(f, os.path.getsize(os.path.join(out, f)) // 1024, 'KB')
