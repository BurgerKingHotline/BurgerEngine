"""Peoples of the world in 1930 for the Earth base: culture groups, cultures, the culture of every modern country and
admin-1 unit (with 1930 corrections), religious mixes and minorities, and 1930 population totals (modern borders).
Used by make_pops.py. Shares are rough historical estimates, good enough for a game."""

# culture group -> (name, color)
GROUPS = {
    'germanic': ('Germanic', (110, 110, 130)), 'anglo': ('Anglo', (190, 60, 70)), 'nordic': ('Nordic', (90, 140, 190)),
    'gallic': ('Gallic', (70, 90, 200)), 'italic': ('Italic', (60, 160, 90)), 'iberian': ('Iberian', (220, 180, 50)),
    'romanian': ('Romanian', (200, 200, 90)), 'west_slavic': ('West Slavic', (200, 90, 110)), 'east_slavic': ('East Slavic', (170, 60, 50)),
    'south_slavic': ('South Slavic', (110, 140, 170)), 'baltic': ('Baltic', (150, 110, 170)), 'finno_ugric': ('Finno-Ugric', (110, 170, 150)),
    'hellenic': ('Hellenic', (100, 170, 220)), 'albanian': ('Albanian', (170, 40, 40)), 'jewish': ('Jewish', (90, 170, 210)),
    'turkic': ('Turkic', (170, 100, 60)), 'iranian': ('Iranian', (60, 120, 90)), 'caucasian': ('Caucasian', (150, 120, 100)),
    'arabic': ('Arabic', (60, 150, 80)), 'indo_aryan': ('Indo-Aryan', (220, 120, 40)), 'dravidian': ('Dravidian', (180, 80, 40)),
    'east_asian': ('East Asian', (200, 70, 70)), 'tibetan': ('Tibeto-Mongol', (190, 160, 90)), 'southeast_asian': ('Southeast Asian', (100, 170, 110)),
    'malay': ('Malayo-Polynesian', (60, 150, 140)), 'american': ('American', (90, 110, 170)), 'latin_american': ('Latin American', (120, 170, 80)),
    'african_diaspora': ('African diaspora', (120, 90, 70)), 'west_african': ('West African', (150, 110, 50)), 'bantu': ('Bantu', (130, 140, 60)),
    'horn': ('Horn of Africa', (170, 130, 80)), 'nilotic': ('Nilo-Saharan', (110, 90, 60)), 'oceanian': ('Oceanian', (80, 140, 160)),
}

