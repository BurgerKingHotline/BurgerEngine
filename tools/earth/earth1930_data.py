"""1930 world data for the Earth map generator (make_earth.py).

Ownership is decided per Natural Earth admin-1 unit (modern provinces/regions): by its country (adm0_a3),
then by unit overrides (iso_3166_2), then by line splits for units that a 1930 border crossed.
Colonies follow the Hearts of Iron IV convention: they are land of their empire; dominions and the
British Raj are their own countries (subjects of the United Kingdom in history/diplomacy).
"""

# ------------------------------------------------------------------------------------------- countries
# tag: (name, (r,g,b), capital (lat, lon), government, flag, population 1930 (millions, approx.), development)
# development: rough industrial level 0..1.2 (used for state tax)
# flag: list of drawing commands, see make_flag() in make_earth.py
COUNTRIES = {
    # Europe
    'ENG': ("United Kingdom", (200, 32, 48), (51.507, -0.128), 'constitutional_monarchy', [('unionjack',)], 46.0, 1.0),
    'IRE': ("Irish Free State", (60, 160, 80), (53.35, -6.26), 'dominion', [('v', '#169B62', '#FFFFFF', '#FF883E')], 3.0, 0.5),
    'FRA': ("France", (57, 96, 170), (48.857, 2.352), 'republic', [('v', '#002395', '#FFFFFF', '#ED2939')], 41.6, 0.9),
    'GER': ("Germany", (96, 96, 96), (52.52, 13.405), 'republic', [('h', '#000000', '#DD0000', '#FFCE00')], 65.0, 1.0),
    'ITA': ("Italy", (60, 140, 70), (41.9, 12.5), 'kingdom', [('v', '#009246', '#FFFFFF', '#CE2B37'), ('disc', 0.5, 0.5, 0.1, '#3A6FB8')], 41.0, 0.6),
    'SPA': ("Spain", (220, 190, 40), (40.417, -3.704), 'kingdom', [('h3', '#AA151B', '#F1BF00', '#AA151B')], 23.6, 0.45),
    'POR': ("Portugal", (40, 110, 60), (38.72, -9.14), 'republic', [('vr', 0.4, '#046A38', '#DA291C'), ('disc', 0.4, 0.5, 0.14, '#FFE900')], 6.8, 0.35),
    'HOL': ("Netherlands", (230, 120, 30), (52.37, 4.9), 'kingdom', [('h', '#AE1C28', '#FFFFFF', '#21468B')], 7.9, 0.8),
    'BEL': ("Belgium", (210, 180, 60), (50.85, 4.35), 'kingdom', [('v', '#000000', '#FAE042', '#ED2939')], 8.1, 0.85),
    'LUX': ("Luxembourg", (120, 170, 220), (49.61, 6.13), 'grand_duchy', [('h', '#ED2939', '#FFFFFF', '#00A1DE')], 0.3, 0.8),
    'SWI': ("Switzerland", (190, 40, 40), (46.95, 7.45), 'republic', [('solid', '#D52B1E'), ('swisscross', '#FFFFFF')], 4.1, 0.9),
    'AUS': ("Austria", (230, 230, 230), (48.21, 16.37), 'republic', [('h', '#ED2939', '#FFFFFF', '#ED2939')], 6.7, 0.6),
    'HUN': ("Hungary", (170, 110, 60), (47.5, 19.04), 'kingdom', [('h', '#CE2939', '#FFFFFF', '#477050')], 8.7, 0.45),
    'CZE': ("Czechoslovakia", (60, 140, 160), (50.08, 14.42), 'republic', [('h2', '#FFFFFF', '#D7141A'), ('tri', '#11457E')], 14.7, 0.7),
    'POL': ("Poland", (200, 70, 90), (52.23, 21.01), 'republic', [('h2', '#FFFFFF', '#DC143C')], 32.0, 0.4),
    'DNZ': ("Free City of Danzig", (170, 90, 90), (54.35, 18.65), 'free_city', [('solid', '#D21034'), ('disc', 0.28, 0.5, 0.12, '#FFD700')], 0.4, 0.7),
    'LIT': ("Lithuania", (210, 200, 60), (54.9, 23.9), 'republic', [('h', '#FDB913', '#006A44', '#C1272D')], 2.4, 0.3),
    'LAT': ("Latvia", (140, 50, 60), (56.95, 24.1), 'republic', [('h3', '#9E3039', '#FFFFFF', '#9E3039')], 1.9, 0.4),
    'EST': ("Estonia", (70, 120, 190), (59.44, 24.75), 'republic', [('h', '#0072CE', '#000000', '#FFFFFF')], 1.1, 0.4),
    'FIN': ("Finland", (230, 240, 250), (60.17, 24.94), 'republic', [('solid', '#FFFFFF'), ('nordic', '#002F6C')], 3.5, 0.45),
    'SWE': ("Sweden", (40, 100, 190), (59.33, 18.07), 'kingdom', [('solid', '#006AA7'), ('nordic', '#FECC00')], 6.1, 0.8),
    'NOR': ("Norway", (150, 70, 70), (59.91, 10.75), 'kingdom', [('solid', '#BA0C2F'), ('nordic', '#FFFFFF', 0.16), ('nordic', '#00205B', 0.08)], 2.8, 0.6),
    'DEN': ("Denmark", (190, 60, 60), (55.68, 12.57), 'kingdom', [('solid', '#C8102E'), ('nordic', '#FFFFFF')], 3.5, 0.7),
    'ICE': ("Iceland", (110, 160, 210), (64.15, -21.94), 'kingdom', [('solid', '#02529C'), ('nordic', '#FFFFFF', 0.16), ('nordic', '#DC1E35', 0.08)], 0.1, 0.4),
    'ROM': ("Romania", (220, 190, 80), (44.43, 26.1), 'kingdom', [('v', '#002B7F', '#FCD116', '#CE1126')], 18.0, 0.3),
    'BUL': ("Bulgaria", (80, 150, 80), (42.7, 23.32), 'kingdom', [('h', '#FFFFFF', '#00966E', '#D62612')], 5.8, 0.25),
    'YUG': ("Yugoslavia", (90, 120, 170), (44.82, 20.46), 'kingdom', [('h', '#003893', '#FFFFFF', '#DE0000')], 13.9, 0.3),
    'ALB': ("Albania", (150, 40, 40), (41.33, 19.82), 'kingdom', [('solid', '#E41E20'), ('disc', 0.5, 0.5, 0.15, '#000000')], 1.0, 0.15),
    'GRE': ("Greece", (100, 160, 220), (37.98, 23.73), 'republic', [('stripes', 9, '#0D5EAF', '#FFFFFF'), ('canton', 0.38, 0.555, '#0D5EAF'), ('cross_in_canton', '#FFFFFF')], 6.4, 0.3),
    'TUR': ("Turkey", (190, 80, 70), (39.93, 32.86), 'republic', [('solid', '#E30A17'), ('disc', 0.4, 0.5, 0.2, '#FFFFFF'), ('disc', 0.45, 0.5, 0.16, '#E30A17')], 14.6, 0.25),
    'SOV': ("Soviet Union", (150, 30, 30), (55.756, 37.617), 'socialist_republic', [('solid', '#CC0000'), ('star', 0.15, 0.22, 0.07, '#FFD700')], 160.0, 0.5),
    # dominions and subjects of the UK
    'CAN': ("Canada", (205, 105, 75), (45.42, -75.7), 'dominion', [('solid', '#C8102E'), ('canton', 0.5, 0.5, '#012169')], 10.2, 0.7),
    'NFL': ("Newfoundland", (175, 140, 115), (47.56, -52.71), 'dominion', [('solid', '#C8102E'), ('canton', 0.5, 0.5, '#012169')], 0.3, 0.4),
    'AST': ("Australia", (70, 110, 200), (-35.28, 149.13), 'dominion', [('solid', '#012169'), ('star', 0.75, 0.5, 0.09, '#FFFFFF')], 6.5, 0.6),
    'NZL': ("New Zealand", (60, 90, 170), (-41.29, 174.78), 'dominion', [('solid', '#012169'), ('star', 0.75, 0.4, 0.07, '#C8102E')], 1.5, 0.6),
    'SAF': ("South Africa", (220, 130, 40), (-25.75, 28.19), 'dominion', [('h', '#FF7900', '#FFFFFF', '#002395')], 8.5, 0.35),
    'RAJ': ("British Raj", (210, 150, 80), (28.61, 77.21), 'colonial_government', [('solid', '#C8102E'), ('canton', 0.5, 0.5, '#012169'), ('star', 0.75, 0.5, 0.1, '#FFFFFF')], 350.0, 0.12),
    # Asia
    'JAP': ("Japan", (240, 240, 240), (35.68, 139.69), 'empire', [('solid', '#FFFFFF'), ('disc', 0.5, 0.5, 0.3, '#BC002D')], 64.0, 0.6),
    'CHI': ("China", (220, 200, 90), (32.06, 118.8), 'republic', [('solid', '#FE0000'), ('canton', 0.5, 0.5, '#000095'), ('disc', 0.25, 0.25, 0.12, '#FFFFFF')], 470.0, 0.12),
    'SIK': ("Xinjiang", (140, 120, 80), (43.82, 87.62), 'warlord_state', [('h', '#FFCE00', '#4AA2DD', '#FFCE00')], 4.0, 0.05),
    'TIB': ("Tibet", (200, 170, 140), (29.65, 91.11), 'theocracy', [('solid', '#E3282C'), ('disc', 0.5, 0.6, 0.18, '#FFD600')], 2.0, 0.05),
    'MON': ("Mongolia", (160, 110, 60), (47.92, 106.92), 'socialist_republic', [('v', '#C4272F', '#015197', '#C4272F')], 0.7, 0.05),
    'TAN': ("Tannu Tuva", (130, 160, 120), (51.72, 94.45), 'socialist_republic', [('solid', '#E1A83C'), ('tri', '#3C6EB4')], 0.07, 0.05),
    'SIA': ("Siam", (100, 120, 200), (13.75, 100.5), 'kingdom', [('h5', '#A51931', '#FFFFFF', '#2D2A4A', '#FFFFFF', '#A51931')], 11.5, 0.1),
    'NEP': ("Nepal", (180, 80, 90), (27.71, 85.32), 'kingdom', [('solid', '#DC143C'), ('tri', '#003893')], 5.5, 0.05),
    'BHU': ("Bhutan", (220, 150, 60), (27.47, 89.64), 'kingdom', [('diag', '#FFD520', '#FF4E12')], 0.3, 0.05),
    'AFG': ("Afghanistan", (120, 140, 90), (34.53, 69.17), 'kingdom', [('v', '#000000', '#D32011', '#007A36')], 11.0, 0.05),
    'PER': ("Persia", (90, 150, 110), (35.69, 51.39), 'kingdom', [('h', '#239F40', '#FFFFFF', '#DA0000')], 12.0, 0.12),
    'SAU': ("Hejaz and Nejd", (90, 160, 80), (24.71, 46.68), 'kingdom', [('solid', '#006C35'), ('band', 0.5, 0.12, '#FFFFFF')], 2.5, 0.05),
    'YEM': ("Yemen", (170, 120, 90), (15.37, 44.19), 'kingdom', [('solid', '#C1121F'), ('star', 0.5, 0.5, 0.1, '#FFFFFF')], 3.0, 0.05),
    'OMA': ("Muscat and Oman", (180, 70, 70), (23.59, 58.41), 'sultanate', [('solid', '#D61F26')], 0.5, 0.05),
    # Africa
    'ETH': ("Ethiopia", (90, 150, 70), (9.03, 38.74), 'empire', [('h', '#078930', '#FCDD09', '#DA121A')], 11.0, 0.05),
    'LIB': ("Liberia", (200, 120, 120), (6.31, -10.8), 'republic', [('stripes', 11, '#BF0A30', '#FFFFFF'), ('canton', 0.45, 0.45, '#002868'), ('star', 0.22, 0.22, 0.1, '#FFFFFF')], 1.5, 0.05),
    # Americas
    'USA': ("United States", (40, 80, 160), (38.9, -77.04), 'republic', [('stripes', 13, '#B22234', '#FFFFFF'), ('canton', 0.4, 0.54, '#3C3B6E')], 123.0, 1.1),
    'MEX': ("Mexico", (110, 160, 90), (19.43, -99.13), 'republic', [('v', '#006847', '#FFFFFF', '#CE1126')], 16.5, 0.2),
    'GUA': ("Guatemala", (90, 160, 210), (14.63, -90.51), 'republic', [('v', '#4997D0', '#FFFFFF', '#4997D0')], 1.8, 0.08),
    'HON': ("Honduras", (60, 110, 190), (14.07, -87.19), 'republic', [('h', '#0073CF', '#FFFFFF', '#0073CF')], 0.9, 0.08),
    'ELS': ("El Salvador", (40, 90, 170), (13.69, -89.22), 'republic', [('h', '#0F47AF', '#FFFFFF', '#0F47AF')], 1.4, 0.08),
    'NIC': ("Nicaragua", (80, 130, 200), (12.11, -86.24), 'republic', [('h', '#0067C6', '#FFFFFF', '#0067C6')], 0.7, 0.08),
    'COS': ("Costa Rica", (150, 60, 90), (9.93, -84.08), 'republic', [('h5', '#002B7F', '#FFFFFF', '#CE1126', '#FFFFFF', '#002B7F')], 0.5, 0.1),
    'PAN': ("Panama", (200, 80, 80), (8.98, -79.52), 'republic', [('quarters', '#FFFFFF', '#D21034', '#005293', '#FFFFFF')], 0.5, 0.1),
    'CUB': ("Cuba", (70, 100, 180), (23.11, -82.37), 'republic', [('stripes', 5, '#002A8F', '#FFFFFF'), ('tri', '#CF142B')], 3.8, 0.15),
    'HAI': ("Haiti", (60, 70, 160), (18.54, -72.34), 'republic', [('h2', '#00209F', '#D21034')], 2.4, 0.05),
    'DOM': ("Dominican Republic", (100, 80, 160), (18.47, -69.9), 'republic', [('quarters', '#002D62', '#CE1126', '#CE1126', '#002D62'), ('cross', '#FFFFFF')], 1.4, 0.08),
    'COL': ("Colombia", (230, 200, 60), (4.71, -74.07), 'republic', [('hr', 0.5, '#FCD116', '#003893', '#CE1126')], 7.9, 0.12),
    'VEN': ("Venezuela", (210, 170, 60), (10.48, -66.9), 'republic', [('h', '#FCE300', '#003DA5', '#EF3340')], 3.0, 0.15),
    'ECU': ("Ecuador", (230, 210, 80), (-0.18, -78.47), 'republic', [('hr', 0.5, '#FFDD00', '#034EA2', '#ED1C24')], 2.2, 0.08),
    'PRU': ("Peru", (190, 70, 70), (-12.05, -77.04), 'republic', [('v', '#D91023', '#FFFFFF', '#D91023')], 5.6, 0.1),
    'BOL': ("Bolivia", (140, 150, 70), (-16.5, -68.15), 'republic', [('h', '#D52B1E', '#F9E300', '#007934')], 2.4, 0.06),
    'BRA': ("Brazil", (60, 160, 60), (-22.91, -43.17), 'republic', [('solid', '#009C3B'), ('diamond', '#FFDF00'), ('disc', 0.5, 0.5, 0.17, '#002776')], 37.0, 0.15),
    'PAR': ("Paraguay", (150, 110, 160), (-25.26, -57.58), 'republic', [('h', '#D52B1E', '#FFFFFF', '#0038A8')], 0.9, 0.06),
    'URU': ("Uruguay", (120, 170, 220), (-34.9, -56.16), 'republic', [('stripes', 9, '#FFFFFF', '#0038A8'), ('canton', 0.5, 0.555, '#FFFFFF'), ('disc', 0.2, 0.28, 0.12, '#FCD116')], 1.7, 0.25),
    'ARG': ("Argentina", (130, 180, 230), (-34.6, -58.38), 'republic', [('h', '#74ACDF', '#FFFFFF', '#74ACDF'), ('disc', 0.5, 0.5, 0.09, '#F6B40E')], 11.9, 0.35),
    'CHL': ("Chile", (200, 90, 110), (-33.45, -70.67), 'republic', [('h2', '#FFFFFF', '#D52B1E'), ('canton', 0.33, 0.5, '#0039A6'), ('star', 0.165, 0.25, 0.08, '#FFFFFF')], 4.4, 0.25),
}

