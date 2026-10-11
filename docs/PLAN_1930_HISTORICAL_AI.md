# Plan: a historical 1930 and distinct country AI

Goal: the 1930 scenario plays out like a believable 1930s (rise and fall of regimes, rivalries, blocs, crises), and each
country's AI acts from its own situation, interests, ideology and fears instead of one shared formula. Nothing is
railroaded: history is the likely path, not the only one.

## Where we are (measured, not guessed)

- **Start state is generic.** `history/diplomacy.txt` has 15 lines (9 subjects, 5 alliances). There is no
  `history/politics` file: every country's ruling party and support come from its *government word* (all kingdoms start
  55% non-aligned, all republics 55% democratic). There are no characters (0 living at any date), three placeholder
  events, no decisions, no truces, guarantees, pacts or grievances, and cores/claims come from the generator, not history.
- **One AI for everyone.** `GameAi` (backend/AI.cs) + the `ai_will_do` weights in `common/diplomatic_actions` are the same
  for the UK and Liberia. The only per-country input is strength, borders, claims and ideology.
- **Symptoms in 60-year sims** (4 seeds, `burger validate Earth1930 --days 21900 --seeds ...`): minors beat majors
  in the 1930s (Cuba humiliates the USA, Danzig humiliates Germany), the Raj frees itself within months of the start,
  Germany/Italy/Japan are not different from Portugal, blocs form by opinion not by geopolitics, and the world drifts
  to ~80-100 countries because releases and revolts are frequent.
- **Tools we already have**: multi-seed headless sims, per-decade rulers/stability print, `BURGER_PEACE_LOG`, backend rule classes
  (politics, diplomacy, population, AI), decisions (`common/decisions`), events with on_actions, subject types, the
  `country_names` and `SeparatistRegion` systems from this round.

## Phase 0 - a historical scorecard (first, so everything after it can be judged)

1. `tools/earth/scorecard.txt`: expectations as data, each with a window and a *probability band* over seeds, e.g.
   `war GER POL 1938-1942 >= 40%`, `ruling GER fascist by 1936 >= 60%`, `ruling SOV communist 1950 >= 90%`,
   `no_conquest_by minor_of major 1930-1945`, `country_count 1950 70-85`, `alliance FRA POL 1939 >= 80%`.
2. `burger validate <project> --scorecard` runs N seeds in parallel (`--seeds` exists), prints pass/fail per line with the
   hit rate, and a table of wars, regime changes and territorial changes per decade.
3. Acceptance: the scorecard runs in under 5 minutes for 8 seeds x 20 years (the monthly AI tick, 160 ms hitches and
   `Path.Found` are the cost - spread the AI across days of the month as part of this).

## Phase 1 - a historical start (pure data, no engine work)