# culture -> (group, name, religion)
C = {
    # Europe
    'german': ('germanic', 'German', 'protestant'), 'austrian': ('germanic', 'Austrian', 'catholic'), 'dutch': ('germanic', 'Dutch', 'protestant'),
    'flemish': ('germanic', 'Flemish', 'catholic'), 'swiss_german': ('germanic', 'Swiss German', 'protestant'), 'luxembourgish': ('germanic', 'Luxembourgish', 'catholic'),
    'afrikaner': ('germanic', 'Afrikaner', 'protestant'),
    'english': ('anglo', 'English', 'protestant'), 'scottish': ('anglo', 'Scottish', 'protestant'), 'welsh': ('anglo', 'Welsh', 'protestant'),
    'irish': ('anglo', 'Irish', 'catholic'), 'australian': ('anglo', 'Australian', 'protestant'), 'new_zealander': ('anglo', 'New Zealander', 'protestant'),
    'anglo_canadian': ('anglo', 'Anglo-Canadian', 'protestant'), 'american': ('american', 'American', 'protestant'),
    'danish': ('nordic', 'Danish', 'protestant'), 'norwegian': ('nordic', 'Norwegian', 'protestant'), 'swedish': ('nordic', 'Swedish', 'protestant'),
    'icelandic': ('nordic', 'Icelandic', 'protestant'),
    'french': ('gallic', 'French', 'catholic'), 'walloon': ('gallic', 'Walloon', 'catholic'), 'swiss_french': ('gallic', 'Swiss French', 'protestant'),
    'french_canadian': ('gallic', 'French Canadian', 'catholic'),
    'italian': ('italic', 'Italian', 'catholic'), 'maltese': ('italic', 'Maltese', 'catholic'),
    'spanish': ('iberian', 'Spanish', 'catholic'), 'catalan': ('iberian', 'Catalan', 'catholic'), 'basque': ('iberian', 'Basque', 'catholic'),
    'galician': ('iberian', 'Galician', 'catholic'), 'portuguese': ('iberian', 'Portuguese', 'catholic'),
    'romanian': ('romanian', 'Romanian', 'orthodox'),
    'polish': ('west_slavic', 'Polish', 'catholic'), 'czech': ('west_slavic', 'Czech', 'catholic'), 'slovak': ('west_slavic', 'Slovak', 'catholic'),
    'russian': ('east_slavic', 'Russian', 'orthodox'), 'ukrainian': ('east_slavic', 'Ukrainian', 'orthodox'), 'belarusian': ('east_slavic', 'Belarusian', 'orthodox'),
    'serbian': ('south_slavic', 'Serbian', 'orthodox'), 'croat': ('south_slavic', 'Croat', 'catholic'), 'slovene': ('south_slavic', 'Slovene', 'catholic'),
    'bosniak': ('south_slavic', 'Bosnian', 'sunni'), 'macedonian': ('south_slavic', 'Macedonian', 'orthodox'), 'bulgarian': ('south_slavic', 'Bulgarian', 'orthodox'),
    'lithuanian': ('baltic', 'Lithuanian', 'catholic'), 'latvian': ('baltic', 'Latvian', 'protestant'),
    'finnish': ('finno_ugric', 'Finnish', 'protestant'), 'estonian': ('finno_ugric', 'Estonian', 'protestant'), 'hungarian': ('finno_ugric', 'Hungarian', 'catholic'),
    'komi': ('finno_ugric', 'Komi', 'orthodox'), 'mordvin': ('finno_ugric', 'Mordvin', 'orthodox'),
    'greek': ('hellenic', 'Greek', 'orthodox'), 'albanian': ('albanian', 'Albanian', 'sunni'), 'jewish': ('jewish', 'Jewish', 'judaism'),
    # Turkic, Iranian, Caucasus
    'turkish': ('turkic', 'Turkish', 'sunni'), 'azerbaijani': ('turkic', 'Azerbaijani', 'shia'), 'tatar': ('turkic', 'Tatar', 'sunni'),
    'bashkir': ('turkic', 'Bashkir', 'sunni'), 'chuvash': ('turkic', 'Chuvash', 'orthodox'), 'kazakh': ('turkic', 'Kazakh', 'sunni'),
    'uzbek': ('turkic', 'Uzbek', 'sunni'), 'turkmen': ('turkic', 'Turkmen', 'sunni'), 'kyrgyz': ('turkic', 'Kyrgyz', 'sunni'),
    'uyghur': ('turkic', 'Uyghur', 'sunni'), 'yakut': ('turkic', 'Yakut', 'animist'),
    'persian': ('iranian', 'Persian', 'shia'), 'kurdish': ('iranian', 'Kurdish', 'sunni'), 'pashtun': ('iranian', 'Pashtun', 'sunni'),
    'tajik': ('iranian', 'Tajik', 'sunni'), 'baloch': ('iranian', 'Baloch', 'sunni'),
    'georgian': ('caucasian', 'Georgian', 'orthodox'), 'armenian': ('caucasian', 'Armenian', 'oriental'), 'chechen': ('caucasian', 'Chechen', 'sunni'),
    'dagestani': ('caucasian', 'Dagestani', 'sunni'), 'circassian': ('caucasian', 'Circassian', 'sunni'),
    # Arab world
    'maghrebi': ('arabic', 'Maghrebi', 'sunni'), 'egyptian': ('arabic', 'Egyptian', 'sunni'), 'levantine': ('arabic', 'Levantine', 'sunni'),
    'mesopotamian': ('arabic', 'Iraqi', 'shia'), 'gulf_arab': ('arabic', 'Gulf Arab', 'sunni'), 'yemeni': ('arabic', 'Yemeni', 'sunni'),
    'sudanese': ('arabic', 'Sudanese', 'sunni'), 'moorish': ('arabic', 'Moorish', 'sunni'),
    # South Asia
    'hindustani': ('indo_aryan', 'Hindustani', 'hindu'), 'bengali': ('indo_aryan', 'Bengali', 'hindu'), 'punjabi': ('indo_aryan', 'Punjabi', 'sunni'),
    'marathi': ('indo_aryan', 'Marathi', 'hindu'), 'gujarati': ('indo_aryan', 'Gujarati', 'hindu'), 'oriya': ('indo_aryan', 'Oriya', 'hindu'),
    'assamese': ('indo_aryan', 'Assamese', 'hindu'), 'sindhi': ('indo_aryan', 'Sindhi', 'sunni'), 'kashmiri': ('indo_aryan', 'Kashmiri', 'sunni'),
    'nepali': ('indo_aryan', 'Nepali', 'hindu'), 'sinhalese': ('indo_aryan', 'Sinhalese', 'buddhist'), 'dhivehi': ('indo_aryan', 'Dhivehi', 'sunni'),
    'tamil': ('dravidian', 'Tamil', 'hindu'), 'telugu': ('dravidian', 'Telugu', 'hindu'), 'kannada': ('dravidian', 'Kannada', 'hindu'),
    'malayali': ('dravidian', 'Malayali', 'hindu'),
    # East and Southeast Asia
    'han': ('east_asian', 'Han Chinese', 'chinese_folk'), 'japanese': ('east_asian', 'Japanese', 'shinto'), 'korean': ('east_asian', 'Korean', 'buddhist'),
    'mongol': ('tibetan', 'Mongol', 'buddhist'), 'kalmyk': ('tibetan', 'Kalmyk', 'buddhist'), 'tibetan': ('tibetan', 'Tibetan', 'buddhist'), 'buryat': ('tibetan', 'Buryat', 'buddhist'),
    'burmese': ('southeast_asian', 'Burmese', 'buddhist'), 'thai': ('southeast_asian', 'Thai', 'buddhist'), 'lao': ('southeast_asian', 'Lao', 'buddhist'),
    'khmer': ('southeast_asian', 'Khmer', 'buddhist'), 'vietnamese': ('southeast_asian', 'Vietnamese', 'buddhist'),
    'malay': ('malay', 'Malay', 'sunni'), 'javanese': ('malay', 'Javanese', 'sunni'), 'sumatran': ('malay', 'Sumatran', 'sunni'),
    'filipino': ('malay', 'Filipino', 'catholic'), 'moro': ('malay', 'Moro', 'sunni'), 'malagasy': ('malay', 'Malagasy', 'animist'),
    'dayak': ('malay', 'Dayak', 'animist'),
    # Americas
    'mexican': ('latin_american', 'Mexican', 'catholic'), 'central_american': ('latin_american', 'Central American', 'catholic'),
    'caribbean': ('latin_american', 'Caribbean Hispanic', 'catholic'), 'colombian': ('latin_american', 'Colombian', 'catholic'),
    'venezuelan': ('latin_american', 'Venezuelan', 'catholic'), 'andean': ('latin_american', 'Andean', 'catholic'), 'chilean': ('latin_american', 'Chilean', 'catholic'),
    'rioplatense': ('latin_american', 'Rioplatense', 'catholic'), 'paraguayan': ('latin_american', 'Paraguayan', 'catholic'),
    'brazilian': ('latin_american', 'Brazilian', 'catholic'),
    'african_american': ('african_diaspora', 'African American', 'protestant'), 'afro_caribbean': ('african_diaspora', 'Afro-Caribbean', 'protestant'),
    'haitian': ('african_diaspora', 'Haitian', 'catholic'), 'native_american': ('american', 'Native American', 'animist'),
    # Africa
    'wolof': ('west_african', 'Wolof', 'sunni'), 'mande': ('west_african', 'Mande', 'sunni'), 'fula': ('west_african', 'Fula', 'sunni'),
    'hausa': ('west_african', 'Hausa', 'sunni'), 'yoruba': ('west_african', 'Yoruba', 'animist'), 'igbo': ('west_african', 'Igbo', 'animist'),
    'akan': ('west_african', 'Akan', 'animist'), 'mossi': ('west_african', 'Mossi', 'animist'), 'kru': ('west_african', 'Kru', 'animist'),
    'ewe': ('west_african', 'Ewe', 'animist'), 'kanuri': ('west_african', 'Kanuri', 'sunni'),
    'kongo': ('bantu', 'Central Bantu', 'animist'), 'swahili': ('bantu', 'East Bantu', 'animist'), 'shona': ('bantu', 'Southern Bantu', 'animist'),
    'zulu': ('bantu', 'Nguni', 'animist'), 'fang': ('bantu', 'Fang', 'animist'),
    'amhara': ('horn', 'Amhara', 'oriental'), 'oromo': ('horn', 'Oromo', 'animist'), 'somali': ('horn', 'Somali', 'sunni'), 'tigrinya': ('horn', 'Tigrinya', 'oriental'),
    'nilotic': ('nilotic', 'Nilotic', 'animist'), 'sara': ('nilotic', 'Sara', 'animist'),
    # Oceania
    'melanesian': ('oceanian', 'Melanesian', 'animist'), 'polynesian': ('oceanian', 'Polynesian', 'protestant'), 'aboriginal': ('oceanian', 'Aboriginal', 'animist'),
}

