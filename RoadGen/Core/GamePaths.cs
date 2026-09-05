using System;
using System.Collections.Generic;
using System.IO;

namespace RoadGen.Core;

/// <summary>The supported Source games whose install directory the user can point at.
/// Only the enum value is stored in settings; display name and materials path are derived.
/// Source 2 titles (CS2, Portal 2: CE) are intentionally absent — their textures are
/// .vtex_c resources that VTFLib cannot decode.</summary>
public enum GameId
{
    HalfLife2,
    EpisodeOne,
    EpisodeTwo,
    CounterStrikeSource,
    TeamFortress2,
    Portal,
    Portal2,
    Left4Dead2,
    MomentumMod,
    Custom
}

/// <summary>Maps a Source game to where its materials live under its install directory,
/// and back again from a chosen directory.</summary>
public static class GamePaths
{
    /// <summary>All games, in the order shown in the toolbar combo.</summary>
    public static readonly IReadOnlyList<GameId> All = new[]
    {
        GameId.HalfLife2,
        GameId.EpisodeOne,
        GameId.EpisodeTwo,
        GameId.CounterStrikeSource,
        GameId.TeamFortress2,
        GameId.Portal,
        GameId.Portal2,
        GameId.Left4Dead2,
        GameId.MomentumMod,
        GameId.Custom
    };

    public static string DisplayName(GameId game)
    {
        switch (game)
        {
            case GameId.HalfLife2: return "Half-Life 2";
            case GameId.EpisodeOne: return "Half-Life 2: Episode One";
            case GameId.EpisodeTwo: return "Half-Life 2: Episode Two";
            case GameId.CounterStrikeSource: return "Counter-Strike: Source";
            case GameId.TeamFortress2: return "Team Fortress 2";
            case GameId.Portal: return "Portal";
            case GameId.Portal2: return "Portal 2";
            case GameId.Left4Dead2: return "Left 4 Dead 2";
            case GameId.MomentumMod: return "Momentum Mod";
            case GameId.Custom: return "Custom...";
            default: return game.ToString();
        }
    }

    /// <summary>Relative "materials" path for a game, relative to its install directory.</summary>
    public static string MaterialsRelativePath(GameId game)
    {
        switch (game)
        {
            case GameId.HalfLife2: return Path.Combine("hl2", "materials");
            case GameId.EpisodeOne: return Path.Combine("episodic", "materials");
            case GameId.EpisodeTwo: return Path.Combine("ep2", "materials");
            case GameId.CounterStrikeSource: return Path.Combine("cstrike", "materials");
            case GameId.TeamFortress2: return Path.Combine("tf", "materials");
            case GameId.Portal: return Path.Combine("portal", "materials");
            case GameId.Portal2: return Path.Combine("portal2", "materials");
            case GameId.Left4Dead2: return Path.Combine("left4dead2", "materials");
            case GameId.MomentumMod: return Path.Combine("momentum", "materials");
            default: return "materials";
        }
    }

    /// <summary>Absolute materials root for a game install directory (or "" if no directory).</summary>
    public static string ResolveMaterialsRoot(string gameDirectory, GameId game)
    {
        if (string.IsNullOrEmpty(gameDirectory))
        {
            return "";
        }

        return Path.Combine(gameDirectory, MaterialsRelativePath(game));
    }

