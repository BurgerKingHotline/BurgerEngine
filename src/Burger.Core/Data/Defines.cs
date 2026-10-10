using Burger.Core.Script;

namespace Burger.Core.Data;

/// <summary>
/// Game-wide tunables from <c>common/defines.txt</c>. Every value has a default, so the file only needs to
/// contain what you want to change. Mods can read custom values with <see cref="Get(string,double)"/>.
/// </summary>
public sealed class Defines
{
    public Node Raw { get; private set; } = Node.Root("defines");

    // --- general ---------------------------------------------------------------------------------
    public GameDate StartDate = new(1900, 1, 1);
    public GameDate EndDate = new(2100, 1, 1);
    public bool PauseOnEvent = true;
    /// <summary>Hide enemy armies and darken land the player cannot see.</summary>
    public bool FogOfWar = true;
    public int EventTimeoutDays = 30;

    // --- map / movement ---------------------------------------------------------------------------
    /// <summary>Kilometres represented by one pixel of the province map. Used for movement time.</summary>
    public double KmPerPixel = 2.0;

    // --- warfare --------------------------------------------------------------------------------------
    /// <summary>hoi4, eu4, imperator, or the id of a combat model registered from a C# script.</summary>
    public string CombatModel = "hoi4";
    /// <summary>0 = locations are taken instantly when entered (HOI4). &gt;0 = hours of siege needed per (fort level + 1) (EU4/Imperator).</summary>
    public int OccupationHours = 0;
    public double RiverAttackPenalty = 0.3;
    public double ExtraWidthPerDirection = 0.5;
    public double ReinforceRate = 0.03;
    public double OrgRecoveryRate = 0.08;
    public double GeneralBonusPerPoint = 0.03;
    public string GeneralStat = "martial";
    public double Hoi4StrengthDamage = 0.05;
    public double Hoi4OrgDamage = 0.10;
    public double Eu4BaseCasualties = 15;
    public double Eu4MoraleDamage = 0.12;
    public int Eu4PhaseDays = 3;
    public double FortDefenseBonus = 0.15;
    public double MinOrgToAttack = 0.6;

    // --- economy ---------------------------------------------------------------------------------
    public double IncomeMultiplier = 1.0;
    public double ManpowerMultiplier = 1.0;
    public double DivisionUpkeepFactor = 0.02;

    // --- diplomacy ---------------------------------------------------------------------------------
    public int BaseDiplomaticRelations = 3;
    public string RelationsStat = "charisma";
    public double RelationsPerStatPoint = 0.25;
    public double OpinionDecayPerMonth = 1.0;
    public double AiDiplomacyChance = 0.15;
    public int TruceYears = 5;
    public double WarScoreBattleCap = 30;
    public double PeaceStateCostMultiplier = 1.0;
    public double SubjugateCost = 60;
    public double AnnexCost = 100;
    public double WarExhaustionPeaceMonths = 24;

    // --- characters ----------------------------------------------------------------------------------
    public List<string> CharacterStats = new() { "martial", "finesse", "charisma", "zeal" };
    public int AdultAge = 16;
    public double DeathChanceBase = 0.004;
    public double DeathChancePerYearOver50 = 0.006;
    public double BirthChancePerYear = 0.15;
    public int MaxFertileAge = 45;

    /// <summary>Days per second for speeds 1-5 (0 = unlimited).</summary>
    public double[] GameSpeeds = { 0.5, 1.5, 4, 10, 0 };

