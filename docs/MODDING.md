# Making a game with BurgerEngine

Everything is text and images inside a project's `game/` folder. After changing files, press **Reload files** in
the main menu (or restart). Mistakes never crash the game: they go to the **error log (F2)** and to
`logs/error.log` with the file and line. `burger validate <project>` checks everything from the command line and
runs the simulation headless for 60 days.

All text files use the Paradox format:

```
# comment
key = value
key = "text with spaces"
block = { key = 1 other = { a b c } }
gold >= 100                      # comparisons in triggers: = != < > <= >=
color = { 255 0 0 }              # or rgb { 255 0 0 }, hsv { 0.5 1 1 } or "FF0000"
@my_constant = 10                # file constant, use as  value = @my_constant
```

---------------------------------------------------------------------------------------------------

## 1. The map

`map/default.map` names the input images. Every image can have a different size; all of them are resampled
to the size of the province map.

| file | what it is |
|---|---|
| `provinces.png` | **one unique color per location**. Water locations are colors too (sea zones). |
| `heightmap.png` | greyscale elevation. Water is drawn flat at `sea_level`. |
| `terrain.png` | painted with the `color` of terrain types from `common/terrain.txt`; textures blend at the edges |
| `trees.png` | greyscale tree density (0 = none) |
| `rivers.png` | every pixel that is not the background color (white by default) is a river. Borders crossed by a river give attackers a penalty (`river_attack_penalty`). |

Settings in `default.map`: `sea_level` (0-255), `height_scale`, `pixels_per_unit` (bigger = smaller map in 3D),
`tree_density`, `tree_spacing`, `water_color`, `river_color`, `river_background`. PNG, BMP, TGA and JPG all work
(8 or 16 bit; Paradox-style indexed BMPs too).

### Locations: `map/locations.txt`

Each hex color of `provinces.png` gets a location name:

```
4A7F1E = berlin
FF0000 = { name = new_york terrain = hills fort = 2 }
0A0A78 = { name = north_sea type = sea }            # type: land (default), sea, lake, impassable
```

- The name is the id used in every other file. The displayed name comes from `localisation/*.txt`
  (`berlin = "Berlin"`); if there is none, `new_york` is shown as "New York".
- Colors missing from the file still work (they are named `loc_rrggbb` and a warning is logged).
  **`burger generate-locations <project>`** appends a generated name for every missing color, so after
  painting new provinces you only have to rename them.
- Terrain is taken from the most common `terrain.png` color inside the location unless `terrain =` is given.
- `map/adjacencies.txt` adds straits (`adjacency = { from = a to = b type = strait }`) or blocks borders
  (`type = impassable`).

### States: `map/states/*.txt`

A state is a group of locations owned and cored by countries:

```
brandenburg = {
    name = "Brandenburg"          # optional display name (or a localisation key)
    owner = GER
    controller = GER              # optional, defaults to owner
    cores = { GER }
    claims = { POL }
    manpower = 250                # added to the owner's manpower each month
    tax = 4                       # gold per month
    category = city               # free text, usable in triggers (category = city)
    buildings = { workshop = 1 }  # free text levels (has_building, add_building)
    locations = { berlin potsdam frankfurt_oder }
}
```

You can also edit states in the game: **F10** opens the map editor. Click a location to use its state as the brush,
switch on *Paint*, then click locations to move them into that state. You can also create states, set owners and
cores, and **Save** the state files.

### Terrain: `common/terrain.txt`

```
forest = {
    color = { 0 86 6 }               # color in terrain.png
    display_color = { 60 100 50 }    # used if the texture is missing
    texture = "gfx/terrain/forest.png"
    movement_cost = 1.3
    combat_width = 70
    attack_modifier = -0.1
    defense_modifier = 0.15
    is_water = no
    supply = 2                       # any extra number is stored for C# scripts (terrain.Mod("supply"))
}
```

---------------------------------------------------------------------------------------------------

## 2. Countries: `common/countries/*.txt`

```
GER = {                                   # tag: exactly 3 characters A-Z / 0-9
    name = "German Empire"                # or a localisation key
    color = { 60 60 60 }
    flag = "gfx/flags/GER.png"            # default gfx/flags/<TAG>.png; a flag is generated if missing
    capital = berlin
    government = monarchy                 # free text (trigger: government = monarchy)
    gold = 200
    manpower = 20000
    playable = yes
    ruler = ger_kaiser                    # character ids from history/characters
    heir = ger_crown_prince
    names = { male = { Otto Karl } female = { Luise } family = { "von Hohenzollern" } }  # for generated characters
}
```

Use the tag everywhere: `owner = GER`, `GER = { add_gold = 10 }`, `is_at_war_with = GER`, `c:GER`.
During play, `set_country_name = "German Republic"`, `set_country_color = { 200 0 0 }` and
`set_flag_image = "gfx/flags/GER_republic.png"` change the name, color and flag (`reset_...` restores them).

---------------------------------------------------------------------------------------------------

## 3. Characters (Imperator style)

`history/characters/*.txt`:

```
ger_kaiser = {
    first_name = Wilhelm
    family = "von Hohenzollern"
    birth_date = 1797.3.22          # or age = 39
    female = no
    country = GER
    martial = 6 finesse = 5 charisma = 7 zeal = 4     # stats from character_stats in defines.txt
    traits = { brave just }
    spouse = ger_kaiserin           # father / mother / spouse = ids defined earlier
    general = yes                   # may lead armies
    portrait = "gfx/portraits/wilhelm.png"
}
```

