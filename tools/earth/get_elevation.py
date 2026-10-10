"""Downloads AWS Terrain Tiles (terrarium, zoom 5, the rows the Earth map uses) and joins them into elev_z5.npy.

Usage: python3 get_elevation.py <output .npy>   (needs numpy and Pillow, and internet access)
"""
import io
import sys
import urllib.request

import numpy as np
from PIL import Image

ROWS = range(4, 23)
out = np.zeros((len(ROWS) * 256, 32 * 256), np.float32)
for j, y in enumerate(ROWS):
    for x in range(32):
        url = f"https://s3.amazonaws.com/elevation-tiles-prod/terrarium/5/{x}/{y}.png"
        data = urllib.request.urlopen(url, timeout=60).read()
        a = np.asarray(Image.open(io.BytesIO(data)).convert('RGB')).astype(np.float32)
        out[j * 256:(j + 1) * 256, x * 256:(x + 1) * 256] = a[..., 0] * 256 + a[..., 1] + a[..., 2] / 256 - 32768
    print(f"row {y} done", flush=True)
np.save(sys.argv[1], out)