    /// <summary>Every define the engine reads, with its default and a description (shown by the editor).</summary>
    public static readonly (string Key, string Default, string Help)[] Known =
    {
        ("start_date", "1900.1.1", "Date the game starts"),
        ("end_date", "2100.1.1", "Date the game ends"),
        ("pause_on_event", "yes", "Pause when an event window opens for the player (an event can override it with pause = yes/no or pause = { trigger })"),
        ("fog_of_war", "yes", "Hide enemy armies and darken land the player cannot see (C#: Game.Rules.Vision)"),
        ("event_timeout_days", "30", "Days before an unanswered event picks its first option"),
        ("game_speeds", "{ 0.5 1.5 4 10 0 }", "In-game days per real second for speeds 1-5 (0 = as fast as possible)"),
        ("km_per_pixel", "2", "Kilometres per pixel of the province map (movement time)"),
        ("combat_model", "hoi4", "Combat resolver: hoi4, eu4, imperator, or one registered from a C# script"),
        ("occupation_hours", "0", "0 = locations are taken when entered (HOI4). >0 = siege hours per fort level + 1 (EU4/Imperator)"),
        ("river_attack_penalty", "0.3", "Attack penalty when attacking across a river"),
        ("extra_width_per_direction", "0.5", "Extra combat width for each additional attack direction"),
        ("reinforce_rate", "0.03", "Share of missing strength refilled per day"),
        ("org_recovery_rate", "0.08", "Share of organisation recovered per day when not fighting"),
        ("general_stat", "martial", "Character stat that makes a general better"),
        ("general_bonus_per_point", "0.03", "Combat bonus per point of the general stat"),
        ("hoi4_strength_damage", "0.05", "HOI4 model: strength damage per hit"),
        ("hoi4_org_damage", "0.1", "HOI4 model: organisation damage per hit"),
        ("eu4_base_casualties", "15", "EU4 model: base casualties per phase"),
        ("eu4_morale_damage", "0.12", "EU4 model: morale damage per phase"),
        ("eu4_phase_days", "3", "EU4 model: days per fire/shock phase"),
        ("fort_defense_bonus", "0.15", "Defense bonus per fort level"),
        ("min_org_to_attack", "0.6", "Organisation share needed before the AI attacks"),
        ("income_multiplier", "1", "Multiplies all tax income"),
        ("manpower_multiplier", "1", "Multiplies all manpower gain"),
        ("division_upkeep_factor", "0.02", "Monthly upkeep = division cost x this"),
        ("recruit_gold_cost", "yes", "Training divisions costs gold (economy feature). The production feature sets it to no: training uses equipment instead"),
        ("production_per_factory", "4.5", "Production: points per military factory per day at full efficiency"),
        ("efficiency_start", "0.1", "Production: efficiency of a new production line"),
        ("efficiency_cap", "1", "Production: highest production efficiency"),
        ("efficiency_growth", "0.02", "Production: daily efficiency growth (slows down near the cap)"),
        ("construction_per_factory", "5", "Production: construction points per civilian factory per day"),
        ("max_factories_per_project", "15", "Production: civilian factories that can work on one building"),
        ("consumer_goods", "0.25", "Production: share of civilian factories busy with consumer goods"),
        ("min_equipment_to_deploy", "0.5", "Production: equipment share a trained division needs before it deploys"),
        ("supply_capital", "15", "Supply: supply produced by the capital"),
        ("supply_local", "2", "Supply: supply of each own, controlled location (+ infrastructure)"),
        ("supply_hub", "10", "Supply hubs: supply of a connected supply hub"),
        ("supply_falloff", "0.75", "Supply: share kept per step away from a source (range mode, terrain makes steps longer)"),
        ("supply_attrition", "0.02", "Supply: daily strength loss of a completely unsupplied division"),
        ("designer_max_battalions", "25", "Division designer: most battalions in one division"),
        ("naval_retreat", "0.4", "Navy: a side retreats when its ships have less than this share of their hit points"),
        ("transport_speed", "12", "Navy: km per hour of armies crossing the sea"),
        ("naval_invasion_penalty", "0.5", "Navy: combat multiplier of divisions attacking from the sea"),
        ("air_wing_size", "50", "Air: planes in a full wing"),
        ("air_base_capacity", "100", "Air: planes one air base level holds"),
        ("air_superiority_bonus", "0.2", "Air: combat bonus with full air superiority over a region (the same as a penalty without any)"),
        ("air_loss_rate", "0.03", "Air: share of planes lost per day in an even air fight"),
        ("cas_damage", "0.02", "Air: close air support damage per plane and ground attack point"),
        ("bombing_damage", "0.0005", "Air: building levels knocked out per plane and strategic attack point per day"),
        ("bomb_repair", "0.05", "Air: bombed building levels repaired per day"),
        ("air_region_states", "4", "Air: states per automatically made air region"),
        ("air_region_km", "700", "Air: largest size of an automatically made air region"),
        ("volunteer_share", "0.15", "Volunteers: share of a country's divisions it may send abroad"),
        ("volunteer_opinion", "20", "Volunteers: opinion the receiver gains"),
        ("lend_lease_opinion", "15", "Lend-lease: opinion the receiver gains"),
        ("base_diplomatic_relations", "3", "Alliances/guarantees/subjects allowed before stats and modifiers"),
        ("relations_stat", "charisma", "Ruler stat that gives extra diplomatic relations"),
        ("relations_per_stat_point", "0.25", "Extra relations per point of that stat"),
        ("opinion_decay_per_month", "1", "How fast temporary opinion fades"),
        ("ai_diplomacy_chance", "0.15", "Monthly chance that an AI country considers a diplomatic action"),
        ("truce_years", "5", "Years of truce after a peace"),
        ("war_score_battle_cap", "30", "Most war score that battles alone can give"),
        ("peace_state_cost_multiplier", "1", "Multiplies the war score cost of taking states"),
        ("subjugate_cost", "60", "War score cost of making the loser a subject"),
        ("annex_cost", "100", "War score cost of annexing the loser"),
        ("war_exhaustion_peace_months", "24", "Months of war after which the AI accepts worse peace deals"),
        ("character_stats", "{ martial finesse charisma zeal }", "Names of character stats"),
        ("adult_age", "16", "Age at which characters become adults"),
        ("death_chance_base", "0.004", "Yearly death chance of a young character"),
        ("death_chance_per_year_over_50", "0.006", "Extra yearly death chance per year of age over 50"),
        ("birth_chance_per_year", "0.15", "Yearly chance of a child for a married woman of fertile age"),
        ("max_fertile_age", "45", "Oldest age at which characters have children"),
    };