# culture of each modern country (adm0_a3) when nothing more specific applies
ADM0 = {
    'DEU': 'german', 'AUT': 'austrian', 'NLD': 'dutch', 'LUX': 'luxembourgish', 'LIE': 'swiss_german', 'CHE': 'swiss_german',
    'GBR': 'english', 'IRL': 'irish', 'IMN': 'english', 'GGY': 'english', 'JEY': 'english', 'AUS': 'australian', 'NZL': 'new_zealander',
    'CAN': 'anglo_canadian', 'USA': 'american', 'DNK': 'danish', 'FRO': 'danish', 'GRL': 'danish', 'NOR': 'norwegian', 'SWE': 'swedish',
    'ISL': 'icelandic', 'FIN': 'finnish', 'ALD': 'swedish', 'FRA': 'french', 'MCO': 'french', 'BEL': 'flemish', 'ITA': 'italian', 'SMR': 'italian',
    'VAT': 'italian', 'MLT': 'maltese', 'ESP': 'spanish', 'AND': 'catalan', 'PRT': 'portuguese', 'GIB': 'english', 'ROU': 'romanian', 'MDA': 'romanian',
    'POL': 'polish', 'CZE': 'czech', 'SVK': 'slovak', 'RUS': 'russian', 'UKR': 'ukrainian', 'BLR': 'belarusian', 'SRB': 'serbian',
    'MNE': 'serbian', 'HRV': 'croat', 'SVN': 'slovene', 'BIH': 'bosniak', 'MKD': 'macedonian', 'BGR': 'bulgarian', 'KOS': 'albanian',
    'LTU': 'lithuanian', 'LVA': 'latvian', 'EST': 'estonian', 'HUN': 'hungarian', 'GRC': 'greek', 'CYP': 'greek', 'CYN': 'turkish', 'ALB': 'albanian',
    'TUR': 'turkish', 'AZE': 'azerbaijani', 'KAZ': 'kazakh', 'UZB': 'uzbek', 'TKM': 'turkmen', 'KGZ': 'kyrgyz', 'TJK': 'tajik', 'GEO': 'georgian',
    'ARM': 'armenian', 'IRN': 'persian', 'AFG': 'pashtun', 'IRQ': 'mesopotamian', 'SYR': 'levantine', 'LBN': 'levantine', 'JOR': 'levantine',
    'ISR': 'levantine', 'PSX': 'levantine', 'PSE': 'levantine', 'SAU': 'gulf_arab', 'YEM': 'yemeni', 'OMN': 'gulf_arab', 'ARE': 'gulf_arab',
    'QAT': 'gulf_arab', 'KWT': 'gulf_arab', 'BHR': 'gulf_arab', 'EGY': 'egyptian', 'LBY': 'maghrebi', 'TUN': 'maghrebi', 'DZA': 'maghrebi',
    'MAR': 'maghrebi', 'SAH': 'moorish', 'MRT': 'moorish', 'SDN': 'sudanese', 'SSD': 'nilotic',
    'IND': 'hindustani', 'PAK': 'punjabi', 'BGD': 'bengali', 'NPL': 'nepali', 'BTN': 'tibetan', 'LKA': 'sinhalese', 'MDV': 'dhivehi',
    'CHN': 'han', 'HKG': 'han', 'MAC': 'han', 'TWN': 'han', 'JPN': 'japanese', 'KOR': 'korean', 'PRK': 'korean', 'MNG': 'mongol',
    'MMR': 'burmese', 'THA': 'thai', 'LAO': 'lao', 'KHM': 'khmer', 'VNM': 'vietnamese', 'MYS': 'malay', 'SGP': 'han', 'BRN': 'malay',
    'IDN': 'javanese', 'PHL': 'filipino', 'TLS': 'melanesian', 'PNG': 'melanesian',
    'MEX': 'mexican', 'GTM': 'central_american', 'BLZ': 'afro_caribbean', 'HND': 'central_american', 'SLV': 'central_american', 'NIC': 'central_american',
    'CRI': 'central_american', 'PAN': 'central_american', 'CUB': 'caribbean', 'DOM': 'caribbean', 'PRI': 'caribbean', 'HTI': 'haitian',
    'JAM': 'afro_caribbean', 'TTO': 'afro_caribbean', 'BHS': 'afro_caribbean', 'BRB': 'afro_caribbean', 'COL': 'colombian', 'VEN': 'venezuelan',
    'ECU': 'andean', 'PER': 'andean', 'BOL': 'andean', 'CHL': 'chilean', 'ARG': 'rioplatense', 'URY': 'rioplatense', 'PRY': 'paraguayan',
    'BRA': 'brazilian', 'GUY': 'afro_caribbean', 'SUR': 'afro_caribbean', 'FLK': 'english',
    'SEN': 'wolof', 'GMB': 'wolof', 'GNB': 'fula', 'GIN': 'fula', 'MLI': 'mande', 'BFA': 'mossi', 'NER': 'hausa', 'NGA': 'hausa',
    'GHA': 'akan', 'CIV': 'akan', 'LBR': 'kru', 'SLE': 'mande', 'TGO': 'ewe', 'BEN': 'ewe', 'CMR': 'fang', 'TCD': 'sudanese',
    'CAF': 'kongo', 'COD': 'kongo', 'COG': 'kongo', 'GAB': 'fang', 'GNQ': 'fang', 'AGO': 'kongo', 'KEN': 'swahili', 'TZA': 'swahili',
    'UGA': 'swahili', 'RWA': 'swahili', 'BDI': 'swahili', 'ETH': 'amhara', 'ERI': 'tigrinya', 'SOM': 'somali', 'SOL': 'somali', 'DJI': 'somali',
    'MOZ': 'shona', 'MWI': 'shona', 'ZMB': 'shona', 'ZWE': 'shona', 'ZAF': 'zulu', 'NAM': 'shona', 'BWA': 'shona', 'LSO': 'zulu', 'SWZ': 'zulu',
    'MDG': 'malagasy', 'COM': 'swahili', 'MUS': 'hindustani', 'SYC': 'afro_caribbean', 'CPV': 'portuguese', 'STP': 'fang',
    'FJI': 'melanesian', 'SLB': 'melanesian', 'VUT': 'melanesian', 'NCL': 'melanesian', 'WSM': 'polynesian', 'TON': 'polynesian', 'PYF': 'polynesian',
    **{k: 'afro_caribbean' for k in ['ABW', 'AIA', 'ATG', 'CUW', 'CYM', 'DMA', 'GRD', 'KNA', 'LCA', 'MAF', 'SXM', 'TCA', 'VCT', 'VGB', 'VIR']},
    'BMU': 'english', 'SHN': 'english', 'SGS': 'english', 'IOT': 'english', 'HMD': 'australian', 'ATF': 'french', 'SPM': 'french', 'WLF': 'polynesian',
    'USG': 'american', 'IOA': 'australian', 'SDS': 'nilotic',
    **{k: 'polynesian' for k in ['ASM', 'NIU', 'KIR']}, **{k: 'melanesian' for k in ['FSM', 'GUM', 'MHL', 'MNP', 'PLW']},
}