    /// <summary>All materials directories to search, in order: the selected game's own, then
    /// every other installed Source game under the same steamapps/common folder — matching how
    /// the engine/Hammer mounts content from each game's gameinfo.txt SearchPaths.</summary>
    public static List<string> ResolveMaterialRoots(string gameDirectory, GameId game)
    {
        var roots = new List<string>();
        if (string.IsNullOrEmpty(gameDirectory))
        {
            return roots;
        }

        // The selected game's own materials first.
        string own = ResolveMaterialsRoot(gameDirectory, game);
        if (!string.IsNullOrEmpty(own))
        {
            roots.Add(own);
        }

        string common = Path.GetDirectoryName(gameDirectory);
        if (string.IsNullOrEmpty(common) || !Directory.Exists(common))
        {
            return roots;
        }

        string selected = gameDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (string sibling in Directory.GetDirectories(common))
        {
            if (string.Equals(sibling.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    selected, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string gameInfoPath = Path.Combine(sibling, "gameinfo.txt");
            if (!File.Exists(gameInfoPath))
            {
                continue;
            }

            foreach (string content in ParseSearchPathGames(gameInfoPath))
            {
                string materials = Path.Combine(sibling, content, "materials");
                if (Directory.Exists(materials) && !roots.Contains(materials))
                {
                    roots.Add(materials);
                }
            }
        }

        return roots;
    }

    /// <summary>Extracts the "Game" content-folder names from a gameinfo.txt SearchPaths block.
    /// Keys and values may be quoted or bare; the "Game" keys repeat (e.g. tf, hl2), so all are kept.</summary>
    private static List<string> ParseSearchPathGames(string gameInfoPath)
    {
        var result = new List<string>();
        string text = File.ReadAllText(gameInfoPath);
        int search = text.IndexOf("\"SearchPaths\"", StringComparison.OrdinalIgnoreCase);
        if (search < 0)
        {
            return result;
        }

        int open = text.IndexOf('{', search);
        if (open < 0)
        {
            return result;
        }

        int depth = 1;
        int close = -1;
        for (int i = open + 1; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') { depth--; if (depth == 0) { close = i; break; } }
        }

        if (close < 0)
        {
            return result;
        }

        string block = text.Substring(open + 1, close - open - 1);

        // Walk the SearchPaths block as key/value pairs. gameinfo.txt files vary: keys and
        // values may be quoted ("Game" "tf") or bare (Game tf), so accept both. The "Game"
        // keys repeat (e.g. tf, hl2) and are what map to mounted content folders.
        int pos = 0;
        string key = null;
        while (pos < block.Length)
        {
            while (pos < block.Length && (block[pos] == ' ' || block[pos] == '\t' ||
                   block[pos] == '\r' || block[pos] == '\n'))
            {
                pos++;
            }

            if (pos >= block.Length)
            {
                break;
            }

            string token;
            if (block[pos] == '"')
            {
                int end = block.IndexOf('"', pos + 1);
                if (end < 0)
                {
                    break;
                }

                token = block.Substring(pos + 1, end - pos - 1);
                pos = end + 1;
            }
            else
            {
                int end = pos;
                while (end < block.Length && block[end] != ' ' && block[end] != '\t' &&
                       block[end] != '\r' && block[end] != '\n' && block[end] != '"' &&
                       block[end] != '{' && block[end] != '}')
                {
                    end++;
                }

                token = block.Substring(pos, end - pos);
                pos = end;
            }

            if (key == null)
            {
                key = token;
            }
            else
            {
                if (key.Equals("Game", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(token);
                }

                key = null;
            }
        }

        return result;
    }

    /// <summary>Finds the gameinfo.txt files for a game install: at the game root (classic
    /// layout) or in an immediate content subfolder (newer layout, e.g. tf/gameinfo.txt).</summary>
    private static IEnumerable<string> FindGameInfoFiles(string gameDir)
    {
        string root = Path.Combine(gameDir, "gameinfo.txt");
        if (File.Exists(root))
        {
            yield return root;
        }

        foreach (string sub in Directory.GetDirectories(gameDir))
        {
            string candidate = Path.Combine(sub, "gameinfo.txt");
            if (File.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>Resolves the content folders a gameinfo.txt mounts, as ABSOLUTE paths. Handles
    /// classic layouts (gameinfo.txt at the game root, bare "Game tf" entries) and newer
    /// layouts (gameinfo.txt inside the game's own content folder with typed entries like
    /// "game+game_write tf" or "game |all_source_engine_paths|hl2"). <paramref name="baseDir"/>
    /// is the game install root (where hl2.exe lives): relative paths resolve against it and
    /// |all_source_engine_paths| maps to it, while |gameinfo_path| maps to the gameinfo's folder.</summary>
    private static List<string> ResolveContentFolders(string gameInfoPath, string baseDir)
    {
        var result = new List<string>();
        string gameInfoDir = Path.GetDirectoryName(gameInfoPath) ?? baseDir;
        string text;
        try
        {
            text = File.ReadAllText(gameInfoPath);
        }
        catch (Exception)
        {
            return result;
        }

        // "SearchPaths" may be quoted or bare in gameinfo.txt; searching without quotes matches both.
        int search = text.IndexOf("SearchPaths", StringComparison.OrdinalIgnoreCase);
        if (search < 0)
        {
            return result;
        }

        int open = text.IndexOf('{', search);
        if (open < 0)
        {
            return result;
        }

        int depth = 1;
        int close = -1;
        for (int i = open + 1; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') { depth--; if (depth == 0) { close = i; break; } }
        }

        if (close < 0)
        {
            return result;
        }

        string block = text.Substring(open + 1, close - open - 1);

        // Walk the SearchPaths block as key/value pairs (keys and values may be quoted or
        // bare; "//" comments are skipped). Every value is a search path the engine mounts.
        int pos = 0;
        string key = null;
        while (pos < block.Length)
        {
            SkipWhitespaceAndComments(block, ref pos);
            if (pos >= block.Length)
            {
                break;
            }

            string token;
            if (block[pos] == '"')
            {
                int end = block.IndexOf('"', pos + 1);
                if (end < 0)
                {
                    break;
                }

                token = block.Substring(pos + 1, end - pos - 1);
                pos = end + 1;
            }
            else
            {
                int end = pos;
                while (end < block.Length && block[end] != ' ' && block[end] != '\t' &&
                       block[end] != '\r' && block[end] != '\n' && block[end] != '"' &&
                       block[end] != '{' && block[end] != '}')
                {
                    end++;
                }

                token = block.Substring(pos, end - pos);
                pos = end;
            }

            if (key == null)
            {
                key = token;
            }
            else
            {
                string folder = ResolveSearchPathValue(token, gameInfoDir, baseDir);
                if (folder != null && !ContainsPath(result, folder))
                {
                    result.Add(folder);
                }

                key = null;
            }
        }

        return result;
    }

    private static void SkipWhitespaceAndComments(string block, ref int pos)
    {
        while (pos < block.Length)
        {
            if (block[pos] == ' ' || block[pos] == '\t' || block[pos] == '\r' || block[pos] == '\n')
            {
                pos++;
                continue;
            }

            if (block[pos] == '/' && pos + 1 < block.Length && block[pos + 1] == '/')
            {
                int eol = block.IndexOf('\n', pos);
                pos = eol < 0 ? block.Length : eol + 1;
                continue;
            }

            break;
        }
    }

    /// <summary>Resolves one SearchPaths value to an absolute content folder path, or null for
    /// wildcards / empty values that can't map to a single folder.</summary>
    private static string ResolveSearchPathValue(string value, string gameInfoDir, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('*'))
        {
            return null; // wildcards like tf/custom/* can't be mapped to one folder
        }

        if (value.StartsWith("|gameinfo_path|", StringComparison.OrdinalIgnoreCase))
        {
            string rest = value.Substring("|gameinfo_path|".Length)
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '.');
            return rest.Length == 0 ? NormalizePath(gameInfoDir) : NormalizePath(Path.Combine(gameInfoDir, rest));
        }

        if (value.StartsWith("|all_source_engine_paths|", StringComparison.OrdinalIgnoreCase))
        {
            string rest = value.Substring("|all_source_engine_paths|".Length)
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return rest.Length == 0 ? NormalizePath(baseDir) : NormalizePath(Path.Combine(baseDir, rest));
        }

        string cleaned = value.Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '.');
        return cleaned.Length == 0 ? NormalizePath(baseDir) : NormalizePath(Path.Combine(baseDir, cleaned));
    }

    /// <summary>Best-effort detection of the game from a chosen install directory by looking
    /// for the game's marker subfolder. Returns <see cref="GameId.Custom"/> when unsure.</summary>
    public static GameId DetectGame(string gameDirectory)
    {
        if (string.IsNullOrEmpty(gameDirectory))
        {
            return GameId.Custom;
        }

        foreach (GameId game in All)
        {
            if (game == GameId.Custom)
            {
                continue;
            }

            string marker = MaterialsRelativePath(game).Split(Path.DirectorySeparatorChar)[0];
            if (Directory.Exists(Path.Combine(gameDirectory, marker)))
            {
                return game;
            }
        }

        return GameId.Custom;
    }

    public static int IndexOf(GameId game)
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i] == game)
            {
                return i;
            }
        }