# ------------------------------------------------------------------------------------------- ownership
# owner of each modern country (adm0_a3); units not listed keep... nothing: every adm0 must be listed
OWNER_BY_ADM0 = {
    # Europe
    'GBR': 'ENG', 'IMN': 'ENG', 'GGY': 'ENG', 'JEY': 'ENG', 'GIB': 'ENG', 'MLT': 'ENG', 'CYP': 'ENG', 'CYN': 'ENG', 'ESB': 'ENG', 'WSB': 'ENG',
    'IRL': 'IRE', 'FRA': 'FRA', 'MCO': 'FRA', 'AND': 'FRA', 'ESP': 'SPA', 'PRT': 'POR', 'ITA': 'ITA', 'SMR': 'ITA', 'VAT': 'ITA',
    'CHE': 'SWI', 'LIE': 'SWI', 'AUT': 'AUS', 'DEU': 'GER', 'LUX': 'LUX', 'BEL': 'BEL', 'NLD': 'HOL', 'DNK': 'DEN', 'FRO': 'DEN',
    'NOR': 'NOR', 'SWE': 'SWE', 'FIN': 'FIN', 'ALD': 'FIN', 'ISL': 'ICE', 'EST': 'EST', 'LVA': 'LAT', 'LTU': 'LIT', 'POL': 'POL',
    'CZE': 'CZE', 'SVK': 'CZE', 'HUN': 'HUN', 'ROU': 'ROM', 'MDA': 'ROM', 'BGR': 'BUL', 'SVN': 'YUG', 'HRV': 'YUG', 'BIH': 'YUG',
    'SRB': 'YUG', 'MNE': 'YUG', 'MKD': 'YUG', 'KOS': 'YUG', 'ALB': 'ALB', 'GRC': 'GRE', 'TUR': 'TUR',
    'RUS': 'SOV', 'UKR': 'SOV', 'BLR': 'SOV', 'GEO': 'SOV', 'ARM': 'SOV', 'AZE': 'SOV', 'KAZ': 'SOV', 'UZB': 'SOV', 'TKM': 'SOV',
    'KGZ': 'SOV', 'TJK': 'SOV', 'KAB': 'SOV',
    # Middle East
    'IRQ': 'ENG', 'JOR': 'ENG', 'ISR': 'ENG', 'PSX': 'ENG', 'KWT': 'ENG', 'BHR': 'ENG', 'QAT': 'ENG', 'ARE': 'ENG',
    'SYR': 'FRA', 'LBN': 'FRA', 'SAU': 'SAU', 'YEM': 'YEM', 'OMN': 'OMA', 'IRN': 'PER', 'AFG': 'AFG',
    # Africa
    'EGY': 'ENG', 'SDN': 'ENG', 'SDS': 'ENG', 'LBY': 'ITA', 'ERI': 'ITA', 'SOM': 'ITA', 'SOL': 'ENG', 'DJI': 'FRA', 'ETH': 'ETH',
    'KEN': 'ENG', 'UGA': 'ENG', 'TZA': 'ENG', 'RWA': 'BEL', 'BDI': 'BEL', 'COD': 'BEL', 'COG': 'FRA', 'GAB': 'FRA', 'CAF': 'FRA',
    'TCD': 'FRA', 'CMR': 'FRA', 'NGA': 'ENG', 'GHA': 'ENG', 'SLE': 'ENG', 'GMB': 'ENG', 'TGO': 'FRA', 'BEN': 'FRA', 'NER': 'FRA',
    'BFA': 'FRA', 'MLI': 'FRA', 'MRT': 'FRA', 'SEN': 'FRA', 'GIN': 'FRA', 'CIV': 'FRA', 'LBR': 'LIB', 'GNB': 'POR', 'CPV': 'POR',
    'STP': 'POR', 'AGO': 'POR', 'MOZ': 'POR', 'GNQ': 'SPA', 'SAH': 'SPA', 'MAR': 'FRA', 'DZA': 'FRA', 'TUN': 'FRA',
    'ZMB': 'ENG', 'ZWE': 'ENG', 'MWI': 'ENG', 'BWA': 'ENG', 'LSO': 'ENG', 'SWZ': 'ENG', 'ZAF': 'SAF', 'NAM': 'SAF',
    'MDG': 'FRA', 'COM': 'FRA', 'MUS': 'ENG', 'SYC': 'ENG', 'SHN': 'ENG', 'ATF': 'FRA', 'IOT': 'ENG',
    # South & East Asia
    'IND': 'RAJ', 'PAK': 'RAJ', 'BGD': 'RAJ', 'MMR': 'RAJ', 'KAS': 'RAJ', 'LKA': 'ENG', 'MDV': 'ENG', 'NPL': 'NEP', 'BTN': 'BHU',
    'CHN': 'CHI', 'TWN': 'JAP', 'HKG': 'ENG', 'MAC': 'POR', 'PRK': 'JAP', 'KOR': 'JAP', 'JPN': 'JAP', 'MNG': 'MON',
    'VNM': 'FRA', 'LAO': 'FRA', 'KHM': 'FRA', 'THA': 'SIA', 'MYS': 'ENG', 'SGP': 'ENG', 'BRN': 'ENG', 'IDN': 'HOL', 'TLS': 'POR',
    'PHL': 'USA', 'PGA': 'CHI',
    # Oceania
    'AUS': 'AST', 'NZL': 'NZL', 'PNG': 'AST', 'NRU': 'AST', 'NFK': 'AST', 'IOA': 'AST', 'ATC': 'AST', 'CSI': 'AST', 'HMD': 'AST',
    'FJI': 'ENG', 'SLB': 'ENG', 'KIR': 'ENG', 'TUV': 'ENG', 'TON': 'ENG', 'VUT': 'ENG', 'PCN': 'ENG', 'NCL': 'FRA', 'PYF': 'FRA',
    'WLF': 'FRA', 'WSM': 'NZL', 'COK': 'NZL', 'NIU': 'NZL', 'PLW': 'JAP', 'FSM': 'JAP', 'MHL': 'JAP', 'MNP': 'JAP', 'GUM': 'USA',
    'ASM': 'USA', 'UMI': 'USA',
    # Americas
    'USA': 'USA', 'CAN': 'CAN', 'GRL': 'DEN', 'SPM': 'FRA', 'BMU': 'ENG', 'MEX': 'MEX', 'GTM': 'GUA', 'BLZ': 'ENG', 'HND': 'HON',
    'SLV': 'ELS', 'NIC': 'NIC', 'CRI': 'COS', 'PAN': 'PAN', 'CUB': 'CUB', 'USG': 'USA', 'JAM': 'ENG', 'BHS': 'ENG', 'TCA': 'ENG',
    'CYM': 'ENG', 'VGB': 'ENG', 'AIA': 'ENG', 'ATG': 'ENG', 'KNA': 'ENG', 'MSR': 'ENG', 'DMA': 'ENG', 'LCA': 'ENG', 'VCT': 'ENG',
    'GRD': 'ENG', 'BRB': 'ENG', 'TTO': 'ENG', 'HTI': 'HAI', 'DOM': 'DOM', 'PRI': 'USA', 'VIR': 'USA', 'ABW': 'HOL', 'CUW': 'HOL',
    'SXM': 'HOL', 'MAF': 'FRA', 'BLM': 'FRA', 'COL': 'COL', 'VEN': 'VEN', 'GUY': 'ENG', 'SUR': 'HOL', 'ECU': 'ECU', 'PER': 'PRU',
    'BOL': 'BOL', 'BRA': 'BRA', 'PRY': 'PAR', 'URY': 'URU', 'ARG': 'ARG', 'CHL': 'CHL', 'FLK': 'ENG', 'SGS': 'ENG', 'CLP': 'FRA',
    'ATA': None,
}