Missing values are randomised. In events you can create a character with
`create_character = { first_name = Karl age = 30 martial = 8 traits = { brave } save_scope_as = new_general }`.
Characters age, marry, have children (married women, `birth_chance_per_year`), die (age, traits with `health`),
and rulers are succeeded by their heir or eldest child. The succession rules can be replaced in C#
(`Game.Rules.Characters`).

Traits (`common/traits.txt`): any number changes the stat or modifier with that name, for example
`martial = 2 general_attack = 0.1 health = -20 fertility = 0.3`. Traits also take `opposites`, `random_weight` and
`inherit_chance`.

---------------------------------------------------------------------------------------------------

## 4. Warfare (Hearts of Iron IV style)

**Units:** `common/units/*.txt` has `unit_types` (battalions) and `division_templates` (sets of battalions).
Stats are free-form numbers that are combined using `stat_aggregation` (sum by default). Combat models read the
stats they need:

| model (`combat_model` in defines) | stats it uses |
|---|---|
| `hoi4` - hourly, width, org/strength | soft_attack hard_attack defense breakthrough hardness armor piercing combat_width |
| `eu4` - daily dice, fire/shock phases, morale | fire shock morale discipline |
| `imperator` - daily d6 + general martial | offense defensive morale |
| all | max_strength max_organisation speed (km/h) |

**Armies and generals:** every division can belong to an *army*, and an army is led by a general (any
adult character; martial and traits such as `general_attack` give bonuses). Starting armies go in
`history/units/*.txt`:

```
GER = {
    army = {
        name = "1st Army"
        general = ger_moltke
        division = { template = infantry_division location = berlin count = 4 }
        front = FRA            # optional: start with a front
        offensive = yes
    }
    division = { template = cavalry_division location = potsdam }   # division without army
}
```

**In the game (F1):** give an army a **front** against an enemy country. Its divisions spread along the border
automatically. Switch between **Hold** and **Attack**: attacking divisions strike the weakest enemy location next to
their post once their organisation has recovered. You can also select divisions (click their counters) and
**right-click** to move or attack directly, EU4/Imperator style.

Battles happen when units move into a location held by enemy units. Units retreat when their organisation
(morale) reaches 0, and they are destroyed if they have nowhere to retreat to. Locations are occupied instantly
(HOI4), or after a siege if `occupation_hours > 0` (EU4/Imperator, multiplied by fort level + 1).

**Battle plans** (`battle_plans` feature): in the Military menu an army can also get a *drawn front* (click border
locations: the front covers only that part of the border), an *offensive arrow* (click an objective: attacks prefer
steps towards it) and a *fallback line* (click your own locations). The mode button cycles **Hold**, **Attack** and
**Fallback** (hold the fallback line). Plans are saved with the army. To give an army a front, click its **Front**
button, then click the country on the map (an enemy, or a neighbour at peace); clicking your own land removes the front.
A changed or vanished front takes its drawn front segment and attack arrow with it.

**Fog of war** (`fog_of_war` feature): land you cannot see is greyed out. The **Fog** button on the map mode bar (or the
F key; `--no-fog-shading` on the command line) turns the shading off; enemy units you cannot see stay hidden.

**Supply** (`supply_range` or `supply_hubs`): every day each country gets a supply map. *Range*: supply spreads
from the capital and your own land, weaker with each step (rough terrain costs more, infrastructure less) and only
through friendly land, so encircled armies starve. *Hubs*: supply comes from the capital and from `supply_hub`
buildings that railways (`railway` levels in states) connect to the capital. Divisions with less supply than they use
lose strength, recover less organisation and fight worse. Defines: `supply_capital`, `supply_local`, `supply_hub`,
`supply_falloff`, `supply_attrition`. Rules: `backend/Supply.cs`.

**Production** (`production`): `common/equipment/*.txt` lists equipment (cost, category army/naval/air,
`default_for` unit categories); unit types may list `equipment = { infantry_equipment = 100 }`. States have
`buildings = { civilian_factory = 2 military_factory = 1 }`; military factories work on production lines
(Production menu), civilian factories build buildings from `common/buildings/*.txt`. New divisions deploy only with
their equipment. Rules: `backend/Production.cs`.

### Navy (`navy` feature)

`common/ships/*.txt`:

```
ship_types = {
    destroyer = { category = screen hp = 20 speed = 32 light_attack = 4 armor = 1 cost = 40 production_cost = 1200 manpower = 200 build_days = 120 }
    transport = { category = transport hp = 15 speed = 20 transport = 2 cost = 30 production_cost = 600 }
}
```

Fleets move through sea locations and ports (own or allied coastal land). When fleets of countries at war meet they
fight: screens shield capital ships, light attack hits screens, heavy attack hits capital ships, armor reduces damage,
and a side retreats below `naval_retreat` of its hit points. Ships repair in port (faster with `naval_base`). Ships
with `transport` let divisions cross the sea: order them to a location across the water and they embark; armies at sea
without an escort are sunk by enemy fleets, and landing on an enemy coast fights at `naval_invasion_penalty`. With
production, dockyards (`dockyard` buildings) build ships on production lines; otherwise ships are bought with gold.
Starting fleets: `history/fleets/*.txt` - `TAG = { fleet = { name = "Home Fleet" location = port ships = { destroyer = 2 } } }`.
In the game: Navy menu (N); select a fleet (click its counter) and right-click a sea location or port. Rules: `backend/Navy.cs`.

