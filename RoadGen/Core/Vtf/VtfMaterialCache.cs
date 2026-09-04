using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace RoadGen.Core.Vtf;

/// <summary>Resolves a material name (e.g. "concrete/concretefloor005a") to a decoded
/// texture bitmap. Results are cached; a checkerboard fallback is returned when the
/// texture is missing or fails to decode. Backed by an ordered list of
/// <see cref="ContentMount"/>s (one per mounted Source content folder) whose loose files
/// and *_dir.vpk archives are searched in priority order — the same model Hammer uses, and
/// the same mounts a material browser would walk.</summary>
public sealed class VtfMaterialCache
{
    private readonly Dictionary<string, Bitmap> _cache =
        new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
    private readonly List<ContentMount> _mounts = new List<ContentMount>();

    // Material path -> why it could not be loaded. Only filled for materials actually
    // requested that fell back to the checkerboard (each distinct material recorded once).
    private readonly Dictionary<string, string> _missing =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private Bitmap _fallback;

    /// <summary>Mounted content folders in search order. Higher-priority content (a game's own
    /// "tf") is searched before shared content ("hl2"), like Hammer's mounted search paths.</summary>
    public IReadOnlyList<ContentMount> Mounts => _mounts;

    /// <summary>The materials folder of each mounted content, for display/debugging.</summary>
    public IReadOnlyList<string> SearchPaths
    {
        get
        {
            var paths = new List<string>(_mounts.Count);
            foreach (ContentMount mount in _mounts)
            {
                paths.Add(mount.MaterialsPath);
            }

            return paths;
        }
    }

    /// <summary>Primary materials root (first mount), or "" when nothing is mounted.</summary>
    public string MaterialsRoot => _mounts.Count > 0 ? _mounts[0].MaterialsPath : "";

    /// <summary>Raised the first time a requested material is missing (falls back to the
    /// checkerboard), carrying the material path. Lets the UI surface a missing-materials
    /// report / refresh a status label without polling.</summary>
    public event Action<string> MissingMaterialFound;

    /// <summary>Replaces the mounted content folders (disposing the old mounts' VPK streams),
    /// re-discovers each folder's *_dir.vpk archives, and drops cached textures and the
    /// missing-material log. Content folders that carry nothing are skipped.</summary>
    public void SetContentRoots(IEnumerable<string> contentFolders)
    {
        foreach (ContentMount mount in _mounts)
        {
            mount.Dispose();
        }

        _mounts.Clear();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (contentFolders != null)
        {
            foreach (string folder in contentFolders)
            {
                if (string.IsNullOrWhiteSpace(folder))
                {
                    continue;
                }

                string normalized = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!seen.Add(normalized))
                {
                    continue;
                }

                ContentMount mount = new ContentMount(normalized);
                if (mount.IsMountable)
                {
                    _mounts.Add(mount);
                }
                else
                {
                    mount.Dispose();
                }
            }
        }