# admin-1 units (iso_3166_2) with another 1930 owner than their modern country
OWNER_BY_UNIT = {
    # Germany's eastern provinces (now Poland and Russia)
    'PL-ZP': 'GER', 'PL-DS': 'GER', 'PL-OP': 'GER', 'PL-LB': 'GER', 'PL-WN': 'GER', 'RU-KGD': 'GER',
    # Poland's eastern lands (now Lithuania, Belarus, Ukraine)
    'LT-VL': 'POL', 'BY-BR': 'POL', 'BY-HR': 'POL', 'UA-46': 'POL', 'UA-61': 'POL', 'UA-26': 'POL', 'UA-07': 'POL', 'UA-56': 'POL',
    # Romania: Bukovina, Southern Dobruja; Soviet Transnistria
    'UA-77': 'ROM', 'BG-08': 'ROM', 'BG-19': 'ROM', 'MD-SN': 'SOV', 'MD-CAM': 'SOV', 'MD-GRI': 'SOV',
    # Czechoslovakia: Carpathian Ruthenia
    'UA-21': 'CZE',
    # Italy: Istria
    'HR-18': 'ITA',
    # Crimea/Sevastopol are listed under Russia in Natural Earth: Soviet either way
    # France: Hatay (Sanjak of Alexandretta)
    'TR-31': 'FRA',
    # Spanish Morocco and Western Sahara regions
    'MA-01': 'SPA', 'MA-15': 'SPA', 'MA-16': 'SPA',
    # British Cameroons
    'CM-NW': 'ENG', 'CM-SW': 'ENG',
    # Aden protectorate
    'YE-AD': 'ENG', 'YE-LA': 'ENG', 'YE-AB': 'ENG', 'YE-SH': 'ENG', 'YE-HD': 'ENG', 'YE-MR': 'ENG', 'YE-DA': 'ENG',
    # Portuguese and French India
    'IN-GA': 'POR', 'IN-DH': 'POR', 'IN-PY': 'FRA',
    # China's de facto independent western regions; Tuva
    'CN-XJ': 'SIK', 'CN-XZ': 'TIB', 'RU-TY': 'TAN',
    # Newfoundland (with Labrador) was its own dominion
    'CA-NL': 'NFL',
}

