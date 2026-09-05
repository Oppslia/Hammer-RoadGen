using System;
using System.Collections.Generic;
using System.IO;

namespace RoadGen.Core.Vtf;

/// <summary>
/// One mounted Source content folder — the unit the engine (and Hammer, via
/// source_level_editor/src/public/filesystem_init.cpp) mounts for every gameinfo.txt
/// SearchPath. A content folder is its loose files on disk PLUS every "*_dir.vpk" (and
/// the chunk files those directory files reference) sitting beside it, e.g. TF2's "tf"
/// folder carries tf2_misc_dir.vpk / tf2_textures_dir.vpk and its shared "hl2" content
/// carries hl2_textures_dir.vpk with NO loose "materials" folder at all.
///
/// Both are addressed by the SAME content-relative path (e.g. "materials/concrete/
/// concretewall074c.vtf") because VPK entry keys are already stored relative to the
/// content root, so one unified lookup covers loose files and packed files — exactly like
/// Hammer's search paths. This is the reusable primitive a future material browser walks
/// (TryRead / EnumerateFileKeys) instead of re-implementing mount logic.
/// </summary>
public sealed class ContentMount : IDisposable
{
    private readonly string _contentPath;
    private readonly List<VpkArchive> _vpks = new List<VpkArchive>();
    private int? _looseFileCount;

    /// <summary>Creates a mount for one content folder and discovers its "*_dir.vpk" archives.
    /// Never throws for a missing folder; such a mount simply carries nothing.</summary>
    public ContentMount(string contentPath)
    {
        _contentPath = contentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        DiscoverVpks();
    }

    /// <summary>Absolute content folder path, e.g. "...\steamapps\common\Team Fortress 2\tf".</summary>
    public string ContentPath => _contentPath;

    /// <summary>Display name of the content folder (e.g. "tf", "hl2").</summary>
    public string Name => Path.GetFileName(_contentPath);

    /// <summary>"&lt;content&gt;/materials". May NOT exist as a loose folder when the content's
    /// materials are only packed in a VPK (e.g. TF2's shared "hl2" content).</summary>
    public string MaterialsPath => Path.Combine(_contentPath, "materials");

    /// <summary>True when the loose "materials" folder exists on disk.</summary>
    public bool HasLooseMaterials => Directory.Exists(MaterialsPath);

    /// <summary>The "*_dir.vpk" archives discovered in this content folder.</summary>
    public IReadOnlyList<VpkArchive> Vpks => _vpks;

    /// <summary>True when the content folder exists on disk and carries something (a loose
    /// materials folder and/or at least one VPK archive) worth mounting.</summary>
    public bool IsMountable => Directory.Exists(_contentPath) && (HasLooseMaterials || _vpks.Count > 0);

    /// <summary>Total files exposed for material resolution: loose files under "materials/"
    /// plus every VPK's entries.</summary>
    public int FileCount => LooseFileCount + VpkFileCount;

    /// <summary>Number of loose files under "&lt;content&gt;/materials" (the only subtree the
    /// material resolver reads loose files from). Computed lazily so mounting a game folder
    /// never forces a full recursive walk up front.</summary>
    public int LooseFileCount
    {
        get
        {
            if (_looseFileCount == null)
            {
                _looseFileCount = CountLooseMaterialsFiles();
            }

            return _looseFileCount.Value;
        }
    }

    /// <summary>Number of files catalogued across this mount's VPK archives.</summary>
    public int VpkFileCount { get; private set; }

    private void DiscoverVpks()
    {
        _vpks.Clear();
        VpkFileCount = 0;
        if (!Directory.Exists(_contentPath))
        {
            return;
        }

        try
        {
            foreach (string dirVpk in Directory.GetFiles(_contentPath, "*_dir.vpk"))
            {
                VpkArchive archive = new VpkArchive(dirVpk);
                _vpks.Add(archive);
                VpkFileCount += archive.EntryCount;
            }
        }
        catch (Exception)
        {
            // A single unreadable folder must not break mounting.
        }
    }