# admin-1 overrides: iso code -> culture
UNIT = {
    # Spain, Belgium, Switzerland, Italy
    **{k: 'catalan' for k in ['ES-B', 'ES-GI', 'ES-L', 'ES-T', 'ES-PM']}, **{k: 'basque' for k in ['ES-SS', 'ES-BI', 'ES-VI', 'ES-NA']},
    **{k: 'galician' for k in ['ES-C', 'ES-LU', 'ES-OR', 'ES-PO']},
    **{k: 'swiss_french' for k in ['CH-GE', 'CH-VD', 'CH-NE', 'CH-JU', 'CH-FR', 'CH-VS']}, 'CH-TI': 'italian', 'IT-BZ': 'german',
    # Britain and Canada
    **{k: 'scottish' for k in ['GB-ABD', 'GB-AGB', 'GB-DGY', 'GB-ELS', 'GB-FIF', 'GB-HLD', 'GB-ORK', 'GB-PKN', 'GB-SCB', 'GB-SLK', 'GB-ZET',
                               'GB-ABE', 'GB-ANS', 'GB-CLK', 'GB-DND', 'GB-EAY', 'GB-EDH', 'GB-ELN', 'GB-ERW', 'GB-FAL', 'GB-GLG', 'GB-IVC',
                               'GB-MLN', 'GB-MRY', 'GB-NAY', 'GB-NLK', 'GB-RFW', 'GB-STG', 'GB-WDU', 'GB-WLN', 'GB-EDU', 'GB-SAY']},
    **{k: 'welsh' for k in ['GB-CGN', 'GB-CMN', 'GB-GWN', 'GB-PEM', 'GB-POW', 'GB-AGY', 'GB-BGE', 'GB-BGW', 'GB-CAY', 'GB-CRF', 'GB-CWY',
                            'GB-DEN', 'GB-FLN', 'GB-MON', 'GB-MTY', 'GB-NTL', 'GB-NWP', 'GB-RCT', 'GB-SWA', 'GB-TOF', 'GB-VGL', 'GB-WRX']},
    'CA-QC': 'french_canadian',
    # Russia's peoples
    'RU-TA': 'tatar', 'RU-BA': 'bashkir', 'RU-CU': 'chuvash', 'RU-CE': 'chechen', 'RU-IN': 'chechen', 'RU-DA': 'dagestani', 'RU-SA': 'yakut',
    'RU-BU': 'buryat', 'RU-KL': 'kalmyk', 'RU-KO': 'komi', 'RU-MO': 'mordvin', 'RU-KB': 'circassian', 'RU-KC': 'circassian', 'RU-AD': 'circassian',
    'RU-TY': 'mongol', 'RU-KGD': 'german',
    # Caucasus and Central Asia
    'GE-AB': 'georgian', 'KZ-SEV': 'russian', 'KZ-VOS': 'russian', 'KZ-KAR': 'russian', 'KZ-PAV': 'russian', 'KZ-KUS': 'russian',
    # China
    'CN-XZ': 'tibetan', 'CN-QH': 'tibetan', 'CN-XJ': 'uyghur', 'CN-NM': 'mongol',
    # Middle East
    **{k: 'kurdish' for k in ['IQ-AR', 'IQ-DA', 'IQ-SU', 'IR-16', 'IR-17', 'IR-05', 'TR-21', 'TR-47', 'TR-65', 'TR-30', 'TR-56', 'TR-72',
                              'TR-73', 'TR-49', 'TR-13', 'TR-12', 'TR-62', 'TR-04', 'SY-HA']},
    **{k: 'azerbaijani' for k in ['IR-01', 'IR-02', 'IR-03', 'IR-11']}, 'IR-13': 'baloch', 'IR-10': 'mesopotamian', 'IR-27': 'turkmen',
    **{k: 'tajik' for k in ['AF-BAL', 'AF-BDS', 'AF-TAK', 'AF-BGL', 'AF-SAM', 'AF-PAR', 'AF-KAP', 'AF-HER', 'AF-BAM', 'AF-GHO', 'AF-PAN', 'AF-KDZ']},
    **{k: 'uzbek' for k in ['AF-JOW', 'AF-FYB', 'AF-SAR']},
    # South Asia
    **{k: 'bengali' for k in ['IN-WB', 'IN-TR']}, 'IN-PB': 'punjabi', 'IN-CH': 'punjabi', 'IN-HR': 'hindustani', **{k: 'marathi' for k in ['IN-MH', 'IN-GA']},
    **{k: 'gujarati' for k in ['IN-GJ', 'IN-DH', 'IN-DD', 'IN-DN']}, **{k: 'tamil' for k in ['IN-TN', 'IN-PY']},
    **{k: 'telugu' for k in ['IN-AP', 'IN-TG', 'IN-TS']}, 'IN-KA': 'kannada', **{k: 'malayali' for k in ['IN-KL', 'IN-LD']}, 'IN-OR': 'oriya',
    **{k: 'assamese' for k in ['IN-AS', 'IN-ML', 'IN-AR', 'IN-MN', 'IN-MZ', 'IN-NL']}, 'IN-JK': 'kashmiri', 'IN-LA': 'tibetan', 'IN-SK': 'nepali',
    'PK-SD': 'sindhi', 'PK-KP': 'pashtun', 'PK-TA': 'pashtun', 'PK-BA': 'baloch', 'PK-JK': 'kashmiri', 'PK-GB': 'kashmiri',
    **{k: 'tamil' for k in ['LK-41', 'LK-42', 'LK-43', 'LK-44', 'LK-45', 'LK-51']},
    # Southeast Asia
    **{k: 'malay' for k in ['TH-94', 'TH-95', 'TH-96']}, **{k: 'moro' for k in ['PH-BAS', 'PH-SLU', 'PH-TAW', 'PH-LAN', 'PH-LAS', 'PH-MAG']},
    **{k: 'dayak' for k in ['MY-12', 'MY-13']}, **{k: 'sumatran' for k in ['ID-AC', 'ID-SU', 'ID-SB', 'ID-RI', 'ID-JA', 'ID-SS', 'ID-BE', 'ID-LA', 'ID-KR', 'ID-BB']},
    **{k: 'dayak' for k in ['ID-KB', 'ID-KT', 'ID-KS', 'ID-KI', 'ID-KU']}, **{k: 'melanesian' for k in ['ID-PA', 'ID-PB', 'ID-MA', 'ID-MU', 'ID-NT']},
    # Africa
    **{k: 'yoruba' for k in ['NG-LA', 'NG-OG', 'NG-OY', 'NG-OS', 'NG-ON', 'NG-EK', 'NG-KW']},
    **{k: 'igbo' for k in ['NG-EN', 'NG-AN', 'NG-IM', 'NG-AB', 'NG-EB', 'NG-DE', 'NG-ED', 'NG-RI', 'NG-BY', 'NG-AK', 'NG-CR']},
    **{k: 'kanuri' for k in ['NG-BO', 'NG-YO']}, **{k: 'sara' for k in ['TD-LO', 'TD-LR', 'TD-MA', 'TD-MC', 'TD-MO', 'TD-TA']},
    **{k: 'oromo' for k in ['ET-OR', 'ET-SN', 'ET-GA', 'ET-BE']}, 'ET-SO': 'somali', 'ET-AF': 'somali', 'ET-TI': 'tigrinya',
    **{k: 'nilotic' for k in ['SD-KS', 'SD-NB']},
}

