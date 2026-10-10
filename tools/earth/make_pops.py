"""Population of the Earth 1930 base (population, religion features).

Reads pop_units.json written by make_earth.py (the people of every state per admin-1 unit) and writes into the game:
  common/cultures/00_cultures.txt    culture groups and cultures
  history/pops/00_pops.txt           pops of every state (culture, religion, size)
  common/countries/countries.txt     culture, religion and accepted cultures of every country (lines added / replaced)

Usage: python3 tools/earth/make_pops.py <pop_units.json> <game folder>
"""
import collections, json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import earth_peoples as P
import earth1930_data as D

SRC, GAME = sys.argv[1], sys.argv[2]
states = json.load(open(SRC))

# --- share of every admin-1 unit in its modern country: the people of the towns inside it (Natural Earth populated
# places) plus a rural floor by area. The generator's own estimate is too flat for this.
import math
NE = os.environ.get('NE_GEOJSON', '/tmp/claude-0/nev/geojson')
CITY_EXP = float(os.environ.get('CITY_EXP', 0.85))       # town weight = people ** CITY_EXP (below 1: big cities count less)
URBAN = float(os.environ.get('URBAN', 0.35))             # share of a country's people spread by town size; the rest by number of towns and area
URBAN_BY_COUNTRY = {'GBR': 0.7, 'DEU': 0.55, 'BEL': 0.6, 'NLD': 0.55, 'USA': 0.55, 'AUS': 0.6, 'FRA': 0.5, 'CAN': 0.5, 'ARG': 0.5,
                    'URY': 0.5, 'CHE': 0.45, 'DNK': 0.45, 'SWE': 0.4, 'ITA': 0.4, 'AUT': 0.45, 'CZE': 0.4, 'ESP': 0.4,
                    'IND': 0.12, 'CHN': 0.12, 'IDN': 0.1, 'PAK': 0.12, 'BGD': 0.06, 'JPN': 0.25}


def rings_of(geom):
    if geom['type'] == 'Polygon':
        return [geom['coordinates']]
    if geom['type'] == 'MultiPolygon':
        return geom['coordinates']
    return []


def ring_area_km2(ring):
    lat0 = sum(p[1] for p in ring) / max(1, len(ring))
    kx = 111.32 * math.cos(math.radians(lat0))
    s_ = 0.0
    for (x1, y1), (x2, y2) in zip(ring, ring[1:] + ring[:1]):
        s_ += (x1 * kx) * (y2 * 110.57) - (x2 * kx) * (y1 * 110.57)
    return abs(s_) / 2


def inside(x, y, ring):
    c_ = False
    n_ = len(ring)
    j = n_ - 1
    for i in range(n_):
        xi, yi = ring[i][0], ring[i][1]
        xj, yj = ring[j][0], ring[j][1]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi + 1e-12) + xi:
            c_ = not c_
        j = i
    return c_


try:
    feats = json.load(open(os.path.join(NE, 'ne_10m_admin_1_states_provinces.geojson')))['features']
    places = [f['properties'] for f in json.load(open(os.path.join(NE, 'ne_10m_populated_places.geojson')))['features']]
except FileNotFoundError:
    feats, places = [], []
    print('WARNING: Natural Earth files not found (set NE_GEOJSON); using the generator estimate only')
unit_area, unit_mass, unit_towns, polys = collections.Counter(), collections.Counter(), collections.Counter(), collections.defaultdict(list)
for f in feats:
    p_ = f['properties']
    key = (p_.get('iso_3166_2') or '', p_['adm0_a3'])
    for poly in rings_of(f['geometry']):
        outer = poly[0]
        unit_area[key] += ring_area_km2(outer) - sum(ring_area_km2(h) for h in poly[1:])
        xs = [q[0] for q in outer]; ys = [q[1] for q in outer]
        polys[p_['adm0_a3']].append((key, (min(xs), min(ys), max(xs), max(ys)), poly))
present = set()
for st in states:
    for u in st['units']:
        present.add((u['iso'], u['adm0']))
