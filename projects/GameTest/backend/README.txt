Backend: your game's locked C# code.

Put C# files here that change how your game works and that players must NOT be able to change:
combat models, warfare/diplomacy/economy rules, the AI, new mechanics...

- While you develop, these files are compiled when the game starts (no engine rebuild needed).
- 'burger export' compiles them INTO the EXE. They are not copied next to it, so players cannot see or change them.
- C# in game/scripts/ works the same way but stays moddable by players.

Every class deriving from GameScript is created automatically. In Burger Studio: Content Browser > Backend > + Create > C# script.
