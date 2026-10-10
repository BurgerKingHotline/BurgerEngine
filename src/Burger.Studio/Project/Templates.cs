namespace Burger.Studio;

public sealed record Template(string Name, string Code, string Description = "");

/// <summary>Snippets for the text editor and starting points for C# scripts. All of them compile/load as they are.</summary>
public static class Templates
{
    public static readonly Template[] Script =
    {
        new("Event (country)", "country_event = {\n\tid = my_mod.1\n\ttitle = \"Title\"\n\tdesc = \"Text. [ROOT] is the country, [ROOT.ruler] its ruler.\"\n\tis_triggered_only = yes\n\ttrigger = { }\n\toption = {\n\t\tname = \"OK\"\n\t\tadd_gold = 10\n\t}\n\toption = {\n\t\tname = \"No\"\n\t\tai_chance = { base = 1 }\n\t}\n}\n"),
        new("Event (character)", "character_event = {\n\tid = my_mod.2\n\ttitle = \"Title\"\n\tdesc = \"[ROOT] is the character.\"\n\tis_triggered_only = yes\n\toption = {\n\t\tname = \"OK\"\n\t\tadd_trait = brave\n\t}\n}\n"),
        new("Event that happens by itself (mean time to happen)", "country_event = {\n\tid = my_mod.3\n\ttitle = \"Title\"\n\tdesc = \"Text\"\n\ttrigger = { is_at_war = yes }\n\tmean_time_to_happen = { months = 24 }\n\toption = { name = \"OK\" }\n}\n"),
        new("Decision", "my_decision = {\n\tname = \"My Decision\"\n\tdesc = \"What it does.\"\n\tpotential = { }\n\tallow = { gold >= 50 }\n\teffect = { add_gold = -50 }\n\tai_will_do = { base = 1 }\n}\n"),
        new("Modifier", "my_modifier = {\n\tname = \"My Modifier\"\n\tincome = 0.1\n\tattack = 0.05\n}\n"),
        new("Conditional modifier (automatic)", "my_auto_modifier = {\n\tname = \"At War\"\n\ttrigger = { is_at_war = yes }\n\tincome = -0.1\n}\n"),
        new("Resource", "piety = {\n\tname = \"Piety\"\n\tstart = 0\n\tmin = 0\n\tmax = 100\n\tmonthly = { value = 0.5 }\n\tcolor = { 200 200 255 }\n}\n"),
        new("Script value (formula)", "my_value = {\n\tvalue = 0\n\tevery_owned_state = { add = tax }\n\tif = { limit = { is_at_war = yes } multiply = 0.5 }\n\tmin = 0\n}\n"),
        new("Window (panel)", "panel = {\n\tid = my_panel\n\ttitle = \"My Window\"\n\tbutton_text = \"Mine\"\n\theader = \"[ROOT]\"\n\ttext = \"Gold: [ROOT.gold]\"\n\tbar = { text = \"Gold\" value = gold min = 0 max = 1000 }\n\tbutton = {\n\t\ttext = \"Spend 10 gold\"\n\t\tallow = { gold >= 10 }\n\t\teffect = { add_gold = -10 }\n\t}\n}\n"),
        new("Map mode", "map_mode = {\n\tid = my_map_mode\n\tname = \"Tax\"\n\tvalue = { value = state.tax }\n\tmin = 0\n\tmax = 10\n\tlow_color = { 60 60 140 }\n\thigh_color = { 240 220 80 }\n}\n"),
        new("Country", "NEW = {\n\tname = \"New Country\"\n\tcolor = { 120 120 120 }\n\tflag = \"gfx/flags/NEW.png\"\n\tcapital = some_location\n}\n"),
        new("State", "my_state = {\n\towner = TAG\n\tcores = { TAG }\n\tmanpower = 100\n\ttax = 1\n\tlocations = { loc_a loc_b }\n}\n"),
        new("Character", "my_character = {\n\tfirst_name = Name\n\tfamily = \"Family\"\n\tage = 30\n\tcountry = TAG\n\ttraits = { brave }\n}\n"),
        new("On action", "on_monthly_pulse = {\n\tevents = { }\n\teffect = { }\n}\n"),
        new("if / else", "if = {\n\tlimit = { gold > 100 }\n\tadd_gold = -10\n}\nelse = {\n\tadd_gold = 5\n}\n"),
        new("random_list", "random_list = {\n\t60 = { add_gold = 10 }\n\t40 = { add_gold = -10 }\n}\n"),
        new("every_owned_state", "every_owned_state = {\n\tlimit = { tax < 2 }\n\tadd_tax = 1\n}\n"),
        new("any_ (trigger with count)", "any_owned_state = {\n\tcount >= 3\n\ttax >= 2\n}\n"),
        new("AI weight (ai_will_do / ai_chance)", "ai_will_do = {\n\tbase = 10\n\tmodifier = { factor = 0 is_at_war = yes }\n\tmodifier = { add = 5 gold > 200 }\n}\n"),
        new("Custom tooltip", "custom_tooltip = {\n\ttext = \"Explained in plain words\"\n\tgold > 100\n}\n"),
    };