| Deliverable | File | Content |
|---|---|---|
| Politics | `history/politics/countries.txt` | ruling party, popularity of the four ideologies, laws, party names, seats, last election, for all 74 countries (Weimar at 40/18/13/29, Italy fascist, USSR communist, Japan, etc.) |
| Diplomacy | `history/diplomacy.txt` | full alliances (Little Entente, Franco-Polish, Balkan entente), guarantees, non-aggression pacts (Poland-USSR 1932, Germany-Poland later), military access, protectorates (Egypt, Iraq, Manchukuo's future), truces, opinion modifiers for grievances (Versailles, Trianon, Fiume, Alsace) |
| Cores and claims | `map/states` + generator | real irredenta: Hungary on Trianon land, Germany on Danzig/Corridor/Sudeten/Memel/Austria, Italy on Dalmatia/Fiume, Bulgaria on Macedonia, Turkey on Hatay, USSR on Bessarabia and the Baltics, China on Manchuria/Outer Mongolia |
| Characters | `history/characters` | rulers, heirs and key ministers by tag (the cabinet feature is on but empty: 0 characters) |
| Armed forces | `history/units|fleets|air` | check against 1930 orders of battle (the sim shows 413 divisions at the start, the Reichswehr limit, naval treaties) |
| Economy | country definitions | gold, manpower, factories by 1930 weight; Raj liberty: start with a garrison/colonial modifier so it does not leave in 1930 |
| Subjects | `subject_types.txt` | dominion, protectorate, colony, mandate, puppet - each with its own liberty desire, tribute, call-to-arms rule |

Acceptance: a 1930 start where no country changes ruling party, borders or alliances in the first 12 months *except*
through scripted/AI events; `burger validate` reports the start as an audit (country by country).

## Phase 2 - historical pressure as content (events and decisions)

Not a fixed timeline: **conditions, weights and decisions** that the AI and the player can use.

1. **Global shocks** (events with `mean_time_to_happen` and triggers): Great Depression 1929-33 (wealth index down by
   trade exposure, which feeds the new stability sources), recovery by policy, arms race, collapse of collective security.
2. **Regional chains**, each a small decision/event tree gated by state, not by date alone: Germany (Weimar crisis ->
   fascist rise needs low stability + low government support, which we now model; then Rhineland, Anschluss, Sudeten,
   Danzig as decisions with AI weights), Japan (Manchuria, China incident), Italy (Ethiopia), USSR (plans, purges, pact),
   Spain (civil war from a political crisis), China (warlords, Japan, the communists), UK/France (appeasement vs
   resolve as a country-level flag), Balkans/Danube, the Middle East mandates, Latin America (Chaco, border wars).
3. Each chain has an **"historical bias"** define (`historical_focus = strict | guided | free`): *strict* triggers the
   chain on its dates; *guided* only boosts AI weights; *free* leaves it to the generic AI. Default: guided.
4. Acceptance: in the scorecard, guided history happens at a stated rate and a player or AI diverging changes the follow-ups.

## Phase 3 - a distinct AI per country

### 3.1 Data: AI profiles
`common/ai_profiles/*.txt`, one per archetype plus per-country overrides:

```
GER = {
    archetype = revisionist
    rivals = { FRA POL CZE }            friends = { ITA }       irredenta = { danzig_state ... }
    ambitions = { unify_germans lebensraum_east rearm }        # named goals (3.2)
    risk = 0.8   patience_years = 4   exhaustion_tolerance = 14
    prefers = { land = 0.6 air = 0.25 navy = 0.1 }             # production and recruitment mix
    peace = { style = harsh  annex_preference = 0.7  puppet_preference = 0.2 }
    phases = { 1930 = rebuilding 1934 = rearming 1936 = expanding }   # re-evaluated by flags/dates
}
```
Archetypes: revisionist great power, status-quo great power, imperial maintainer, regional hegemon, buffer minor,
isolationist, client/dominion, warlord state, revolutionary regime. Profiles are data, so a game can define its own.

### 3.2 Engine: goals instead of weights
- `AiGoal` (new, in the AI feature): `{ key, owner, target, kind (reclaim, unify, secure, contain, expand, colonise, defend), priority, state }`.
  Monthly, each country *selects* goals from its profile + situation (rule `AiGoals(game, country)`), keeps at most
  3-4, and every other AI action (declare war, alliance, guarantee, production, recruitment, peace terms,
  colonisation) asks the goal list first (`rule GoalWeight(action, target)`), falling back to today's weights.
- Backend rules, as agreed: `GameAiRules` (new file `backend/AIGoals.cs`) holds the formulas; the engine only runs the loop.
- **Threat model**: per country, per neighbour/rival: strength ratio (land, air, navy), allies, distance, claims, ideology
  opinion -> a threat score used for alliance seeking, guarantees and rearmament.
- **War decision**: expected strength with allies vs the target with its allies, war readiness (mobilisation period,
  exhaustion, 2-front rule), profile risk. Fixes the "minor beats major" symptom at the source.
- **Alliance blocs**: ideological and security blocs (Entente, Comintern-style, Axis-like) from threat and ideology;
  guarantees to threatened neighbours by status-quo powers; appeasement as a profile trait with a trigger threshold.
- **Peace style** per profile (extends `BuildDemands`: harsh/lenient, annex vs puppet vs regime change, who gets the colonies).
- **Per-country templates and doctrine**: division designer templates by profile (infantry-heavy, armoured, mountain),
  naval/air weights, lend-lease behaviour.

### 3.3 Tooling for tuning
- `burger ai-report <project> <TAG> [--date]`: goals, threat table, why each war/alliance was or was not chosen.
- Scorecard lines per profile: "GER declares war on a neighbour 1936-1941 in >= 60% of seeds", "POR never declares a war on a
  great power", "ISO profile countries never join an offensive war".

## Phase 4 - balance and regression

1. Run the scorecard on 8 seeds after every phase; keep a `docs/SCORECARD_HISTORY.txt` of hit rates.
2. Plausibility bands for the world in 1950 (country count, share of democracies 30-60%, civil wars per decade,
   no country over 20% of the land by 1945 unless historical).
3. Performance: AI monthly tick spread over the days of the month; profile `Ai.*` per country; budget 1 ms/day for 74 countries.

## Order and sizes

| Step | Size | Depends on |
|---|---|---|
| Phase 0 scorecard + AI tick spreading | small | - |
| Phase 1 data (politics, diplomacy, claims, characters, forces) | large, mostly data | 0 |
| Phase 2 shocks and 6-8 regional chains | large, mostly script | 1 |
| Phase 3.1-3.2 profiles + goals + threat/war decision | medium engine + data | 1 (profiles need the facts) |
| Phase 3.3 `ai-report` and per-profile scorecards | small | 3.2 |
| Phase 4 balance passes | continuous | all |

Order of value: 0, 1, 3.2 (war decision and threat fixes the loudest symptoms), 2, rest of 3, 4.

## Decisions I need from you before Phase 1

1. **Railroad dial**: default `historical_focus = guided` (history is likely, not certain)? Or strict by default?
2. **Scope of 1930**: all 74 countries to the same depth, or deep for ~20 (great powers, Danube, Far East, Iberia) and
   archetype-level for the rest?
3. **Real names and people**: use real rulers/parties (Hitler, Stalin), or fictional placeholders like generic party names?
4. **Time horizon**: tune for 1930-1950 only, or keep the century-long sandbox working at the same quality?
