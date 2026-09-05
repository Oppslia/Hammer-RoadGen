using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RoadGen.Core;

/// <summary>Discovers every FGD file Hammer would load for each installed Source game and
/// unions their <c>@MaterialExclusion</c> directories into one exclusion table, also folding
/// in the per-game <c>-MaterialExcludeDirN</c> lists from each GameConfig.txt Hammer block.
///
/// Hammer hides a material when its name starts with an excluded directory; the check runs
/// against BOTH configured sources (CMaterial::ShouldSkipMaterial, Material.cpp ~668-697):
///  (1) <c>g_pGameConfig->m_MaterialExclusions</c> — the per-game list in each GameConfig.txt
///      "Hammer" block (MaterialExcludeCount / -MaterialExcludeDirN, gameconfig.cpp:295-302),
///      and
///  (2) <c>pGD->m_FGDMaterialExclusions</c> — each loaded .fgd's <c>@MaterialExclusion [...]</c>
///      block (GameData::LoadFGDMaterialExclusions, fgdlib/gamedata.cpp:868).
/// RoadGen mounts every installed game's content at once, so instead of only the one active
/// config Hammer would use, it gathers every game's exclusions together.
///
/// Discovery mirrors how Hammer finds the FGDs:
///  - a game's config lives next to hammer.exe, i.e. <c>&lt;game&gt;\bin\GameConfig.txt</c>
///    (ConfigManager.cpp GAME_CONFIG_FILENAME / LoadConfigsInternal);
///  - that file's Hammer block names the FGDs under GameData0..N (AddDefaultConfig writes
///    GameData0 = &lt;bin&gt;\&lt;fgd&gt;);
///  - each FGD is tokenized like Hammer's TokenReader: <c>@include</c> is followed relative
///    to the FGD's own folder first (gamedata.cpp Load), and <c>@MaterialExclusion</c> tokens
///    up to the closing <c>]</c> are collected (quoted or bare, <c>//</c> comments skipped).
/// </summary>
public static class FgdDiscovery
{
    /// <summary>Scans every installed game directory (the same Steam-library crawl the content
    /// mounts use, see <see cref="GamePaths.AllInstalledGameDirectories"/>) and returns the
    /// union of all FGD-defined + GameConfig-defined material exclusion directories, trimmed,
    /// deduplicated and sorted. Returns an empty list when no Source FGDs are discoverable
    /// (the caller keeps its shipped default list in that case).</summary>
    public static List<string> DiscoverMaterialExclusions()
    {
        var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedFgds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string gameDir in GamePaths.AllInstalledGameDirectories())
        {
            string bin = Path.Combine(gameDir, "bin");
            if (!Directory.Exists(bin))
            {
                continue;
            }

            // Primary route, exactly how Hammer learns its FGDs: GameData0..N inside each
            // Hammer block of <game>\bin\GameConfig.txt. The same pass also picks up that
            // block's -MaterialExcludeDirN entries (the per-game exclusion list).
            string gameConfig = Path.Combine(bin, "GameConfig.txt");
            if (File.Exists(gameConfig))
            {
                ReadGameConfig(gameConfig, visitedFgds, exclusions);
            }

            // Safety net for installs that carry FGDs in bin but no (or an incomplete)
            // GameConfig.txt: every *.fgd sitting next to the engine binaries is loaded too.
            // The visited set dedupes against what GameDataN already pulled in.
            foreach (string fgd in SafeEnumerateFiles(bin, "*.fgd"))
            {
                LoadFgd(fgd, exclusions, visitedFgds);
            }
        }

        var result = new List<string>(exclusions);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    // ---------------------------------------------------------------- GameConfig.txt