# 1930 corrections: units of modern countries that were German-speaking then
GERMAN_1930 = {'PL-ZP', 'PL-DS', 'PL-OP', 'PL-LB', 'PL-WN', 'RU-KGD', 'CZ-KA', 'CZ-US', 'CZ-LI', 'LT-KL'}

# religion mixes of the main culture per unit / country: key (iso or adm0) -> [(religion, share)] (rest = culture default)
RELIGION = {
    **{k: [('catholic', 1.0)] for k in ['DE-BY', 'DE-BW', 'DE-SL', 'DE-RP']}, 'DE-NW': [('catholic', 0.55)],
    **{k: [('catholic', 0.9)] for k in ['NL-NB', 'NL-LI']}, 'NLD': [('catholic', 0.35)],
    **{k: [('catholic', 0.85)] for k in ['CH-LU', 'CH-UR', 'CH-SZ', 'CH-OW', 'CH-NW', 'CH-ZG', 'CH-FR', 'CH-SO', 'CH-TI', 'CH-VS', 'CH-AI', 'CH-JU', 'CH-SG']},
    'GB-BLA': [('protestant', 1.0)], 'IRL': [('protestant', 0.07)],
    **{k: [('catholic', 0.6)] for k in ['UA-46', 'UA-61', 'UA-26']}, 'HUN': [('protestant', 0.27)], 'SVK': [('protestant', 0.13)],
    'LVA': [('catholic', 0.25)], 'CZE': [('protestant', 0.08)],
    'BIH': [('orthodox', 0.42), ('catholic', 0.22)], 'ALB': [('orthodox', 0.2), ('catholic', 0.1)], 'MKD': [('sunni', 0.3)],
    'BGR': [('sunni', 0.13)], 'KOS': [('orthodox', 0.1)], 'SRB': [('sunni', 0.04)],
    'LBN': [('catholic', 0.35), ('oriental', 0.15), ('shia', 0.2)], 'SYR': [('oriental', 0.1), ('shia', 0.12)], 'EGY': [('oriental', 0.08)],
    'IRQ': [('sunni', 0.4)], 'BHR': [('shia', 0.6)], 'OMN': [('ibadi', 0.6)], 'YEM': [('shia', 0.4)], 'AZE': [('sunni', 0.15)],
    'PAK': [('hindu', 0.12), ('sikh', 0.06)], 'PK-PB': [('hindu', 0.12), ('sikh', 0.1)], 'BGD': [('sunni', 0.7)], 'IN-WB': [('sunni', 0.3)],
    'IN-PB': [('sikh', 0.4), ('sunni', 0.3)], 'IN-JK': [('hindu', 0.2)], 'IND': [('sunni', 0.13)], 'IN-KL': [('oriental', 0.2), ('sunni', 0.18)],
    'IN-UP': [('sunni', 0.15)], 'IN-BR': [('sunni', 0.13)], 'IN-AS': [('sunni', 0.3)], 'IN-HR': [('sunni', 0.3)],
    'LKA': [('hindu', 0.2)], 'NPL': [('buddhist', 0.1)], 'MMR': [('animist', 0.1)],
    'IDN': [('animist', 0.05)], 'MYS': [], 'PHL': [('sunni', 0.04)], 'VNM': [('chinese_folk', 0.3), ('catholic', 0.08)], 'KOR': [('animist', 0.3), ('protestant', 0.03)],
    'PRK': [('animist', 0.3)], 'JPN': [('buddhist', 0.4)], 'CHN': [('buddhist', 0.1), ('sunni', 0.02)],
    'NGA': [('animist', 0.2)], **{k: [('protestant', 0.25), ('sunni', 0.3)] for k in ['NG-LA', 'NG-OG', 'NG-OY', 'NG-OS', 'NG-ON', 'NG-EK']},
    'ETH': [('sunni', 0.3)], 'ERI': [('sunni', 0.45)], 'TCD': [], 'SDN': [], 'TZA': [('sunni', 0.25)], 'KEN': [('protestant', 0.1)],
    'UGA': [('catholic', 0.15), ('protestant', 0.1)], 'MOZ': [('sunni', 0.1)], 'ZAF': [('protestant', 0.25)], 'MDG': [('protestant', 0.15), ('catholic', 0.1)],
    'GHA': [('protestant', 0.1)], 'CMR': [('catholic', 0.1)], 'COD': [('catholic', 0.15)], 'AGO': [('catholic', 0.15)],
    'BFA': [('sunni', 0.2)], 'CIV': [('sunni', 0.15)], 'GIN': [], 'SLE': [('animist', 0.4)], 'TGO': [], 'BEN': [],
    'USA': [('catholic', 0.2)], 'CAN': [('catholic', 0.15)], 'AUS': [('catholic', 0.2)], 'NZL': [('catholic', 0.13)], 'DEU': [('catholic', 0.12)],
    'GUY': [('hindu', 0.4)], 'SUR': [('hindu', 0.3), ('sunni', 0.2)], 'TTO': [('hindu', 0.3)], 'FJI': [('hindu', 0.4)], 'MUS': [('sunni', 0.17), ('catholic', 0.25)],
    'RUS': [('irreligious', 0.1)], 'UKR': [('irreligious', 0.05)], 'ALB_': [],
}