# Units a 1930 border crossed: (iso_3166_2, owner, test(lon, lat)) - first match wins, the rest of the unit keeps its owner
def _west_of(points):
    """True west of a polyline given as (lon, lat) points ordered by latitude."""
    pts = sorted(points, key=lambda p: p[1])
    def test(lon, lat):
        if lat < pts[0][1] or lat > pts[-1][1]:
            return False
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            if y0 <= lat <= y1:
                t = 0 if y1 == y0 else (lat - y0) / (y1 - y0)
                return lon < x0 + t * (x1 - x0)
        return False
    return test

SPLITS = [
    # Finland: Karelian Isthmus, Ladoga Karelia, Petsamo
    ('RU-LEN', 'FIN', lambda lon, lat: lat > 60.2 and lon < 31.0),
    ('RU-KR', 'FIN', _west_of([(31.95, 60.9), (32.3, 62.0), (31.6, 63.0), (30.0, 64.0), (29.6, 66.6)])),
    ('RU-MUR', 'FIN', lambda lon, lat: lon < 31.4 and lat > 68.9),
    # Japan: Karafuto (south Sakhalin) and the Kurils
    ('RU-SAK', 'JAP', lambda lon, lat: lat < 50.0 or lon > 145.2),
    # Germany/Danzig/Poland in Pomerania and Upper Silesia
    ('PL-PM', 'DNZ', lambda lon, lat: 18.45 < lon < 19.25 and 54.1 < lat < 54.46),
    ('PL-PM', 'GER', lambda lon, lat: lon < 17.75 or (lon > 18.95 and lat < 54.25)),
    ('PL-SL', 'GER', lambda lon, lat: lon < 18.93 and lat > 50.22),
    # Italy: Julian March and Fiume
    ('SVN', 'ITA', lambda lon, lat: lon < 14.3),
    ('HR-08', 'ITA', lambda lon, lat: lon < 14.5),
    # Italy: Dodecanese
    ('GR-L', 'ITA', lambda lon, lat: lon > 26.4),
    # Spanish Morocco: Rif coast
    ('MA-03', 'SPA', lambda lon, lat: lat > 34.9),
    ('MA-04', 'SPA', lambda lon, lat: lat > 34.95 and lon < -2.5),
]

