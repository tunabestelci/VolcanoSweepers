# Kaya varliklari uretimi (Poly Haven CC0: dark_rock, tiger_rock, lichen_rock)

1. `prep_tex.py`: 4k dokulari (diff jpg, normal exr) Unity icin 2048 JPG/PNG'ye cevirir.
2. `make_rocks.py`: Blender (headless) ile tiger/lichen disp haritasindan cok seviyeli (L0/L1) kaya OBJ'leri uretir.
   `blender -b --factory-startup --python make_rocks.py -- <cikti_klasoru>` (once disp png'leri `<ad>_disp.npy` olarak kaydedin).
Cikti: `Assets/_Project/Art/Models/Rocks2` ve `Assets/_Project/Art/Textures/Rocks`.
