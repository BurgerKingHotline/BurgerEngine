"""Generates the 'Earth 1930' base map for BurgerEngine.

Usage: python3 make_earth.py <natural-earth geojson dir> <elevation .npy> <output game dir>

Inputs (both free to use):
  * Natural Earth 1:10m vectors (public domain): admin-0/1, lakes, rivers, regions, marine areas, populated places, glaciers.
  * Elevation: AWS Terrain Tiles (terrarium, zoom 5, rows 4-22), joined into one array by get_elevation.py.
Output: a BurgerEngine game folder (map/, common/countries, common/terrain.txt, gfx/flags, gfx/terrain, history/diplomacy.txt).
"""
import collections
import json
import math
import os
import re
import sys
import unicodedata

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from scipy.spatial import cKDTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import earth1930_data as D  # noqa: E402

NE, ELEV, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
W, H = 6144, 3456
TOP_LAT = 78.0
TARGET_LAND, TARGET_SEA, TARGET_STATES = 5150, 950, 1150
MAX_STATE_LOCATIONS = 9
rng = np.random.default_rng(1930)


def log(*a):
    print(*a, flush=True)


# ----------------------------------------------------------------------------------------- projection (Mercator)
def merc(lat):
    lat = max(-85.0, min(85.0, lat))
    return math.log(math.tan(math.pi / 4 + math.radians(lat) / 2))


MTOP = merc(TOP_LAT)


def px(lon):
    return (lon + 180.0) / 360.0 * W


def py(lat):
    return (MTOP - merc(lat)) / (2 * math.pi) * W


ROW_LAT = np.degrees(np.arctan(np.sinh(MTOP - (np.arange(H) + 0.5) / W * 2 * np.pi))).astype(np.float32)
COL_LON = ((np.arange(W) + 0.5) / W * 360.0 - 180.0).astype(np.float32)
ROW_COS = np.cos(np.radians(ROW_LAT)).astype(np.float32)
KM_PER_PX_EQ = 40075.0 / W
BOTTOM_LAT = float(ROW_LAT[-1])
log(f"map {W}x{H}, latitudes {TOP_LAT} .. {BOTTOM_LAT:.1f}")


def load(name):
    with open(os.path.join(NE, name), encoding='utf-8') as f:
        return json.load(f)['features']


def rings(geom):
    """Yields (exterior, [holes]) for Polygon/MultiPolygon geometries."""
    if geom is None:
        return
    t = geom['type']
    polys = [geom['coordinates']] if t == 'Polygon' else geom['coordinates'] if t == 'MultiPolygon' else []
    for p in polys:
        if p:
            yield p[0], p[1:]


def proj(ring):
    return [(px(x), py(y)) for x, y in ring]


def draw_polys(draw, geom, value, holes_value=None):
    for ext, holes in rings(geom):
        pts = proj(ext)
        if len(pts) >= 3:
            draw.polygon(pts, fill=value)
        if holes_value is not None:
            for h in holes:
                hp = proj(h)
                if len(hp) >= 3:
                    draw.polygon(hp, fill=holes_value)


def slug(s):
    s = unicodedata.normalize('NFKD', s or '').encode('ascii', 'ignore').decode()
    s = re.sub(r'[^a-zA-Z0-9]+', '_', s).strip('_').lower()
    if not s:
        s = 'unnamed'
    if s[0].isdigit():
        s = 'l_' + s
    return s


def ascii_name(s):
    s = unicodedata.normalize('NFKD', s or '').encode('ascii', 'ignore').decode().strip()
    return re.sub(r'\s+', ' ', s.replace('"', "'"))


# ----------------------------------------------------------------------------------------- admin-1 units
log("loading admin-1 ...")
adm0_feats = load('ne_10m_admin_0_countries.geojson')
ADM0 = {}
for f in adm0_feats:
    p = f['properties']
    ADM0[p['ADM0_A3']] = (p.get('SUBREGION') or '', float(p.get('POP_EST') or 0))

feats = load('ne_10m_admin_1_states_provinces.geojson')
units = []  # index = raster id - 1
for f in feats:
    p = f['properties']
    a3 = p['adm0_a3']
    if a3 not in D.OWNER_BY_ADM0:
        raise SystemExit(f"no 1930 owner for country {a3} ({p['admin']})")
    owner = D.OWNER_BY_ADM0[a3]
    iso = p.get('iso_3166_2') or ''
    owner = D.OWNER_BY_UNIT.get(iso, owner)
    units.append(dict(adm0=a3, iso=iso, name=ascii_name(p.get('name_en') or p.get('name') or p.get('admin')),
                      region=p.get('region') or '', owner=owner, area=float(p.get('area_sqkm') or 0), geom=f['geometry']))
log(f"{len(units)} admin-1 units")

UNIT_NAMES = {'PL-ZP': 'Pomerania', 'PL-DS': 'Lower Silesia', 'PL-OP': 'Upper Silesia', 'PL-LB': 'East Brandenburg', 'PL-WN': 'Masuria',
              'RU-KGD': 'East Prussia', 'LT-VL': 'Wilno', 'BY-BR': 'Polesie', 'BY-HR': 'Nowogrodek', 'UA-46': 'Lwow', 'UA-61': 'Tarnopol',
              'UA-26': 'Stanislawow', 'UA-07': 'Volhynia', 'UA-56': 'Rowne', 'UA-77': 'Bukovina', 'BG-08': 'Southern Dobruja',
              'BG-19': 'Silistra', 'UA-21': 'Carpathian Ruthenia', 'HR-18': 'Istria', 'TR-31': 'Alexandretta', 'CA-NL': 'Newfoundland and Labrador',
              'CN-XJ': 'Xinjiang', 'CN-XZ': 'Tibet', 'RU-TY': 'Tannu Tuva', 'IN-GA': 'Goa', 'IN-PY': 'Pondichery', 'YE-AD': 'Aden'}
SPLIT_NAMES = {('RU-LEN', 'FIN'): 'Viipuri', ('RU-KR', 'FIN'): 'Ladoga Karelia', ('RU-MUR', 'FIN'): 'Petsamo', ('RU-SAK', 'JAP'): 'Karafuto',
               ('PL-PM', 'DNZ'): 'Danzig', ('PL-PM', 'GER'): 'Further Pomerania', ('PL-SL', 'GER'): 'Gleiwitz',
               ('SVN', 'ITA'): 'Julian March', ('HR-08', 'ITA'): 'Fiume', ('GR-L', 'ITA'): 'Dodecanese', ('MA-03', 'SPA'): 'Rif',
               ('MA-04', 'SPA'): 'Melilla'}
for u in units:
    if u['iso'] in UNIT_NAMES:
        u['name'] = UNIT_NAMES[u['iso']]

order = sorted(range(len(units)), key=lambda i: -units[i]['area'])
img = Image.new('I', (W, H), 0)
dr = ImageDraw.Draw(img)
for i in order:
    if units[i]['owner'] is None:
        continue
    draw_polys(dr, units[i]['geom'], i + 1, holes_value=0)
uid = np.asarray(img, dtype=np.int32).copy()
del img, dr
log(f"admin-1 raster: {np.count_nonzero(uid)} land pixels")

# lakes (big ones become lake locations; small ones stay land)
lake_feats = [f for f in load('ne_10m_lakes.geojson') if (f['properties'].get('scalerank') or 9) <= 5]
img = Image.new('I', (W, H), 0)
dr = ImageDraw.Draw(img)
lake_names = {}
for k, f in enumerate(sorted(lake_feats, key=lambda f: f['properties'].get('scalerank') or 9)):
    draw_polys(dr, f['geometry'], k + 1, holes_value=None)
    lake_names[k + 1] = ascii_name(f['properties'].get('name') or f['properties'].get('name_alt') or 'Lake')