        ClearCache();
        _missing.Clear();
    }

    /// <summary>True when <paramref name="texture"/> is the missing-texture fallback.</summary>
    public bool IsFallback(Bitmap texture) => ReferenceEquals(texture, _fallback);

    /// <summary>Number of distinct requested materials that fell back to the checkerboard.</summary>
    public int MissingMaterialCount => _missing.Count;

    /// <summary>The missing-material log: material path -> why it could not be loaded.
    /// Intended for a missing-materials report (and as groundwork for a material browser).</summary>
    public IReadOnlyDictionary<string, string> MissingMaterials => _missing;

    /// <summary>Gets (and caches) the texture for a material path. Never returns null —
    /// falls back to a checkerboard and records the miss (with a reason) once.</summary>
    public Bitmap GetMaterialBitmap(string materialPath)
    {
        if (string.IsNullOrWhiteSpace(materialPath))
        {
            return GetFallback();
        }

        if (_cache.TryGetValue(materialPath, out Bitmap cached))
        {
            return cached;
        }

        Bitmap loaded;
        string missReason;
        if (!TryLoad(materialPath, out loaded, out missReason))
        {
            loaded = GetFallback();
            if (!_missing.ContainsKey(materialPath))
            {
                _missing[materialPath] = missReason;
                MissingMaterialFound?.Invoke(materialPath);
            }
        }

        _cache[materialPath] = loaded;
        return loaded;
    }

    /// <summary>Loads a material the way Hammer does: find the material's .vmt across the
    /// mounted content folders/VPKs, read its $basetexture (which may point anywhere under
    /// materials/, never assumed to match the material's name or folder), then resolve that
    /// texture path across the same mounts. Only when no .vmt exists at all do we fall back
    /// to a texture named exactly like the material (the engine's implicit-material case).</summary>
    private bool TryLoad(string materialPath, out Bitmap bitmap, out string missReason)
    {
        bitmap = null;
        missReason = null;

        // Hammer material paths are lowercase, use forward slashes and have no extension.
        string relative = materialPath.Trim().TrimStart('/').Replace('\\', '/');

        byte[] vmt = TryFindBytes(relative + ".vmt");
        string textureRelative = relative;
        bool redirected = false;
        if (vmt != null)
        {
            string basetexture = ParseVmtBaseTexture(vmt);
            if (!string.IsNullOrWhiteSpace(basetexture))
            {
                // $basetexture can redirect anywhere (e.g. cs_havana/ground01grass.vmt ->
                // de_aztec/ground01grass), like Hammer reading the parsed material var.
                textureRelative = basetexture.Trim().Replace('\\', '/').TrimStart('/');
                redirected = true;
            }
            // No $basetexture key? Hammer's GetPreviewImageName falls back to the material
            // name (pMaterial->GetName()), so textureRelative stays == relative.
        }

        byte[] bytes = TryFindBytes(textureRelative + ".vtf");
        if (bytes == null)
        {
            missReason = redirected
                ? "'" + relative + ".vmt' -> $basetexture '" + textureRelative + "' not found in any mounted folder/VPK"
                : vmt != null
                    ? "'" + relative + ".vmt' found but its '" + textureRelative + ".vtf' is not in any mounted folder/VPK"
                    : "no '" + relative + ".vmt' or '.vtf' in any mounted folder/VPK";
            return false;
        }

        bitmap = Decode(bytes);
        if (bitmap == null)
        {
            missReason = "'" + textureRelative + "' found but failed to decode";
            return false;
        }

        return true;
    }

    private static Bitmap Decode(byte[] bytes)
    {
        try
        {
            return VtfTextureLoader.LoadFromBytes(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Finds raw file bytes by a path relative to a content root's "materials"
    /// folder (e.g. "concrete/concretewall074c.vtf"): asks each mount in priority order. The
    /// key is prefixed with "materials/" so it matches both loose files (below
    /// "&lt;content&gt;/materials") and VPK entries (whose keys are content-relative too).</summary>
    private byte[] TryFindBytes(string relative)
    {
        string contentRelative = "materials/" + relative;
        foreach (ContentMount mount in _mounts)
        {
            byte[] data = mount.TryRead(contentRelative);
            if (data != null)
            {
                return data;
            }
        }

        return null;
    }

    /// <summary>Renders the missing-material log as human-readable text, including the
    /// mounted content folders so leftover checkerboard faces can be diagnosed.</summary>
    public string MissingMaterialsReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Missing materials (" + _missing.Count + "):");
        if (_missing.Count == 0)
        {
            sb.AppendLine("  none — every requested material resolved");
        }
        else
        {
            foreach (KeyValuePair<string, string> pair in _missing)
            {
                sb.AppendLine("  " + pair.Key + "  ->  " + pair.Value);
            }
        }

        sb.AppendLine();
        sb.AppendLine("Mounted content folders (" + _mounts.Count + "):");
        if (_mounts.Count == 0)
        {
            sb.AppendLine("  none");
        }
        else
        {
            foreach (ContentMount mount in _mounts)
            {
                sb.AppendLine("  " + mount.Describe());
            }
        }

        return sb.ToString();
    }

    private void ClearCache()
    {
        foreach (Bitmap bitmap in _cache.Values)
        {
            bitmap.Dispose();
        }

        _cache.Clear();
        _fallback?.Dispose();
        _fallback = null;
    }

    /// <summary>Reads the value of the $basetexture (or $baseTexture) key from a .vmt's
    /// bytes, e.g. "de_aztec/ground01grass" from `"$baseTexture" "de_aztec/ground01grass"`
    /// OR the bare-key form `$basetexture "metal/metalwall048c"` (Valve .vmts mix quoted
    /// keys, bare keys, and bare unquoted values). Returns null when the key is absent.
    /// Mirrors what the engine's material system exposes via FindVar("$baseTexture").</summary>
    private static string ParseVmtBaseTexture(byte[] vmtBytes)
    {
        try
        {
            string text = System.Text.Encoding.ASCII.GetString(vmtBytes);
            int i = 0;
            while (i < text.Length)
            {
                int dollar = text.IndexOf('$', i);
                if (dollar < 0)
                {
                    return null;
                }

                int identEnd = dollar + 1;
                while (identEnd < text.Length &&
                       (char.IsLetterOrDigit(text[identEnd]) || text[identEnd] == '_'))
                {
                    identEnd++;
                }

                string key = text.Substring(dollar + 1, identEnd - dollar - 1);
                if (!key.Equals("basetexture", StringComparison.OrdinalIgnoreCase))
                {
                    i = identEnd;
                    continue;
                }

                // A QUOTED key ("$basetexture") has its closing quote immediately after the
                // identifier, with no whitespace; a BARE key ($basetexture) does not. Check
                // the character right at identEnd to tell them apart, then skip whitespace to
                // reach the value (quoted or bare). Skipping whitespace BEFORE this check is
                // wrong: for a bare key it lands on the value's OPENING quote and mistakes it
                // for a key-closing quote, so the quoted value gets read as a bare token that
                // swallows its trailing quote.
                int p = identEnd;
                if (p < text.Length && text[p] == '"')
                {
                    p++; // the quoted key's own closing quote
                }

                p = SkipVmtWhitespace(text, p);

                if (p >= text.Length)
                {
                    return null;
                }

                if (text[p] == '"')
                {
                    // Quoted value, e.g. "metal/metalwall048c".
                    int valueEnd = text.IndexOf('"', p + 1);
                    if (valueEnd < 0)
                    {
                        return null;
                    }

                    return text.Substring(p + 1, valueEnd - p - 1).Trim();
                }

                // Bare (unquoted) value: read to whitespace or a closing brace.
                int bareEnd = p;
                while (bareEnd < text.Length && text[bareEnd] != ' ' && text[bareEnd] != '\t' &&
                       text[bareEnd] != '\r' && text[bareEnd] != '\n' && text[bareEnd] != '}')
                {
                    bareEnd++;
                }

                return text.Substring(p, bareEnd - p).Trim();
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    private static int SkipVmtWhitespace(string text, int start)
    {
        int p = start;
        while (p < text.Length && (text[p] == ' ' || text[p] == '\t' ||
               text[p] == '\r' || text[p] == '\n'))
        {
            p++;
        }

        return p;
    }

    private Bitmap GetFallback()
    {
        if (_fallback == null)
        {
            _fallback = BuildCheckerboard();
        }

        return _fallback;
    }

    /// <summary>Magenta/black checkerboard, the classic "missing texture" indicator. Sized as a
    /// single 2x2 period so one texture repeat = one checker cell in the world.</summary>
    private static Bitmap BuildCheckerboard()
    {
        const int cell = 8;
        const int size = 16;
        Bitmap bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            for (int y = 0; y < size / cell; y++)
            {
                for (int x = 0; x < size / cell; x++)
                {
                    using Brush brush = new SolidBrush((x + y) % 2 == 0 ? Color.Magenta : Color.Black);
                    g.FillRectangle(brush, x * cell, y * cell, cell, cell);
                }
            }
        }

        return bitmap;
    }
}
