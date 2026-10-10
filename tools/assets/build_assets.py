"""Builds assets/features, assets/presets and assets/bases from the engine source and the sample game.

Usage (from the repo root):  python3 tools/assets/build_assets.py .
Needs Python 3 with Pillow. Regenerates the backend C# copies from src/Burger.Core (backend_gen.py).
"""
import os, re, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))

ENGINE = sys.argv[1]                      # repo root
SAMPLE = os.path.join(ENGINE, 'projects', 'SampleGame')
A = os.path.join(ENGINE, 'assets')
FEAT = os.path.join(A, 'features')

for d in ('features', 'presets', 'bases'):
    shutil.rmtree(os.path.join(A, d), ignore_errors=True)
shutil.rmtree(os.path.join(A, 'backend'), ignore_errors=True)

# backend C# of every feature, generated from the engine's own rule classes
subprocess.check_call([sys.executable, os.path.join(HERE, 'backend_gen.py'), os.path.join(ENGINE, 'src', 'Burger.Core'), FEAT])
os.makedirs(os.path.join(FEAT, 'combat_custom', 'backend'), exist_ok=True)
shutil.copy(os.path.join(HERE, 'Combat_Custom.cs'), os.path.join(FEAT, 'combat_custom', 'backend', 'Combat_Custom.cs'))


def manifest(fid, name, category, order, desc, requires=(), group=None, defines=None, generate=()):
    os.makedirs(os.path.join(FEAT, fid), exist_ok=True)
    s = f'# Feature package. Everything next to this file is copied into a project when the feature is added.\n'
    s += f'name = "{name}"\ncategory = "{category}"\norder = {order}\ndescription = "{desc}"\n'
    if requires: s += 'requires = { ' + ' '.join(requires) + ' }\n'
    if group: s += f'group = {group}\n'
    if group == 'supply': s += 'conflicts = { ' + ('supply_hubs' if fid == 'supply_range' else 'supply_range') + ' }\n'
    if defines: s += 'defines = { ' + ' '.join(f'{k} = {v}' for k, v in defines.items()) + ' }\n'
    if generate: s += 'generate = { ' + ' '.join(generate) + ' }\n'
    open(os.path.join(FEAT, fid, 'feature.txt'), 'w', newline='\n').write(s)


def data(fid, rel):
    src = os.path.join(SAMPLE, 'game', rel)
    dst = os.path.join(FEAT, fid, 'game', rel)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy(src, dst)


manifest('military', 'Land military', 'Military', 10,
         'Unit types, division templates, divisions and armies with generals, movement, fronts, battles and occupation. A recruit menu trains new divisions.',
         generate=['starting_armies'])
data('military', 'common/units/units.txt')
for fid, name, model, order, desc in [
        ('combat_hoi4', 'Combat: Hearts of Iron IV style', 'hoi4', 11, 'Hourly rounds, combat width, soft/hard attack against defense and breakthrough, armor and piercing, organisation and strength damage.'),
        ('combat_eu4', 'Combat: Europa Universalis style', 'eu4', 12, 'Daily dice rolls with fire and shock phases, general pips, morale and discipline.'),
        ('combat_imperator', 'Combat: Imperator style', 'imperator', 13, 'Daily d6 rolls modified by the generals, offense against defense, morale and strength damage.'),
        ('combat_custom', 'Combat: empty template', 'custom', 14, 'A short, commented combat model to write your own rules from scratch.')]:
    manifest(fid, name, 'Military', order, desc, requires=['military'], group='combat', defines={'combat_model': model})
manifest('fog_of_war', 'Fog of war', 'Military', 20,
         'Players only see their own land, their allies and what is around their armies; the rest of the map is greyed out and enemy armies are hidden.',
         defines={'fog_of_war': 'yes'})
manifest('diplomacy', 'Diplomacy & war', 'Diplomacy', 30,
         'Wars with casus belli, war score and peace deals; opinion, alliances, military access, guarantees and subjects; data-driven diplomatic actions.')
for rel in ['common/diplomatic_actions/00_actions.txt', 'common/casus_belli.txt', 'common/subject_types.txt']:
    data('diplomacy', rel)
manifest('characters', 'Characters & dynasties', 'Characters', 40,
         'Rulers and heirs, families and marriages, births and deaths, succession, traits and stats. Rulers are created automatically for every country.')
data('characters', 'common/traits.txt')
manifest('character_interactions', 'Character interactions', 'Characters', 41,
         'Actions on characters: appoint an heir, make a general, gifts, arranged marriages, execution.', requires=['characters'])
data('character_interactions', 'common/character_interactions/00_interactions.txt')
manifest('economy', 'Gold & manpower economy', 'Economy', 50,
         'Monthly tax income, army upkeep and manpower from states. Training and reinforcing divisions costs gold and manpower.',
         generate=['starting_treasury'])
manifest('ai', 'AI', 'AI', 90,
         'Computer-controlled countries: manage armies and fronts, recruit, use diplomacy and make peace (each part only with its feature).')

def text(fid, rel, content):
    dst = os.path.join(FEAT, fid, rel)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    open(dst, 'w', newline='\n').write(content)

manifest('production', 'Production (factories & equipment)', 'Military', 15,
         'HOI4 production: civilian and military factories in states, production lines with efficiency, an equipment stockpile and a construction queue. Training and reinforcing divisions use manpower and equipment instead of gold.',
         requires=['military'], defines={'recruit_gold_cost': 'no'}, generate=['starting_factories'])
