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
**Fallback** (hold the fallback line). Plans are saved with the army.

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

## 5. Diplomacy (Imperator style)

- **Opinion**: modifiers that decay (`add_opinion`, `reverse_add_opinion`) plus a base from the relationship.
- **Relations**: alliances, guarantees, military access, subjects (`common/subject_types.txt`), truces, claims.
  The number of alliances, guarantees and subjects is limited (`base_diplomatic_relations` + ruler charisma).
- **Wars**: declared with a casus belli (`common/casus_belli.txt`); defenders' allies and guarantors are called in,
  and allies can refuse (which breaks the alliance). **War score** comes from occupation and battles.
- **Peace**: demand states, subjugation, annexation or gold. Each costs war score. The AI accepts if your war
  score covers the cost. The AI also sends offers, which you accept or decline in a popup.

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
| Look of the interface | `interface/theme.txt` `theme = { }` | fonts (`font`, `bold_font`, `header_font`), `font_scale`, `ui_scale`, `ornate`, colors, optional 9-slice `panel_texture` / `button_texture` / `top_bar_texture` |

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