    /// <summary>Parses one GameConfig.txt (a nested KeyValues file: block names and key/value
    /// pairs are quoted). For every pair that sits inside a "Hammer" block, a GameData&lt;N&gt;
    /// value is treated as an FGD to load (the file may list many game configs — all are
    /// gathered) and a -MaterialExcludeDir&lt;N&gt; value is added straight to
    /// <paramref name="exclusions"/>.</summary>
    private static void ReadGameConfig(string gameConfigPath, HashSet<string> visitedFgds,
        HashSet<string> exclusions)
    {
        string text;
        try
        {
            text = File.ReadAllText(gameConfigPath);
        }
        catch (Exception)
        {
            return;
        }

        List<Tok> tokens = Tokenize(text);
        string configDir = Path.GetDirectoryName(gameConfigPath) ?? "";
        var stack = new List<string>();
        int i = 0;
        while (i < tokens.Count)
        {
            Tok t = tokens[i];
            if (t.Kind == TokKind.Symbol)
            {
                if (t.Text == "}")
                {
                    if (stack.Count > 0)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }
                }

                i++;
                continue;
            }

            // A block opens when a quoted/word name is immediately followed by '{'.
            Tok next = (i + 1 < tokens.Count) ? tokens[i + 1] : default;
            if (next.Kind == TokKind.Symbol && next.Text == "{")
            {
                stack.Add(t.Text);
                i += 2;
                continue;
            }

            // Otherwise a name followed by a value is a key/value pair. Only Hammer blocks
            // carry the keys we care about.
            if (next.Kind == TokKind.Quoted || next.Kind == TokKind.Word)
            {
                if (IsUnderHammer(stack))
                {
                    string key = t.Text;
                    if (key.StartsWith("GameData", StringComparison.OrdinalIgnoreCase)
                        && IsAllDigits(key.Substring("GameData".Length)))
                    {
                        LoadFgd(ResolvePath(next.Text, configDir), exclusions, visitedFgds);
                    }
                    else if (key.StartsWith("-MaterialExcludeDir", StringComparison.OrdinalIgnoreCase))
                    {
                        AddExclusion(exclusions, next.Text);
                    }
                }

                i += 2;
                continue;
            }

            i++;
        }
    }

    private static bool IsUnderHammer(List<string> stack)
    {
        foreach (string name in stack)
        {
            if (string.Equals(name, "Hammer", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAllDigits(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>FGD paths from GameConfig.txt are written as absolute paths by Hammer. If one
    /// ever comes through relative, resolve it against the GameConfig.txt's folder.</summary>
    private static string ResolvePath(string value, string baseDir)
    {
        string p = value.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(p) ? p : Path.Combine(baseDir, p);
    }

    // ---------------------------------------------------------------- FGD files

    /// <summary>Loads one FGD: tokenizes it like Hammer's TokenReader and reacts to
    /// <c>@MaterialExclusion [...]</c> (collect entries) and <c>@include "file"</c> (recurse
    /// into the included FGD, resolved relative to this FGD's folder). <paramref name="visited"/>
    /// dedupes files loaded multiple times (garrysmod.fgd includes base.fgd, halflife2.fgd
    /// includes base.fgd again) and breaks include cycles. All other sections are skipped
    /// naturally — the scan only acts when it sees a standalone '@' token.</summary>
    private static void LoadFgd(string fgdPath, HashSet<string> exclusions,
        HashSet<string> visited)
    {
        string normalized = NormalizePath(fgdPath);
        if (!File.Exists(normalized) || !visited.Add(normalized))
        {
            return;
        }

        string text;
        try
        {
            text = File.ReadAllText(normalized);
        }
        catch (Exception)
        {
            return;
        }

        string dir = Path.GetDirectoryName(normalized) ?? "";
        List<Tok> tokens = Tokenize(text);
        int i = 0;
        while (i < tokens.Count)
        {
            Tok t = tokens[i];
            if (t.Kind != TokKind.Symbol || t.Text != "@")
            {
                i++;
                continue;
            }

            // The section name immediately follows the '@' (an identifier, e.g. include,
            // materialexclusion, pointclass, ...). Everything else is ignored.
            Tok name = (i + 1 < tokens.Count) ? tokens[i + 1] : default;
            if (name.Kind != TokKind.Word)
            {
                i += 2;
                continue;
            }

            if (string.Equals(name.Text, "include", StringComparison.OrdinalIgnoreCase))
            {
                // @include "base.fgd" — resolve against this FGD's folder (Hammer tries the
                // including file's path first, then falls back to its start directory).
                Tok fileTok = (i + 2 < tokens.Count) ? tokens[i + 2] : default;
                if (fileTok.Kind == TokKind.Quoted || fileTok.Kind == TokKind.Word)
                {
                    string include = ResolvePath(fileTok.Text, dir);
                    if (!File.Exists(include))
                    {
                        include = fileTok.Text.Replace('/', Path.DirectorySeparatorChar);
                    }

                    LoadFgd(include, exclusions, visited);
                }

                i += 3;
                continue;
            }

            if (string.Equals(name.Text, "materialexclusion", StringComparison.OrdinalIgnoreCase))
            {
                i = CollectExclusionBlock(tokens, i + 2, exclusions);
                continue;
            }

            i += 2;
        }
    }

    /// <summary>Collects the entries of one <c>@MaterialExclusion [ ... ]</c> block, mirroring
    /// LoadFGDMaterialExclusions: entries are quoted or bare string tokens read until the
    /// closing ']' (a directory with a space, like "environment maps", must be quoted — a bare
    /// multi-word line would be split into separate entries, exactly as Hammer would). Returns
    /// the index just past the block so scanning can resume.</summary>
    private static int CollectExclusionBlock(List<Tok> tokens, int start, HashSet<string> exclusions)
    {
        int i = start;
        // Hammer expects the '[' to be the very next token after the section name.
        if (i < tokens.Count && tokens[i].Kind == TokKind.Symbol && tokens[i].Text == "[")
        {
            i++;
        }

        for (; i < tokens.Count; i++)
        {
            Tok t = tokens[i];
            if (t.Kind == TokKind.Symbol)
            {
                if (t.Text == "]")
                {
                    return i + 1;
                }

                continue;
            }

            if (t.Kind == TokKind.Quoted || t.Kind == TokKind.Word)
            {
                AddExclusion(exclusions, t.Text);
            }
        }

        return tokens.Count;
    }

    private static void AddExclusion(HashSet<string> exclusions, string raw)
    {
        string dir = raw.Trim().Trim('/').Trim('\\');
        if (dir.Length > 0)
        {
            exclusions.Add(dir);
        }
    }

    // ---------------------------------------------------------------- shared tokenizer

    private enum TokKind
    {
        Quoted,
        Word,
        Symbol,
    }

    private readonly struct Tok
    {
        public readonly TokKind Kind;
        public readonly string Text;

        public Tok(TokKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }
    }

    /// <summary>Splits Valve text (FGD / GameConfig.txt / KeyValues) into tokens the way
    /// Hammer's TokenReader does: whitespace separates, "..." is one quoted token, // comments
    /// run to end of line (/* ... */ also skipped), and every other run of punctuation is one
    /// symbol token ('@', '[', ']', '{', '}', ...). Word characters are letters, digits and
    /// underscore — so "@MaterialExclusion" becomes Symbol('@') + Word("MaterialExclusion"),
    /// and "@include" Symbol('@') + Word("include").</summary>
    private static List<Tok> Tokenize(string text)
    {
        var result = new List<Tok>();
        int i = 0;
        int len = text.Length;
        while (i < len)
        {
            char c = text[i];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < len && text[i + 1] == '/')
            {
                int eol = text.IndexOf('\n', i);
                i = eol < 0 ? len : eol + 1;
                continue;
            }

            if (c == '/' && i + 1 < len && text[i + 1] == '*')
            {
                int close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? len : close + 2;
                continue;
            }

            if (c == '"')
            {
                int end = i + 1;
                while (end < len && text[end] != '"')
                {
                    // A backslash-escaped quote is unusual in FGD/GameConfig but harmless to
                    // honor when present.
                    if (text[end] == '\\' && end + 1 < len)
                    {
                        end++;
                    }

                    end++;
                }

                result.Add(new Tok(TokKind.Quoted, text.Substring(i + 1, end - i - 1)));
                i = end < len ? end + 1 : len;
                continue;
            }

            if (IsWordChar(c))
            {
                int end = i + 1;
                while (end < len && IsWordChar(text[end]))
                {
                    end++;
                }

                result.Add(new Tok(TokKind.Word, text.Substring(i, end - i)));
                i = end;
                continue;
            }

            // Any other character is a one-character symbol (operators/punctuation).
            result.Add(new Tok(TokKind.Symbol, c.ToString()));
            i++;
        }

        return result;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string NormalizePath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static IEnumerable<string> SafeEnumerateFiles(string dir, string pattern)
    {
        try
        {
            return Directory.GetFiles(dir, pattern);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Runs the same scan and formats a short diagnostics report: every game directory
    /// that has a bin, whether that bin has a GameConfig.txt, the loose *.fgd files found there
    /// (the route that catches CSS/Portal/Portal2/L4D2 authoring tools), and the final exclusion
    /// table. Appended to %AppData%\RoadGen\discovery-report.txt at startup so a freshly
    /// installed set of authoring tools can be confirmed as picked up on the next launch.</summary>
    public static string DiscoveryReport()
    {
        List<string> exclusions = DiscoverMaterialExclusions();
        var sb = new StringBuilder();
        sb.AppendLine("FGD material-exclusion discovery");
        sb.AppendLine("=================================");
        foreach (string gameDir in GamePaths.AllInstalledGameDirectories())
        {
            string bin = Path.Combine(gameDir, "bin");
            if (!Directory.Exists(bin))
            {
                sb.AppendLine("  " + gameDir + "  (no bin - skipped)");
                continue;
            }

            sb.AppendLine("  " + gameDir);
            sb.AppendLine("    bin\\GameConfig.txt: " + (File.Exists(Path.Combine(bin, "GameConfig.txt")) ? "yes" : "no"));
            var names = new List<string>();
            foreach (string fgd in SafeEnumerateFiles(bin, "*.fgd"))
            {
                names.Add(Path.GetFileName(fgd) ?? fgd);
            }

            sb.AppendLine("    bin\\*.fgd (" + names.Count + "): " +
                (names.Count == 0 ? "(none)" : string.Join(", ", names)));
        }

        sb.AppendLine();
        if (exclusions.Count == 0)
        {
            sb.AppendLine("No @MaterialExclusion entries found in any FGD - RoadGen is using its built-in tf.fgd default.");
        }
        else
        {
            sb.AppendLine("@MaterialExclusion table (" + exclusions.Count + "): " + string.Join(", ", exclusions));
        }

        return sb.ToString();
    }
}