# minorities: key (iso or adm0) -> [(culture, religion or None, share of the unit's people)]
MINORITY = {
    # Kalmykia and Astrakhan: Kalmyks (Oirat Mongols, not Mongolians) with Russians and Kazakhs
    'RU-KL': [('russian', None, 0.38), ('kazakh', None, 0.11)],
    # Jews of Europe and elsewhere
    'POL': [('jewish', None, 0.10)], 'LTU': [('jewish', None, 0.07), ('polish', None, 0.05)], 'LT-VL': [('polish', None, 0.55), ('jewish', None, 0.1)],
    'LVA': [('jewish', None, 0.05), ('russian', None, 0.1), ('german', None, 0.03)], 'EST': [('russian', None, 0.08)],
    'ROU': [('jewish', None, 0.04), ('german', None, 0.04)], 'MDA': [('ukrainian', None, 0.1), ('jewish', None, 0.07), ('russian', None, 0.1)],
    **{k: [('hungarian', None, 0.25)] for k in ['RO-BH', 'RO-SM', 'RO-CJ', 'RO-SJ', 'RO-AR', 'RO-TM', 'RO-BN']},
    **{k: [('hungarian', None, 0.7)] for k in ['RO-HR', 'RO-CV']}, 'RO-MS': [('hungarian', None, 0.45)],
    'HUN': [('jewish', None, 0.05), ('german', None, 0.06)], 'CZE': [('jewish', None, 0.02), ('german', None, 0.12)],
    'SVK': [('hungarian', None, 0.12), ('jewish', None, 0.04)], 'SK-NI': [('hungarian', None, 0.35)], 'SK-KI': [('hungarian', None, 0.2)],
    'UKR': [('jewish', None, 0.06), ('russian', None, 0.09), ('polish', None, 0.02)], 'UA-46': [('polish', None, 0.3), ('jewish', None, 0.1)],
    'UA-61': [('polish', None, 0.35), ('jewish', None, 0.08)], 'UA-26': [('polish', None, 0.2), ('jewish', None, 0.1)],
    'UA-07': [('polish', None, 0.15), ('jewish', None, 0.1)], 'UA-56': [('polish', None, 0.15), ('jewish', None, 0.1)],
    'UA-43': [('russian', None, 0.45), ('tatar', None, 0.25)], 'UA-21': [('hungarian', None, 0.15), ('jewish', None, 0.12)],
    'BLR': [('jewish', None, 0.08), ('polish', None, 0.08), ('russian', None, 0.05)], 'RUS': [('jewish', None, 0.015), ('ukrainian', None, 0.05)],
    'DEU': [('jewish', None, 0.008)], 'AUT': [('jewish', None, 0.03)], 'NLD': [('jewish', None, 0.015)], 'GBR': [('jewish', None, 0.006), ('irish', None, 0.02)],
    'FRA': [('italian', None, 0.02)], 'USA': [('african_american', None, 0.1), ('jewish', None, 0.035)], 'ARG': [('italian', None, 0.12), ('spanish', None, 0.08), ('jewish', None, 0.02)],
    'BRA': [('african_american', 'catholic', 0.15), ('italian', None, 0.03), ('portuguese', None, 0.03), ('japanese', None, 0.005)],
    'ISR': [('jewish', None, 0.4)], 'PSX': [('jewish', None, 0.1)], 'PSE': [('jewish', None, 0.1)],
    'IRQ': [('jewish', None, 0.03)], 'MAR': [('jewish', None, 0.02)], 'TUN': [('jewish', None, 0.02), ('french', None, 0.04), ('italian', None, 0.04)],
    'DZA': [('french', None, 0.13), ('spanish', None, 0.02), ('jewish', None, 0.02)], 'LBY': [('italian', None, 0.05)], 'EGY': [('greek', None, 0.01)],
    'YEM': [('jewish', None, 0.02)], 'IRN': [('armenian', None, 0.01), ('jewish', None, 0.01)],
    # Balkans and Turkey
    'YUG_': [], 'SRB': [('hungarian', None, 0.05)], 'RS-06': [('hungarian', None, 0.3), ('german', None, 0.15)], 'RS-05': [('hungarian', None, 0.35), ('german', None, 0.15)],
    'RS-02': [('hungarian', None, 0.15), ('german', None, 0.2)], 'RS-04': [('german', None, 0.25), ('romanian', None, 0.1)],
    'MKD': [('albanian', None, 0.2), ('turkish', None, 0.08)], 'BGR': [('turkish', None, 0.1)], 'GRC': [('albanian', None, 0.02), ('turkish', None, 0.02)],
    'CYP': [('turkish', None, 0.18)], 'HRV': [('serbian', None, 0.15)], 'HR-18': [('italian', None, 0.4)], 'SVN': [], 'TUR': [('greek', None, 0.01)],
    'GEO': [('armenian', None, 0.11), ('russian', None, 0.04)], 'AZE': [('armenian', None, 0.1), ('russian', None, 0.1)], 'KAZ': [('russian', None, 0.25), ('ukrainian', None, 0.1)],
    'UZB': [('tajik', None, 0.1), ('russian', None, 0.05)], 'KGZ': [('russian', None, 0.12), ('uzbek', None, 0.1)], 'TJK': [('uzbek', None, 0.25)], 'TKM': [('russian', None, 0.08)],
    'FIN': [('swedish', None, 0.1)], 'BEL': [('walloon', None, 0.05)], 'CAN': [('french_canadian', None, 0.12)],
    # Asia and Africa
    'MYS': [('han', None, 0.35), ('tamil', None, 0.1)], 'SGP': [('malay', None, 0.12), ('tamil', None, 0.08)], 'THA': [('han', None, 0.1)],
    'IDN': [('han', None, 0.02)], 'PHL': [('han', None, 0.01)], 'VNM': [('han', None, 0.02), ('khmer', None, 0.02)], 'MMR': [('hindustani', None, 0.03), ('bengali', 'sunni', 0.02)],
    'LKA': [('tamil', None, 0.1)], 'TWN': [('japanese', None, 0.05)], 'KOR': [('japanese', None, 0.03)], 'PRK': [('japanese', None, 0.02)],
    'CN-LN': [('japanese', None, 0.01)], 'CN-NM': [('han', None, 0.7)], 'CN-XJ': [('han', 'chinese_folk', 0.1), ('kazakh', None, 0.08)],
    'CN-QH': [('han', None, 0.45)], 'MNG': [('han', None, 0.03)], 'NPL': [], 'AFG': [('uzbek', None, 0.05)],
    'ZAF': [('afrikaner', None, 0.12), ('english', None, 0.08), ('hindustani', None, 0.02)], 'NAM': [('afrikaner', None, 0.08), ('german', None, 0.03)],
    'ZWE': [('english', None, 0.05)], 'KEN': [('english', None, 0.005), ('hindustani', None, 0.01), ('somali', None, 0.03)],
    'TZA': [('hindustani', None, 0.01)], 'UGA': [('hindustani', None, 0.005), ('nilotic', None, 0.15)], 'SDN': [('nilotic', None, 0.2)],
    'MDG': [('french', None, 0.01)], 'AGO': [('portuguese', None, 0.01)], 'MOZ': [('portuguese', None, 0.01)], 'CMR': [('fula', None, 0.1), ('hausa', None, 0.05)],
    'NGA': [('fula', None, 0.1)], 'GHA': [('mossi', None, 0.1)], 'SEN': [('fula', None, 0.2)], 'MLI': [('fula', None, 0.15), ('moorish', None, 0.05)],
    'NER': [('kanuri', None, 0.05), ('fula', None, 0.1)], 'TCD': [('kanuri', None, 0.05)], 'ETH': [('somali', None, 0.05)],
    'MEX': [('native_american', 'catholic', 0.15)], 'GTM': [('native_american', 'catholic', 0.5)], 'PER': [('native_american', 'catholic', 0.3)],
    'BOL': [('native_american', 'catholic', 0.4)], 'ECU': [('native_american', 'catholic', 0.25)], 'AUS': [('aboriginal', None, 0.01)],
    'NZL': [('polynesian', None, 0.05)], 'PAK': [], 'IND': [],
}

