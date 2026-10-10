using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Studio;

public enum FieldKind { Word, Text, Number, Bool, Color, Country, Location, Character, List, Date, Choice, Image }

/// <summary>One editable key of a block, shown as a form field.</summary>
public sealed class FieldSpec
{
    public string Key = "";
    public string Label = "";
    public FieldKind Kind;
    public string? Help;
    /// <summary>Options for Choice and List fields.</summary>
    public Func<ProjectIndex, IEnumerable<string>>? Options;
    /// <summary>For Image fields: folder new images are copied to.</summary>
    public string ImageFolder = "gfx";
    /// <summary>For Image fields: path shown when the key is missing ({id} = block id).</summary>
    public string? DefaultImage;

    public FieldSpec(string key, string label, FieldKind kind, string? help = null)
    {
        Key = key; Label = label; Kind = kind; Help = help;
    }
}

/// <summary>Describes a kind of block (countries, characters, decisions...) for <see cref="BlockListPage"/>.</summary>
public sealed class BlockSpec
{
    public string Title = "";
    /// <summary>File or folder (without .txt) like the engine reads it: common/traits → common/traits.txt and common/traits/*.txt.</summary>
    public string Source = "";
    /// <summary>Blocks are inside this block (e.g. unit_types = { ... }).</summary>
    public string? Container;
    /// <summary>If set, the id is the value of this key inside the block (events: id = my_mod.1) and the statement key is the type.</summary>
    public string? IdKey;
    /// <summary>Statement keys that belong to this list (null = every block).</summary>
    public string[]? StatementKeys;
    public string Help = "";
    public List<FieldSpec> Fields = new();
    /// <summary>Extra fields computed from the project (e.g. character stats from defines).</summary>
    public Func<ProjectIndex, IEnumerable<FieldSpec>>? ExtraFields;
    /// <summary>Script text of a new block. {id} is replaced with the id.</summary>
    public string Template = "{id} = {\n}\n";
    /// <summary>For IdKey lists: the statement types to choose from when creating (first is the default).</summary>
    public string[]? NewTypes;
    /// <summary>File new blocks go to when the project has none yet.</summary>
    public string DefaultFile = "";
    /// <summary>Returns an error for an invalid new id, or null.</summary>
    public Func<string, string?>? ValidateId;
    /// <summary>Field whose value is shown as the display name in the list.</summary>
    public string NameKey = "name";
    /// <summary>The script tab is shown first (for blocks that are mostly script).</summary>
    public bool ScriptFirst;
}

/// <summary>The block lists of the editor.</summary>
public static class Specs
{
    private static FieldSpec F(string key, string label, FieldKind kind, string? help = null) => new(key, label, kind, help);

    private static string? IdRule(string s)
    {
        if (s.Length == 0) return "Enter an id";
        if (s.Any(c => char.IsWhiteSpace(c) || "{}=<>#\"!?".Contains(c))) return "No spaces or { } = < > # \" in ids";
        return null;
    }

    public static readonly BlockSpec Countries = new()
    {
        Title = "Countries",
        Source = "common/countries",
        DefaultFile = "common/countries/countries.txt",
        Help = "Countries are identified by a three letter tag (GER, FRA, A01). Give a new country land on the Map page (set a state's owner). Name, color and flag can also change during the game.",
        ValidateId = s => Country.IsValidTag(s) ? null : "A tag is exactly 3 capital letters or digits, e.g. GER",
        Fields =
        {
            F("name", "Name", FieldKind.Text, "Display name (or a localisation key)"),
            F("color", "Map color", FieldKind.Color),
            new FieldSpec("flag", "Flag", FieldKind.Image, "Image shown as the flag. Choose a PNG/JPG; it is copied into gfx/flags.") { ImageFolder = "gfx/flags", DefaultImage = "gfx/flags/{id}.png" },
            F("capital", "Capital", FieldKind.Location, "Location key"),
            F("government", "Government", FieldKind.Word, "Any word; scripts can check it with government = monarchy"),
            F("playable", "Playable", FieldKind.Bool, "Can the player choose this country?"),
            F("gold", "Starting gold", FieldKind.Number),
            F("manpower", "Starting manpower", FieldKind.Number),
            F("ruler", "Ruler", FieldKind.Character, "Character id from Characters (empty = a random ruler is created)"),
            F("heir", "Heir", FieldKind.Character),
        },
        Template = "{id} = {\n\tname = \"New Country\"\n\tcolor = { 150 150 150 }\n\tflag = \"gfx/flags/{id}.png\"\n\tgovernment = monarchy\n}\n",
    };