text('production', 'game/common/equipment/00_equipment.txt', """# Equipment, made by military factories (Production menu) and used by divisions.
#   cost            production cost of one unit
#   category        army / naval / air (naval equipment is built in dockyards)
#   default_for     unit categories that need it when a unit type lists no 'equipment = { ... }'
#   default_amount  how many such a unit needs
# A unit type can also list exactly what it needs:  equipment = { infantry_equipment = 100 support_equipment = 10 }

infantry_equipment = {
	name = "Infantry Equipment"
	cost = 0.5
	category = army
	default_for = { infantry cavalry }
	default_amount = 100
}
support_equipment = {
	name = "Support Equipment"
	cost = 2
	category = army
	default_for = { support }
	default_amount = 25
}
artillery_equipment = {
	name = "Artillery"
	cost = 3.5
	category = army
	default_for = { support artillery }
	default_amount = 12
}
""")
text('production', 'game/common/buildings/00_factories.txt', """# Factories (production feature). Levels are stored per state: buildings = { civilian_factory = 2 }
#   cost          construction cost of one level (civilian factories add construction points per day)
#   max_level     most levels in one state
#   shares_slots  shares the state's building slots with other such buildings (HOI4)
#   potential     optional trigger (state scope) for where it may be built

civilian_factory = {
	name = "Civilian Factory"
	desc = "Builds buildings and makes consumer goods."
	cost = 10800
	max_level = 20
	category = factory
	shares_slots = yes
}
military_factory = {
	name = "Military Factory"
	desc = "Produces equipment for production lines."
	cost = 7200
	max_level = 20
	category = factory
	shares_slots = yes
}
""")
INFRA = """# Infrastructure: carries supply further and speeds up construction in the state.
infrastructure = {
	name = "Infrastructure"
	desc = "Roads: supply reaches further, construction in the state is faster."
	cost = 3000
	max_level = 5
	category = infrastructure
}
"""
manifest('supply_range', 'Supply: range', 'Military', 16,
         'Supply spreads from the capital and from your own land, weaker with every step (rough terrain makes steps longer). It only travels through friendly land, so encircled armies starve. Too many divisions in one place also run short.',
         requires=['military'], group='supply', generate=['starting_infrastructure'])
manifest('supply_hubs', 'Supply: hubs & railways', 'Military', 17,
         'HOI4 supply: the capital and supply hubs feed nearby land, but a hub only works while railways connect it to the capital. Cut the railway and the armies it feeds starve.',
         requires=['military'], group='supply', generate=['starting_infrastructure', 'starting_railways'])
for fid in ('supply_range', 'supply_hubs'):
    text(fid, 'game/common/buildings/00_infrastructure.txt', INFRA)
text('supply_hubs', 'game/common/buildings/00_railways.txt', """# Railways and supply hubs (supply_hubs feature).
railway = {
	name = "Railway"
	desc = "Connects supply hubs to the capital. A hub only works while friendly states with railways link it to the capital."
	cost = 1600
	max_level = 5
	category = infrastructure
}
supply_hub = {
	name = "Supply Hub"
	desc = "Feeds supply to the land around it when connected to the capital by railways."
	cost = 6000
	max_level = 1
	category = infrastructure
}
""")
manifest('division_designer', 'Division designer', 'Military', 18,
         'Players design their own division templates in the game: choose battalions, see the stats, equipment and training time, update or delete designs. Designs are stored in the save.',
         requires=['military'])

manifest('navy', 'Navy (fleets & transports)', 'Military', 19,
         'Ship types, fleets that sail through sea locations and ports, naval battles where screens protect capital ships, repairs in naval bases, and transport ships that carry divisions over the sea (naval invasions fight at a penalty, unescorted armies at sea can be sunk). With the production feature, dockyards build the ships.',
         requires=['military'], generate=['starting_naval_bases', 'starting_fleets'])
text('navy', 'game/common/ships/00_ships.txt', """# Ship types (navy feature).
#   category       screen (shields capital ships in battle), capital, transport, or your own
#   hp             hit points; a side retreats when its ships fall below naval_retreat (defines) of their hit points
#   speed          km per hour (a fleet moves at the speed of its slowest ship)
#   light_attack   damage against screens and transports
#   heavy_attack   damage against capital ships (and less against screens)
#   armor          reduces damage taken
#   transport      divisions this ship can carry over the sea
#   cost           gold, when bought without the production feature (with economy)
#   production_cost  dockyard production cost, with the production feature
#   manpower, build_days   crew and building time when bought with gold
# Any other number is kept as a stat, readable from C# (ship.Type.Stat("my_stat")).

ship_types = {
	destroyer = {
		name = "Destroyer"
		category = screen
		hp = 20
		speed = 32
		light_attack = 4
		heavy_attack = 0.5
		armor = 1
		cost = 40
		production_cost = 1200
		manpower = 200
		build_days = 120
	}
	light_cruiser = {
		name = "Light Cruiser"
		category = screen
		hp = 40
		speed = 30
		light_attack = 7
		heavy_attack = 1
		armor = 3
		cost = 80
		production_cost = 2600
		manpower = 500
		build_days = 200
	}
	battleship = {
		name = "Battleship"
		category = capital
		hp = 120
		speed = 22
		light_attack = 3
		heavy_attack = 14
		armor = 12
		cost = 250
		production_cost = 9000
		manpower = 1500
		build_days = 540
	}
	transport = {
		name = "Transport"
		category = transport
		hp = 15
		speed = 20
		transport = 2
		cost = 30
		production_cost = 600
		manpower = 100
		build_days = 90
	}
}
""")
text('navy', 'game/common/buildings/00_naval.txt', """# Naval buildings (navy feature). Levels are stored per state: buildings = { naval_base = 1 dockyard = 1 }
naval_base = {
	name = "Naval Base"
	desc = "Ships in a port of this state repair faster."
	cost = 3000
	max_level = 5
	category = naval
	potential = { is_coastal = yes }
}
dockyard = {
	name = "Dockyard"
	desc = "Builds ships on dockyard production lines (production feature)."
	cost = 6400
	max_level = 20
	category = factory
	shares_slots = yes
	potential = { is_coastal = yes }
}
""")