    private int CountLooseMaterialsFiles()
    {
        if (!HasLooseMaterials)
        {
            return 0;
        }

        int count = 0;
        try
        {
            foreach (string _ in SafeEnumerateFiles(MaterialsPath))
            {
                count++;
            }
        }
        catch (Exception)
        {
            return count;
        }

        return count;
    }

    /// <summary>Reads a file by content-relative path (forward slashes, e.g.
    /// "materials/concrete/concretewall074c.vtf"). Loose file first, then each VPK in
    /// discovery order. Returns null when the file is not in this mount.</summary>
    public byte[] TryRead(string contentRelativePath)
    {
        if (string.IsNullOrWhiteSpace(contentRelativePath))
        {
            return null;
        }

        if (Directory.Exists(_contentPath))
        {
            string disk = Path.Combine(_contentPath, contentRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(disk))
            {
                try
                {
                    return File.ReadAllBytes(disk);
                }
                catch (Exception)
                {
                    // Unreadable loose file; fall through to the archives.
                }
            }
        }

        foreach (VpkArchive vpk in _vpks)
        {
            if (vpk.TryRead(contentRelativePath, out byte[] data))
            {
                return data;
            }
        }

        return null;
    }

    /// <summary>Resolves a content-relative path to a real on-disk file path only when this
    /// mount serves it as a LOOSE file. A VPK entry never has a local path — which is exactly
    /// why Hammer's "Open Source" works only for files that exist on disk. Returns false when
    /// only an archive copy exists or the file is absent.</summary>
    public bool TryGetLooseFilePath(string contentRelativePath, out string fullPath)
    {
        fullPath = null;
        if (string.IsNullOrWhiteSpace(contentRelativePath) || !Directory.Exists(_contentPath))
        {
            return false;
        }

        string disk = Path.Combine(_contentPath, contentRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(disk))
        {
            return false;
        }

        fullPath = disk;
        return true;
    }

    /// <summary>All content-relative file keys available in this mount: loose files under the
    /// content folder first, then every VPK's entries. This is the seam a material browser
    /// walks to list what a content folder can serve (filter to "materials/" + ".vmt"/".vtf").</summary>
    public IEnumerable<string> EnumerateFileKeys()
    {
        if (Directory.Exists(_contentPath))
        {
            foreach (string file in SafeEnumerateFiles(_contentPath))
            {
                if (file.Length > _contentPath.Length)
                {
                    yield return file.Substring(_contentPath.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                }
            }
        }

        foreach (VpkArchive vpk in _vpks)
        {
            foreach (string key in vpk.Keys)
            {
                yield return key;
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            string[] files;
            string[] subDirs;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch (Exception)
            {
                files = Array.Empty<string>();
            }

            try
            {
                subDirs = Directory.GetDirectories(dir);
            }
            catch (Exception)
            {
                subDirs = Array.Empty<string>();
            }

            foreach (string file in files)
            {
                yield return file;
            }

            foreach (string subDir in subDirs)
            {
                pending.Push(subDir);
            }
        }
    }

    /// <summary>One-line description for status bars and reports, e.g.
    /// "tf — 12,345 loose + 2 VPKs (131,500 files) — C:\...\Team Fortress 2\tf".</summary>
    public string Describe()
    {
        string body = Name + (HasLooseMaterials
            ? " — " + LooseFileCount.ToString("N0") + " loose file(s)"
            : " — no loose materials");
        if (_vpks.Count > 0)
        {
            body += " + " + _vpks.Count + " VPK(s) (" + VpkFileCount.ToString("N0") + " files)";
        }

        return body + " — " + _contentPath;
    }

    public void Dispose()
    {
        foreach (VpkArchive vpk in _vpks)
        {
            vpk.Dispose();
        }

        _vpks.Clear();
    }
}