centre = {}
for a3_, lst in polys.items():
    for key, (x0, y0, x1, y1), poly in lst:
        if key in present and key not in centre:
            centre[key] = ((x0 + x1) / 2, (y0 + y1) / 2)


def nearest_present(key, x, y):
    # towns in small units the map merged into their neighbours count for the nearest unit that is on the map
    if key in present:
        return key
    best, bd = None, 1e18
    for k2, (cx, cy) in centre.items():
        if k2[1] != key[1]:
            continue
        d_ = (cx - x) ** 2 + (cy - y) ** 2
        if d_ < bd:
            best, bd = k2, d_
    return best or key


for pl in places:
    x, y, a3 = pl['LONGITUDE'], pl['LATITUDE'], pl['ADM0_A3']
    # 1950 size where known (closer to 1930), else a share of today's; big cities count less than in proportion
    p50, pmax = float(pl.get('POP1950') or 0) * 1000, float(pl.get('POP_MAX') or 0)   # POP1950 is in thousands
    people = max(0.0, p50 if p50 > 0 else pmax * 0.3) ** CITY_EXP
    for key, (x0, y0, x1, y1), poly in polys.get(a3, []):
        if x0 <= x <= x1 and y0 <= y <= y1 and inside(x, y, poly[0]) and not any(inside(x, y, h) for h in poly[1:]):
            k_ = nearest_present(key, x, y)
            unit_mass[k_] += people
            unit_towns[k_] += 1
            break
unit_total = collections.Counter()   # generator people per unit (all states)
for st in states:
    for u in st['units']:
        unit_total[(u['iso'], u['adm0'])] += u['pop']
adm0_area, adm0_mass, adm0_towns, adm0_gen = collections.Counter(), collections.Counter(), collections.Counter(), collections.Counter()
for key in unit_total:
    adm0_area[key[1]] += unit_area.get(key, 0)
    adm0_mass[key[1]] += unit_mass.get(key, 0)
    adm0_towns[key[1]] += unit_towns.get(key, 0)
    adm0_gen[key[1]] += unit_total[key]
weight = {}
for key, n in unit_total.items():
    a3 = key[1]
    if adm0_mass[a3] <= 0 or adm0_area[a3] <= 0:
        weight[key] = n          # no towns known: keep the generator estimate
        continue
    # town dwellers follow town sizes; country people follow how many towns there are (settled land), plus a little by area
    u_ = URBAN_BY_COUNTRY.get(a3, URBAN)
    rural = 0.9 * unit_towns.get(key, 0) / adm0_towns[a3] + 0.1 * unit_area.get(key, 0) / adm0_area[a3]
    weight[key] = (u_ * unit_mass.get(key, 0) / adm0_mass[a3] + (1 - u_) * rural) * P.DENSITY_FIX.get(key[0], P.DENSITY_FIX.get(a3 + '*', 1.0))
adm0_w = collections.Counter()
for key, w_ in weight.items():
    adm0_w[key[1]] += w_ if adm0_mass[key[1]] > 0 else 0
for st in states:
    for u in st['units']:
        key = (u['iso'], u['adm0'])
        if unit_total[key] > 0 and adm0_mass[u['adm0']] > 0 and adm0_w[u['adm0']] > 0:
            # this state's part of the unit keeps its share of the unit
            u['pop'] = u['pop'] / unit_total[key] * weight[key] / adm0_w[u['adm0']] * adm0_gen[u['adm0']]

# --- units with known 1930 populations get exactly that; the rest of their country shares what is left
fixed_by_unit = {}
fixed_sum = collections.Counter()
for st in states:
    for u in st['units']:
        if u['iso'] in P.UNIT_POP_1930:
            fixed_by_unit[(u['iso'], u['adm0'])] = P.UNIT_POP_1930[u['iso']] * 1e6
# units the map merged into a neighbour (Tokyo, Paris, ...) add their people to the nearest unit on the map, if that one is fixed too
seen_iso = {k[0] for k in fixed_by_unit}
for a3_, lst in polys.items():
    for key, (x0, y0, x1, y1), poly in lst:
        if key[0] in P.UNIT_POP_1930 and key not in present and key[0] not in seen_iso:
            seen_iso.add(key[0])
            to = nearest_present(key, (x0 + x1) / 2, (y0 + y1) / 2)
            if to in fixed_by_unit:
                fixed_by_unit[to] += P.UNIT_POP_1930[key[0]] * 1e6