# 1930 population in millions within modern borders (corrects the generator's rough regional estimates)
POP_1930 = {
    'CHN': 489, 'IND': 279, 'USA': 123.1, 'RUS': 101, 'DEU': 59, 'IDN': 60.7, 'PAK': 27.5, 'FRA': 41.6, 'BRA': 34.6, 'JPN': 64.4, 'GBR': 46.0,
    'BGD': 40.5, 'UKR': 31.6, 'ESP': 23.6, 'ITA': 41.0, 'POL': 29.5, 'KOR': 13.6, 'PRK': 7.4, 'MEX': 16.6, 'EGY': 14.6, 'NGA': 20.0,
    'IRN': 12.2, 'CAN': 10.4, 'ROU': 15.1, 'TUR': 14.6, 'VNM': 17.0, 'NLD': 7.9, 'ETH': 11.0, 'MMR': 14.6, 'COL': 7.4, 'PHL': 13.0,
    'UZB': 4.6, 'COD': 10.0, 'ZAF': 8.0, 'ARG': 11.9, 'SDN': 5.5, 'DZA': 6.3, 'AUS': 6.5, 'CZE': 10.6, 'THA': 12.0, 'BEL': 8.1,
    'GRC': 6.4, 'BLR': 9.0, 'SWE': 6.1, 'MAR': 5.5, 'AFG': 7.0, 'NPL': 5.5, 'LKA': 5.3, 'MYS': 4.3, 'KAZ': 6.0, 'PER': 5.6, 'CHL': 4.4,
    'VEN': 3.0, 'CUB': 3.8, 'HUN': 8.7, 'PRT': 6.8, 'AUT': 6.7, 'CHE': 4.1, 'SRB': 5.0, 'KOS': 0.6, 'HRV': 3.7, 'BGR': 5.8, 'DNK': 3.5,
    'FIN': 3.5, 'NOR': 2.8, 'IRL': 3.0, 'SVK': 3.3, 'LTU': 3.0, 'LVA': 1.9, 'EST': 1.1, 'SVN': 1.2, 'BIH': 2.3, 'MKD': 0.95, 'ALB': 1.0,
    'MDA': 2.5, 'GEO': 3.3, 'ARM': 1.1, 'AZE': 2.3, 'KGZ': 1.0, 'TJK': 1.2, 'TKM': 1.2, 'IRQ': 3.0, 'SYR': 2.6, 'SAU': 3.0, 'YEM': 4.0,
    'TZA': 5.0, 'KEN': 3.0, 'UGA': 3.5, 'GHA': 3.2, 'MOZ': 4.0, 'AGO': 3.0, 'MDG': 3.6, 'NZL': 1.5, 'TWN': 4.6, 'MNG': 0.7, 'ISR': 0.6,
    'PSX': 0.4, 'PSE': 0.4, 'JOR': 0.3, 'LBN': 0.8, 'KHM': 2.9, 'LAO': 1.0, 'SGP': 0.55, 'CIV': 2.0, 'CMR': 2.5, 'MLI': 2.6, 'BFA': 2.5,
    'NER': 1.7, 'SEN': 1.4, 'GIN': 2.0, 'ZWE': 1.2, 'ZMB': 1.4, 'MWI': 1.6, 'TUN': 2.4, 'LBY': 0.7, 'ECU': 2.2, 'BOL': 2.2, 'URY': 1.7,
    'PRY': 0.9, 'GTM': 1.8, 'HTI': 2.4, 'DOM': 1.4, 'SOM': 1.0, 'ERI': 0.6, 'SSD': 2.0, 'TCD': 1.5, 'CAF': 1.0, 'COG': 0.6, 'GAB': 0.4,
    'RWA': 1.8, 'BDI': 1.8, 'BEN': 1.1, 'TGO': 0.8, 'SLE': 1.7, 'LBR': 0.8, 'MRT': 0.4, 'NAM': 0.3, 'BWA': 0.25, 'LSO': 0.5, 'OMN': 0.4,
    'ARE': 0.08, 'KWT': 0.07, 'QAT': 0.03, 'BHR': 0.1, 'PNG': 1.3, 'HND': 0.95, 'SLV': 1.4, 'NIC': 0.7, 'CRI': 0.5, 'PAN': 0.5, 'JAM': 1.0,
    'PRI': 1.5, 'BTN': 0.25, 'CYP': 0.35, 'LUX': 0.3, 'ISL': 0.1, 'MLT': 0.24,
}

# country culture / religion / accepted cultures when the majority of its land does not say it well (1930)
COUNTRY = {
    'ENG': ('english', 'protestant', ['scottish', 'welsh']), 'SOV': ('russian', 'irreligious', ['ukrainian', 'belarusian']),
    'RAJ': ('hindustani', 'hindu', ['bengali', 'punjabi', 'marathi', 'gujarati', 'tamil', 'telugu', 'kannada', 'malayali', 'oriya']),
    'CZE': ('czech', 'catholic', ['slovak']), 'YUG': ('serbian', 'orthodox', ['croat', 'slovene']), 'SPA': ('spanish', 'catholic', ['catalan', 'galician']),
    'BEL': ('flemish', 'catholic', ['walloon']), 'SWI': ('swiss_german', 'protestant', ['swiss_french', 'italian']), 'CAN': ('anglo_canadian', 'protestant', ['french_canadian']),
    'USA': ('american', 'protestant', []), 'CHI': ('han', 'chinese_folk', []), 'JAP': ('japanese', 'shinto', []), 'TUR': ('turkish', 'sunni', []),
    'PER': ('persian', 'shia', []), 'GER': ('german', 'protestant', []), 'ITA': ('italian', 'catholic', []), 'FRA': ('french', 'catholic', []),
    'POL': ('polish', 'catholic', []), 'ROM': ('romanian', 'orthodox', []), 'HUN': ('hungarian', 'catholic', []),
    'SAF': ('afrikaner', 'protestant', ['english']), 'ETH': ('amhara', 'oriental', []), 'AUS': ('austrian', 'catholic', []),
    'AST': ('australian', 'protestant', []), 'NZL': ('new_zealander', 'protestant', []), 'SIK': ('uyghur', 'sunni', []), 'TIB': ('tibetan', 'buddhist', []),
    'MON': ('mongol', 'buddhist', []), 'TAN': ('mongol', 'buddhist', []), 'HOL': ('dutch', 'protestant', []), 'POR': ('portuguese', 'catholic', []),
    'DEN': ('danish', 'protestant', []), 'NFL': ('anglo_canadian', 'protestant', []), 'IRE': ('irish', 'catholic', []), 'BRA': ('brazilian', 'catholic', []),
}