    public void Load(Node root)
    {
        Raw = root;
        // Allow both flat keys and grouped blocks:  combat = { combat_model = hoi4 }
        var flat = Node.Root(root.File);
        Flatten(root, flat);
        Raw = flat;

        StartDate = GameDate.Parse(Str("start_date", StartDate.ToScript()), StartDate);
        EndDate = GameDate.Parse(Str("end_date", EndDate.ToScript()), EndDate);
        PauseOnEvent = Bool("pause_on_event", PauseOnEvent);
        FogOfWar = Bool("fog_of_war", FogOfWar);
        EventTimeoutDays = Int("event_timeout_days", EventTimeoutDays);
        KmPerPixel = Get("km_per_pixel", KmPerPixel);
        CombatModel = Str("combat_model", CombatModel).ToLowerInvariant();
        OccupationHours = Int("occupation_hours", OccupationHours);
        RiverAttackPenalty = Get("river_attack_penalty", RiverAttackPenalty);
        ExtraWidthPerDirection = Get("extra_width_per_direction", ExtraWidthPerDirection);
        ReinforceRate = Get("reinforce_rate", ReinforceRate);
        OrgRecoveryRate = Get("org_recovery_rate", OrgRecoveryRate);
        GeneralBonusPerPoint = Get("general_bonus_per_point", GeneralBonusPerPoint);
        GeneralStat = Str("general_stat", GeneralStat);
        Hoi4StrengthDamage = Get("hoi4_strength_damage", Hoi4StrengthDamage);
        Hoi4OrgDamage = Get("hoi4_org_damage", Hoi4OrgDamage);
        Eu4BaseCasualties = Get("eu4_base_casualties", Eu4BaseCasualties);
        Eu4MoraleDamage = Get("eu4_morale_damage", Eu4MoraleDamage);
        Eu4PhaseDays = Math.Max(1, Int("eu4_phase_days", Eu4PhaseDays));
        FortDefenseBonus = Get("fort_defense_bonus", FortDefenseBonus);
        MinOrgToAttack = Get("min_org_to_attack", MinOrgToAttack);
        IncomeMultiplier = Get("income_multiplier", IncomeMultiplier);
        ManpowerMultiplier = Get("manpower_multiplier", ManpowerMultiplier);
        DivisionUpkeepFactor = Get("division_upkeep_factor", DivisionUpkeepFactor);
        BaseDiplomaticRelations = Int("base_diplomatic_relations", BaseDiplomaticRelations);
        RelationsStat = Str("relations_stat", RelationsStat);
        RelationsPerStatPoint = Get("relations_per_stat_point", RelationsPerStatPoint);
        OpinionDecayPerMonth = Get("opinion_decay_per_month", OpinionDecayPerMonth);
        AiDiplomacyChance = Get("ai_diplomacy_chance", AiDiplomacyChance);
        TruceYears = Int("truce_years", TruceYears);
        WarScoreBattleCap = Get("war_score_battle_cap", WarScoreBattleCap);
        PeaceStateCostMultiplier = Get("peace_state_cost_multiplier", PeaceStateCostMultiplier);
        SubjugateCost = Get("subjugate_cost", SubjugateCost);
        AnnexCost = Get("annex_cost", AnnexCost);
        WarExhaustionPeaceMonths = Get("war_exhaustion_peace_months", WarExhaustionPeaceMonths);
        var speeds = root.GetList("game_speeds");
        if (speeds.Count == 5 && speeds.All(x => Node.TryParseDouble(x, out _)))
            GameSpeeds = speeds.Select(x => { Node.TryParseDouble(x, out var d); return d; }).ToArray();
        var stats = root.GetList("character_stats");
        if (stats.Count == 0) stats = flat.GetList("character_stats");
        if (stats.Count > 0) CharacterStats = stats.Select(s => s.ToLowerInvariant()).ToList();
        AdultAge = Int("adult_age", AdultAge);
        DeathChanceBase = Get("death_chance_base", DeathChanceBase);
        DeathChancePerYearOver50 = Get("death_chance_per_year_over_50", DeathChancePerYearOver50);
        BirthChancePerYear = Get("birth_chance_per_year", BirthChancePerYear);
        MaxFertileAge = Int("max_fertile_age", MaxFertileAge);
    }

    private static void Flatten(Node src, Node dst)
    {
        foreach (var n in src.All)
        {
            if (n.Children != null && n.Items().Count == 0 && n.BlockType == null && !string.Equals(n.Key, "character_stats", StringComparison.OrdinalIgnoreCase) && !string.Equals(n.Key, "game_speeds", StringComparison.OrdinalIgnoreCase))
                Flatten(n, dst);
            else
                dst.Children!.Add(n);
        }
    }

    /// <summary>Reads any numeric define (including custom ones added by modders).</summary>
    public double Get(string key, double def) => Raw.GetDouble(key, def);
    public bool GetBool(string key, bool def) => Raw.Get(key) is { Value: not null } n ? n.AsBool(def) : def;
    public int Int(string key, int def) => Raw.GetInt(key, def);
    public bool Bool(string key, bool def) => Raw.GetBool(key, def);
    public string Str(string key, string def) => Raw.GetString(key, def) ?? def;
}