# Modern countries that were the homeland of their 1930 owner (states get cores). Everything else an
# empire owns is a colony (no cores, low development).
HOMELAND = {
    'ENG': {'GBR', 'IMN', 'GGY', 'JEY'}, 'FRA': {'FRA', 'MCO', 'AND'}, 'SPA': {'ESP'}, 'POR': {'PRT'}, 'ITA': {'ITA', 'SMR', 'VAT'},
    'HOL': {'NLD'}, 'BEL': {'BEL'}, 'DEN': {'DNK', 'FRO'}, 'USA': {'USA'}, 'JAP': {'JPN'}, 'SAF': {'ZAF'}, 'AST': {'AUS', 'NFK'},
    'NZL': {'NZL'}, 'RAJ': {'IND', 'PAK', 'BGD', 'MMR', 'KAS'}, 'CHI': {'CHN', 'PGA'}, 'SOV': {'RUS', 'UKR', 'BLR', 'GEO', 'ARM', 'AZE',
    'KAZ', 'UZB', 'TKM', 'KGZ', 'TJK', 'KAB', 'MDA'}, 'ROM': {'ROU', 'MDA', 'BGR', 'UKR'}, 'CZE': {'CZE', 'SVK', 'UKR'},
    'YUG': {'SVN', 'HRV', 'BIH', 'SRB', 'MNE', 'MKD', 'KOS'}, 'GER': {'DEU', 'POL', 'RUS'}, 'POL': {'POL', 'LTU', 'BLR', 'UKR'},
    'FIN': {'FIN', 'ALD', 'RUS'},
}