        return -1;
    }

    public static bool TryParse(string name, out GameId game)
    {
        if (name != null && Enum.TryParse(name, true, out game))
        {
            return true;
        }

        game = GameId.Custom;
        return false;
    }

    /// <summary>All materials roots to search: every installed Source game (auto-discovered
    /// from the Steam libraries, like Hammer mounting all games), with the manual game
    /// directory (if any) taking precedence. No manual game directory is required.</summary>
    public static List<string> ResolveAllMaterialRoots(string manualGameDirectory, GameId game)
    {
        List<string> roots = AllInstalledMaterialRoots();

        if (!string.IsNullOrEmpty(manualGameDirectory))
        {
            List<string> manual = ResolveMaterialRoots(manualGameDirectory, game);
            manual.Reverse(); // preserve original order after Insert(0)
            foreach (string path in manual)
            {
                roots.Insert(0, path);
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(roots.Count);
        foreach (string path in roots)
        {
            if (seen.Add(path))
            {
                result.Add(path);
            }
        }

        return result;
    }

    /// <summary>Scans every Steam library for Source games and returns each game's materials
    /// directories, by resolving every mounted content folder from each game's gameinfo.txt
    /// (classic and modern layouts) and keeping those that contain a loose materials folder.</summary>
    public static List<string> AllInstalledMaterialRoots()
    {
        var roots = new List<string>();
        foreach (string steamApps in FindSteamAppsFolders())
        {
            string common = Path.Combine(steamApps, "common");
            if (!Directory.Exists(common))
            {
                continue;
            }

            foreach (string gameDir in Directory.GetDirectories(common))
            {
                foreach (string gameInfoPath in FindGameInfoFiles(gameDir))
                {
                    foreach (string contentFolder in ResolveContentFolders(gameInfoPath, gameDir))
                    {
                        string materials = Path.Combine(contentFolder, "materials");
                        if (Directory.Exists(materials) && !ContainsPath(roots, materials))
                        {
                            roots.Add(materials);
                        }
                    }
                }
            }
        }

        return roots;
    }

    /// <summary>Scans every Steam library for Source games and returns each game's CONTENT
    /// folders (absolute paths), mirroring what Hammer mounts from each gameinfo.txt
    /// SearchPath (see source_level_editor/src/public/filesystem_init.cpp). A content folder
    /// is mounted even when it has no loose "materials" folder — as long as it carries a
    /// "*_dir.vpk" archive (e.g. TF2's shared "hl2" content is VPK-only). Callers turn these
    /// into <see cref="RoadGen.Core.Vtf.ContentMount"/>s, which is how VPK-only content gets
    /// its textures discovered. Deduplicated, first-seen order preserved.</summary>
    public static List<string> AllInstalledContentFolders()
    {
        var folders = new List<string>();
        foreach (string steamApps in FindSteamAppsFolders())
        {
            string common = Path.Combine(steamApps, "common");
            if (!Directory.Exists(common))
            {
                continue;
            }

            foreach (string gameDir in Directory.GetDirectories(common))
            {
                foreach (string gameInfoPath in FindGameInfoFiles(gameDir))
                {
                    foreach (string contentFolder in ResolveContentFolders(gameInfoPath, gameDir))
                    {
                        if (!IsMountableContentFolder(contentFolder))
                        {
                            continue;
                        }

                        if (!ContainsPath(folders, contentFolder))
                        {
                            folders.Add(contentFolder);
                        }
                    }
                }
            }
        }

        return folders;
    }

    /// <summary>Every game install directory under every Steam library's "common" folder —
    /// the same crawl the content mounts use. Each Source game keeps its FGD files and the
    /// Hammer GameConfig.txt that references them under <c>&lt;game&gt;\bin</c>, so FGD
    /// discovery (see <see cref="FgdDiscovery"/>) walks this list to locate them.</summary>
    public static List<string> AllInstalledGameDirectories()
    {
        var dirs = new List<string>();
        foreach (string steamApps in FindSteamAppsFolders())
        {
            string common = Path.Combine(steamApps, "common");
            if (!Directory.Exists(common))
            {
                continue;
            }

            foreach (string gameDir in Directory.GetDirectories(common))
            {
                if (!ContainsPath(dirs, gameDir))
                {
                    dirs.Add(gameDir);
                }
            }
        }

        return dirs;
    }

    /// <summary>True when a content folder exists and can serve materials: it has a loose
    /// "materials" folder and/or at least one "*_dir.vpk" archive (whose keys are
    /// content-relative, so its packed materials belong to this content folder).</summary>
    private static bool IsMountableContentFolder(string contentFolder)
    {
        if (!Directory.Exists(contentFolder))
        {
            return false;
        }

        if (Directory.Exists(Path.Combine(contentFolder, "materials")))
        {
            return true;
        }

        try
        {
            return Directory.GetFiles(contentFolder, "*_dir.vpk").Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Finds every steamapps folder: the main Steam install plus each library root
    /// listed in libraryfolders.vdf.</summary>
    private static List<string> FindSteamAppsFolders()
    {
        var result = new List<string>();
        string steamRoot = FindSteamRoot();
        if (steamRoot == null)
        {
            return result;
        }

        AddSteamApps(result, Path.Combine(steamRoot, "steamapps"));

        string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (string library in ParseLibraryFolders(vdf))
            {
                AddSteamApps(result, Path.Combine(library, "steamapps"));
            }
        }

        return result;
    }

    private static void AddSteamApps(List<string> result, string path)
    {
        string normalized = NormalizePath(path);
        if (Directory.Exists(normalized) && !ContainsPath(result, normalized))
        {
            result.Add(normalized);
        }
    }

    /// <summary>Normalizes separators and trailing slashes so registry and VDF paths compare
    /// equal even when one uses forward slashes / different case.</summary>
    private static string NormalizePath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool ContainsPath(List<string> paths, string path)
    {
        foreach (string p in paths)
        {
            if (string.Equals(NormalizePath(p), NormalizePath(path), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FindSteamRoot()
    {
        try
        {
            using (Microsoft.Win32.RegistryKey key =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                string path = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(Path.Combine(path, "steamapps")))
                {
                    return path;
                }
            }
        }
        catch (Exception)
        {
            // Registry unavailable; fall through to a drive scan.
        }

        // Steam can live on any drive and in several folder names; scan every fixed drive
        // for a folder that contains steamapps.
        string[] candidates = new[]
        {
            @"Program Files (x86)\Steam",
            @"Program Files\Steam",
            @"Steam",
            @"SteamLibrary"
        };
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
            {
                continue;
            }

            foreach (string candidate in candidates)
            {
                string root = Path.Combine(drive.Name, candidate);
                if (Directory.Exists(Path.Combine(root, "steamapps")))
                {
                    return root;
                }
            }
        }

        return null;
    }

    /// <summary>Reads the "path" values from libraryfolders.vdf (each is a Steam library root).</summary>
    private static List<string> ParseLibraryFolders(string vdfPath)
    {
        var result = new List<string>();
        string text = File.ReadAllText(vdfPath);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '"')
            {
                continue;
            }

            int keyEnd = text.IndexOf('"', i + 1);
            if (keyEnd < 0) break;
            string key = text.Substring(i + 1, keyEnd - i - 1);
            if (!key.Equals("path", StringComparison.OrdinalIgnoreCase))
            {
                i = keyEnd;
                continue;
            }

            int valueStart = keyEnd + 1;
            while (valueStart < text.Length && text[valueStart] != '"') valueStart++;
            int valueEnd = valueStart < text.Length ? text.IndexOf('"', valueStart + 1) : -1;
            if (valueEnd < 0) break;
            // VDF escapes backslashes as "\\", so unescape to a real Windows path.
            result.Add(text.Substring(valueStart + 1, valueEnd - valueStart - 1).Replace("\\\\", "\\"));
            i = valueEnd;
        }

        return result;
    }

    /// <summary>Returns a multi-line human-readable report of the auto-discovery pipeline, for
    /// diagnosing why no games were found on a particular machine. Lists EVERY folder under
    /// each common directory so a missing gameinfo.txt or empty library is visible.</summary>
    public static string DiscoveryReport()
    {
        var sb = new System.Text.StringBuilder();
        string steamRoot = FindSteamRoot();
        sb.AppendLine(steamRoot == null ? "Steam root: NOT FOUND" : "Steam root: " + steamRoot);

        List<string> steamApps = FindSteamAppsFolders();
        sb.AppendLine("steamapps folders: " + (steamApps.Count == 0 ? "none" : string.Join(" ; ", steamApps)));

        var mounts = new List<string>();
        foreach (string steamAppsFolder in steamApps)
        {
            string common = Path.Combine(steamAppsFolder, "common");
            if (!Directory.Exists(common))
            {
                sb.AppendLine("no common folder: " + common);
                continue;
            }

            string[] gameDirs = Directory.GetDirectories(common);
            sb.AppendLine("common (" + gameDirs.Length + " folder(s)): " + common);
            foreach (string gameDir in gameDirs)
            {
                List<string> gameInfoFiles = new List<string>(FindGameInfoFiles(gameDir));
                if (gameInfoFiles.Count == 0)
                {
                    sb.AppendLine("  " + Path.GetFileName(gameDir) + ": no gameinfo.txt");
                    continue;
                }

                foreach (string gameInfoPath in gameInfoFiles)
                {
                    List<string> folders = ResolveContentFolders(gameInfoPath, gameDir);
                    List<string> names = folders.ConvertAll(f => Path.GetFileName(f) ?? f);
                    sb.AppendLine("  " + Path.GetFileName(gameDir) + " (" +
                        Path.GetFileName(Path.GetDirectoryName(gameInfoPath)) + "): content = [" +
                        string.Join(", ", names) + "]");
                    foreach (string folder in folders)
                    {
                        string materials = Path.Combine(folder, "materials");
                        bool loose = Directory.Exists(materials);
                        List<string> vpks = ListDirVpks(folder);
                        bool mountable = loose || vpks.Count > 0;
                        sb.AppendLine("      " + (Path.GetFileName(folder) ?? folder) + ": " +
                            (mountable ? "MOUNTED" : "skipped") +
                            (loose ? " (loose materials)" : "") +
                            (vpks.Count > 0 ? " (" + vpks.Count + " VPK)" : "") +
                            (mountable ? "" : " — no materials folder or *_dir.vpk"));
                        if (loose)
                        {
                            sb.AppendLine("          materials: " + materials);
                        }

                        foreach (string vpk in vpks)
                        {
                            sb.AppendLine("          vpk: " + vpk);
                        }

                        if (mountable && !ContainsPath(mounts, folder))
                        {
                            mounts.Add(folder);
                        }
                    }
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("content mounts (" + mounts.Count + "): " +
            (mounts.Count == 0 ? "none" : string.Join(" ; ", mounts)));
        return sb.ToString();
    }

    private static List<string> ListDirVpks(string contentFolder)
    {
        try
        {
            return new List<string>(Directory.GetFiles(contentFolder, "*_dir.vpk"));
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }
}