# hand corrections of the town-based weights (iso -> factor): farmland that towns do not show, and empty land
DENSITY_FIX = {
    # Java and Bali were crowded farmland; Borneo, New Guinea and inner Sumatra nearly empty
    **{k: 4.0 for k in ['ID-JB', 'ID-JT', 'ID-JI', 'ID-YO', 'ID-BT', 'ID-JK', 'ID-BA']},
    **{k: 0.25 for k in ['ID-KI', 'ID-KT', 'ID-KB', 'ID-KS', 'ID-KU', 'ID-PA', 'ID-PB', 'ID-RI', 'ID-JA', 'ID-KR']},
    # Manchuria and China's west were thinly settled; the Yangtze delta and North China plain crowded
    **{k: 0.35 for k in ['CN-HL', 'CN-JL', 'CN-NM', 'CN-XJ', 'CN-XZ', 'CN-QH', 'CN-GS', 'CN-NX']},
    **{k: 1.5 for k in ['CN-SD', 'CN-HA', 'CN-HE', 'CN-JS', 'CN-AH', 'CN-SC', 'CN-HB', 'CN-HN']},
    # empty north and interior
    'US-AK': 0.03, **{k: 0.1 for k in ['CA-YT', 'CA-NT', 'CA-NU']}, **{k: 0.3 for k in ['AU-WA', 'AU-NT']},
    **{k: 0.3 for k in ['BR-AM', 'BR-PA', 'BR-RR', 'BR-AP', 'BR-AC', 'BR-RO', 'BR-MT', 'BR-TO']},
    **{k: 0.3 for k in ['RU-SA', 'RU-KK', 'RU-MAG', 'RU-CHU', 'RU-KAM', 'RU-KHA', 'RU-AMU', 'RU-YAN', 'RU-KHM', 'RU-TY', 'RU-EVE', 'RU-TAY']},
    # deserts, far islands and thinly settled north (town counts overrate them)
    **{k: 0.05 for k in ['EG-WAD', 'EG-MT', 'EG-BA', 'EG-SIN', 'EG-JS']}, 'JP-01': 0.4, 'RU-KYA': 0.4, 'RU-IRK': 0.6,
    **{k: 0.15 for k in ['GB-ZET', 'GB-ORK', 'GB-ELS']},
    'GB-GLA': 0.7, 'RU-MOW': 0.7, 'RU-MOS': 0.7, 'CN-HI': 0.3, 'PL-SL': 0.35, 'CL-AN': 0.3, 'CL-TA': 0.4,
}

# 1930 census-like populations (millions) of single admin-1 units; the rest of their country shares what is left
UNIT_POP_1930 = {
    # United States (1930 census)
    'US-NY': 12.59, 'US-PA': 9.63, 'US-IL': 7.63, 'US-OH': 6.65, 'US-TX': 5.82, 'US-CA': 5.68, 'US-MI': 4.84, 'US-MA': 4.25, 'US-NJ': 4.04,
    'US-MO': 3.63, 'US-NC': 3.17, 'US-IN': 3.24, 'US-WI': 2.94, 'US-GA': 2.91, 'US-AL': 2.65, 'US-TN': 2.62, 'US-KY': 2.61, 'US-MN': 2.56,
    'US-IA': 2.47, 'US-VA': 2.42, 'US-LA': 2.10, 'US-OK': 2.40, 'US-MS': 2.01, 'US-AR': 1.85, 'US-SC': 1.74, 'US-WV': 1.73, 'US-MD': 1.63,
    'US-KS': 1.88, 'US-CT': 1.61, 'US-FL': 1.47, 'US-WA': 1.56, 'US-NE': 1.38, 'US-OR': 0.95, 'US-CO': 1.04, 'US-ME': 0.80, 'US-SD': 0.69,
    'US-ND': 0.68, 'US-RI': 0.69, 'US-MT': 0.54, 'US-UT': 0.51, 'US-NH': 0.47, 'US-ID': 0.45, 'US-AZ': 0.44, 'US-VT': 0.36, 'US-NM': 0.42,
    'US-DE': 0.24, 'US-WY': 0.23, 'US-NV': 0.09, 'US-DC': 0.49, 'US-AK': 0.06, 'US-HI': 0.37,
    # India (1931 census, modern state borders, rough)
    'IN-UP': 46.5, 'IN-UT': 3.1, 'IN-BR': 23.5, 'IN-JH': 9.5, 'IN-WB': 21.0, 'IN-OR': 10.0, 'IN-AP': 16.5, 'IN-TG': 7.5, 'IN-TN': 21.0,
    'IN-KL': 9.5, 'IN-KA': 13.0, 'IN-MH': 23.0, 'IN-GJ': 11.5, 'IN-RJ': 11.7, 'IN-MP': 14.0, 'IN-CT': 5.5, 'IN-PB': 9.5, 'IN-HR': 4.5,
    'IN-HP': 2.0, 'IN-JK': 3.3, 'IN-AS': 7.0, 'IN-DL': 0.64, 'IN-GA': 0.5,
    # China (1930s estimates)
    'CN-SC': 40.0, 'CN-CQ': 10.0, 'CN-SD': 37.0, 'CN-HA': 34.0, 'CN-JS': 34.0, 'CN-HE': 28.0, 'CN-BJ': 1.5, 'CN-TJ': 1.4, 'CN-SH': 3.1,
    'CN-HN': 28.0, 'CN-HB': 25.0, 'CN-AH': 22.0, 'CN-GD': 30.0, 'CN-ZJ': 21.0, 'CN-JX': 16.0, 'CN-FJ': 11.0, 'CN-GX': 13.0, 'CN-YN': 12.0,
    'CN-GZ': 10.0, 'CN-SN': 10.0, 'CN-SX': 12.0, 'CN-GS': 6.0, 'CN-LN': 15.0, 'CN-JL': 7.0, 'CN-HL': 4.0, 'CN-NM': 2.5, 'CN-XJ': 4.0,
    'CN-XZ': 1.0, 'CN-QH': 1.0, 'CN-NX': 0.7, 'CN-HI': 2.2,
    # Japan (1930 census, prefectures)
    'JP-01': 2.81, 'JP-02': 0.88, 'JP-03': 0.98, 'JP-04': 1.14, 'JP-05': 0.99, 'JP-06': 1.08, 'JP-07': 1.51, 'JP-08': 1.49, 'JP-09': 1.14,
    'JP-10': 1.19, 'JP-11': 1.46, 'JP-12': 1.47, 'JP-13': 5.41, 'JP-14': 1.62, 'JP-15': 1.93, 'JP-16': 0.78, 'JP-17': 0.76, 'JP-18': 0.62,
    'JP-19': 0.63, 'JP-20': 1.72, 'JP-21': 1.18, 'JP-22': 1.80, 'JP-23': 2.57, 'JP-24': 1.16, 'JP-25': 0.69, 'JP-26': 1.55, 'JP-27': 3.54,
    'JP-28': 2.65, 'JP-29': 0.60, 'JP-30': 0.83, 'JP-31': 0.49, 'JP-32': 0.74, 'JP-33': 1.28, 'JP-34': 1.69, 'JP-35': 1.14, 'JP-36': 0.72,
    'JP-37': 0.73, 'JP-38': 1.14, 'JP-39': 0.72, 'JP-40': 2.53, 'JP-41': 0.69, 'JP-42': 1.23, 'JP-43': 1.35, 'JP-44': 0.95, 'JP-45': 0.76,
    'JP-46': 1.56, 'JP-47': 0.58,
    # France: overseas parts (1930s), the mainland shares the rest
    'FR-GF': 0.03, 'FR-MQ': 0.23, 'FR-GP': 0.27, 'FR-RE': 0.2, 'FR-YT': 0.02,
}

# (1930 owner, modern country) -> culture of the people there in 1930 (lands that changed hands after the war)
OWNER_CULTURE = {('GER', 'POL'): 'german', ('GER', 'RUS'): 'german', ('DNZ', 'POL'): 'german', ('JAP', 'RUS'): 'japanese', ('FIN', 'RUS'): 'finnish'}