manifest('air', 'Air (wings & air regions)', 'Military', 21,
         'HOI4 air war: the map is split into air regions; air wings at air bases fly air superiority (a combat bonus for the side that holds the sky), close air support (hits enemy divisions in battles) or strategic bombing (knocks out factories and railways, needs production) over regions in range. With the production feature military factories build the planes.',
         requires=['military'], generate=['starting_air_regions', 'starting_air_bases', 'starting_air'])
text('air', 'game/common/planes/00_planes.txt', """# Plane types (air feature). A wing has air_wing_size planes (defines).
#   category          fighter, cas, bomber (or your own; only shown in the UI)
#   range             km a wing can fly from its air base to the center of a region
#   air_attack        air superiority: shoots down enemy planes (needed for the superiority mission)
#   air_defense       fewer losses
#   agility           a little extra air superiority
#   ground_attack     close air support damage to enemy divisions in battles (needed for the CAS mission)
#   strategic_attack  strategic bombing damage to buildings (needed for the bombing mission, production feature)
#   cost              gold for a full wing (without the production feature, with economy)
#   production_cost   production cost of ONE plane (with the production feature)
#   manpower, build_days   crew and building time of a wing bought with gold
# Any other number is kept as a stat, readable from C# (wing.Type.Stat("my_stat")).

plane_types = {
	fighter = {
		name = "Fighter"
		category = fighter
		range = 700
		air_attack = 12
		air_defense = 10
		agility = 40
		cost = 60
		production_cost = 22
		manpower = 100
		build_days = 90
	}
	close_air_support = {
		name = "Close Air Support"
		category = cas
		range = 500
		air_attack = 2
		air_defense = 8
		agility = 20
		ground_attack = 20
		cost = 70
		production_cost = 24
		manpower = 120
		build_days = 100
	}
	bomber = {
		name = "Strategic Bomber"
		category = bomber
		range = 1400
		air_defense = 14
		agility = 10
		strategic_attack = 12
		ground_attack = 2
		cost = 110
		production_cost = 45
		manpower = 200
		build_days = 150
	}
}
""")
text('air', 'game/common/buildings/00_air.txt', """# Air bases (air feature). Each level holds air_base_capacity planes (defines).
air_base = {
	name = "Air Base"
	desc = "Air wings are stationed here. Each level holds more planes."
	cost = 4000
	max_level = 10
	category = military
}
""")

manifest('battle_plans', 'Battle plans (fronts, arrows, fallback lines)', 'Military', 22,
         'HOI4 battle plans for armies: draw the part of the border a front covers, an offensive arrow towards an objective and a fallback line to hold, then switch the army between Hold, Attack and Fallback. The AI aims its offensives at enemy capitals.',
         requires=['military'])
manifest('lend_lease', 'Lend-lease & volunteers', 'Military', 23,
         'Help countries at war without joining: send volunteer divisions that fight for them until they are at peace, and (with production) send a share of your equipment stockpile every month. Both improve their opinion of you. The AI helps friends too.',
         requires=['military', 'diplomacy'])
text('lend_lease', 'game/common/diplomatic_actions/10_support.txt', """# Support actions (lend_lease feature). 'dialog' opens a window in the game where the details are chosen:
#   dialog = volunteers   how many volunteer divisions to send (they fight for the recipient until it is at peace)
#   dialog = lend_lease   which equipment, a one-time shipment of an amount, or a share of the stockpile every month
send_volunteers = {
	name = "Send Volunteers"
	desc = "Send divisions to fight in their war without joining it. They come home when the war ends."
	dialog = volunteers
	potential = {
		scope:recipient = { is_at_war = yes }
		NOT = { is_at_war_with = scope:recipient }
	}
	allow = { num_of_divisions >= 1 }
}
send_equipment = {
	name = "Send Equipment"
	desc = "Ship equipment from our stockpile: once, or a share of it every month (lend-lease)."
	dialog = lend_lease
	potential = {
		has_feature = production
		NOT = { is_at_war_with = scope:recipient }
	}
}
""")

# ------------------------------------------------------------------ politics group
manifest('politics', 'Politics (ideologies & elections)', 'Politics', 60,
         'HOI4-style politics: every country has a popularity for each ideology and a ruling party. Democracies hold elections that fill a parliament; the biggest party that can find a majority governs, alone or in a coalition. Popularity drifts with propaganda, lost wars, low stability and the neighbours. Ideologies change opinions between countries. Adds political power and stability, a Politics menu and an Ideology map mode.')