for (iso, a3), v in fixed_by_unit.items():
    fixed_sum[a3] += v
unit_now = collections.Counter()
for st in states:
    for u in st['units']:
        unit_now[(u['iso'], u['adm0'])] += u['pop']
free_now = collections.Counter()
for (iso, a3), v in unit_now.items():
    if (iso, a3) not in fixed_by_unit:
        free_now[a3] += v
for st in states:
    for u in st['units']:
        key = (u['iso'], u['adm0'])
        a3 = u['adm0']
        if a3 not in fixed_sum or a3 not in P.POP_1930 or unit_now[key] <= 0:
            continue
        target = P.POP_1930[a3] * 1e6
        f = min(1.0, target / fixed_sum[a3])          # fixed units never exceed the country total
        if key in fixed_by_unit:
            u['pop'] = u['pop'] / unit_now[key] * fixed_by_unit[key] * f
        else:
            rest = max(0.0, target - fixed_sum[a3] * f)
            u['pop'] = u['pop'] / max(1e-9, free_now[a3]) * rest
fixed_countries = set(fixed_sum)

# --- scale people to 1930 totals of each modern country
gen_total = collections.Counter()
for st in states:
    for u in st['units']:
        gen_total[u['adm0']] += u['pop']
scale = {a3: P.POP_1930[a3] * 1e6 / v for a3, v in gen_total.items() if a3 in P.POP_1930 and v > 0 and a3 not in fixed_countries}

missing = set()


def culture_of(u, owner=None):
    iso, a3 = u['iso'], u['adm0']
    if (owner, a3) in P.OWNER_CULTURE:
        return P.OWNER_CULTURE[(owner, a3)]
    if iso in P.UNIT:
        return P.UNIT[iso]
    if iso in P.GERMAN_1930:
        return 'german'
    if a3 in P.ADM0:
        return P.ADM0[a3]
    missing.add(a3)
    return None


def unit_pops(u, owner):
    """(culture, religion, people) of one admin-1 unit inside a state."""
    people = u['pop'] * scale.get(u['adm0'], 1.0)
    main = culture_of(u, owner)
    if main is None or people <= 0:
        return []
    out = []
    minorities = P.MINORITY.get(u['iso'], P.MINORITY.get(u['adm0'], []))
    moved = (owner, u['adm0']) in P.OWNER_CULTURE or u['iso'] in P.GERMAN_1930
    if moved:  # lands that changed hands later: the modern country's minorities and faiths do not apply
        minorities = []
    rest = 1.0
    for cul, rel, share in minorities:
        if cul == main or cul not in P.C:
            continue
        share = min(share, rest)
        rest -= share
        out.append((cul, rel or P.C[cul][2], people * share))
    mix = [] if moved else P.RELIGION.get(u['iso'], P.RELIGION.get(u['adm0'], []))
    left = 1.0
    for rel, share in mix:
        share = min(share, left)
        left -= share
        out.append((main, rel, people * rest * share))
    out.append((main, P.C[main][2], people * rest * left))
    return out


pops = {}
for st in states:
    agg = collections.Counter()
    for u in st['units']:
        for cul, rel, n in unit_pops(u, st['owner']):
            agg[(cul, rel)] += n
    total = sum(agg.values())
    # keep groups of at least 1% (or 2000 people); the rest joins the biggest group
    big = {k: v for k, v in agg.items() if v >= max(2000, 0.01 * total)}
    if not big and agg:
        k = agg.most_common(1)[0][0]
        big = {k: total}
    else:
        lost = total - sum(big.values())
        if big and lost > 0:
            k = max(big, key=big.get)
            big[k] += lost
    pops[st['key']] = big
if missing:
    print('WARNING: no culture for modern countries', sorted(missing))

