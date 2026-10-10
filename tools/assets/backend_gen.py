"""Builds the default backend C# files (assets/backend/*.cs) from the engine's rule classes."""
import re, sys, os

SRC = sys.argv[1]   # src/Burger.Core
OUT = sys.argv[2]   # assets/backend


def read(rel):
    return open(os.path.join(SRC, rel), encoding='utf-8').read()


def class_block(text, header_regex):
    """Returns (doc_comment, header_line, body_without_outer_braces) of the class whose header matches."""
    m = re.search(header_regex, text)
    assert m, header_regex
    start = m.start()
    # doc comment lines directly above
    before = text[:start].rstrip('\n').split('\n')
    doc = []
    while before and before[-1].strip().startswith('///'):
        doc.insert(0, before.pop())
    i = text.index('{', m.end() - 1)
    depth = 0
    for j in range(i, len(text)):
        if text[j] == '{':
            depth += 1
        elif text[j] == '}':
            depth -= 1
            if depth == 0:
                return '\n'.join(doc), text[start:i].strip(), text[i + 1:j]
    raise Exception('unbalanced')


def overrides(body):
    body = body.replace('public virtual ', 'public override ').replace('protected virtual ', 'protected override ')
    return body


def file(name, intro, parts, setup_doc, setup_body):
    s = '// ' + intro.replace('\n', '\n// ') + '\n'
    s += '//\n// This file is BACKEND code: it is compiled into your game\'s EXE and players cannot change it.\n'
    s += '// It starts as a copy of the engine\'s default. Edit anything you like; delete the file to use the engine default again.\n\n'
    s += '\n\n'.join(parts)
    s += f'\n\n/// <summary>{setup_doc}</summary>\npublic sealed class {name}Setup : GameScript\n{{\n'
    s += '    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules\n'
    s += '    public override int Priority => -1000;\n\n'
    s += '    public override void OnLoad()\n    {\n' + setup_body + '    }\n}\n'
    return s.replace('\r\n', '\n')


def rules_class(rel, base, new_name):
    doc, header, body = class_block(read(rel), r'public class ' + base + r'\b[^\n]*')
    return f'{doc}\npublic class {new_name} : {base}\n{{{overrides(body)}}}'



FEAT = OUT  # assets/features
def put(feature, name, text):
    d = os.path.join(FEAT, feature, 'backend')
    os.makedirs(d, exist_ok=True)
    open(os.path.join(d, name), 'w', encoding='utf-8', newline='\n').write(re.sub(r'\n{3,}', '\n\n', text).replace('{\n\n', '{\n'))

cm = read('Warfare/CombatModels.cs')
for cls, new, fid, fname, label in [('Hoi4CombatModel', 'Hoi4Combat', 'combat_hoi4', 'Combat_HOI4.cs', 'Hearts of Iron IV style'),
                                    ('Eu4CombatModel', 'Eu4Combat', 'combat_eu4', 'Combat_EU4.cs', 'Europa Universalis style'),
                                    ('ImperatorCombatModel', 'ImperatorCombat', 'combat_imperator', 'Combat_Imperator.cs', 'Imperator: Rome style')]:
    doc, header, body = class_block(cm, r'public sealed class ' + cls + r' : ICombatModel')
    body = re.sub(r'(?<![.\w])Data\.Defines', 'Defines', body)
    part = f'{doc}\npublic sealed class {new} : ICombatModel\n{{{body}}}'
    put(fid, fname, file(new + '', f'{label} combat model. Active while common/defines.txt says combat_model = ' + fid[7:] + '.\n'
        'Helpers shared by all models (frontline, combat width, damage) are in CombatMath.',
        [part], 'Registers the combat model (same id as the engine one, so this copy replaces it).',
        f'        Game.Combat.Register(new {new}());\n'))

put('military', 'Warfare.cs', file('Warfare',
    'Warfare rules: who may enter where, movement speed, organisation recovery and division power.',
    [rules_class('Warfare/WarfareRules.cs', 'WarfareRules', 'GameWarfareRules')],
    'Installs the warfare rules.', '        Game.Rules.Warfare = new GameWarfareRules();\n'))
put('fog_of_war', 'Vision.cs', file('Vision',
    'Fog of war: which locations each country can see (its land, allies, and around its armies).',
    [rules_class('Warfare/Vision.cs', 'VisionRules', 'GameVisionRules')],
    'Installs the vision rules.', '        Game.Rules.Vision = new GameVisionRules();\n'))
put('economy', 'Economy.cs', file('Economy',
    'Economy rules: income, army upkeep, manpower and who may recruit what where.',
    [rules_class('Economy/EconomySystem.cs', 'EconomyRules', 'GameEconomyRules')],
    'Installs the economy rules.', '        Game.Rules.Economy = new GameEconomyRules();\n'))