text('politics', 'game/common/ideologies/00_ideologies.txt', """# Ideologies (politics feature). Every country has a popularity for each of them; the ruling party has one ideology.
#   color          map color (Ideology map mode) and party color
#   adjective      used for rebel countries ("Nationalist Spain") and texts
#   party_name     default party name (countries can rename it: history/politics or set_party_name)
#   elections      countries ruled by this ideology hold elections
#   extremist      gains popularity when stability is low or a war goes badly
#   coalition      parties it will govern with (both sides must list each other)
#   opinion        opinion its rulers have of countries ruled by each ideology
#   modifier       modifiers for countries ruled by it (any modifier key: manpower, production, political_power_gain...)
#   government     optional: the country's government word when this ideology takes power
# Scripts: ruling_ideology = fascist, in_government = democratic, popularity_communist >= 0.3, seats_democratic > 50,
#          add_popularity = { ideology = fascist value = 0.05 }, set_ruling_party = communist

democratic = {
	name = "Democratic"
	adjective = "Democratic"
	party_name = "Democratic Party"
	color = { 70 115 205 }
	elections = yes
	coalition = { neutrality }
	opinion = { democratic = 15 communist = -20 fascist = -20 }
	modifier = { stability_target = 5 opinion = 5 }
}
communist = {
	name = "Communist"
	adjective = "Communist"
	party_name = "Communist Party"
	color = { 190 30 35 }
	extremist = yes
	opinion = { communist = 20 democratic = -15 fascist = -40 neutrality = -10 }
	modifier = { political_power_gain = 3 manpower = 0.1 }
	government = socialist_republic
}
fascist = {
	name = "Fascist"
	adjective = "Nationalist"
	party_name = "National Party"
	color = { 120 80 45 }
	extremist = yes
	coalition = { neutrality }
	opinion = { fascist = 20 democratic = -20 communist = -40 neutrality = -5 }
	modifier = { political_power_gain = 3 manpower = 0.15 }
}
neutrality = {
	name = "Non-aligned"
	adjective = "Loyalist"
	party_name = "Independents"
	color = { 150 150 125 }
	coalition = { democratic fascist }
	opinion = { neutrality = 5 communist = -10 }
	modifier = { stability_target = 5 }
}
""")
text('politics', 'game/common/governments/00_governments.txt', """# Starting politics by government type (the 'government' word of each country). A country listed in
# history/politics/*.txt uses that instead; any other government word uses 'default'.
#   ruling_party   ideology of the ruling party
#   popularity     starting support of each ideology (any numbers - they are scaled to 100%)
#   laws           starting law per law group (laws feature)
#   seats          seats in parliament (default: define parliament_seats, 100)
#
# history/politics/countries.txt example:
#   GER = {
#       ruling_party = democratic
#       popularity = { democratic = 40 fascist = 18 communist = 13 neutrality = 29 }
#       parties = { fascist = "NSDAP" democratic = "SPD" }
#       seats = 491
#       last_election = 1928.5.20
#       laws = { conscription = volunteer_only }
#   }

default = { ruling_party = neutrality popularity = { neutrality = 50 democratic = 30 communist = 10 fascist = 10 } laws = { political_parties = restricted_parties } }
republic = { ruling_party = democratic popularity = { democratic = 55 neutrality = 20 communist = 13 fascist = 12 } laws = { political_parties = multi_party press = free_press } }
constitutional_monarchy = { ruling_party = democratic popularity = { democratic = 50 neutrality = 30 communist = 10 fascist = 10 } laws = { political_parties = multi_party press = free_press } }
dominion = { ruling_party = democratic popularity = { democratic = 60 neutrality = 25 communist = 7 fascist = 8 } laws = { political_parties = multi_party press = free_press conscription = volunteer_only } }
free_city = { ruling_party = democratic popularity = { democratic = 45 fascist = 30 neutrality = 15 communist = 10 } laws = { political_parties = multi_party } }
kingdom = { ruling_party = neutrality popularity = { neutrality = 55 democratic = 25 fascist = 12 communist = 8 } laws = { political_parties = restricted_parties } }
monarchy = { ruling_party = neutrality popularity = { neutrality = 55 democratic = 25 fascist = 12 communist = 8 } laws = { political_parties = restricted_parties } }
empire = { ruling_party = neutrality popularity = { neutrality = 60 democratic = 18 fascist = 12 communist = 10 } laws = { political_parties = restricted_parties } }
grand_duchy = { ruling_party = neutrality popularity = { neutrality = 55 democratic = 35 fascist = 5 communist = 5 } laws = { political_parties = multi_party } }
sultanate = { ruling_party = neutrality popularity = { neutrality = 70 democratic = 12 fascist = 10 communist = 8 } laws = { political_parties = restricted_parties press = state_press } }
theocracy = { ruling_party = neutrality popularity = { neutrality = 75 democratic = 10 fascist = 8 communist = 7 } laws = { political_parties = restricted_parties press = state_press } }
warlord_state = { ruling_party = neutrality popularity = { neutrality = 60 communist = 15 fascist = 13 democratic = 12 } laws = { political_parties = restricted_parties press = state_press } }
colonial_government = { ruling_party = neutrality popularity = { neutrality = 70 democratic = 20 communist = 5 fascist = 5 } laws = { political_parties = restricted_parties } }
socialist_republic = { ruling_party = communist popularity = { communist = 80 neutrality = 10 democratic = 5 fascist = 5 } laws = { political_parties = single_party press = propaganda_ministry economy = partial_mobilisation } }
""")
text('politics', 'game/common/resources/00_politics.txt', """# Resources of the politics feature (a game's own file with the same keys replaces these).
political_power = {
	name = "Political Power"
	desc = "Spent on laws, ministers and suppressing political movements. Modifier: political_power_gain."
	start = 50
	min = 0
	max = 1000
	monthly = { value = 15 add = modifier:political_power_gain }
	color = { 210 180 90 }
	decimals = 0
}
stability = {
	name = "Stability"
	desc = "How content the country is. Moves 10% per month towards 60 plus the modifier stability_target; wars lower it. Low stability helps extremists and political movements."
	start = 60
	min = 0
	max = 100
	monthly = {
		value = 60
		add = modifier:stability_target
		subtract = stability
		multiply = 0.1
		if = { limit = { is_at_war = yes } subtract = 0.5 }
	}
	color = { 120 200 255 }
}
""")