lake = np.asarray(img, dtype=np.int32).copy()
del img, dr
counts = np.bincount(lake.ravel(), minlength=len(lake_names) + 1)
small = np.where(counts < 70)[0]
lake[np.isin(lake, small)] = 0
log(f"lakes kept: {len([c for c in counts[1:] if c >= 70])}")
uid[lake > 0] = 0

# ----------------------------------------------------------------------------------------- land cleanup
land = uid > 0
lab, n = ndimage.label(land)
sizes = np.bincount(lab.ravel())
tiny = np.where(sizes < 8)[0]
tiny = tiny[tiny > 0]
land[np.isin(lab, tiny)] = False
uid[~land] = 0
# small enclosed water holes (not lakes) become land of the nearest unit
water = ~land & (lake == 0)
wl, wn = ndimage.label(water)
wsizes = np.bincount(wl.ravel())
border_labels = set(np.unique(np.concatenate([wl[0], wl[-1], wl[:, 0], wl[:, -1]])))
holes = [i for i in range(1, wn + 1) if wsizes[i] < 40 and i not in border_labels]
holemask = np.isin(wl, holes)
if holemask.any():
    _, (iy, ix) = ndimage.distance_transform_edt(uid == 0, return_indices=True)
    uid[holemask] = uid[iy[holemask], ix[holemask]]
    del iy, ix
land = uid > 0
del lab, wl
log(f"land pixels after cleanup: {land.sum()}")

# ----------------------------------------------------------------------------------------- splits (1930 borders inside units)
iso_index = collections.defaultdict(list)
for i, u in enumerate(units):
    iso_index[u['iso']].append(i)
    iso_index[u['adm0']].append(i)
for key, owner, test in D.SPLITS:
    targets = [i for i in iso_index.get(key, [])]
    for i in targets:
        if units[i]['owner'] == owner:
            continue
        ys, xs = np.nonzero(uid == i + 1)
        if len(ys) == 0:
            continue
        sel = np.array([test(float(COL_LON[x]), float(ROW_LAT[y])) for y, x in zip(ys, xs)], dtype=bool)
        if not sel.any():
            continue
        new = dict(units[i])
        new['owner'] = owner
        new['name'] = SPLIT_NAMES.get((key, owner), units[i]['name'])
        new['split'] = True
        units.append(new)
        uid[ys[sel], xs[sel]] = len(units)
log(f"units after splits: {len(units)}")

# ----------------------------------------------------------------------------------------- elevation
log("elevation ...")
elev_src = np.load(ELEV)  # web mercator zoom 5, rows from tile y=4 (pixel 1024 of 8192)
gy = ((1.0 - (MTOP - (np.arange(H) + 0.5) / W * 2 * np.pi) / np.pi) / 2.0 * 8192.0 - 1024.0).astype(np.float32)
gx = ((np.arange(W) + 0.5) / W * 8192.0).astype(np.float32)
elev = np.empty((H, W), np.float32)
for y0 in range(0, H, 256):
    yy = gy[y0:y0 + 256]
    YY, XX = np.meshgrid(yy, gx, indexing='ij')
    elev[y0:y0 + 256] = ndimage.map_coordinates(elev_src, [YY, XX], order=1, mode='nearest')
del elev_src
elev[land & (elev < 1)] = 1
log(f"elevation range {elev.min():.0f} .. {elev.max():.0f}")

# ----------------------------------------------------------------------------------------- terrain
log("terrain ...")
LAT2 = np.broadcast_to(ROW_LAT[:, None], (H, W))
LON2 = np.broadcast_to(COL_LON[None, :], (H, W))