# --- countries: culture and religion of their home states
home_people = collections.defaultdict(collections.Counter)
home_faith = collections.defaultdict(collections.Counter)
for st in states:
    owner = st['owner']
    home = D.HOMELAND.get(owner)
    main_a3 = max(st['units'], key=lambda u: u['pop'])['adm0'] if st['units'] else None
    is_home = home is None or main_a3 in home
    for (cul, rel), n in pops[st['key']].items():
        home_people[owner][cul] += n * (1 if is_home else 0.05)
        home_faith[owner][rel] += n * (1 if is_home else 0.05)
countries = {}
for tag in D.COUNTRIES:
    if tag in P.COUNTRY:
        countries[tag] = P.COUNTRY[tag]
    elif home_people[tag]:
        countries[tag] = (home_people[tag].most_common(1)[0][0], home_faith[tag].most_common(1)[0][0], [])

# --- write files
used = {c for p in pops.values() for (c, _) in p} | {v[0] for v in countries.values()} | {a for v in countries.values() for a in v[2]}
os.makedirs(os.path.join(GAME, 'common', 'cultures'), exist_ok=True)
with open(os.path.join(GAME, 'common', 'cultures', '00_cultures.txt'), 'w', newline='\n', encoding='utf-8') as f:
    f.write('# Cultures of 1930 (population feature), in culture groups. Cultures of the same group accept each other.\n'
            '# Generated by tools/earth/make_pops.py from tools/earth/earth_peoples.py; edit freely.\n\n')
    for g, (gname, col) in P.GROUPS.items():
        members = [k for k, v in P.C.items() if v[0] == g and k in used]
        if not members:
            continue
        f.write(f'{g} = {{\n\tname = "{gname}"\n\tcolor = {{ {col[0]} {col[1]} {col[2]} }}\n')
        for k in members:
            f.write(f'\t{k} = {{ name = "{P.C[k][1]}" religion = {P.C[k][2]} }}\n')
        f.write('}\n')
os.makedirs(os.path.join(GAME, 'history', 'pops'), exist_ok=True)
with open(os.path.join(GAME, 'history', 'pops', '00_pops.txt'), 'w', newline='\n', encoding='utf-8') as f:
    f.write('# People of every state in 1930 (population feature): culture, religion (religion feature) and size.\n'
            '# Rough estimates from Natural Earth data and 1930 totals; generated by tools/earth/make_pops.py.\n')
    for st in states:
        p = pops[st['key']]
        if not p:
            continue
        f.write(f"{st['key']} = {{\n")
        for (cul, rel), n in sorted(p.items(), key=lambda kv: -kv[1]):
            f.write(f'\tpop = {{ culture = {cul} religion = {rel} size = {int(round(n))} }}\n')
        f.write('}\n')
cf = os.path.join(GAME, 'common', 'countries', 'countries.txt')
text = open(cf, encoding='utf-8').read()
text = re.sub(r'\n\t(culture|religion|accepted_cultures) = [^\n]*', '', text)
out = []
cur = None
for line in text.split('\n'):
    m = re.match(r'^([A-Z0-9]{3}) = \{', line)
    if m:
        cur = m.group(1)
    out.append(line)
    if cur in countries and line.startswith('\tgovernment = '):
        cul, rel, acc = countries[cur]
        out.append(f'\tculture = {cul}')
        out.append(f'\treligion = {rel}')
        if acc:
            out.append('\taccepted_cultures = { ' + ' '.join(acc) + ' }')
open(cf, 'w', newline='\n', encoding='utf-8').write('\n'.join(out))

world = sum(sum(p.values()) for p in pops.values())
print(f'states {len(pops)}, people {world / 1e9:.3f}B, cultures used {len(used)}, countries {len(countries)}')
for tag in ['GER', 'ENG', 'FRA', 'SOV', 'CHI', 'RAJ', 'USA', 'JAP', 'ITA', 'POL', 'CZE', 'YUG', 'TUR']:
    tot = sum(sum(pops[st['key']].values()) for st in states if st['owner'] == tag)
    print(f'  {tag}: {countries.get(tag)} {tot / 1e6:.1f}M')