### Air (`air` feature)

`map/air_regions.txt` groups states into air regions (`region = { name = "..." states = { a b c } }`; states in no
region are grouped automatically). `common/planes/*.txt` has `plane_types` with `range` (km), `air_attack`,
`air_defense`, `agility`, `ground_attack`, `strategic_attack`. Air wings (`air_wing_size` planes) sit at air bases
(`air_base` levels, `air_base_capacity` planes each) and fly a mission over a region within range:
**air superiority** (the side holding the sky gets up to `air_superiority_bonus` in combat there), **close air
support** (damages enemy divisions in battles in that region) or **strategic bombing** (knocks out factories,
railways and infrastructure until repaired; production feature). Starting wings: `history/air/*.txt` -
`TAG = { wing = { type = fighter base = state planes = 50 } }`. In the game: Air menu and the Air map mode. Rules: `backend/Air.cs`.

### Lend-lease and volunteers (`lend_lease` feature)

In another country's window (Support): send volunteer divisions to a country at war (they fight for it without you
joining the war, and come home at peace; at most `volunteer_share` of your divisions) and, with production, a share
of your equipment stockpile every month. Rules: `backend/Support.cs`.

**Writing your own combat** - see *C# scripting* below (`ICombatModel`).

---------------------------------------------------------------------------------------------------

## 4b. Politics (Hearts of Iron IV style)

Four features: `politics` (ideologies, elections, parliament), `laws`, `cabinet` (needs `characters`) and `civil_wars`.
In the game: the Politics menu (O) and the Ideology map mode; another country's window shows its government.

**Ideologies** - `common/ideologies/*.txt`. Every country has a popularity for each ideology (adding up to 100%) and a
ruling party of one ideology. Keys: `name`, `adjective` (rebel names: "Nationalist Spain"), `party_name`, `color`,
`elections = yes` (countries it rules hold elections), `extremist = yes` (gains support from low stability and lost
wars), `coalition = { ... }` (parties it governs with - both must list each other), `opinion = { fascist = -20 }`
(how its rulers see countries ruled by each ideology), `modifier = { ... }` (for countries it rules), `government`
(optional government word set when it takes power), `peace_puppets = yes/no` (AI winners ruled by it set up subjects of
the peoples they defeat instead of taking foreign land; default: ideologies with elections, i.e. democracies).

**Starting politics** - `history/politics/*.txt` per country, else `common/governments/*.txt` by the country's
`government` word (`default` for any other word):
```
GER = {
    ruling_party = democratic
    popularity = { democratic = 40 fascist = 18 communist = 13 neutrality = 29 }   # any numbers, scaled to 100%
    parties = { fascist = "NSDAP" democratic = "SPD" }
    seats = 491                    # parliament size (define parliament_seats, 100)
    last_election = 1928.5.20      # elections then follow every election_frequency_months (48)
    laws = { conscription = volunteer_only }
}
```

**Popularity drift** (monthly, `backend/Politics.cs` `MonthlyDrift`): modifiers named `IDEOLOGY_drift` and
`ruling_party_drift` (points per month), propaganda of rulers without elections (`ruling_party_drift` define), lost
wars, low or high stability, ideologies ruling the neighbours, and chance.

**Elections**: votes = popularity with a little chance; seats by D'Hondt among parties that are not banned and pass
`election_threshold` (0.05). The biggest party that can form a majority with willing partners governs (coalition);
otherwise the biggest party governs alone. When an ideology without elections wins, elections stop.

**Political power and stability** are resources (`common/resources/00_politics.txt`; your own file with the same
keys replaces them). Modifier keys: `political_power_gain` (per month), `stability_target`.

**Laws** - `common/laws/*.txt`: each block is a law group whose blocks are its laws. Law keys: `cost` (political
power, define `law_cost`), `potential`, `allow`, `modifier`, `bans = { communist fascist }`, `single_party = yes`,
`suspends_elections = yes`, `on_enact`, `ai_will_do`. A group changes at most every `law_change_cooldown_days` (180).

