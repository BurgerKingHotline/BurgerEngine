Backend: your game's locked C# code.

C# files here change how your game works and players can NOT change them.
- While you develop, these files are compiled when the game starts (no engine rebuild needed).
- 'burger export' compiles them INTO the EXE. They are not copied next to it, so players cannot see or change them.
- C# in game/scripts/ works the same way but stays moddable by players.

Features add their files here (Project > Features in Burger Studio): Warfare.cs, Combat_HOI4.cs, Vision.cs,
Diplomacy.cs, Characters.cs, Economy.cs, AI.cs... They are your copies: change them freely.
Delete one and the engine's built-in version is used again.

Every class deriving from GameScript is created automatically.