def noise(scale, seed):
    r = np.random.default_rng(seed)
    g = r.random((H // scale + 2, W // scale + 2)).astype(np.float32)
    z = ndimage.zoom(g, scale, order=3)[:H, :W]
    return ndimage.gaussian_filter(z, scale / 4)


def region_mask(classes, exclude=()):
    img = Image.new('L', (W, H), 0)
    d = ImageDraw.Draw(img)
    for f in load('ne_10m_geography_regions_polys.geojson'):
        p = f['properties']
        if p.get('FEATURECLA') in classes and p.get('NAME') not in exclude:
            draw_polys(d, f['geometry'], 255)
    return np.asarray(img) > 0


desert_poly = region_mask({'Desert'}, exclude={'CAATINGAS', 'Entre Rios', 'PUNJAB', 'Big Bend'})
wet_poly = region_mask({'Wetlands', 'Delta'})
tundra_poly = region_mask({'Tundra'}, exclude={'CANADIAN SHIELD'})
img = Image.new('L', (W, H), 0)
d = ImageDraw.Draw(img)
for f in load('ne_10m_glaciated_areas.geojson'):
    draw_polys(d, f['geometry'], 255)
glacier = (np.asarray(img) > 0) & land
del img, d

n1 = noise(48, 1)
n2 = noise(16, 2)
slope = ndimage.generic_gradient_magnitude(ndimage.gaussian_filter(elev, 2), ndimage.sobel)
T_PLAINS, T_FOREST, T_HILLS, T_MOUNT, T_DESERT, T_MARSH, T_OCEAN, T_JUNGLE, T_TUNDRA, T_GLACIER = range(10)
ter = np.full((H, W), T_PLAINS, np.uint8)
# climate zones with wobbly edges (latitude/longitude moved by large-scale noise)
n3 = noise(96, 3)
n4 = noise(96, 4)
LAT = LAT2 + (n3 - 0.5) * 9.0
LON = LON2 + (n4 - 0.5) * 12.0
alat = np.abs(LAT)
boreal = (LAT > 49) & (LAT < 68)
ter[boreal & (n1 + 0.25 * n2 > 0.4)] = T_FOREST
temperate = (alat >= 28) & (alat <= 50)
ter[temperate & (n1 + 0.3 * n2 > 0.72)] = T_FOREST
tropic = (alat < 11) & (
    ((LON > -80) & (LON < -44)) | ((LON > 8) & (LON < 31)) | ((LON > 94) & (LON < 155)) | ((LON > -92) & (LON < -76) & (LAT > 5)))
ter[tropic & (n1 + 0.3 * n2 > 0.33)] = T_JUNGLE
ter[(alat >= 10) & (alat < 24) & (n1 + 0.3 * n2 > 0.8)] = T_FOREST
# dry lands: Natural Earth desert areas plus the Sahara-Arabia belt and inner Australia, with soft edges
sahara = (LAT > 16) & (LAT < 31.5) & (LON > -17) & (LON < 58) & ~((LON > 32) & (LON < 36) & (LAT < 23) & False)
australia = (((LAT + 25.5) / 8.5) ** 2 + ((LON - 131.0) / 13.0) ** 2) < 1.0
dry = desert_poly | sahara | australia
ter[dry] = T_DESERT
# wetlands
pripet = (((LAT2 - 52.0) / 0.8) ** 2 + ((LON2 - 27.5) / 3.2) ** 2 < 1) & (noise(24, 9) > 0.35)
sudd = (((LAT2 - 8.0) / 1.5) ** 2 + ((LON2 - 30.8) / 1.4) ** 2 < 1)
ter[(wet_poly | pripet | sudd) & (elev < 400)] = T_MARSH
# cold
ter[(LAT >= 67.0) | tundra_poly | ((LAT > 60) & (LON2 > 60) & (elev > 900))] = T_TUNDRA
# relief
ter[((elev > 600) & (slope > 900)) | (elev > 1700)] = T_HILLS
ter[(elev > 2900) | ((elev > 1500) & (slope > 2600))] = T_MOUNT
ter[glacier] = T_GLACIER
ter[~land] = T_OCEAN
del n1, n2, n3, n4, LAT, LON, slope, desert_poly, wet_poly, tundra_poly, dry, boreal, temperate, tropic, sahara, australia
TER_COLORS = [(86, 124, 27), (0, 86, 6), (112, 74, 31), (65, 42, 17), (206, 169, 99), (75, 147, 174), (8, 31, 130),
              (20, 110, 60), (150, 160, 140), (235, 240, 245)]
log("terrain counts:", np.bincount(ter.ravel(), minlength=10))

# ----------------------------------------------------------------------------------------- population density weight
log("density ...")
places = [f['properties'] for f in load('ne_10m_populated_places.geojson')]
sc = 8
dens = np.zeros((H // sc + 1, W // sc + 1), np.float64)
for p in places:
    la, lo = p['LATITUDE'], p['LONGITUDE']
    if la > TOP_LAT or la < BOTTOM_LAT:
        continue
    dens[int(py(la) / sc), int(px(lo) / sc) % (W // sc)] += max(1000.0, float(p.get('POP_MAX') or 0))
dens = ndimage.gaussian_filter(dens, 5)
dens = np.log1p(dens / 2000.0)
dens = ndimage.zoom(dens, sc, order=1)[:H, :W].astype(np.float32)
lv = dens[land]
lo_, hi_ = np.percentile(lv, 5), np.percentile(lv, 99)
dens = np.clip((dens - lo_) / max(1e-6, hi_ - lo_), 0, 1)
area = (ROW_COS ** 2)[:, None].astype(np.float32)  # true area of a pixel relative to the equator
# Europe (where most of the wars of the period happen) gets far more detail, like Paradox maps
europe = ((LON2 > -11) & (LON2 < 42) & (LAT2 > 35) & (LAT2 < 71)).astype(np.float32)
mideast = ((LON2 > -10) & (LON2 < 62) & (LAT2 > 28) & (LAT2 < 42)).astype(np.float32)
east_asia = ((LON2 > 100) & (LON2 < 146) & (LAT2 > 18) & (LAT2 < 46)).astype(np.float32)
regional = ndimage.gaussian_filter(1.0 + 3.2 * europe + 0.8 * mideast + 0.6 * east_asia, 25)
del europe, mideast, east_asia
weight = (area * regional * (0.07 + dens ** 1.6)).astype(np.float32)
weight[~land] = 0
log("total land weight", float(weight.sum()))

# ----------------------------------------------------------------------------------------- unit statistics & adjacency
nu = len(units) + 1
flat = uid.ravel()
u_w = np.bincount(flat, weights=weight.ravel(), minlength=nu)
u_px = np.bincount(flat, minlength=nu)


def pair_counts(a):
    pairs = collections.Counter()
    for sa, sb in ((a[:, :-1], a[:, 1:]), (a[:-1, :], a[1:, :])):
        m = (sa != sb) & (sa > 0) & (sb > 0)
        x, y = sa[m], sb[m]
        lo = np.minimum(x, y).astype(np.int64)
        hi = np.maximum(x, y).astype(np.int64)
        keys, cnt = np.unique(lo * 1_000_000 + hi, return_counts=True)
        for k, c in zip(keys, cnt):
            pairs[(int(k // 1_000_000), int(k % 1_000_000))] += int(c)
    return pairs


log("unit adjacency ...")
upairs = pair_counts(uid)
neigh = collections.defaultdict(set)
for (a, b), c in upairs.items():
    neigh[a].add(b)
    neigh[b].add(a)

# ----------------------------------------------------------------------------------------- state clustering
log("states ...")
present = [i for i in range(1, nu) if u_px[i] > 0]
parent = {i: i for i in present}


def find(x):
    while parent[x] != x:
        parent[x] = parent[parent[x]]
        x = parent[x]
    return x


cw = {i: float(u_w[i]) for i in present}
target_state = float(weight.sum()) / TARGET_STATES
# simpler explicit implementation with member lists
members = {i: [i] for i in present}
roots_alive = set(present)


def cluster_neighbors(r):
    out = set()
    for i in members[r]:
        for j in neigh[i]:
            rj = find(j)
            if rj != r:
                out.add(rj)
    return out


merged_any = True
while merged_any:
    merged_any = False
    for r in sorted(roots_alive, key=lambda r: cw[r]):
        if r not in roots_alive or cw[r] >= 0.45 * target_state:
            continue
        owner = units[r - 1]['owner']
        regions = {units[i - 1]['region'] for i in members[r]}
        adm0s = {units[i - 1]['adm0'] for i in members[r]}
        best, best_score = None, None
        for c in cluster_neighbors(r):
            if units[c - 1]['owner'] != owner:
                continue
            same_region = bool(regions & {units[i - 1]['region'] for i in members[c]} - {''})
            same_adm0 = bool(adm0s & {units[i - 1]['adm0'] for i in members[c]})
            score = (0 if same_region else 1, 0 if same_adm0 else 1, cw[c])
            if best_score is None or score < best_score:
                best, best_score = c, score
        if best is None or cw[best] + cw[r] > 2.2 * target_state:
            continue
        parent[r] = best
        members[best].extend(members.pop(r))
        cw[best] += cw.pop(r)
        roots_alive.discard(r)
        merged_any = True
log(f"state clusters: {len(roots_alive)}")
cluster_of_unit = np.zeros(nu, np.int32)
clusters = sorted(roots_alive)
for ci, r in enumerate(clusters):
    for i in members[r]:
        cluster_of_unit[i] = ci + 1
cl = cluster_of_unit[uid]  # cluster raster (0 = water)

# ----------------------------------------------------------------------------------------- locations inside clusters
log("land locations ...")
target_loc = float(weight.sum()) / TARGET_LAND
loc = np.zeros((H, W), np.int32)
loc_state_cluster = [None]  # index = location id
next_loc = 1
objs = ndimage.find_objects(cl)


def kmeans(pts, w, k, iters=12):
    """Weighted k-means++ on 2D points. Returns centers."""
    n = len(pts)
    if k >= n:
        return pts.astype(np.float64)
    p = w / w.sum()
    centers = [pts[rng.choice(n, p=p)]]
    d2 = ((pts - centers[0]) ** 2).sum(1)
    for _ in range(1, k):
        prob = d2 * w
        s = prob.sum()
        idx = rng.choice(n, p=prob / s) if s > 0 else rng.integers(n)
        centers.append(pts[idx])
        d2 = np.minimum(d2, ((pts - pts[idx]) ** 2).sum(1))
    c = np.array(centers, np.float64)
    for _ in range(iters):
        _, a = cKDTree(c).query(pts)
        sw = np.bincount(a, weights=w, minlength=k)
        sx = np.bincount(a, weights=w * pts[:, 0], minlength=k)
        sy = np.bincount(a, weights=w * pts[:, 1], minlength=k)
        ok = sw > 0
        c[ok, 0] = sx[ok] / sw[ok]
        c[ok, 1] = sy[ok] / sw[ok]
    return c


for ci, sl in enumerate(objs, start=1):
    if sl is None:
        continue
    sub = cl[sl] == ci
    ys, xs = np.nonzero(sub)
    wsub = weight[sl][sub].astype(np.float64)
    wtot = wsub.sum()
    k = int(round(wtot / target_loc))
    k = max(1, min(k, len(ys) // 70 if len(ys) >= 70 else 1))
    pts = np.stack([xs, ys], 1).astype(np.float64)
    if k == 1:
        lab_ = np.zeros(len(ys), np.int64)
    else:
        if len(pts) > 40000:
            pick = rng.choice(len(pts), 40000, replace=False, p=wsub / wtot)
            cen = kmeans(pts[pick], np.ones(len(pick)), k)
        else:
            cen = kmeans(pts, wsub + 1e-9, k)
        _, lab_ = cKDTree(cen).query(pts)
    used = np.unique(lab_)
    remap = {v: next_loc + j for j, v in enumerate(used)}
    ids = np.vectorize(remap.get)(lab_) if len(used) > 1 else np.full(len(ys), next_loc)
    loc[sl][sub] = ids
    for j in range(len(used)):
        loc_state_cluster.append(ci)
    next_loc += len(used)
n_land = next_loc - 1
log(f"land locations: {n_land}")


def fix_fragments(locmap, groups, first, last):
    """Keeps each location in one piece where it touches another location of the same group."""
    sl_all = ndimage.find_objects(locmap)
    moved = 0
    for lid in range(first, last + 1):
        sl = sl_all[lid - 1] if lid - 1 < len(sl_all) else None
        if sl is None:
            continue
        ys0, ys1 = max(0, sl[0].start - 1), min(H, sl[0].stop + 1)
        xs0, xs1 = max(0, sl[1].start - 1), min(W, sl[1].stop + 1)
        box = locmap[ys0:ys1, xs0:xs1]
        m = box == lid
        lab_, nlab = ndimage.label(m)
        if nlab <= 1:
            continue
        sizes = np.bincount(lab_.ravel())
        sizes[0] = 0
        keep = sizes.argmax()
        for c in range(1, nlab + 1):
            if c == keep:
                continue
            frag = lab_ == c
            ring = ndimage.binary_dilation(frag) & ~frag
            nb = box[ring]
            nb = nb[(nb > 0) & (nb != lid)]
            nb = [v for v in nb if groups[v] == groups[lid]]
            if not nb:
                continue
            box[frag] = collections.Counter(nb).most_common(1)[0][0]
            moved += 1
    return moved


log("fragments moved:", fix_fragments(loc, loc_state_cluster, 1, n_land))

# ----------------------------------------------------------------------------------------- seas and lakes
log("sea zones ...")
ocean = ~land & (lake == 0)
dist_px, (iy, ix) = ndimage.distance_transform_edt(ocean, return_indices=True)
km = dist_px * (KM_PER_PX_EQ * ROW_COS)[:, None]
del iy, ix
sea_w = (area * np.where(km < 150, 1.0, (150.0 / np.maximum(km, 1)) ** 1.25)).astype(np.float32)
sea_w = np.maximum(sea_w, 0.02 * area)
sea_w *= regional ** 1.4  # finer sea zones around Europe
del regional
sea_w[~ocean] = 0
ys, xs = np.nonzero(ocean)
wv = sea_w[ys, xs].astype(np.float64)
pick = rng.choice(len(ys), 120000, replace=False, p=wv / wv.sum())
cen = kmeans(np.stack([xs[pick], ys[pick]], 1).astype(np.float64), np.ones(len(pick)), TARGET_SEA, iters=15)
_, lab_ = cKDTree(cen).query(np.stack([xs, ys], 1).astype(np.float64))
loc[ys, xs] = n_land + 1 + lab_
del km, sea_w, wv, lab_
n_sea_raw = TARGET_SEA
sea_group = [None] * (n_land + 1) + ['sea'] * n_sea_raw
log("sea fragments moved:", fix_fragments(loc, sea_group, n_land + 1, n_land + n_sea_raw))
# lakes
lake_loc = {}
next_id = n_land + n_sea_raw + 1
for lk in np.unique(lake):
    if lk == 0:
        continue
    m = lake == lk
    n_px = int(m.sum())
    k = 1 if n_px < 30000 else min(4, n_px // 30000 + 1)
    if k == 1:
        loc[m] = next_id
        lake_loc[next_id] = (lake_names[lk], 0)
        next_id += 1
    else:
        yy, xx = np.nonzero(m)
        pts = np.stack([xx, yy], 1).astype(np.float64)
        sel = rng.choice(len(pts), min(20000, len(pts)), replace=False)
        c = kmeans(pts[sel], np.ones(len(sel)), k)
        _, a = cKDTree(c).query(pts)
        for j in range(k):
            loc[yy[a == j], xx[a == j]] = next_id
            lake_loc[next_id] = (lake_names[lk], j + 1)
            next_id += 1
# compact ids (drop empty)
counts = np.bincount(loc.ravel(), minlength=next_id)
alive = [i for i in range(1, next_id) if counts[i] > 0]
remap = np.zeros(next_id, np.int32)
for new, old in enumerate(alive, start=1):
    remap[old] = new
loc = remap[loc]
old_of = {new: old for new, old in enumerate(alive, start=1)}
NLOC = len(alive)
kind = {}
for new, old in old_of.items():
    kind[new] = 'land' if old <= n_land else 'sea' if old <= n_land + n_sea_raw else 'lake'
log(f"locations: {NLOC} (land {sum(1 for v in kind.values() if v == 'land')}, sea {sum(1 for v in kind.values() if v == 'sea')}, lake {sum(1 for v in kind.values() if v == 'lake')})")
assert (loc > 0).all(), "every pixel must belong to a location"

# ----------------------------------------------------------------------------------------- per-location info
flat = loc.ravel()
cnt = np.bincount(flat, minlength=NLOC + 1)
sx = np.bincount(flat, weights=np.broadcast_to(np.arange(W, dtype=np.float64)[None, :], (H, W)).ravel(), minlength=NLOC + 1)
sy = np.bincount(flat, weights=np.broadcast_to(np.arange(H, dtype=np.float64)[:, None], (H, W)).ravel(), minlength=NLOC + 1)
cx, cy = sx / np.maximum(cnt, 1), sy / np.maximum(cnt, 1)
loc_cluster = np.zeros(NLOC + 1, np.int32)
for new, old in old_of.items():
    if kind[new] == 'land':
        loc_cluster[new] = loc_state_cluster[old]
# dominant unit of each land location
pair = flat.astype(np.int64) * (len(units) + 2) + uid.ravel()
keys, c2 = np.unique(pair[uid.ravel() > 0], return_counts=True)
loc_unit = np.zeros(NLOC + 1, np.int32)
best = np.zeros(NLOC + 1, np.int64)
for k_, c_ in zip(keys, c2):
    l_, u_ = int(k_ // (len(units) + 2)), int(k_ % (len(units) + 2))
    if c_ > best[l_]:
        best[l_], loc_unit[l_] = c_, u_
del pair, keys, c2

# ----------------------------------------------------------------------------------------- states (split big clusters)
log("final states ...")
state_of_loc = np.zeros(NLOC + 1, np.int32)
states = []  # dict(name, owner, units set, locs)
by_cluster = collections.defaultdict(list)
for l_ in range(1, NLOC + 1):
    if kind[l_] == 'land':
        by_cluster[loc_cluster[l_]].append(l_)
DIRS = ['North', 'South', 'East', 'West', 'Central', 'Northeast', 'Northwest', 'Southeast', 'Southwest']


def direction(dx, dy):
    ang = math.degrees(math.atan2(-dy, dx)) % 360
    return ['East', 'Northeast', 'North', 'Northwest', 'West', 'Southwest', 'South', 'Southeast'][int((ang + 22.5) // 45) % 8]


for ci in sorted(by_cluster):
    ls = by_cluster[ci]
    r = clusters[ci - 1]
    us = members[r]
    owner = units[r - 1]['owner']
    big_unit = max(us, key=lambda i: u_w[i])
    regions = collections.Counter(units[i - 1]['region'] for i in us if units[i - 1]['region'])
    if len(us) >= 3 and regions and regions.most_common(1)[0][1] == len(us) and not units[big_unit - 1].get('split'):
        base_name = ascii_name(regions.most_common(1)[0][0])
    else:
        base_name = units[big_unit - 1]['name']
    groups = [ls]
    if len(ls) > MAX_STATE_LOCATIONS:
        k = int(math.ceil(len(ls) / 7.0))
        pts = np.array([[cx[l_], cy[l_]] for l_ in ls])
        c = kmeans(pts, np.array([cnt[l_] for l_ in ls], np.float64), k)
        _, a = cKDTree(c).query(pts)
        groups = [[ls[j] for j in range(len(ls)) if a[j] == g] for g in range(k)]
        groups = [g for g in groups if g]
    gx_all = np.average([cx[l_] for l_ in ls], weights=[cnt[l_] for l_ in ls])
    gy_all = np.average([cy[l_] for l_ in ls], weights=[cnt[l_] for l_ in ls])
    used_names = set()
    for g in groups:
        name = base_name
        if len(groups) > 1:
            gx_ = np.average([cx[l_] for l_ in g], weights=[cnt[l_] for l_ in g])
            gy_ = np.average([cy[l_] for l_ in g], weights=[cnt[l_] for l_ in g])
            dname = direction(gx_ - gx_all, gy_ - gy_all) if math.hypot(gx_ - gx_all, gy_ - gy_all) > 3 else 'Central'
            name = f"{dname} {base_name}"
            n_ = 2
            while name in used_names:
                name = f"{dname} {base_name} {n_}"
                n_ += 1
        used_names.add(name)
        states.append(dict(name=name, owner=owner, locs=g, units={loc_unit[l_] for l_ in g}))
        for l_ in g:
            state_of_loc[l_] = len(states)
log(f"states: {len(states)}")

# ----------------------------------------------------------------------------------------- names
log("names ...")
loc_names = {}
place_best = {}
for p in places:
    la, lo = p['LATITUDE'], p['LONGITUDE']
    if la > TOP_LAT or la < BOTTOM_LAT:
        continue
    x, y = int(px(lo)) % W, min(H - 1, int(py(la)))
    l_ = int(loc[y, x])
    if kind.get(l_) != 'land':
        continue
    pop = float(p.get('POP_MAX') or 0) * (3 if p.get('ADM0CAP') else 1) * (1.5 if p.get('FEATURECLA', '').startswith('Admin-1') else 1)
    if l_ not in place_best or pop > place_best[l_][0]:
        nm = ascii_name(p.get('NAMEASCII') or p.get('NAME'))
        place_best[l_] = (pop, D.CITY_1930.get(nm, nm))
for l_, (_, nm) in place_best.items():
    loc_names[l_] = nm
for si, st in enumerate(states, start=1):
    unnamed = [l_ for l_ in st['locs'] if l_ not in loc_names]
    if not unnamed:
        continue
    sxm = np.average([cx[l_] for l_ in st['locs']], weights=[cnt[l_] for l_ in st['locs']])
    sym = np.average([cy[l_] for l_ in st['locs']], weights=[cnt[l_] for l_ in st['locs']])
    base = st['name']
    for l_ in unnamed:
        if len(st['locs']) == 1:
            loc_names[l_] = base
        else:
            loc_names[l_] = f"{direction(cx[l_] - sxm, cy[l_] - sym)} {base}" if math.hypot(cx[l_] - sxm, cy[l_] - sym) > 2 else base
# seas: majority marine area name
marine = load('ne_10m_geography_marine_polys.geojson')
img = Image.new('I', (W, H), 0)
d = ImageDraw.Draw(img)
mnames = {}
for k_, f in enumerate(sorted(marine, key=lambda f: -(f['properties'].get('scalerank') or 0))):
    draw_polys(d, f['geometry'], k_ + 1)
    mnames[k_ + 1] = ascii_name(f['properties'].get('name') or '')
mar = np.asarray(img, dtype=np.int32)
del img, d
sea_ids = [l_ for l_ in range(1, NLOC + 1) if kind[l_] == 'sea']
m = (mar > 0) & np.isin(loc, sea_ids)
pk = loc[m].astype(np.int64) * 1000 + mar[m]
keys, c2 = np.unique(pk, return_counts=True)
bestm = {}
for k_, c_ in zip(keys, c2):
    l_, mm = int(k_ // 1000), int(k_ % 1000)
    if mnames.get(mm) and c_ > bestm.get(l_, (0, 0))[0]:
        bestm[l_] = (c_, mnames[mm])
del mar, m, pk


def ocean_name(l_):
    lon = float(COL_LON[min(W - 1, int(cx[l_]))])
    lat = float(ROW_LAT[min(H - 1, int(cy[l_]))])
    if lat > 66:
        return 'Arctic Ocean'
    if lat < -50:
        return 'Southern Ocean'
    if -70 < lon < 20 and not (lat < 0 and lon > -60 and lon < 20 and False):
        return 'North Atlantic Ocean' if lat > 0 else 'South Atlantic Ocean'
    if 20 <= lon < 120 and lat < 25:
        return 'Indian Ocean'
    return 'North Pacific Ocean' if lat > 0 else 'South Pacific Ocean'


for l_ in sea_ids:
    loc_names[l_] = bestm[l_][1] if l_ in bestm else ocean_name(l_)
for l_ in range(1, NLOC + 1):
    if kind[l_] == 'lake':
        nm, part = lake_loc[old_of[l_]]
        loc_names[l_] = nm if part == 0 else f"{nm} {['', 'North', 'South', 'East', 'West'][part]}".strip()
# make names unique (display names may repeat; keys may not)
groups = collections.defaultdict(list)
for l_, nm in loc_names.items():
    groups[nm].append(l_)
for nm, ls in groups.items():
    if len(ls) <= 1:
        continue
    if kind[ls[0]] in ('sea', 'lake'):
        mx = np.mean([cx[l_] for l_ in ls])
        my = np.mean([cy[l_] for l_ in ls])
        taken = set()
        for l_ in ls:
            dn = direction(cx[l_] - mx, cy[l_] - my)
            new = f"{nm} ({dn})"
            k_ = 2
            while new in taken:
                new = f"{nm} ({dn} {k_})"
                k_ += 1
            taken.add(new)
            loc_names[l_] = new
loc_keys = {}
used = set()
for l_ in range(1, NLOC + 1):
    base = slug(loc_names[l_])
    if kind[l_] == 'land' and base in used:
        base = slug(f"{loc_names[l_]} {states[state_of_loc[l_] - 1]['name']}")
    key, k_ = base, 2
    while key in used:
        key = f"{base}_{k_}"
        k_ += 1
    used.add(key)
    loc_keys[l_] = key
state_keys = []
used_s = set()
for st in states:
    base = slug(st['name']) + '_state'
    key, k_ = base, 2
    while key in used_s:
        key = f"{slug(st['name'])}_{k_}_state"
        k_ += 1
    used_s.add(key)
    state_keys.append(key)

# ----------------------------------------------------------------------------------------- population, tax, manpower
log("population ...")
adm0_of_unit = np.array([''] + [u['adm0'] for u in units], dtype=object)
unit_pw = np.bincount(uid.ravel(), weights=(dens * area).ravel(), minlength=nu)  # population weight per unit
adm0_pw = collections.Counter()
for i in range(1, nu):
    adm0_pw[units[i - 1]['adm0']] += unit_pw[i]
unit_pop = np.zeros(nu)
for i in range(1, nu):
    a3 = units[i - 1]['adm0']
    sub, pop = ADM0.get(a3, ('', 0.0))
    if pop <= 0 or adm0_pw[a3] <= 0:
        continue
    unit_pop[i] = pop * D.POP_FACTOR.get(sub, 0.2) * unit_pw[i] / adm0_pw[a3]
# per location population share by its pixels' unit and weight
loc_pw = np.bincount(loc.ravel(), weights=(dens * area * (uid > 0)).ravel(), minlength=NLOC + 1)
loc_unit_pop = np.zeros(NLOC + 1)
unit_loc_pw = collections.Counter()
for l_ in range(1, NLOC + 1):
    if kind[l_] == 'land':
        unit_loc_pw[loc_unit[l_]] += loc_pw[l_]
for l_ in range(1, NLOC + 1):
    if kind[l_] == 'land' and unit_loc_pw[loc_unit[l_]] > 0:
        loc_unit_pop[l_] = unit_pop[loc_unit[l_]] * loc_pw[l_] / unit_loc_pw[loc_unit[l_]]


def is_colony(owner, a3):
    home = D.HOMELAND.get(owner)
    return home is not None and a3 not in home


for st in states:
    st['pop'] = sum(loc_unit_pop[l_] for l_ in st['locs'])
    a3s = collections.Counter(units[loc_unit[l_] - 1]['adm0'] for l_ in st['locs'])
    st['adm0'] = a3s.most_common(1)[0][0]
    st['colony'] = is_colony(st['owner'], st['adm0'])
    dev = D.COUNTRIES[st['owner']][6] if st['owner'] in D.COUNTRIES else 0.1
    if st['colony']:
        dev = min(dev, 0.12)
    pm = st['pop'] / 1e6
    st['tax'] = round(max(0.3, min(12.0, 0.3 + pm * dev * 1.3)), 1)
    st['manpower'] = int(round(10 + pm * 1000 * 0.12 * (0.3 if st['colony'] else 1.0)))

# ----------------------------------------------------------------------------------------- countries present
owners_present = collections.Counter(st['owner'] for st in states)
missing = [t for t in owners_present if t not in D.COUNTRIES]
if missing:
    raise SystemExit(f"owners without country definition: {missing}")
log(f"countries with land: {len(owners_present)}")

# capitals
capital_loc = {}
for tag in owners_present:
    la, lo = D.COUNTRIES[tag][2]
    x, y = int(px(lo)) % W, min(H - 1, max(0, int(py(la))))
    l_ = int(loc[y, x])
    if kind.get(l_) != 'land' or states[state_of_loc[l_] - 1]['owner'] != tag:
        own = [l2 for st in states if st['owner'] == tag for l2 in st['locs']]
        l_ = min(own, key=lambda l2: (cx[l2] - x) ** 2 + (cy[l2] - y) ** 2)
    capital_loc[tag] = l_

# ----------------------------------------------------------------------------------------- straits
log("straits ...")
land_ids = np.array([kind.get(i) == 'land' for i in range(NLOC + 1)])
landmask = land_ids[loc]
lab_l, nl = ndimage.label(landmask)
comp_sizes = np.bincount(lab_l.ravel())
straits = {}
STRAIT_PX = 7
objs = ndimage.find_objects(lab_l)
for c in range(1, nl + 1):
    sl = objs[c - 1]
    if sl is None or comp_sizes[c] < 20:
        continue
    y0, y1 = max(0, sl[0].start - STRAIT_PX), min(H, sl[0].stop + STRAIT_PX)
    x0, x1 = max(0, sl[1].start - STRAIT_PX), min(W, sl[1].stop + STRAIT_PX)
    box = lab_l[y0:y1, x0:x1]
    mine = box == c
    dist, (iy, ix) = ndimage.distance_transform_edt(~mine, return_indices=True)
    other = (box > 0) & (box != c) & (dist <= STRAIT_PX)
    if not other.any():
        continue
    oy, ox = np.nonzero(other)
    for yy, xx in zip(oy, ox):
        a = int(loc[y0 + iy[yy, xx], x0 + ix[yy, xx]])
        b = int(loc[y0 + yy, x0 + xx])
        key = (min(a, b), max(a, b))
        dd = dist[yy, xx]
        comp_pair = (min(c, int(box[yy, xx])), max(c, int(box[yy, xx])))
        if comp_pair not in straits or dd < straits[comp_pair][1]:
            straits[comp_pair] = (key, dd)
strait_pairs = sorted({v[0] for v in straits.values()})
log(f"straits: {len(strait_pairs)}")
del lab_l

# ----------------------------------------------------------------------------------------- write files
log("writing ...")
os.makedirs(os.path.join(OUT, 'map', 'states'), exist_ok=True)
for d_ in ('common/countries', 'gfx/flags', 'gfx/terrain', 'history', 'localisation'):
    os.makedirs(os.path.join(OUT, d_), exist_ok=True)

# colors
colors = {}
seen = set()
for l_ in range(1, NLOC + 1):
    v = (l_ * 2654435761) & 0xFFFFFF
    while v in seen or v in (0, 0xFFFFFF):
        v = (v * 1103515245 + 12345) & 0xFFFFFF
    seen.add(v)
    colors[l_] = v
lut = np.zeros((NLOC + 1, 3), np.uint8)
for l_, v in colors.items():
    lut[l_] = ((v >> 16) & 255, (v >> 8) & 255, v & 255)
Image.fromarray(lut[loc], 'RGB').save(os.path.join(OUT, 'map', 'provinces.png'), optimize=True)

SEA_LEVEL = 20
h = np.zeros((H, W), np.float32)
hl = SEA_LEVEL + 2 + np.power(np.clip(elev, 0, 7000) / 7000.0, 0.55) * (255 - SEA_LEVEL - 3)
h[landmask] = hl[landmask]
h[~landmask] = np.clip(SEA_LEVEL - 1 + np.clip(elev[~landmask], -6000, 0) / 450.0, 2, SEA_LEVEL - 1)
Image.fromarray(np.clip(h, 0, 255).astype(np.uint8), 'L').save(os.path.join(OUT, 'map', 'heightmap.png'), optimize=True)
del h, hl

ter[~landmask] = T_OCEAN
ter[landmask & (ter == T_OCEAN)] = T_PLAINS
tc = np.array(TER_COLORS, np.uint8)
Image.fromarray(tc[ter], 'RGB').save(os.path.join(OUT, 'map', 'terrain.png'), optimize=True)
tree_density = np.array([18, 190, 60, 40, 0, 70, 0, 235, 25, 0], np.float32)[ter]
tree_density *= np.clip(0.55 + noise(12, 7), 0, 1.3)
Image.fromarray(np.clip(tree_density, 0, 255).astype(np.uint8), 'L').save(os.path.join(OUT, 'map', 'trees.png'), optimize=True)
del tree_density

riv = Image.new('RGB', (W, H), (255, 255, 255))
d = ImageDraw.Draw(riv)
for f in load('ne_10m_rivers_lake_centerlines.geojson'):
    p = f['properties']
    sr = p.get('scalerank') or 10
    if sr > 6 or p.get('featurecla') == 'Lake Centerline':
        continue
    width = 2 if sr <= 3 else 1
    g = f['geometry']
    lines = [g['coordinates']] if g['type'] == 'LineString' else g['coordinates'] if g['type'] == 'MultiLineString' else []
    for line in lines:
        pts = [(px(x), py(y)) for x, y in line if BOTTOM_LAT <= y <= TOP_LAT]
        if len(pts) >= 2:
            d.line(pts, fill=(40, 90, 200), width=width)
riv_a = np.asarray(riv).copy()
riv_a[~landmask] = 255
Image.fromarray(riv_a, 'RGB').save(os.path.join(OUT, 'map', 'rivers.png'), optimize=True)
del riv, riv_a

with open(os.path.join(OUT, 'map', 'default.map'), 'w', newline='\n') as f:
    f.write('# Earth 1930 (generated by tools/earth/make_earth.py). Mercator, 78N to %.0fS.\n' % -BOTTOM_LAT)
    f.write('provinces = "provinces.png"\nheightmap = "heightmap.png"\nterrain = "terrain.png"\ntrees = "trees.png"\nrivers = "rivers.png"\n'
            'locations = "locations.txt"\nadjacencies = "adjacencies.txt"\nstates = "states"\n\n'
            f'sea_level = {SEA_LEVEL}\nheight_scale = 9\npixels_per_unit = 10\ntree_density = 1.0\ntree_spacing = 14\n'
            'river_background = { 255 255 255 }\nwater_color = { 30 66 112 }\nriver_color = { 46 96 158 }\n')

with open(os.path.join(OUT, 'map', 'locations.txt'), 'w', newline='\n', encoding='utf-8') as f:
    f.write('# Locations: color in provinces.png = { name, shown name, type }. Generated from Natural Earth (public domain).\n')
    for l_ in range(1, NLOC + 1):
        v = colors[l_]
        t = '' if kind[l_] == 'land' else f' type = {kind[l_]}'
        f.write(f'{v:06X} = {{ name = {loc_keys[l_]} display_name = "{loc_names[l_]}"{t} }}\n')

with open(os.path.join(OUT, 'map', 'adjacencies.txt'), 'w', newline='\n') as f:
    f.write('# Straits: land connections across narrow water (found automatically).\n')
    for a, b in strait_pairs:
        f.write(f'adjacency = {{ from = {loc_keys[a]} to = {loc_keys[b]} type = strait }}\n')

with open(os.path.join(OUT, 'map', 'states', '00_states.txt'), 'w', newline='\n', encoding='utf-8') as f:
    f.write('# States of 1930 (owner, cores, manpower, tax, locations). Generated; edit freely.\n')
    for si, st in enumerate(states):
        cores = '' if st['colony'] else f"\n\tcores = {{ {st['owner']} }}"
        cat = '\n\tcategory = colony' if st['colony'] else ''
        f.write(f"{state_keys[si]} = {{\n\tname = \"{st['name']}\"\n\towner = {st['owner']}{cores}\n\tmanpower = {st['manpower']}\n"
                f"\ttax = {st['tax']}{cat}\n\tlocations = {{ {' '.join(loc_keys[l_] for l_ in st['locs'])} }}\n}}\n")


# air regions (air feature): groups of neighbouring states, about TARGET_AIR_REGIONS for the world
TARGET_AIR_REGIONS = 170
st_pts = np.array([[np.average([cx[l_] for l_ in st['locs']], weights=[cnt[l_] for l_ in st['locs']]),
                    np.average([cy[l_] for l_ in st['locs']], weights=[cnt[l_] for l_ in st['locs']])] for st in states])
st_w = np.array([sum(cnt[l_] for l_ in st['locs']) * float(ROW_COS[min(H - 1, int(st_pts[i][1]))]) ** 2 for i, st in enumerate(states)])
reg_w = st_w + st_w.mean() * 0.5  # balance by area, with a floor so small European states still group
acen = kmeans(st_pts, reg_w, TARGET_AIR_REGIONS, iters=20)
_, areg = cKDTree(acen).query(st_pts)
with open(os.path.join(OUT, 'map', 'air_regions.txt'), 'w', newline='\n', encoding='utf-8') as f:
    f.write('# Air regions (air feature): air wings are assigned to regions, and air superiority is fought per region.\n'
            '# region_key = { name = "Shown name" states = { state_keys } }\n')
    used_r = set()
    for r_ in range(TARGET_AIR_REGIONS):
        idx = [i for i in range(len(states)) if areg[i] == r_]
        if not idx:
            continue
        main = max(idx, key=lambda i: st_w[i])
        nm = states[main]['name']
        if nm in used_r:
            nm = f"{nm} {r_}"
        used_r.add(nm)
        f.write(f'air_region_{r_ + 1} = {{\n\tname = "{nm}"\n\tstates = {{ {" ".join(state_keys[i] for i in idx)} }}\n}}\n')


def make_flag(tag, cmds, path):
    fw, fh = 96, 64
    im = Image.new('RGB', (fw, fh), (255, 255, 255))
    dd = ImageDraw.Draw(im)
    canton_box = None
    for c in cmds:
        op = c[0]
        if op == 'solid':
            dd.rectangle([0, 0, fw, fh], fill=c[1])
        elif op in ('h', 'h3', 'h5'):
            cols = c[1:]
            if op == 'h3':
                b = [0, fh // 4, fh * 3 // 4, fh]
                for i, col in enumerate(cols):
                    dd.rectangle([0, b[i], fw, b[i + 1]], fill=col)
                continue
            for i, col in enumerate(cols):
                dd.rectangle([0, fh * i // len(cols), fw, fh * (i + 1) // len(cols)], fill=col)
        elif op == 'h2':
            dd.rectangle([0, 0, fw, fh // 2], fill=c[1])
            dd.rectangle([0, fh // 2, fw, fh], fill=c[2])
        elif op == 'hr':
            dd.rectangle([0, 0, fw, int(fh * c[1])], fill=c[2])
            dd.rectangle([0, int(fh * c[1]), fw, int(fh * (c[1] + (1 - c[1]) / 2))], fill=c[3])
            dd.rectangle([0, int(fh * (c[1] + (1 - c[1]) / 2)), fw, fh], fill=c[4])
        elif op == 'v':
            cols = c[1:]
            for i, col in enumerate(cols):
                dd.rectangle([fw * i // len(cols), 0, fw * (i + 1) // len(cols), fh], fill=col)
        elif op == 'vr':
            dd.rectangle([0, 0, int(fw * c[1]), fh], fill=c[2])
            dd.rectangle([int(fw * c[1]), 0, fw, fh], fill=c[3])
        elif op == 'stripes':
            n_, a_, b_ = c[1], c[2], c[3]
            for i in range(n_):
                dd.rectangle([0, fh * i // n_, fw, fh * (i + 1) // n_], fill=a_ if i % 2 == 0 else b_)
        elif op == 'canton':
            canton_box = [0, 0, int(fw * c[1]), int(fh * c[2])]
            dd.rectangle(canton_box, fill=c[3])
        elif op == 'cross_in_canton':
            x1, y1 = canton_box[2], canton_box[3]
            t = max(2, y1 // 5)
            dd.rectangle([0, y1 // 2 - t // 2, x1, y1 // 2 + t // 2], fill=c[1])
            dd.rectangle([x1 // 2 - t // 2, 0, x1 // 2 + t // 2, y1], fill=c[1])
        elif op == 'nordic':
            t = int(fh * (c[2] if len(c) > 2 else 0.18))
            xc = int(fw * 0.37)
            dd.rectangle([xc - t // 2, 0, xc + t // 2, fh], fill=c[1])
            dd.rectangle([0, fh // 2 - t // 2, fw, fh // 2 + t // 2], fill=c[1])
        elif op == 'cross':
            t = fh // 6
            dd.rectangle([fw // 2 - t // 2, 0, fw // 2 + t // 2, fh], fill=c[1])
            dd.rectangle([0, fh // 2 - t // 2, fw, fh // 2 + t // 2], fill=c[1])
        elif op == 'swisscross':
            t, l2 = fh // 6, fh // 3
            dd.rectangle([fw // 2 - t // 2, fh // 2 - l2, fw // 2 + t // 2, fh // 2 + l2], fill=c[1])
            dd.rectangle([fw // 2 - l2, fh // 2 - t // 2, fw // 2 + l2, fh // 2 + t // 2], fill=c[1])
        elif op == 'disc':
            x, y, r = c[1] * fw, c[2] * fh, c[3] * fh
            dd.ellipse([x - r, y - r, x + r, y + r], fill=c[4])
        elif op == 'star':
            x, y, r = c[1] * fw, c[2] * fh, c[3] * fh * 1.4
            pts = []
            for i in range(10):
                a = -math.pi / 2 + i * math.pi / 5
                rr = r if i % 2 == 0 else r * 0.42
                pts.append((x + rr * math.cos(a), y + rr * math.sin(a)))
            dd.polygon(pts, fill=c[4])
        elif op == 'tri':
            dd.polygon([(0, 0), (int(fw * 0.42), fh // 2), (0, fh)], fill=c[1])
        elif op == 'band':
            y = c[1] * fh
            dd.rectangle([fw * 0.15, y - c[2] * fh / 2, fw * 0.85, y + c[2] * fh / 2], fill=c[3])
        elif op == 'diamond':
            dd.polygon([(fw * 0.08, fh / 2), (fw / 2, fh * 0.1), (fw * 0.92, fh / 2), (fw / 2, fh * 0.9)], fill=c[1])
        elif op == 'diag':
            dd.polygon([(0, 0), (fw, 0), (0, fh)], fill=c[1])
            dd.polygon([(fw, 0), (fw, fh), (0, fh)], fill=c[2])
        elif op == 'quarters':
            dd.rectangle([0, 0, fw // 2, fh // 2], fill=c[1])
            dd.rectangle([fw // 2, 0, fw, fh // 2], fill=c[2])
            dd.rectangle([0, fh // 2, fw // 2, fh], fill=c[3])
            dd.rectangle([fw // 2, fh // 2, fw, fh], fill=c[4])
        elif op == 'unionjack':
            dd.rectangle([0, 0, fw, fh], fill='#012169')
            dd.line([(0, 0), (fw, fh)], fill='#FFFFFF', width=12)
            dd.line([(fw, 0), (0, fh)], fill='#FFFFFF', width=12)
            dd.line([(0, 0), (fw, fh)], fill='#C8102E', width=4)
            dd.line([(fw, 0), (0, fh)], fill='#C8102E', width=4)
            dd.rectangle([fw // 2 - 10, 0, fw // 2 + 10, fh], fill='#FFFFFF')
            dd.rectangle([0, fh // 2 - 10, fw, fh // 2 + 10], fill='#FFFFFF')
            dd.rectangle([fw // 2 - 6, 0, fw // 2 + 6, fh], fill='#C8102E')
            dd.rectangle([0, fh // 2 - 6, fw, fh // 2 + 6], fill='#C8102E')
        else:
            raise SystemExit(f"unknown flag command {op} for {tag}")
    im.save(path)


with open(os.path.join(OUT, 'common', 'countries', 'countries.txt'), 'w', newline='\n', encoding='utf-8') as f:
    f.write('# Countries of 1930. The three letter tag is used everywhere to refer to the country.\n')
    for tag in sorted(owners_present, key=lambda t: -owners_present[t]):
        name, col, _, gov, flag, _, _ = D.COUNTRIES[tag]
        f.write(f'{tag} = {{\n\tname = "{name}"\n\tcolor = {{ {col[0]} {col[1]} {col[2]} }}\n\tcapital = {loc_keys[capital_loc[tag]]}\n\tgovernment = {gov}\n}}\n')
        make_flag(tag, flag, os.path.join(OUT, 'gfx', 'flags', f'{tag}.png'))

subjects = [('ENG', t) for t in ('CAN', 'NFL', 'AST', 'NZL', 'SAF', 'IRE', 'RAJ')] + [('SOV', 'MON'), ('SOV', 'TAN')]
alliances = [('FRA', 'POL'), ('CZE', 'YUG'), ('CZE', 'ROM'), ('ROM', 'YUG'), ('FRA', 'CZE')]
with open(os.path.join(OUT, 'history', 'diplomacy.txt'), 'w', newline='\n') as f:
    f.write('# Relations at the start of 1930 (used with the diplomacy feature).\n')
    for o, s in subjects:
        if o in owners_present and s in owners_present:
            f.write(f'subject = {{ overlord = {o} subject = {s} type = client_state }}\n')
    for a, b in alliances:
        if a in owners_present and b in owners_present:
            f.write(f'alliance = {{ first = {a} second = {b} }}\n')

TERRAIN_TXT = """# Terrain types. 'color' is the color used in map/terrain.png.
# texture: tiled image drawn on the map. movement_cost multiplies travel time.
# combat_width / attack_modifier / defense_modifier are used by the combat models.
plains = { color = { 86 124 27 } display_color = { 128 150 78 } texture = "gfx/terrain/plains.png" movement_cost = 1.0 combat_width = 90 }
forest = { color = { 0 86 6 } display_color = { 60 100 50 } texture = "gfx/terrain/forest.png" movement_cost = 1.3 combat_width = 70 attack_modifier = -0.1 defense_modifier = 0.15 }
hills = { color = { 112 74 31 } display_color = { 130 112 80 } texture = "gfx/terrain/hills.png" movement_cost = 1.4 combat_width = 70 attack_modifier = -0.15 defense_modifier = 0.25 }
mountains = { color = { 65 42 17 } display_color = { 110 100 92 } texture = "gfx/terrain/mountains.png" movement_cost = 2.0 combat_width = 50 attack_modifier = -0.3 defense_modifier = 0.5 }
desert = { color = { 206 169 99 } display_color = { 200 178 120 } texture = "gfx/terrain/desert.png" movement_cost = 1.2 combat_width = 90 }
marsh = { color = { 75 147 174 } display_color = { 90 112 82 } texture = "gfx/terrain/marsh.png" movement_cost = 1.8 combat_width = 60 attack_modifier = -0.2 defense_modifier = 0.2 }
ocean = { color = { 8 31 130 } display_color = { 30 60 120 } texture = "gfx/terrain/ocean.png" is_water = yes }
jungle = { color = { 20 110 60 } display_color = { 40 90 45 } texture = "gfx/terrain/jungle.png" movement_cost = 1.6 combat_width = 60 attack_modifier = -0.2 defense_modifier = 0.25 }
tundra = { color = { 150 160 140 } display_color = { 150 152 138 } texture = "gfx/terrain/tundra.png" movement_cost = 1.3 combat_width = 80 }
glacier = { color = { 235 240 245 } display_color = { 230 235 240 } texture = "gfx/terrain/glacier.png" movement_cost = 3.0 impassable = yes }
"""
with open(os.path.join(OUT, 'common', 'terrain.txt'), 'w', newline='\n') as f:
    f.write(TERRAIN_TXT)
lang = {'plains': 'Plains', 'forest': 'Forest', 'hills': 'Hills', 'mountains': 'Mountains', 'desert': 'Desert', 'marsh': 'Marsh',
        'ocean': 'Ocean', 'jungle': 'Jungle', 'tundra': 'Tundra', 'glacier': 'Glacier'}
with open(os.path.join(OUT, 'localisation', 'earth.txt'), 'w', newline='\n') as f:
    f.write('# Shown names (location and state names are in their own files).\n')
    for k_, v in lang.items():
        f.write(f'{k_} = "{v}"\n')

stats = dict(locations=NLOC, land=sum(1 for v in kind.values() if v == 'land'), sea=sum(1 for v in kind.values() if v == 'sea'),
             lakes=sum(1 for v in kind.values() if v == 'lake'), states=len(states), countries=len(owners_present), straits=len(strait_pairs))
with open(os.path.join(OUT, 'map', 'earth_stats.json'), 'w') as f:
    json.dump(stats, f, indent=1)
# which admin-1 units (and how many people of each) make up every state - input of make_pops.py
pop_units = []
for si, st in enumerate(states):
    per = collections.Counter()
    for l_ in st['locs']:
        per[loc_unit[l_]] += loc_unit_pop[l_]
    pop_units.append(dict(key=state_keys[si], owner=st['owner'], pop=st['pop'],
                          units=[dict(iso=units[u - 1]['iso'], adm0=units[u - 1]['adm0'], name=units[u - 1]['name'], region=units[u - 1]['region'], pop=v)
                                 for u, v in per.most_common() if u > 0]))
with open(os.path.join(OUT, 'pop_units.json'), 'w') as f:
    json.dump(pop_units, f)
log("done", stats)