# Population factor 1930 / today by Natural Earth subregion (approximate)
POP_FACTOR = {
    'Northern Europe': 0.62, 'Western Europe': 0.58, 'Southern Europe': 0.55, 'Eastern Europe': 0.62,
    'Northern America': 0.36, 'Central America': 0.13, 'Caribbean': 0.25, 'South America': 0.17,
    'Northern Africa': 0.16, 'Western Africa': 0.08, 'Middle Africa': 0.09, 'Eastern Africa': 0.08, 'Southern Africa': 0.13,
    'Western Asia': 0.12, 'Central Asia': 0.25, 'Southern Asia': 0.18, 'Eastern Asia': 0.34, 'South-Eastern Asia': 0.16,
    'Australia and New Zealand': 0.26, 'Melanesia': 0.15, 'Micronesia': 0.2, 'Polynesia': 0.2,
}

# Modern names → 1930 names for cities used as location names
CITY_1930 = {
    'Saint Petersburg': 'Leningrad', 'Gdansk': 'Danzig', 'Kaliningrad': 'Konigsberg', 'Wroclaw': 'Breslau', 'Szczecin': 'Stettin',
    'Volgograd': 'Stalingrad', 'Yekaterinburg': 'Sverdlovsk', 'Mumbai': 'Bombay', 'Chennai': 'Madras', 'Kolkata': 'Calcutta',
    'Beijing': 'Peiping', 'Ho Chi Minh City': 'Saigon', 'Harare': 'Salisbury', 'Kinshasa': 'Leopoldville', 'Jakarta': 'Batavia',
    'Lviv': 'Lwow', 'Vilnius': 'Wilno', 'Kyiv': 'Kiev', 'Chisinau': 'Kishinev', 'Tbilisi': 'Tiflis', 'Olsztyn': 'Allenstein',
    'Opole': 'Oppeln', 'Zielona Gora': 'Grunberg', 'Koszalin': 'Koslin', 'Legnica': 'Liegnitz', 'Gliwice': 'Gleiwitz',
    'Bytom': 'Beuthen', 'Elblag': 'Elbing', 'Slupsk': 'Stolp', 'Klaipeda': 'Memel', 'Chernivtsi': 'Cernauti', 'Bratislava': 'Bratislava',
    'Maputo': 'Lourenco Marques', 'Kisangani': 'Stanleyville', 'Lubumbashi': 'Elisabethville', 'Bujumbura': 'Usumbura',
    'Kampala': 'Kampala', 'Yangon': 'Rangoon', 'Dhaka': 'Dacca', 'Chattogram': 'Chittagong', 'Guangzhou': 'Canton',
    'Taipei': 'Taihoku', 'Seoul': 'Keijo', 'Pyongyang': 'Heijo', 'Busan': 'Fusan', 'Shenyang': 'Mukden', 'Dalian': 'Dairen',
    'Changchun': 'Changchun', 'Almaty': 'Alma-Ata', 'Bishkek': 'Frunze', 'Dushanbe': 'Stalinabad', 'Ashgabat': 'Poltoratsk',
    'Samara': 'Samara', 'Nizhny Novgorod': 'Nizhny Novgorod', 'Donetsk': 'Stalino', 'Dnipro': 'Dnepropetrovsk', 'Luhansk': 'Voroshilovgrad',
    'Kinshasa ': 'Leopoldville', 'Windhoek': 'Windhoek', 'Nairobi': 'Nairobi', 'Ndjamena': 'Fort-Lamy', "N'Djamena": 'Fort-Lamy',
    'Bangui': 'Bangui', 'Kisumu': 'Kisumu', 'Lusaka': 'Lusaka', 'Bulawayo': 'Bulawayo', 'Mutare': 'Umtali', 'Gweru': 'Gwelo',
    'Malabo': 'Santa Isabel', 'Jayapura': 'Hollandia', 'Makassar': 'Makassar', 'Bengaluru': 'Bangalore', 'Pune': 'Poona',
    'Varanasi': 'Benares', 'Kanpur': 'Cawnpore', 'Vadodara': 'Baroda', 'Thiruvananthapuram': 'Trivandrum', 'Kochi': 'Cochin',
    'Puducherry': 'Pondichery', 'Ankara': 'Ankara', 'Istanbul': 'Istanbul', 'Izmir': 'Smyrna', 'Thessaloniki': 'Salonika',
    'Tallinn': 'Tallinn', 'Kaunas': 'Kaunas', 'Minsk': 'Minsk', 'Kazan': 'Kazan', 'Ulaanbaatar': 'Ulaanbaatar', 'Kyzyl': 'Kyzyl',
}