manifest('laws', 'Laws', 'Politics', 61,
         'Groups of laws (conscription, economy, press, political parties) of which a country has one each. Changing a law costs political power and may need requirements; laws give modifiers, can ban parties or suspend elections. The AI picks laws that suit its ideology and wars.',
         requires=['politics'])
text('laws', 'game/common/laws/00_laws.txt', """# Laws (laws feature). Each top-level block is a law group; a country has exactly one law of every group.
#   default          the law countries start with (else the first) - governments can set others
#   cost             political power to enact (default: define law_cost, 100)
#   potential        shown only while true      allow   can be enacted only while true
#   modifier         modifiers while in force   on_enact effects when enacted
#   bans             parties of these ideologies get no seats and cannot govern
#   single_party     only the ruling party is allowed (and there are no elections)
#   suspends_elections  no elections while in force
#   ai_will_do       how much the AI wants it (it changes when another law is clearly better)
# Scripts: has_law = war_economy, set_law = war_economy (free, ignores requirements)

conscription = {
	name = "Conscription"
	default = limited_conscription
	volunteer_only = {
		name = "Volunteer Only"
		desc = "Only volunteers serve."
		cost = 50
		modifier = { manpower = -0.5 stability_target = 3 }
		ai_will_do = { base = 0.5 modifier = { add = 1 ruling_ideology = democratic NOT = { is_at_war = yes } } }
	}
	limited_conscription = {
		name = "Limited Conscription"
		desc = "A small share of the young men serve."
		modifier = { }
		ai_will_do = { base = 1 }
	}
	extensive_conscription = {
		name = "Extensive Conscription"
		desc = "Most young men serve for two years."
		allow = { OR = { is_at_war = yes ruling_ideology = fascist ruling_ideology = communist } }
		modifier = { manpower = 0.5 production = -0.03 stability_target = -3 }
		ai_will_do = { base = 0 modifier = { add = 2 is_at_war = yes } modifier = { add = 1.5 OR = { ruling_ideology = fascist ruling_ideology = communist } } }
	}
	service_by_requirement = {
		name = "Service by Requirement"
		desc = "Everyone who can fight serves."
		cost = 150
		allow = { is_at_war = yes }
		modifier = { manpower = 1.0 production = -0.1 stability_target = -8 }
		ai_will_do = { base = 0 modifier = { add = 2.6 is_at_war = yes manpower < 2000 } }
	}
}

economy = {
	name = "Economy"
	default = partial_mobilisation
	civilian_economy = {
		name = "Civilian Economy"
		desc = "Factories make consumer goods."
		modifier = { consumer_goods = 0.2 construction_speed = 0.1 stability_target = 5 }
		ai_will_do = { base = 0.5 modifier = { add = 1 ruling_ideology = democratic NOT = { is_at_war = yes } } }
	}
	partial_mobilisation = {
		name = "Partial Mobilisation"
		desc = "Industry prepares for war."
		modifier = { }
		ai_will_do = { base = 1 }
	}
	war_economy = {
		name = "War Economy"
		desc = "Industry serves the army first."
		allow = { OR = { is_at_war = yes ruling_ideology = fascist ruling_ideology = communist } }
		modifier = { consumer_goods = -0.3 production = 0.1 stability_target = -5 }
		ai_will_do = { base = 0 modifier = { add = 2 is_at_war = yes } modifier = { add = 1.4 ruling_ideology = fascist } }
	}
	total_mobilisation = {
		name = "Total Mobilisation"
		desc = "Everything for the war."
		cost = 150
		allow = { is_at_war = yes }
		modifier = { consumer_goods = -0.5 production = 0.2 stability_target = -12 }
		ai_will_do = { base = 0 modifier = { add = 2.7 is_at_war = yes stability > 40 } }
	}
}

press = {
	name = "Press"
	default = state_press
	free_press = {
		name = "Free Press"
		desc = "Newspapers print what they want."
		allow = { NOT = { ruling_ideology = fascist ruling_ideology = communist } }
		modifier = { democratic_drift = 0.3 stability_target = 3 }
		ai_will_do = { base = 0 modifier = { add = 2 ruling_ideology = democratic } }
	}
	state_press = {
		name = "Guided Press"
		desc = "The government keeps an eye on the newspapers."
		modifier = { ruling_party_drift = 0.15 }
		ai_will_do = { base = 1 }
	}
	propaganda_ministry = {
		name = "Propaganda Ministry"
		desc = "The state tells the people what to think."
		allow = { NOT = { ruling_ideology = democratic } }
		modifier = { ruling_party_drift = 0.5 political_power_gain = -3 }
		ai_will_do = { base = 0 modifier = { add = 2 OR = { ruling_ideology = fascist ruling_ideology = communist } } }
	}
}

political_parties = {
	name = "Political Parties"
	default = multi_party
	multi_party = {
		name = "Free Parties"
		desc = "Every party may stand in elections."
		modifier = { }
		ai_will_do = { base = 1 modifier = { add = 1 ruling_ideology = democratic } }
	}
	restricted_parties = {
		name = "Extremists Banned"
		desc = "Communist and fascist parties are banned."
		allow = { NOT = { ruling_ideology = fascist ruling_ideology = communist } }
		bans = { communist fascist }
		modifier = { stability_target = -2 }
		ai_will_do = { base = 0 modifier = { add = 1.5 ruling_ideology = neutrality } modifier = { add = 1 ruling_ideology = democratic OR = { popularity_communist > 0.3 popularity_fascist > 0.3 } } }
	}
	single_party = {
		name = "One-Party State"
		desc = "Only the ruling party is allowed. There are no elections."
		cost = 150
		allow = { NOT = { ruling_ideology = democratic } }
		single_party = yes
		modifier = { political_power_gain = 5 ruling_party_drift = 0.3 stability_target = -3 }
		ai_will_do = { base = 0 modifier = { add = 2.5 OR = { ruling_ideology = fascist ruling_ideology = communist } } }
	}
}
""")