    public static readonly BlockSpec Characters = new()
    {
        Title = "Characters",
        Source = "history/characters",
        DefaultFile = "history/characters/characters.txt",
        NameKey = "first_name",
        Help = "Characters that exist at the start. Refer to them in scripts as character:<id>. Rulers and heirs are set here (ruler = yes) or on the country.",
        ValidateId = IdRule,
        Fields =
        {
            F("first_name", "First name", FieldKind.Text),
            F("family", "Family", FieldKind.Text),
            F("female", "Female", FieldKind.Bool),
            F("birth_date", "Birth date", FieldKind.Date, "year.month.day, e.g. 1801.6.8"),
            F("age", "Age (instead of birth date)", FieldKind.Number),
            F("country", "Country", FieldKind.Country),
            F("ruler", "Ruler of the country", FieldKind.Bool),
            F("heir", "Heir of the country", FieldKind.Bool),
            F("general", "Is a general", FieldKind.Bool),
            F("spouse", "Spouse", FieldKind.Character),
            F("father", "Father", FieldKind.Character),
            F("mother", "Mother", FieldKind.Character),
            new FieldSpec("traits", "Traits", FieldKind.List) { Options = i => i.Traits },
            F("loyalty", "Loyalty", FieldKind.Number),
            F("health", "Health", FieldKind.Number),
            new FieldSpec("portrait", "Portrait", FieldKind.Image) { ImageFolder = "gfx/portraits" },
            F("dead", "Dead at start", FieldKind.Bool),
        },
        ExtraFields = i => i.CharacterStats.Select(s => new FieldSpec(s, Burger.Core.Localisation.Prettify(s) + " (stat)", FieldKind.Number)),
        Template = "{id} = {\n\tfirst_name = Name\n\tfamily = \"Family\"\n\tage = 30\n\t# country = TAG        # the country the character belongs to\n}\n",
    };

