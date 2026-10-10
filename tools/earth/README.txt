Earth 1930 map generator
========================

make_earth.py builds the "Earth 1930" base map (assets_src/earth1930/game; build_assets copies it to
assets/bases/earth1930 so it shows up in the new-project wizard and in `burger new --base earth1930`).

1. Natural Earth 1:10m GeoJSON (public domain): clone https://github.com/nvkelso/natural-earth-vector and use its
   geojson/ folder (admin_0_countries, admin_1_states_provinces, lakes, rivers_lake_centerlines,
   geography_regions_polys, geography_marine_polys, populated_places, glaciated_areas).
2. Elevation: python3 get_elevation.py elev_z5.npy   (608 AWS Terrain Tiles, about 50 MB)
3. python3 make_earth.py <geojson dir> elev_z5.npy ../../assets_src/earth1930/game   (needs numpy, scipy, Pillow)

The 1930 world is described in earth1930_data.py: countries (name, color, capital, flag, population),
which 1930 country owned each modern country / province, and line splits where a 1930 border crossed a
modern province. Change it and run the generator again to make another start date. The generator's
settings (map size, number of land locations / sea zones / states) are at the top of make_earth.py.
