# BurgerEngine

A grand strategy engine in the style of Paradox's Clausewitz engine, written in C# (.NET 8).
You build a game out of **images and plain-text files**. Export turns it into a standalone **EXE**, and the
`game/` folder next to it stays editable, so your players can mod everything.

- **Pick your mechanics.** A new game starts from a map and a set of *features*: land military, a combat model (HOI4, EU, Imperator style or an empty template), fog of war, diplomacy & war, characters, character interactions, an economy, AI. Presets give you HOI4-, EU-, Imperator- or CK-style bundles, or start from a blank slate. Every feature's code and data is copied into your project, so you can change anything, and you can add or remove features at any time.
- **Map** built from a province map, heightmap, terrain map, tree map and river map, rendered in 3D with terrain textures.
- **Earth 1930.** A ready-made map of the whole world on 1 January 1930 (about 6400 locations, 1450 states, 74 countries) to start any game from, plus `projects/Earth1930`, a playable Hearts of Iron IV-style game on it.
- **Locations, states and countries.** Every province color is a named *location*, locations are grouped into *states*, and states are owned and cored by *countries* (3-letter tags like `GER`). A country's name, color and flag can change during play.
- **Hearts of Iron IV-style warfare.** Divisions and armies are led by generals (characters), fronts are drawn automatically, and armies can hold or attack. Combat is pluggable: `hoi4`, `eu4` and `imperator` are built in, or you can write your own in C#.
- **Imperator: Rome-style diplomacy and characters.** Opinion, alliances, guarantees, military access, subjects, claims, casus belli, war score and peace deals. Characters have stats, traits, families, births, deaths and succession, plus character events. All diplomatic actions and character interactions are data files.
- **A Paradox-style scripting language** (triggers, effects, scopes, events, on_actions) with tooltips and a validator.
- **C# mod scripts** compiled at runtime. They can add triggers, effects, combat models and console commands, or replace the diplomacy, economy, character and movement rules or the AI.
- **Built-in tools.** An error log (F2), a developer console (`` ` ``), a map editor (F10), save/load, and a CLI that creates, validates and exports projects.

## Quick start (Windows)

1. Install the **.NET 8 SDK or newer** (https://dotnet.microsoft.com/download).
2. Double-click **`BurgerStudio.bat`** to open **Burger Studio**, the editor (the first run builds the engine).
   Everything below can be done from it: map, countries, characters, mechanics, events, C# scripts, checking,
   playing and exporting.
   `BurgerEngine.bat` is the same in a text menu.
3. Choose **1 - Play a project** and press Enter to play the included `SampleGame`.
4. Create your own game in Burger Studio's start screen: pick a map (the sample island, a tiny test map or **Earth 1930**), a preset (Blank slate, Hearts of Iron IV style,
   Europa Universalis style, Imperator style, Crusader Kings style, Everything) and tick the features you want - or take
   a full copy of the sample game. From the command line: `burger new MyGame --preset hoi4` (`burger presets` lists them;
   add `--base earth1930` for the world map).
5. Choose **4 - Export** to get `projects/<YourGame>/export/<Name>/<Name>.exe` plus its `game/` folder. Zip that folder and share it.

All of these also work from the command line, as `burger help`, `burger run SampleGame`, `burger export MyGame` and so on.

## In-game controls

| Key | Action |
|---|---|
| WASD / arrows / middle mouse drag | move the camera |
| mouse wheel, Q/E | zoom |
| Space, 1-5 | pause, game speed |
| Left click | select a location (or click your unit counters to select divisions; Shift adds) |
| Right click | move/attack with the selected divisions or army, or open a country's diplomacy |
| Tab | cycle map modes (political, terrain, states, diplomacy, supply, air, locations) |
| F1 / R / F3 / F4 | military / recruit / characters / wars |
| P / N | production / navy (with those features) |
| F2 | error log (every script problem with file and line) |
| F5 | quick save |
| F10 | map editor (move locations between states, set owners and cores, save state files) |
| `` ` `` | developer console (`help`, `tag GER`, `effect add_gold = 100`, `event my.1`, `war FRA`, `combat eu4`, ...) |

## Folder layout

```
BurgerEngine/
  BurgerEngine.bat        menu: play / new / validate / export
  burger.bat              command line tool
  src/Burger.Core/        simulation, scripting, map processing, warfare, diplomacy, characters, saves
  src/Burger.Game/        renderer and user interface (raylib)
  src/Burger.Tools/       CLI (new, validate, run, export, generate-locations, docs)
  projects/SampleGame/    the sample game
  docs/MODDING.md         how to make a game (file formats, scripting, C#)
  docs/SCRIPT_REFERENCE.md   every trigger / effect / scope (generated by `burger docs`)
```

A project looks like this (the same layout ships with the exported EXE):

```
game/
  game.txt                      name, version, default country
  common/defines.txt            rules (combat_model, occupation, diplomacy, characters...)
  common/countries/*.txt        TAG = { name color flag capital ruler ... }
  common/terrain.txt            terrain types (map color, texture, movement, combat)
  common/units/*.txt            unit types and division templates
  common/traits.txt             character traits
  common/casus_belli.txt, subject_types.txt
  common/diplomatic_actions/    data-driven diplomacy
  common/character_interactions/
  common/on_actions/            hooks (game start, monthly pulse, war declared, new ruler, ...)
  map/default.map               which images to use and map settings
  map/provinces.png heightmap.png terrain.png trees.png rivers.png
  map/locations.txt             HEX = location_name
  map/states/*.txt              states: locations, owner, cores, claims
  history/characters/  history/units/  history/diplomacy.txt
  events/*.txt                  country_event / character_event
  localisation/*.txt            key = "text"
  gfx/flags/TAG.png  gfx/terrain/*.png
  scripts/*.cs                  C# mod scripts
```

## Burger Studio (the editor)

Burger Studio is built into the engine (Dear ImGui) and laid out like Unity / Unreal. Every panel can be dragged,
docked, tabbed or closed; *View > Reset layout* restores the default.

| Panel | What it does |
|---|---|
| **Viewport** | The live 3D map. Left click selects a location (and its state), middle-drag / WASD pans, wheel zooms. Map modes, including your own. **Paint state**: drag over locations to move them into the selected state. |
| **Inspector** | Properties of the selection, with what matters in context (a country's states and characters, a state's locations and owner): a form for the common keys (color picker, drop-downs for tags/locations/characters, image fields) plus the full **Script** of the entry. Locations and states: owner, cores, claims, tax, manpower, display names, new/delete state. Drop a PNG from Windows on it to set a flag or portrait. |
| **Content Browser** | Two roots: **Game** (shipped next to the EXE, players can mod it) and **Backend** (your locked C#, see below). Thumbnails and names like Unity, search across all files. **+ Create** or right-click: Resource, Modifier, Decision, Script value, Window, Map mode, On action, Country, Character, Terrain, Unit type, Division template, Event file, Diplomatic action, Character interaction, Casus belli, Subject type, Trait, C# script (from templates), Folder, Text file. New entries are **added to an existing file** of that kind (the main file is preselected; if the folder has several files you pick one). Events are added by right-clicking an event file > *New country/character event in this file* (any content file has a matching *New ... in this file* item). Rename (F2), duplicate, delete (to the Recycle Bin), drag files in from Windows to import them. |
| **Code** | Tabs for script and C# files with line numbers, syntax check, C# compile check, templates and a list of every script word. |
| **Console** | *Problems* from **Check (F7)** - click one to jump to the line; the engine *Log*; *Output* of Play/Export. |
| **Project Settings** | game.txt, combat model, game speeds, every game rule (defines) with descriptions, engine formulas you can replace, and all display texts. |

An **Outliner** (searchable tree of every object) can be opened from the View menu.

Edits change only the keys you touch, so comments and formatting in your files are kept. Nothing is written until you
save (Ctrl+S); Play (F5), Check (F7) and Export save first.

### Features

Features are the mechanics of a game. **Project > Features** lists them with *Add* / *Remove*; a project's list is in
`game/features.txt` (the engine only runs the systems of installed features, so a blank slate has no armies, wars,
characters or money until you add them).

| Feature | Adds |
|---|---|
| `military` | unit types, division templates, armies, movement, fronts, battles, occupation, recruit menu (`backend/Warfare.cs`, `common/units/`, starting armies for every country) |
| `combat_hoi4`, `combat_eu4`, `combat_imperator`, `combat_custom` | a combat model each (`backend/Combat_*.cs`); the active one is `combat_model` in defines (*Use* button). `combat_custom` is a short commented template to write your own |
| `production` | HOI4 factories, production lines with efficiency, equipment stockpile, construction queue; divisions need equipment (`backend/Production.cs`, `common/equipment/`, `common/buildings/`) |
| `supply_range` / `supply_hubs` | supply that spreads from your land (range: terrain and encirclement matter) or from the capital and supply hubs linked by railways (hubs); unsupplied divisions lose strength (`backend/Supply.cs`, Supply map mode). Pick one |
| `division_designer` | players design division templates in the game (Designer menu) |
| `navy` | ship types, fleets, naval battles (screens protect capital ships), repairs in naval bases, transports that carry divisions over sea, naval invasions (`backend/Navy.cs`, `common/ships/`, starting fleets) |
| `air` | air regions (`map/air_regions.txt`), air wings at air bases flying air superiority, close air support or strategic bombing (`backend/Air.cs`, `common/planes/`, Air map mode) |
| `battle_plans` | draw an army's front segment, an offensive arrow and a fallback line (Military menu) |
| `lend_lease` | send volunteer divisions and equipment (once or monthly) to countries at war (diplomatic actions; `backend/Support.cs`) |
| `politics` | ideologies with a popularity in every country, a ruling party, elections with parliament seats and coalitions, ideology opinions, political power and stability, Politics menu (O) and Ideology map mode (`common/ideologies/`, `common/governments/`, `history/politics/`, `backend/Politics.cs`) |
| `laws` | law groups (conscription, economy, press, political parties) that cost political power and give modifiers; laws can ban parties or suspend elections (`common/laws/`) |
| `cabinet` | ministers from the governing parties in cabinet posts, bonuses from their stats (`common/cabinet/`; needs `characters`) |
| `civil_wars` | political movements with demands that revolt unless you give in or suppress them, and HOI4-style civil wars that split the country |
| `population` | pops of a culture in every state that grow, assimilate and migrate and scale tax and manpower; accepted cultures; People menu (U), People and Culture map modes (`common/cultures/`, `history/pops/`, `backend/Population.cs`) |
| `religion` | religions in families, state religion, conversion, religious opinion and unrest, Religion map mode (`common/religions/`) |
| `social_classes` | Imperator-style classes (nobles, clergy, burghers, peasants, serfs) with promotion, for older eras (`common/pop_types/`) |
| `cores_claims` | cores where at least half the people are of accepted cultures (at once for your own culture, else after 25 years), cores lost after 50 years, cultural claims every year, a religious claim action; non-core states give 60% |
| `colonization` | EU-style colonists that settle unowned land near your territory |
| `independence` | independence movements of unaccepted cultures (calmer in colonies than in conquered land) that break away and fight wars of independence unless suppressed, released or accepted |
| `buildings_classic` / `buildings_modern` | farms, aqueducts, markets, temples, barracks / hospitals, housing, schools, radio and police stations that change growth, room, tax, assimilation and stability |

The **Ledger** (top right, F9) charts population, income, manpower, divisions, factories, war dead, war exhaustion,
stability, colonies, coups and more over time for the biggest countries and yours, or for the whole world; C# mods can add
their own metrics with `Game.Statistics.Add(...)`.
| `fog_of_war` | hidden enemy armies and greyed-out land you cannot see (`backend/Vision.cs`) |
| `diplomacy` | EU-style casus belli with war goals (cores, claims, culture, religion, liberation, ideology, independence...), faction war goals, ticking war score, war exhaustion, EU-style peace offers (land, subjects, independence, releasing peoples, regime change, humiliation), AI peace, liberty desire of subjects, non-aggression pacts, diplomatic plays, opinion, alliances, access, guarantees, diplomatic actions |
| `characters` | rulers, heirs, families, births, deaths, succession, traits (rulers are created for every country) |
| `character_interactions` | appoint heir, make general, gifts, marriages, execution |
| `economy` | tax income, upkeep, manpower; training costs gold and manpower (without it training is free) |
| `ai` | computer-controlled countries (each part only for installed features) |

Adding a feature copies its files into the project - from then on they are your files. Removing one deletes its
files you did not change and moves changed ones to `removed_features/<id>/`. Your own ids work too (*Add custom*):
they add no files, and your scripts check them with `has_feature = my_feature` or `Game.Features.Has("my_feature")`.
Command line: `burger features MyGame`, `burger features MyGame add fog_of_war`, `burger features MyGame remove ai`.

### Game code vs backend code

| Folder | Players can change it? | Use it for |
|---|---|---|
| `game/scripts/*.cs` | yes - shipped as files, compiled when the game starts | mod-friendly additions |
| `backend/*.cs` (next to `game/`) | **no** - `burger export` compiles it into the EXE and does not ship the files | your game's real rules: combat, warfare, diplomacy, economy, AI, new mechanics |

Both use the same API (classes deriving from `GameScript`) and both are compiled when the game starts while you develop,
so there is no rebuild step. Features put copies of the engine's rules in the backend (`Warfare.cs`, `Combat_HOI4.cs`, `Vision.cs`, `Diplomacy.cs`, `Characters.cs`, `Economy.cs`, `AI.cs`...). Edit them freely; delete one and the engine's built-in version is used again. Scripts can use classes from the backend. The engine itself (map loading, rendering, the
script language) is not part of either and is not meant to be edited.

## Changing the engine itself

Open `BurgerEngine.sln` in Visual Studio, Rider or VS Code. Most game logic can be changed from a project's
`scripts/` folder without touching the engine (see *C# scripting* in `docs/MODDING.md`). If you do change engine
code, run **7 - Rebuild** in `BurgerEngine.bat`.

## The Earth 1930 map

`assets/bases/earth1930` is the whole world (Mercator, 78N to 58S, 6144x3456 pixels) on 1 January 1930:
5300 land locations named after their largest towns (1930 names such as Danzig, Stalingrad or Batavia), 950 sea
zones, 158 lakes, 1450 states and 74 countries with flags, capitals, populations (state manpower and tax),
straits, rivers, terrain (plains, forest, jungle, hills, mountains, desert, marsh, tundra, glacier) and air regions.
Colonies belong to their empires; dominions and the British Raj are subjects of the United Kingdom (with the
diplomacy feature). It is made from public-domain Natural Earth data and free elevation tiles by
`tools/earth/make_earth.py`; the 1930 world itself (who owned what, capitals, colors, flags) is in
`tools/earth/earth1930_data.py`, so another start date is a matter of editing that file and running the generator
(see `tools/earth/README.txt`). Sources are listed in `game/map/SOURCES.txt`.