manifest('cabinet', 'Cabinet (ministers)', 'Politics', 62,
         'Ministers from the governing parties fill cabinet posts (head of government, foreign, economy, security, army); each gives bonuses from its stat. Appointing costs political power; ministers leave when their party leaves the government.',
         requires=['politics', 'characters'])
text('cabinet', 'game/common/cabinet/00_cabinet.txt', """# Cabinet posts (cabinet feature). Ministers are characters of a governing party (missing ones are generated).
#   stat        character stat that makes a minister good at the post
#   per_point   modifiers per point of that stat      modifier   modifiers while the post is filled
#   cost        political power to appoint (default: define minister_cost, 50)

head_of_government = {
	name = "Head of Government"
	stat = charisma
	per_point = { political_power_gain = 0.5 stability_target = 0.5 }
}
foreign_minister = {
	name = "Foreign Minister"
	stat = finesse
	per_point = { opinion = 1 }
}
economy_minister = {
	name = "Economy Minister"
	stat = finesse
	per_point = { production = 0.01 construction_speed = 0.01 income = 0.01 }
}
security_minister = {
	name = "Security Minister"
	stat = zeal
	per_point = { stability_target = 0.5 }
	modifier = { ruling_party_drift = 0.1 }
}
army_chief = {
	name = "Chief of the Army"
	stat = martial
	per_point = { attack = 0.005 defense = 0.005 org_recovery = 0.01 }
}
""")

manifest('civil_wars', 'Political movements & civil wars', 'Politics', 63,
         'Ideologies shut out of power gather in movements with a demand (legalise their party or put them in power) that grow until they revolt unless you give in or suppress them (Victoria 3 style). A shut-out ideology with majority support can also start a civil war at any time (HOI4 style). Civil wars split the country: the rebels get part of the land and army, and if they win they take over the country. Without armies and wars, revolts are coups.',
         requires=['politics'])

# ------------------------------------------------------------------ presets (mechanics bundles)
P = os.path.join(A, 'presets'); os.makedirs(P)
def preset(pid, order, name, desc, features, defines):
    s = f'# Preset: the features (and rules) a new project starts with.\nname = "{name}"\norder = {order}\ndescription = "{desc}"\n'
    s += 'features = { ' + ' '.join(features) + ' }\n'
    if defines: s += 'defines = { ' + ' '.join(f'{k} = {v}' for k, v in defines.items()) + ' }\n'
    open(os.path.join(P, pid + '.txt'), 'w', newline='\n').write(s)
preset('blank', 1, 'Blank slate', 'Only the core: map, countries, time, events, decisions, resources, windows and C# scripting. Add features one by one.', [], {})
preset('hoi4', 2, 'Hearts of Iron IV style', 'Armies with HOI4 combat, factories and equipment, supply hubs and railways, a division designer, fleets and transports, air wings, battle plans, lend-lease and volunteers, fog of war, wars and diplomacy, ideologies with elections, laws and civil wars, AI. Locations are taken as soon as they are entered.',
       ['military', 'combat_hoi4', 'production', 'supply_hubs', 'division_designer', 'navy', 'air', 'battle_plans', 'lend_lease', 'fog_of_war', 'diplomacy', 'politics', 'laws', 'civil_wars', 'ai'], {'combat_model': 'hoi4', 'occupation_hours': '0'})
preset('eu5', 3, 'Europa Universalis style', 'Armies with EU-style combat and sieges, fleets, diplomacy, economy, rulers and heirs, AI.',
       ['military', 'combat_eu4', 'supply_range', 'navy', 'diplomacy', 'economy', 'characters', 'ai'], {'combat_model': 'eu4', 'occupation_hours': '48'})
preset('imperator', 4, 'Imperator style', 'Characters with interactions, armies with Imperator combat and sieges, diplomacy, economy, AI.',
       ['military', 'combat_imperator', 'supply_range', 'diplomacy', 'economy', 'characters', 'character_interactions', 'ai'], {'combat_model': 'imperator', 'occupation_hours': '48'})
preset('ck3', 5, 'Crusader Kings style', 'Characters, dynasties and interactions first; armies with Imperator-style combat and long sieges, diplomacy, gold, AI.',
       ['characters', 'character_interactions', 'military', 'combat_imperator', 'supply_range', 'diplomacy', 'economy', 'ai'], {'combat_model': 'imperator', 'occupation_hours': '72'})
preset('everything', 9, 'Everything', 'Every feature the engine has (supply: hubs & railways).', ['military', 'combat_hoi4', 'combat_eu4', 'combat_imperator', 'production', 'supply_hubs',
       'division_designer', 'navy', 'air', 'battle_plans', 'lend_lease', 'fog_of_war', 'diplomacy', 'characters', 'character_interactions', 'economy', 'politics', 'laws', 'cabinet', 'civil_wars', 'ai'], {'combat_model': 'hoi4'})

