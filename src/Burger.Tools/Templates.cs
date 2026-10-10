using System.Text;

namespace Burger.Tools;

/// <summary>Files written by 'burger new' and 'burger export'.</summary>
public static class Templates
{
    /// <summary>A csproj that gives code editors IntelliSense for scripts/*.cs (the game compiles the scripts itself).</summary>
    public static void WriteScriptsProject(string scriptsDir, string engineRoot)
    {
        Directory.CreateDirectory(scriptsDir);
        string core = Path.Combine(engineRoot, "src", "Burger.Core", "Burger.Core.csproj");
        File.WriteAllText(Path.Combine(scriptsDir, "Scripts.csproj"), $@"<!-- Only for code completion in Visual Studio / VS Code / Rider. The game compiles these scripts itself at startup. -->
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>annotations</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <NoWarn>CS1591</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include=""{core}"" />
    <Using Include=""Burger.Core"" />
    <Using Include=""Burger.Core.AI"" />
    <Using Include=""Burger.Core.Characters"" />
    <Using Include=""Burger.Core.Data"" />
    <Using Include=""Burger.Core.Diplomacy"" />
    <Using Include=""Burger.Core.Economy"" />
    <Using Include=""Burger.Core.Events"" />
    <Using Include=""Burger.Core.Map"" />
    <Using Include=""Burger.Core.Mechanics"" />
    <Using Include=""Burger.Core.Model"" />
    <Using Include=""Burger.Core.Modding"" />
    <Using Include=""Burger.Core.Script"" />
    <Using Include=""Burger.Core.Scripting"" />
    <Using Include=""Burger.Core.Warfare"" />
  </ItemGroup>
</Project>
");
    }

    public static string PlayerReadme(string title, string exe) => $@"{title}
{new string('=', title.Length)}

Start the game with {exe}.

Saves are stored in the 'saves' folder, logs (including error.log) in 'logs'.

MODDING
Everything in the 'game' folder is plain text and images and can be edited:
  game/common/countries/   countries (tag, name, color, flag)
  game/map/                province map, heightmap, terrain, trees, rivers, locations.txt, states/
  game/history/            starting characters, armies and diplomacy
  game/events/             events
  game/common/             rules (defines.txt), units, traits, diplomatic actions...
  game/scripts/            C# scripts, compiled when the game starts
  game/localisation/       texts
Press F2 in game to see errors in modified files, ` for the console, F10 for the map editor.

Made with BurgerEngine.
";

}

/// <summary>Minimal 24-bit BMP writer (BMP is read by the engine like PNG).</summary>
public static class Bmp
{
    public static void Write(string path, int w, int h, byte[] rgb)
    {
        int rowSize = (w * 3 + 3) & ~3;
        int dataSize = rowSize * h;
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(54 + dataSize); bw.Write(0); bw.Write(54);
        bw.Write(40); bw.Write(w); bw.Write(h); bw.Write((short)1); bw.Write((short)24); bw.Write(0); bw.Write(dataSize);
        bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);
        var row = new byte[rowSize];
        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 3;
                row[x * 3] = rgb[i + 2];
                row[x * 3 + 1] = rgb[i + 1];
                row[x * 3 + 2] = rgb[i];
            }
            bw.Write(row);
        }
    }
}