    public static readonly BlockSpec Terrain = new()
    {
        Title = "Terrain",
        Source = "common/terrain",
        DefaultFile = "common/terrain.txt",
        Help = "Terrain types are matched by color against map/terrain.png. The texture is tiled over the 3D map.",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            F("color", "Color in terrain.png", FieldKind.Color),
            F("display_color", "Map mode color", FieldKind.Color),
            new FieldSpec("texture", "Texture", FieldKind.Image) { ImageFolder = "gfx/terrain" },
            F("movement_cost", "Movement cost", FieldKind.Number, "1 = normal"),
            F("combat_width", "Combat width", FieldKind.Number),
            F("attack_modifier", "Attack modifier", FieldKind.Number, "-0.2 = attackers deal 20% less damage"),
            F("defense_modifier", "Defense modifier", FieldKind.Number),
            F("is_water", "Water", FieldKind.Bool),
            F("impassable", "Impassable", FieldKind.Bool),
        },
        Template = "{id} = {\n\tcolor = { 128 128 128 }\n\tmovement_cost = 1\n\tcombat_width = 80\n}\n",
    };

    public static readonly BlockSpec UnitTypes = new()
    {
        Title = "Unit types",
        Source = "common/units",
        Container = "unit_types",
        DefaultFile = "common/units/units.txt",
        Help = "Battalion types. Divisions are built from these (see Division templates). Combat models read the stats; any extra number you add can be read by a custom combat model with d.Stat(\"key\").",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            new FieldSpec("category", "Category", FieldKind.Choice) { Options = _ => new[] { "infantry", "cavalry", "artillery", "armor", "support" } },
            F("cost", "Cost (gold)", FieldKind.Number),
            F("manpower", "Manpower", FieldKind.Number),
            F("build_days", "Build days", FieldKind.Number),
            F("max_strength", "Max strength", FieldKind.Number),
            F("max_organisation", "Max organisation", FieldKind.Number),
            F("soft_attack", "Soft attack", FieldKind.Number),
            F("hard_attack", "Hard attack", FieldKind.Number),
            F("defense", "Defense", FieldKind.Number),
            F("breakthrough", "Breakthrough", FieldKind.Number),
            F("combat_width", "Combat width", FieldKind.Number),
            F("speed", "Speed (km/h)", FieldKind.Number),
        },
        Template = "{id} = { cost = 8 manpower = 1000 build_days = 30 max_strength = 25 max_organisation = 60 soft_attack = 6 hard_attack = 1 defense = 22 breakthrough = 3 combat_width = 2 speed = 4 }\n",
    };

    public static readonly BlockSpec DivisionTemplates = new()
    {
        Title = "Division templates",
        Source = "common/units",
        Container = "division_templates",
        DefaultFile = "common/units/units.txt",
        Help = "What countries can recruit: a name and the battalions it contains, e.g. units = { infantry = 6 artillery = 1 }. owner = TAG limits a template to one country.",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            F("owner", "Only for country", FieldKind.Country),
        },
        Template = "{id} = {\n\tname = \"New Division\"\n\tunits = { infantry = 6 }\n}\n",
    };

    public static readonly BlockSpec Equipment = new()
    {
        Title = "Equipment",
        Source = "common/equipment",
        DefaultFile = "common/equipment/00_equipment.txt",
        Help = "Equipment made by military factories (or dockyards for naval equipment) and used by divisions (production feature). Unit types list what they need with equipment = { ... }, or use the defaults below for their category.",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            F("cost", "Production cost per unit", FieldKind.Number),
            new FieldSpec("category", "Category", FieldKind.Choice) { Options = _ => new[] { "army", "naval", "air" } },
            F("default_amount", "Default amount per unit", FieldKind.Number, "Used by unit types of the default_for categories that list no equipment"),
        },
        Template = "{id} = {\n\tname = \"New Equipment\"\n\tcost = 1\n\tcategory = army\n\tdefault_for = { }      # unit categories that need it by default, e.g. { infantry }\n\tdefault_amount = 100\n}\n",
    };

    public static readonly BlockSpec ShipTypes = new()
    {
        Title = "Ship Types",
        Source = "common/ships",
        DefaultFile = "common/ships/00_ships.txt",
        Container = "ship_types",
        Help = "Ship classes (navy feature). Screens shield capital ships in battle; ships with 'transport' carry divisions over the sea. With the production feature dockyards build them (production_cost), otherwise they are bought with gold and manpower.",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            new FieldSpec("category", "Category", FieldKind.Choice) { Options = _ => new[] { "screen", "capital", "transport" } },
            F("hp", "Hit points", FieldKind.Number),
            F("speed", "Speed (km/h)", FieldKind.Number),
            F("light_attack", "Light attack", FieldKind.Number, "Damage against screens and transports"),
            F("heavy_attack", "Heavy attack", FieldKind.Number, "Damage against capital ships"),
            F("armor", "Armor", FieldKind.Number),
            F("transport", "Divisions carried", FieldKind.Number),
            F("cost", "Gold cost", FieldKind.Number),
            F("production_cost", "Production cost (dockyards)", FieldKind.Number),
            F("manpower", "Crew (manpower)", FieldKind.Number),
            F("build_days", "Days to build (gold)", FieldKind.Number),
        },
        Template = "{id} = {\n\tname = \"New Ship\"\n\tcategory = screen\n\thp = 20\n\tspeed = 30\n\tlight_attack = 4\n\tarmor = 1\n\tcost = 40\n\tproduction_cost = 1200\n\tmanpower = 200\n\tbuild_days = 120\n}\n",
    };

    public static readonly BlockSpec PlaneTypes = new()
    {
        Title = "Plane Types",
        Source = "common/planes",
        DefaultFile = "common/planes/00_planes.txt",
        Container = "plane_types",
        Help = "Plane types (air feature). Air attack flies air superiority, ground attack close air support, strategic attack bombing. A wing only flies over air regions within range (km) of its air base.",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            new FieldSpec("category", "Category", FieldKind.Choice) { Options = _ => new[] { "fighter", "cas", "bomber" } },
            F("range", "Range (km)", FieldKind.Number),
            F("air_attack", "Air attack", FieldKind.Number, "Needed for air superiority"),
            F("air_defense", "Air defense", FieldKind.Number),
            F("agility", "Agility", FieldKind.Number),
            F("ground_attack", "Ground attack", FieldKind.Number, "Needed for close air support"),
            F("strategic_attack", "Strategic attack", FieldKind.Number, "Needed for strategic bombing"),
            F("cost", "Gold cost (wing)", FieldKind.Number),
            F("production_cost", "Production cost (one plane)", FieldKind.Number),
            F("manpower", "Crew (manpower)", FieldKind.Number),
            F("build_days", "Days to build (gold)", FieldKind.Number),
        },
        Template = "{id} = {\n\tname = \"New Plane\"\n\tcategory = fighter\n\trange = 700\n\tair_attack = 10\n\tair_defense = 10\n\tagility = 30\n\tcost = 60\n\tproduction_cost = 22\n\tmanpower = 100\n\tbuild_days = 90\n}\n",
    };

    public static readonly BlockSpec Buildings = new()
    {
        Title = "Buildings",
        Source = "common/buildings",
        DefaultFile = "common/buildings/00_buildings.txt",
        Help = "State buildings: factories, dockyards, infrastructure, railways, supply hubs, air bases... Levels are stored per state (buildings = { military_factory = 2 }). With the production feature, civilian factories construct them.",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            F("desc", "Description", FieldKind.Text),
            F("cost", "Construction cost", FieldKind.Number),
            F("max_level", "Maximum level", FieldKind.Number),
            F("category", "Category", FieldKind.Word, "factory, infrastructure, military..."),
            F("shares_slots", "Uses the state's building slots", FieldKind.Bool),
        },
        Template = "{id} = {\n\tname = \"New Building\"\n\tdesc = \"\"\n\tcost = 5000\n\tmax_level = 5\n\tcategory = other\n}\n",
    };

    public static readonly BlockSpec Resources = new()
    {
        Title = "Resources",
        Source = "common/resources",
        DefaultFile = "common/resources/00_resources.txt",
        Help = "A new number for every country (or character): prestige, stability, piety... Each one automatically gets a value for triggers (prestige >= 20), effects add_<id> / set_<id>, a top bar display and the monthly change formula.",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            F("desc", "Description", FieldKind.Text),
            F("start", "Start value", FieldKind.Number),
            F("min", "Minimum", FieldKind.Number, "Empty = no minimum"),
            F("max", "Maximum", FieldKind.Number, "Empty = no maximum"),
            F("top_bar", "Show in top bar", FieldKind.Bool),
            F("color", "Color", FieldKind.Color),
            F("decimals", "Decimals shown", FieldKind.Number),
            new FieldSpec("scope", "Belongs to", FieldKind.Choice) { Options = _ => new[] { "country", "character" } },
        },
        Template = "{id} = {\n\tname = \"New Resource\"\n\tdesc = \"\"\n\tstart = 0\n\tmin = 0\n\tmax = 100\n\tmonthly = {\n\t\tvalue = 0.1          # change per month (a script value: can use if/add/multiply...)\n\t}\n\tcolor = { 200 200 120 }\n}\n",
    };

    public static readonly BlockSpec Ideologies = new()
    {
        Title = "Ideologies",
        Source = "common/ideologies",
        DefaultFile = "common/ideologies/00_ideologies.txt",
        Help = "Political ideologies (politics feature). Every country has a popularity for each; the ruling party has one. Scripts: ruling_ideology = <id>, popularity_<id> >= 0.3, add_popularity = { ideology = <id> value = 0.05 }.",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            F("adjective", "Adjective", FieldKind.Text, "Used for rebel countries (\"Nationalist Spain\")"),
            F("party_name", "Party name", FieldKind.Text, "Default name of its party in every country"),
            F("color", "Color", FieldKind.Color),
            F("elections", "Holds elections", FieldKind.Bool, "Countries ruled by it hold elections"),
            F("extremist", "Extremist", FieldKind.Bool, "Gains support when stability is low or wars are lost"),
            F("coalition", "Coalition partners", FieldKind.List, "Ideologies it governs with (both must list each other)"),
            F("government", "Government word", FieldKind.Word, "Optional: the country's government when it takes power"),
        },
        Template = "{id} = {\n\tname = \"New Ideology\"\n\tcolor = { 150 150 150 }\n\telections = no\n\topinion = { {id} = 10 }\n\tmodifier = { }\n}\n",
    };

    public static readonly BlockSpec Governments = new()
    {
        Title = "Government setups",
        Source = "common/governments",
        DefaultFile = "common/governments/00_governments.txt",
        Help = "Starting politics by government word (politics feature): ruling party, popularity and laws for countries not listed in history/politics. 'default' is used for any other word.",
        ValidateId = IdRule,
        Fields =
        {
            F("ruling_party", "Ruling ideology", FieldKind.Word),
            F("seats", "Parliament seats", FieldKind.Number),
        },
        Template = "{id} = {\n\truling_party = neutrality\n\tpopularity = { neutrality = 60 democratic = 40 }\n}\n",
        ScriptFirst = true,
    };

    public static readonly BlockSpec LawGroups = new()
    {
        Title = "Law groups",
        Source = "common/laws",
        DefaultFile = "common/laws/00_laws.txt",
        Help = "Law groups (laws feature). Each group holds its laws as blocks; a country has one law of every group. Law keys: cost, potential, allow, modifier, bans, single_party, suspends_elections, on_enact, ai_will_do.",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            F("desc", "Description", FieldKind.Text),
            F("default", "Default law", FieldKind.Word),
        },
        Template = "{id} = {\n\tname = \"New Law Group\"\n\tdefault = first_law\n\tfirst_law = {\n\t\tname = \"First Law\"\n\t\tmodifier = { }\n\t}\n\tsecond_law = {\n\t\tname = \"Second Law\"\n\t\tcost = 100\n\t\tmodifier = { manpower = 0.1 }\n\t}\n}\n",
        ScriptFirst = true,
    };

    public static readonly BlockSpec CabinetPosts = new()
    {
        Title = "Cabinet posts",
        Source = "common/cabinet",
        DefaultFile = "common/cabinet/00_cabinet.txt",
        Help = "Cabinet posts (cabinet feature). Ministers come from the governing parties; per_point modifiers are multiplied by the minister's stat.",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            F("stat", "Stat", FieldKind.Word, "Character stat that makes a minister good at it"),
            F("cost", "Cost (political power)", FieldKind.Number),
        },
        Template = "{id} = {\n\tname = \"New Post\"\n\tstat = charisma\n\tper_point = { stability_target = 0.5 }\n}\n",
    };

    public static readonly BlockSpec Modifiers = new()
    {
        Title = "Modifiers",
        Source = "common/modifiers",
        DefaultFile = "common/modifiers/00_modifiers.txt",
        Help = "Bundles of bonuses. Give them with add_modifier = { name = <id> years = 1 }, or add a trigger to apply them automatically while it is true. Keys the engine uses: income upkeep manpower tax attack defense org_recovery speed opinion relations. Any other key can be read in script values as modifier:<key>.",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            F("desc", "Description", FieldKind.Text),
            new FieldSpec("scope", "Applies to", FieldKind.Choice) { Options = _ => new[] { "country", "state", "location", "character" } },
            F("hidden", "Hidden", FieldKind.Bool),
            F("income", "income", FieldKind.Number, "+0.1 = +10% income"),
            F("upkeep", "upkeep", FieldKind.Number),
            F("manpower", "manpower", FieldKind.Number),
            F("tax", "tax (state)", FieldKind.Number),
            F("attack", "attack", FieldKind.Number),
            F("defense", "defense", FieldKind.Number),
            F("org_recovery", "org_recovery", FieldKind.Number),
            F("speed", "speed", FieldKind.Number),
            F("opinion", "opinion", FieldKind.Number, "Opinion other countries have of this one"),
            F("relations", "relations", FieldKind.Number, "Extra diplomatic relation slots"),
        },
        Template = "{id} = {\n\tname = \"New Modifier\"\n\tincome = 0.1\n\t# trigger = { is_at_war = yes }     # optional: applied automatically while true\n}\n",
    };

    public static readonly BlockSpec Decisions = new()
    {
        Title = "Decisions",
        Source = "common/decisions",
        DefaultFile = "common/decisions/00_decisions.txt",
        Help = "Buttons in the Decisions panel (F6 in game). potential = when it is shown, allow = when it can be taken, effect = what happens. The AI takes decisions using ai_will_do.",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            F("desc", "Description", FieldKind.Text),
            F("category", "Category", FieldKind.Word),
            F("cost", "Cost (gold)", FieldKind.Number),
            F("cooldown_days", "Cooldown (days)", FieldKind.Number),
            F("repeatable", "Repeatable", FieldKind.Bool),
            new FieldSpec("picture", "Picture", FieldKind.Image) { ImageFolder = "gfx/decisions" },
        },
        Template = "{id} = {\n\tname = \"New Decision\"\n\tdesc = \"What this decision does.\"\n\tpotential = { }\n\tallow = { gold >= 50 }\n\teffect = {\n\t\tadd_gold = -50\n\t}\n\tai_will_do = { base = 1 }\n}\n",
    };

    public static readonly BlockSpec ScriptValues = new()
    {
        Title = "Script values",
        Source = "common/script_values",
        DefaultFile = "common/script_values/00_values.txt",
        ScriptFirst = true,
        Help = "Named formulas usable anywhere a number is: triggers (army_size > 5), effects (add_gold = war_tax), texts ([ROOT.war_tax]). Some names replace engine formulas - see 'Change how the game works'.",
        ValidateId = IdRule,
        Template = "{id} = {\n\tvalue = 0\n\tadd = { value = num_of_states multiply = 2 }\n\tif = { limit = { is_at_war = yes } multiply = 1.5 }\n\tmin = 0\n}\n",
    };

    public static readonly BlockSpec Interface = new()
    {
        Title = "Windows & map modes",
        Source = "interface",
        IdKey = "id",
        StatementKeys = new[] { "panel", "map_mode" },
        NewTypes = new[] { "panel", "map_mode" },
        DefaultFile = "interface/00_panels.txt",
        ScriptFirst = true,
        Help = "panel = a window opened from the left menu bar (optional icon and hotkey): header, text, bar, button, separator and list elements. map_mode = colors every location by a value. Texts can show values: [ROOT.prestige].",
        ValidateId = IdRule,
        Fields =
        {
            F("title", "Title (panel)", FieldKind.Text),
            F("button_text", "Button text (panel)", FieldKind.Text),
            F("hotkey", "Hotkey (panel)", FieldKind.Word, "F7, F8, ..."),
            new FieldSpec("icon", "Icon (panel)", FieldKind.Image) { ImageFolder = "gfx/interface/icons" },
            F("width", "Width (panel)", FieldKind.Number),
            F("name", "Name (map mode)", FieldKind.Text),
            F("min", "Min value (map mode)", FieldKind.Number),
            F("max", "Max value (map mode)", FieldKind.Number),
            F("low_color", "Low color (map mode)", FieldKind.Color),
            F("high_color", "High color (map mode)", FieldKind.Color),
        },
        Template = "panel = {\n\tid = {id}\n\ttitle = \"New Window\"\n\tbutton_text = \"Window\"\n\theader = \"[ROOT]\"\n\ttext = \"Gold: [ROOT.gold]\"\n\tbutton = {\n\t\ttext = \"Do something\"\n\t\tallow = { gold >= 10 }\n\t\teffect = { add_gold = -10 }\n\t}\n}\n",
    };

    public static readonly BlockSpec Events = new()
    {
        Title = "Events",
        Source = "events",
        IdKey = "id",
        StatementKeys = new[] { "country_event", "character_event", "event" },
        NewTypes = new[] { "country_event", "character_event" },
        DefaultFile = "events/my_events.txt",
        NameKey = "title",
        Help = "Pop-ups with options. Fire them from on actions, effects (country_event = { id = x days = 10 }) or with mean_time_to_happen. ROOT is the country (or character for character_event).",
        ValidateId = IdRule,
        Fields =
        {
            F("title", "Title", FieldKind.Text),
            F("desc", "Text", FieldKind.Text),
            new FieldSpec("picture", "Picture", FieldKind.Image) { ImageFolder = "gfx/events" },
            F("is_triggered_only", "Only fired by scripts", FieldKind.Bool, "yes = never fires by itself (needs an on action or effect)"),
            F("fire_only_once", "Fire only once", FieldKind.Bool),
            F("hidden", "Hidden (no window)", FieldKind.Bool),
            F("pause", "Pauses the game", FieldKind.Bool, "yes/no for this event (not set = the pause_on_event rule). For a condition use the Script: pause = { is_at_war = yes }"),
        },
        Template = "country_event = {\n\tid = {id}\n\ttitle = \"Event title\"\n\tdesc = \"Event text. [ROOT] is the country.\"\n\tis_triggered_only = yes\n\ttrigger = { }\n\timmediate = { }\n\toption = {\n\t\tname = \"OK\"\n\t\tadd_gold = 10\n\t}\n}\n",
    };

    public static readonly BlockSpec DiplomaticActions = new()
    {
        Title = "Diplomatic actions",
        Source = "common/diplomatic_actions",
        DefaultFile = "common/diplomatic_actions/00_actions.txt",
        Help = "Actions in the diplomacy panel. scope:actor = the country doing it, scope:recipient = the target. With requires_acceptance = yes the AI decides using ai_acceptance.",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            F("desc", "Description", FieldKind.Text),
            F("category", "Category", FieldKind.Word),
            F("requires_acceptance", "Needs acceptance", FieldKind.Bool),
            F("cost", "Cost (gold)", FieldKind.Number),
            F("cooldown_days", "Cooldown (days)", FieldKind.Number),
        },
        Template = "{id} = {\n\tname = \"New Action\"\n\tpotential = { }\n\tallow = { }\n\trequires_acceptance = yes\n\tai_acceptance = { base = 0 opinion_factor = 0.5 }\n\ton_accept = { }\n\tai_will_do = { base = 0 }\n}\n",
    };

    public static readonly BlockSpec CharacterInteractions = new()
    {
        Title = "Character interactions",
        Source = "common/character_interactions",
        DefaultFile = "common/character_interactions/00_interactions.txt",
        Help = "Actions on characters (Imperator style). scope:actor = your country, scope:recipient = the character.",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            F("desc", "Description", FieldKind.Text),
            F("category", "Category", FieldKind.Word),
            F("requires_acceptance", "Needs acceptance", FieldKind.Bool),
            F("cost", "Cost (gold)", FieldKind.Number),
            F("cooldown_days", "Cooldown (days)", FieldKind.Number),
        },
        Template = "{id} = {\n\tname = \"New Interaction\"\n\tpotential = { }\n\tallow = { }\n\ton_accept = { }\n}\n",
    };

    public static readonly BlockSpec CasusBelli = new()
    {
        Title = "Casus belli",
        Source = "common/casus_belli",
        DefaultFile = "common/casus_belli.txt",
        Help = "Reasons for war. allow decides who can use it against whom.",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            F("peace_cost_multiplier", "Peace cost multiplier", FieldKind.Number),
            F("others_opinion", "Opinion of others", FieldKind.Number),
            F("ai_weight", "AI weight", FieldKind.Number),
        },
        Template = "{id} = {\n\tname = \"New Casus Belli\"\n\tallow = { always = yes }\n}\n",
    };

    public static readonly BlockSpec SubjectTypes = new()
    {
        Title = "Subject types",
        Source = "common/subject_types",
        DefaultFile = "common/subject_types.txt",
        Help = "Kinds of subjects (vassal, tributary...).",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            F("income_share", "Income share to overlord", FieldKind.Number),
            F("joins_overlord_wars", "Joins overlord's wars", FieldKind.Bool),
            F("can_declare_war", "Can declare war", FieldKind.Bool),
        },
        Template = "{id} = {\n\tname = \"New Subject Type\"\n\tincome_share = 0.1\n}\n",
    };

    public static readonly BlockSpec Traits = new()
    {
        Title = "Traits",
        Source = "common/traits",
        DefaultFile = "common/traits.txt",
        Help = "Character traits. Numbers named after a character stat change that stat (martial = 2).",
        ValidateId = IdRule,
        Fields =
        {
            F("name", "Name", FieldKind.Text),
            F("desc", "Description", FieldKind.Text),
            new FieldSpec("category", "Category", FieldKind.Choice) { Options = _ => new[] { "personality", "education", "health", "fame" } },
            F("random_weight", "Random weight", FieldKind.Number, "Chance to be given to generated characters (0 = never)"),
            F("inherit_chance", "Inherit chance", FieldKind.Number, "0-1"),
            new FieldSpec("opposites", "Opposite traits", FieldKind.List) { Options = i => i.Traits },
        },
        ExtraFields = i => i.CharacterStats.Select(s => new FieldSpec(s, Burger.Core.Localisation.Prettify(s), FieldKind.Number)),
        Template = "{id} = {\n\tname = \"New Trait\"\n\trandom_weight = 10\n}\n",
    };

    public static readonly BlockSpec OnActions = new()
    {
        Title = "On actions",
        Source = "common/on_actions",
        DefaultFile = "common/on_actions/00_on_actions.txt",
        ScriptFirst = true,
        Help = "Things that happen at engine moments. Names: on_game_start, on_game_start_global, on_monthly_pulse, on_yearly_pulse, on_war_declared, on_war_declared_on_us, on_peace, on_battle_won, on_battle_lost, on_location_occupied, on_state_transferred, on_country_eliminated, on_new_ruler, on_character_death, on_character_born, on_decision_taken. Each can fire events (events = { id }) and run effects (effect = { }).",
        ValidateId = IdRule,
        Template = "{id} = {\n\tevents = { }\n\teffect = { }\n}\n",
    };
}