put('diplomacy', 'Diplomacy.cs', file('Diplomacy',
    'Diplomacy rules: opinion, diplomatic relations, declaring war, calls to arms, war score and peace costs.\n'
    'The diplomatic actions themselves are data (common/diplomatic_actions) and stay moddable.',
    [rules_class('Diplomacy/DiplomacyRules.cs', 'DiplomacyRules', 'GameDiplomacyRules')],
    'Installs the diplomacy rules.', '        Game.Rules.Diplomacy = new GameDiplomacyRules();\n'))
put('characters', 'Characters.cs', file('Characters',
    'Character rules: succession, heirs, natural death and births.',
    [rules_class('Characters/CharacterSystem.cs', 'CharacterRules', 'GameCharacterRules')],
    'Installs the character rules.', '        Game.Rules.Characters = new GameCharacterRules();\n'))
doc, header, body = class_block(read('AI/AiSystem.cs'), r'public class AiSystem\b')
body = overrides(body)
body = re.sub(r'\n    protected readonly Game Game;\n', '\n', body)
body = re.sub(r'\n    public AiSystem\(Game game\) \{ Game = game; \}\n', '\n    public GameAi(Game game) : base(game) { }\n', body)
body = re.sub(r'\n    /// <summary>Set to false to freeze all AI decisions \(debugging\)\.</summary>\n    public bool Enabled \{ get; set; \} = true;\n', '\n', body)
ai = f'/// <summary>How AI countries run their armies, recruit, use diplomacy and make peace.</summary>\npublic class GameAi : AiSystem\n{{{body}}}'
put('ai', 'AI.cs', file('Ai',
    'AI: how computer-controlled countries manage armies and fronts, recruit, use diplomacy and make peace.\n'
    'Each part only runs when the game has the matching feature (military, diplomacy).',
    [ai], 'Installs the AI.', '        Game.Ai = new GameAi(Game);\n'))
put('production', 'Production.cs', file('Production',
    'Production rules: which buildings count as factories, factory output and efficiency, construction speed and\n'
    'where buildings may be built. The production system itself (lines, stockpile, queue) uses these rules.',
    [rules_class('Production/ProductionSystem.cs', 'ProductionRules', 'GameProductionRules')],
    'Installs the production rules.', '        Game.Rules.Production = new GameProductionRules();\n'))
for fid in ('supply_range', 'supply_hubs'):
    put(fid, 'Supply.cs', file('Supply',
        'Supply rules: which land carries supply, how far it reaches (terrain, infrastructure), the capital, local supply,\n'
        'supply hubs and railways (hub mode) and how much supply a division uses.',
        [rules_class('Supply/SupplySystem.cs', 'SupplyRules', 'GameSupplyRules')],
        'Installs the supply rules.', '        Game.Rules.Supply = new GameSupplyRules();\n'))
put('division_designer', 'Designer.cs', file('Designer',
    'Division designer rules: how many battalions a division may have, which designs are allowed and which\n'
    'unit types a country may use (by default: a unit type with potential = { ... } needs that trigger).',
    [rules_class('Warfare/Designer.cs', 'DesignerRules', 'GameDesignerRules')],
    'Installs the designer rules.', '        Game.Designer.Rules = new GameDesignerRules();\n'))
put('navy', 'Navy.cs', file('Navy',
    'Naval rules: what counts as a port, ship damage and targeting (screens protect capital ships), when fleets retreat,\n'
    'repairs, transport capacity, how long armies take to cross the sea and the naval invasion penalty.',
    [rules_class('Navy/NavySystem.cs', 'NavyRules', 'GameNavyRules')],
    'Installs the naval rules.', '        Game.Rules.Navy = new GameNavyRules();\n'))
put('air', 'Air.cs', file('Air',
    'Air rules: wing size, air base capacity, which bases a country may use, ranges, air superiority and its combat bonus,\n'
    'losses, close air support and bombing damage, bombing targets and repairs.',
    [rules_class('Air/AirSystem.cs', 'AirRules', 'GameAirRules')],
    'Installs the air rules.', '        Game.Rules.Air = new GameAirRules();\n'))
put('lend_lease', 'Support.cs', file('Support',
    'Lend-lease and volunteer rules: how many volunteer divisions a country may send, who may receive volunteers or\n'
    'equipment, and the opinion the receiver gains.',
    [rules_class('Warfare/Support.cs', 'SupportRules', 'GameSupportRules')],
    'Installs the support rules.', '        Game.Rules.Support = new GameSupportRules();\n'))
put('politics', 'Politics.cs', file('Politics',
    'Politics rules: how ideology popularity drifts each month, election votes, which parties form coalitions,\n'
    'how fast political movements grow and when shut-out ideologies start civil wars.',
    [rules_class('Politics/PoliticsRules.cs', 'PoliticsRules', 'GamePoliticsRules')],
    'Installs the politics rules.', '        Game.Rules.Politics = new GamePoliticsRules();\n'))
print('ok')