# ------------------------------------------------------------------ base maps
B = os.path.join(A, 'bases')
isle = os.path.join(B, 'isle', 'game')
os.makedirs(isle)
open(os.path.join(B, 'isle', 'base.txt'), 'w', newline='\n').write(
    'name = "Island map (10 countries)"\ndescription = "The sample game\'s island: 251 locations, 48 states, 10 countries with flags. No mechanics - add them with features."\n')
keep = ['game.txt', 'map', 'localisation', 'gfx/flags', 'gfx/fonts', 'gfx/terrain', 'common/countries', 'common/terrain.txt', 'interface/theme.txt']
for rel in keep:
    src = os.path.join(SAMPLE, 'game', rel); dst = os.path.join(isle, rel)
    if os.path.isdir(src): shutil.copytree(src, dst)
    else: os.makedirs(os.path.dirname(dst), exist_ok=True); shutil.copy(src, dst)
for root, _, files in os.walk(isle):
    for f in files: os.chmod(os.path.join(root, f), 0o644)
# countries: no rulers/heirs/money (those come from the characters and economy features)
cf = os.path.join(isle, 'common/countries/countries.txt')
t = open(cf).read()
t = re.sub(r'\n\t(ruler|heir|gold|manpower) = [^\n]*', '', t)
open(cf, 'w', newline='\n').write(t)
# states: no buildings (features add them), no air regions (the air feature makes them)
for sf in os.listdir(os.path.join(isle, 'map', 'states')):
    sp = os.path.join(isle, 'map', 'states', sf)
    t = open(sp).read()
    t = re.sub(r'\n\t+buildings = \{[^}]*\}', '', t)
    t = re.sub(r' +buildings = \{[^}]*\}', '', t)
    open(sp, 'w', newline='\n').write(t)
ar = os.path.join(isle, 'map', 'air_regions.txt')
if os.path.exists(ar): os.remove(ar)
gt = os.path.join(isle, 'game.txt')
t = open(gt).read()
open(gt, 'w', newline='\n').write(t)

# shared core files for every base: defines, welcome event, on_actions, features.txt (empty)
CORE_DEFINES = '''# Game rules. Every value has a default in the engine; delete a line to use the default.
# Features add their own rules here when they are installed (combat_model, fog_of_war...).
start_date = 1836.1.1
end_date = 1936.1.1
pause_on_event = yes            # events can override this with pause = yes/no/{ trigger }
# in-game days per real second for speeds 1-5 (0 = as fast as the computer can go)
game_speeds = { 0.5 1.5 4 10 0 }
'''
WELCOME = '''# Events. Add more: right-click this file in the Content Browser > New country/character event.
namespace = start

country_event = {
	id = start.1
	title = "Welcome to [ROOT]"
	desc = "This game starts as a blank slate. Add mechanics in Burger Studio with Project > Features."
	is_triggered_only = yes
	trigger = { is_player = yes }
	option = { name = "Let us begin!" }
}
'''
ON_ACTIONS = '''# On actions: run events or effects when something happens.
on_game_start = {
	events = { start.1 }
}
'''
def core(game):
    for rel, text in [('common/defines.txt', CORE_DEFINES), ('events/start_events.txt', WELCOME), ('common/on_actions/00_on_actions.txt', ON_ACTIONS),
                      ('features.txt', '# Features (mechanics) this game uses. Manage them in Burger Studio: Project > Features,\n# or with: burger features <project> add|remove <id>\nfeatures = {\n}\n')]:
        p = os.path.join(game, rel); os.makedirs(os.path.dirname(p), exist_ok=True)
        if not os.path.exists(p): open(p, 'w', newline='\n').write(text)
core(isle)

# tiny base: written by 'burger new --blank' code before; here as plain files
tiny = os.path.join(B, 'tiny', 'game')
os.makedirs(tiny)
open(os.path.join(B, 'tiny', 'base.txt'), 'w', newline='\n').write(
    'name = "Tiny test map (2 countries)"\ndescription = "Six locations in two states on a small island: the smallest valid map, good for testing mechanics."\n')
print('assets ok')