**Cabinet** - `common/cabinet/*.txt`: posts with `stat`, `per_point = { ... }` (times the minister's stat),
`modifier`, `cost` (define `minister_cost`). Ministers are characters of a governing party (flag `party_IDEOLOGY`);
missing candidates are generated. They step down when their party leaves the government.

**Movements and civil wars** (`civil_wars`): an ideology shut out of power (banned, or no elections it could win) with
at least `movement_threshold` (0.25) support forms a movement demanding to legalise its party or to take power. It
grows each month (faster with low stability, `movement_speed`) and revolts at 100%: give in (its demand happens) or
suppress it (`suppress_movement_cost` political power, some stability). A shut-out ideology above `coup_threshold`
(0.4) may seize power in a coup (`coup_chance` a month, likelier the more popular it is and the less popular the
government and stability are); above `civil_war_threshold` (0.5) it may also start a civil war (`civil_war_chance`).
A revolt is a coup instead of a civil war when the government's parties have less than `civil_war_loyal_share` (0.2)
support or the rebels more than `coup_above` (0.75). After a coup or civil war, `revolt_calm_years` (5) pass before the
next. A ruling party without elections keeps at least about `ruling_party_floor` (0.15) support. A civil war splits
off a share of the country's home states, army, treasury and manpower equal to the ideology's support (10-90%) as a new
country ruled by that ideology (tags R00, R01...); the Ledger counts the rebels as part of the country meanwhile. It ends
when one side has lost its capital and most of its land (`civil_war_defeat_share`, 0.3): if the rebels win, the
country keeps its name and tag but takes their government, laws and army. Civil wars cannot end with a peace deal.
Without armies and wars (no `military`/`diplomacy`), revolts are coups.

**Parties of minorities and faiths** (with population): in elections, people of an unaccepted culture vote for its
national party (`minority_party_turnout`, 70%, of them, if they are at least `minority_party_min_share`, 3%) and people of
another faith that make up `religious_party_min_share` (10%) for a religious party (`religious_party_turnout`, 40%). Only
people outside colonies vote. These parties (seat keys `culture:KEY`, `religion:KEY`) win seats but never govern; national
parties speed up independence movements. Rule `MinorityVotes` in backend/Politics.cs.

**Election campaigns**: `election_campaign_days` (60) before an election the engine fires `on_election_campaign`; the
politics feature ships campaign events (`events/politics_elections.txt`: rally the voters, promise reforms, a quiet campaign,
scandals). `add_popularity = { ideology = ruling value = 0.05 }` changes the ruling party's support.

**Culture policy** (population + laws): the law group `culture_policy` (`common/laws/10_culture_policy.txt`, shown only
with population: law groups can have `potential`) - Equal Rights, Tolerance, Discrimination, Forced Assimilation. Modifier
keys `minority_output` (unaccepted people count as this share of accepted ones for tax and manpower), `culture_tension`
(how much unaccepted cultures lower stability), `assimilation`, `independence_speed`.

**Scripts**: triggers `ruling_ideology = X` (or `has_government`), `in_government = X`, `same_ideology = scope:recipient`,
`holds_elections = yes`, `party_banned = X`, `has_law = L`, `has_movement = yes|X`, `in_civil_war = yes`,
`has_minister = post`; values `popularity_X`, `seats_X`, `ruling_popularity`, `months_to_election`,
`movement_progress`; effects `add_popularity = { ideology = X value = 0.05 }`, `set_ruling_party = X`,
`set_law = L`, `hold_election = yes`, `start_civil_war = { ideology = X size = 0.4 }`,
`set_party_name = { ideology = X name = "..." }`. On actions: `on_election`, `on_ruling_party_changed`,
`on_law_changed`, `on_movement_formed`, `on_civil_war_started` (scope:rebel), `on_civil_war_ended`.
Console: `politics [TAG]`, `popularity X 0.1`, `ideology X`, `election [TAG]`, `law L`, `civilwar X [share] [TAG]`.

---------------------------------------------------------------------------------------------------

## 4c. Population (Imperator style)

Features: `population` (pops, cultures), `religion`, `social_classes`, `cores_claims`, `colonization`, `independence`, and the content
packages `buildings_classic` (older eras) and `buildings_modern`. In the game: the People menu (U), the People,
Culture and Religion map modes, the people of a state in its window, and the Ledger (F9).

**Cultures** - `common/cultures/*.txt`: culture groups with their cultures inside (`name`, `color`, `religion` of new
pops). Cultures of one group accept each other (define `culture_group_accepted = no` to turn off). A country sets
`culture = german`, `religion = protestant` and `accepted_cultures = { ... }` in common/countries; without them it takes
the biggest culture/faith of its states (a game without cultures gets one culture per country automatically).

**Pops** - `history/pops/*.txt`:
```
berlin_state = {
    pop = { culture = german religion = protestant size = 4200000 }
    pop = { culture = jewish religion = judaism size = 160000 }    # type = burghers with social_classes
    owned_since = 1871.1.18                                         # for cores (optional)
}
```
States without an entry get people of the owner's culture (`pops_per_manpower` x manpower). Every month pops grow
(`pop_growth`, slowing as the state fills its room: `pop_capacity_factor` x the starting people), people of unaccepted
cultures assimilate (`assimilation_rate`), people of other faiths convert (`conversion_rate`) - except in colonies (states with
`category = colony`, or non-core states not connected by land to the capital) - people move from crowded
states to emptier ones of the same country (`migration_rate`). A state's tax and manpower are its base values times its
people compared to the start, less for unaccepted cultures, other faiths and non-core states (`noncore_output`).
Unaccepted cultures and other faiths lower stability (`culture_stability`, `religion_stability`). Countries can accept
a culture that makes up at least `accept_culture_share` of their people (People menu; political power or gold).
People in colonies (states with `category = colony`, or non-core states not connected by land to the capital) do not count
for the culture and religion figures, stability or accepting cultures: a people that lives only in colonies cannot be accepted.

Modifier keys: `pop_growth` (yearly, state or country), `pop_capacity` (+share), `assimilation`, `conversion`,
`promotion` (+share of speed), `colonists`, `colony_speed`, `colony_range`.

**Social classes** - `common/pop_types/*.txt`: `share` of new pops, `growth`, `promote_to` / `promote_rate`, and a
`modifier` block (state `tax` / `manpower` at 100% of the people in that class, scaled by its share).

**Religion** - `common/religions/*.txt`: groups with religions inside. Opinion: same religion `same_religion_opinion`
(10), same group `same_religion_group_opinion` (0), other `other_religion_opinion` (-10).

**Cores and claims** (`cores_claims`): an owner can only core a state where at least `core_culture_share` (half) of the
people are of cultures it accepts: at once if its own culture is the majority, otherwise after `core_years` (25) of
ownership (rule `CanCore` in backend/Population.cs); former owners lose a core after `core_loss_years` (50) unless their culture lives there
(a claim remains). Every January countries claim neighbouring states where their culture is the majority; the action
`claim_fellow_believers` claims states of fellow believers (religion).

**Colonization** (`colonization`): send colonists to an unowned state within `colony_range_km` (1200 km) of your land
(`colonize_cost` gold, `colony_upkeep` gold a month with the economy); it grows `colony_speed` (4%) a month, natives
slow it and attack now and then (`native_resistance`), and at 100% it becomes your state with `colony_settlers` people.

**Independence** (`independence`): people of an unaccepted culture that is the majority in some states (never the capital
state) form a movement once they are `independence_min_share` (2%) of the country. It grows each month (rule
`IndependenceProgress`: `independence_speed`, 0.4; slower in colonies than in conquered land - `colonial_independence_factor` 0.6 - faster at low stability and
in lost wars; modifier `independence_speed`). The owner can suppress it (`suppress_independence_cost`, more each time),
release the states, or accept the culture. At 100% the states declare independence: a fallen country of that culture comes
back, or a new one forms (culture `country_name`, default "<Culture> Republic"); with armies and wars it raises a militia
(`independence_people_per_division`), part of the garrison defects (`independence_garrison_defect`) and it wins if it still
holds half its land after `independence_war_months` (24). On actions: `on_independence_movement`, `on_independence_declared`,
`on_independence` (scope: the new country, `scope:origin`).

**Backend rules** (`backend/Population.cs`, class `GamePopulationRules`): the engine keeps the bookkeeping (pops, saves,
when things run) and asks these rules for every decision, so a game can change them without touching the engine.
Pops: `StartingPops`, `YearlyGrowth`, `MonthlyGrowth` (the capacity curve), `Capacity`, `YearlyAssimilation`,
`YearlyConversion`, `YearlyPromotion`, `MigrationTarget`, `MonthlyMigration`, `OutputFactors`, `StabilityPenalty`.
Cultures and cores: `IsAccepted`, `AcceptCultureCost`, `CannotAcceptCulture`, `CanCore`, `CoreLossYears`,
`ClaimsNeighbour`, `IsColony`. Colonization: `MaxColonies`, `ColonizeCost`, `ColonyRangeKm`, `ColonyUpkeep`,
`ColonyProgress`, `NativeAttack`, `ColonySettlers`, `ColonyBaseOutput`, `AiColonyScore`. Independence:
`SeparatistCulture` (which states want to leave, and with whom), `IndependenceMinShare`, `IndependenceProgress`,
`SuppressIndependenceCost`, `SuppressIndependenceEffect`, `DecolonisationPressure`, `ColonialOpinion`,
`IndependentCountryName`, `GarrisonDefectChance`, `IndependenceDivisions`, `IndependenceWarOutcome`,
`AiIndependenceResponse`. Religion: `ReligionOpinion`. A project whose Population.cs predates a rule uses the engine default for it.

**Buildings**: any building can have `modifier = { ... }` (its state, per level) and `country_modifier = { ... }` (its
owner, per level). Without production they are bought with gold in the state window (`gold_cost`, `build_months`); with
production they are built with civilian factories (`cost`).

**Scripts**: triggers `culture = X` / `culture_group = X` / `religion = X` / `religion_group = X` (country: its own;
state or location: the majority), `has_accepted_culture = X`, `same_culture` / `same_religion = scope:recipient`,
`has_fellow_believers_in = scope:recipient`, `is_colonizing = yes`; values `population` (country, state), `accepted_share`,
`faithful_share`, `num_of_colonies`; effects `add_pops = { culture = X religion = Y size = 1000 }` (state; negative
removes), `set_culture`, `add_accepted_culture`, `remove_accepted_culture`, `set_state_religion`,
`convert_pops = { religion = X share = 0.5 }`, `assimilate_pops = { culture = X share = 0.5 }`,
`add_religious_claims = scope:recipient`. On action: `on_colony_founded` (scope:state).

---------------------------------------------------------------------------------------------------

## 5. Diplomacy (Imperator style)

- **Opinion**: modifiers that decay (`add_opinion`, `reverse_add_opinion`) plus a base from the relationship.
- **Relations**: alliances, guarantees, military access, subjects (`common/subject_types.txt`), truces, claims.
  The number of alliances, guarantees and subjects is limited (`base_diplomatic_relations` + ruler charisma).
- **Wars**: declared with a casus belli (`common/casus_belli.txt`, EU style; the player picks one in the Declare War window).
  A casus belli has a `war_goal` (conquest, claims, cores, culture, religion, liberation, ideology, independence,
  subjugation, humiliation): claims/cores/culture/religion/liberation are valid only if the target has such states.
  Demands that match the goal cost `goal_cost_multiplier`, a deal can cost at most `max_demands`, and with
  `ticking_war_score` holding the goal states adds war score every month (up to `max_ticking_war_score`, 25; not holding
  any after a year takes it away). The defender and every ally that joins add their own goal (their best land casus belli
  against an enemy): **faction war goals** - their states are cheap for them and go to them in peace deals.
  Defenders' allies and guarantors are called in; allies can refuse (which breaks the alliance).
  **War score** comes from occupation, battles and the ticking goal.
- **War exhaustion** (0-`max_war_exhaustion`, 20): grows while at war (`war_exhaustion_monthly`), with enemy occupation
  (`war_exhaustion_occupation`), men lost in battle compared to the army (`war_exhaustion_casualties`) and fronts that do
  not move for a year (`war_exhaustion_stalemate`); falls at peace (`war_exhaustion_recovery`). It lowers stability,
  manpower and political power, keeps exhausted allies at home and makes countries accept peace sooner (rules
  `MonthlyWarExhaustion`, `WarExhaustionModifier`, `PeaceWillingness`).
- **Peace**: demand states, subjugation, annexation, gold, freeing the loser's subjects (independence), releasing a people of
  the loser as a country, regime change (the winner's ideology takes power) or humiliation. Each costs war score. The AI
  accepts if your war score (plus its war exhaustion, a long war or a stalemate) covers the cost. AI winners demand their
  war goals first; the AI offers peace when losing, worn out, or when a war goes nowhere. The old ruler in a war of
  independence accepts a white peace only when losing or exhausted. Allies and subjects of the winner only get land that
  touches their own; the rest goes to the war leader. Democracies (`peace_puppets`, rule `PrefersPuppets`) make the loser
  a subject when they hold much of its people's land, release other peoples of the land they hold as subjects, and take
  only land of their own peoples, cores and claims.
- **Subjects** follow their overlord's ideology (set when they become subjects and kept monthly). A country is never made
  a subject of an overlord that already rules its homeland (same primary culture, or half of its core states): it is
  annexed instead, and such existing subjects are annexed. Subjects cannot have subjects of their own.
  `BURGER_PEACE_LOG=1 burger validate <project> --days N` prints every peace deal and the subjects at the end.
- **Liberty desire** (subjects, 0-100, EU style): from the subject's strength and size compared to its overlord, its opinion
  of it, a different ideology or culture group, the overlord losing a war, the tribute, `liberty_desire` of the subject type
  and the modifier `liberty_desire`. At 50 (`liberty_desire_rebel`) the subject stops joining its overlord's wars and may
  fight a War of Independence (casus belli `independence`); the AI overlord releases subjects that want out and could win.
- **Non-aggression pacts**: no war between the two for 10 years; breaking one angers everyone (`break_pact_opinion`).
- **Diplomatic plays**: the action Demand State asks for a state we claim (or have a core on); a refusal means war.
- **The end of a country**: when a country loses its last state its armies, fleets, air wings, lend-lease and factory
  queues go too; if it was the player's, the player becomes an observer.
- **Opinion of colonial empires** (independence feature): countries with few colonies dislike countries with many
  (`colonial_opinion_per_colony`, up to `max_colonial_opinion`), more so as decolonisation pressure grows with every country
  that gains its independence; granting independence is applauded.

Scripts: triggers `has_casus_belli = { target = scope:recipient type = reclaim_cores }` (no type: any but conquest),
`has_non_aggression_pact = scope:recipient`, `ideological_enemy = scope:recipient`, `same_religion_group = scope:recipient`;
values `war_exhaustion`, `liberty_desire`; effects `create_non_aggression_pact = { target = scope:recipient years = 10 }`,
`break_non_aggression_pact = scope:recipient`, `add_war_exhaustion = 2`, `cede_claimed_state = scope:recipient`.

### Diplomatic actions: `common/diplomatic_actions/*.txt`

Every button in the diplomacy panel is data:

```
offer_alliance = {
    name = "Offer Alliance"
    desc = "Allies are called to arms when either of us is attacked."
    cost = 0                       # gold
    cooldown_days = 365
    potential = { NOT = { is_allied_with = scope:recipient } }       # shown only if true
    allow = { free_relations >= 1 }                                    # clickable only if true (listed in tooltip)
    requires_acceptance = yes
    ai_acceptance = {                                                  # > 0 means yes
        base = -25
        opinion_factor = 0.5          # + their opinion of us × 0.5
        strength_factor = 10          # + 10 × (our strength / their strength - 1)
        modifier = { add = 15 borders = scope:recipient }
    }
    on_accept = { create_alliance = scope:recipient }
    on_decline = { reverse_add_opinion = { target = scope:recipient value = -5 } }
    ai_will_do = { base = 0 modifier = { add = 1 scope:recipient = { opinion = { target = ROOT value > 25 } } } }
}
```

Scopes: `ROOT` / `scope:actor` = the country doing it, `scope:recipient` = the other country.
**Character interactions** (`common/character_interactions/`) use the same format, with `scope:target` = the character.
The C# rules (`Game.Rules.Diplomacy`: opinions, war rules, call to arms, peace costs, AI peace acceptance)
can be replaced from a script.

---------------------------------------------------------------------------------------------------

## 6. Events: `events/*.txt`

```
namespace = my_mod

country_event = {
    id = my_mod.1
    title = "The [ROOT] Revolution"            # [ROOT], [ROOT.ruler], [scope:x], [ROOT.gold] are replaced
    desc = my_mod.1.desc                        # text or localisation key
    picture = "gfx/event_pictures/revolution.png"
    trigger = { tag = GER year >= 1848 NOT = { has_flag = revolution_done } }
    mean_time_to_happen = { months = 12 modifier = { factor = 0.5 gold < 0 } }
    # is_triggered_only = yes      → only fired by on_actions/scripts
    # fire_only_once = yes  hidden = yes  major = yes
    # pause = no                   → does not pause the game for the player (default: pause_on_event in defines)
    # pause = { is_at_war = yes }  → or a trigger, checked when the event fires
    immediate = { set_flag = revolution_done }
    option = {
        name = "Proclaim the Republic"
        set_country_name = "German Republic"
        set_flag_image = "gfx/flags/GER_republic.png"
        ai_chance = { factor = 2 }
    }
    option = {
        name = "Crush them"
        trigger = { manpower >= 5000 }
        add_manpower = -5000
    }
}

character_event = {
    id = my_mod.2
    title = "[ROOT] is Ill"
    trigger = { is_ruler = yes age >= 60 }
    mean_time_to_happen = { years = 5 }
    option = { name = "Rest" add_trait = sickly employer = { add_gold = -20 } }
}
```

Fire events from scripts with `country_event = { id = my_mod.1 days = 10 }` (or `character_event`), or from
`common/on_actions/*.txt`:

```
on_new_ruler = { events = { my_mod.3 } }
on_monthly_pulse = { random_events = { 90 = 0 10 = my_mod.4 } }
on_war_declared = { effect = { add_manpower = 1000 } }
```

The hooks and their scopes are listed at the top of the sample's `00_on_actions.txt`.

---------------------------------------------------------------------------------------------------

## 7. Scripting language

Triggers check conditions and effects change the game. Both work in a **scope**: a country, character, state,
location or war.

```
# scope switches
ROOT = { ... }   THIS  PREV  GER = { }  c:GER  state:brandenburg  location:berlin  character:ger_kaiser
scope:recipient  ruler  heir  capital  owner  controller  state  employer  spouse  father  mother  overlord
ROOT.ruler.spouse = { }          # chains
owner = ROOT                     # comparing scopes in a trigger

# logic
AND = { }  OR = { }  NOT = { }  NOR = { }  NAND = { }
# iterators: any_ / all_ (triggers),  every_ / random_ (effects)
any_owned_state = { is_core_of = FRA }
any_neighbor_country = { count >= 2 is_at_war = yes }
every_owned_location = { limit = { terrain = forest } set_fort = 1 }
random_character = { limit = { is_adult = yes } add_trait = brave }
# control flow in effects
if = { limit = { gold > 100 } add_gold = -100 } else_if = { limit = { ... } ... } else = { ... }
random = { chance = 25 add_gold = 50 }
random_list = { 60 = { add_gold = 10 } 40 = { add_manpower = 500 } }
hidden_effect = { ... }    custom_tooltip = "Something happens"
# values usable in comparisons and as arguments
add_gold = income        add_gold = scope:recipient.gold        set_variable = { name = x value = 5 }
check_variable = { name = x value >= 5 }     add_gold = var:x
save_scope_as = my_target   →   scope:my_target
```

**`docs/SCRIPT_REFERENCE.md`** lists every trigger, effect, value, iterator and link. Regenerate it with
`burger docs` after adding your own in C#.

---------------------------------------------------------------------------------------------------

## 8. C# scripting

C# lives in two places: `game/scripts/` (players can mod it) and the project's `backend/` folder (compiled into the
exported EXE, players cannot change it). Everything below works the same in both.


Every `.cs` file in `game/scripts/` is compiled when the game starts. Players of your exported game can do
the same. Every class deriving from `GameScript` is created automatically. Common namespaces are already imported.
For code completion, open `game/scripts/Scripts.csproj` (created by `burger new`) in your editor.

```csharp
public class MyMod : GameScript
{
    public override void OnLoad()
    {
        // new trigger value and effect for the .txt files
        Game.Script.RegisterValue("prestige", (ctx, scope) => scope?.Vars.Get("prestige"));
        Game.Script.RegisterEffect("add_prestige", (ctx, node) =>
            ctx.ThisCountry?.Vars.Set("prestige", ctx.ThisCountry.Vars.Get("prestige") + node.AsDouble()));
        Game.Script.RegisterTrigger("is_great_power", (ctx, node) => ctx.ThisCountry?.OwnedStates.Count >= 10 == node.AsBool(true));

        // swap rules
        Game.Rules.Diplomacy = new MyDiplomacyRules();    // opinions, war rules, call to arms, peace
        Game.Rules.Economy   = new MyEconomyRules();      // income, upkeep, manpower, recruitment
        Game.Rules.Characters = new MyCharacterRules();   // succession, death, births
        Game.Rules.Warfare   = new MyWarfareRules();      // movement, passability, org recovery
        Game.Ai = new MyAi(Game);                         // subclass AiSystem
        Game.Combat.Register(new MyCombat());             // then  combat_model = mycombat  in defines.txt

        Game.Commands.Register("hello", "says hello", args => "Hello!");
    }

    public override void OnMonthlyTick() { /* ... */ }
    public override void OnBattleEnded(Battle battle) { /* ... */ }
}

public class MyCombat : ICombatModel
{
    public string Id => "mycombat";
    public string Name => "My combat";
    public void OnBattleStart(Battle b, Game game) { }
    public void Tick(Battle b, Game game)          // every in-game hour
    {
        foreach (var d in b.Defenders) CombatMath.Damage(b, d, strength: 0.5, org: 1.0);
        foreach (var a in b.Attackers) CombatMath.Damage(b, a, strength: 0.4, org: 1.2);
    }
}
```

Hooks: `OnLoad, OnGameStart, OnSaveLoaded, OnHourlyTick, OnDailyTick, OnMonthlyTick, OnYearlyTick, OnEventFired,
OnWarDeclared, OnPeace, OnBattleEnded, OnLocationControllerChanged, OnStateOwnerChanged, OnCharacterDied,
OnDiplomaticAction, OnDivisionDestroyed, OnCountryEliminated`.

Variables and flags set on countries, characters, states and locations (`Vars`) are saved automatically.

---------------------------------------------------------------------------------------------------

## 9. New mechanics without C#

**Features.** `game/features.txt` lists the mechanics the game uses (`military`, `combat_hoi4`, `fog_of_war`,
`diplomacy`, `characters`, `character_interactions`, `economy`, `ai`, plus your own ids). Systems of missing features
do not run. Scripts can check them: `trigger = { has_feature = economy }`; C#: `Game.Features.Has("economy")`.
Add and remove them in Burger Studio (*Project > Features*) or with `burger features <project> add|remove <id>`.

| What | Where | You get |
|---|---|---|
| Resource (prestige, piety...) | `common/resources/*.txt` | a value (`prestige >= 20`), effects `add_prestige` / `set_prestige`, a monthly change formula, a top bar display |
| Modifier | `common/modifiers/*.txt` | `add_modifier = { name = x years = 1 }`, `remove_modifier`, `has_modifier`; with `trigger = { }` it applies itself |
| Decision | `common/decisions/*.txt` | a button in the Decisions panel (F6) with `potential`, `allow`, `effect`, `cost`, `cooldown_days`, `ai_will_do` |
| Script value | `common/script_values/*.txt` | a named formula usable anywhere a number is (`value`, `add`, `multiply`, `if`, `every_...`, `min`, `max`, `round`) |
| Window | `interface/*.txt` `panel = { }` | a button in the left menu bar (`icon = "gfx/...png"`, or its first letters) + a window on the left with `header`, `text`, `bar`, `button`, `separator`, `list` |
| Map mode | `interface/*.txt` `map_mode = { }` | locations colored by a value between `low_color` and `high_color` |
| Look of the interface | `interface/theme.txt` `theme = { }` | fonts (`font`, `bold_font`, `header_font`), `font_scale`, `ui_scale`, `ornate`, colors, `textures = { panel = "gfx/interface/ui/panel.png" ... }`: an image per element (panel, panel_header, menu_bar, top_bar, tooltip, button, button_hover, button_selected, button_disabled, close_button(_hover), bar, bar_fill, text_field(_focused), scrollbar, scroll_thumb, card, card_selected, section_line), `texture_borders = { panel = 12 }` (9-slice edges); menu icons `gfx/interface/ui/menu_<name>.png`. New projects ship editable default images (made by tools/assets/make_ui_gfx.py) |

**Theme.** Every color the interface uses has a name: `panel panel_border panel_inner_border panel_header top_bar
text text_dim title good bad button button_hover button_selected button_disabled button_border tooltip menu_bar`.
Write them as `{ r g b }` or `{ r g b alpha }`. C# scripts can change the theme while the game runs:
`Game.Interface.Theme.SetColor("panel", 20, 24, 40, 240);` (fonts and textures: change the field, then call
`Game.Interface.Theme.Changed()`). The sample's `interface/theme.txt` lists every value.

**Interface layout.** Your own country's menus (military, recruit, characters, decisions and every `panel`) are in
the left menu bar and open on the left, together with the location or country you clicked. World-wide overviews
(wars, the list of countries) open from the top right. Map modes are in the bottom right.

Modifier keys the engine reads: `income upkeep manpower tax attack defense org_recovery speed opinion relations`.
Any other key can be read in formulas as `modifier:my_key`.

**Replacing engine formulas.** A script value with one of these names replaces the engine's own formula:
`country_monthly_income`, `country_monthly_expenses`, `country_monthly_manpower`, `max_diplomatic_relations`,
`character_yearly_death_chance`, `character_yearly_birth_chance`, `division_combat_power`, `division_org_recovery`,
`location_war_value`, `base_opinion`. In Burger Studio, *Project Settings > Formulas > Replace* writes a working starting point.

**Game speed.** `game_speeds = { 0.5 1.5 4 10 0 }` in defines = in-game days per second for speeds 1-5 (0 = as fast
as the computer can go).

## 10. Defines: `common/defines.txt`

Every value has a default; the sample file lists them all with comments. The most important ones:
`start_date`, `combat_model`, `occupation_hours`, `km_per_pixel`, `general_stat`, `income_multiplier`,
`base_diplomatic_relations`, `truce_years`, `character_stats`, `birth_chance_per_year`, `fog_of_war`,
`pause_on_event`. C# scripts can read custom
defines with `Game.Data.Defines.Get("my_value", 1.0)`.

## 11. Exporting

`burger export <project> [--name MyGame]` (or option 4 in `BurgerEngine.bat`) validates the project, then builds
a self-contained, single-file `MyGame.exe` (players do not need .NET installed). It copies `game/` next to it and
adds a README for players. The EXE finds `game/` next to itself; saves and logs go into `saves/` and `logs/`.
