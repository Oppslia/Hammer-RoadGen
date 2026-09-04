using System;
using System.IO;
using System.Text.Json;

namespace RoadGen.Core;

/// <summary>Persists a few user preferences (the game install directory + which game it is)
/// as JSON in %AppData%\RoadGen so the user is only prompted once.</summary>
public static class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RoadGen", "settings.json");

    /// <summary>The Source game's install directory (e.g. ".../steamapps/common/Half-Life 2").</summary>
    public static string GameDirectory { get; set; } = "";

    /// <summary>Which game that directory is; drives where its materials folder lives.</summary>
    public static GameId Game { get; set; } = GameId.HalfLife2;

    /// <summary>Resolved absolute materials root for the current settings ("" when no directory).</summary>
    public static string MaterialsRoot => GamePaths.ResolveMaterialsRoot(GameDirectory, Game);

    public static void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (doc.RootElement.TryGetProperty("GameDirectory", out JsonElement directory)
                && directory.ValueKind == JsonValueKind.String)
            {
                GameDirectory = directory.GetString() ?? "";
            }

            if (doc.RootElement.TryGetProperty("Game", out JsonElement game)
                && game.ValueKind == JsonValueKind.String
                && GamePaths.TryParse(game.GetString(), out GameId parsed))
            {
                Game = parsed;
            }
        }
        catch (Exception)
        {
            // Corrupt or unreadable settings are ignored; the app just starts fresh.
        }
    }

    public static void Save()
    {
        try
        {
            string directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { GameDirectory, Game }));
        }
        catch (Exception)
        {
            // Never let settings persistence crash the app.
        }
    }
}