from PIL import Image
w, h = 512, 256
prov = Image.new('RGB', (w, h)); hgt = Image.new('L', (w, h)); ter = Image.new('RGB', (w, h))
land_colors = [(200, 40, 40), (40, 200, 40), (40, 40, 200), (200, 200, 40), (200, 40, 200), (40, 200, 200)]
pp, hp, tp = prov.load(), hgt.load(), ter.load()
for y in range(h):
    for x in range(w):
        land = 96 <= x < 416 and 48 <= y < 208
        c = (10, 10, 120)
        if land:
            gx = min(2, (x - 96) // 107); gy = (y - 48) // 80
            c = land_colors[gy * 3 + gx]
        elif x >= w // 2: c = (10, 10, 160)
        pp[x, y] = c
        hp[x, y] = (40 + ((x * 7 + y * 13) % 40)) if land else 10
        tp[x, y] = ((86, 124, 27) if x < 256 else (0, 86, 6)) if land else (8, 31, 130)
os.makedirs(os.path.join(tiny, 'map', 'states'))
prov.save(os.path.join(tiny, 'map', 'provinces.png')); hgt.save(os.path.join(tiny, 'map', 'heightmap.png')); ter.save(os.path.join(tiny, 'map', 'terrain.png'))
files = {
    'game.txt': 'name = "My Game"\nversion = "0.1"\nauthor = ""\ndescription = "A new BurgerEngine game."\ndefault_country = AAA\n',
    'map/default.map': 'provinces = "provinces.png"\nheightmap = "heightmap.png"\nterrain = "terrain.png"\nlocations = "locations.txt"\nsea_level = 20\nheight_scale = 4\npixels_per_unit = 5\n',
    'map/locations.txt': '# HEX = location_name\nC82828 = first_town\n28C828 = second_town\n2828C8 = third_town\nC8C828 = fourth_town\nC828C8 = fifth_town\n28C8C8 = sixth_town\n0A0A78 = { name = west_sea type = sea }\n0A0AA0 = { name = east_sea type = sea }\n',
    'map/states/states.txt': 'west_state = {\n\towner = AAA\n\tcores = { AAA }\n\tmanpower = 150\n\ttax = 3\n\tlocations = { first_town second_town fourth_town }\n}\neast_state = {\n\towner = BBB\n\tcores = { BBB }\n\tmanpower = 150\n\ttax = 3\n\tlocations = { third_town fifth_town sixth_town }\n}\n',
    'common/terrain.txt': '# Terrain types. color = the color in map/terrain.png\nplains = { color = { 86 124 27 } texture = "gfx/terrain/plains.png" movement_cost = 1 combat_width = 80 }\nforest = { color = { 0 86 6 } display_color = { 60 100 50 } texture = "gfx/terrain/forest.png" movement_cost = 1.3 combat_width = 60 defense_modifier = 0.15 }\nocean = { color = { 8 31 130 } texture = "gfx/terrain/ocean.png" is_water = yes }\n',
    'common/countries/countries.txt': '# Countries. The three letter tag is used everywhere to refer to the country.\nAAA = {\n\tname = "Westland"\n\tcolor = { 180 60 60 }\n\tcapital = first_town\n}\nBBB = {\n\tname = "Eastland"\n\tcolor = { 60 90 180 }\n\tcapital = third_town\n}\n',
    'localisation/text.txt': 'first_town = "Firstford"\n',
}
for rel, text in files.items():
    p = os.path.join(tiny, rel); os.makedirs(os.path.dirname(p), exist_ok=True); open(p, 'w', newline='\n').write(text)
for rel in ['gfx/fonts', 'gfx/terrain']:
    shutil.copytree(os.path.join(isle, rel), os.path.join(tiny, rel))
os.makedirs(os.path.join(tiny, 'interface')); shutil.copy(os.path.join(isle, 'interface/theme.txt'), os.path.join(tiny, 'interface/theme.txt'))
for root, _, fs in os.walk(tiny):
    for f in fs: os.chmod(os.path.join(root, f), 0o644)
core(tiny)
print('tiny ok')

# earth 1930 base: generated map (assets_src/earth1930, made by tools/earth/make_earth.py) + shared files
src_earth = os.path.join(ENGINE, 'assets_src', 'earth1930', 'game')
if os.path.isdir(src_earth):
    from PIL import Image, ImageEnhance, ImageOps
    eb = os.path.join(B, 'earth1930')
    earth = os.path.join(eb, 'game')
    shutil.copytree(src_earth, earth)
    open(os.path.join(eb, 'base.txt'), 'w', newline='\n').write(
        'name = "Earth 1930"\ndescription = "The whole world on 1 January 1930: about 6400 locations, 1450 states and 74 countries with 1930 borders. '
        'Colonies belong to their empires; dominions and the British Raj are subjects of the United Kingdom (with the diplomacy feature)."\n')
    for rel in ['gfx/fonts', 'interface/theme.txt']:
        a, b = os.path.join(isle, rel), os.path.join(earth, rel)
        if os.path.isdir(a): shutil.copytree(a, b, dirs_exist_ok=True)
        else: os.makedirs(os.path.dirname(b), exist_ok=True); shutil.copy(a, b)
    tdir = os.path.join(earth, 'gfx', 'terrain')
    os.makedirs(tdir, exist_ok=True)
    for f in os.listdir(os.path.join(isle, 'gfx', 'terrain')):
        shutil.copy(os.path.join(isle, 'gfx', 'terrain', f), os.path.join(tdir, f))
    def tinted(src, rgb, amount, bright=1.0):
        im = Image.open(os.path.join(tdir, src)).convert('RGB')
        im = Image.blend(im, Image.new('RGB', im.size, rgb), amount)
        return ImageEnhance.Brightness(im).enhance(bright)
    tinted('forest.png', (20, 70, 30), 0.35, 0.85).save(os.path.join(tdir, 'jungle.png'))
    tinted('plains.png', (150, 155, 140), 0.55, 1.0).save(os.path.join(tdir, 'tundra.png'))
    tinted('desert.png', (240, 245, 250), 0.8, 1.05).save(os.path.join(tdir, 'glacier.png'))
    open(os.path.join(earth, 'game.txt'), 'w', newline='\n').write(
        'name = "Earth 1930"\nversion = "0.1"\nauthor = ""\ndescription = "The world in 1930."\ndefault_country = GER\n')
    os.makedirs(os.path.join(earth, 'common'), exist_ok=True)
    open(os.path.join(earth, 'common', 'defines.txt'), 'w', newline='\n').write(CORE_DEFINES.replace('start_date = 1836.1.1', 'start_date = 1930.1.1').replace('end_date = 1936.1.1', 'end_date = 1950.1.1') + 'km_per_pixel = 5.0\n')
    core(earth)
    for root, _, fs in os.walk(earth):
        for f in fs: os.chmod(os.path.join(root, f), 0o644)
    print('earth ok')