    public static readonly Template[] CSharp =
    {
        new("Empty script", @"// Every class deriving from GameScript is created when the game starts.
public class MyScript : GameScript
{
    public override void OnLoad()
    {
        // register triggers, effects, combat models, rules, windows... here
    }

    public override void OnMonthlyTick()
    {
    }
}
", "A script with the most used hooks."),

        new("New trigger, effect and value for .txt files", @"// Adds words you can use in every .txt file:
//   trigger:  is_rich = yes
//   effect:   double_gold = yes
//   value:    gold_per_state  (gold_per_state > 10,  add_gold = gold_per_state)
public class MyScriptWords : GameScript
{
    public override void OnLoad()
    {
        Game.Script.RegisterTrigger(""is_rich"",
            (ctx, node) => (ctx.CountryOfThis?.Gold >= 500) == node.AsBool(true),
            (ctx, node) => node.AsBool(true) ? ""Has at least 500 gold"" : ""Has less than 500 gold"");

        Game.Script.RegisterEffect(""double_gold"",
            (ctx, node) => { if (ctx.CountryOfThis is { } c && node.AsBool(true)) c.Gold *= 2; },
            (ctx, node) => ""Doubles the treasury"");

        Game.Script.RegisterValue(""gold_per_state"",
            (ctx, scope) => ScopeHelpers.CountryOf(scope) is { } c && c.OwnedStates.Count > 0 ? c.Gold / c.OwnedStates.Count : 0);
    }
}
", "Use it in events, decisions and windows."),

        new("Custom combat model", @"// A new way to resolve battles. Activate it with  combat_model = my_combat  in common/defines.txt
// (or type  combat my_combat  in the in-game console).
public class MyCombatScript : GameScript
{
    public override void OnLoad() => Game.Combat.Register(new MyCombat());
}

public class MyCombat : ICombatModel
{
    public string Id => ""my_combat"";
    public string Name => ""My combat model"";

    public void OnBattleStart(Battle battle, Game game) { }

    // called every hour while the battle lasts
    public void Tick(Battle b, Game game)
    {
        if (b.Hours % 12 != 0) return; // one round every 12 hours
        double attack = b.Attackers.Sum(d => d.Stat(""soft_attack"") * d.StrengthFraction) * CombatMath.CountryMultiplier(game, b.Attackers, ""attack"");
        double defend = b.Defenders.Sum(d => d.Stat(""defense"") * 0.3 * d.StrengthFraction)
                        * (1 + b.Location.Terrain.DefenseModifier) * CombatMath.CountryMultiplier(game, b.Defenders, ""defense"");
        foreach (var d in b.Defenders) CombatMath.Damage(b, d, attack * 0.01 / b.Defenders.Count, attack * 0.02 / b.Defenders.Count);
        foreach (var a in b.Attackers) CombatMath.Damage(b, a, defend * 0.01 / b.Attackers.Count, defend * 0.02 / b.Attackers.Count);
        CombatMath.UpdateProgress(b); // ends the battle when one side breaks
    }
}
", "Replace HOI4/EU4/Imperator combat with your own rules."),

        new("Change diplomacy rules", @"// Replaces parts of the diplomacy rules. Override only what you want to change;
// everything else keeps the engine behaviour (base.Method(...)).
public class MyDiplomacyScript : GameScript
{
    public override void OnLoad() => Game.Rules.Diplomacy = new MyDiplomacyRules();
}

public class MyDiplomacyRules : DiplomacyRules
{
    // allies only help in defensive wars
    public override bool WillJoinWar(Game game, Country ally, War war, bool asAttacker, Country caller)
        => !asAttacker && base.WillJoinWar(game, ally, war, asAttacker, caller);

    // one extra diplomatic relation for everybody
    public override int MaxRelations(Game game, Country c) => base.MaxRelations(game, c) + 1;

    // no wars during the first year of the game
    public override string? CannotDeclareWarReason(Game game, Country attacker, Country defender, CasusBelli? cb)
        => game.World.Date.Year == game.Data.Defines.StartDate.Year ? ""Too early for war"" : base.CannotDeclareWarReason(game, attacker, defender, cb);
}
", "Alliances, war declarations, peace costs, opinion..."),

        new("Change economy rules", @"// Replaces the income / upkeep / manpower formulas.
// (For simple changes you can instead define the script value country_monthly_income in common/script_values.)
public class MyEconomyScript : GameScript
{
    public override void OnLoad() => Game.Rules.Economy = new MyEconomyRules();
}

public class MyEconomyRules : EconomyRules
{
    // big empires are harder to tax: income shrinks with the number of states
    public override double MonthlyIncome(Game game, Country c)
        => base.MonthlyIncome(game, c) * Math.Max(0.5, 1.0 - c.OwnedStates.Count * 0.01);

    // armies cost double while at peace
    public override double MonthlyExpenses(Game game, Country c)
        => base.MonthlyExpenses(game, c) * (game.World.WarsOf(c).Any() ? 1 : 2);
}
", "Income, army upkeep, manpower, recruitment."),

        new("Change character rules", @"// Succession, heirs, death and birth chances.
public class MyCharacterScript : GameScript
{
    public override void OnLoad() => Game.Rules.Characters = new MyCharacterRules();
}

public class MyCharacterRules : CharacterRules
{
    // characters live longer
    public override double YearlyDeathChance(Game game, Character c) => base.YearlyDeathChance(game, c) * 0.5;
}
", "Succession laws, life span, children."),

        new("Change movement / warfare rules", @"public class MyWarfareScript : GameScript
{
    public override void OnLoad() => Game.Rules.Warfare = new MyWarfareRules();
}

public class MyWarfareRules : WarfareRules
{
    // all movement is 50% faster
    public override double MoveHours(Game game, Division d, Adjacency adj) => base.MoveHours(game, d, adj) / 1.5;
}
", "Movement speed, who may enter where, organisation recovery."),

        new("Custom AI", @"// Replace parts of the AI. AiSystem has DailyTick, MonthlyTick, ManageMilitary, Recruit, Diplomacy, Peace, BuildDemands.
public class MyAiScript : GameScript
{
    public override void OnLoad() => Game.Ai = new MyAi(Game);
}

public class MyAi : AiSystem
{
    public MyAi(Game game) : base(game) { }

    // example: poor countries do not bother with diplomacy
    public override void Diplomacy(Country c)
    {
        if (c.Gold < 20) return;
        base.Diplomacy(c);
    }
}
", "Decide how computer countries behave."),

        new("Window and map mode in C#", @"// Windows and map modes can be written in interface/*.txt without C#.
// Use C# when you need something the script language cannot express.
public class MyInterfaceScript : GameScript
{
    public override void OnLoad()
    {
        Game.Interface.AddMapMode(new MapModeDef
        {
            Id = ""coastal"",
            Name = ""Coast"",
            ColorFn = loc => loc.IsWater ? null : loc.IsCoastal ? new Rgb(80, 160, 220) : new Rgb(150, 140, 110),
        });

        Game.Interface.AddPanel(new PanelDef
        {
            Id = ""treasury"",
            Title = ""Treasury"",
            ButtonText = ""Treasury"",
            Elements =
            {
                new PanelElement { Kind = PanelElementKind.Header, TextFn = ctx => ctx.CountryOfThis?.Name ?? """" },
                new PanelElement { Kind = PanelElementKind.Text, TextFn = ctx => $""Gold: {ctx.CountryOfThis?.Gold:0}"" },
                new PanelElement
                {
                    Kind = PanelElementKind.Button, Text = ""Mint coins (+25 gold)"",
                    EnabledFn = ctx => ctx.CountryOfThis?.Gold < 1000,
                    OnClick = ctx => { if (ctx.CountryOfThis is { } c) c.Gold += 25; },
                },
            },
        });
    }
}
", "Map modes and windows with C# logic."),

        new("Console command", @"public class MyCommands : GameScript
{
    public override void OnLoad()
    {
        // type  richest  in the in-game console (` key)
        Game.Commands.Register(""richest"", ""list the 5 richest countries"", args =>
            string.Join(""\n"", Game.World.Countries.Where(c => c.Exists).OrderByDescending(c => c.Gold).Take(5)
                .Select(c => $""{c.Tag} {c.Name}: {c.Gold:0}"")));
    }
}
", "Debug and cheat commands."),

        new("React to game events", @"public class MyReactions : GameScript
{
    public override void OnWarDeclared(War war)
    {
        Game.Notify($""A war has started: {war.Name}"", NotificationKind.War);
    }

    public override void OnBattleEnded(Battle battle)
    {
        var winner = battle.AttackerWon ? battle.AttackerCountry : battle.DefenderCountry;
        winner.Gold += 5; // plunder
    }

    public override void OnCharacterDied(Character character)
    {
    }

    public override void OnYearlyTick()
    {
        foreach (var c in Game.World.Countries.Where(c => c.Exists))
            Game.Modifiers.Remove(c, ""my_modifier"");
    }
}
", "Hooks: wars, battles, deaths, ticks, peace, elimination..."),
    };

    public static readonly Template[] CSharpSnippets = CSharp.Skip(1).Concat(new[]
    {
        new Template("Loop over countries", "foreach (var c in Game.World.Countries.Where(c => c.Exists))\n{\n}\n"),
        new Template("Run script text", "Game.RunEffect(\"add_gold = 10\", country);\n"),
        new Template("Check a trigger", "if (Game.EvalTrigger(\"is_at_war = yes\", country))\n{\n}\n"),
        new Template("Read / write a variable", "double v = country.Vars.Get(\"my_var\");\ncountry.Vars.Set(\"my_var\", v + 1);\n"),
        new Template("Add a modifier", "Game.Modifiers.Add(country, \"my_modifier\", days: 365);\n"),
    }).ToArray();
}
